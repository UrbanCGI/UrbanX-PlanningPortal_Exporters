using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the 3ds Max side of the phasing name fixer. Reads the scene into the fixer's model, keeps the
    /// project's word fixes with the scene, and runs a plan as one MAXScript batch. Every decision is made by
    /// <see cref="PhasingNameFixer"/> and the script by <see cref="PhasingFixScript"/> (shared, tested); this file
    /// only reads and runs.
    /// </summary>
    internal static class PhasingScene
    {
        // Root node app data under Loader.Class_ID: sub-ids 0 and 1 hold the exporter's node GUID and instance
        // flag, so the word fixes take one of their own ("PHFX").
        private const int WordFixesSubId = 0x50484658;

        /// <summary>Every node of the scene (handle, name, parent, place among its siblings, group flags) and every Max layer.</summary>
        public static void Read(List<PhasingSceneNode> nodes, List<PhasingMaxLayer> maxLayers)
        {
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
                    nodes.Add(new PhasingSceneNode
                    {
                        Key = child.Handle,
                        Name = child.Name,
                        ParentKey = parentKey,
                        ChildIndex = i,
                        HasChildren = child.NumberOfChildren > 0,
                        IsGroupHead = child.IsGroupHead,
                        IsGroupMember = child.IsGroupMember
                    });
                    pending.Enqueue(child);
                }
            }

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
                    // Without its parent the layer is still renamed; only a re-parent could be planned twice.
                }
                maxLayers.Add(new PhasingMaxLayer { Name = layer.Name, ParentName = parentName });
            }
        }

        /// <summary>Reads the scene and plans the fix.</summary>
        public static PhasingFixPlan Plan(IEnumerable<PhasingWordFix> wordFixes, IDictionary<long, int> stageOverrides)
        {
            return Plan(wordFixes, stageOverrides, null);
        }

        /// <summary>
        /// Reads the scene and plans the fix; the Max layer changes that earlier fixes with problems
        /// (<paramref name="unfinished"/>) left undone are added as Check rows.
        /// </summary>
        public static PhasingFixPlan Plan(IEnumerable<PhasingWordFix> wordFixes, IDictionary<long, int> stageOverrides, IEnumerable<PhasingFixResult> unfinished)
        {
            var nodes = new List<PhasingSceneNode>();
            var maxLayers = new List<PhasingMaxLayer>();
            Read(nodes, maxLayers);
            var plan = PhasingNameFixer.Plan(nodes, maxLayers, wordFixes, stageOverrides);
            if (plan.TopKey != null && unfinished != null)
            {
                var notes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var result in unfinished)
                {
                    plan.Rows.AddRange(PhasingFixScript.UnfinishedMaxLayerRows(result, maxLayers).Where(r => notes.Add(r.WhatChanged)));
                }
            }
            return plan;
        }

        /// <summary>True when the scene carries its own word-fix list.</summary>
        public static bool HasSavedWordFixes()
        {
            return ReadSavedWordFixes() != null;
        }

        /// <summary>The scene's word fixes, or the HS2 list when the scene has none saved.</summary>
        public static List<PhasingWordFix> LoadWordFixes()
        {
            return ReadSavedWordFixes() ?? PhasingNameFixer.DefaultWordFixes();
        }

        public static void SaveWordFixes(IEnumerable<PhasingWordFix> fixes)
        {
            EnsureLoaded();
            var root = Loader.Core.RootNode;
            if (root.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, WordFixesSubId) != null)
            {
                root.RemoveAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, WordFixesSubId);
            }
            root.AddAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, WordFixesSubId, Encoding.UTF8.GetBytes(PhasingWordFixText.Write(fixes)));
            Loader.Global.SetSaveRequiredFlag(true, false);
        }

        /// <summary>
        /// Carries out the plan in one MAXScript batch (one undo record). The caller holds the scene first if it
        /// wants Edit > Fetch to restore it, and plans again afterwards to see what is left.
        /// </summary>
        public static PhasingFixResult Apply(PhasingFixPlan plan)
        {
            var batch = PhasingFixScript.Build(plan, "__phasing_fix_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_");
            string output = null;
            string error = null;
            try
            {
#if MAX2022_OR_NEWER
                output = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(batch.Script, ManagedServices.MaxscriptSDK.ScriptSource.NotSpecified);
#else
                output = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(batch.Script);
#endif
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            var result = PhasingFixScript.ReadResult(batch, output);
            if (error != null && result.Aborted == null)
            {
                result.Aborted = error;
            }
            if (!result.Completed && !string.IsNullOrEmpty(output))
            {
                // Something came back without the end marker, e.g. a compile error returned as text: show its start,
                // and keep all of it in the MAXScript Listener.
                if (result.Aborted == null)
                {
                    result.Aborted = PhasingFixScript.OneLine(output, 300);
                }
                try
                {
                    Loader.Global.TheListener.EditStream.Puts("Fix phasing names: 3ds Max sent back\n" + output.Replace("\r\n", "\n") + "\n");
                }
                catch (Exception)
                {
                    // The message box still shows the start of it.
                }
            }
            return result;
        }

        private static List<PhasingWordFix> ReadSavedWordFixes()
        {
            EnsureLoaded();
            var chunk = Loader.Core.RootNode.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, WordFixesSubId);
            if (chunk == null || chunk.Data == null)
            {
                return null;
            }
            try
            {
                return PhasingWordFixText.Read(Encoding.UTF8.GetString(chunk.Data));
            }
            catch (Exception)
            {
                return null;
            }
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
