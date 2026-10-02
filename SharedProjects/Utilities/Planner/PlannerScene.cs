using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>A 3ds Max layer as the layer planners see it. Layer names are unique in a scene.</summary>
    public sealed class PlannerSceneLayer
    {
        public string Name;
        /// <summary>The parent layer's name, or null for a top-level layer.</summary>
        public string ParentName;

        public PlannerSceneLayer Clone()
        {
            return new PlannerSceneLayer { Name = Name, ParentName = ParentName };
        }
    }

    /// <summary>A scene node (object or helper) as the layer planners see it.</summary>
    public sealed class PlannerSceneNode
    {
        /// <summary>A key the 3ds Max side maps back to its node (the node handle); helpers a plan creates get "new:&lt;n&gt;".</summary>
        public string Id;
        public string Name;
        public string LayerName;
        /// <summary>The Max parent (link), or null at the scene root.</summary>
        public string ParentId;
        /// <summary>A helper or dummy (Point helper, Dummy, group head, container); false for geometry.</summary>
        public bool IsHelper;
        /// <summary>
        /// A group head or a container: a helper 3ds Max keeps for its members. It never stands for a layer (ungrouping
        /// deletes a group head, and its planner_* properties with it), so it is treated like any other object.
        /// </summary>
        public bool IsGroupHead;
        /// <summary>The node's planner_* user properties (<see cref="PlannerProps"/>).</summary>
        public Dictionary<string, string> Props = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>True for a helper that can stand for its layer (not a group head or container).</summary>
        public bool CanStandForLayer { get { return IsHelper && !IsGroupHead; } }

        public PlannerSceneNode Clone()
        {
            return new PlannerSceneNode
            {
                Id = Id,
                Name = Name,
                LayerName = LayerName,
                ParentId = ParentId,
                IsHelper = IsHelper,
                IsGroupHead = IsGroupHead,
                Props = new Dictionary<string, string>(Props ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            };
        }
    }

    public enum PlannerSceneOpKind
    {
        CreateLayer,
        RenameLayer,
        SetLayerParent,
        /// <summary>Creates a Point helper named <c>NewName</c> on layer <c>LayerName</c>; later operations refer to it by <c>NodeId</c>.</summary>
        CreateHelper,
        RenameHelper,
        /// <summary>Puts an existing node on another layer (a same-named helper found on the wrong layer).</summary>
        MoveNodeToLayer,
        /// <summary>Links <c>NodeId</c> to <c>ParentNodeId</c> (null unlinks to the scene root), keeping its world position.</summary>
        LinkNode,
        SetUserProp,
        ClearUserProp
    }

    /// <summary>
    /// One scene change a planner asks for. Operations apply in order and name things as they are at that point
    /// (a layer renamed earlier is referred to by its new name afterwards).
    /// </summary>
    public sealed class PlannerSceneOp
    {
        public PlannerSceneOpKind Kind;
        /// <summary>The layer acted on (CreateLayer, RenameLayer, SetLayerParent), or the layer a helper is created on / a node is moved to.</summary>
        public string LayerName;
        /// <summary>CreateLayer, RenameLayer, CreateHelper, RenameHelper: the name after the operation.</summary>
        public string NewName;
        /// <summary>CreateLayer, SetLayerParent: the parent layer, or null for the top level.</summary>
        public string ParentLayerName;
        public string NodeId;
        /// <summary>The node's name when the operation runs (for messages only).</summary>
        public string NodeName;
        /// <summary>LinkNode: the new parent, or null for the scene root.</summary>
        public string ParentNodeId;
        public string ParentNodeName;
        public string PropName;
        public string PropValue;

        public static PlannerSceneOp CreateLayer(string name, string parentName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.CreateLayer, LayerName = name, NewName = name, ParentLayerName = parentName };
        }

        public static PlannerSceneOp RenameLayer(string name, string newName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.RenameLayer, LayerName = name, NewName = newName };
        }

        public static PlannerSceneOp SetLayerParent(string name, string parentName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.SetLayerParent, LayerName = name, ParentLayerName = parentName };
        }

        public static PlannerSceneOp CreateHelper(string nodeId, string name, string layerName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.CreateHelper, NodeId = nodeId, NodeName = name, NewName = name, LayerName = layerName };
        }

        public static PlannerSceneOp RenameHelper(PlannerSceneNode node, string newName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.RenameHelper, NodeId = node.Id, NodeName = node.Name, NewName = newName };
        }

        public static PlannerSceneOp MoveNodeToLayer(PlannerSceneNode node, string layerName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.MoveNodeToLayer, NodeId = node.Id, NodeName = node.Name, LayerName = layerName };
        }

        public static PlannerSceneOp LinkNode(PlannerSceneNode node, PlannerSceneNode parent)
        {
            return new PlannerSceneOp
            {
                Kind = PlannerSceneOpKind.LinkNode,
                NodeId = node.Id,
                NodeName = node.Name,
                ParentNodeId = parent != null ? parent.Id : null,
                ParentNodeName = parent != null ? parent.Name : null
            };
        }

        public static PlannerSceneOp SetUserProp(PlannerSceneNode node, string propName, string value)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.SetUserProp, NodeId = node.Id, NodeName = node.Name, PropName = propName, PropValue = value };
        }

        public static PlannerSceneOp ClearUserProp(PlannerSceneNode node, string propName)
        {
            return new PlannerSceneOp { Kind = PlannerSceneOpKind.ClearUserProp, NodeId = node.Id, NodeName = node.Name, PropName = propName };
        }

        /// <summary>The operation in plain words, for the dry-run list.</summary>
        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case PlannerSceneOpKind.CreateLayer:
                        return ParentLayerName != null
                            ? string.Format("Create layer '{0}' under '{1}'", NewName, ParentLayerName)
                            : string.Format("Create top-level layer '{0}'", NewName);
                    case PlannerSceneOpKind.RenameLayer:
                        return string.Format("Rename layer '{0}' to '{1}'", LayerName, NewName);
                    case PlannerSceneOpKind.SetLayerParent:
                        return ParentLayerName != null
                            ? string.Format("Move layer '{0}' under '{1}'", LayerName, ParentLayerName)
                            : string.Format("Move layer '{0}' to the top level", LayerName);
                    case PlannerSceneOpKind.CreateHelper:
                        return string.Format("Create helper '{0}' on layer '{1}'", NewName, LayerName);
                    case PlannerSceneOpKind.RenameHelper:
                        return string.Format("Rename helper '{0}' to '{1}'", NodeName, NewName);
                    case PlannerSceneOpKind.MoveNodeToLayer:
                        return string.Format("Put '{0}' on layer '{1}'", NodeName, LayerName);
                    case PlannerSceneOpKind.LinkNode:
                        return ParentNodeId != null
                            ? string.Format("Link '{0}' to '{1}'", NodeName, ParentNodeName)
                            : string.Format("Unlink '{0}'", NodeName);
                    case PlannerSceneOpKind.SetUserProp:
                        return string.Format("Set {0} = {1} on '{2}'", PropName, PropValue, NodeName);
                    case PlannerSceneOpKind.ClearUserProp:
                        return string.Format("Clear {0} on '{1}'", PropName, NodeName);
                    default:
                        return Kind.ToString();
                }
            }
        }

        public override string ToString()
        {
            return Description;
        }
    }

    public enum PlannerReviewSeverity
    {
        /// <summary>A change the plan makes (rename, move, create).</summary>
        Info = 0,
        /// <summary>Worth knowing, nothing to fix (a code was tidied, a layer is left in place).</summary>
        Note = 1,
        /// <summary>Probably not what was meant: check before applying.</summary>
        Warning = 2,
        /// <summary>The plan cannot do what the data asks for.</summary>
        Error = 3
    }

    public sealed class PlannerReviewItem
    {
        public PlannerReviewSeverity Severity;
        /// <summary>The layer or object the item is about.</summary>
        public string Subject;
        public string Message;

        public override string ToString()
        {
            return Severity.ToString().ToUpperInvariant() + ": " + Message;
        }
    }

    /// <summary>A Planner folder layer as a plan leaves it.</summary>
    public sealed class PlannerPlannedFolder
    {
        /// <summary>The layer's name after the plan.</summary>
        public string LayerName;
        public string ParentLayerName;
        /// <summary>The helper's node id ("new:&lt;n&gt;" for a helper the plan creates).</summary>
        public string HelperId;
        public string Code;
        public string Name;
        /// <summary>The Planner folder id (schedule updates only).</summary>
        public string FolderId;
        public DateTime? LegacyStart;
        public DateTime? LegacyFinish;
        public bool LegacyTbc;
        /// <summary>Depth below the phasing root (top-level folders are 0).</summary>
        public int Depth;
    }

    /// <summary>The result of a planner: the operations to apply (none are applied to the Max scene yet) and the review list.</summary>
    public sealed class PlannerScenePlan
    {
        public readonly List<PlannerSceneOp> Operations = new List<PlannerSceneOp>();
        public readonly List<PlannerReviewItem> Review = new List<PlannerReviewItem>();
        /// <summary>The folder layers under the phasing root in outline order, as the plan leaves them.</summary>
        public readonly List<PlannerPlannedFolder> Folders = new List<PlannerPlannedFolder>();
        /// <summary>The phasing root layer's name after the plan.</summary>
        public string RootLayerName;
        /// <summary>The scene as it will be once the operations are applied.</summary>
        public PlannerScene Result;

        public bool HasChanges { get { return Operations.Count > 0; } }

        public int Count(PlannerReviewSeverity severity)
        {
            return Review.Count(r => r.Severity == severity);
        }

        public string Summary()
        {
            var text = string.Format(CultureInfo.InvariantCulture, "{0} change(s) to {1} folder layer(s); {2} warning(s), {3} error(s).",
                Operations.Count, Folders.Count, Count(PlannerReviewSeverity.Warning), Count(PlannerReviewSeverity.Error));
            if (Operations.Count == 0)
            {
                text += " The layers already match.";
            }
            return text;
        }
    }

    /// <summary>
    /// The scene as the layer planners see it: layers (name, parent) and nodes (name, layer, parent link, planner
    /// props). The 3ds Max side fills it in; the planners never touch Max. <see cref="Apply"/> runs operations on
    /// the model itself, which the planners use to stay consistent and a dry run can use to preview the result.
    /// </summary>
    public sealed class PlannerScene
    {
        /// <summary>Layers in the layer manager's order (siblings keep this order in outlines).</summary>
        public List<PlannerSceneLayer> Layers = new List<PlannerSceneLayer>();
        public List<PlannerSceneNode> Nodes = new List<PlannerSceneNode>();

        public PlannerScene Clone()
        {
            return new PlannerScene
            {
                Layers = Layers.Select(l => l.Clone()).ToList(),
                Nodes = Nodes.Select(n => n.Clone()).ToList()
            };
        }

        /// <summary>Max compares layer names without regard to case.</summary>
        public static readonly StringComparer LayerNameComparer = StringComparer.OrdinalIgnoreCase;

        public PlannerSceneLayer FindLayer(string name)
        {
            return name == null ? null : Layers.FirstOrDefault(l => LayerNameComparer.Equals(l.Name, name));
        }

        // Id lookups run inside per-node loops (parent walks), so they go through an index. Nodes is a plain list
        // callers may edit, so the index is rebuilt whenever it looks stale: the count changed, or the entry found
        // no longer carries the id. A miss rebuilds once to be sure. The first node with an id wins, as before.
        private Dictionary<string, PlannerSceneNode> nodeIndex;
        private int nodeIndexCount = -1;

        public PlannerSceneNode FindNode(string id)
        {
            if (id == null)
            {
                return null;
            }
            PlannerSceneNode node;
            if (nodeIndex != null && nodeIndexCount == Nodes.Count && nodeIndex.TryGetValue(id, out node) && node.Id == id)
            {
                return node;
            }
            nodeIndex = new Dictionary<string, PlannerSceneNode>(StringComparer.Ordinal);
            foreach (var each in Nodes)
            {
                if (each != null && each.Id != null && !nodeIndex.ContainsKey(each.Id))
                {
                    nodeIndex[each.Id] = each;
                }
            }
            nodeIndexCount = Nodes.Count;
            return nodeIndex.TryGetValue(id, out node) ? node : null;
        }

        public IEnumerable<PlannerSceneLayer> ChildLayers(string parentName)
        {
            return parentName == null
                ? Layers.Where(l => l.ParentName == null)
                : Layers.Where(l => l.ParentName != null && LayerNameComparer.Equals(l.ParentName, parentName));
        }

        /// <summary>The layers below <paramref name="name"/>, depth first, parents before children.</summary>
        public List<PlannerSceneLayer> Descendants(string name)
        {
            var result = new List<PlannerSceneLayer>();
            var guard = new HashSet<string>(LayerNameComparer) { name };
            CollectDescendants(name, result, guard);
            return result;
        }

        private void CollectDescendants(string name, List<PlannerSceneLayer> result, HashSet<string> guard)
        {
            foreach (var child in ChildLayers(name).ToList())
            {
                if (!guard.Add(child.Name))
                {
                    continue;
                }
                result.Add(child);
                CollectDescendants(child.Name, result, guard);
            }
        }

        public IEnumerable<PlannerSceneNode> NodesOn(string layerName)
        {
            return Nodes.Where(n => LayerNameComparer.Equals(n.LayerName, layerName));
        }

        /// <summary>
        /// The helper that stands for a layer: a helper on the layer carrying planner_* properties (the one named
        /// like the layer first), else one named like the layer (or <paramref name="oldName"/>), exact case before
        /// any case. Helpers whose id is in <paramref name="exclude"/> are skipped, and so are group heads and
        /// containers. Null when there is none.
        /// </summary>
        public PlannerSceneNode FindLayerHelper(string layerName, string oldName = null, ICollection<string> exclude = null)
        {
            var onLayer = NodesOn(layerName).Where(n => n.CanStandForLayer && (exclude == null || !exclude.Contains(n.Id))).ToList();
            var marked = onLayer.Where(n => PlannerProps.MarksHelper(n.Props)).ToList();
            if (marked.Count > 0)
            {
                return marked.FirstOrDefault(n => string.Equals(n.Name, layerName, StringComparison.Ordinal)) ?? marked[0];
            }
            return onLayer.FirstOrDefault(n => string.Equals(n.Name, layerName, StringComparison.Ordinal))
                   ?? (oldName != null ? onLayer.FirstOrDefault(n => string.Equals(n.Name, oldName, StringComparison.Ordinal)) : null)
                   ?? onLayer.FirstOrDefault(n => string.Equals(n.Name, layerName, StringComparison.OrdinalIgnoreCase))
                   ?? (oldName != null ? onLayer.FirstOrDefault(n => string.Equals(n.Name, oldName, StringComparison.OrdinalIgnoreCase)) : null);
        }

        /// <summary>The top-level phasing root layer (the first, when there are several), or null.</summary>
        public PlannerSceneLayer FindPhasingRoot()
        {
            return ChildLayers(null).FirstOrDefault(l => PlannerCodes.IsPhasingRoot(l.Name));
        }

        public bool IsInside(string layerName, string ancestorName)
        {
            var guard = 0;
            for (var layer = FindLayer(layerName); layer != null && guard < 10000; layer = FindLayer(layer.ParentName), guard++)
            {
                if (LayerNameComparer.Equals(layer.Name, ancestorName))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A copy of the scene with <paramref name="operations"/> applied.</summary>
        public PlannerScene Apply(IEnumerable<PlannerSceneOp> operations)
        {
            var scene = Clone();
            foreach (var op in operations)
            {
                scene.ApplyInPlace(op);
            }
            return scene;
        }

        /// <summary>Applies one operation to this model. Throws when the operation does not fit the model.</summary>
        public void ApplyInPlace(PlannerSceneOp op)
        {
            switch (op.Kind)
            {
                case PlannerSceneOpKind.CreateLayer:
                    if (FindLayer(op.NewName) != null)
                    {
                        throw new InvalidOperationException("Layer already exists: " + op.NewName);
                    }
                    if (op.ParentLayerName != null && FindLayer(op.ParentLayerName) == null)
                    {
                        throw new InvalidOperationException("No parent layer " + op.ParentLayerName);
                    }
                    Layers.Add(new PlannerSceneLayer { Name = op.NewName, ParentName = op.ParentLayerName == null ? null : FindLayer(op.ParentLayerName).Name });
                    break;
                case PlannerSceneOpKind.RenameLayer:
                    {
                        var layer = RequireLayer(op.LayerName);
                        var clash = FindLayer(op.NewName);
                        if (clash != null && clash != layer)
                        {
                            throw new InvalidOperationException("Layer already exists: " + op.NewName);
                        }
                        var oldName = layer.Name;
                        layer.Name = op.NewName;
                        foreach (var child in Layers.Where(l => l.ParentName != null && LayerNameComparer.Equals(l.ParentName, oldName)))
                        {
                            child.ParentName = op.NewName;
                        }
                        foreach (var node in Nodes.Where(n => LayerNameComparer.Equals(n.LayerName, oldName)))
                        {
                            node.LayerName = op.NewName;
                        }
                        break;
                    }
                case PlannerSceneOpKind.SetLayerParent:
                    {
                        var layer = RequireLayer(op.LayerName);
                        if (op.ParentLayerName != null)
                        {
                            var parent = RequireLayer(op.ParentLayerName);
                            if (IsInside(parent.Name, layer.Name))
                            {
                                throw new InvalidOperationException("Layer " + layer.Name + " cannot move inside itself");
                            }
                            layer.ParentName = parent.Name;
                        }
                        else
                        {
                            layer.ParentName = null;
                        }
                        break;
                    }
                case PlannerSceneOpKind.CreateHelper:
                    if (FindNode(op.NodeId) != null)
                    {
                        throw new InvalidOperationException("Node id already used: " + op.NodeId);
                    }
                    Nodes.Add(new PlannerSceneNode { Id = op.NodeId, Name = op.NewName, LayerName = RequireLayer(op.LayerName).Name, IsHelper = true });
                    break;
                case PlannerSceneOpKind.RenameHelper:
                    RequireNode(op.NodeId).Name = op.NewName;
                    break;
                case PlannerSceneOpKind.MoveNodeToLayer:
                    RequireNode(op.NodeId).LayerName = RequireLayer(op.LayerName).Name;
                    break;
                case PlannerSceneOpKind.LinkNode:
                    {
                        var node = RequireNode(op.NodeId);
                        if (op.ParentNodeId != null)
                        {
                            RequireNode(op.ParentNodeId);
                            for (var p = op.ParentNodeId; p != null; p = FindNode(p) != null ? FindNode(p).ParentId : null)
                            {
                                if (p == node.Id)
                                {
                                    throw new InvalidOperationException("Linking " + node.Name + " would make a loop");
                                }
                            }
                        }
                        node.ParentId = op.ParentNodeId;
                        break;
                    }
                case PlannerSceneOpKind.SetUserProp:
                    RequireNode(op.NodeId).Props[op.PropName] = op.PropValue;
                    break;
                case PlannerSceneOpKind.ClearUserProp:
                    RequireNode(op.NodeId).Props.Remove(op.PropName);
                    break;
            }
        }

        private PlannerSceneLayer RequireLayer(string name)
        {
            var layer = FindLayer(name);
            if (layer == null)
            {
                throw new InvalidOperationException("No layer " + name);
            }
            return layer;
        }

        private PlannerSceneNode RequireNode(string id)
        {
            var node = FindNode(id);
            if (node == null)
            {
                throw new InvalidOperationException("No node " + id);
            }
            return node;
        }
    }
}
