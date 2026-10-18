using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the 3ds Max side of the Schedule tab. Reads the scene into the layer builder's model (every node with
    /// its handle, parent, layer, helper and shape flags, planner_id, hidden and Do not export; every Max layer with its
    /// parent; the scene's Export hidden objects), keeps the last imported layer list, the layers added in Max and the
    /// Planner workspace's project start with the scene, selects nodes, runs a link, IN / RM rename, Add layer or Clean up
    /// scene graph plan (with the Planner's list, the layers and helpers too) as one MAXScript batch, and saves a copy of the scene before a clean-up. Every decision is made by
    /// <see cref="PlannerLayerPlanner"/>, <see cref="PlannerLinkPlanner"/>, <see cref="PlannerSchedule"/>, <see cref="PlannerTagEdit"/>,
    /// <see cref="PlannerAddLayer"/> and <see cref="PlannerCleanUp"/> and every script by <see cref="PlannerLayerScript"/>,
    /// <see cref="PlannerLinkScript"/>, <see cref="PlannerTagEdit"/> and <see cref="PlannerCleanUpScript"/> (shared, tested);
    /// this file only reads and runs.
    /// </summary>
    internal static class PlannerScheduleScene
    {
        // Root node app data under Loader.Class_ID: sub-ids 0 and 1 hold the exporter's node GUID and instance flag,
        // Fix phasing names' word fixes take 0x50484658 ("PHFX"); the layer list takes its own ("PLLS").
        private const int LayerListSubId = 0x504C4C53;
        // The layers added in Max with Add layer, until the Planner's list has them ("PLAD").
        private const int AddedLayersSubId = 0x504C4144;
        // The Planner workspace's project start, when the modeller gives it ("PLPS").
        private const int ProjectStartSubId = 0x504C5053;

        /// <summary>The scene as the layer builder sees it.</summary>
        public static PlannerSceneSnapshot Read()
        {
            return Read(null);
        }

        /// <summary>
        /// The scene as the layer builder and the schedule see it. <paramref name="hiddenByPreview"/>: the nodes the date
        /// scrubber's preview has hidden, read as they were before it (showing).
        /// </summary>
        public static PlannerSceneSnapshot Read(ICollection<long> hiddenByPreview)
        {
            var snapshot = new PlannerSceneSnapshot();
            try
            {
                snapshot.ExportHidden = Loader.Core.RootNode.GetBoolProperty("babylonjs_exporthidden");
            }
            catch (Exception)
            {
                // Read as off, the exporter's default.
            }
            var layerOf = new Dictionary<uint, string>();
            var manager = Loader.Core.LayerManager;
            for (int i = 0; i < manager.LayerCount; i++)
            {
                var layer = manager.GetLayer(i);
                if (layer == null)
                {
                    continue;
                }
                string parentName = null;
                try
                {
                    var parentLayer = layer.ParentLayer;
                    parentName = parentLayer != null ? parentLayer.Name : null;
                }
                catch (Exception)
                {
                    // Read as a top-level layer; a move under its parent may then be planned again.
                }
                snapshot.Layers.Add(new PhasingMaxLayer { Name = layer.Name, ParentName = parentName });
                try
                {
                    foreach (var node in layer.LayerNodes())
                    {
                        if (node != null)
                        {
                            layerOf[node.Handle] = layer.Name;
                        }
                    }
                }
                catch (Exception)
                {
                    // The layer's nodes are read as on no known layer.
                }
            }

            var root = Loader.Core.RootNode;
            var seen = new HashSet<uint>();
            var pending = new Queue<IINode>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                long? parentKey = parent.IsRootNode ? (long?)null : parent.Handle;
                for (int i = 0; i < parent.NumberOfChildren; i++)
                {
                    var child = parent.GetChildNode(i);
                    if (child == null || !seen.Add(child.Handle))
                    {
                        continue;
                    }
                    bool isHelper, isShape;
                    PhasingScene.Classify(child, out isHelper, out isShape);
                    string layerName;
                    layerOf.TryGetValue(child.Handle, out layerName);
                    snapshot.Nodes.Add(new PlannerSceneNode
                    {
                        Key = child.Handle,
                        Name = child.Name,
                        ParentKey = parentKey,
                        ChildIndex = i,
                        HasChildren = child.NumberOfChildren > 0,
                        IsGroupHead = child.IsGroupHead,
                        IsGroupMember = child.IsGroupMember,
                        IsHelper = isHelper,
                        IsShape = isShape,
                        LayerName = layerName,
                        PlannerId = PlannerIdOf(child),
                        Hidden = (hiddenByPreview == null || !hiddenByPreview.Contains(child.Handle)) && IsHiddenForExport(child),
                        NoExport = NoExportOf(child)
                    });
                    pending.Enqueue(child);
                }
            }
            return snapshot;
        }

        // Hidden as the export reads it (BabylonExporter.IsNodeExportable): the node, its layer or its category.
        private static bool IsHiddenForExport(IINode node)
        {
            try
            {
                return node.IsHidden(NodeHideFlags.None, false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool NoExportOf(IINode node)
        {
            try
            {
                return node.GetBoolProperty("babylonjs_noexport");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string PlannerIdOf(IINode node)
        {
            try
            {
                var id = node.GetStringProperty(PlannerSceneModel.PlannerIdProperty, null);
                return string.IsNullOrEmpty(id) ? null : id.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The list kept with the scene

        /// <summary>The layer list saved with the scene: its text, file and import time; false when the scene has none.</summary>
        public static bool TryLoadList(out string csvText, out string fileName, out DateTime? importedUtc)
        {
            csvText = null;
            fileName = null;
            importedUtc = null;
            EnsureLoaded();
            var chunk = Loader.Core.RootNode.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, LayerListSubId);
            if (chunk == null || chunk.Data == null)
            {
                return false;
            }
            try
            {
                return PlannerLayerListStore.TryRead(Encoding.UTF8.GetString(chunk.Data), out fileName, out importedUtc, out csvText);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SaveList(string csvText, string fileName, DateTime importedUtc)
        {
            EnsureLoaded();
            var root = Loader.Core.RootNode;
            if (root.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, LayerListSubId) != null)
            {
                root.RemoveAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, LayerListSubId);
            }
            root.AddAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, LayerListSubId,
                Encoding.UTF8.GetBytes(PlannerLayerListStore.Write(fileName, importedUtc, csvText)));
            Loader.Global.SetSaveRequiredFlag(true, false);
        }

        /// <summary>The layers added in Max kept with the scene (empty when there are none).</summary>
        public static List<PlannerAddedLayer> LoadAdded()
        {
            EnsureLoaded();
            var chunk = Loader.Core.RootNode.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, AddedLayersSubId);
            if (chunk == null || chunk.Data == null)
            {
                return new List<PlannerAddedLayer>();
            }
            try
            {
                return PlannerAddedLayerStore.Read(Encoding.UTF8.GetString(chunk.Data));
            }
            catch (Exception)
            {
                return new List<PlannerAddedLayer>();
            }
        }

        public static void SaveAdded(IEnumerable<PlannerAddedLayer> added)
        {
            EnsureLoaded();
            var root = Loader.Core.RootNode;
            if (root.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, AddedLayersSubId) != null)
            {
                root.RemoveAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, AddedLayersSubId);
            }
            var list = new List<PlannerAddedLayer>(added ?? new List<PlannerAddedLayer>());
            if (list.Count > 0)
            {
                root.AddAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, AddedLayersSubId, Encoding.UTF8.GetBytes(PlannerAddedLayerStore.Write(list)));
            }
            Loader.Global.SetSaveRequiredFlag(true, false);
        }

        /// <summary>The Planner workspace's project start kept with the scene, or null when none was given.</summary>
        public static DateTime? LoadProjectStart()
        {
            EnsureLoaded();
            var chunk = Loader.Core.RootNode.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, ProjectStartSubId);
            if (chunk == null || chunk.Data == null)
            {
                return null;
            }
            try
            {
                DateTime day;
                return DateTime.TryParseExact(Encoding.UTF8.GetString(chunk.Data).Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out day) ? day.Date : (DateTime?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Keeps the Planner workspace's project start with the scene (null: none given; the Schedule tab's preview starts today).</summary>
        public static void SaveProjectStart(DateTime? start)
        {
            EnsureLoaded();
            var root = Loader.Core.RootNode;
            if (root.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, ProjectStartSubId) != null)
            {
                root.RemoveAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, ProjectStartSubId);
            }
            if (start.HasValue)
            {
                root.AddAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, ProjectStartSubId,
                    Encoding.UTF8.GetBytes(start.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            }
            Loader.Global.SetSaveRequiredFlag(true, false);
        }

        // Selection

        /// <summary>The handles of the nodes selected in 3ds Max.</summary>
        public static List<long> SelectedKeys()
        {
            var keys = new List<long>();
            for (int i = 0; i < Loader.Core.SelNodeCount; i++)
            {
                var node = Loader.Core.GetSelNode(i);
                if (node != null)
                {
                    keys.Add(node.Handle);
                }
            }
            return keys;
        }

        /// <summary>Selects the nodes (by handle) in 3ds Max, instead of what was selected; returns how many were found.</summary>
        public static int Select(IEnumerable<long> keys)
        {
#if MAX2020_OR_NEWER
            var tab = Loader.Global.INodeTab.Create();
#else
            var tab = Loader.Global.NodeTab.Create();
#endif
            int found = 0;
            foreach (var key in keys)
            {
                if (key <= 0 || key > uint.MaxValue)
                {
                    continue;
                }
                var node = Loader.Core.GetINodeByHandle((uint)key);
                if (node != null)
                {
                    tab.AppendNode(node, false, 0);
                    found++;
                }
            }
            Loader.Core.ClearNodeSelection(false);
            if (found > 0)
            {
                Loader.Core.SelectNodeTab(tab, true, false);
            }
            Loader.Core.RedrawViews(Loader.Core.Time, RedrawFlags.Normal, null);
            return found;
        }

        // Running the batches

        /// <summary>
        /// Renames the objects of an IN / RM edit in one MAXScript batch (one undo record): renamed, failures, whether it
        /// ran to its end, and the error that stopped it.
        /// </summary>
        public static void ApplyRenames(PlannerTagEditPlan plan, out int renamed, out List<string> failures, out bool completed, out string aborted)
        {
            List<string> refused;
            List<PlannerTagRename> sent;
            var script = PlannerTagEdit.Script(plan, out refused, out sent);
            string error;
            var output = Run(script, "IN / RM", out error);
            PlannerTagEdit.ReadResult(sent, output, out renamed, out failures, out completed, out aborted);
            failures.InsertRange(0, refused);
            if (error != null && aborted == null)
            {
                aborted = error;
            }
            if (!completed && !string.IsNullOrEmpty(output) && aborted == null)
            {
                aborted = PhasingFixScript.OneLine(output, 300);
            }
        }

        /// <summary>Makes an added layer and its helper with the layer builder's batch (<see cref="PlannerLayerScript"/>, one undo record).</summary>
        public static PlannerLayerResult ApplyAddLayer(PlannerAddLayerPlan plan)
        {
            return ApplyBuild(plan.Build);
        }

        /// <summary>
        /// Carries out a layer builder's plan (Add layer's) in one MAXScript batch (one undo record). The caller holds the
        /// scene first if it wants Edit > Fetch to restore it, and plans again afterwards to see what is left. The Planner's list
        /// is built by Clean up scene graph's batch (<see cref="ApplyCleanUp"/>).
        /// </summary>
        public static PlannerLayerResult ApplyBuild(PlannerLayerPlan plan)
        {
            var batch = PlannerLayerScript.Build(plan, "__planner_build_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_");
            string error;
            var output = Run(batch.Script, "Add layer", out error);
            var result = PlannerLayerScript.ReadResult(batch, output);
            if (error != null && result.Aborted == null)
            {
                result.Aborted = error;
            }
            if (!result.Completed && !string.IsNullOrEmpty(output) && result.Aborted == null)
            {
                result.Aborted = PhasingFixScript.OneLine(output, 300);
            }
            return result;
        }

        /// <summary>Carries out a link plan in one MAXScript batch (one undo record).</summary>
        public static PlannerLinkResult ApplyLinks(PlannerLinkPlan plan)
        {
            var batch = PlannerLinkScript.Build(plan);
            string error;
            var output = Run(batch.Script, "Link objects to their helpers", out error);
            var result = PlannerLinkScript.ReadResult(batch, output);
            if (error != null && result.Aborted == null)
            {
                result.Aborted = error;
            }
            if (!result.Completed && !string.IsNullOrEmpty(output) && result.Aborted == null)
            {
                result.Aborted = PhasingFixScript.OneLine(output, 300);
            }
            return result;
        }

        /// <summary>
        /// Carries out Clean up scene graph's operations (<paramref name="result"/>, which <paramref name="plan"/> resolved) in
        /// one MAXScript batch, one undo record. The caller saves a copy and holds the scene first when the modeller asks.
        /// </summary>
        public static PlannerCleanUpOutcome ApplyCleanUp(PlannerCleanUpPlan plan, PlannerCleanUpResult result)
        {
            var batch = PlannerCleanUpScript.Build(plan, result, "__planner_cleanup_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_");
            string error;
            var output = Run(batch.Script, PlannerCleanUp.UndoLabel, out error);
            var outcome = PlannerCleanUpScript.ReadResult(batch, output);
            if (error != null && outcome.Aborted == null)
            {
                outcome.Aborted = error;
            }
            if (!outcome.Completed && !string.IsNullOrEmpty(output) && outcome.Aborted == null)
            {
                outcome.Aborted = PhasingFixScript.OneLine(output, 300);
            }
            return outcome;
        }

        /// <summary>
        /// Saves the scene as it is now to <paramref name="path"/> (saveMaxFile with useNewFile off), so the open scene keeps its
        /// own file name and its unsaved changes stay marked: null when saved, else why not.
        /// </summary>
        public static string SaveCopy(string path)
        {
            var literal = PhasingFixScript.Quote(path);
            if (string.IsNullOrEmpty(path) || literal == null)
            {
                return "the file name holds a character that cannot be passed to MAXScript";
            }
            string output;
            try
            {
                var script = "(saveMaxFile " + literal + " clearNeedSaveFlag:false useNewFile:false quiet:true) as string";
#if MAX2022_OR_NEWER
                output = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(script, ManagedServices.MaxscriptSDK.ScriptSource.NotSpecified);
#else
                output = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(script);
#endif
            }
            catch (Exception e)
            {
                return e.Message;
            }
            var answer = (output ?? string.Empty).Trim().Trim('"');
            if (!string.Equals(answer, "true", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            {
                return "3ds Max did not save it" + (string.IsNullOrEmpty(answer) ? string.Empty : " (it answered " + PhasingFixScript.OneLine(answer, 200) + ")");
            }
            return null;
        }

        private static string Run(string script, string what, out string error)
        {
            string output = null;
            error = null;
            try
            {
#if MAX2022_OR_NEWER
                output = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(script, ManagedServices.MaxscriptSDK.ScriptSource.NotSpecified);
#else
                output = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(script);
#endif
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            if (!string.IsNullOrEmpty(output) && output.IndexOf("-END", StringComparison.Ordinal) < 0)
            {
                // Something came back without the end marker, e.g. a compile error returned as text: keep all of it in
                // the MAXScript Listener.
                try
                {
                    Loader.Global.TheListener.EditStream.Puts(what + ": 3ds Max sent back\n" + output.Replace("\r\n", "\n") + "\n");
                }
                catch (Exception)
                {
                    // The message box still shows the start of it.
                }
            }
            return output;
        }

        private static void EnsureLoaded()
        {
            if (Loader.Class_ID == null)
            {
                Loader.AssemblyMain();
            }
        }
    }
}
