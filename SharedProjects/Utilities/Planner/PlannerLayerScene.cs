using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>One node of the scene as the layer builder sees it: a 3ds Max node, keyed by its handle.</summary>
    public sealed class PlannerSceneNode
    {
        /// <summary>Stable key: the 3ds Max node handle.</summary>
        public long Key;
        public string Name;
        /// <summary>The parent's key, or null at the scene root.</summary>
        public long? ParentKey;
        /// <summary>Position among its siblings; equal values keep the order of the list.</summary>
        public int ChildIndex;
        /// <summary>True when other nodes are linked under this one, even if they are not in the list.</summary>
        public bool HasChildren;
        public bool IsGroupHead;
        public bool IsGroupMember;
        /// <summary>A helper rather than geometry (a dummy, a point, a group head), as 3ds Max reports it.</summary>
        public bool IsHelper;
        /// <summary>A shape (a spline such as a RailClone path), which the export leaves out unless something exported is linked under it.</summary>
        public bool IsShape;
        /// <summary>The Max layer the node sits on, or null when not known.</summary>
        public string LayerName;
        /// <summary>The node's planner_id user property, or null.</summary>
        public string PlannerId;
        /// <summary>Hidden, as the export reads it (the node, its layer or its category hidden).</summary>
        public bool Hidden;
        /// <summary>Do not export is ticked in its object properties (babylonjs_noexport).</summary>
        public bool NoExport;
    }

    /// <summary>The scene the layer builder plans against: every node and every Max layer.</summary>
    public sealed class PlannerSceneSnapshot
    {
        public readonly List<PlannerSceneNode> Nodes = new List<PlannerSceneNode>();
        /// <summary>The Max layers, in the Layer Manager's order.</summary>
        public readonly List<PhasingMaxLayer> Layers = new List<PhasingMaxLayer>();
        /// <summary>The scene's Export hidden objects setting: hidden nodes go into the GLB too.</summary>
        public bool ExportHidden;
    }

    /// <summary>
    /// A layer and its helper node of the same name as one unit of the phasing tree, as the scene has them: either may be
    /// missing (a layer without its helper, a helper without its layer).
    /// </summary>
    public sealed class PlannerSceneUnit
    {
        public PhasingMaxLayer Layer;
        public PlannerSceneNode Helper;
        /// <summary>Inside the phasing root: the layer in the root layer's tree, or the helper under the root node.</summary>
        public bool InPhasingTree;
        /// <summary>The ID the unit's name carries, or null.</summary>
        public PlannerLayerIdKey IdKey;

        /// <summary>The layer's name, else the helper's.</summary>
        public string Name
        {
            get { return Layer != null ? Layer.Name : Helper.Name; }
        }

        /// <summary>The helper's Planner ID, or null.</summary>
        public string PlannerId
        {
            get { return Helper != null ? Helper.PlannerId : null; }
        }

        /// <summary>"layer and helper", "layer only (no helper)" or "helper only (no layer)".</summary>
        public string What
        {
            get
            {
                if (Layer != null && Helper != null)
                {
                    return "layer and helper";
                }
                return Layer != null ? "layer only (no helper)" : "helper only (no layer)";
            }
        }
    }

    /// <summary>
    /// The scene indexed for the layer builder and the object linker: nodes by key and their children, Max layers by name
    /// (3ds Max layer names ignore capitals) and their children, the phasing root as Fix phasing names finds it, and the
    /// layer + helper units. Pure logic over a <see cref="PlannerSceneSnapshot"/>.
    /// </summary>
    public sealed class PlannerSceneModel
    {
        /// <summary>The user property that ties a helper to its row of the Planner's list.</summary>
        public const string PlannerIdProperty = "planner_id";
        /// <summary>The root a scene without one gets: a layer and a helper of this name at the top.</summary>
        public const string DefaultRootName = "Work_Phasing";

        private static readonly Regex PhaseGroup = new Regex(@"^_[0-9]+_.", RegexOptions.CultureInvariant);

        public readonly List<PlannerSceneNode> Nodes = new List<PlannerSceneNode>();
        public readonly List<PhasingMaxLayer> Layers = new List<PhasingMaxLayer>();
        public readonly Dictionary<long, PlannerSceneNode> NodeByKey = new Dictionary<long, PlannerSceneNode>();
        public readonly Dictionary<string, PhasingMaxLayer> LayerByName = new Dictionary<string, PhasingMaxLayer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, List<PlannerSceneNode>> children = new Dictionary<long, List<PlannerSceneNode>>();
        private readonly List<PlannerSceneNode> roots;
        private readonly ILookup<string, PhasingMaxLayer> layerChildren;
        private readonly Dictionary<long, bool> leftOut = new Dictionary<long, bool>();
        private static readonly List<PlannerSceneNode> None = new List<PlannerSceneNode>();

        /// <summary>The phasing root node: the first top-level node with "phasing" in its name, or null.</summary>
        public PlannerSceneNode RootNode;
        /// <summary>The phasing root layer: the layer named like the root node, else the first top-level layer with "phasing" in its name; or null.</summary>
        public PhasingMaxLayer RootLayer;
        /// <summary>Max layers inside the root layer's tree (the root layer not included).</summary>
        public readonly HashSet<string> TreeLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Nodes under the root node at any depth (the root node not included).</summary>
        public readonly HashSet<long> TreeNodes = new HashSet<long>();
        /// <summary>Every layer + helper unit of the scene (see <see cref="PlannerSceneUnit"/>).</summary>
        public readonly List<PlannerSceneUnit> Units = new List<PlannerSceneUnit>();
        /// <summary>Keys of the helpers of the units inside the phasing tree that have their layer, and of the root node: the phasing helpers.</summary>
        public readonly HashSet<long> PhasingHelpers = new HashSet<long>();

        public PlannerSceneModel(PlannerSceneSnapshot snapshot)
        {
            var keys = new HashSet<long>();
            foreach (var node in snapshot != null ? snapshot.Nodes : new List<PlannerSceneNode>())
            {
                if (node != null && keys.Add(node.Key))
                {
                    Nodes.Add(node);
                    NodeByKey[node.Key] = node;
                }
            }
            foreach (var layer in snapshot != null ? snapshot.Layers : new List<PhasingMaxLayer>())
            {
                if (layer != null && layer.Name != null && !LayerByName.ContainsKey(layer.Name))
                {
                    Layers.Add(layer);
                    LayerByName[layer.Name] = layer;
                }
            }
            var position = new Dictionary<long, int>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                position[Nodes[i].Key] = i;
            }
            roots = Nodes.Where(n => !n.ParentKey.HasValue || !NodeByKey.ContainsKey(n.ParentKey.Value))
                .OrderBy(n => n.ChildIndex).ThenBy(n => position[n.Key]).ToList();
            foreach (var siblings in Nodes.Where(n => n.ParentKey.HasValue && NodeByKey.ContainsKey(n.ParentKey.Value)).GroupBy(n => n.ParentKey.Value))
            {
                children[siblings.Key] = siblings.OrderBy(n => n.ChildIndex).ThenBy(n => position[n.Key]).ToList();
            }
            layerChildren = Layers.Where(l => l.ParentName != null).ToLookup(l => l.ParentName, StringComparer.OrdinalIgnoreCase);

            FindRoot();
            BuildUnits();
        }

        /// <summary>The top-level nodes (and nodes whose parent is not in the list), in their order at the top.</summary>
        public IList<PlannerSceneNode> Roots
        {
            get { return roots.AsReadOnly(); }
        }

        public List<PlannerSceneNode> ChildrenOf(PlannerSceneNode node)
        {
            List<PlannerSceneNode> list;
            return node != null && children.TryGetValue(node.Key, out list) ? list : None;
        }

        public PlannerSceneNode ParentOf(PlannerSceneNode node)
        {
            PlannerSceneNode parent;
            return node != null && node.ParentKey.HasValue && NodeByKey.TryGetValue(node.ParentKey.Value, out parent) ? parent : null;
        }

        public IEnumerable<PhasingMaxLayer> ChildLayersOf(string layerName)
        {
            return layerName == null ? Enumerable.Empty<PhasingMaxLayer>() : layerChildren[layerName];
        }

        /// <summary>The layer and every layer above it, nearest first.</summary>
        public List<PhasingMaxLayer> LayerAncestors(PhasingMaxLayer layer)
        {
            var result = new List<PhasingMaxLayer>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = layer;
            while (current != null && current.ParentName != null && seen.Add(current.Name))
            {
                PhasingMaxLayer parent;
                if (!LayerByName.TryGetValue(current.ParentName, out parent))
                {
                    break;
                }
                result.Add(parent);
                current = parent;
            }
            return result;
        }

        /// <summary>Is <paramref name="node"/> <paramref name="ancestor"/> or linked under it at any depth?</summary>
        public bool IsUnder(PlannerSceneNode node, PlannerSceneNode ancestor)
        {
            var seen = new HashSet<long>();
            for (var current = node; current != null && seen.Add(current.Key); current = ParentOf(current))
            {
                if (current.Key == ancestor.Key)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A shape (a spline such as a RailClone path) with nothing exported linked under it at any depth: the export
        /// leaves it out and the Planner never sees it (as Fix phasing names reads it).
        /// </summary>
        public bool LeftOut(PlannerSceneNode node)
        {
            if (!node.IsShape)
            {
                return false;
            }
            bool result;
            if (leftOut.TryGetValue(node.Key, out result))
            {
                return result;
            }
            result = true;
            var seen = new HashSet<long> { node.Key };
            var pending = new Stack<PlannerSceneNode>();
            pending.Push(node);
            while (pending.Count > 0 && result)
            {
                var current = pending.Pop();
                if (current.HasChildren && !children.ContainsKey(current.Key))
                {
                    result = false;
                    break;
                }
                foreach (var child in ChildrenOf(current))
                {
                    if (!seen.Add(child.Key))
                    {
                        continue;
                    }
                    if (!child.IsShape)
                    {
                        result = false;
                        break;
                    }
                    pending.Push(child);
                }
            }
            leftOut[node.Key] = result;
            return result;
        }

        /// <summary>The unit whose layer has this name (any capitals), or null.</summary>
        public PlannerSceneUnit UnitOfLayer(string layerName)
        {
            return layerName == null ? null : Units.FirstOrDefault(u => u.Layer != null && string.Equals(u.Layer.Name, layerName, StringComparison.OrdinalIgnoreCase));
        }

        // The root as Fix phasing names finds it: the first top-level node with "phasing" in its name (a shape the export
        // leaves out does not count), and the layer named like it, else the first top-level layer with "phasing" in its
        // name. With no root node, a layer named Work_Phasing anywhere also counts.
        private void FindRoot()
        {
            RootNode = roots.FirstOrDefault(n => !LeftOut(n) && Contains(n.Name, "phasing"));
            if (RootNode != null && RootNode.Name != null)
            {
                LayerByName.TryGetValue(RootNode.Name, out RootLayer);
            }
            if (RootLayer == null)
            {
                RootLayer = Layers.FirstOrDefault(l => l.ParentName == null && Contains(l.Name, "phasing"));
            }
            if (RootLayer == null && RootNode == null)
            {
                LayerByName.TryGetValue(DefaultRootName, out RootLayer);
            }

            if (RootLayer != null)
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RootLayer.Name };
                var waiting = new Stack<PhasingMaxLayer>(ChildLayersOf(RootLayer.Name));
                while (waiting.Count > 0)
                {
                    var layer = waiting.Pop();
                    if (!visited.Add(layer.Name))
                    {
                        continue;
                    }
                    TreeLayers.Add(layer.Name);
                    foreach (var child in ChildLayersOf(layer.Name))
                    {
                        waiting.Push(child);
                    }
                }
            }
            if (RootNode != null)
            {
                var pending = new Stack<PlannerSceneNode>(ChildrenOf(RootNode));
                while (pending.Count > 0)
                {
                    var node = pending.Pop();
                    if (!TreeNodes.Add(node.Key))
                    {
                        continue;
                    }
                    foreach (var child in ChildrenOf(node))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        // Layers and helpers paired by name (any capitals). Every layer is a unit but the root layer, the layers above it
        // and the default layer "0". A helper pairs with the layer of its name, preferring one sitting on that layer, then
        // one inside the phasing root, then one with a Planner ID. A helper with a Planner ID left over pairs with the layer
        // it sits on when that layer has no helper yet (a helper renamed by hand), else stands alone. One without a Planner
        // ID whose name has drifted from its layer's (one of the two re-dated or renamed by hand, as a scene built by hand
        // may have it), and that is not a Max group of objects (IsObjectGroup), pairs with the layer it sits on when that
        // layer has no helper yet, both names carry the same layer
        // ID and no other such helper sits on that layer; else it stands alone when its name reads as a layer or a phase
        // group (_<n>_<name>). Other helpers (Max groups of objects, pivots) are not units.
        private void BuildUnits()
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0" };
            if (RootLayer != null)
            {
                excluded.Add(RootLayer.Name);
                foreach (var above in LayerAncestors(RootLayer))
                {
                    excluded.Add(above.Name);
                }
            }
            var candidates = Nodes
                .Where(n => (RootNode == null || n.Key != RootNode.Key) && n.Name != null && !LeftOut(n)
                    && (n.IsHelper || n.IsShape || n.PlannerId != null))
                .ToList();
            var byName = candidates.ToLookup(n => n.Name, StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<long>();

            foreach (var layer in Layers.Where(l => !excluded.Contains(l.Name)))
            {
                var unit = new PlannerSceneUnit { Layer = layer, InPhasingTree = TreeLayers.Contains(layer.Name) };
                var helper = byName[layer.Name]
                    .Where(n => !used.Contains(n.Key))
                    .OrderBy(n => string.Equals(n.LayerName, layer.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(n => TreeNodes.Contains(n.Key) ? 0 : 1)
                    .ThenBy(n => n.PlannerId != null ? 0 : 1)
                    .FirstOrDefault();
                if (helper != null)
                {
                    unit.Helper = helper;
                    used.Add(helper.Key);
                }
                Units.Add(unit);
            }
            foreach (var helper in candidates.Where(n => n.PlannerId != null && !used.Contains(n.Key)))
            {
                var home = helper.LayerName != null && !excluded.Contains(helper.LayerName)
                    ? Units.FirstOrDefault(u => u.Helper == null && u.Layer != null && string.Equals(u.Layer.Name, helper.LayerName, StringComparison.OrdinalIgnoreCase))
                    : null;
                used.Add(helper.Key);
                if (home != null)
                {
                    home.Helper = helper;
                }
                else
                {
                    Units.Add(new PlannerSceneUnit { Helper = helper });
                }
            }
            // A Max group of objects is never taken for a drifted helper: it would be renamed to the layer's name and
            // the layer's loose objects linked into it.
            var drifted = candidates
                .Where(n => !used.Contains(n.Key) && n.PlannerId == null && n.IsHelper && !IsObjectGroup(n) && n.LayerName != null && !excluded.Contains(n.LayerName))
                .Select(n => new { Helper = n, Key = PlannerLayerIdKey.FromName(n.Name) })
                .Where(c => c.Key != null)
                .GroupBy(c => c.Helper.LayerName, StringComparer.OrdinalIgnoreCase);
            foreach (var onLayer in drifted)
            {
                var home = Units.FirstOrDefault(u => u.Helper == null && u.Layer != null && string.Equals(u.Layer.Name, onLayer.Key, StringComparison.OrdinalIgnoreCase));
                var layerKey = home != null ? PlannerLayerIdKey.FromName(home.Layer.Name) : null;
                var same = layerKey != null ? onLayer.Where(c => layerKey.Equals(c.Key)).ToList() : null;
                if (same != null && same.Count == 1)
                {
                    home.Helper = same[0].Helper;
                    used.Add(same[0].Helper.Key);
                }
            }
            foreach (var helper in candidates.Where(n => !used.Contains(n.Key) && n.IsHelper && ReadsAsPhasingName(n.Name)))
            {
                used.Add(helper.Key);
                Units.Add(new PlannerSceneUnit { Helper = helper });
            }
            foreach (var unit in Units)
            {
                if (unit.Helper != null && TreeNodes.Contains(unit.Helper.Key))
                {
                    unit.InPhasingTree = true;
                }
                unit.IdKey = PlannerLayerIdKey.FromName(unit.Name);
                // Only a helper with its layer is a phasing helper for the object links: a helper alone may be a Max
                // group of objects that happens to have a phasing name, which must not be emptied into its layer's helper.
                if (unit.Helper != null && unit.Layer != null && unit.InPhasingTree)
                {
                    PhasingHelpers.Add(unit.Helper.Key);
                }
            }
            if (RootNode != null)
            {
                PhasingHelpers.Add(RootNode.Key);
            }
        }

        /// <summary>
        /// A 3ds Max group head with anything but helpers among its members (geometry, a spline): a group of objects,
        /// which is never taken for a phasing helper by its name alone.
        /// </summary>
        public bool IsObjectGroup(PlannerSceneNode node)
        {
            if (node == null || !node.IsGroupHead)
            {
                return false;
            }
            var seen = new HashSet<long> { node.Key };
            var pending = new Stack<PlannerSceneNode>(ChildrenOf(node));
            while (pending.Count > 0)
            {
                var child = pending.Pop();
                if (!seen.Add(child.Key) || !child.IsGroupMember)
                {
                    continue;
                }
                if (!child.IsHelper)
                {
                    return true;
                }
                foreach (var below in ChildrenOf(child))
                {
                    pending.Push(below);
                }
            }
            return false;
        }

        /// <summary>A layer name (it starts with an ID) or a phase group's (_&lt;n&gt;_&lt;name&gt;).</summary>
        public static bool ReadsAsPhasingName(string name)
        {
            return name != null && (PhaseGroup.IsMatch(name) || PlannerNaming.ParseLayerName(name) != null);
        }

        private static bool Contains(string text, string part)
        {
            return text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
