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
        /// <summary>Naming v2: the Planner schedules it, but reads its name only in part (a tag it cannot read, a reference to a layer nobody has).</summary>
        public bool InPart;
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
        public const string NotInThisExportReason = "this export leaves it out (Export only selected, or a layer not chosen to export)";

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
                self[node.Key] = !node.NoExport && !node.NotInThisExport && !(node.Hidden && !exportHidden) && !node.IsShape;
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
                else if (node.NotInThisExport)
                {
                    LeftOut[node.Key] = NotInThisExportReason;
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

    /// <summary>
    /// When the Planner's Generate from model is run: the time zone it runs in and the workspace's start. The new names'
    /// inputs (the Planner's project start and folders, as the layer list gives them) are in PlannerScheduleV2.cs.
    /// </summary>
    public sealed partial class PlannerScheduleOptions
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

    /// <summary>
    /// A layer as the new names' Generate from model reads it: a layer folder of the Planner's project, known by its layer
    /// ID (its own fields: PlannerScheduleV2.cs).
    /// </summary>
    public sealed partial class PlannerScheduleLayer
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
    }

    /// <summary>One activity Generate from model makes, with its place in time (the new names' own fields: PlannerScheduleV2.cs).</summary>
    public sealed partial class PlannerScheduleActivity
    {
        public string Name;
        public PlannerTaskType Type;
        /// <summary>The layer whose folder holds it.</summary>
        public PlannerScheduleLayer Home;
        /// <summary>Work of its home layer's own ID (its event layer is its home), not a reference to another layer.</summary>
        public bool IsOwn;
        /// <summary>The layer whose dates place it: the layer whose folder holds it, which every activity has on the new names.</summary>
        public PlannerScheduleLayer DatedBy;
        /// <summary>Its reference names a layer nobody has: a removal is parked after the works end, an install on the project start (spec 3.6).</summary>
        public bool Unresolved;
        public bool Tbc;
        /// <summary>Minutes from the project start, as the scheduler chains them.</summary>
        public long StartMinute;
        public long Duration;
        /// <summary>The instants the 4D view reads, epoch milliseconds: its start (an install shows) and finish (a removal hides).</summary>
        public long StartMs;
        public long EndMs;
        /// <summary>The objects it binds, in the Planner's order.</summary>
        public readonly List<string> Members = new List<string>();
    }

    /// <summary>One object the Planner schedules, and the activities that bind it (the new names' own fields: PlannerScheduleV2.cs).</summary>
    public sealed partial class PlannerScheduleObject
    {
        /// <summary>Its name in the GLB, as the Planner reads it.</summary>
        public string Name;
        /// <summary>Its node's name in 3ds Max: the GLB's, unless the export added " (n)" (another node has the name).</summary>
        public string MaxName;
        /// <summary>The scene node the Planner shows and hides for this name.</summary>
        public long Key;
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
    /// What the Planner's first Generate from model makes of a scene on the new names, and what its 4D view shows at any
    /// instant: the layers, every activity with its dates, each object's installs and removals, and the objects it does not
    /// schedule, with why. The engine is naming v2's (<see cref="BuildV2(PlannerSceneSnapshot, PlannerScheduleOptions)"/>,
    /// in PlannerScheduleV2.cs, a port of the Planner's buildFromLayersV2), run on the model the export would write. This
    /// part holds what the engine shares with the export naming check: the viewport's name index and its layer and split
    /// indexes (features/viewport/nodeNames.ts, meshes.ts), the time helpers (local midnights in the given time zone, as the
    /// browser reads them), the 4D view (state/timeline.ts meshVisibilityFromEvents), the IN and RM columns, and the old
    /// names' tag helpers the check's advice uses. Orders follow the Planner's natural order (<see cref="PlannerNaturalOrder"/>)
    /// and JavaScript's stable sorts. Pure logic, no 3ds Max.
    ///
    /// The old names' engine (Ph/St tags, dated layer names) is gone: no tool built it once the Schedule tab asked for a
    /// scene on the old names to be cleaned up first (D23). The naming reader (<see cref="NamingOfScene"/>) still tells the
    /// old names from the new.
    /// </summary>
    public sealed partial class PlannerSchedule
    {
        /// <summary>Set when Generate would not run in layer mode, saying why; the layers and activities are then empty.</summary>
        public string Message;
        public PlannerScheduleModel Model;
        public TimeZoneInfo Zone;
        /// <summary>The project start Generate dates against (minute 0).</summary>
        public DateTime ProjectStart;
        public long AnchorMs;
        /// <summary>The last day of the works; removals for a layer nobody has are parked the day after (spec 3.6).</summary>
        public DateTime? WorksEnd;
        /// <summary>The layers, in the Planner's order.</summary>
        public readonly List<PlannerScheduleLayer> Layers = new List<PlannerScheduleLayer>();
        /// <summary>The activities, in the Planner's (name) order.</summary>
        public readonly List<PlannerScheduleActivity> Activities = new List<PlannerScheduleActivity>();
        public readonly Dictionary<string, PlannerScheduleObject> Objects = new Dictionary<string, PlannerScheduleObject>(StringComparer.Ordinal);
        /// <summary>Objects the Planner does not schedule (under a phasing root, any of them, or tagged anywhere), with why.</summary>
        public readonly List<PlannerNotScheduled> NotScheduled = new List<PlannerNotScheduled>();
        /// <summary>Problems with layer names, as the Planner lists them ("layer: issue").</summary>
        public readonly List<string> LayerIssues = new List<string>();
        /// <summary>The names the Planner's index holds, in its order (meshes first), and the node each stands for.</summary>
        public readonly List<string> Names = new List<string>();
        public readonly Dictionary<string, PlannerModelNode> Index = new Dictionary<string, PlannerModelNode>(StringComparer.Ordinal);
        /// <summary>Every name the Planner reads under a dated layer (its layerIndex): the name and the layer's raw name.</summary>
        public readonly Dictionary<string, string> LayerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The 4D events of each bound name, earliest first, removals before installs at one instant.</summary>
        public readonly Dictionary<string, List<PlannerScheduleEvent>> Events = new Dictionary<string, List<PlannerScheduleEvent>>(StringComparer.Ordinal);

        public bool Usable
        {
            get { return Message == null; }
        }

        // ---------------------------------------------------------------------------------------------------------
        // The indexes

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
        // A split piece's suffix (meshes.ts mergedName); the export naming check folds pieces with it too.
        internal static readonly Regex SplitSuffix = new Regex(@"_(?:primitive)?[0-9]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        // Names, as the new names' engine and the export naming check read them

        // The layer's label without its first number: what a mesh with no description is named after.
        private static string LabelTail(PlannerLayerName p)
        {
            return p.Label == p.Order ? string.Empty : p.Label.Substring(p.Order.Length + 1);
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

        // A mesh name's leading and trailing tags, where the parser reads them (the Planner's LEAD_TAG and TRAIL_TAG): the
        // leading one at the start, followed by "_" or the end; the trailing one at the end or before a space-separated note.
        private static readonly Regex RetagLead = new Regex(@"^Ph[0-9]+_St[0-9]+(?:[._-][0-9]+)?_(IN|RM)(?=_|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RetagTrail = new Regex(@"_Ph[0-9]+_St[0-9]+(?:[._-][0-9]+)?_(IN|RM)(?=\z|" + PlannerNaming.JavaScriptSpace + ")",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// A two-tag object name with both its tags moved to the phase and stage given, written the current way
        /// ("Ph2_St01_RM", "Ph2_St04_2_IN"), each keeping its type and the rest of the name as written (the Planner's
        /// retagged): "Ph2_St02_RM_Bench_Ph2_St02_IN" gives "Ph2_St01_RM_Bench_Ph2_St01_IN" for phase 2, stage 1.
        /// </summary>
        public static string Retagged(string name, int phase, int stage, int? subStage)
        {
            Func<Match, string> tag = m => "Ph" + phase.ToString(CultureInfo.InvariantCulture) + "_" + PlannerNaming.StageText(stage, subStage) + "_"
                + m.Groups[1].Value.ToUpperInvariant();
            var text = RetagTrail.Replace(name, m => "_" + tag(m), 1);
            return RetagLead.Replace(text, m => tag(m), 1);
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
            return LocalInstant(day, 0);
        }

        /// <summary>
        /// The instant a local date and a time of day (minutes after midnight, as the clock on the wall reads them) name,
        /// epoch milliseconds: JavaScript's new Date(y, m, d, h, min), the Planner's localInstant. Not midnight plus the
        /// minutes: on a clock-change day those two part. A time the clocks skip moves forward by the gap (it is read with
        /// the offset in force before the change); a time they repeat is its first occurrence, the earlier instant (.NET
        /// would take the second, standard time).
        /// </summary>
        public long LocalInstant(DateTime day, int minuteOfDay)
        {
            var local = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified).AddMinutes(minuteOfDay);
            TimeSpan offset;
            if (Zone.IsInvalidTime(local))
            {
                // The offset of the same wall time a day before (the last of two, if repeated), as JavaScript reads a gap.
                var before = local.AddDays(-1);
                offset = Zone.IsAmbiguousTime(before) ? Zone.GetAmbiguousTimeOffsets(before).Min() : Zone.GetUtcOffset(before);
            }
            else if (Zone.IsAmbiguousTime(local))
            {
                offset = Zone.GetAmbiguousTimeOffsets(local).Max();
            }
            else
            {
                offset = Zone.GetUtcOffset(local);
            }
            var utc = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
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

        /// <summary>
        /// The first and last day of the schedule: the days of every layer's dates and work, which may start before the project
        /// start (it never moves): <see cref="SpanV2"/>.
        /// </summary>
        public void Span(out DateTime first, out DateTime last)
        {
            SpanV2(out first, out last);
        }

        /// <summary>
        /// The layers at work on the day: those whose dates or parked day cover it; a layer with neither (no work, no dates) is
        /// at work on none.
        /// </summary>
        public List<PlannerScheduleLayer> ActiveOn(DateTime day)
        {
            return Layers.Where(l => l.Dated && l.Start <= day.Date && day.Date <= l.End).ToList();
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

        /// <summary>The activities installing (IN) or removing (RM) filed in the layer's folder, which its dates place.</summary>
        public List<PlannerScheduleActivity> ColumnOf(PlannerScheduleLayer layer, PlannerTaskType type)
        {
            return Activities.Where(a => a.Type == type && a.DatedBy == layer).ToList();
        }

        /// <summary>
        /// Where an object's work is placed, one entry per activity binding it: "IN 02_01_2_Construction", the label of the
        /// layer whose folder holds it (every activity's, on the new names). Sorted.
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
        /// The scheduled object a scene node is a split piece of (Name_N linked under Name, merged into Name as the Planner
        /// reads it), or null when it is not one or Name is not scheduled (<see cref="MergedOwnerOf"/> for any Name).
        /// </summary>
        public PlannerScheduleObject PieceOf(long key)
        {
            var owner = MergedOwnerOf(key);
            PlannerScheduleObject obj;
            return owner != null && Objects.TryGetValue(owner.Name, out obj) ? obj : null;
        }

        /// <summary>
        /// The node a scene node is a split piece of: Name_N linked under Name, which the Planner folds into Name by name
        /// and ancestor alone (meshes.ts mergedName), whether or not Name is tagged or scheduled; null when it is not one.
        /// </summary>
        public PlannerModelNode MergedOwnerOf(long key)
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
            PlannerModelNode owner;
            return merged != node.Name && Index.TryGetValue(merged, out owner) ? owner : null;
        }

        /// <summary>"IN &lt;label&gt;" or "RM &lt;label&gt;": the layer whose folder holds the activity (<see cref="PlannerScheduleActivity.DatedBy"/>).</summary>
        public static string PlaceOf(PlannerScheduleActivity activity)
        {
            return (activity.Type == PlannerTaskType.Install ? "IN " : "RM ") + activity.DatedBy.Label;
        }

        /// <summary>The layer a node stands for (one of its helpers), or null.</summary>
        public PlannerScheduleLayer LayerOfNode(long key)
        {
            return Layers.FirstOrDefault(l => l.NodeKeys.Contains(key));
        }

        // ---------------------------------------------------------------------------------------------------------
        // What the Planner does not schedule

        /// <summary>
        /// "the export calls it X (1), as a node it writes before it is called X too, so the Planner reads it as an object
        /// of its own: give it a unique name".
        /// </summary>
        public static string RenamedByExportText(PlannerModelNode node)
        {
            return "the export calls it " + node.Name + ", as a node it writes before it is called " + node.MaxName
                + " too, so the Planner reads it as an object of its own: give it a unique name";
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
