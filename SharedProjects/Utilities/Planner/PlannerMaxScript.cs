using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>The MAXScript for a plan's layer, helper and link operations, and what the caller still has to do.</summary>
    public sealed class PlannerMaxScriptBatch
    {
        /// <summary>The script, or null when the plan has no layer, helper or link operations.</summary>
        public string Script;
        /// <summary>Indexes (into the plan's operations) of the operations the script carries, in order.</summary>
        public readonly List<int> ScriptedOperations = new List<int>();
        /// <summary>Indexes of the user-property operations, which the caller applies through the SDK after the script.</summary>
        public readonly List<int> PropertyOperations = new List<int>();
        /// <summary>A created helper's plan id ("new:&lt;n&gt;") to its 1-based slot in the script's list of created helpers.</summary>
        public readonly Dictionary<string, int> NewHelperSlots = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>What the script reported back.</summary>
    public sealed class PlannerMaxScriptResult
    {
        /// <summary>True when the script ran to its end (its marker came back).</summary>
        public bool Completed;
        /// <summary>Slot of a created helper to its node handle.</summary>
        public readonly Dictionary<int, uint> CreatedHandles = new Dictionary<int, uint>();
        /// <summary>Index of a failed operation (into the plan's operations) to 3ds Max's message.</summary>
        public readonly Dictionary<int, string> Failures = new Dictionary<int, string>();
    }

    /// <summary>
    /// Turns planner operations into MAXScript, the documented and stable way to create, rename and nest layers,
    /// create Point helpers, put nodes on layers and link them (setting <c>.parent</c> keeps a node where it is
    /// in the world). Everything runs in one <c>undo</c> record; each operation has its own try/catch, so one
    /// failure is reported against its operation and the rest still run. Nodes are addressed by handle
    /// (<c>maxOps.getNodeByHandle</c>), layers by name, and every name goes in as an escaped string literal.
    /// User properties are not scripted: the caller writes them through the exporter's own property helpers,
    /// which own the encoding (<see cref="PlannerMaxScriptBatch.PropertyOperations"/>). Pure string work, so it is unit-tested.
    /// </summary>
    public static class PlannerMaxScript
    {
        public const string DoneMarker = "PLANNER-LAYERS-DONE";
        public const string NewIdPrefix = "new:";

        /// <summary>A MAXScript string literal for <paramref name="text"/>: backslash, quote and control characters escaped.</summary>
        public static string Quote(string text)
        {
            var builder = new StringBuilder((text ?? string.Empty).Length + 2);
            builder.Append('"');
            foreach (var c in text ?? string.Empty)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append(' ');
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        public static bool IsNewId(string id)
        {
            return id != null && id.StartsWith(NewIdPrefix, StringComparison.Ordinal);
        }

        public static bool IsPropertyOperation(PlannerSceneOp op)
        {
            return op.Kind == PlannerSceneOpKind.SetUserProp || op.Kind == PlannerSceneOpKind.ClearUserProp;
        }

        /// <summary>
        /// The script for <paramref name="operations"/>. Existing nodes must be addressed by their node handle
        /// (decimal), helpers the plan creates by "new:&lt;n&gt;". Throws when an id is neither.
        /// </summary>
        public static PlannerMaxScriptBatch Build(IList<PlannerSceneOp> operations, string undoLabel)
        {
            var batch = new PlannerMaxScriptBatch();
            var body = new StringBuilder();
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                if (IsPropertyOperation(op))
                {
                    batch.PropertyOperations.Add(i);
                    continue;
                }
                var statement = Statement(op, batch);
                body.Append("\t\ttry (").Append(statement).Append(") catch (plannerFail plannerOut ")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append(" (getCurrentException()))\r\n");
                batch.ScriptedOperations.Add(i);
            }
            if (batch.ScriptedOperations.Count == 0)
            {
                return batch;
            }

            var script = new StringBuilder();
            script.Append("(\r\n");
            script.Append("\tlocal plannerMade = #()\r\n");
            script.Append("\tlocal plannerOut = stringStream \"\"\r\n");
            script.Append("\tfn plannerLayer n = (local l = LayerManager.getLayerFromName n; if l == undefined do throw (\"There is no layer \" + n); l)\r\n");
            script.Append("\tfn plannerNode h = (local o = maxOps.getNodeByHandle h; if not (isValidNode o) do throw (\"There is no node with handle \" + (h as string)); o)\r\n");
            script.Append("\tfn plannerNew arr k = (local o = if k <= arr.count then arr[k] else undefined; if not (isValidNode o) do throw (\"The helper was not created\"); o)\r\n");
            script.Append("\tfn plannerCreateLayer n parentName = (local l = LayerManager.newLayerFromName n; if l == undefined do throw \"3ds Max did not create the layer (is the name taken?)\"; if parentName != undefined do l.setParent (plannerLayer parentName); l)\r\n");
            // The planners never ask for a rename that only changes case, so the name must come back exactly.
            script.Append("\tfn plannerRenameLayer oldName newName = (local l = plannerLayer oldName; l.setName newName; if l.name != newName do throw \"3ds Max refused the new layer name (is it taken?)\"; l)\r\n");
            script.Append("\tfn plannerSetParent n parentName = (local l = plannerLayer n; if parentName == undefined then l.setParent undefined else l.setParent (plannerLayer parentName); l)\r\n");
            // The layer first: when it is missing, no stray Point is left on the current layer.
            script.Append("\tfn plannerPoint arr k n layerName = (local l = plannerLayer layerName; local p = Point name:n; l.addNode p; arr[k] = p; p)\r\n");
            script.Append("\tfn plannerName o n = (o.name = n; o)\r\n");
            script.Append("\tfn plannerMoveTo o layerName = ((plannerLayer layerName).addNode o; o)\r\n");
            script.Append("\tfn plannerLink o p = (o.parent = p; o)\r\n");
            script.Append("\tfn plannerFail ss i msg = (\r\n");
            script.Append("\t\tlocal m = (msg as string)\r\n");
            script.Append("\t\tm = substituteString m \"\\r\" \" \"\r\n");
            script.Append("\t\tm = substituteString m \"\\n\" \" \"\r\n");
            script.Append("\t\tm = substituteString m \"\\t\" \" \"\r\n");
            script.Append("\t\tformat \"F\\t%\\t%\\n\" i m to:ss\r\n");
            script.Append("\t)\r\n");
            script.Append("\tundo ").Append(Quote(string.IsNullOrEmpty(undoLabel) ? "Planner layers" : undoLabel)).Append(" on\r\n");
            script.Append("\t(\r\n");
            script.Append(body);
            script.Append("\t)\r\n");
            script.Append("\tfor k = 1 to plannerMade.count where isValidNode plannerMade[k] do format \"N\\t%\\t%\\n\" k plannerMade[k].inode.handle to:plannerOut\r\n");
            script.Append("\tredrawViews()\r\n");
            script.Append("\t(").Append(Quote(DoneMarker + "\n")).Append(" + (plannerOut as string))\r\n");
            script.Append(")\r\n");
            batch.Script = script.ToString();
            return batch;
        }

        private static string Statement(PlannerSceneOp op, PlannerMaxScriptBatch batch)
        {
            switch (op.Kind)
            {
                case PlannerSceneOpKind.CreateLayer:
                    return "plannerCreateLayer " + Quote(op.NewName) + " " + LayerOrUndefined(op.ParentLayerName);
                case PlannerSceneOpKind.RenameLayer:
                    return "plannerRenameLayer " + Quote(op.LayerName) + " " + Quote(op.NewName);
                case PlannerSceneOpKind.SetLayerParent:
                    return "plannerSetParent " + Quote(op.LayerName) + " " + LayerOrUndefined(op.ParentLayerName);
                case PlannerSceneOpKind.CreateHelper:
                    {
                        if (!IsNewId(op.NodeId) || batch.NewHelperSlots.ContainsKey(op.NodeId))
                        {
                            throw new InvalidOperationException("A created helper needs a new, unused id: " + op.NodeId);
                        }
                        var slot = batch.NewHelperSlots.Count + 1;
                        batch.NewHelperSlots[op.NodeId] = slot;
                        return "plannerPoint plannerMade " + slot.ToString(CultureInfo.InvariantCulture) + " " + Quote(op.NewName) + " " + Quote(op.LayerName);
                    }
                case PlannerSceneOpKind.RenameHelper:
                    return "plannerName " + NodeRef(op.NodeId, batch) + " " + Quote(op.NewName);
                case PlannerSceneOpKind.MoveNodeToLayer:
                    return "plannerMoveTo " + NodeRef(op.NodeId, batch) + " " + Quote(op.LayerName);
                case PlannerSceneOpKind.LinkNode:
                    return "plannerLink " + NodeRef(op.NodeId, batch) + " " + (op.ParentNodeId != null ? NodeRef(op.ParentNodeId, batch) : "undefined");
                default:
                    throw new InvalidOperationException("Not a scripted operation: " + op.Kind);
            }
        }

        private static string LayerOrUndefined(string layerName)
        {
            return string.IsNullOrEmpty(layerName) ? "undefined" : Quote(layerName);
        }

        private static string NodeRef(string id, PlannerMaxScriptBatch batch)
        {
            int slot;
            if (IsNewId(id))
            {
                if (!batch.NewHelperSlots.TryGetValue(id, out slot))
                {
                    throw new InvalidOperationException("The helper " + id + " is used before it is created");
                }
                return "(plannerNew plannerMade " + slot.ToString(CultureInfo.InvariantCulture) + ")";
            }
            uint handle;
            if (!uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out handle))
            {
                throw new InvalidOperationException("Not a 3ds Max node handle: " + id);
            }
            return "(plannerNode " + handle.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// Where each helper the operations create ends up: its name and layer once every operation has run
        /// (renames of the helper or its layer, and moves to another layer, followed in order). Keyed by the "new:"
        /// id. Used when 3ds Max does not report the created handles back.
        /// </summary>
        public static Dictionary<string, KeyValuePair<string, string>> CreatedHelperPlaces(IList<PlannerSceneOp> operations)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var layers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var op in operations ?? new List<PlannerSceneOp>())
            {
                switch (op.Kind)
                {
                    case PlannerSceneOpKind.CreateHelper:
                        if (IsNewId(op.NodeId))
                        {
                            names[op.NodeId] = op.NewName;
                            layers[op.NodeId] = op.LayerName;
                        }
                        break;
                    case PlannerSceneOpKind.RenameHelper:
                        if (names.ContainsKey(op.NodeId))
                        {
                            names[op.NodeId] = op.NewName;
                        }
                        break;
                    case PlannerSceneOpKind.MoveNodeToLayer:
                        if (layers.ContainsKey(op.NodeId))
                        {
                            layers[op.NodeId] = op.LayerName;
                        }
                        break;
                    case PlannerSceneOpKind.RenameLayer:
                        foreach (var id in layers.Keys.ToList())
                        {
                            if (PlannerScene.LayerNameComparer.Equals(layers[id], op.LayerName))
                            {
                                layers[id] = op.NewName;
                            }
                        }
                        break;
                }
            }
            var places = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            foreach (var entry in names)
            {
                places[entry.Key] = new KeyValuePair<string, string>(entry.Value, layers[entry.Key]);
            }
            return places;
        }

        /// <summary>
        /// The fallback when 3ds Max ran the script but did not hand its report back: each created helper is found in
        /// the scene as read afterwards by its exact name on its layer (a helper is named like its layer, so the name
        /// is unique there). Among several, the newest node (highest handle) without planner_* properties wins.
        /// Returns the "new:" id to the node's scene id; helpers that cannot be found are left out.
        /// </summary>
        public static Dictionary<string, string> ResolveCreatedHelpers(PlannerScene scene, IList<PlannerSceneOp> operations)
        {
            var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
            if (scene == null)
            {
                return resolved;
            }
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var place in CreatedHelperPlaces(operations))
            {
                var candidates = scene.Nodes
                    .Where(n => n.CanStandForLayer
                                && !IsNewId(n.Id)
                                && !taken.Contains(n.Id)
                                && string.Equals(n.Name, place.Value.Key, StringComparison.Ordinal)
                                && PlannerScene.LayerNameComparer.Equals(n.LayerName, place.Value.Value))
                    .OrderBy(n => PlannerProps.MarksHelper(n.Props) ? 1 : 0)
                    .ThenByDescending(n => HandleOf(n.Id))
                    .ToList();
                if (candidates.Count > 0)
                {
                    resolved[place.Key] = candidates[0].Id;
                    taken.Add(candidates[0].Id);
                }
            }
            return resolved;
        }

        private static ulong HandleOf(string id)
        {
            uint handle;
            return uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out handle) ? handle : 0;
        }

        /// <summary>
        /// Reads what the script returned: the marker, then one line per created helper ("N", slot, handle) and per
        /// failed operation ("F", index, message), tab separated. Tolerates the value coming back in its printed
        /// form (quoted, with escapes).
        /// </summary>
        public static PlannerMaxScriptResult ParseResult(string output)
        {
            var result = new PlannerMaxScriptResult();
            if (string.IsNullOrEmpty(output))
            {
                return result;
            }
            var at = output.IndexOf(DoneMarker, StringComparison.Ordinal);
            if (at < 0)
            {
                return result;
            }
            result.Completed = true;
            var text = output.Substring(at + DoneMarker.Length);
            if (text.IndexOf('\n') < 0 && text.Contains("\\n"))
            {
                text = text.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r', '"');
                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length < 3)
                {
                    continue;
                }
                int index;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                {
                    continue;
                }
                if (parts[0] == "N")
                {
                    uint handle;
                    if (uint.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out handle))
                    {
                        result.CreatedHandles[index] = handle;
                    }
                }
                else if (parts[0] == "F")
                {
                    var message = parts[2].Trim();
                    const string prefix = "-- Runtime error:";
                    if (message.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        message = message.Substring(prefix.Length).Trim();
                    }
                    result.Failures[index] = message;
                }
            }
            return result;
        }
    }
}
