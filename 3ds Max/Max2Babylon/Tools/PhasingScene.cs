using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: reads the scene into the old phasing name fixer's model (the export check's counts on a scene with the
    /// old names), tells helpers and shapes from geometry, and keeps the project's word fixes with the scene (Clean up scene
    /// graph uses them). Every decision is made by <see cref="PhasingNameFixer"/> (shared, tested); this file only reads.
    /// Fix phasing names itself is replaced by Clean up scene graph.
    /// </summary>
    internal static class PhasingScene
    {
        // Root node app data under Loader.Class_ID: sub-ids 0 and 1 hold the exporter's node GUID and instance
        // flag, so the word fixes take one of their own ("PHFX").
        private const int WordFixesSubId = 0x50484658;

        /// <summary>Every node of the scene (handle, name, parent, place among its siblings, group, helper and shape flags) and every Max layer.</summary>
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
                    bool isHelper, isShape;
                    Classify(child, out isHelper, out isShape);
                    nodes.Add(new PhasingSceneNode
                    {
                        Key = child.Handle,
                        Name = child.Name,
                        ParentKey = parentKey,
                        ChildIndex = i,
                        HasChildren = child.NumberOfChildren > 0,
                        IsGroupHead = child.IsGroupHead,
                        IsGroupMember = child.IsGroupMember,
                        IsHelper = isHelper,
                        IsShape = isShape
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

        /// <summary>
        /// What 3ds Max says a node is. A helper rather than geometry: a group head, or a node whose evaluated object is
        /// of the helper super class (dummies, points and the like). A shape: a node whose evaluated object is of the
        /// shape super class, a spline such as a RailClone path, which the export leaves out (a spline with a modifier
        /// that makes it geometry, e.g. Sweep or Extrude, evaluates to geometry, is exported and is not a shape). A node
        /// whose base object is geometry is not evaluated: modifiers do not make geometry a helper or a shape. Anything
        /// that cannot be read is neither.
        /// </summary>
        internal static void Classify(IINode node, out bool isHelper, out bool isShape)
        {
            isHelper = false;
            isShape = false;
            try
            {
                if (node.IsGroupHead)
                {
                    isHelper = true;
                    return;
                }
                var objectRef = node.ObjectRef;
                if (objectRef == null)
                {
                    return;
                }
                var baseObject = objectRef.FindBaseObject();
                if (baseObject != null && baseObject.SuperClassID == SClass_ID.Geomobject)
                {
                    return;
                }
                var state = node.EvalWorldState(0, false);
                if (state == null || state.Obj == null)
                {
                    return;
                }
                var superClass = state.Obj.SuperClassID;
                isHelper = superClass == SClass_ID.Helper;
                isShape = superClass == SClass_ID.Shape;
            }
            catch (Exception)
            {
                isHelper = false;
                isShape = false;
            }
        }

        /// <summary>
        /// Reads the scene and plans the old fix, with no stage changes (the export check reads an old-name scene's top
        /// node, the objects the old rules leave, and its counts from it).
        /// </summary>
        public static PhasingFixPlan Plan(IEnumerable<PhasingWordFix> wordFixes)
        {
            var nodes = new List<PhasingSceneNode>();
            var maxLayers = new List<PhasingMaxLayer>();
            Read(nodes, maxLayers);
            return PhasingNameFixer.Plan(nodes, maxLayers, wordFixes, null);
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
