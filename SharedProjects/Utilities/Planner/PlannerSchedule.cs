using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>One node of the model as the Planner reads the exported GLB: a 3ds Max node the export carries.</summary>
    public sealed class PlannerModelNode
    {
        /// <summary>The 3ds Max node handle.</summary>
        public long Key;
        /// <summary>
        /// Its name in the GLB, which the Planner reads: its 3ds Max name, with " (1)", " (2)", ... added when a node the
        /// export wrote before it has the same name (the glTF export's GetUniqueNodeName).
        /// </summary>
        public string Name;
        /// <summary>Its name in 3ds Max.</summary>
        public string MaxName;
        /// <summary>The node it is linked under in the GLB, or null at the top.</summary>
        public PlannerModelNode Parent;
        /// <summary>A mesh in the GLB (geometry); helpers, groups and the empty nodes the export makes are not.</summary>
        public bool Mesh;

        /// <summary>The export gave it another name than its own, as a node written before it has its name.</summary>
        public bool RenamedByExport
        {
            get { return !string.Equals(Name, MaxName, StringComparison.Ordinal); }
        }
    }

    /// <summary>A node of the scene the Planner does not schedule, and why.</summary>
    public sealed class PlannerNotScheduled
    {
        public long Key;
        public string Name;
        public string Reason;
        /// <summary>The Max layer it sits on, for the list.</summary>
        public string LayerName;
    }

    /// <summary>
    /// The scene as the export hands it to the Planner: which nodes the GLB carries, as the exporter decides it (a node
    /// that is hidden while Export hidden objects is off, or has Do not export ticked, is left out unless something
    /// exported is linked under it, when it goes as an empty node; a spline goes only as such an empty node; nothing
    /// under a node left out goes), in the GLB's order and with the GLB's names.
    ///
    /// The order is the export's: depth first, each node before the nodes linked under it, those in the scene's order
    /// (BabylonExporter.exportNodeRec), the top-level nodes that hold geometry first and the rest after them
    /// (getRootNodes takes the top nodes of the meshes first, then of lights, cameras and helpers). The glTF export
    /// writes the nodes in that order and gives a name it has written already " (1)", " (2)", ... (GLTFExporter.
    /// GetUniqueNodeName), so a second node called X reaches the Planner as "X (1)", an object of its own; Babylon's
    /// loader keeps that order, which the Planner's index follows. Not mirrored: the export writes an instance straight
    /// after the first copy of its geometry, and lights and cameras after the meshes among their siblings; that order
    /// only matters when names repeat or two layers share a label. Pure logic over a <see cref="PlannerSceneSnapshot"/>.
    /// </summary>
    public sealed class PlannerScheduleModel
    {
        public const string HiddenReason = "hidden, and the export leaves hidden objects out (Export hidden objects is off)";
        public const string NoExportReason = "Do not export is ticked in its object properties";

        public readonly PlannerSceneModel Scene;
        /// <summary>The GLB's nodes, in the GLB's (the export's) order.</summary>
        public readonly List<PlannerModelNode> Nodes = new List<PlannerModelNode>();
        public readonly Dictionary<long, PlannerModelNode> ByKey = new Dictionary<long, PlannerModelNode>();
        /// <summary>Nodes the export leaves out for a reason of their own (hidden, Do not export), with why.</summary>
        public readonly Dictionary<long, string> LeftOut = new Dictionary<long, string>();

        public PlannerScheduleModel(PlannerSceneSnapshot snapshot)
        {
            Scene = new PlannerSceneModel(snapshot);
            bool exportHidden = snapshot != null && snapshot.ExportHidden;
            var self = new Dictionary<long, bool>();
            foreach (var node in Scene.Nodes)
            {
                self[node.Key] = !node.NoExport && !(node.Hidden && !exportHidden) && !node.IsShape;
            }

            // The export's order: each top-level node's tree depth first, the trees holding geometry first.
            var order = new List<PlannerSceneNode>();
            var seen = new HashSet<long>();
            var trees = Scene.Roots.Select(root => DepthFirst(root, seen)).ToList();
            foreach (var tree in trees.Where(HoldsGeometry))
            {
                order.AddRange(tree);
            }
            foreach (var tree in trees.Where(t => !HoldsGeometry(t)))
            {
                order.AddRange(tree);
            }

            // Relevant to the export: exportable itself, or with something exportable linked under it at any depth.
            // Worked out bottom-up without recursion, so a deep hierarchy cannot overflow the stack.
            var relevant = new Dictionary<long, bool>();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var node = order[i];
                bool result = self[node.Key];
                foreach (var child in Scene.ChildrenOf(node))
                {
                    bool r;
                    if (!result && relevant.TryGetValue(child.Key, out r) && r)
                    {
                        result = true;
                    }
                }
                relevant[node.Key] = result;
            }

            // Exported: relevant, under an exported node or at the top (the export never goes below a node it leaves out).
            var exported = new HashSet<long>();
            foreach (var node in order)
            {
                var parent = Scene.ParentOf(node);
                bool r;
                if ((parent == null || exported.Contains(parent.Key)) && relevant.TryGetValue(node.Key, out r) && r)
                {
                    exported.Add(node.Key);
                }
            }

            // The GLB's nodes in the export's order, each with the name the glTF export gives it.
            var written = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in order)
            {
                if (!exported.Contains(node.Key))
                {
                    continue;
                }
                var maxName = node.Name ?? string.Empty;
                var name = maxName;
                int count;
                if (written.TryGetValue(maxName, out count))
                {
                    name = maxName + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";
                    written[maxName] = count + 1;
                }
                else
                {
                    written[maxName] = 1;
                }
                var model = new PlannerModelNode { Key = node.Key, Name = name, MaxName = maxName, Mesh = self[node.Key] && !node.IsHelper };
                Nodes.Add(model);
                ByKey[node.Key] = model;
            }
            foreach (var node in Scene.Nodes)
            {
                if (exported.Contains(node.Key))
                {
                    continue;
                }
                if (node.NoExport)
                {
                    LeftOut[node.Key] = NoExportReason;
                }
                else if (node.Hidden && !exportHidden)
                {
                    LeftOut[node.Key] = HiddenReason;
                }
            }
            foreach (var model in Nodes)
            {
                var parent = Scene.ParentOf(Scene.NodeByKey[model.Key]);
                PlannerModelNode up;
                model.Parent = parent != null && ByKey.TryGetValue(parent.Key, out up) ? up : null;
            }
        }

        /// <summary>The nodes the export gives another name than their own (a node written before them has it).</summary>
        public IEnumerable<PlannerModelNode> RenamedByExport
        {
            get { return Nodes.Where(n => n.RenamedByExport); }
        }

        // A node and everything linked under it, depth first, each node before the nodes under it, those in the scene's
        // order. Without recursion, so a deep hierarchy cannot overflow the stack.
        private List<PlannerSceneNode> DepthFirst(PlannerSceneNode root, HashSet<long> seen)
        {
            var result = new List<PlannerSceneNode>();
            var pending = new Stack<PlannerSceneNode>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!seen.Add(node.Key))
                {
                    continue;
                }
                result.Add(node);
                var children = Scene.ChildrenOf(node);
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    pending.Push(children[i]);
                }
            }
            return result;
        }

        // A top-level tree the export takes with the meshes' (geometry: not a helper, not a shape).
        private static bool HoldsGeometry(List<PlannerSceneNode> tree)
        {
            return tree.Any(n => !n.IsHelper && !n.IsShape);
        }
    }

    /// <summary>When the Planner's Generate from model is run: the time zone it runs in and the workspace's start.</summary>
    public sealed class PlannerScheduleOptions
    {
        /// <summary>The browser's time zone: dates are local midnights, as JavaScript reads them.</summary>
        public TimeZoneInfo Zone = TimeZoneInfo.Local;
        /// <summary>
        /// The workspace's start before Generate, which moves it back to the earliest dated layer when that is earlier. Null:
        /// a workspace that starts on the earliest dated layer (the start of the works).
        /// </summary>
        public DateTime? WorkspaceStart;
        /// <summary>The start when no layer has a date at all and no workspace start is given.</summary>
        public DateTime Today = DateTime.Today;
    }

    /// <summary>A layer as Generate from model reads it: a dated group of the hierarchy, known by its label.</summary>
    public sealed class PlannerScheduleLayer
    {
        public string Label;
        /// <summary>The first raw name seen for the label (issues are reported against it).</summary>
        public string Raw;
        public PlannerLayerName Parsed;
        /// <summary>The phase its ID names, else the one most of its objects' tags of its stage carry; null when none.</summary>
        public int? Phase;
        /// <summary>The phase group (_&lt;n&gt;_&lt;name&gt;) its folder is filed in, or null.</summary>
        public string Group;
        /// <summary>The GLB nodes with this label that hold its objects: the layer's helpers.</summary>
        public readonly List<long> NodeKeys = new List<long>();
        /// <summary>Its first and last day; an undated layer's are the project start.</summary>
        public DateTime Start;
        public DateTime End;
        /// <summary>Its position in the Planner's order (phase and stage, then label).</summary>
        public int Order;

        public bool Dated
        {
            get { return Parsed.Start.HasValue; }
        }

        public bool Tbc
        {
            get { return Parsed.Tbc; }
        }

        internal readonly List<PlannerSchedule.Member> Members = new List<PlannerSchedule.Member>();
        internal readonly List<PlannerSchedule.Seg> OwnSegs = new List<PlannerSchedule.Seg>();
        internal readonly List<PlannerSchedule.Seg> ForeignSegs = new List<PlannerSchedule.Seg>();
        // (phase, the layer's stage) for each own segment, and a two-number layer's ID, in the order first owned.
        internal readonly List<KeyValuePair<string, PlannerSchedule.StageOwner>> OwnStages = new List<KeyValuePair<string, PlannerSchedule.StageOwner>>();
    }

    /// <summary>One activity Generate from model makes, with its place in time.</summary>
    public sealed class PlannerScheduleActivity
    {
        public string Name;
        public PlannerTaskType Type;
        /// <summary>The layer whose folder holds it.</summary>
        public PlannerScheduleLayer Home;
        /// <summary>Its home layer's own work, spread over the layer's dates; else anchored to another stage's layer.</summary>
        public bool IsOwn;
        /// <summary>The layer whose dates place it: its home for its own work, the dated layer of its stage for another's; null when placed by rule.</summary>
        public PlannerScheduleLayer DatedBy;
        /// <summary>No dated layer has its stage: a removal is parked the day after the works end, an install sits at the project start.</summary>
        public bool Unresolved;
        public bool Tbc;
        /// <summary>The tag it reads (its first object's).</summary>
        public PlannerMeshSegment Segment;
        /// <summary>Minutes from the project start, as the scheduler chains them.</summary>
        public long StartMinute;
        public long Duration;
        /// <summary>The instants the 4D view reads, epoch milliseconds: its start (an install shows) and finish (a removal hides).</summary>
        public long StartMs;
        public long EndMs;
        /// <summary>The objects it binds, in the Planner's order.</summary>
        public readonly List<string> Members = new List<string>();

        /// <summary>"Ph1_St04_2": the tag's phase and stage, as the activity's name writes them.</summary>
        public string StageText
        {
            get { return "Ph" + Segment.Phase.ToString(CultureInfo.InvariantCulture) + "_" + PlannerSchedule.StageTag(Segment); }
        }
    }

    /// <summary>One object the Planner schedules, and the activities that bind it.</summary>
    public sealed class PlannerScheduleObject
    {
        /// <summary>Its name in the GLB, as the Planner reads it.</summary>
        public string Name;
        /// <summary>Its node's name in 3ds Max: the GLB's, unless the export added " (n)" (another node has the name).</summary>
        public string MaxName;
        /// <summary>The scene node the Planner shows and hides for this name.</summary>
        public long Key;
        public PlannerMeshName Parsed;
        /// <summary>The layer it is filed in (the dated group above it).</summary>
        public PlannerScheduleLayer FiledIn;
        public readonly List<PlannerScheduleActivity> Activities = new List<PlannerScheduleActivity>();
    }

    /// <summary>A change of an object's state in the 4D view.</summary>
    public struct PlannerScheduleEvent
    {
        /// <summary>Epoch milliseconds.</summary>
        public long Instant;
        public bool Install;
    }

    /// <summary>
    /// What Generate from model (layer mode) makes of the scene, and what the Planner's 4D view shows at any instant: the
    /// layers, every activity with its dates, each object's installs and removals, and the objects it does not schedule,
    /// with why. A line-by-line port of the Planner's buildPhasingFromModel and buildFromLayers (apps/web/src/lib/
    /// phasingFromModel.ts), the viewport's name index and its layer, group and split indexes (features/viewport/
    /// nodeNames.ts, meshes.ts), the store's start date (Generate moves it back to the earliest dated layer), the scheduler
    /// (state/schedule.ts) and the 4D events (state/timeline.ts buildMeshEvents, meshVisibilityFromEvents), run on the
    /// model the export would write. Orders follow the Planner's natural order (<see cref="PlannerNaturalOrder"/>) and
    /// JavaScript's stable sorts; dates are local midnights in the given time zone, as the browser reads them. Pure logic,
    /// no 3ds Max.
    /// </summary>
    public sealed class PlannerSchedule
    {
        /// <summary>Set when Generate would not run in layer mode, saying why; the layers and activities are then empty.</summary>
        public string Message;
        public PlannerScheduleModel Model;
        public TimeZoneInfo Zone;
        /// <summary>The project start Generate dates against (minute 0).</summary>
        public DateTime ProjectStart;
        public long AnchorMs;
        /// <summary>The last day a dated layer ends; removals no dated layer has are parked the day after.</summary>
        public DateTime? WorksEnd;
        /// <summary>The layers, in the Planner's order.</summary>
        public readonly List<PlannerScheduleLayer> Layers = new List<PlannerScheduleLayer>();
        /// <summary>The activities, in the Planner's (name) order.</summary>
        public readonly List<PlannerScheduleActivity> Activities = new List<PlannerScheduleActivity>();
        public readonly Dictionary<string, PlannerScheduleObject> Objects = new Dictionary<string, PlannerScheduleObject>(StringComparer.Ordinal);
        /// <summary>Objects the Planner does not schedule (under the phasing root, or tagged anywhere), with why.</summary>
        public readonly List<PlannerNotScheduled> NotScheduled = new List<PlannerNotScheduled>();
        /// <summary>Problems with layer names, as the Planner lists them ("layer: issue").</summary>
        public readonly List<string> LayerIssues = new List<string>();
        /// <summary>The names the Planner's index holds, in its order (meshes first), and the node each stands for.</summary>
        public readonly List<string> Names = new List<string>();
        public readonly Dictionary<string, PlannerModelNode> Index = new Dictionary<string, PlannerModelNode>(StringComparer.Ordinal);
        /// <summary>Every name the Planner reads under a dated layer (its layerIndex): the name and the layer's raw name.</summary>
        public readonly Dictionary<string, string> LayerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The 4D events of each bound name, earliest first, installs before removals at one instant.</summary>
        public readonly Dictionary<string, List<PlannerScheduleEvent>> Events = new Dictionary<string, List<PlannerScheduleEvent>>(StringComparer.Ordinal);

        public bool Usable
        {
            get { return Message == null; }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Building

        public static PlannerSchedule Build(PlannerSceneSnapshot snapshot, PlannerScheduleOptions options)
        {
            return Build(new PlannerScheduleModel(snapshot), options);
        }

        public static PlannerSchedule Build(PlannerScheduleModel model, PlannerScheduleOptions options)
        {
            options = options ?? new PlannerScheduleOptions();
            var schedule = new PlannerSchedule { Model = model, Zone = options.Zone ?? TimeZoneInfo.Local };
            schedule.ReadIndex();
            var layers = schedule.LayerIndex();
            var groups = schedule.LayerGroupIndex(layers);
            var splits = schedule.SplitIndex();
            var names = splits.Count > 0 ? ToObjects(schedule.Names, splits) : new List<string>(schedule.Names);

            // The store's Generate: the project start moves back to the earliest dated layer anything sits under.
            DateTime? earliest = null;
            foreach (var raw in layers.Values.Distinct(StringComparer.Ordinal))
            {
                var parsed = PlannerNaming.ParseLayerName(raw);
                if (parsed != null && parsed.Start.HasValue && (!earliest.HasValue || parsed.Start.Value < earliest.Value))
                {
                    earliest = parsed.Start.Value;
                }
            }
            var start = options.WorkspaceStart.HasValue ? options.WorkspaceStart.Value.Date : (earliest ?? options.Today.Date);
            if (earliest.HasValue && earliest.Value < start)
            {
                start = earliest.Value;
            }
            schedule.ProjectStart = start;
            schedule.AnchorMs = schedule.Midnight(start);

            if (layers.Count > 0)
            {
                schedule.BuildFromLayers(names, layers, groups);
            }
            if (schedule.Objects.Count == 0)
            {
                schedule.Message = layers.Count == 0
                    ? "No object sits under a dated layer (a group named N_<Activity>_DD-MM-YY), so Generate from model would fall back to its older stage counter (segment mode), which the Schedule tab does not show."
                    : "No tagged object (Ph<n>_St<nn>_IN|RM_...) sits under a dated layer, so Generate from model would fall back to its older stage counter (segment mode), which the Schedule tab does not show.";
                schedule.Layers.Clear();
                schedule.Activities.Clear();
                schedule.Events.Clear();
                schedule.WorksEnd = null;
            }
            schedule.ListNotScheduled();
            return schedule;
        }

        // The viewport's name index: meshes first, then the other nodes, the first node of a name winning.
        private void ReadIndex()
        {
            foreach (var node in Model.Nodes.Where(n => n.Mesh))
            {
                if (!Index.ContainsKey(node.Name))
                {
                    Index[node.Name] = node;
                    Names.Add(node.Name);
                }
            }
            foreach (var node in Model.Nodes.Where(n => !n.Mesh && n.Name.Length > 0))
            {
                if (!Index.ContainsKey(node.Name))
                {
                    Index[node.Name] = node;
                    Names.Add(node.Name);
                }
            }
        }

        private const int MaxDepth = 64;
        private static readonly Regex SplitSuffix = new Regex(@"_(?:primitive)?[0-9]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PrimitiveSuffix = new Regex(@"_primitive[0-9]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // nodeNames.ts isAncestorNamed.
        private bool IsAncestorNamed(string ancestor, string of)
        {
            PlannerModelNode node;
            Index.TryGetValue(of, out node);
            int hops = 0;
            for (var p = node != null ? node.Parent : null; p != null && hops < MaxDepth; p = p.Parent, hops++)
            {
                if (p.Name == ancestor)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The object a split piece (Name_N, Name_primitiveN under Name) belongs to, or the name itself (meshes.ts mergedName).</summary>
        public string MergedName(string name)
        {
            var current = name;
            while (SplitSuffix.IsMatch(current))
            {
                var stripped = SplitSuffix.Replace(current, string.Empty, 1);
                if (stripped.Length == 0 || !Index.ContainsKey(stripped) || !IsAncestorNamed(stripped, name))
                {
                    break;
                }
                current = stripped;
            }
            return current;
        }

        // Group names read once each: the same few are read for every object under them.
        private readonly Dictionary<string, PlannerLayerName> layerNames = new Dictionary<string, PlannerLayerName>(StringComparer.Ordinal);

        private PlannerLayerName LayerName(string raw)
        {
            PlannerLayerName parsed;
            if (!layerNames.TryGetValue(raw, out parsed))
            {
                parsed = PlannerNaming.ParseLayerName(raw);
                layerNames[raw] = parsed;
            }
            return parsed;
        }

        // nodeNames.ts layerIndex: each named object's nearest ancestor that reads as a layer.
        private Dictionary<string, string> LayerIndex()
        {
            foreach (var name in Names)
            {
                if (MergedName(name) != name)
                {
                    continue;
                }
                int hops = 0;
                for (var p = Index[name].Parent; p != null && hops < MaxDepth; p = p.Parent, hops++)
                {
                    if (p.Name.Length > 0 && p.Name != name && LayerName(p.Name) != null)
                    {
                        LayerOf[name] = p.Name;
                        break;
                    }
                }
            }
            return LayerOf;
        }

        // nodeNames.ts layerGroupIndex: each layer's nearest phase group above it, and each group's.
        private Dictionary<string, string> LayerGroupIndex(Dictionary<string, string> layers)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var todo = layers.Values.Distinct(StringComparer.Ordinal).ToList();
            var seen = new HashSet<string>(todo, StringComparer.Ordinal);
            for (int i = 0; i < todo.Count; i++)
            {
                PlannerModelNode node;
                Index.TryGetValue(todo[i], out node);
                string group = null;
                int hops = 0;
                for (var p = node != null ? node.Parent : null; p != null && hops < MaxDepth; p = p.Parent, hops++)
                {
                    if (p.Name.Length > 0 && ParsePhaseGroup(p.Name) != null)
                    {
                        group = p.Name;
                        break;
                    }
                }
                if (group == null)
                {
                    continue;
                }
                result[todo[i]] = group;
                if (seen.Add(group))
                {
                    todo.Add(group);
                }
            }
            return result;
        }

        // nodeNames.ts splitIndex: the split pieces (Name_N under Name) and the object each belongs to.
        private Dictionary<string, string> SplitIndex()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in Names)
            {
                if (PrimitiveSuffix.IsMatch(name))
                {
                    continue;
                }
                var merged = MergedName(name);
                if (merged != name)
                {
                    result[name] = merged;
                }
            }
            return result;
        }

        // phasingFromModel.ts toObjects: material shards into their part, split pieces into their object, deduped.
        internal static List<string> ToObjects(IList<string> names, Dictionary<string, string> splits)
        {
            var present = new HashSet<string>(names, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var raw in names)
            {
                var part = PrimitiveParent(raw, present);
                string merged;
                var obj = splits != null && splits.TryGetValue(part, out merged) ? merged : part;
                if (seen.Add(obj))
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        private static string PrimitiveParent(string name, HashSet<string> present)
        {
            var current = name;
            while (PrimitiveSuffix.IsMatch(current))
            {
                var stripped = PrimitiveSuffix.Replace(current, string.Empty, 1);
                if (stripped.Length == 0 || !present.Contains(stripped))
                {
                    break;
                }
                current = stripped;
            }
            return current;
        }

        private static readonly Regex DupSuffix = new Regex(PlannerNaming.JavaScriptSpace + @"*\([0-9]+\)" + PlannerNaming.JavaScriptSpace + @"*\z", RegexOptions.CultureInvariant);
        // JavaScript's "." matches anything but a line end: \n, \r, U+2028 and U+2029.
        private static readonly Regex PhaseGroupName = new Regex(@"^_([0-9]+)_([^\n\r" + (char)0x2028 + (char)0x2029 + @"]+)\z", RegexOptions.CultureInvariant);

        /// <summary>The Planner's parsePhaseGroupName: a phase group's folder label (_&lt;n&gt;_&lt;name&gt;), or null.</summary>
        public static string ParsePhaseGroup(string raw)
        {
            if (raw == null)
            {
                return null;
            }
            var label = DupSuffix.Replace(raw, string.Empty, 1).Trim(PlannerNaming.JavaScriptSpaceChars);
            return PhaseGroupName.IsMatch(label) ? label : null;
        }

        // ---------------------------------------------------------------------------------------------------------
        // buildFromLayers

        internal sealed class Member
        {
            public string Part;
            public PlannerMeshName Parsed;
        }

        internal sealed class Seg
        {
            public Member Member;
            public PlannerMeshSegment Segment;
        }

        internal sealed class StageOwner
        {
            public int Phase;
            public int Stage;
            public int? SubStage;
            public PlannerScheduleLayer Layer;
        }

        private sealed class Occurrence
        {
            public PlannerScheduleLayer Layer;
            public PlannerMeshSegment Segment;
            public Member Member;
            public bool IsOwn;
        }

        private sealed class Planned
        {
            public PlannerScheduleLayer Home;
            public bool IsOwn;
            public PlannerMeshSegment Segment;
            public List<Member> Members;
            public string Name;
        }

        private sealed class Resolved
        {
            public DateTime At;
            public bool Tbc;
            public bool Exact;
            public PlannerScheduleLayer Layer;
        }

        // The Planner's issues write an em dash.
        private static readonly string Dash = ((char)0x2014).ToString();

        private void BuildFromLayers(List<string> names, Dictionary<string, string> layersByNode, Dictionary<string, string> groupsByNode)
        {
            var parts = ToObjects(names, null);
            var byLabel = new Dictionary<string, PlannerScheduleLayer>(StringComparer.Ordinal);
            var inOrder = new List<PlannerScheduleLayer>();
            var warnedDupes = new HashSet<string>(StringComparer.Ordinal);
            Func<string, string> groupOf = rawLayer =>
            {
                string raw;
                return groupsByNode.TryGetValue(rawLayer, out raw) && ParsePhaseGroup(raw) != null ? raw : null;
            };

            // 2. Tagged parts under their layer's label.
            foreach (var part in parts)
            {
                var mesh = PlannerNaming.ParseMeshName(part);
                if (mesh == null)
                {
                    continue;
                }
                string rawLayer;
                var layer = layersByNode.TryGetValue(part, out rawLayer) ? LayerName(rawLayer) : null;
                if (layer == null)
                {
                    continue;
                }
                PlannerScheduleLayer g;
                if (!byLabel.TryGetValue(layer.Label, out g))
                {
                    g = new PlannerScheduleLayer { Label = layer.Label, Raw = rawLayer, Parsed = layer, Group = groupOf(rawLayer) };
                    byLabel[layer.Label] = g;
                    inOrder.Add(g);
                    foreach (var issue in layer.Issues)
                    {
                        LayerIssues.Add(rawLayer + ": " + issue);
                    }
                }
                else if (g.Group == null && rawLayer != g.Raw)
                {
                    g.Group = groupOf(rawLayer);
                }
                if (rawLayer != g.Raw && (layer.Start != g.Parsed.Start || layer.End != g.Parsed.End) && warnedDupes.Add(rawLayer))
                {
                    LayerIssues.Add(rawLayer + ": same name as \"" + g.Raw + "\" but different dates " + Dash + " using the first");
                }
                PlannerModelNode layerNode;
                if (Index.TryGetValue(rawLayer, out layerNode) && !g.NodeKeys.Contains(layerNode.Key))
                {
                    g.NodeKeys.Add(layerNode.Key);
                }
                g.Members.Add(new Member { Part = part, Parsed = mesh });
            }

            // 3. Each segment OWN (its stage is the layer's, in the layer's phase) or FOREIGN (another stage's work).
            var strays = new List<KeyValuePair<PlannerScheduleLayer, Member>>();
            foreach (var g in inOrder)
            {
                int stage = g.Parsed.Stage;
                int? sub = g.Parsed.SubStage;
                var leadPhases = g.Members.Where(m => PlannerNaming.SameStage(m.Parsed.Lead.Stage, m.Parsed.Lead.SubStage, stage, sub)).Select(m => m.Parsed.Lead.Phase).ToList();
                var trailPhases = g.Members.Where(m => m.Parsed.Trail != null && PlannerNaming.SameStage(m.Parsed.Trail.Stage, m.Parsed.Trail.SubStage, stage, sub))
                    .Select(m => m.Parsed.Trail.Phase).ToList();
                g.Phase = g.Parsed.Phase ?? Majority(leadPhases) ?? Majority(trailPhases);
                var layerPhase = g.Phase;
                var layer = g;
                Func<PlannerMeshSegment, bool> isOwn = s => PlannerNaming.SameStage(s.Stage, s.SubStage, stage, sub) && (!layerPhase.HasValue || s.Phase == layerPhase.Value);
                Action<Member, PlannerMeshSegment> own = (m, s) =>
                {
                    layer.OwnSegs.Add(new Seg { Member = m, Segment = s });
                    SetOwnStage(layer, s.Phase, stage, sub);
                };
                Action<Member, PlannerMeshSegment> foreign = (m, s) => layer.ForeignSegs.Add(new Seg { Member = m, Segment = s });
                if (g.Parsed.Phase.HasValue)
                {
                    SetOwnStage(g, g.Parsed.Phase.Value, stage, sub);
                }
                foreach (var m in g.Members)
                {
                    var lead = m.Parsed.Lead;
                    var trail = m.Parsed.Trail;
                    bool leadOwn = isOwn(lead);
                    bool trailOwn = trail != null && isOwn(trail);
                    if (!leadOwn && !trailOwn)
                    {
                        // A stray: its leading segment is dated once every layer's own work is known.
                        strays.Add(new KeyValuePair<PlannerScheduleLayer, Member>(g, m));
                        if (trail != null)
                        {
                            foreign(m, trail);
                        }
                        continue;
                    }
                    if (leadOwn)
                    {
                        own(m, lead);
                    }
                    else
                    {
                        foreign(m, lead);
                    }
                    if (trail != null)
                    {
                        if (trailOwn)
                        {
                            own(m, trail);
                        }
                        else
                        {
                            foreign(m, trail);
                        }
                    }
                }
            }

            // 4. Which dated layers own each (phase, stage), and the end of the works.
            var owned = new List<StageOwner>();
            DateTime? worksEnd = null;
            foreach (var g in inOrder)
            {
                if (!g.Parsed.Start.HasValue)
                {
                    continue;
                }
                foreach (var o in g.OwnStages)
                {
                    owned.Add(new StageOwner { Phase = o.Value.Phase, Stage = o.Value.Stage, SubStage = o.Value.SubStage, Layer = g });
                }
                var end = (g.Parsed.End ?? g.Parsed.Start).Value;
                if (!worksEnd.HasValue || end > worksEnd.Value)
                {
                    worksEnd = end;
                }
            }
            WorksEnd = worksEnd;
            // The dated layer owning a segment's (phase, stage): the very sub-stage first, then the earliest start.
            Func<PlannerMeshSegment, Resolved> resolve = s =>
            {
                Resolved best = null;
                foreach (var o in owned)
                {
                    if (o.Phase != s.Phase || !PlannerNaming.SameStage(o.Stage, o.SubStage, s.Stage, s.SubStage))
                    {
                        continue;
                    }
                    bool exact = o.SubStage == s.SubStage;
                    var d = o.Layer.Parsed.Start.Value;
                    if (best == null || (exact && !best.Exact) || (exact == best.Exact && d < best.At))
                    {
                        best = new Resolved { At = d, Tbc = o.Layer.Parsed.Tbc, Exact = exact, Layer = o.Layer };
                    }
                }
                return best;
            };
            foreach (var stray in strays)
            {
                var seg = new Seg { Member = stray.Value, Segment = stray.Value.Parsed.Lead };
                if (resolve(stray.Value.Parsed.Lead) != null)
                {
                    stray.Key.ForeignSegs.Add(seg);
                }
                else
                {
                    stray.Key.OwnSegs.Add(seg);
                }
            }

            // 5. Plan every activity with its name: one per (description, phase, stage, type) across the whole model.
            var sortedLayers = PlannerNaturalOrder.StableSort(inOrder, (x, y) =>
            {
                double rx = LayerRank(x.Parsed);
                double ry = LayerRank(y.Parsed);
                if (rx != ry)
                {
                    return rx < ry ? -1 : 1;
                }
                return PlannerNaturalOrder.Compare(SpellingKey(x.Parsed.Label), SpellingKey(y.Parsed.Label));
            });
            for (int i = 0; i < sortedLayers.Count; i++)
            {
                sortedLayers[i].Order = i;
            }
            var occurrences = new Dictionary<string, List<Occurrence>>(StringComparer.Ordinal);
            var occurrenceKeys = new List<string>();
            foreach (var g in sortedLayers)
            {
                var layer = g;
                Action<Seg, bool> add = (s, isOwn) =>
                {
                    var key = StemOf(layer, s.Member) + "|" + SegKey(s.Segment) + "|" + TypeToken(s.Segment.Type);
                    List<Occurrence> list;
                    if (!occurrences.TryGetValue(key, out list))
                    {
                        list = new List<Occurrence>();
                        occurrences[key] = list;
                        occurrenceKeys.Add(key);
                    }
                    list.Add(new Occurrence { Layer = layer, Segment = s.Segment, Member = s.Member, IsOwn = isOwn });
                };
                foreach (var s in g.OwnSegs)
                {
                    add(s, true);
                }
                foreach (var s in g.ForeignSegs)
                {
                    add(s, false);
                }
            }
            var all = new List<Planned>();
            foreach (var key in occurrenceKeys)
            {
                var occ = occurrences[key];
                var owners = occ.Where(o => o.IsOwn).ToList();
                var first = owners.Count > 0 ? owners[0] : occ[0];
                var unique = new List<Member>();
                var seenParts = new HashSet<string>(StringComparer.Ordinal);
                foreach (var o in occ)
                {
                    if (seenParts.Add(o.Member.Part))
                    {
                        unique.Add(o.Member);
                    }
                }
                var planned = new Planned
                {
                    Home = first.Layer,
                    IsOwn = owners.Count > 0,
                    Segment = first.Segment,
                    Members = PlannerNaturalOrder.StableSort(unique, (a, b) => PartCompare(a.Part, b.Part)),
                    Name = NameOf(StemOf(first.Layer, first.Member), first.Segment)
                };
                all.Add(planned);
                foreach (var other in owners.Skip(1).Select(o => o.Layer).Distinct())
                {
                    if (other != first.Layer)
                    {
                        LayerIssues.Add(other.Raw + ": its \"" + planned.Name + "\" is the same work (description, stage and phase) as in \"" + first.Layer.Raw + "\" " + Dash + " merged into that layer");
                    }
                }
            }

            // 6. Emit, one layer at a time in order.
            var anchor = ProjectStart;
            foreach (var g in sortedLayers)
            {
                var layer = g;
                var own = all.Where(p => p.Home == layer && p.IsOwn).ToList();
                var foreignPlans = all.Where(p => p.Home == layer && !p.IsOwn).ToList();
                // Removal-only objects first, then by object name, an install before its own object's removal.
                var installParts = new HashSet<string>(own.Where(p => p.Segment.Type == PlannerTaskType.Install).SelectMany(p => p.Members.Select(m => m.Part)), StringComparer.Ordinal);
                own = PlannerNaturalOrder.StableSort(own, (x, y) =>
                {
                    int ix = x.Members.Any(m => installParts.Contains(m.Part)) ? 1 : 0;
                    int iy = y.Members.Any(m => installParts.Contains(m.Part)) ? 1 : 0;
                    if (ix != iy)
                    {
                        return ix - iy;
                    }
                    int c = PartCompare(x.Members[0].Part, y.Members[0].Part);
                    if (c != 0)
                    {
                        return c;
                    }
                    if (x.Segment.Type == y.Segment.Type)
                    {
                        return 0;
                    }
                    return x.Segment.Type == PlannerTaskType.Install ? -1 : 1;
                });
                foreignPlans = PlannerNaturalOrder.StableSort(foreignPlans, (x, y) =>
                {
                    int c = PlannerNaturalOrder.Compare(SegKey(x.Segment), SegKey(y.Segment));
                    return c != 0 ? c : PartCompare(x.Members[0].Part, y.Members[0].Part);
                });

                var parsed = g.Parsed;
                var startD = parsed.Start ?? anchor;
                var endD = parsed.End ?? startD;
                g.Start = startD;
                g.End = endD;
                long totalMin = MinutesBetween(startD, endD.AddDays(1));
                long startOff = MinutesBetween(anchor, startD);
                int n = own.Count;
                long each = n > 0 ? Math.Max(1, totalMin / n) : 0;
                PlannerScheduleActivity previous = null;
                for (int i = 0; i < n; i++)
                {
                    // Spread evenly; the last one takes the rounding, so the folder ends on the layer's finish.
                    long duration = i == n - 1 ? totalMin - each * (n - 1) : each;
                    var activity = Make(own[i], layer, duration, parsed.Tbc);
                    activity.IsOwn = true;
                    activity.DatedBy = layer;
                    activity.StartMinute = previous == null ? startOff : previous.StartMinute + previous.Duration;
                    previous = activity;
                }
                foreach (var p in foreignPlans)
                {
                    var s = p.Segment;
                    var owner = resolve(s);
                    DateTime at;
                    bool unresolved = false;
                    if (owner != null)
                    {
                        at = owner.At;
                    }
                    else if (s.Type == PlannerTaskType.Dismantle)
                    {
                        // Nobody owns that stage: parked the day after the works end, flagged.
                        at = (worksEnd ?? endD).AddDays(1);
                        unresolved = true;
                    }
                    else
                    {
                        // An install nobody owns sits at the project start; phase 0 is there before the works began.
                        at = anchor;
                        unresolved = s.Phase != 0;
                    }
                    bool tbc = parsed.Tbc || unresolved || (owner != null && owner.Tbc);
                    var activity = Make(p, layer, MinutesBetween(at, at.AddDays(1)), tbc);
                    activity.IsOwn = false;
                    activity.DatedBy = owner != null ? owner.Layer : null;
                    activity.Unresolved = owner == null;
                    activity.StartMinute = MinutesBetween(anchor, at);
                }
            }
            Layers.AddRange(sortedLayers);

            // The scheduler's dates, and the instants the 4D view reads from them (state/schedule.ts; domain startMs).
            foreach (var activity in Activities)
            {
                var local = LocalTime(AnchorMs + activity.StartMinute * 60000L);
                activity.StartMs = Midnight(local.Date) + (long)(local.Hour * 60 + local.Minute) * 60000L;
                activity.EndMs = activity.StartMs + activity.Duration * 60000L;
            }
            var byName = PlannerNaturalOrder.StableSort(Activities, (a, b) => PlannerNaturalOrder.Compare(a.Name, b.Name));
            Activities.Clear();
            Activities.AddRange(byName);

            // The objects and their 4D events (timeline.ts buildMeshEvents).
            foreach (var g in sortedLayers)
            {
                foreach (var m in g.Members)
                {
                    if (!Objects.ContainsKey(m.Part))
                    {
                        PlannerModelNode node;
                        Index.TryGetValue(m.Part, out node);
                        Objects[m.Part] = new PlannerScheduleObject
                        {
                            Name = m.Part,
                            MaxName = node != null ? node.MaxName : m.Part,
                            Key = node != null ? node.Key : 0,
                            Parsed = m.Parsed,
                            FiledIn = g
                        };
                    }
                }
            }
            foreach (var activity in Activities)
            {
                bool install = activity.Type == PlannerTaskType.Install;
                var ev = new PlannerScheduleEvent { Instant = install ? activity.StartMs : activity.EndMs, Install = install };
                foreach (var name in activity.Members)
                {
                    List<PlannerScheduleEvent> list;
                    if (!Events.TryGetValue(name, out list))
                    {
                        list = new List<PlannerScheduleEvent>();
                        Events[name] = list;
                    }
                    list.Add(ev);
                    PlannerScheduleObject obj;
                    if (Objects.TryGetValue(name, out obj))
                    {
                        obj.Activities.Add(activity);
                    }
                }
            }
            foreach (var name in Events.Keys.ToList())
            {
                Events[name] = PlannerNaturalOrder.StableSort(Events[name], (x, y) =>
                {
                    if (x.Instant != y.Instant)
                    {
                        return x.Instant < y.Instant ? -1 : 1;
                    }
                    return x.Install == y.Install ? 0 : x.Install ? -1 : 1;
                });
            }
        }

        private PlannerScheduleActivity Make(Planned p, PlannerScheduleLayer home, long duration, bool tbc)
        {
            var activity = new PlannerScheduleActivity
            {
                Name = p.Name,
                Type = p.Segment.Type,
                Home = home,
                Segment = p.Segment,
                Duration = Math.Max(1, duration),
                Tbc = tbc
            };
            activity.Members.AddRange(p.Members.Select(m => m.Part));
            Activities.Add(activity);
            return activity;
        }

        private static void SetOwnStage(PlannerScheduleLayer layer, int phase, int stage, int? sub)
        {
            var key = phase.ToString(CultureInfo.InvariantCulture) + "|" + StageLabel(stage, sub);
            var owner = new StageOwner { Phase = phase, Stage = stage, SubStage = sub };
            for (int i = 0; i < layer.OwnStages.Count; i++)
            {
                if (layer.OwnStages[i].Key == key)
                {
                    layer.OwnStages[i] = new KeyValuePair<string, StageOwner>(key, owner);
                    return;
                }
            }
            layer.OwnStages.Add(new KeyValuePair<string, StageOwner>(key, owner));
        }

        // The most common value, the lowest on a tie.
        private static int? Majority(List<int> values)
        {
            if (values.Count == 0)
            {
                return null;
            }
            return values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        }

        // phaseStageRank / stageRank: phases first, then stages and sub-stages; a one-number layer by its stage alone.
        private static double LayerRank(PlannerLayerName p)
        {
            double stageRank = p.Stage + (p.SubStage ?? 0) / 1000.0;
            return p.Phase.HasValue ? p.Phase.Value * 10000 + stageRank : stageRank;
        }

        // The layer's label without its first number: what a mesh with no description is named after.
        private static string LabelTail(PlannerLayerName p)
        {
            return p.Label == p.Order ? string.Empty : p.Label.Substring(p.Order.Length + 1);
        }

        private static readonly Regex EdgeUnderscores = new Regex(@"^_+|_+\z", RegexOptions.CultureInvariant);

        private static string StemOf(PlannerScheduleLayer layer, Member m)
        {
            var description = EdgeUnderscores.Replace(m.Parsed.Description, string.Empty);
            if (description.Length > 0)
            {
                return description;
            }
            var tail = LabelTail(layer.Parsed);
            return tail.Length > 0 ? tail : layer.Parsed.Label;
        }

        /// <summary>"3", or "3_2": a stage as the Planner keys it.</summary>
        public static string StageLabel(int stage, int? sub)
        {
            return sub.HasValue
                ? stage.ToString(CultureInfo.InvariantCulture) + "_" + sub.Value.ToString(CultureInfo.InvariantCulture)
                : stage.ToString(CultureInfo.InvariantCulture);
        }

        private static string SegKey(PlannerMeshSegment s)
        {
            return s.Phase.ToString(CultureInfo.InvariantCulture) + "|" + StageLabel(s.Stage, s.SubStage);
        }

        private static string TypeToken(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "IN" : "RM";
        }

        private static readonly Regex OldSubStageSeparator = new Regex(@"[.-]", RegexOptions.CultureInvariant);

        /// <summary>"St04", "St04_2": a tag's stage as written, its sub-stage after the underscore (the Planner's stTag).</summary>
        public static string StageTag(PlannerMeshSegment s)
        {
            var written = "St" + s.StageToken;
            return s.Legacy ? OldSubStageSeparator.Replace(written, "_", 1) : written;
        }

        private static string NameOf(string stem, PlannerMeshSegment s)
        {
            return stem + "_Ph" + s.Phase.ToString(CultureInfo.InvariantCulture) + "_" + StageTag(s) + "_" + TypeToken(s.Type);
        }

        private static int PartCompare(string x, string y)
        {
            return PlannerNaturalOrder.Compare(CachedSpellingKey(x), CachedSpellingKey(y));
        }

        // Sorts ask for the same names' keys many times: kept, per thread, for a while.
        [ThreadStatic] private static Dictionary<string, string> spellingKeys;

        private static string CachedSpellingKey(string name)
        {
            if (spellingKeys == null || spellingKeys.Count > 200000)
            {
                spellingKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            string key;
            if (!spellingKeys.TryGetValue(name, out key))
            {
                key = SpellingKey(name);
                spellingKeys[name] = key;
            }
            return key;
        }

        private static readonly Regex TagSubStage = new Regex(@"(^|_)(Ph[0-9]+_St[0-9]+)[.-]([0-9]+)(?=_|" + PlannerNaming.JavaScriptSpace + @"|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LayerDotSubStage = new Regex(@"^([0-9]{1,2}[_ ]+[0-9]{1,2}|[0-9]+)\.([0-9]+)(?=_|\z)", RegexOptions.CultureInvariant);

        /// <summary>The Planner's spellingKey: every sub-stage and every other hyphen written as an underscore.</summary>
        public static string SpellingKey(string name)
        {
            var text = LayerDotSubStage.Replace(name, "$1_$2", 1);
            text = TagSubStage.Replace(text, "$1$2_$3");
            return text.Replace('-', '_');
        }

        // ---------------------------------------------------------------------------------------------------------
        // Time, as JavaScript's Date reads it in the browser's zone

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The local midnight a day starts at, epoch milliseconds (JavaScript's new Date(y, m, d)).</summary>
        public long Midnight(DateTime day)
        {
            var local = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified);
            // A midnight the clocks skip is the first minute after the gap, as JavaScript moves it.
            int guard = 0;
            while (Zone.IsInvalidTime(local) && guard++ < 48)
            {
                local = local.AddMinutes(30);
            }
            DateTime utc;
            if (Zone.IsAmbiguousTime(local))
            {
                // JavaScript takes the earlier of the two.
                var largest = Zone.GetAmbiguousTimeOffsets(local).Max();
                utc = DateTime.SpecifyKind(local - largest, DateTimeKind.Utc);
            }
            else
            {
                utc = TimeZoneInfo.ConvertTimeToUtc(local, Zone);
            }
            return (long)Math.Round((utc - Epoch).TotalMilliseconds);
        }

        /// <summary>Whole minutes from one local midnight to another (the Planner's minutesBetween).</summary>
        public long MinutesBetween(DateTime from, DateTime to)
        {
            return (long)Math.Floor((Midnight(to) - Midnight(from)) / 60000.0 + 0.5);
        }

        /// <summary>The local date and time of an instant.</summary>
        public DateTime LocalTime(long ms)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(Epoch.AddMilliseconds(ms), Zone);
        }

        // ---------------------------------------------------------------------------------------------------------
        // The 4D view

        /// <summary>Is the named object showing at the instant (timeline.ts meshVisibilityFromEvents)? An unbound name always is.</summary>
        public bool VisibleAt(string name, long instant)
        {
            List<PlannerScheduleEvent> list;
            if (!Events.TryGetValue(name, out list) || list.Count == 0)
            {
                return true;
            }
            bool visible = !list[0].Install;
            foreach (var e in list)
            {
                if (e.Instant <= instant)
                {
                    visible = e.Install;
                }
                else
                {
                    break;
                }
            }
            return visible;
        }

        /// <summary>
        /// The scene's nodes the 4D view hides at the instant: the node of each bound name it switches off, and everything
        /// linked under such a node (Babylon hides what is under a node it switches off).
        /// </summary>
        public HashSet<long> HiddenAt(long instant)
        {
            var off = new HashSet<long>();
            foreach (var pair in Events)
            {
                PlannerModelNode node;
                if (!VisibleAt(pair.Key, instant) && Index.TryGetValue(pair.Key, out node))
                {
                    off.Add(node.Key);
                }
            }
            var hidden = new HashSet<long>();
            if (off.Count == 0)
            {
                return hidden;
            }
            var scene = Model.Scene;
            var pending = new Stack<KeyValuePair<PlannerSceneNode, bool>>();
            foreach (var root in scene.Nodes.Where(n => scene.ParentOf(n) == null))
            {
                pending.Push(new KeyValuePair<PlannerSceneNode, bool>(root, false));
            }
            var seen = new HashSet<long>();
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (!seen.Add(item.Key.Key))
                {
                    continue;
                }
                bool isOff = item.Value || off.Contains(item.Key.Key);
                if (isOff)
                {
                    hidden.Add(item.Key.Key);
                }
                foreach (var child in scene.ChildrenOf(item.Key))
                {
                    pending.Push(new KeyValuePair<PlannerSceneNode, bool>(child, isOff));
                }
            }
            return hidden;
        }

        /// <summary>The first and last day of the schedule: the project start to the day the last activity ends.</summary>
        public void Span(out DateTime first, out DateTime last)
        {
            first = ProjectStart;
            last = WorksEnd ?? ProjectStart;
            foreach (var activity in Activities)
            {
                var end = LocalTime(Math.Max(activity.StartMs, activity.EndMs - 1)).Date;
                if (end > last)
                {
                    last = end;
                }
            }
            if (last < first)
            {
                last = first;
            }
        }

        /// <summary>The layers at work on the day: dated layers whose dates cover it, and undated ones on the project start.</summary>
        public List<PlannerScheduleLayer> ActiveOn(DateTime day)
        {
            return Layers.Where(l => l.Dated ? l.Start <= day.Date && day.Date <= l.End : day.Date == ProjectStart).ToList();
        }

        /// <summary>
        /// The objects installed or removed on the day, as the Gantt shows a day's work: installs that start on it, and
        /// removals whose last minute is on it.
        /// </summary>
        public List<PlannerScheduleObject> ChangingOn(DateTime day)
        {
            long from = Midnight(day.Date);
            long to = Midnight(day.Date.AddDays(1));
            var result = new List<PlannerScheduleObject>();
            foreach (var obj in Objects.Values)
            {
                List<PlannerScheduleEvent> list;
                if (Events.TryGetValue(obj.Name, out list) && list.Any(e => e.Install ? e.Instant >= from && e.Instant < to : e.Instant > from && e.Instant <= to))
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------
        // The IN and RM columns

        /// <summary>The activities installing (IN) or removing (RM) that the layer's dates place.</summary>
        public List<PlannerScheduleActivity> ColumnOf(PlannerScheduleLayer layer, PlannerTaskType type)
        {
            return Activities.Where(a => a.Type == type && a.DatedBy == layer).ToList();
        }

        /// <summary>The activities no dated layer places: installs at the project start, removals parked after the works end.</summary>
        public List<PlannerScheduleActivity> ByRule(PlannerTaskType type)
        {
            return Activities.Where(a => a.Type == type && a.DatedBy == null).ToList();
        }

        /// <summary>
        /// Where an object's work is placed, one entry per activity binding it: "IN 02_01_2_Construction" (the label of the
        /// layer placing it), "IN (project start)" or "RM (after the end)" for work placed by rule. Sorted.
        /// </summary>
        public List<string> PlacesOf(string objectName)
        {
            PlannerScheduleObject obj;
            if (!Objects.TryGetValue(objectName, out obj))
            {
                return new List<string>();
            }
            return obj.Activities.Select(PlaceOf).OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Where the work of the object a scene node stands for is placed (<see cref="PlacesOf"/>); empty when the node is
        /// not an object the Planner schedules (a piece of one, a helper, a node another of its GLB name stands for).
        /// </summary>
        public List<string> PlacesOfNode(long key)
        {
            var obj = ObjectOf(key);
            return obj != null ? PlacesOf(obj.Name) : new List<string>();
        }

        /// <summary>The object the Planner schedules for a scene node, or null.</summary>
        public PlannerScheduleObject ObjectOf(long key)
        {
            PlannerModelNode node;
            PlannerScheduleObject obj;
            return Model.ByKey.TryGetValue(key, out node) && Objects.TryGetValue(node.Name, out obj) && obj.Key == key ? obj : null;
        }

        /// <summary>The name the GLB gives a scene node, or null when the export leaves it out.</summary>
        public string NameOf(long key)
        {
            PlannerModelNode node;
            return Model.ByKey.TryGetValue(key, out node) ? node.Name : null;
        }

        /// <summary>
        /// The object a scene node is a split piece of (Name_N linked under Name, merged into Name as the Planner reads
        /// it), or null when it is not one.
        /// </summary>
        public PlannerScheduleObject PieceOf(long key)
        {
            PlannerModelNode node;
            if (!Model.ByKey.TryGetValue(key, out node) || node.Name.Length == 0)
            {
                return null;
            }
            PlannerModelNode indexed;
            if (!Index.TryGetValue(node.Name, out indexed) || indexed.Key != key)
            {
                return null;
            }
            var merged = MergedName(node.Name);
            PlannerScheduleObject obj;
            return merged != node.Name && Objects.TryGetValue(merged, out obj) ? obj : null;
        }

        /// <summary>"IN &lt;label&gt;", "IN (project start)" or "RM (after the end)".</summary>
        public static string PlaceOf(PlannerScheduleActivity activity)
        {
            var type = activity.Type == PlannerTaskType.Install ? "IN " : "RM ";
            if (activity.DatedBy != null)
            {
                return type + activity.DatedBy.Label;
            }
            return type + (activity.Type == PlannerTaskType.Install ? "(project start)" : "(after the end)");
        }

        /// <summary>The layer a node stands for (one of its helpers), or null.</summary>
        public PlannerScheduleLayer LayerOfNode(long key)
        {
            return Layers.FirstOrDefault(l => l.NodeKeys.Contains(key));
        }

        // ---------------------------------------------------------------------------------------------------------
        // What the Planner does not schedule

        private static readonly Regex LooksTagged = new Regex(@"^\s*ph\s*[-_ ]?\s*[0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A tag as the parser writes one, after the start of a name: at its end (<description>_Ph<n>_St<nn>[_<m>]_IN|RM) or
        // in its middle (Kerb_Ph1_St01_IN_001, or 3ds Max's clone number Kerb_Ph1_St01_IN001).
        private static readonly Regex TagAfterStart = new Regex(@"[^A-Za-z0-9]Ph[0-9]+_St[0-9]+(?:[_.-][0-9]+)?_(?:IN|RM)(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Why the Planner does not schedule a name that carries a tag, but not at its start.</summary>
        public const string TagNotAtStartReason = "its tag is not at the start of its name, and the Planner reads an object only when its name starts with its tag "
            + "(Ph<n>_St<nn>[_<m>]_IN|RM_<description>)";

        /// <summary>
        /// A name that does not start with a tag but carries one later on, at its end or in its middle: the Planner reads a
        /// tag after the description only when the name starts with one, so it does not read this one at all.
        /// </summary>
        public static bool HasTagNotAtStart(string name)
        {
            return !string.IsNullOrEmpty(name) && PlannerNaming.ParseMeshName(name) == null && TagAfterStart.IsMatch(name);
        }

        /// <summary>
        /// "the export calls it X (1), as a node it writes before it is called X too, so the Planner reads it as an object
        /// of its own: give it a unique name".
        /// </summary>
        public static string RenamedByExportText(PlannerModelNode node)
        {
            return "the export calls it " + node.Name + ", as a node it writes before it is called " + node.MaxName
                + " too, so the Planner reads it as an object of its own: give it a unique name";
        }

        // Every node under the phasing root that is geometry, and every tagged node anywhere, that the Planner does not
        // schedule; helpers, splines the export leaves out and the pieces of an object are not objects of their own. The
        // Planner reads the GLB's names: a node the export renames (" (1)") is read by that name, as an object of its own.
        private void ListNotScheduled()
        {
            var scene = Model.Scene;
            foreach (var node in scene.Nodes)
            {
                var name = node.Name ?? string.Empty;
                PlannerModelNode model;
                Model.ByKey.TryGetValue(node.Key, out model);
                var read = model != null ? model.Name : name;
                bool tagged = PlannerNaming.ParseMeshName(read) != null;
                bool underRoot = scene.TreeNodes.Contains(node.Key) || (node.LayerName != null && scene.TreeLayers.Contains(node.LayerName));
                bool geometry = !node.IsHelper && !node.IsShape;
                if (scene.LeftOut(node) || scene.PhasingHelpers.Contains(node.Key) || !(tagged || HasTagNotAtStart(read) || (underRoot && geometry)))
                {
                    continue;
                }
                string reason = null;
                PlannerModelNode indexed;
                string why;
                if (UnderObject(node.Key))
                {
                    // Part of a scheduled object (linked under it, a member of its group): shown and hidden with it.
                    continue;
                }
                if (model == null)
                {
                    reason = Model.LeftOut.TryGetValue(node.Key, out why) ? "not exported: " + why : "not exported: a node above it is left out of the export";
                }
                else if (read.Length == 0)
                {
                    reason = "it has no name";
                }
                else if (!Index.TryGetValue(read, out indexed) || indexed.Key != node.Key)
                {
                    // Two nodes the GLB gives one name (one called "X (1)" in Max, another renamed so by the export).
                    reason = "another node reaches the Planner as " + read + " too, and the Planner shows and hides only the first node of a name: give it a unique name";
                }
                else if (MergedName(read) == read && !Objects.ContainsKey(read) && Usable)
                {
                    string layer;
                    if (!tagged)
                    {
                        reason = LooksTagged.IsMatch(read)
                            ? "its tag does not read (Ph<n>_St<nn>[_<m>]_IN|RM_<description>)"
                            : HasTagNotAtStart(read)
                                ? TagNotAtStartReason + (LayerOf.ContainsKey(read) ? string.Empty : "; no dated layer is above it either")
                                : LayerOf.TryGetValue(read, out layer) ? "it has no Ph/St tag" : "it has no Ph/St tag and no dated layer above it";
                    }
                    else if (!LayerOf.ContainsKey(read))
                    {
                        reason = "it is not linked under a dated layer's helper, so the Planner cannot date it";
                        var unit = node.LayerName != null ? scene.UnitOfLayer(node.LayerName) : null;
                        if (unit != null && unit.InPhasingTree && PlannerNaming.ParseLayerName(unit.Name) != null)
                        {
                            reason += " (it sits on the layer " + node.LayerName + ": Link objects to their helpers puts it under the layer's helper)";
                        }
                    }
                    if (reason != null && model.RenamedByExport)
                    {
                        reason += "; " + RenamedByExportText(model);
                    }
                }
                if (reason != null)
                {
                    NotScheduled.Add(new PlannerNotScheduled { Key = node.Key, Name = name, Reason = reason, LayerName = node.LayerName });
                }
            }
        }

        // A node linked under (at any depth) the node of a scheduled object, which the 4D view shows and hides with it.
        private bool UnderObject(long key)
        {
            var scene = Model.Scene;
            PlannerSceneNode node;
            if (!scene.NodeByKey.TryGetValue(key, out node))
            {
                return false;
            }
            int hops = 0;
            for (var parent = scene.ParentOf(node); parent != null && hops < MaxDepth; parent = scene.ParentOf(parent), hops++)
            {
                if (ObjectOf(parent.Key) != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
