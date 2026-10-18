using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>Where a layer's preview dates come from on the new names (spec 5.4, 5.8). No date is set in 3ds Max (D20).</summary>
    public enum PlannerDateSource
    {
        /// <summary>No dates: the preview parks the layer's work on a day of its own (D12), or it has no work.</summary>
        None,
        /// <summary>The Planner: its layer list's Start, Finish and TBC.</summary>
        Planner
    }

    /// <summary>
    /// A folder of the Planner's project as the layer list describes it (naming v2, spec 5.4 and 5.6): one row, its name,
    /// the folder it sits in, its TBC flag and, for a layer, its dates (the list's Start and Finish, inclusive days). The
    /// preview reads it as the Planner's new-name Generate reads the project's folders (plannerLayersV2): a folder whose name
    /// reads a two- or three-number layer ID is that layer's, and a layer's dates are its own work's span, which a dated row
    /// gives as its Start at 00:00 to the midnight after its Finish. An ID only a row has (planned in the Planner, not built
    /// in 3ds Max yet) is a list-only layer: a reference to it files work there.
    /// </summary>
    public sealed class PlannerPlanFolder
    {
        /// <summary>The folder's name: the list's Layer text.</summary>
        public string Name = string.Empty;
        /// <summary>The folder it sits in (a phase group's), or null at the top.</summary>
        public PlannerPlanFolder Parent;
        /// <summary>Flagged TBC, as the list says.</summary>
        public bool Tbc;
        /// <summary>A layer's first and last day; null for an undated row (and a group). One alone stands for a single day.</summary>
        public DateTime? Start;
        public DateTime? Finish;
        /// <summary>Where the dates come from: the Planner's list.</summary>
        public PlannerDateSource Source = PlannerDateSource.Planner;
        /// <summary>
        /// The layer ID the folder is, when not the one its name reads: a row Clean up scene graph finds by its Planner ID
        /// on a helper of another ID (the ID changed in the Planner, not built yet) is that helper's layer, so the layer keeps
        /// the row's dates (spec 5.4, precedence 1). Null: read from its name.
        /// </summary>
        public PlannerLayerIdKey Id;
        /// <summary>The list row it is, or null.</summary>
        public PlannerLayerRow Row;
    }

    /// <summary>
    /// An activity the Planner's project already has, as the first Generate counts it when it parks a layer (spec 3.5: a
    /// layer whose every event the project already has an activity for takes no parked day): its type and the objects it
    /// binds. The layer list carries no bindings, so the Schedule tab has none to give; the rule is kept so the preview stays
    /// the Planner's Generate.
    /// </summary>
    public sealed class PlannerExistingWork
    {
        public PlannerTaskType Type;
        public readonly List<string> Objects = new List<string>();
    }

    public sealed partial class PlannerScheduleOptions
    {
        /// <summary>
        /// Naming v2: the Planner's project start (the list's Project start column, or the tab's Planner project start): minute
        /// 0, and the first day a layer with no dates is parked on. It never moves (spec 3.4). Null: <see cref="WorkspaceStart"/>,
        /// else <see cref="Today"/>.
        /// </summary>
        public DateTime? ProjectStart;
        /// <summary>
        /// Naming v2: the Planner's folders in the layer list's outline order, groups included: every list row (spec 5.13's
        /// per-ID dates and list-only IDs). Empty: a new workspace, where every layer with work is parked.
        /// </summary>
        public List<PlannerPlanFolder> Folders = new List<PlannerPlanFolder>();
        /// <summary>Naming v2: the work the Planner's project binds already (<see cref="PlannerExistingWork"/>); none from the tab.</summary>
        public List<PlannerExistingWork> ExistingWork = new List<PlannerExistingWork>();
    }

    /// <summary>
    /// A folder of the project after the preview's first Generate on the new names, as the Planner's merge leaves it: the
    /// Planner's folders where they were (one at the top filed in a phase group's folder the Generate adds, as the merge
    /// files it), and those the Generate adds, a new layer's (TBC) and a phase group's.
    /// </summary>
    public sealed class PlannerScheduleFolder
    {
        public string Name;
        /// <summary>The folder it sits in, or null at the top.</summary>
        public PlannerScheduleFolder Parent;
        /// <summary>Its own TBC flag (<see cref="TbcHere"/> counts the folders above it too).</summary>
        public bool Tbc;
        /// <summary>The day the Generate parked its layer's work on (the Planner's modelParkedOn), or null.</summary>
        public DateTime? ParkedOn;
        /// <summary>The Planner's folder it is (a list row), or null for one the Generate adds.</summary>
        public PlannerPlanFolder From;
        /// <summary>The layer ID it is; null for a phase group's folder, or a folder with no ID.</summary>
        public PlannerLayerIdKey Id;
        /// <summary>The layer it is (<see cref="PlannerSchedule.Layers"/>), or null.</summary>
        public PlannerScheduleLayer Layer;

        public string ParentName
        {
            get { return Parent != null ? Parent.Name : null; }
        }

        /// <summary>TBC as the Planner's Gantt and exports read a folder: its own flag, or a folder's above it.</summary>
        public bool TbcHere
        {
            get
            {
                int hops = 0;
                for (var f = this; f != null && hops < 64; f = f.Parent, hops++)
                {
                    if (f.Tbc)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }

    /// <summary>A layer the first Generate parks on a day of its own (D12): its folder and the day.</summary>
    public sealed class PlannerParkedLayer
    {
        public string Folder;
        public DateTime Day;
    }

    /// <summary>An event whose reference names a layer neither the scene nor the Planner has (spec 3.6): its activity, and that layer.</summary>
    public sealed class PlannerMissingLayer
    {
        public string ActivityName;
        /// <summary>The ID as written the current way ("01_23"); "99_99" is work whose date is not known yet.</summary>
        public string Layer;
    }

    /// <summary>Layer nodes or folders sharing one layer ID (spec 3.1).</summary>
    public sealed class PlannerSharedId
    {
        public string Id;
        /// <summary>Folders sharing it; else layer nodes whose names differ.</summary>
        public bool Folders;
        public readonly List<string> Names = new List<string>();
    }

    /// <summary>A folder found by its layer ID whose name after the ID is not the scene layer's: kept as the Planner names it.</summary>
    public sealed class PlannerRenamedLayer
    {
        public string Folder;
        public string Layer;
    }

    /// <summary>
    /// What the new-name Generate reports (spec 3.7), for the preview (the Planner's GenerateReportV2 and the result's lists):
    /// the objects it scheduled and not, the layers it parked, references to layers nobody has, naming issues, and the rest.
    /// </summary>
    public sealed class PlannerScheduleV2Report
    {
        /// <summary>Tagged objects under a layer node: scheduled.</summary>
        public readonly List<string> Matched = new List<string>();
        /// <summary>Objects with no tag: not scheduled, shown for the whole programme.</summary>
        public readonly List<string> Skipped = new List<string>();
        /// <summary>Tagged objects under no layer node, or under a number-led node that is no layer ID: not scheduled.</summary>
        public readonly List<string> Unfiled = new List<string>();
        /// <summary>Objects named the old way (a Ph/St tag): not scheduled.</summary>
        public readonly List<string> OtherNaming = new List<string>();
        /// <summary>Layers with no dates, each parked on a day of its own, in layer order from the project start (D12).</summary>
        public readonly List<PlannerParkedLayer> ParkedLayers = new List<PlannerParkedLayer>();
        /// <summary>References to a layer nobody has: filed with their object, parked TBC (spec 3.6).</summary>
        public readonly List<PlannerMissingLayer> MissingLayers = new List<PlannerMissingLayer>();
        /// <summary>The objects' naming issues (<see cref="PlannerNaming.NamingIssuesV2"/>), old tags left out (<see cref="OtherNaming"/>).</summary>
        public readonly List<PlannerNamingIssueV2> NamingIssues = new List<PlannerNamingIssueV2>();
        /// <summary>Folders with a layer ID the scene has no layer for (list-only layers): kept, with their dates.</summary>
        public readonly List<string> FoldersWithoutLayer = new List<string>();
        public readonly List<PlannerRenamedLayer> RenamedLayers = new List<PlannerRenamedLayer>();
        public readonly List<PlannerSharedId> SharedIds = new List<PlannerSharedId>();
        /// <summary>Scheduled objects whose names end in " (n)": the export renamed a second object of one name.</summary>
        public readonly List<string> NumberedBindings = new List<string>();
        /// <summary>Objects whose two events run the other way round from their name, dated layers only (spec 3.7).</summary>
        public readonly List<string> WrongOrder = new List<string>();
        /// <summary>The activities made TBC: work in a parked layer, and references to a layer nobody has.</summary>
        public readonly List<string> TbcActivities = new List<string>();
        /// <summary>Activity names more than one activity has (two exported objects of one name in one layer), in the order of their first copy.</summary>
        public readonly List<string> DuplicateNames = new List<string>();
        /// <summary>Finish-to-start links made.</summary>
        public int DependencyCount;
        /// <summary>
        /// The list's layer rows dated to one day and TBC, as the Planner's list writes a layer it still has parked (spec 3.5).
        /// The list does not say which are parked, so the preview reads them as dated; the Planner parks a layer it does not
        /// have yet after its still-parked layers' days, and does not check an order against a parked layer.
        /// </summary>
        public readonly List<string> OneTbcDayRows = new List<string>();
        /// <summary>Of <see cref="WrongOrder"/>, the objects with an event in a layer of <see cref="OneTbcDayRows"/>: the Planner skips them while it is parked.</summary>
        public readonly List<string> WrongOrderIfDated = new List<string>();
    }

    /// <summary>Where the removals for a layer nobody has are parked (spec 3.6): the day after this.</summary>
    public enum PlannerWorksEndFrom
    {
        /// <summary>The last dated work.</summary>
        DatedWork,
        /// <summary>The last parked day: no layer is dated.</summary>
        ParkedDay,
        /// <summary>The project start: no layer is dated or parked.</summary>
        ProjectStart
    }

    /// <summary>A layer of a new-name scene: its ID and the layer nodes that carry it (one, or several sharing it).</summary>
    public sealed class PlannerSceneLayerV2
    {
        public PlannerLayerIdKey Id;
        /// <summary>Its layer nodes, as the Planner reads their names, each once.</summary>
        public readonly List<string> Nodes = new List<string>();
    }

    /// <summary>
    /// A scene read the new names' way, as the export naming check reports on it (spec 5.10; <see cref="PlannerSchedule.ReadNamesV2"/>):
    /// what the Planner's Generate reads of the model (readModelV2) and the naming issues it lists (namingIssuesV2), with
    /// nothing scheduled. Every name is the GLB's, as the Planner reads it: a second node of one name is "X (1)".
    /// </summary>
    public sealed class PlannerSceneNamesV2
    {
        /// <summary>The layer nodes, empty ones included, each object's layer node, and the number-led nodes passed over.</summary>
        public PlannerLayerNodeIndex Nodes;
        /// <summary>The GLB node each name the Planner's index holds stands for (meshes first, the first node of a name winning).</summary>
        public readonly Dictionary<string, PlannerModelNode> Index = new Dictionary<string, PlannerModelNode>(StringComparer.Ordinal);
        /// <summary>The split pieces (Name_N under Name) and the object each folds into.</summary>
        public readonly Dictionary<string, string> Pieces = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The scene's layers, in the order their first nodes come.</summary>
        public readonly List<PlannerSceneLayerV2> Layers = new List<PlannerSceneLayerV2>();
        /// <summary>Each object (pieces folded in; layer nodes, number-led nodes and phase groups left out), with the ID of the layer it is scheduled in.</summary>
        public readonly List<PlannerObjectLayer> Objects = new List<PlannerObjectLayer>();
        /// <summary>Each object's name as read, with its own layer.</summary>
        public readonly Dictionary<string, PlannerObjectNameV2> Parsed = new Dictionary<string, PlannerObjectNameV2>(StringComparer.Ordinal);
        /// <summary>Tagged objects under a layer node: scheduled.</summary>
        public readonly List<string> Matched = new List<string>();
        /// <summary>Objects with no tag: not scheduled, shown for the whole programme.</summary>
        public readonly List<string> Skipped = new List<string>();
        /// <summary>Tagged objects under no layer node, or under a number-led node that is no layer ID: not scheduled.</summary>
        public readonly List<string> Unfiled = new List<string>();
        /// <summary>Objects named the old way (a Ph/St tag): not scheduled.</summary>
        public readonly List<string> OtherNaming = new List<string>();
        /// <summary>"layer: issue", as the Planner lists them: the layer names' issues, then each number-led node that is no layer ID holding tagged objects.</summary>
        public readonly List<string> LayerIssues = new List<string>();
        /// <summary>The layer names' issues as read: each layer node and what its name has that it should not.</summary>
        public readonly List<KeyValuePair<string, PlannerNamingCode>> LayerNameIssues = new List<KeyValuePair<string, PlannerNamingCode>>();
        /// <summary>Each number-led node that is no layer ID holding tagged objects, and how many it holds.</summary>
        public readonly List<KeyValuePair<string, int>> NotLayers = new List<KeyValuePair<string, int>>();
        /// <summary>Layer nodes sharing one ID whose names differ.</summary>
        public readonly List<PlannerSharedId> SharedIds = new List<PlannerSharedId>();
        /// <summary>The objects' naming issues, read against every layer ID the scene or the Planner has; old tags left out (<see cref="OtherNaming"/>).</summary>
        public readonly List<PlannerNamingIssueV2> NamingIssues = new List<PlannerNamingIssueV2>();
        /// <summary>The events the scheduled objects' names give: installs and removals.</summary>
        public int Installs;
        public int Removals;
        /// <summary>The layers the scheduled objects' references name (_RM_&lt;ID&gt;, _IN_&lt;ID&gt;), as the IDs are written the current way, each once, in the order first named.</summary>
        public readonly List<string> ReferencedLayers = new List<string>();
    }

    public sealed partial class PlannerScheduleLayer
    {
        /// <summary>Naming v2: the layer ID; null on the old names.</summary>
        public PlannerLayerIdKey Id;
        /// <summary>Naming v2: its folder after the preview's Generate.</summary>
        public PlannerScheduleFolder Folder;
        /// <summary>Naming v2: where its dates come from: the Planner's list, or none (parked, or no work).</summary>
        public PlannerDateSource DateSource;
        /// <summary>Naming v2: the day its work is parked on (it has no dates yet, D12), or null.</summary>
        public DateTime? ParkedOn;
        /// <summary>Naming v2: the list row its folder is, or null (a layer only the scene has).</summary>
        public PlannerLayerRow ListRow;
        /// <summary>Naming v2: the scene has a layer node of its ID; false for a list-only layer, planned in the Planner.</summary>
        public bool InScene;
    }

    public sealed partial class PlannerScheduleActivity
    {
        /// <summary>Naming v2: the object it is made for (one activity per event, D4).</summary>
        public string Object;
        /// <summary>Naming v2: its event layer (spec 3.2), the layer whose dates place it: the ID its name ends with.</summary>
        public PlannerLayerIdKey EventLayer;
        /// <summary>Naming v2: its place in its layer's order (D5): group 1, 2 or 3; 0 for a reference to a layer nobody has.</summary>
        public int Group;
        /// <summary>Naming v2: the activity it follows, finish to start (the Planner's dependency), or null.</summary>
        public PlannerScheduleActivity After;
        // Naming v2: its start in minutes after the project start (with no After), as the Planner's startOffsetMinutes.
        internal long OffsetMinutes;
    }

    public sealed partial class PlannerScheduleObject
    {
        /// <summary>Naming v2: its name as the new names read it, with its own layer; null on the old names.</summary>
        public PlannerObjectNameV2 NameV2;
    }

    /// <summary>
    /// The new names' engine (naming v2, spec 3 and 5.6; what it shares with the export naming check is in
    /// PlannerSchedule.cs): what the Planner's first Generate makes of a new-name scene over the Planner's folders and days as
    /// the layer list gives them, and what its 4D view shows at any instant. A line-by-line port of the Planner's
    /// buildFromLayersV2, readModelV2 and layerSlotsV2 (apps/web/src/lib/phasingFromModel.ts), the viewport's layerNodeIndex
    /// (features/viewport/nodeNames.ts) and, for the folders, what the store's merge does on a first Generate into that
    /// workspace (mergeGeneratedPhasing), at commit 6509458, with the Planner's scheduler and 4D events (state/schedule.ts,
    /// state/timeline.ts).
    ///
    /// The preview's workspace is the list's (spec 5.6): a folder per row, a dated row's folder holding its days as its own
    /// work, and nothing else. So no folder holds the scene's work yet: each scene layer takes the first folder with its ID in
    /// outline order (the guard of spec 3.1 lets it, and the work fallback of spec 4.1 finds nothing), no Merge fit applies,
    /// and the folders' places follow the merge's first-Generate rules. Every date comes from the Planner's list (none is set
    /// in 3ds Max, D20); dates and TBC in layer names are never read. Pure logic, no 3ds Max.
    /// </summary>
    public sealed partial class PlannerSchedule
    {
        /// <summary>The naming the schedule was built on: the new names, the one engine there is (the old names' is gone).</summary>
        public PlannerNamingKind Naming;
        /// <summary>Naming v2: what the scene's naming was read from (its objects, or its layer names), or null.</summary>
        public PlannerNamingSource? NamingFrom;
        /// <summary>Naming v2: the report's lists.</summary>
        public PlannerScheduleV2Report V2;
        /// <summary>Naming v2: the project's folders after the first Generate, in outline order.</summary>
        public readonly List<PlannerScheduleFolder> Folders = new List<PlannerScheduleFolder>();
        /// <summary>Naming v2: what the works end at (<see cref="WorksEnd"/>), the day after which removals for a layer nobody has are parked.</summary>
        public PlannerWorksEndFrom WorksEndFrom;
        /// <summary>
        /// Naming v2: each object's own layer as the new names read it (spec 2.2), by its name in the GLB: the ID of the layer
        /// node it sits under, the one its tags are read against; null for one under none (or under a number-led node that is
        /// no layer ID). Tagged or not, every object is here; layer nodes, phase groups and split pieces are not.
        /// </summary>
        public readonly Dictionary<string, PlannerLayerIdKey> OwnLayers = new Dictionary<string, PlannerLayerIdKey>(StringComparer.Ordinal);

        /// <summary>
        /// The Planner's rule for where a removal for a layer nobody has is parked (spec 3.6; its Generate report says it so),
        /// for where the Planner's dates are not known: "the day after the last dated work (with none dated, the day after the
        /// last parked day)".
        /// </summary>
        public const string MissingRemovalRule = "the day after the last dated work (with none dated, the day after the last parked day)";

        /// <summary>Naming v2: what the works end at, as the tab says it: "the last dated work", "the last parked day" or "the project start".</summary>
        public string WorksEndText
        {
            get
            {
                switch (WorksEndFrom)
                {
                    case PlannerWorksEndFrom.ParkedDay:
                        return "the last parked day";
                    case PlannerWorksEndFrom.ProjectStart:
                        return "the project start";
                    default:
                        return "the last dated work";
                }
            }
        }

        /// <summary>
        /// The scene's naming (spec 2.3), as the Planner reads the model's (namingOfModel, <see cref="PlannerNaming.NamingOfModel"/>):
        /// the larger count of old-kind and new-kind objects under layer nodes, a date in a layer name deciding only a tie (TBC
        /// alone never does). Null when the scene has no tagged object, no layer node and no dated layer name: read as the old
        /// names, as the Planner's Generate reads it (the Schedule tab works on such a scene, as nothing in it needs Clean up).
        /// </summary>
        public static PlannerModelNaming NamingOfScene(PlannerScheduleModel model)
        {
            var probe = new PlannerSchedule { Model = model, Zone = TimeZoneInfo.Utc };
            probe.ReadIndex();
            var oldLayers = probe.LayerIndex();
            return PlannerNaming.NamingOfModel(probe.Names, probe.SplitIndex(), oldLayers, probe.LayerNodeIndex());
        }

        /// <summary>
        /// The new names' schedule of a scene: the Schedule tab's preview. The tab builds it only once the scene reads as the
        /// new names (or as neither), since a scene on the old names needs Clean up scene graph first (D23).
        /// </summary>
        public static PlannerSchedule BuildV2(PlannerSceneSnapshot snapshot, PlannerScheduleOptions options)
        {
            var model = new PlannerScheduleModel(snapshot);
            return BuildV2(model, options, NamingOfScene(model));
        }

        /// <summary>As <see cref="BuildV2(PlannerSceneSnapshot, PlannerScheduleOptions)"/>, with the naming read already (<see cref="NamingOfScene"/>).</summary>
        public static PlannerSchedule BuildV2(PlannerScheduleModel model, PlannerScheduleOptions options, PlannerModelNaming naming)
        {
            options = options ?? new PlannerScheduleOptions();
            var schedule = new PlannerSchedule
            {
                Model = model,
                Zone = options.Zone ?? TimeZoneInfo.Local,
                Naming = PlannerNamingKind.New,
                NamingFrom = naming != null ? naming.From : (PlannerNamingSource?)null,
                V2 = new PlannerScheduleV2Report()
            };
            schedule.ReadIndex();
            // The old reading of the layers (the viewport's layerIndex, the project's meshLayers): only to tell a tagged object
            // under a number-led node that is no layer ID here (readModelV2).
            var oldLayers = new Dictionary<string, string>(schedule.LayerIndex(), StringComparer.Ordinal);
            schedule.LayerOf.Clear();
            var nodes = schedule.LayerNodeIndex();
            var splits = schedule.SplitIndex();
            // The project start never moves on the new names: offsets are minutes from it.
            var start = (options.ProjectStart ?? options.WorkspaceStart ?? options.Today).Date;
            schedule.ProjectStart = start;
            schedule.AnchorMs = schedule.Midnight(start);
            bool made = schedule.BuildFromLayersV2(nodes, oldLayers, splits, options);
            if (!made)
            {
                schedule.Message = "No layer node (a helper named <ID>_<Name>, the ID two or three numbers) and no object with an IN or RM tag under one, "
                    + "so the Planner's Generate from model would make nothing.";
                schedule.Layers.Clear();
                schedule.Activities.Clear();
                schedule.Events.Clear();
                schedule.Folders.Clear();
                schedule.WorksEnd = null;
            }
            schedule.ListNotScheduledV2(nodes, oldLayers);
            return schedule;
        }

        /// <summary>
        /// Naming v2: the own layer of the object a scene node stands for (<see cref="OwnLayers"/>); null for a node the export
        /// leaves out, one another node of its GLB name stands for, one under no layer node, and a node that is no object (a
        /// layer node, a phase group, a split piece).
        /// </summary>
        public PlannerLayerIdKey OwnLayerOfNode(long key)
        {
            var name = NameOf(key);
            PlannerModelNode indexed;
            PlannerLayerIdKey id;
            return name != null && Index.TryGetValue(name, out indexed) && indexed.Key == key && OwnLayers.TryGetValue(name, out id) ? id : null;
        }

        /// <summary>
        /// What the Planner's new-name Generate reads of a scene and the naming issues it lists, for the export naming check
        /// (spec 5.10): its layer nodes, empty ones included, and each object read with its own layer (readModelV2), then the
        /// objects' naming issues read against every layer ID the scene has and <paramref name="plannerIds"/>, the IDs the
        /// Planner's folders have (the imported layer list's; none when no list is imported), old tags left out as the Planner
        /// leaves them out (they are <see cref="PlannerSceneNamesV2.OtherNaming"/>). Nothing is scheduled, so no date is read.
        /// </summary>
        public static PlannerSceneNamesV2 ReadNamesV2(PlannerScheduleModel model, IEnumerable<PlannerLayerIdKey> plannerIds)
        {
            var schedule = new PlannerSchedule { Model = model, Zone = TimeZoneInfo.Utc, Naming = PlannerNamingKind.New };
            schedule.ReadIndex();
            var oldLayers = new Dictionary<string, string>(schedule.LayerIndex(), StringComparer.Ordinal);
            schedule.LayerOf.Clear();
            var nodes = schedule.LayerNodeIndex();
            var splits = schedule.SplitIndex();
            var read = schedule.ReadModelV2(nodes, oldLayers, splits);

            var result = new PlannerSceneNamesV2 { Nodes = nodes };
            foreach (var pair in schedule.Index)
            {
                result.Index[pair.Key] = pair.Value;
            }
            foreach (var pair in splits)
            {
                result.Pieces[pair.Key] = pair.Value;
            }
            foreach (var layer in read.Layers)
            {
                var item = new PlannerSceneLayerV2 { Id = layer.Id };
                item.Nodes.AddRange(layer.Nodes);
                result.Layers.Add(item);
            }
            result.Objects.AddRange(read.IssueObjects);
            foreach (var pair in read.Parsed)
            {
                result.Parsed[pair.Key] = pair.Value;
            }
            result.Matched.AddRange(read.Matched);
            result.Skipped.AddRange(read.Skipped);
            result.Unfiled.AddRange(read.Unfiled);
            result.OtherNaming.AddRange(read.OtherNaming);
            result.LayerIssues.AddRange(read.LayerIssues);
            result.LayerNameIssues.AddRange(read.LayerNameIssues);
            result.NotLayers.AddRange(read.NotLayers);
            result.SharedIds.AddRange(read.SharedIds);
            var knownIds = read.Layers.Select(l => l.Id).Concat((plannerIds ?? Enumerable.Empty<PlannerLayerIdKey>()).Where(id => id != null && id.Phase.HasValue)).ToList();
            result.NamingIssues.AddRange(PlannerNaming.NamingIssuesV2(read.IssueObjects, knownIds, splits).Where(i => i.Code != PlannerNamingCode.OldTag));
            foreach (var e in read.Events)
            {
                if (e.Type == PlannerTaskType.Install)
                {
                    result.Installs++;
                }
                else
                {
                    result.Removals++;
                }
                var id = PlannerNaming.LayerIdText(e.Layer);
                if (e.Group == 2 && !result.ReferencedLayers.Contains(id))
                {
                    result.ReferencedLayers.Add(id);
                }
            }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------
        // The layer nodes (nodeNames.ts layerNodeIndex)

        private static readonly Regex NumberLed = new Regex("^[0-9]", RegexOptions.CultureInvariant);

        /// <summary>
        /// Naming v2's view of the model's nodes (nodeNames.ts layerNodeIndex): every empty node whose name starts with a two-
        /// or three-number ID is a layer node, an empty layer included; each object's layer is the nearest layer node above
        /// it. A split piece folds into its object first; phase groups are never layers or objects; an empty node led by a
        /// number that is no such ID (a one-number "05_Retainment", an older "02_05-2_Kerbs", a Max group head "2.4 High
        /// Hoarding") is passed over on the way up from an object, and listed.
        /// </summary>
        internal PlannerLayerNodeIndex LayerNodeIndex()
        {
            var objectOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var withPieces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in Names)
            {
                var obj = MergedName(name);
                objectOf[name] = obj;
                if (obj != name)
                {
                    withPieces.Add(obj);
                }
            }
            // A node holding anything but its own pieces (a node of another object below it) has no geometry through them: a
            // helper with an object named "<helper>_1" under it, beside its other objects, is still a layer.
            var holdsOthers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in Names)
            {
                var obj = objectOf[name];
                int hops = 0;
                for (var p = Index[name].Parent; p != null && hops < MaxDepth; p = p.Parent, hops++)
                {
                    if (p.Name != obj)
                    {
                        holdsOthers.Add(p.Name);
                    }
                }
            }
            var result = new PlannerLayerNodeIndex();
            // Layers, skipped nodes and groups: never objects.
            var passed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in Names)
            {
                if (objectOf[name] != name)
                {
                    continue;
                }
                if (ParsePhaseGroup(name) != null)
                {
                    passed.Add(name);
                    continue;
                }
                if (Index[name].Mesh || (withPieces.Contains(name) && !holdsOthers.Contains(name)))
                {
                    continue;
                }
                if (PlannerNaming.ParseLayerNameV2(name) != null)
                {
                    result.Layers.Add(name);
                }
                else if (NumberLed.IsMatch(name.Trim(PlannerNaming.JavaScriptSpaceChars)))
                {
                    result.Skipped.Add(name);
                }
                else
                {
                    continue;
                }
                passed.Add(name);
            }
            var isLayer = new HashSet<string>(result.Layers, StringComparer.Ordinal);
            foreach (var name in Names)
            {
                if (objectOf[name] != name || passed.Contains(name))
                {
                    continue;
                }
                int hops = 0;
                for (var p = Index[name].Parent; p != null && hops < MaxDepth; p = p.Parent, hops++)
                {
                    if (p.Name != name && isLayer.Contains(p.Name))
                    {
                        result.Objects[name] = p.Name;
                        break;
                    }
                }
            }
            foreach (var pair in GroupIndexOf(result.Layers))
            {
                result.Groups[pair.Key] = pair.Value;
            }
            return result;
        }

        // nodeNames.ts groupIndex: for each of the layers, the nearest phase group above it, and for each such group the
        // nearest group above that.
        private Dictionary<string, string> GroupIndexOf(IEnumerable<string> layers)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var todo = layers.Distinct(StringComparer.Ordinal).ToList();
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

        // ---------------------------------------------------------------------------------------------------------
        // readModelV2

        /// <summary>A model layer as a new-name Generate reads it: its layer nodes, one ID among them (spec 3.1).</summary>
        private sealed class ModelLayerV2
        {
            public PlannerLayerIdKey Id;
            public string Key;
            /// <summary>Its layer nodes, each name once: one, or several sharing the ID (a helper copied in 3ds Max is "X (1)").</summary>
            public readonly List<string> Nodes = new List<string>();
            /// <summary>Their &lt;ID&gt;_&lt;Name&gt;, each once: a new folder takes the first.</summary>
            public readonly List<string> Labels = new List<string>();
            /// <summary>The phase group node above it, the first of its nodes to sit in one; null for none.</summary>
            public string Group;
        }

        /// <summary>One event of a new-name object, filed as Generate files it (spec 3.2).</summary>
        private sealed class EventV2
        {
            public string Object;
            /// <summary>The object's own name, which names its activities.</summary>
            public string Own;
            public PlannerTaskType Type;
            /// <summary>Its event layer: the object's own layer, or the one its reference names.</summary>
            public PlannerLayerIdKey Layer;
            /// <summary>The object's own layer.</summary>
            public ModelLayerV2 Home;
            /// <summary>Its place in its layer's order (D5): 1, the own layer's leading tag; 2, another layer's reference to this one; 3, the own layer's end.</summary>
            public int Group;
            public PlannerObjectNameV2 Parsed;
        }

        /// <summary>A new-name model as Generate reads it: its layers by ID, and its tagged objects' events.</summary>
        private sealed class ModelReadV2
        {
            /// <summary>Its layers, in the order their first nodes come; layer nodes sharing an ID are one layer.</summary>
            public readonly List<ModelLayerV2> Layers = new List<ModelLayerV2>();
            public readonly Dictionary<string, ModelLayerV2> ByKey = new Dictionary<string, ModelLayerV2>(StringComparer.Ordinal);
            public readonly List<EventV2> Events = new List<EventV2>();
            public readonly List<string> Matched = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public readonly List<string> Unfiled = new List<string>();
            public readonly List<string> OtherNaming = new List<string>();
            /// <summary>"layer: issue": the layer names' issues, then each number-led node that is no layer ID holding tagged objects.</summary>
            public readonly List<string> LayerIssues = new List<string>();
            /// <summary>The layer names' issues as read: each layer node and what its name has that it should not.</summary>
            public readonly List<KeyValuePair<string, PlannerNamingCode>> LayerNameIssues = new List<KeyValuePair<string, PlannerNamingCode>>();
            /// <summary>Each number-led node that is no layer ID holding tagged objects, and how many it holds, in the order first met.</summary>
            public readonly List<KeyValuePair<string, int>> NotLayers = new List<KeyValuePair<string, int>>();
            public readonly List<PlannerSharedId> SharedIds = new List<PlannerSharedId>();
            /// <summary>Each object, with the ID of the layer it is scheduled in (null for none), as namingIssuesV2 reads them.</summary>
            public readonly List<PlannerObjectLayer> IssueObjects = new List<PlannerObjectLayer>();
            public readonly Dictionary<string, PlannerObjectNameV2> Parsed = new Dictionary<string, PlannerObjectNameV2>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Read a new-name model (spec 2.2, 3.1, 3.2; readModelV2): its layers by ID, and each object's events. Layer nodes,
        /// number-led nodes passed over and phase groups are never objects. A tagged object is scheduled in the layer node it
        /// sits under. Not scheduled: one under none, and one whose nearest layer to the old reader is a number-led node that
        /// is no layer ID here (a one-number "05_Retainment", an older "02_05-2_..."), even inside another layer's helper; both
        /// are unfiled, and such a node is listed with how many it holds.
        /// </summary>
        private ModelReadV2 ReadModelV2(PlannerLayerNodeIndex nodes, Dictionary<string, string> oldLayers, Dictionary<string, string> splits)
        {
            var read = new ModelReadV2();
            // The model's layers, by ID. A layer node's name is only read for issues; dates in it are not read.
            foreach (var node in nodes.Layers)
            {
                var p = PlannerNaming.ParseLayerNameV2(node);
                if (p == null)
                {
                    continue;
                }
                foreach (var issue in p.Issues)
                {
                    read.LayerIssues.Add(node + ": " + PlannerNaming.NamingIssueV2[issue].Text);
                    read.LayerNameIssues.Add(new KeyValuePair<string, PlannerNamingCode>(node, issue));
                }
                var key = PlannerNaming.LayerIdKey(p.Id);
                ModelLayerV2 layer;
                if (!read.ByKey.TryGetValue(key, out layer))
                {
                    layer = new ModelLayerV2 { Id = p.Id, Key = key };
                    read.ByKey[key] = layer;
                    read.Layers.Add(layer);
                }
                layer.Nodes.Add(node);
                if (!layer.Labels.Contains(p.Label))
                {
                    layer.Labels.Add(p.Label);
                }
                string above;
                if (layer.Group == null && nodes.Groups.TryGetValue(node, out above) && ParsePhaseGroup(above) != null)
                {
                    layer.Group = above;
                }
            }
            // One model file here, so layer nodes share an ID only by their names.
            foreach (var layer in read.Layers.Where(l => l.Labels.Count > 1))
            {
                var shared = new PlannerSharedId { Id = PlannerNaming.LayerIdText(layer.Id), Folders = false };
                shared.Names.AddRange(layer.Nodes);
                read.SharedIds.Add(shared);
            }

            // Each object's events. Layer nodes, number-led nodes passed over and phase groups are never objects.
            var notObjects = new HashSet<string>(nodes.Layers.Concat(nodes.Skipped), StringComparer.Ordinal);
            var skippedNodes = new HashSet<string>(nodes.Skipped, StringComparer.Ordinal);
            var objects = ToObjects(Names, splits).Where(n => !notObjects.Contains(n) && ParsePhaseGroup(n) == null).ToList();
            // A number-led node that is no layer ID, and the tagged objects under it, in the order first met.
            var underSkipped = new Dictionary<string, int>(StringComparer.Ordinal);
            var underSkippedOrder = new List<string>();
            foreach (var obj in objects)
            {
                string node;
                nodes.Objects.TryGetValue(obj, out node);
                var parsedNode = node != null ? PlannerNaming.ParseLayerNameV2(node) : null;
                // Nearest to a number-led node the old reader takes for a layer and this one passes over: in no layer.
                string old;
                bool passedOver = oldLayers.TryGetValue(obj, out old) && skippedNodes.Contains(old);
                ModelLayerV2 home = null;
                if (parsedNode != null && !passedOver)
                {
                    read.ByKey.TryGetValue(PlannerNaming.LayerIdKey(parsedNode.Id), out home);
                }
                var p = PlannerNaming.ParseObjectNameV2(obj, home != null ? home.Id : null);
                read.Parsed[obj] = p;
                read.IssueObjects.Add(new PlannerObjectLayer { Name = obj, Layer = home != null ? home.Id : null });
                if (p.Kind == PlannerNamingKind.Old)
                {
                    read.OtherNaming.Add(obj);
                    continue;
                }
                if (!p.Kind.HasValue)
                {
                    read.Skipped.Add(obj);
                    continue;
                }
                if (home == null)
                {
                    if (passedOver)
                    {
                        int n;
                        if (!underSkipped.TryGetValue(old, out n))
                        {
                            underSkippedOrder.Add(old);
                        }
                        underSkipped[old] = n + 1;
                    }
                    read.Unfiled.Add(obj);
                    continue;
                }
                read.Matched.Add(obj);
                foreach (var e in PlannerNaming.ObjectEventsV2(p))
                {
                    read.Events.Add(new EventV2
                    {
                        Object = obj,
                        Own = p.Own,
                        Type = e.Type,
                        Layer = e.Ref ?? home.Id,
                        Home = home,
                        Group = e.When == PlannerEventTime.During ? 1 : e.When == PlannerEventTime.End ? 3 : 2,
                        Parsed = p
                    });
                }
            }
            foreach (var node in underSkippedOrder)
            {
                int n = underSkipped[node];
                read.LayerIssues.Add(node + ": " + NotALayerText(node) + " Its " + n.ToString(CultureInfo.InvariantCulture)
                    + " tagged object" + (n == 1 ? " is" : "s are") + " not scheduled.");
                read.NotLayers.Add(new KeyValuePair<string, int>(node, n));
            }
            return read;
        }

        /// <summary>
        /// Why a number-led node is no layer on the new names, as the Planner's Generate says it: a space after an ID that
        /// would read ("03_03 Kerbs"), with how to write it; else its ID.
        /// </summary>
        public static string NotALayerText(string node)
        {
            return "Not a layer with the new names: " + NotALayerWhy(node) + ".";
        }

        /// <summary>The reason of <see cref="NotALayerText"/> alone: "a space follows its ID, where an underscore belongs: name it 03_03_Kerbs", or "its ID needs two or three numbers".</summary>
        public static string NotALayerWhy(string node)
        {
            var spaced = PlannerNaming.LayerNameSpacedAfterId(node);
            return spaced != null ? "a space follows its ID, where an underscore belongs: name it " + spaced : "its ID needs two or three numbers";
        }

        // ---------------------------------------------------------------------------------------------------------
        // The Planner's side (plannerLayersV2), and the plan

        /// <summary>A layer folder the project has, as a new-name Generate reads the Planner's side: a folder whose name reads a layer ID.</summary>
        private sealed class PlannerFolderV2
        {
            public PlannerPlanFolder Folder;
            public PlannerLayerIdKey Id;
            public string Key;
            /// <summary>Where its own work runs (its dates), in minutes from the project start, end exclusive; none for an undated row.</summary>
            public bool HasSpan;
            public long SpanStart;
            public long SpanEnd;
        }

        /// <summary>
        /// A row of the Planner's list dated to one day and TBC: how the list writes a layer the Planner still has parked (spec
        /// 3.5), and a layer dated to one day that stays TBC (D13) alike. The list does not tell them apart.
        /// </summary>
        public static bool OneTbcDay(PlannerPlanFolder folder)
        {
            return folder != null && folder.Source == PlannerDateSource.Planner && folder.Tbc && folder.Row != null
                && (folder.Start ?? folder.Finish).HasValue && (folder.Start ?? folder.Finish).Value.Date == (folder.Finish ?? folder.Start).Value.Date;
        }

        /// <summary>A layer Generate places work in: the model's, or one only a Planner folder has, named by a reference.</summary>
        private sealed class PlanLayerV2
        {
            public PlannerLayerIdKey Id;
            public string Key;
            public ModelLayerV2 Model;
            /// <summary>The project's folder it is, by ID; null: it gets a new folder.</summary>
            public PlannerFolderV2 At;
            public readonly List<EventV2> G1 = new List<EventV2>();
            public readonly List<EventV2> G2 = new List<EventV2>();
            public readonly List<EventV2> G3 = new List<EventV2>();
            /// <summary>Its objects' events whose reference names a layer nobody has: filed here, named for that layer, so outside its span.</summary>
            public readonly List<EventV2> Missing = new List<EventV2>();
            /// <summary>The span its work was placed over (its dates, or its parked day), when it has work.</summary>
            public bool HasSpan;
            public long SpanStart;
            public long SpanEnd;
            public DateTime? ParkedOn;
            public GenFolderV2 Folder;
        }

        /// <summary>A folder the Generate makes: a layer's (for a plan layer) or a phase group's, before the merge places it.</summary>
        private sealed class GenFolderV2
        {
            public string Name;
            public GenFolderV2 Parent;
            /// <summary>The project's folder the Generate found it is (buildFromLayersV2's claimedFolders), or null.</summary>
            public PlannerPlanFolder Claim;
            public bool Tbc;
            public DateTime? ParkedOn;
            public PlanLayerV2 Plan;
            /// <summary>The project's folder the merge matched it to, or null: added.</summary>
            public PlannerPlanFolder Match;
            public PlannerScheduleFolder Final;
        }

        // The Planner's folders in outline order: each top-level folder, then what it holds, depth first, siblings as stored.
        private static List<T> OutlineOrder<T>(IList<T> folders, Func<T, T> parentOf) where T : class
        {
            var all = new HashSet<T>(folders);
            var kids = new Dictionary<T, List<T>>();
            foreach (var f in folders)
            {
                var p = parentOf(f);
                if (p == null || !all.Contains(p))
                {
                    continue;
                }
                List<T> list;
                if (!kids.TryGetValue(p, out list))
                {
                    list = new List<T>();
                    kids[p] = list;
                }
                list.Add(f);
            }
            var result = new List<T>();
            var seen = new HashSet<T>();
            var pending = new Stack<T>();
            Action<T> walk = top =>
            {
                pending.Push(top);
                while (pending.Count > 0)
                {
                    var f = pending.Pop();
                    if (!seen.Add(f))
                    {
                        continue;
                    }
                    result.Add(f);
                    List<T> list;
                    if (kids.TryGetValue(f, out list))
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            pending.Push(list[i]);
                        }
                    }
                }
            };
            foreach (var f in folders)
            {
                var p = parentOf(f);
                if (p == null || !all.Contains(p))
                {
                    walk(f);
                }
            }
            // Folders in a loop of parents: after the rest, as stored.
            foreach (var f in folders)
            {
                walk(f);
            }
            return result;
        }

        // plannerLayersV2 on the list's folders: each folder whose name reads a layer ID (or that stands for one), in outline
        // order, with its own work's span. A dated row's own work is its days, Start at 00:00 to the midnight after Finish.
        private List<PlannerFolderV2> PlannerLayersV2(IList<PlannerPlanFolder> folders)
        {
            var result = new List<PlannerFolderV2>();
            foreach (var folder in OutlineOrder(folders ?? new List<PlannerPlanFolder>(), f => f.Parent))
            {
                var id = folder.Id != null && folder.Id.Phase.HasValue ? folder.Id : PlannerNaming.FolderLayerId(folder.Name);
                if (id == null)
                {
                    continue;
                }
                var pl = new PlannerFolderV2 { Folder = folder, Id = id, Key = PlannerNaming.LayerIdKey(id) };
                if (folder.Start.HasValue || folder.Finish.HasValue)
                {
                    var first = (folder.Start ?? folder.Finish).Value.Date;
                    var last = (folder.Finish ?? folder.Start).Value.Date;
                    pl.HasSpan = true;
                    pl.SpanStart = MinutesBetween(ProjectStart, first);
                    pl.SpanEnd = pl.SpanStart + Math.Max(1, MinutesBetween(first, last.AddDays(1)));
                }
                result.Add(pl);
            }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------
        // layerSlotsV2

        /// <summary>Where one of a layer's events runs (<see cref="LayerSlotsV2"/>): minutes from the layer's start, end exclusive, and the chain event it follows (-1: none).</summary>
        public struct PlannerSlotV2
        {
            public long Start;
            public long End;
            public int After;
        }

        /// <summary>
        /// Where a layer's work runs over its span of <paramref name="total"/> minutes as the first Generate places it (spec 3.4:
        /// D5, D11, D19; the Planner's layerSlotsV2):
        ///  - the CHAIN, groups 1 and 2 (<paramref name="chain"/> events, in the D5 order), splits the span evenly, chained
        ///    finish-to-start, so it ends exactly on the layer's finish. A span shorter in minutes than the chain gives each
        ///    event a minute while it lasts, and the rest share its last minute, in parallel: no work runs past the finish;
        ///  - the END-OF-LAYER events, group 3 (one entry each in <paramref name="ends"/>), take no slot of their own: they run
        ///    in parallel over the last slot, or over the whole layer when the chain is empty (D11);
        ///  - an end-of-layer event whose own object holds the last slot with its leading event (ends[i] true) halves that slot
        ///    with it: the leading event the first half, rounded down, and this one the second (D19).
        /// </summary>
        public static void LayerSlotsV2(long total, int chain, IList<bool> ends, out List<PlannerSlotV2> chainSlots, out List<PlannerSlotV2> endSlots)
        {
            var slots = new List<PlannerSlotV2>();
            if (total >= chain)
            {
                long at = 0;
                var lengths = EvenSlots(total, chain);
                for (int i = 0; i < lengths.Count; i++)
                {
                    slots.Add(new PlannerSlotV2 { Start = at, End = at + lengths[i], After = i > 0 ? i - 1 : -1 });
                    at += lengths[i];
                }
            }
            else
            {
                for (int i = 0; i < chain; i++)
                {
                    // Past the span's last minute, in parallel over it.
                    long at = Math.Min(i, total - 1);
                    slots.Add(new PlannerSlotV2 { Start = at, End = at + 1, After = at > 0 ? (int)(at - 1) : -1 });
                }
            }
            bool hasLast = slots.Count > 0;
            long lastStart = hasLast ? slots[slots.Count - 1].Start : 0;
            long half = (long)Math.Floor((total - lastStart) / 2.0);
            // D19: one object holds the last slot, so one such event at most.
            int split = hasLast && half >= 1 ? ends.IndexOf(true) : -1;
            if (hasLast && split >= 0)
            {
                var last = slots[slots.Count - 1];
                last.End = lastStart + half;
                slots[slots.Count - 1] = last;
            }
            int lastAfter = hasLast ? slots[slots.Count - 1].After : -1;
            var endList = new List<PlannerSlotV2>();
            for (int i = 0; i < ends.Count; i++)
            {
                endList.Add(i == split
                    ? new PlannerSlotV2 { Start = lastStart + half, End = total, After = slots.Count - 1 }
                    : new PlannerSlotV2 { Start = lastStart, End = total, After = lastAfter });
            }
            chainSlots = slots;
            endSlots = endList;
        }

        // The even spread of a layer's work (spec 3.4): n slots over total minutes, each the total divided by their count,
        // rounded down, the last taking the remainder, so the slots end exactly on the layer's finish.
        private static List<long> EvenSlots(long total, int n)
        {
            var result = new List<long>();
            if (n <= 0)
            {
                return result;
            }
            long each = Math.Max(1, (long)Math.Floor((double)total / n));
            for (int i = 0; i < n; i++)
            {
                result.Add(i == n - 1 ? total - each * (n - 1) : each);
            }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------
        // buildFromLayersV2

        /// <summary>
        /// Naming v2's first Generate (spec 3, D4, D5, D11, D12, D19; buildFromLayersV2), over the Planner's folders as the
        /// list gives them, then the folders placed as the merge places them on a first Generate, the layers, the 4D events and
        /// the order check. False when it makes nothing (no layer, no event: the Planner's Generate would not land).
        ///  - FOLDERS come from layer IDs. Layer nodes sharing an ID are one layer (listed when their names differ). Each layer
        ///    takes the project's first folder with its ID in outline order (of the layer list's rows, the one the list check
        ///    keeps: the outline sorts them by name, D24); a layer with none gets a new folder named
        ///    &lt;ID&gt;_&lt;Name&gt;, TBC (D6), in its phase group's folder. Every layer node makes a folder, an empty one
        ///    included. A folder whose ID the scene has no layer for is kept, and listed; one a reference names takes that work.
        ///  - ONE ACTIVITY PER EVENT (D4), named &lt;own name&gt;_&lt;IN|RM&gt;_&lt;ID&gt; for its event layer. A leading tag is
        ///    in the object's own layer; a trailing tag at the end of it, or, naming another layer, in that layer's folder, or,
        ///    naming a layer nobody has, in the object's own folder, outside its span (spec 3.6).
        ///  - THE ORDER in a layer (D5): group 1, its objects' leading tags, removals first, then installs; group 2, other
        ///    layers' objects whose references name it, by the ID of the layer they sit in, installs and removals mixed; each by
        ///    own name, in natural order, hyphens read as underscores. Group 3, its objects' end-of-layer tags, removals first,
        ///    then installs, take no slot of their own (D11).
        ///  - DATES: a layer with dates splits them exactly (<see cref="LayerSlotsV2"/>). Any other layer with work is parked
        ///    TBC on a day of its own (D12), in layer-number order from the project start, and its folder records the day; but
        ///    a layer whose every event the project already has an activity for takes no day. A reference to a layer nobody
        ///    has is parked TBC: a removal the day after the last dated work ends (none dated: the day after the last parked
        ///    day), an install on the project start.
        /// </summary>
        private bool BuildFromLayersV2(PlannerLayerNodeIndex nodes, Dictionary<string, string> oldLayers, Dictionary<string, string> splits, PlannerScheduleOptions options)
        {
            var anchor = ProjectStart;
            var report = V2;
            // 1. The model's layers, by ID, and its objects' events.
            var read = ReadModelV2(nodes, oldLayers, splits);
            var events = read.Events;
            report.Matched.AddRange(read.Matched);
            report.Skipped.AddRange(read.Skipped);
            report.Unfiled.AddRange(read.Unfiled);
            report.OtherNaming.AddRange(read.OtherNaming);
            report.SharedIds.AddRange(read.SharedIds);
            report.NumberedBindings.AddRange(read.Matched.Where(n => DupSuffix.IsMatch(n)));
            LayerIssues.AddRange(read.LayerIssues);
            foreach (var pair in nodes.Objects)
            {
                LayerOf[pair.Key] = pair.Value;
            }
            foreach (var obj in read.IssueObjects)
            {
                OwnLayers[obj.Name] = obj.Layer;
            }

            // 2. The Planner's layer folders, by ID: the first in outline order is the layer's (of the list's rows, the one the
            // list keeps). Those sharing an ID are listed once every layer has its folder.
            var planned = PlannerLayersV2(options.Folders);
            report.OneTbcDayRows.AddRange(planned.Where(pl => OneTbcDay(pl.Folder)).Select(pl => pl.Folder.Name));
            var plannerByKey = new Dictionary<string, PlannerFolderV2>(StringComparer.Ordinal);
            var withKey = new Dictionary<string, List<PlannerFolderV2>>(StringComparer.Ordinal);
            var keysInOrder = new List<string>();
            foreach (var pl in planned)
            {
                List<PlannerFolderV2> list;
                if (!withKey.TryGetValue(pl.Key, out list))
                {
                    list = new List<PlannerFolderV2>();
                    withKey[pl.Key] = list;
                    plannerByKey[pl.Key] = pl;
                    keysInOrder.Add(pl.Key);
                }
                list.Add(pl);
            }
            // Of the layer list's rows sharing an ID, the layer takes the row the list check keeps (PlannerLayerList: the first
            // in the file without a problem; the others are its problems), not the first in the outline, which sorts siblings by
            // name (D24). The Planner's own folders carry no row: the first in outline order, as its Generate takes it.
            foreach (var key in keysInOrder)
            {
                var sharing = withKey[key];
                if (sharing.Count > 1 && sharing.All(pl => pl.Folder.Row != null))
                {
                    var kept = sharing.OrderBy(pl => pl.Folder.Row.HasProblem ? 1 : 0).ThenBy(pl => pl.Folder.Row.Index).ToList();
                    sharing.Clear();
                    sharing.AddRange(kept);
                    plannerByKey[key] = kept[0];
                }
            }

            // 3. The objects' naming issues, read against every layer ID the model or the Planner has, their split pieces included.
            var knownIds = read.Layers.Select(l => l.Id).Concat(planned.Select(pl => pl.Id)).ToList();
            report.NamingIssues.AddRange(PlannerNaming.NamingIssuesV2(read.IssueObjects, knownIds, splits).Where(i => i.Code != PlannerNamingCode.OldTag));

            // 4. The layers work goes in: the model's, and a Planner folder a reference names (spec 3.6). The guard of spec 3.1
            // lets a layer take a folder holding none of the model's work, or some of its own: in the preview no folder holds
            // the model's work (a row's folder holds its days alone), so each layer takes the first free folder with its ID,
            // and the later passes (the lenient guard, the work fallback of spec 4.1, an empty layer's own ID) find nothing more.
            var taken = new HashSet<PlannerFolderV2>();
            var atOf = new Dictionary<string, PlannerFolderV2>(StringComparer.Ordinal);
            Func<string, PlannerFolderV2> folderFor = key =>
            {
                List<PlannerFolderV2> list;
                return withKey.TryGetValue(key, out list) ? list.FirstOrDefault(pl => !taken.Contains(pl)) : null;
            };
            var layersInOrder = PlannerNaturalOrder.StableSort(read.Layers, (x, y) => PlannerNaming.CompareLayerIds(x.Id, y.Id));
            foreach (var layer in layersInOrder)
            {
                var at = folderFor(layer.Key);
                if (at != null)
                {
                    atOf[layer.Key] = at;
                    taken.Add(at);
                }
            }
            foreach (var key in keysInOrder)
            {
                if (!read.ByKey.ContainsKey(key) && !taken.Contains(plannerByKey[key]))
                {
                    report.FoldersWithoutLayer.Add(withKey[key][0].Folder.Name);
                }
            }
            // A folder found by its ID keeps its name: a name after the ID other than the model layer's is noted.
            foreach (var layer in layersInOrder)
            {
                PlannerFolderV2 at;
                if (!atOf.TryGetValue(layer.Key, out at) || at.Key != layer.Key)
                {
                    continue;
                }
                var v2 = PlannerNaming.ParseLayerNameV2(at.Folder.Name);
                var old = v2 == null ? PlannerNaming.ParseLayerName(at.Folder.Name) : null;
                var named = v2 != null ? v2.Name : old != null ? old.Name : string.Empty;
                var model = PlannerNaming.ParseLayerNameV2(layer.Labels[0]);
                if (SpellingKey(named) != SpellingKey(model != null ? model.Name : string.Empty))
                {
                    report.RenamedLayers.Add(new PlannerRenamedLayer { Folder = at.Folder.Name, Layer = layer.Labels[0] });
                }
            }
            var plan = new Dictionary<string, PlanLayerV2>(StringComparer.Ordinal);
            var planOrder = new List<PlanLayerV2>();
            Func<string, PlannerLayerIdKey, ModelLayerV2, PlannerFolderV2, PlanLayerV2> planLayer = (key, id, model, at) =>
            {
                var P = new PlanLayerV2 { Id = id, Key = key, Model = model, At = at };
                plan[key] = P;
                planOrder.Add(P);
                return P;
            };
            foreach (var layer in read.Layers)
            {
                PlannerFolderV2 at;
                atOf.TryGetValue(layer.Key, out at);
                planLayer(layer.Key, layer.Id, layer, at);
            }
            foreach (var e in events)
            {
                var key = PlannerNaming.LayerIdKey(e.Layer);
                PlanLayerV2 P;
                if (!plan.TryGetValue(key, out P))
                {
                    var at = folderFor(key);
                    if (at != null)
                    {
                        P = planLayer(key, at.Id, null, at);
                    }
                }
                if (P == null)
                {
                    plan[e.Home.Key].Missing.Add(e);
                }
                else
                {
                    (e.Group == 1 ? P.G1 : e.Group == 2 ? P.G2 : P.G3).Add(e);
                }
            }

            // 5. Order each layer's work (D5), date it and emit it, in layer-number order.
            Comparison<EventV2> byOwn = (x, y) =>
            {
                int c = PlannerNaturalOrder.Compare(x.Own.Replace('-', '_'), y.Own.Replace('-', '_'));
                return c != 0 ? c : PlannerNaturalOrder.Compare(x.Object, y.Object);
            };
            Func<EventV2, int> removalFirst = e => e.Type == PlannerTaskType.Dismantle ? 0 : 1;
            var sorted = PlannerNaturalOrder.StableSort(planOrder, (x, y) => PlannerNaming.CompareLayerIds(x.Id, y.Id));
            foreach (var P in sorted)
            {
                var g1 = PlannerNaturalOrder.StableSort(P.G1, (x, y) => { int r = removalFirst(x) - removalFirst(y); return r != 0 ? r : byOwn(x, y); });
                var g2 = PlannerNaturalOrder.StableSort(P.G2, (x, y) => { int r = PlannerNaming.CompareLayerIds(x.Home.Id, y.Home.Id); return r != 0 ? r : byOwn(x, y); });
                var g3 = PlannerNaturalOrder.StableSort(P.G3, (x, y) => { int r = removalFirst(x) - removalFirst(y); return r != 0 ? r : byOwn(x, y); });
                P.G1.Clear();
                P.G1.AddRange(g1);
                P.G2.Clear();
                P.G2.AddRange(g2);
                P.G3.Clear();
                P.G3.AddRange(g3);
            }
            var folderOfActivity = new Dictionary<PlannerScheduleActivity, GenFolderV2>();
            Func<EventV2, GenFolderV2, long, bool, PlannerScheduleActivity> makeActivity = (e, folder, duration, tbc) =>
            {
                var a = new PlannerScheduleActivity
                {
                    Name = PlannerNaming.ActivityNameV2(e.Own, e.Type, e.Layer),
                    Type = e.Type,
                    Object = e.Object,
                    EventLayer = e.Layer,
                    Group = e.Group,
                    Duration = Math.Max(1, duration),
                    Tbc = tbc
                };
                a.Members.Add(e.Object);
                Activities.Add(a);
                folderOfActivity[a] = folder;
                if (tbc)
                {
                    report.TbcActivities.Add(a.Name);
                }
                return a;
            };
            // A layer's work over its span, where LayerSlotsV2 puts it: groups 1 and 2 chained, group 3 over the last slot.
            Action<PlanLayerV2, GenFolderV2, bool> place = (P, folder, tbc) =>
            {
                var chain = P.G1.Concat(P.G2).ToList();
                var holder = chain.Count > 0 ? chain[chain.Count - 1] : null;
                List<PlannerSlotV2> chainSlots, endSlots;
                LayerSlotsV2(P.SpanEnd - P.SpanStart, chain.Count, P.G3.Select(e => holder != null && holder.Group == 1 && holder.Object == e.Object).ToList(),
                    out chainSlots, out endSlots);
                var made = new List<PlannerScheduleActivity>();
                Func<EventV2, PlannerSlotV2, PlannerScheduleActivity> emit = (e, slot) =>
                {
                    var a = makeActivity(e, folder, slot.End - slot.Start, tbc);
                    if (slot.After < 0)
                    {
                        a.OffsetMinutes = P.SpanStart + slot.Start;
                        a.StartMinute = a.OffsetMinutes;
                    }
                    else
                    {
                        // Finish-to-start, contiguous.
                        a.After = made[slot.After];
                        a.StartMinute = a.After.StartMinute + a.After.Duration;
                        report.DependencyCount++;
                    }
                    return a;
                };
                for (int i = 0; i < chain.Count; i++)
                {
                    made.Add(emit(chain[i], chainSlots[i]));
                }
                for (int i = 0; i < P.G3.Count; i++)
                {
                    emit(P.G3[i], endSlots[i]);
                }
            };
            // The work the project's activities bind already, each activity once: a layer whose every event has one makes no day.
            var bound = new Dictionary<string, List<PlannerExistingWork>>(StringComparer.Ordinal);
            foreach (var work in options.ExistingWork ?? new List<PlannerExistingWork>())
            {
                foreach (var obj in work.Objects)
                {
                    var key = obj + "|" + TypeToken(work.Type);
                    List<PlannerExistingWork> list;
                    if (!bound.TryGetValue(key, out list))
                    {
                        list = new List<PlannerExistingWork>();
                        bound[key] = list;
                    }
                    list.Add(work);
                }
            }
            Func<PlanLayerV2, bool> allKept = P =>
            {
                var used = new HashSet<PlannerExistingWork>();
                foreach (var e in P.G1.Concat(P.G2).Concat(P.G3))
                {
                    List<PlannerExistingWork> list;
                    var a = bound.TryGetValue(e.Object + "|" + TypeToken(e.Type), out list) ? list.FirstOrDefault(x => !used.Contains(x)) : null;
                    if (a == null)
                    {
                        return false;
                    }
                    used.Add(a);
                }
                return true;
            };
            // Parked layers' days run on from the project start (no folder of the list's is still parked: the list writes a
            // parked layer's days as its dates, TBC).
            var parkFrom = anchor;
            int parked = 0;
            var gen = new List<GenFolderV2>();
            var groupFolders = new Dictionary<string, GenFolderV2>(StringComparer.Ordinal);
            // phaseGroupFolders: a group's folder, made when the first layer folder it holds is, just before it, and after the
            // group it sits in; a group met again on one walk up is taken as the top.
            Func<string, HashSet<string>, GenFolderV2> groupFolder = null;
            groupFolder = (raw, walk) =>
            {
                var label = ParsePhaseGroup(raw);
                if (label == null)
                {
                    return null;
                }
                GenFolderV2 madeGroup;
                if (groupFolders.TryGetValue(label, out madeGroup))
                {
                    return madeGroup;
                }
                if (!walk.Add(label))
                {
                    return null;
                }
                string above;
                var parent = nodes.Groups.TryGetValue(raw, out above) && above.Length > 0 ? groupFolder(above, walk) : null;
                var f = new GenFolderV2 { Name = label, Parent = parent };
                groupFolders[label] = f;
                gen.Add(f);
                return f;
            };
            var madeFor = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var P in sorted)
            {
                int work = P.G1.Count + P.G2.Count + P.G3.Count;
                bool tbc = false;
                DateTime? parkedOn = null;
                if (work > 0 && P.At != null && P.At.HasSpan)
                {
                    // Its own work's dates in the Planner.
                    P.HasSpan = true;
                    P.SpanStart = P.At.SpanStart;
                    P.SpanEnd = P.At.SpanEnd;
                }
                else if (work > 0)
                {
                    // Parked on a day of its own (D12), but not a layer whose every event the project has already.
                    var day = parkFrom.AddDays(parked);
                    long start = MinutesBetween(anchor, day);
                    P.HasSpan = true;
                    P.SpanStart = start;
                    P.SpanEnd = start + MinutesBetween(day, day.AddDays(1));
                    tbc = true;
                    if (!allKept(P))
                    {
                        parked++;
                        parkedOn = day;
                    }
                }
                // A folder the Planner has keeps its place, unless it is still named as the model names the layer: that one
                // sits in its phase group's folder, as today (the merge files it there when it adds that folder).
                bool placed = P.At != null && (P.Model == null || SpellingKey(P.At.Folder.Name) != SpellingKey(P.Model.Labels[0]));
                var group = P.Model != null && P.Model.Group != null && !placed ? groupFolder(P.Model.Group, new HashSet<string>(StringComparer.Ordinal)) : null;
                var folder = new GenFolderV2
                {
                    Name = P.At != null ? P.At.Folder.Name : P.Model.Labels[0],
                    Parent = group,
                    Claim = P.At != null ? P.At.Folder : null,
                    // A new layer folder is TBC (D6); one the Planner has keeps its flag (D13).
                    Tbc = P.At != null ? P.At.Folder.Tbc : true,
                    ParkedOn = parkedOn,
                    Plan = P
                };
                gen.Add(folder);
                P.Folder = folder;
                P.ParkedOn = parkedOn;
                if (parkedOn.HasValue)
                {
                    report.ParkedLayers.Add(new PlannerParkedLayer { Folder = folder.Name, Day = parkedOn.Value });
                }
                if (P.HasSpan)
                {
                    place(P, folder, tbc);
                }
                if (P.At == null)
                {
                    madeFor[P.Key] = folder.Name;
                }
            }
            // Folders sharing an ID (spec 3.1), the one holding the layer's work first.
            foreach (var key in keysInOrder)
            {
                var pls = withKey[key];
                PlanLayerV2 P;
                plan.TryGetValue(key, out P);
                var own = P != null && P.At != null && P.At.Key == key ? P.At : null;
                string first;
                if (own != null)
                {
                    first = own.Folder.Name;
                }
                else if (P == null || P.At != null || !madeFor.TryGetValue(key, out first))
                {
                    first = null;
                }
                var shared = new PlannerSharedId { Id = PlannerNaming.LayerIdText(pls[0].Id), Folders = true };
                if (first != null)
                {
                    shared.Names.Add(first);
                }
                shared.Names.AddRange(pls.Where(pl => pl != own).Select(pl => pl.Folder.Name));
                if (shared.Names.Count > 1)
                {
                    report.SharedIds.Add(shared);
                }
            }
            // References to a layer nobody has: the removal after the works end, the install on the project start (spec 3.6).
            var datedEnds = planned.Where(pl => pl.HasSpan).Select(pl => pl.SpanEnd)
                .Concat(sorted.Where(P => P.HasSpan && !P.ParkedOn.HasValue && P.At != null && P.At.HasSpan).Select(P => P.SpanEnd)).ToList();
            var parkedDays = report.ParkedLayers.Select(p => p.Day).OrderBy(d => d).ToList();
            var afterWorks = datedEnds.Count > 0
                ? LocalTime(AnchorMs + (datedEnds.Max() - 1) * 60000L).Date.AddDays(1)
                : parkedDays.Count > 0 ? parkedDays[parkedDays.Count - 1].AddDays(1) : anchor.AddDays(1);
            WorksEnd = afterWorks.AddDays(-1);
            WorksEndFrom = datedEnds.Count > 0 ? PlannerWorksEndFrom.DatedWork : parkedDays.Count > 0 ? PlannerWorksEndFrom.ParkedDay : PlannerWorksEndFrom.ProjectStart;
            foreach (var P in sorted)
            {
                foreach (var e in P.Missing)
                {
                    var day = e.Type == PlannerTaskType.Dismantle ? afterWorks : anchor;
                    var a = makeActivity(e, P.Folder, MinutesBetween(day, day.AddDays(1)), true);
                    a.Group = 0;
                    a.Unresolved = true;
                    a.OffsetMinutes = MinutesBetween(anchor, day);
                    a.StartMinute = a.OffsetMinutes;
                    report.MissingLayers.Add(new PlannerMissingLayer { ActivityName = a.Name, Layer = PlannerNaming.LayerIdText(e.Layer) });
                }
            }
            if (planOrder.Count == 0)
            {
                return false;
            }

            // The project's folders once the merge has placed them, and the layers.
            PlaceFoldersV2(options.Folders ?? new List<PlannerPlanFolder>(), gen, folderOfActivity);
            MakeLayersV2(planned, read, sorted);
            foreach (var a in Activities)
            {
                // The folder an event is filed in: its column, its place and its home (spec 5.6, 5.13).
                var layer = folderOfActivity[a].Final.Layer;
                a.Home = layer;
                a.DatedBy = layer;
                a.IsOwn = !a.Unresolved && layer != null && layer.Id != null && PlannerNaming.LayerIdKey(layer.Id) == PlannerNaming.LayerIdKey(a.EventLayer);
            }

            // The scheduler's dates and the instants the 4D view reads from them, as on the old names.
            foreach (var activity in Activities)
            {
                var local = LocalTime(AnchorMs + activity.StartMinute * 60000L);
                activity.StartMs = LocalInstant(local.Date, local.Hour * 60 + local.Minute);
                activity.EndMs = activity.StartMs + activity.Duration * 60000L;
            }
            var byName = PlannerNaturalOrder.StableSort(Activities, (a, b) => PlannerNaturalOrder.Compare(a.Name, b.Name));
            Activities.Clear();
            Activities.AddRange(byName);
            var tbcInOrder = Activities.Where(a => a.Tbc).Select(a => a.Name).ToList();
            report.TbcActivities.Clear();
            report.TbcActivities.AddRange(tbcInOrder);
            // duplicateActivityNames: every name counted, then those with two or more copies, in the order of their first copy.
            var seenNames = new Dictionary<string, int>(StringComparer.Ordinal);
            var namesInOrder = new List<string>();
            foreach (var a in Activities)
            {
                int n;
                if (!seenNames.TryGetValue(a.Name, out n))
                {
                    namesInOrder.Add(a.Name);
                }
                seenNames[a.Name] = n + 1;
            }
            report.DuplicateNames.AddRange(namesInOrder.Where(name => seenNames[name] > 1));

            // The objects and their 4D events (timeline.ts buildMeshEvents).
            foreach (var obj in read.Matched)
            {
                if (Objects.ContainsKey(obj))
                {
                    continue;
                }
                PlannerModelNode node;
                Index.TryGetValue(obj, out node);
                var home = events.First(e => e.Object == obj).Home;
                Objects[obj] = new PlannerScheduleObject
                {
                    Name = obj,
                    MaxName = node != null ? node.MaxName : obj,
                    Key = node != null ? node.Key : 0,
                    NameV2 = read.Parsed[obj],
                    FiledIn = plan[home.Key].Folder.Final.Layer
                };
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
            // Earliest first; at one instant a removal before an install.
            foreach (var name in Events.Keys.ToList())
            {
                Events[name] = PlannerNaturalOrder.StableSort(Events[name], (x, y) =>
                {
                    if (x.Instant != y.Instant)
                    {
                        return x.Instant < y.Instant ? -1 : 1;
                    }
                    return x.Install == y.Install ? 0 : x.Install ? 1 : -1;
                });
            }
            List<string> ifDated;
            report.WrongOrder.AddRange(WrongOrderV2(read.Matched, out ifDated));
            report.WrongOrderIfDated.AddRange(ifDated);
            return true;
        }

        // ---------------------------------------------------------------------------------------------------------
        // The folders after the merge (mergeGeneratedPhasing on a first Generate)

        private static readonly Regex SegmentScheme = new Regex(@"^PH[0-9]+(?:_St[0-9]+(?:[._-][0-9]+)?)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The project's folders once the store's merge has taken the Generate's (mergeGeneratedPhasing, as it runs on a first
        /// Generate, where no activity is kept): each folder the Generate found by its ID is that folder; a phase group's folder
        /// is the project's folder of its name, when one alone has it, else the folder all the project's folders it holds sit
        /// in (when at least half of what it holds is placed); the rest are added. A folder the Planner has at the top, still
        /// named as the Generate names it, is filed in a group's folder the merge adds; a group's folder that would hold nothing
        /// is not added. A folder keeps its own TBC flag (D13); a layer the Generate parked records its day where work was added.
        /// </summary>
        private void PlaceFoldersV2(List<PlannerPlanFolder> existing, List<GenFolderV2> gen, Dictionary<PlannerScheduleActivity, GenFolderV2> folderOfActivity)
        {
            var claimed = new HashSet<PlannerPlanFolder>();
            var genFor = new Dictionary<PlannerPlanFolder, GenFolderV2>();
            Action<GenFolderV2, PlannerPlanFolder> claim = (f, match) =>
            {
                claimed.Add(match);
                genFor[match] = f;
                f.Match = match;
            };
            foreach (var f in gen)
            {
                if (f.Claim != null && !claimed.Contains(f.Claim))
                {
                    claim(f, f.Claim);
                }
            }
            // A new-name layer folder is matched by its ID alone; a group's folder by its name, when unambiguous on both sides
            // (no folder of the project holds the model's work, so no other candidate overlaps).
            var genByName = gen.GroupBy(f => SpellingKey(f.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var f in gen)
            {
                if (f.Match != null || PlannerNaming.FolderLayerId(f.Name) != null)
                {
                    continue;
                }
                var key = SpellingKey(f.Name);
                var sameName = existing.Where(c => !claimed.Contains(c) && SpellingKey(c.Name) == key).ToList();
                if (sameName.Count == 1 && genByName[key] == 1)
                {
                    claim(f, sameName[0]);
                }
            }
            // A folder holding only folders is the project's folder its placed subfolders all sit in, on the same terms: at least
            // half of what it holds directly placed there. Subfolders go first.
            Func<GenFolderV2, int> depthOf = f =>
            {
                int d = 0;
                for (var p = f.Parent; p != null && d < 64; p = p.Parent)
                {
                    d++;
                }
                return d;
            };
            var activitiesIn = new Dictionary<GenFolderV2, List<PlannerScheduleActivity>>();
            foreach (var pair in folderOfActivity)
            {
                List<PlannerScheduleActivity> list;
                if (!activitiesIn.TryGetValue(pair.Value, out list))
                {
                    list = new List<PlannerScheduleActivity>();
                    activitiesIn[pair.Value] = list;
                }
                list.Add(pair.Key);
            }
            Func<PlannerPlanFolder, bool> takeable = c => !claimed.Contains(c) && !SegmentScheme.IsMatch(c.Name) && PlannerNaming.ParseMeshName(c.Name) == null
                && PlannerNaming.FolderLayerId(c.Name) == null;
            foreach (var f in PlannerNaturalOrder.StableSort(gen.Where(g => g.Match == null), (x, y) => depthOf(y) - depthOf(x)))
            {
                var layerId = PlannerNaming.FolderLayerId(f.Name);
                List<PlannerScheduleActivity> acts;
                activitiesIn.TryGetValue(f, out acts);
                int own = (acts ?? new List<PlannerScheduleActivity>()).Count(a => layerId == null || PlannerNaming.LayerIdKey(a.EventLayer) == PlannerNaming.LayerIdKey(layerId));
                var kids = gen.Where(k => k.Parent == f).ToList();
                var placedKids = kids.Where(k => k.Match != null).ToList();
                var parents = placedKids.Select(k => k.Match.Parent).Distinct().ToList();
                var parent = parents.Count == 1 ? parents[0] : null;
                if (parent != null && takeable(parent) && placedKids.Count * 2 >= own + kids.Count)
                {
                    claim(f, parent);
                }
            }

            // The folders: the project's, then the added ones, each in its folder.
            var finalOf = new Dictionary<PlannerPlanFolder, PlannerScheduleFolder>();
            var result = new List<PlannerScheduleFolder>();
            foreach (var c in existing)
            {
                var final = new PlannerScheduleFolder
                {
                    Name = c.Name,
                    Tbc = c.Tbc,
                    From = c,
                    Id = c.Id != null && c.Id.Phase.HasValue ? c.Id : PlannerNaming.FolderLayerId(c.Name)
                };
                finalOf[c] = final;
                result.Add(final);
            }
            foreach (var c in existing)
            {
                PlannerScheduleFolder parent;
                finalOf[c].Parent = c.Parent != null && finalOf.TryGetValue(c.Parent, out parent) ? parent : null;
            }
            var added = new List<PlannerScheduleFolder>();
            foreach (var f in gen)
            {
                if (f.Match != null)
                {
                    f.Final = finalOf[f.Match];
                    continue;
                }
                f.Final = new PlannerScheduleFolder
                {
                    Name = f.Name,
                    Tbc = f.Tbc,
                    Id = f.Plan != null ? f.Plan.Id : PlannerNaming.FolderLayerId(f.Name)
                };
                added.Add(f.Final);
            }
            foreach (var f in gen.Where(g => g.Match == null))
            {
                f.Final.Parent = f.Parent != null ? f.Parent.Final : null;
            }
            var isNew = new HashSet<PlannerScheduleFolder>(added);
            Func<PlannerScheduleFolder, PlannerScheduleFolder, bool> insideOf = (id, top) =>
            {
                int hops = 0;
                for (var p = id; p != null && hops <= result.Count + added.Count; p = p.Parent, hops++)
                {
                    if (p == top)
                    {
                        return true;
                    }
                }
                return false;
            };
            // A folder at the top still named as the Generate names it is filed in the folder the model nests it in, when this
            // merge adds that folder (a layer's folder in its phase group's).
            foreach (var c in existing)
            {
                GenFolderV2 g;
                if (!genFor.TryGetValue(c, out g) || c.Parent != null || g.Parent == null)
                {
                    continue;
                }
                if (SpellingKey(c.Name) != SpellingKey(g.Name) && !IsPriorFolderName(c.Name, g.Name))
                {
                    continue;
                }
                var to = g.Parent.Final;
                if (!isNew.Contains(to) || insideOf(to, finalOf[c]))
                {
                    continue;
                }
                finalOf[c].Parent = to;
            }
            // A group's folder added that would hold nothing is not added, nor an outer group that leaves empty.
            var holding = new Dictionary<PlannerScheduleFolder, int>();
            Action<PlannerScheduleFolder> hold = f =>
            {
                if (f == null)
                {
                    return;
                }
                int n;
                holding.TryGetValue(f, out n);
                holding[f] = n + 1;
            };
            foreach (var g in folderOfActivity.Values)
            {
                hold(g.Final);
            }
            foreach (var f in result.Concat(added))
            {
                hold(f.Parent);
            }
            var holdsFolders = new HashSet<GenFolderV2>(gen.Where(g => g.Parent != null).Select(g => g.Parent));
            var dropped = new HashSet<PlannerScheduleFolder>();
            foreach (var f in PlannerNaturalOrder.StableSort(gen.Where(g => g.Match == null), (x, y) => depthOf(y) - depthOf(x)))
            {
                int n;
                holding.TryGetValue(f.Final, out n);
                if (!holdsFolders.Contains(f) || activitiesIn.ContainsKey(f) || n > 0)
                {
                    continue;
                }
                dropped.Add(f.Final);
                if (f.Final.Parent != null)
                {
                    int m;
                    if (!holding.TryGetValue(f.Final.Parent, out m))
                    {
                        m = 1;
                    }
                    holding[f.Final.Parent] = m - 1;
                }
            }
            // A layer's parked day is recorded on its folder only where the merge adds work to it (spec 3.5).
            var addedIn = new HashSet<PlannerScheduleFolder>(folderOfActivity.Values.Select(g => g.Final));
            foreach (var f in gen.Where(g => g.ParkedOn.HasValue && addedIn.Contains(g.Final)))
            {
                f.Final.ParkedOn = f.ParkedOn;
            }
            // A new folder is listed before the first folder it holds.
            var folders = result.Concat(added.Where(f => !dropped.Contains(f))).ToList();
            foreach (var f in added.Where(f => !dropped.Contains(f)))
            {
                int at = folders.IndexOf(f);
                int first = folders.FindIndex(c => c != f && insideOf(c, f));
                if (first != -1 && first < at)
                {
                    folders.RemoveAt(at);
                    folders.Insert(first, f);
                }
            }
            Folders.Clear();
            Folders.AddRange(folders);
        }

        // isOlderSpellingOf: name is target with some of it still spelled an older way (a sub-stage's dot or hyphen, another
        // hyphen, for the underscore; the dot for the hyphen), or target itself.
        private static bool IsOlderSpellingOf(string name, string target)
        {
            if (name.Length != target.Length || SpellingKey(name) != SpellingKey(target))
            {
                return false;
            }
            for (int i = 0; i < name.Length; i++)
            {
                char was = name[i];
                char now = target[i];
                if (was != now && !((was == '.' || was == '-') && now == '_') && !(was == '.' && now == '-'))
                {
                    return false;
                }
            }
            return true;
        }

        // isPriorFolderName: name was given by an earlier generation to the folder now called current (its label without its
        // number, or either spelled an older way).
        private static bool IsPriorFolderName(string name, string current)
        {
            var p = PlannerNaming.ParseLayerName(current);
            var tail = p != null ? LabelTail(p) : string.Empty;
            string prior = tail.Length > 0 && tail != current ? tail : null;
            return IsOlderSpellingOf(name, current) || (prior != null && IsOlderSpellingOf(name, prior));
        }

        // The layers: one per folder with a layer ID, in layer-number order (then outline order). A plan layer's has its scene
        // nodes and the span its work was placed over (its dates, or its parked day); any other's, its list row's dates.
        private void MakeLayersV2(List<PlannerFolderV2> planned, ModelReadV2 read, List<PlanLayerV2> plans)
        {
            var plannedOf = new Dictionary<PlannerPlanFolder, PlannerFolderV2>();
            foreach (var pl in planned)
            {
                if (!plannedOf.ContainsKey(pl.Folder))
                {
                    plannedOf[pl.Folder] = pl;
                }
            }
            var layers = new List<PlannerScheduleLayer>();
            var dated = new HashSet<PlannerScheduleLayer>();
            foreach (var f in OutlineOrder(Folders, x => x.Parent))
            {
                if (f.Id == null)
                {
                    continue;
                }
                var layer = new PlannerScheduleLayer
                {
                    Label = f.Name,
                    Raw = f.Name,
                    Id = f.Id,
                    Folder = f,
                    Phase = f.Id.Phase,
                    ListRow = f.From != null ? f.From.Row : null,
                    InScene = read.ByKey.ContainsKey(PlannerNaming.LayerIdKey(f.Id)),
                    Start = ProjectStart,
                    End = ProjectStart
                };
                f.Layer = layer;
                layers.Add(layer);
            }
            foreach (var P in plans)
            {
                var layer = P.Folder != null && P.Folder.Final != null ? P.Folder.Final.Layer : null;
                if (layer == null)
                {
                    continue;
                }
                if (P.Model != null)
                {
                    layer.Raw = P.Model.Nodes[0];
                    layer.Group = P.Model.Group;
                    foreach (var node in P.Model.Nodes)
                    {
                        PlannerModelNode n;
                        if (Index.TryGetValue(node, out n) && !layer.NodeKeys.Contains(n.Key))
                        {
                            layer.NodeKeys.Add(n.Key);
                        }
                    }
                }
                if (P.HasSpan)
                {
                    SetDays(layer, P.SpanStart, P.SpanEnd);
                    layer.ParkedOn = P.ParkedOn;
                    layer.DateSource = P.At != null && P.At.HasSpan ? P.At.Folder.Source : PlannerDateSource.None;
                    dated.Add(layer);
                }
            }
            foreach (var layer in layers.Where(l => !dated.Contains(l)))
            {
                PlannerFolderV2 pl;
                if (layer.Folder.From != null && plannedOf.TryGetValue(layer.Folder.From, out pl) && pl.HasSpan)
                {
                    SetDays(layer, pl.SpanStart, pl.SpanEnd);
                    layer.DateSource = pl.Folder.Source;
                    dated.Add(layer);
                }
            }
            // As the old names read a layer's name: its ID, its name, its dates and TBC (its folder's, a folder's above it, or
            // parked), here as the Planner gives them.
            foreach (var layer in layers)
            {
                var name = PlannerNaming.ParseLayerNameV2(layer.Label);
                bool isDated = dated.Contains(layer);
                layer.Parsed = new PlannerLayerName
                {
                    Order = layer.Id.Phase.Value.ToString("00", CultureInfo.InvariantCulture),
                    Phase = layer.Id.Phase,
                    Stage = layer.Id.Stage,
                    SubStage = layer.Id.SubStage,
                    Name = name != null ? name.Name : layer.Label,
                    Label = layer.Label,
                    Tbc = layer.Folder.TbcHere || layer.ParkedOn.HasValue,
                    Start = isDated ? layer.Start : (DateTime?)null,
                    End = isDated ? layer.End : (DateTime?)null
                };
            }
            var inOrder = PlannerNaturalOrder.StableSort(layers, (x, y) => PlannerNaming.CompareLayerIds(x.Id, y.Id));
            for (int i = 0; i < inOrder.Count; i++)
            {
                inOrder[i].Order = i;
            }
            Layers.Clear();
            Layers.AddRange(inOrder);
        }

        // A layer's first day and the day of its last minute.
        private void SetDays(PlannerScheduleLayer layer, long start, long end)
        {
            layer.Start = LocalTime(AnchorMs + start * 60000L).Date;
            layer.End = LocalTime(AnchorMs + Math.Max(start, end - 1) * 60000L).Date;
        }

        // ---------------------------------------------------------------------------------------------------------
        // wrongOrderV2

        /// <summary>
        /// The objects whose two events the schedule runs the other way round from their name (spec 3.7; wrongOrderV2): taken
        /// away then put back (RM_X_IN...), or put up then taken down (IN_X_RM...), as the 4D view applies them. A pair is
        /// skipped while either event's layer is still parked, or when an event is named for no layer; a dated layer that is
        /// still TBC is checked (D13), and so is an event named for a layer no folder has. <paramref name="ifDated"/>: those of
        /// them with an event in a layer the list shows on one TBC day, which the Planner skips if it still has it parked.
        /// </summary>
        private List<string> WrongOrderV2(IEnumerable<string> objects, out List<string> ifDated)
        {
            var result = new List<string>();
            ifDated = new List<string>();
            // The first folder with each ID, in outline order.
            var byKey = new Dictionary<string, PlannerScheduleFolder>(StringComparer.Ordinal);
            foreach (var f in OutlineOrder(Folders, x => x.Parent))
            {
                if (f.Id != null && !byKey.ContainsKey(PlannerNaming.LayerIdKey(f.Id)))
                {
                    byKey[PlannerNaming.LayerIdKey(f.Id)] = f;
                }
            }
            // Object and type: the first activity binding it so.
            var eventOf = new Dictionary<string, PlannerScheduleActivity>(StringComparer.Ordinal);
            foreach (var a in Activities)
            {
                foreach (var m in a.Members)
                {
                    var key = m + "|" + TypeToken(a.Type);
                    if (!eventOf.ContainsKey(key))
                    {
                        eventOf[key] = a;
                    }
                }
            }
            // The folder of an activity's event layer: the first with its ID, or null for a layer no folder has.
            Func<PlannerScheduleActivity, PlannerScheduleFolder> layerFolder = a =>
            {
                var folder = a.Home != null ? a.Home.Folder : null;
                var id = PlannerNaming.ActivityEventLayer(a.Name, folder != null ? folder.Name : null);
                PlannerScheduleFolder f;
                return id != null && byKey.TryGetValue(PlannerNaming.LayerIdKey(id), out f) ? f : null;
            };
            Func<PlannerScheduleActivity, bool> isDated = a =>
            {
                var folder = a.Home != null ? a.Home.Folder : null;
                var id = PlannerNaming.ActivityEventLayer(a.Name, folder != null ? folder.Name : null);
                if (id == null)
                {
                    return false;
                }
                // A layer no folder has is never still parked.
                var f = layerFolder(a);
                return f == null || !StillParked(f);
            };
            Func<PlannerScheduleActivity, bool> oneTbcDay = a =>
            {
                var f = layerFolder(a);
                return f != null && OneTbcDay(f.From);
            };
            foreach (var obj in objects)
            {
                var p = PlannerNaming.ParseObjectNameV2(obj);
                if (p.Kind != PlannerNamingKind.New || !p.Lead.HasValue || !p.Trail.HasValue)
                {
                    continue;
                }
                PlannerScheduleActivity first, second;
                if (!eventOf.TryGetValue(obj + "|" + TypeToken(p.Lead.Value), out first) || !eventOf.TryGetValue(obj + "|" + TypeToken(p.Trail.Value), out second)
                    || !isDated(first) || !isDated(second))
                {
                    continue;
                }
                // inNameOrder: taken away first, the removal ends by the time the install starts; put up first, the install
                // starts before the removal ends.
                long firstEnd = first.StartMinute + first.Duration;
                long secondEnd = second.StartMinute + second.Duration;
                bool inOrder = p.Lead.Value == PlannerTaskType.Dismantle ? firstEnd <= second.StartMinute : first.StartMinute < secondEnd;
                if (!inOrder)
                {
                    result.Add(obj);
                    if (oneTbcDay(first) || oneTbcDay(second))
                    {
                        ifDated.Add(obj);
                    }
                }
            }
            return result;
        }

        // stillParkedOn: the folder records a parked day, and its own work all still sits there, TBC, a day at a time.
        private bool StillParked(PlannerScheduleFolder folder)
        {
            if (!folder.ParkedOn.HasValue || folder.Id == null)
            {
                return false;
            }
            var key = PlannerNaming.LayerIdKey(folder.Id);
            var own = Activities.Where(a => a.Home != null && a.Home.Folder == folder
                && PlannerNaming.LayerIdKey(PlannerNaming.ActivityEventLayer(a.Name, folder.Name) ?? folder.Id) == key).ToList();
            return own.Count > 0 && own.All(a =>
            {
                var first = LocalTime(AnchorMs + a.StartMinute * 60000L).Date;
                var last = LocalTime(AnchorMs + (a.StartMinute + a.Duration - 1) * 60000L).Date;
                return (a.Tbc || folder.TbcHere) && first == last && first == folder.ParkedOn.Value;
            });
        }

        // ---------------------------------------------------------------------------------------------------------
        // The scrubber's span

        // The first and last day: the project start, every layer's dates and parked day, and every activity's days.
        private void SpanV2(out DateTime first, out DateTime last)
        {
            first = ProjectStart;
            last = WorksEnd ?? ProjectStart;
            foreach (var layer in Layers.Where(l => l.Dated))
            {
                if (layer.Start < first)
                {
                    first = layer.Start;
                }
                if (layer.End > last)
                {
                    last = layer.End;
                }
            }
            foreach (var activity in Activities)
            {
                var start = LocalTime(activity.StartMs).Date;
                var end = LocalTime(Math.Max(activity.StartMs, activity.EndMs - 1)).Date;
                if (start < first)
                {
                    first = start;
                }
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

        // ---------------------------------------------------------------------------------------------------------
        // What the new names do not schedule

        /// <summary>Why a new-name object with an old Ph/St tag is not scheduled.</summary>
        public const string OldTagReasonV2 = "it holds an old Ph/St tag, which the new names do not read: Clean up scene graph\u2026 converts it";

        /// <summary>Why a new-name object with no tag is not scheduled.</summary>
        public const string NoTagReasonV2 = "it has no IN or RM tag (IN_ or RM_ at the start, _IN or _RM at the end), so it shows for the whole programme";

        /// <summary>Why a tagged new-name object under no layer node is not scheduled.</summary>
        public const string NoLayerReasonV2 = "it is not linked under a layer's helper (a node named <ID>_<Name>, the ID two or three numbers), so the Planner cannot place it";

        // Every node under the phasing root that is geometry, and every tagged node anywhere, that the Planner does not
        // schedule (as on the old names, read the new way); and the scheduled objects the Planner reads only in part: a tag it
        // does not read, or a reference to a layer nobody has. Helpers, layer nodes, number-led nodes and phase groups are
        // not objects, nor the pieces of an object.
        private void ListNotScheduledV2(PlannerLayerNodeIndex nodes, Dictionary<string, string> oldLayers)
        {
            var scene = Model.Scene;
            var notObjects = new HashSet<string>(nodes.Layers.Concat(nodes.Skipped), StringComparer.Ordinal);
            var skippedNodes = new HashSet<string>(nodes.Skipped, StringComparer.Ordinal);
            var issuesOf = new Dictionary<string, List<PlannerNamingCode>>(StringComparer.Ordinal);
            if (V2 != null)
            {
                foreach (var issue in V2.NamingIssues.Where(i => PlannerNaming.NamingIssueV2[i.Code].Severity == NamingSeverity.Error))
                {
                    foreach (var name in issue.Names)
                    {
                        List<PlannerNamingCode> list;
                        if (!issuesOf.TryGetValue(name, out list))
                        {
                            list = new List<PlannerNamingCode>();
                            issuesOf[name] = list;
                        }
                        if (!list.Contains(issue.Code))
                        {
                            list.Add(issue.Code);
                        }
                    }
                }
            }
            foreach (var node in scene.Nodes)
            {
                var name = node.Name ?? string.Empty;
                PlannerModelNode model;
                Model.ByKey.TryGetValue(node.Key, out model);
                var read = model != null ? model.Name : name;
                var parsed = read.Length > 0 ? PlannerNaming.ParseObjectNameV2(read) : null;
                bool tagged = parsed != null && parsed.Kind.HasValue;
                bool underRoot = scene.TreeNodes.Contains(node.Key) || (node.LayerName != null && scene.TreeLayers.Contains(node.LayerName));
                bool geometry = !node.IsHelper && !node.IsShape;
                if (scene.LeftOut(node) || scene.PhasingHelpers.Contains(node.Key) || !(tagged || (underRoot && geometry)))
                {
                    continue;
                }
                if (notObjects.Contains(read) || ParsePhaseGroup(read) != null || UnderObject(node.Key))
                {
                    continue;
                }
                string reason = null;
                bool inPart = false;
                PlannerModelNode indexed;
                string why;
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
                    reason = "another node reaches the Planner as " + read + " too, and the Planner shows and hides only the first node of a name: give it a unique name";
                }
                else if (MergedName(read) == read && Usable)
                {
                    PlannerScheduleObject obj;
                    if (!Objects.TryGetValue(read, out obj))
                    {
                        reason = NotScheduledReasonV2(read, node, nodes, oldLayers, skippedNodes);
                        if (reason != null && model.RenamedByExport)
                        {
                            reason += "; " + RenamedByExportText(model);
                        }
                    }
                    else
                    {
                        reason = InPartReasonV2(obj, issuesOf);
                        inPart = true;
                    }
                }
                if (reason != null)
                {
                    NotScheduled.Add(new PlannerNotScheduled { Key = node.Key, Name = name, Reason = reason, LayerName = node.LayerName, InPart = inPart });
                }
            }
        }

        // Why an object the new names do not schedule is not: an old tag, no tag (with what its name has instead), or no layer.
        private string NotScheduledReasonV2(string read, PlannerSceneNode node, PlannerLayerNodeIndex nodes, Dictionary<string, string> oldLayers, HashSet<string> skippedNodes)
        {
            string layerNode;
            nodes.Objects.TryGetValue(read, out layerNode);
            var parsedNode = layerNode != null ? PlannerNaming.ParseLayerNameV2(layerNode) : null;
            var p = PlannerNaming.ParseObjectNameV2(read, parsedNode != null ? parsedNode.Id : null);
            if (p.Kind == PlannerNamingKind.Old)
            {
                return OldTagReasonV2;
            }
            if (!p.Kind.HasValue)
            {
                var said = p.Issues.Where(c => c != PlannerNamingCode.Hyphen && c != PlannerNamingCode.Trimmed).Select(c => PlannerNaming.NamingIssueV2[c].Text).ToList();
                return NoTagReasonV2 + (said.Count > 0 ? " (" + string.Join(" ", said.Select(LowerFirst).ToArray()).TrimEnd('.') + ")" : string.Empty);
            }
            string old;
            if (oldLayers.TryGetValue(read, out old) && skippedNodes.Contains(old))
            {
                return "it sits under " + old + ", which is not a layer: " + LowerFirst(NotALayerText(old)).TrimEnd('.');
            }
            var reason = NoLayerReasonV2;
            var unit = node.LayerName != null ? Model.Scene.UnitOfLayer(node.LayerName) : null;
            if (unit != null && unit.InPhasingTree && PlannerNaming.ParseLayerNameV2(unit.Name) != null)
            {
                reason += " (it sits on the layer " + node.LayerName + ": Link objects to their helpers puts it under the layer's helper)";
            }
            return reason;
        }

        // What the Planner does not read of a scheduled object's name: a tag it cannot read (after a space, at both ends, a
        // number that is no layer ID), and a reference to a layer nobody has (parked TBC; 99_99 is work whose date is not known
        // yet). Null when it reads all of it.
        private string InPartReasonV2(PlannerScheduleObject obj, Dictionary<string, List<PlannerNamingCode>> issuesOf)
        {
            var parts = new List<string>();
            List<PlannerNamingCode> codes;
            if (issuesOf.TryGetValue(obj.Name, out codes))
            {
                parts.AddRange(codes.Where(c => c != PlannerNamingCode.DuplicateExport).Select(c => LowerFirst(PlannerNaming.NamingIssueV2[c].Text).TrimEnd('.')));
            }
            foreach (var a in obj.Activities.Where(x => x.Unresolved))
            {
                var what = a.Type == PlannerTaskType.Install ? "install" : "removal";
                var where = a.Type == PlannerTaskType.Install ? "on the project start" : "the day after " + WorksEndText;
                var id = PlannerNaming.LayerIdText(a.EventLayer);
                parts.Add(id == PlannerNaming.DateUnknownLayer
                    ? "its " + what + " is for layer " + id + ", work whose date is not known yet: the Planner parks it TBC " + where
                    : "its " + what + " names layer " + id + ", which neither the scene nor the Planner's list has: the Planner parks it TBC " + where + " (add the layer, or correct the tag)");
            }
            return parts.Count > 0 ? "read only in part: " + string.Join("; ", parts.ToArray()) : null;
        }

        // ---------------------------------------------------------------------------------------------------------
        // What the Schedule tab says on the new names

        /// <summary>
        /// The new names' summary under the tree (spec 5.6): what the preview scheduled (the objects it reads only in part
        /// counted apart from those it does not schedule), the layers it parked, and where its project start comes from.
        /// <paramref name="plannerStart"/>: the tab's Planner project start is given (else today).
        /// </summary>
        public string SummaryV2(bool plannerStart)
        {
            int installs = Activities.Where(a => a.Type == PlannerTaskType.Install).Sum(a => a.Members.Count);
            int removals = Activities.Where(a => a.Type == PlannerTaskType.Dismantle).Sum(a => a.Members.Count);
            int parked = V2.ParkedLayers.Count;
            int inPart = NotScheduled.Count(n => n.InPart);
            var dated = Layers.Count(l => l.DateSource == PlannerDateSource.Planner);
            var text = "IN / RM from names (new names): " + PhasingFixScript.Count(Objects.Count, "object", "objects") + " scheduled"
                + (inPart > 0 ? " (" + inPart.ToString(CultureInfo.InvariantCulture) + " read only in part)" : string.Empty) + " in "
                + PhasingFixScript.Count(Layers.Count(l => l.InScene), "layer", "layers") + " (" + installs.ToString(CultureInfo.InvariantCulture) + " IN, "
                + removals.ToString(CultureInfo.InvariantCulture) + " RM), " + PhasingFixScript.Count(NotScheduled.Count - inPart, "not scheduled", "not scheduled") + "; "
                + PhasingFixScript.Count(dated, "layer dated", "layers dated") + " by the Planner's list";
            if (parked > 0)
            {
                text += ", " + PhasingFixScript.Count(parked, "layer", "layers") + " with no dates parked one day each, TBC, in layer order from the project start"
                    + (V2.OneTbcDayRows.Count > 0 ? " (" + ParkedElsewhere + ")" : string.Empty);
            }
            text += "; project start " + ProjectStart.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                + (plannerStart ? " (the Planner's)." : " (today: tick Planner project start, by the scrubber, to give the Planner's).");
            return text + " A preview of the Planner's first Generate: once planners edit timings there, the Planner's schedule differs.";
        }

        /// <summary>
        /// Said of a layer the preview parks when the list has rows on one TBC day (<see cref="PlannerScheduleV2Report.OneTbcDayRows"/>):
        /// the Planner may still have some of them parked, and parks a layer it does not have yet after their days.
        /// </summary>
        public const string ParkedElsewhere = "the Planner parks a layer it does not have yet after the days of the layers it still has parked, "
            + "which the list does not mark among its rows on one TBC day, so the Planner's day may differ";

        /// <summary>
        /// The new names' lists (spec 3.7), one note each, for the notes above the tree: references to a layer nobody has
        /// (and to 99_99) and where they are parked, old names, illegal names, shared IDs (folders sharing one: the preview may
        /// take another of them than the Planner), list-only layers, renamed layers (or a list written before the new names),
        /// the wrong-order objects (apart, those in a layer the Planner may still have parked) and names ending " (n)".
        /// </summary>
        public List<string> NotesV2()
        {
            var report = V2;
            var lines = new List<string>();
            var missing = report.MissingLayers.Where(m => m.Layer != PlannerNaming.DateUnknownLayer).ToList();
            var unknown = report.MissingLayers.Where(m => m.Layer == PlannerNaming.DateUnknownLayer).ToList();
            var parkedWhere = "parked TBC (a removal the day after " + WorksEndText + ", an install on the project start)";
            // Rows the Planner may still have parked are dated in the preview, so the works may end elsewhere in the Planner.
            var mayDiffer = report.OneTbcDayRows.Count > 0 && WorksEndFrom == PlannerWorksEndFrom.DatedWork
                ? " The Planner's day may differ while it still has parked layers the list shows on one TBC day." : string.Empty;
            if (missing.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(missing.Count, "activity names", "activities name") + " a layer that neither the scene nor the Planner's list has, "
                    + parkedWhere + ": " + string.Join(", ", missing.Select(m => m.ActivityName).ToArray()) + "." + mayDiffer);
            }
            if (unknown.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(unknown.Count, "activity is", "activities are") + " for layer " + PlannerNaming.DateUnknownLayer
                    + ", work whose date is not known yet, " + parkedWhere + ": " + string.Join(", ", unknown.Select(m => m.ActivityName).ToArray()) + "." + mayDiffer);
            }
            if (report.OtherNaming.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(report.OtherNaming.Count, "object keeps", "objects keep") + " old Ph/St names, which the Planner will not schedule on the new names: "
                    + string.Join(", ", report.OtherNaming.ToArray()) + ".");
            }
            foreach (var issue in report.NamingIssues.Where(i => PlannerNaming.NamingIssueV2[i.Code].Severity == NamingSeverity.Error && i.Code != PlannerNamingCode.UnknownLayer))
            {
                lines.Add("Object name: " + string.Join(", ", issue.Names.ToArray()) + ": " + PlannerNaming.NamingIssueV2[issue.Code].Text);
            }
            foreach (var shared in report.SharedIds)
            {
                // Of folders sharing an ID, Generate takes the first in the Planner's own folder order; the preview takes the row
                // the list keeps (the first in the file without a problem), so the Planner's may be another.
                lines.Add(shared.Folders
                    ? "The list's rows share the layer ID " + shared.Id + ": " + string.Join(", ", shared.Names.ToArray()) + ". The preview dates the layer's work by "
                        + "the row the list keeps (the first of them in the file), the Planner by the first in its own folder order, which the list does not give, "
                        + "so the Planner may date it by another. Give each layer its own ID in the Planner."
                    : "Layer nodes share the layer ID " + shared.Id + ": " + string.Join(", ", shared.Names.ToArray()) + ".");
            }
            if (report.FoldersWithoutLayer.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(report.FoldersWithoutLayer.Count, "layer of the list has", "layers of the list have") + " no layer in the scene yet (planned in the Planner): "
                    + string.Join(", ", report.FoldersWithoutLayer.ToArray()) + ".");
            }
            // Renamed in the Planner; or named as the list does not write a layer on the new names (dates, TBC or spaces in it):
            // a list made before them. A layer whose ID folders share is said above.
            var sharedByFolders = new HashSet<string>(report.SharedIds.Where(s => s.Folders).Select(s => s.Id), StringComparer.Ordinal);
            var datedNames = new List<string>();
            var spacedNames = new List<string>();
            foreach (var renamed in report.RenamedLayers)
            {
                var scene = PlannerNaming.ParseLayerNameV2(renamed.Layer);
                var id = scene != null ? PlannerNaming.LayerIdText(scene.Id) : null;
                if (id != null && sharedByFolders.Contains(id))
                {
                    continue;
                }
                if (ListWritesAnotherWay(renamed.Folder))
                {
                    (PlannerNaming.LayerNameCarriesDates(renamed.Folder) ? datedNames : spacedNames).Add(renamed.Folder);
                    continue;
                }
                lines.Add("Layer " + id + " is " + renamed.Folder + " in the Planner and " + renamed.Layer + " in the scene: the Planner keeps its own name, which Clean up scene graph carries to 3ds Max.");
            }
            if (datedNames.Count > 0)
            {
                lines.Add("The list's layer names carry dates or TBC, as lists did before the Planner's new names (" + PhasingFixScript.Count(datedNames.Count, "layer", "layers") + ": "
                    + PhasingFixScript.FirstNames(datedNames, PlannerNamingValidator.NamesShown) + "): Clean up scene graph names the layers without them, "
                    + "as layers carry no dates; export the list from the Planner again to have its names as they are now.");
            }
            if (spacedNames.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(spacedNames.Count, "layer name in the list has", "layer names in the list have") + " spaces, which the Planner's new names write as underscores: "
                    + PhasingFixScript.FirstNames(spacedNames, PlannerNamingValidator.NamesShown) + ". Export the list from the Planner again before Clean up scene graph.");
            }
            var wrong = report.WrongOrder.Where(o => !report.WrongOrderIfDated.Contains(o)).ToList();
            if (wrong.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(wrong.Count, "object's two events run", "objects' two events run") + " the other way round from the name: "
                    + string.Join(", ", wrong.ToArray()) + ".");
            }
            if (report.WrongOrderIfDated.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(report.WrongOrderIfDated.Count, "object's two events run", "objects' two events run") + " the other way round from the name, "
                    + "with an event in a layer the list shows on one TBC day, which may still be parked in the Planner: it checks the order once that layer is dated: "
                    + string.Join(", ", report.WrongOrderIfDated.ToArray()) + ".");
            }
            int numbered = report.NumberedBindings.Count;
            if (numbered > 0)
            {
                lines.Add(PhasingFixScript.Count(numbered, "scheduled object reaches", "scheduled objects reach") + " the Planner with \" (n)\" after "
                    + (numbered == 1 ? "its name, as another object has the same name: " : "their names, as other objects have the same names: ")
                    + string.Join(", ", report.NumberedBindings.ToArray()) + ". Give each object its own name in 3ds Max.");
            }
            return lines;
        }

        // layerListNameV2's underscored: whitespace runs, with any underscores beside them, as one underscore.
        private static readonly Regex SpaceRun = SpaceRunRegex();

        private static Regex SpaceRunRegex()
        {
            var spaceOrUnderscore = PlannerNaming.JavaScriptSpace.Insert(PlannerNaming.JavaScriptSpace.Length - 1, "_");
            return new Regex(spaceOrUnderscore + "*" + PlannerNaming.JavaScriptSpace + spaceOrUnderscore + "*", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// The Planner's new-names layer list writes this layer folder's name another way after its ID (maxLayersCsv.ts
        /// layerListNameV2's rewritten): without the dates and TBC the Planner reads in it, whitespace runs as underscores. A
        /// list whose Layer cell is so is one made before the new names.
        /// </summary>
        public static bool ListWritesAnotherWay(string folderName)
        {
            var id = PlannerNaming.FolderLayerId(folderName);
            var read = id != null ? PlannerNaming.ParseLayerName(folderName) : null;
            if (read == null)
            {
                return false;
            }
            var written = SpaceRun.Replace(read.Name.Trim(PlannerNaming.JavaScriptSpaceChars), "_");
            var asRead = PlannerNaming.ParseLayerNameV2(folderName);
            return SpellingKey(asRead != null ? asRead.Name : read.Name) != SpellingKey(written);
        }

        private static string LowerFirst(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);
        }
    }
}
