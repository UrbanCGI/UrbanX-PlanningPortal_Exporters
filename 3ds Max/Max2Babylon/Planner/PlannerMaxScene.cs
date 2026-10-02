using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>What applying a plan to the 3ds Max scene did.</summary>
    sealed class PlannerApplyOutcome
    {
        public int Applied;
        /// <summary>One line per operation that did not apply, in plain words.</summary>
        public readonly List<string> Failures = new List<string>();
        /// <summary>Set when the MAXScript part did not run to its end; the helper properties were then not written.</summary>
        public string ScriptError;
        /// <summary>True once the script was handed to 3ds Max: with a <see cref="ScriptError"/>, some layer changes may have been made.</summary>
        public bool ScriptStarted;
        /// <summary>
        /// Set when 3ds Max ran the script but did not hand its report back: the created helpers were then found by
        /// name and the properties written anyway; <see cref="Unconfirmed"/> layer changes are not counted as applied.
        /// </summary>
        public string ResultNote;
        /// <summary>Layer, helper and link changes sent to 3ds Max whose outcome it did not report.</summary>
        public int Unconfirmed;

        public bool Succeeded { get { return ScriptError == null && Failures.Count == 0; } }
    }

    /// <summary>
    /// UrbanCGI fork: the 3ds Max side of the Planner layers. Reads the scene into the abstract
    /// <see cref="PlannerScene"/> the planners work on (layers with their parents; every node with its handle,
    /// layer, link and, for helpers, the planner_* properties) and applies planner operations back to it.
    /// Layer, helper and link changes run as one MAXScript undo record (<see cref="PlannerMaxScript"/>); user
    /// properties go through the exporter's own property helpers. Nothing is ever deleted.
    /// </summary>
    static class PlannerMaxScene
    {
        public const string UndoLabel = "Planner layers";

        /// <summary>Node handle to the name of the layer the node sits on, for the whole scene.</summary>
        public static Dictionary<uint, string> NodeLayerMap()
        {
            var map = new Dictionary<uint, string>();
            var manager = Loader.IIFPLayerManager;
            for (int i = 0; i < manager.Count; i++)
            {
                var layer = manager.GetLayer(i);
                if (layer == null)
                {
                    continue;
                }
                foreach (var node in LayerNodes(layer))
                {
                    if (node != null)
                    {
                        map[node.Handle] = layer.Name;
                    }
                }
            }
            return map;
        }

        private static IEnumerable<IINode> LayerNodes(IILayerProperties layer)
        {
#if MAX2020_OR_NEWER
            ITab<IINode> nodes = Loader.Global.INodeTab.Create();
#else
            ITab<IINode> nodes = Loader.Global.INodeTabNS.Create();
#endif
            layer.Nodes(nodes);
            return Tools.ITabToIEnumerable(nodes);
        }

        /// <summary>The scene as the planners see it. Node ids are node handles in decimal.</summary>
        public static PlannerScene Read()
        {
            var scene = new PlannerScene();
            var layerOf = new Dictionary<uint, string>();
            var manager = Loader.IIFPLayerManager;
            for (int i = 0; i < manager.Count; i++)
            {
                var layer = manager.GetLayer(i);
                if (layer == null)
                {
                    continue;
                }
                var parent = layer.ParentLayerProperties;
                scene.Layers.Add(new PlannerSceneLayer { Name = layer.Name, ParentName = parent != null ? parent.Name : null });
                foreach (var node in LayerNodes(layer))
                {
                    if (node != null)
                    {
                        layerOf[node.Handle] = layer.Name;
                    }
                }
            }

            foreach (var node in Loader.Core.RootNode.NodeTree())
            {
                if (node == null)
                {
                    continue;
                }
                string layerName;
                layerOf.TryGetValue(node.Handle, out layerName);
                var parent = node.ParentNode;
                var isHelper = IsHelper(node);
                scene.Nodes.Add(new PlannerSceneNode
                {
                    Id = IdOf(node),
                    Name = node.Name,
                    LayerName = layerName,
                    ParentId = parent != null && !parent.IsRootNode ? IdOf(parent) : null,
                    IsHelper = isHelper,
                    IsGroupHead = isHelper && IsGroupOrContainer(node),
                    Props = isHelper ? node.GetPlannerProps() : new Dictionary<string, string>(StringComparer.Ordinal)
                });
            }
            return scene;
        }

        public static string IdOf(IINode node)
        {
            return node.Handle.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The node a scene id names, or null (an id the scene no longer has, or a "new:" id).</summary>
        public static IINode NodeById(string id)
        {
            uint handle;
            if (id == null || !uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out handle))
            {
                return null;
            }
            return Loader.Core.GetINodeByHandle(handle);
        }

        /// <summary>
        /// A helper or dummy (Point, Dummy, group head): judged by the base object's superclass, so no modifier
        /// stack is evaluated.
        /// </summary>
        private static bool IsHelper(IINode node)
        {
            try
            {
                var objectRef = node.ObjectRef;
                if (objectRef == null)
                {
                    return false;
                }
                var baseObject = objectRef.FindBaseObject();
                return baseObject != null && baseObject.SuperClassID == SClass_ID.Helper;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// A group head or a container: a helper 3ds Max keeps for its members, which must never stand for a layer
        /// (ungrouping deletes a group head, and its planner_* properties with it).
        /// </summary>
        private static bool IsGroupOrContainer(IINode node)
        {
            try
            {
                return node.IsGroupHead || Loader.Global.ContainerManagerInterface.IsContainerNode(node) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Applies the operations to the scene: layers, helpers and links through one MAXScript undo record, then
        /// the planner_* user properties (created helpers are found by the handles the script reports back, or,
        /// when 3ds Max does not hand the report back, by name on their layer in the scene as it now stands).
        /// </summary>
        public static PlannerApplyOutcome Apply(IList<PlannerSceneOp> operations)
        {
            var outcome = new PlannerApplyOutcome();
            if (operations == null || operations.Count == 0)
            {
                return outcome;
            }

            PlannerMaxScriptBatch batch;
            try
            {
                batch = PlannerMaxScript.Build(operations, UndoLabel);
            }
            catch (Exception e)
            {
                outcome.ScriptError = "The changes could not be prepared: " + e.Message;
                return outcome;
            }

            var created = new Dictionary<string, uint>(StringComparer.Ordinal);
            if (batch.Script != null)
            {
                string output;
                outcome.ScriptStarted = true;
                try
                {
                    output = ScriptsUtilities.ExecuteMaxScriptQuery(batch.Script);
                }
                catch (Exception e)
                {
                    outcome.ScriptError = "3ds Max could not run the layer changes: " + e.Message;
                    return outcome;
                }
                var result = PlannerMaxScript.ParseResult(output);
                if (result.Completed)
                {
                    foreach (var index in batch.ScriptedOperations)
                    {
                        string message;
                        if (result.Failures.TryGetValue(index, out message))
                        {
                            outcome.Failures.Add(operations[index].Description + ": " + message);
                        }
                        else
                        {
                            outcome.Applied++;
                        }
                    }
                    foreach (var slot in batch.NewHelperSlots)
                    {
                        uint handle;
                        if (result.CreatedHandles.TryGetValue(slot.Value, out handle))
                        {
                            created[slot.Key] = handle;
                        }
                    }
                }
                else
                {
                    // No report came back. That can mean the script ran but its value was not handed over, so the
                    // properties do not depend on it: the created helpers are looked up by name on their layers.
                    // Only when helpers were to be created and none is there did the script clearly not run.
                    var text = (output ?? string.Empty).Trim();
                    var resolved = PlannerMaxScript.ResolveCreatedHelpers(Read(), operations);
                    if (batch.NewHelperSlots.Count > 0 && resolved.Count == 0)
                    {
                        outcome.ScriptError = "3ds Max did not run the layer changes" + (text.Length > 0 ? ": " + Shorten(text, 400) : ".");
                        return outcome;
                    }
                    foreach (var entry in resolved)
                    {
                        uint handle;
                        if (uint.TryParse(entry.Value, NumberStyles.None, CultureInfo.InvariantCulture, out handle))
                        {
                            created[entry.Key] = handle;
                        }
                    }
                    outcome.Unconfirmed = batch.ScriptedOperations.Count;
                    outcome.ResultNote = "3ds Max did not report back on the layer changes"
                                         + (text.Length > 0 ? " (it returned: " + Shorten(text, 200) + ")" : string.Empty)
                                         + (batch.NewHelperSlots.Count > 0
                                             ? string.Format(CultureInfo.InvariantCulture, ". {0} of {1} new helper(s) were found by name, and the helper properties were written.", resolved.Count, batch.NewHelperSlots.Count)
                                             : ". The helper properties were written.");
                }
            }

            foreach (var index in batch.PropertyOperations)
            {
                var op = operations[index];
                IINode node;
                uint handle;
                if (PlannerMaxScript.IsNewId(op.NodeId))
                {
                    node = created.TryGetValue(op.NodeId, out handle) ? Loader.Core.GetINodeByHandle(handle) : null;
                }
                else
                {
                    node = NodeById(op.NodeId);
                }
                if (node == null)
                {
                    outcome.Failures.Add(op.Description + ": the node is not in the scene.");
                    continue;
                }
                try
                {
                    node.SetPlannerProp(op.PropName, op.Kind == PlannerSceneOpKind.SetUserProp ? op.PropValue : null);
                    outcome.Applied++;
                }
                catch (Exception e)
                {
                    outcome.Failures.Add(op.Description + ": " + e.Message);
                }
            }

            Loader.Global.SetSaveRequiredFlag(true, false);
            return outcome;
        }

        /// <summary>Replaces the 3ds Max selection with the nodes the ids name; returns how many were selected.</summary>
        public static int Select(IEnumerable<string> ids)
        {
#if MAX2020_OR_NEWER
            var tab = Loader.Global.INodeTab.Create();
#else
            var tab = Loader.Global.INodeTabNS.Create();
#endif
            int count = 0;
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                var node = NodeById(id);
                if (node != null)
                {
                    tab.AppendNode(node, false, 0);
                    count++;
                }
            }
            Loader.Core.ClearNodeSelection(count == 0);
            if (count > 0)
            {
                Loader.Core.SelectNodeTab(tab, true, true);
            }
            return count;
        }

        private static string Shorten(string text, int length)
        {
            return text.Length <= length ? text : text.Substring(0, length) + "...";
        }
    }
}
