using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>Where a row of the Schedule tab's tree comes from.</summary>
    public enum PlannerScheduleRowSource
    {
        /// <summary>A row of the Planner's layer list.</summary>
        List,
        /// <summary>A layer added in Max, not yet in the Planner's list.</summary>
        Added,
        /// <summary>A layer of the scene's phasing tree, or one of its phasing roots (no list imported).</summary>
        Scene,
        /// <summary>A phasing layer of the scene the Planner's list does not have.</summary>
        Unlisted,
        /// <summary>A layer the Planner reads that no row stands for (outside the phasing tree).</summary>
        Outside
    }

    /// <summary>Why the Schedule tab's tree highlights a row TBC (D21), or None.</summary>
    public enum PlannerRowTbc
    {
        /// <summary>Not highlighted.</summary>
        None,
        /// <summary>
        /// The Planner's list marks it TBC: a layer the Planner has not dated yet (on its placeholder day at the start of the
        /// schedule) or one dated but not confirmed (D13). Once filled, also a row whose layer the preview flags TBC.
        /// </summary>
        NotSettled,
        /// <summary>A layer of the scene the Planner's list does not have (one added in 3ds Max, or one the list leaves out): the Planner has no dates for it yet.</summary>
        NotInList,
        /// <summary>A layer of the scene while no layer list is imported: the Planner's dates for it are not known here.</summary>
        NoList
    }

    /// <summary>One row of the Schedule tab: a layer, its status, and the objects in its IN and RM columns.</summary>
    public sealed class PlannerScheduleRow
    {
        public PlannerScheduleRowSource Source;
        public int Depth;
        /// <summary>What the row shows as its name.</summary>
        public string Name;
        /// <summary>The Max layer (and helper) it stands for now, or null when the scene has none.</summary>
        public string LayerName;
        public string Kind = string.Empty;
        public string Start = string.Empty;
        public string Finish = string.Empty;
        public bool Tbc;
        public string Status = string.Empty;
        /// <summary>The list row's state, for the colours; null for rows that are not the list's.</summary>
        public PlannerRowState? State;
        public PlannerLayerRow ListRow;
        public PlannerRowPlan RowPlan;
        public PlannerAddedLayer Added;
        public PlannerSceneUnit Unit;
        /// <summary>The phasing root a Root row (or a list's top-level row named like a root) stands for in the scene, or null.</summary>
        public PlannerPhasingRoot Root;
        public PlannerUnlistedItem UnlistedItem;
        /// <summary>The layer of the schedule it stands for (null until filled, or when the Planner reads none there).</summary>
        public PlannerScheduleLayer Layer;
        public readonly List<PlannerScheduleActivity> In = new List<PlannerScheduleActivity>();
        public readonly List<PlannerScheduleActivity> Rm = new List<PlannerScheduleActivity>();
        /// <summary>
        /// On the new names, the row's own dates and where they come from: the list's Start and Finish as its one date reader
        /// reads them (the Planner), else none. No date comes from 3ds Max (D20).
        /// </summary>
        public DateTime? StartDate;
        public DateTime? FinishDate;
        public PlannerDateSource DateSource;

        /// <summary>
        /// Why the tree highlights the row TBC (D21): a row the Planner's list marks TBC (or, once filled, whose layer the
        /// preview flags TBC) has dates the Planner has not settled yet; a layer of the scene the list does not have (a layer
        /// added in Max, one the list leaves out, one read outside the phasing tree) has none from the Planner yet, and so has
        /// every layer of the scene while no list is imported. Groups and roots the list does not mark are not highlighted.
        /// </summary>
        public PlannerRowTbc TbcMark
        {
            get
            {
                if (Source == PlannerScheduleRowSource.List)
                {
                    return Tbc ? PlannerRowTbc.NotSettled : PlannerRowTbc.None;
                }
                if (IsGroup)
                {
                    return PlannerRowTbc.None;
                }
                return Source == PlannerScheduleRowSource.Scene ? PlannerRowTbc.NoList : PlannerRowTbc.NotInList;
            }
        }

        /// <summary>
        /// A group or a phasing root (Kind Group or Root): it holds other rows and has no dates of its own (R17), so the tab
        /// treats both alike: never TBC unless the list says so (D21), bold, and a span over what it holds in the Gantt.
        /// </summary>
        public bool IsGroup
        {
            get { return string.Equals(Kind, "Group", StringComparison.OrdinalIgnoreCase) || string.Equals(Kind, "Root", StringComparison.OrdinalIgnoreCase); }
        }

        public int InCount
        {
            get { return In.Sum(a => a.Members.Count); }
        }

        public int RmCount
        {
            get { return Rm.Sum(a => a.Members.Count); }
        }

        /// <summary>
        /// A row whose IN and RM can be edited, on the new names: a layer of the preview (by its helper, its list row, or its
        /// layer ID; one only the Planner's list has included), or a layer of the scene whose name reads as a layer ID. Edits
        /// target the layer as the preview reads it (<see cref="PlannerTagTarget.For"/>).
        /// </summary>
        public bool Editable
        {
            get
            {
                return Layer != null ? Layer.Id != null : LayerName != null && PlannerNaming.ParseLayerNameV2(LayerName) != null;
            }
        }
    }

    /// <summary>
    /// The Schedule tab's rows: the Planner's list as a tree (or, with no list, the scene's phasing tree, every root's), the
    /// layers added in Max under their parents, the scene's phasing layers the list does not have, and, once filled, each
    /// row's IN and RM (the objects whose install or removal its layer's folder holds, as Generate from model files them),
    /// with rows for the layers the Planner reads that no row stands for. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerScheduleRows
    {
        public const string AddedStatus = "Added in Max \u2014 not yet in the Planner";

        /// <summary>
        /// The tree: <paramref name="list"/> with <paramref name="plan"/> (its statuses in the scene) when a list is
        /// imported, its Root rows included, else the scene's phasing tree (in a scene of several phasing roots, a Root row
        /// for each, with its units under it; with one root, its units at the top); the added layers under their parents.
        /// Null schedule: no IN / RM yet. On the new names (<paramref name="naming"/>), rows carry their list dates and no
        /// dates from names; an added layer has none.
        /// </summary>
        public static List<PlannerScheduleRow> Tree(PlannerLayerList list, PlannerLayerPlan plan, PlannerSceneModel scene, IList<PlannerAddedLayer> added, PlannerSchedule schedule,
            PlannerNamingKind naming = PlannerNamingKind.Old)
        {
            bool newNames = naming == PlannerNamingKind.New;
            var rows = new List<PlannerScheduleRow>();
            var addedList = (added ?? new List<PlannerAddedLayer>()).ToList();
            var addedNames = new HashSet<string>(addedList.Select(a => a.Layer), StringComparer.OrdinalIgnoreCase);
            if (list != null && list.Usable)
            {
                var byRow = plan != null ? plan.Rows.ToDictionary(r => r.Row) : new Dictionary<PlannerLayerRow, PlannerRowPlan>();
                foreach (var row in list.Outline())
                {
                    PlannerRowPlan rowPlan;
                    byRow.TryGetValue(row, out rowPlan);
                    var unit = rowPlan != null ? rowPlan.Unit : null;
                    var root = row.Kind == PlannerLayerKind.Root || (rowPlan != null && rowPlan.IsRoot) ? RootOfRow(row, rowPlan, scene) : null;
                    rows.Add(new PlannerScheduleRow
                    {
                        Source = PlannerScheduleRowSource.List,
                        Depth = row.Depth,
                        Name = row.Layer.Length > 0 ? row.Layer : "(line " + row.Line.ToString(CultureInfo.InvariantCulture) + ")",
                        LayerName = unit != null ? unit.Name : root != null ? RootLayerName(root) : null,
                        Kind = row.KindDisplay,
                        Start = row.Start,
                        Finish = row.Finish,
                        Tbc = row.Tbc,
                        Status = rowPlan != null ? rowPlan.StatusText : row.HasProblem ? "Problem: " + string.Join("; ", row.Problems.ToArray()) : string.Empty,
                        State = rowPlan != null ? rowPlan.State : row.HasProblem ? PlannerRowState.Problem : (PlannerRowState?)null,
                        ListRow = row,
                        RowPlan = rowPlan,
                        Unit = unit,
                        Root = root,
                        StartDate = newNames ? row.StartDate : null,
                        FinishDate = newNames ? row.FinishDate : null,
                        DateSource = newNames && (row.StartDate.HasValue || row.FinishDate.HasValue) ? PlannerDateSource.Planner : PlannerDateSource.None
                    });
                }
            }
            else if (scene != null)
            {
                rows.AddRange(SceneTree(scene, addedNames, newNames));
            }

            // The added layers, each among its parent's rows in the order the Planner sorts names in (D24: the tool sorts the
            // rows itself, never in the order they were added); one straight under a phasing root (its record names the
            // root's Max layer, or, as records did before they named it, no parent) under the row that stands for its root,
            // or among the top rows when no row does (one root, a list without Root rows); and one whose parent the tree does
            // not have at the end.
            foreach (var layer in addedList)
            {
                var unit = scene != null ? scene.UnitOfLayer(layer.Layer) : null;
                // No dates: the Planner has none for it until its list has it (D20, D21).
                var row = new PlannerScheduleRow
                {
                    Source = PlannerScheduleRowSource.Added,
                    Name = layer.Layer,
                    LayerName = unit != null ? unit.Name : null,
                    Kind = layer.Kind,
                    Status = unit != null ? AddedStatus : "Added in Max, and no longer in the scene",
                    Added = layer,
                    Unit = unit,
                };
                int parent = string.IsNullOrEmpty(layer.ParentLayer) ? -1
                    : rows.FindIndex(r => r.LayerName != null && string.Equals(r.LayerName, layer.ParentLayer, StringComparison.OrdinalIgnoreCase));
                bool underRoot = parent < 0 && (string.IsNullOrEmpty(layer.ParentLayer)
                    || (scene != null && scene.PhasingRoots.Any(r => r.Layer != null && string.Equals(r.Layer.Name, layer.ParentLayer, StringComparison.OrdinalIgnoreCase))));
                if (underRoot && scene != null)
                {
                    // Its root: the one its own layer and helper are in, else the one whose Max layer the record names.
                    var root = unit != null && unit.Root != null ? unit.Root : scene.RootOfLayer(layer.ParentLayer);
                    parent = root != null ? rows.FindIndex(r => r.Root == root && r.IsGroup) : -1;
                }
                bool atTop = parent < 0 && underRoot;
                if (parent < 0 && !atTop)
                {
                    rows.Add(row);
                    continue;
                }
                row.Depth = parent < 0 ? 0 : rows[parent].Depth + 1;
                // Among the parent's rows (its subtree), before the first of its children that sorts after it.
                int at = parent + 1;
                while (at < rows.Count && (parent < 0 || rows[at].Depth > rows[parent].Depth))
                {
                    if (rows[at].Depth == row.Depth && PlannerNaturalOrder.Compare(rows[at].Name, row.Name) > 0)
                    {
                        break;
                    }
                    at++;
                }
                rows.Insert(at, row);
            }
            Fill(rows, schedule);
            return rows;
        }

        /// <summary>
        /// The scene's phasing layers the list does not have, the added ones left out (the tree shows them), in the order the
        /// Planner sorts names in (D24). A phasing root no Root row names is listed too (R12): <paramref name="scene"/> says
        /// which items are roots, each a row of Kind Root.
        /// </summary>
        public static List<PlannerScheduleRow> Unlisted(PlannerLayerPlan plan, IList<PlannerAddedLayer> added, PlannerSchedule schedule,
            PlannerNamingKind naming = PlannerNamingKind.Old, PlannerSceneModel scene = null)
        {
            var addedNames = new HashSet<string>((added ?? new List<PlannerAddedLayer>()).Select(a => a.Layer), StringComparer.OrdinalIgnoreCase);
            var rows = new List<PlannerScheduleRow>();
            if (plan == null)
            {
                return rows;
            }
            foreach (var item in PlannerNaturalOrder.StableSort(plan.NotInList.Where(n => !addedNames.Contains(n.Name)), (a, b) => PlannerNaturalOrder.Compare(a.Name, b.Name)))
            {
                // A phasing root as the scene has it; else a layer or helper (one with no unit by its name alone).
                var root = RootOfItem(item, scene);
                var row = root != null ? RootRow(root, PlannerScheduleRowSource.Unlisted, item.Note)
                    : item.Unit != null ? SceneRow(item.Unit, 0, PlannerScheduleRowSource.Unlisted, item.Note, naming == PlannerNamingKind.New)
                    : new PlannerScheduleRow
                    {
                        Source = PlannerScheduleRowSource.Unlisted,
                        Name = item.Name,
                        LayerName = item.Name,
                        Kind = KindOfName(item.Name, naming == PlannerNamingKind.New),
                        Status = item.Note ?? string.Empty
                    };
                row.UnlistedItem = item;
                rows.Add(row);
            }
            // A phasing root of the scene that no Root row of the list names (the list has Root rows): kept, never deleted.
            foreach (var root in PlannerNaturalOrder.StableSort(plan.UnlistedRoots, (a, b) => PlannerNaturalOrder.Compare(a.Name, b.Name)))
            {
                const string note = "the Planner's list names its phasing roots, and not this one: left as it is (a backup copy? delete it, or rename it without \"phasing\")";
                var row = RootRow(root, PlannerScheduleRowSource.Unlisted, note);
                row.UnlistedItem = new PlannerUnlistedItem { Name = root.Name, What = "phasing root", Note = note };
                rows.Add(row);
            }
            Fill(rows, schedule);
            return rows;
        }

        /// <summary>
        /// What the tab says above its list of the scene's phasing layers the Planner's list does not have: <paramref name="unlisted"/>
        /// of them listed there, and <paramref name="added"/> more added in Max, which the tree shows instead (the summary above
        /// counts both, so the two always agree).
        /// </summary>
        public static string UnlistedText(int unlisted, int added)
        {
            var apart = added > 0 ? ", apart from " + PhasingFixScript.Count(added, "layer", "layers") + " added in Max, which the tree shows" : string.Empty;
            return unlisted == 0
                ? "Every phasing layer in the scene is in the Planner's list" + apart + "."
                : "In the scene but not in the Planner's list \u2014 left as they are (" + unlisted.ToString(CultureInfo.InvariantCulture) + ")" + apart + ":";
        }

        public const string UnlistedAtWorkStatus = "Not in the Planner's list \u2014 left as it is; the Planner reads its work";

        /// <summary>
        /// The rows the tree adds after its own for the scene's phasing layers the list does not have whose work the Planner
        /// reads (filled: the schedule has a layer there), so that the Gantt beside the tree charts every layer the scrubber
        /// finds at work. Each is a copy of its row in <paramref name="unlisted"/> (which the list of such layers below the
        /// tree keeps), at the top level, in that order.
        /// </summary>
        public static List<PlannerScheduleRow> UnlistedAtWork(IEnumerable<PlannerScheduleRow> unlisted)
        {
            var rows = new List<PlannerScheduleRow>();
            foreach (var row in (unlisted ?? Enumerable.Empty<PlannerScheduleRow>()).Where(r => r.Layer != null))
            {
                var copy = new PlannerScheduleRow
                {
                    Source = PlannerScheduleRowSource.Unlisted,
                    Depth = 0,
                    Name = row.Name,
                    LayerName = row.LayerName,
                    Kind = row.Kind,
                    Start = row.Start,
                    Finish = row.Finish,
                    Tbc = row.Tbc,
                    Status = UnlistedAtWorkStatus,
                    Unit = row.Unit,
                    Root = row.Root,
                    UnlistedItem = row.UnlistedItem,
                    Layer = row.Layer,
                    StartDate = row.StartDate,
                    FinishDate = row.FinishDate,
                    DateSource = row.DateSource
                };
                copy.In.AddRange(row.In);
                copy.Rm.AddRange(row.Rm);
                rows.Add(copy);
            }
            return rows;
        }

        /// <summary>
        /// The rows for what no row of <paramref name="shown"/> stands for: the layers of the scene the Planner reads outside
        /// the phasing tree (outside every phasing root). On the new names every piece of work is filed in a layer's folder
        /// (the old names' installs and removals no dated layer placed went with their engine), so no other row is needed.
        /// </summary>
        public static List<PlannerScheduleRow> Extra(IEnumerable<PlannerScheduleRow> shown, PlannerSchedule schedule)
        {
            var rows = new List<PlannerScheduleRow>();
            if (schedule == null || !schedule.Usable)
            {
                return rows;
            }
            var mapped = new HashSet<PlannerScheduleLayer>(shown.Where(r => r.Layer != null).Select(r => r.Layer));
            // A folder of the list's with no work and no scene layer has its own list row already.
            foreach (var layer in schedule.Layers.Where(l => !mapped.Contains(l) && l.InScene))
            {
                var row = new PlannerScheduleRow
                {
                    Source = PlannerScheduleRowSource.Outside,
                    Name = layer.Raw,
                    LayerName = layer.Raw,
                    Kind = "Layer",
                    Start = layer.Dated ? PlannerNaming.ToIsoDate(layer.Start) : string.Empty,
                    Finish = layer.Dated ? PlannerNaming.ToIsoDate(layer.End) : string.Empty,
                    Tbc = layer.Tbc,
                    Status = "A layer the Planner reads, outside the phasing tree",
                    Layer = layer,
                };
                if (layer.DateSource != PlannerDateSource.None)
                {
                    row.StartDate = layer.Start;
                    row.FinishDate = layer.End;
                    row.DateSource = layer.DateSource;
                }
                row.In.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Install));
                row.Rm.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Dismantle));
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// The tree's tooltip for a row highlighted TBC (D21): why the Planner has no settled dates for it. Null for
        /// <see cref="PlannerRowTbc.None"/>.
        /// </summary>
        public static string TbcText(PlannerRowTbc mark)
        {
            switch (mark)
            {
                case PlannerRowTbc.NotSettled:
                    return "TBC: the Planner has not settled this layer's dates yet.";
                case PlannerRowTbc.NotInList:
                    return "TBC: this layer is not in the Planner's list yet, so the Planner has no dates for it.";
                case PlannerRowTbc.NoList:
                    return "TBC: no layer list is imported, so the Planner's dates for this layer are not known here.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// What the tab says of a scene on the new names before Fill IN / RM: where the preview's dates will come from, by the
        /// precedence of <see cref="PlannerFolders"/>. With no layer list imported, every layer is parked, so it says so rather
        /// than speak of the list's dates.
        /// </summary>
        public static string NewNamesFillNote(PlannerLayerList list)
        {
            const string scene = "The scene uses the new names (layer IDs, dates held in the Planner)";
            if (list != null && list.Usable)
            {
                return scene + ": Fill IN / RM previews the Planner's first Generate over the list's dates.";
            }
            return scene + ", and no layer list is imported: Fill IN / RM previews the Planner's first Generate with every layer"
                + " parked one day each, TBC, in layer order from the project start. Import the Planner's layer list to preview with its dates.";
        }

        /// <summary>
        /// The Planner's folders as the new names' preview reads them (spec 5.4, 5.6; <see cref="PlannerScheduleOptions.Folders"/>),
        /// with each layer's preview dates by precedence:
        ///  1. its list row (every row, in outline order, in its parent row's folder, TBC as the row says; of rows sharing a
        ///     layer ID, the preview takes the one the list check keeps, the first in the file), found as Clean up
        ///     finds it: a row Clean up finds on a helper of another layer ID by its Planner ID stands for that helper's layer,
        ///     so the layer keeps the row's dates; an undated row's layer is parked, TBC;
        ///  2. none: the preview parks the layer, one day each in layer order (D12). No date comes from 3ds Max (D20), so a
        ///     layer added in Max, or any layer while no list is imported, is parked.
        /// </summary>
        public static List<PlannerPlanFolder> PlannerFolders(PlannerLayerList list, PlannerLayerPlan plan)
        {
            var folders = new List<PlannerPlanFolder>();
            if (list != null && list.Usable)
            {
                var plans = new Dictionary<PlannerLayerRow, PlannerRowPlan>();
                foreach (var rowPlan in plan != null ? plan.Rows : new List<PlannerRowPlan>())
                {
                    plans[rowPlan.Row] = rowPlan;
                }
                var byRow = new Dictionary<PlannerLayerRow, PlannerPlanFolder>();
                foreach (var row in list.Outline())
                {
                    var folder = new PlannerPlanFolder { Name = row.Layer, Tbc = row.Tbc, Row = row, Source = PlannerDateSource.Planner };
                    // Groups and roots carry no dates of their own (R17).
                    if (row.Kind != PlannerLayerKind.Group && row.Kind != PlannerLayerKind.Root)
                    {
                        folder.Start = row.StartDate;
                        folder.Finish = row.FinishDate;
                    }
                    PlannerRowPlan rowPlan;
                    if (plans.TryGetValue(row, out rowPlan) && rowPlan.MatchedBy == PlannerMatchKind.PlannerId && rowPlan.Unit != null && rowPlan.Unit.Helper != null)
                    {
                        var helper = PlannerNaming.ParseLayerNameV2(rowPlan.Unit.Helper.Name);
                        var own = PlannerNaming.FolderLayerId(row.Layer);
                        if (helper != null && (own == null || PlannerNaming.LayerIdKey(own) != PlannerNaming.LayerIdKey(helper.Id)))
                        {
                            folder.Id = helper.Id;
                        }
                    }
                    byRow[row] = folder;
                    folders.Add(folder);
                }
                foreach (var row in list.Rows)
                {
                    PlannerPlanFolder parent;
                    if (row.Parent != null && byRow.ContainsKey(row) && byRow.TryGetValue(row.Parent, out parent))
                    {
                        byRow[row].Parent = parent;
                    }
                }
            }
            return folders;
        }

        // Each row's layer of the schedule (by its helper, else by its list row's folder, else by the layer ID of its Max layer,
        // so a row only the list has finds its folder) and its IN and RM: everything filed in the layer's folder (spec 5.6).
        // Every schedule is the new names' (the tab fills nothing on the old names, D23).
        private static void Fill(List<PlannerScheduleRow> rows, PlannerSchedule schedule)
        {
            if (schedule == null || !schedule.Usable)
            {
                return;
            }
            foreach (var row in rows)
            {
                PlannerScheduleLayer layer = null;
                if (row.Unit != null && row.Unit.Helper != null)
                {
                    layer = schedule.LayerOfNode(row.Unit.Helper.Key);
                }
                if (layer == null && row.ListRow != null)
                {
                    layer = schedule.Layers.FirstOrDefault(l => l.ListRow == row.ListRow);
                }
                var id = layer == null && row.LayerName != null ? PlannerNaming.ParseLayerNameV2(row.LayerName) : null;
                if (id != null)
                {
                    layer = schedule.Layers.FirstOrDefault(l => l.InScene && PlannerNaming.LayerIdKey(l.Id) == PlannerNaming.LayerIdKey(id.Id));
                }
                row.Layer = layer;
                row.In.Clear();
                row.Rm.Clear();
                if (layer != null)
                {
                    row.In.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Install));
                    row.Rm.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Dismantle));
                    // The TBC column says what the preview flags (spec 5.8): the list's TBC, and a layer it parks or a new
                    // layer's folder too.
                    if (layer.Tbc && !row.IsGroup)
                    {
                        row.Tbc = true;
                    }
                }
            }
        }

        private static PlannerScheduleRow SceneRow(PlannerSceneUnit unit, int depth, PlannerScheduleRowSource source, string status, bool newNames)
        {
            if (newNames)
            {
                // The new names: a layer by its ID alone, never dated by its name (dates and TBC there are not read).
                return new PlannerScheduleRow
                {
                    Source = source,
                    Depth = depth,
                    Name = unit.Name,
                    LayerName = unit.Layer != null ? unit.Layer.Name : unit.Name,
                    Kind = KindOfName(unit.Name, true),
                    Status = status ?? string.Empty,
                    Unit = unit,
                };
            }
            // The old names: a layer by its name as the old reader reads it, but no date or TBC read from it either (D20: every
            // date comes from the Planner's list; the tab waits for Clean up on such a scene, D23).
            return new PlannerScheduleRow
            {
                Source = source,
                Depth = depth,
                Name = unit.Name,
                LayerName = unit.Layer != null ? unit.Layer.Name : unit.Name,
                Kind = KindOfName(unit.Name, false),
                Status = status ?? string.Empty,
                Unit = unit
            };
        }

        // The Kind of a layer or group of the scene read from its name: Layer when it reads as a layer (by its ID on the new
        // names, as the old reader reads it on the old), else Group. A phasing root is never read so: the scene says which
        // nodes are roots (RootRow).
        private static string KindOfName(string name, bool newNames)
        {
            bool layer = newNames ? PlannerNaming.ParseLayerNameV2(name) != null : PlannerNaming.ParseLayerName(name) != null;
            return layer ? "Layer" : "Group";
        }

        // A phasing root's row: its name, and the Max layer of its name (else its helper's name: a root with no Max layer
        // of its name, which Add layer makes).
        private static PlannerScheduleRow RootRow(PlannerPhasingRoot root, PlannerScheduleRowSource source, string status)
        {
            return new PlannerScheduleRow
            {
                Source = source,
                Name = root.Name,
                LayerName = RootLayerName(root),
                Kind = "Root",
                Status = status ?? string.Empty,
                Root = root
            };
        }

        // The phasing root an item the list does not have stands for, by the scene: the root whose helper or Max layer its
        // unit is, else (an item with no unit) the root of its name or its Max layer's name, any capitals. Null for every
        // other item, and with no scene to compare.
        private static PlannerPhasingRoot RootOfItem(PlannerUnlistedItem item, PlannerSceneModel scene)
        {
            if (scene == null)
            {
                return null;
            }
            var unit = item.Unit;
            if (unit != null)
            {
                return scene.PhasingRoots.FirstOrDefault(r => (unit.Helper != null && r.Node != null && r.Node.Key == unit.Helper.Key)
                    || (unit.Layer != null && r.Layer != null && string.Equals(r.Layer.Name, unit.Layer.Name, StringComparison.OrdinalIgnoreCase)));
            }
            return scene.PhasingRoots.FirstOrDefault(r => string.Equals(r.Name, item.Name, StringComparison.OrdinalIgnoreCase)
                || (r.Layer != null && string.Equals(r.Layer.Name, item.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private static string RootLayerName(PlannerPhasingRoot root)
        {
            return root.Layer != null ? root.Layer.Name : root.Name;
        }

        // The phasing root a list's Root row (or a top-level row named like a root) stands for, as Clean up finds it: the one
        // whose helper the plan found for the row, else the one whose helper carries its Planner ID, else the one of its name
        // (any capitals); null when the scene has none yet (Clean up makes it) or the scene is not compared.
        private static PlannerPhasingRoot RootOfRow(PlannerLayerRow row, PlannerRowPlan rowPlan, PlannerSceneModel scene)
        {
            if (rowPlan == null || scene == null)
            {
                return null;
            }
            PlannerSceneNode node;
            if (rowPlan.Helper != null && rowPlan.Helper.Key.HasValue && scene.NodeByKey.TryGetValue(rowPlan.Helper.Key.Value, out node) && scene.IsPhasingRootNode(node))
            {
                return scene.RootOfNode(node);
            }
            return scene.PhasingRoots.FirstOrDefault(r => r.Node != null && !string.IsNullOrEmpty(row.PlannerId) && string.Equals(r.Node.PlannerId, row.PlannerId, StringComparison.Ordinal))
                ?? scene.PhasingRoots.FirstOrDefault(r => string.Equals(r.Name, row.Layer, StringComparison.OrdinalIgnoreCase));
        }

        // The units of the phasing tree, every root's, parents first, as rows with their depths: nested as their Max layers
        // nest (a helper without its layer as its helper is linked). In a scene of several roots each root's row comes
        // first, at the top in name order, with its units under it; with one root its units are the top rows. A layer
        // added in Max has its added row instead (Tree), the rows under it kept where they are.
        private static List<PlannerScheduleRow> SceneTree(PlannerSceneModel scene, HashSet<string> addedNames, bool newNames)
        {
            var units = scene.Units.Where(u => u.InPhasingTree).ToList();
            var parentOf = new Dictionary<PlannerSceneUnit, PlannerSceneUnit>();
            foreach (var unit in units)
            {
                PlannerSceneUnit parent = null;
                if (unit.Layer != null && unit.Layer.ParentName != null)
                {
                    parent = units.FirstOrDefault(u => u != unit && u.Layer != null && string.Equals(u.Layer.Name, unit.Layer.ParentName, StringComparison.OrdinalIgnoreCase));
                }
                else if (unit.Layer == null && unit.Helper != null)
                {
                    var above = scene.ParentOf(unit.Helper);
                    parent = above != null ? units.FirstOrDefault(u => u != unit && u.Helper != null && u.Helper.Key == above.Key) : null;
                }
                if (parent != null)
                {
                    parentOf[unit] = parent;
                }
            }
            var result = new List<PlannerScheduleRow>();
            var seen = new HashSet<PlannerSceneUnit>();
            Action<PlannerSceneUnit, int> walk = null;
            walk = (unit, depth) =>
            {
                if (!seen.Add(unit) || depth > 64)
                {
                    return;
                }
                if (!addedNames.Contains(unit.Name))
                {
                    result.Add(SceneRow(unit, depth, PlannerScheduleRowSource.Scene, "In the scene", newNames));
                }
                foreach (var child in units.Where(u => { PlannerSceneUnit p; return parentOf.TryGetValue(u, out p) && p == unit; })
                    .OrderBy(u => u.Name, PlannerNaturalOrder.Comparer))
                {
                    walk(child, depth + 1);
                }
            };
            var tops = units.Where(u => !parentOf.ContainsKey(u)).OrderBy(u => u.Name, PlannerNaturalOrder.Comparer).ToList();
            if (scene.PhasingRoots.Count > 1)
            {
                foreach (var root in scene.PhasingRoots)
                {
                    result.Add(RootRow(root, PlannerScheduleRowSource.Scene, "In the scene"));
                    foreach (var top in tops.Where(u => u.Root == root))
                    {
                        walk(top, 1);
                    }
                }
            }
            foreach (var top in tops)
            {
                walk(top, 0);
            }
            foreach (var rest in units.Where(u => !seen.Contains(u)))
            {
                walk(rest, 0);
            }
            return result;
        }
    }
}
