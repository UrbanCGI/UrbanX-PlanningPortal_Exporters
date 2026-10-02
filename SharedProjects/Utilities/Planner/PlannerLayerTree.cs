using System;
using System.Collections.Generic;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>One layer of the phasing tree as it stands: its helper, and the code and name it reads as.</summary>
    public sealed class PlannerLayerInfo
    {
        public PlannerSceneLayer Layer;
        /// <summary>The helper that stands for the layer, or null when it has none yet.</summary>
        public PlannerSceneNode Helper;
        /// <summary>The Work_Phasing root itself.</summary>
        public bool IsRoot;
        /// <summary>0 for the root, 1 for top-level folders, and so on.</summary>
        public int Depth;
        /// <summary>The folder code: the helper's planner_code, else read from the layer name. Null when it has none.</summary>
        public string Code;
        /// <summary>The folder name without the code: the helper's planner_name, else read from the layer name.</summary>
        public string Name;
        /// <summary>True when the layer name still reads as an old dated layer (a <c>_GG_</c> group, or dates / TBC in the name).</summary>
        public bool LegacyName;

        public string LayerName { get { return Layer.Name; } }
    }

    /// <summary>
    /// The Work_Phasing root and the layers under it, read the same way by the Planner layers panel and the
    /// naming check: a layer's code and name come from its helper's planner_* properties once a plan has
    /// written them, otherwise from the layer name (an old dated name is read with the legacy rules).
    /// </summary>
    public sealed class PlannerLayerTree
    {
        /// <summary>The root first, then the folder layers in outline order (depth first, layer manager order).</summary>
        public readonly List<PlannerLayerInfo> Layers = new List<PlannerLayerInfo>();
        private readonly Dictionary<string, PlannerLayerInfo> byName = new Dictionary<string, PlannerLayerInfo>(PlannerScene.LayerNameComparer);
        private readonly Dictionary<string, PlannerLayerInfo> byHelperId = new Dictionary<string, PlannerLayerInfo>(StringComparer.Ordinal);

        public PlannerScene Scene { get; private set; }

        /// <summary>The root's entry, or null when the scene has no phasing root.</summary>
        public PlannerLayerInfo Root { get { return Layers.Count > 0 ? Layers[0] : null; } }

        /// <summary>The folder layers (everything but the root).</summary>
        public IEnumerable<PlannerLayerInfo> Folders { get { return Layers.Skip(1); } }

        public static PlannerLayerTree Read(PlannerScene scene)
        {
            var tree = new PlannerLayerTree { Scene = scene ?? new PlannerScene() };
            var root = tree.Scene.FindPhasingRoot();
            if (root == null)
            {
                return tree;
            }
            tree.Add(root, 0, true);
            var depthOf = new Dictionary<string, int>(PlannerScene.LayerNameComparer) { { root.Name, 0 } };
            foreach (var layer in tree.Scene.Descendants(root.Name))
            {
                int parentDepth;
                var depth = layer.ParentName != null && depthOf.TryGetValue(layer.ParentName, out parentDepth) ? parentDepth + 1 : 1;
                depthOf[layer.Name] = depth;
                tree.Add(layer, depth, false);
            }
            return tree;
        }

        private void Add(PlannerSceneLayer layer, int depth, bool isRoot)
        {
            var helper = Scene.FindLayerHelper(layer.Name);
            var info = new PlannerLayerInfo { Layer = layer, Helper = helper, Depth = depth, IsRoot = isRoot };
            var legacy = isRoot ? null : PlannerCodes.ParseLegacyLayer(layer.Name, null);
            info.LegacyName = legacy != null && (legacy.IsGroup || legacy.HasSchedule);

            var props = helper != null ? helper.Props : null;
            info.Code = PlannerProps.Get(props, PlannerProps.Code);
            info.Name = PlannerProps.Get(props, PlannerProps.Name);
            if (info.Code == null && info.Name == null && !isRoot)
            {
                if (info.LegacyName)
                {
                    info.Code = legacy.Code;
                    info.Name = legacy.Name;
                }
                else
                {
                    var coded = PlannerCodes.ParseCodedLayerName(layer.Name);
                    info.Code = coded.Code;
                    info.Name = PlannerCodes.SanitiseName(coded.Name);
                }
            }
            if (isRoot && info.Name == null)
            {
                info.Name = layer.Name;
            }

            Layers.Add(info);
            byName[layer.Name] = info;
            if (helper != null && !byHelperId.ContainsKey(helper.Id))
            {
                byHelperId[helper.Id] = info;
            }
        }

        /// <summary>The entry for a layer on the tree (root included), or null for a layer outside it.</summary>
        public PlannerLayerInfo Find(string layerName)
        {
            PlannerLayerInfo info;
            return layerName != null && byName.TryGetValue(layerName, out info) ? info : null;
        }

        /// <summary>The layer a Planner helper stands for, or null when the node is not one.</summary>
        public PlannerLayerInfo LayerOfHelper(string nodeId)
        {
            PlannerLayerInfo info;
            return nodeId != null && byHelperId.TryGetValue(nodeId, out info) ? info : null;
        }

        public bool IsPlannerHelper(string nodeId)
        {
            return LayerOfHelper(nodeId) != null;
        }

        /// <summary>
        /// The layer whose helper is the node's nearest Planner-helper ancestor: the folder the exported model files
        /// the node under (the GLB hierarchy is the Max links). Null when no Planner helper is above it.
        /// </summary>
        public PlannerLayerInfo FiledUnder(PlannerSceneNode node)
        {
            var guard = 0;
            for (var parent = node != null ? Scene.FindNode(node.ParentId) : null; parent != null && guard < 10000; parent = Scene.FindNode(parent.ParentId), guard++)
            {
                var info = LayerOfHelper(parent.Id);
                if (info != null)
                {
                    return info;
                }
            }
            return null;
        }

        /// <summary>The folder codes in outline order (null for a folder without a code), as removal targets resolve against them.</summary>
        public List<string> FolderCodes()
        {
            return Folders.Select(f => f.Code).ToList();
        }
    }
}
