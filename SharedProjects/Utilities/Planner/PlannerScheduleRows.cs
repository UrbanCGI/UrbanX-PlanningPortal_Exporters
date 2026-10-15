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
        /// <summary>A layer of the scene's phasing tree (no list imported).</summary>
        Scene,
        /// <summary>A phasing layer of the scene the Planner's list does not have.</summary>
        Unlisted,
        /// <summary>A dated group the Planner reads that no row stands for (outside the phasing tree).</summary>
        Outside,
        /// <summary>Work no dated layer places: installs at the project start, removals after the end.</summary>
        ByRule
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
        public PlannerUnlistedItem UnlistedItem;
        /// <summary>The layer of the schedule it stands for (null until filled, or when the Planner reads none there).</summary>
        public PlannerScheduleLayer Layer;
        public readonly List<PlannerScheduleActivity> In = new List<PlannerScheduleActivity>();
        public readonly List<PlannerScheduleActivity> Rm = new List<PlannerScheduleActivity>();

        public int InCount
        {
            get { return In.Sum(a => a.Members.Count); }
        }

        public int RmCount
        {
            get { return Rm.Sum(a => a.Members.Count); }
        }

        /// <summary>
        /// A row whose IN and RM can be edited: a layer the Planner reads there (by its helper), or a layer of the scene
        /// whose name reads as a layer ID. Edits target the layer as the Planner reads it (<see cref="PlannerTagTarget.For"/>).
        /// </summary>
        public bool Editable
        {
            get
            {
                return Source != PlannerScheduleRowSource.ByRule
                    && (Layer != null || (LayerName != null && PlannerNaming.ParseLayerName(LayerName) != null));
            }
        }
    }

    /// <summary>
    /// The Schedule tab's rows: the Planner's list as a tree (or, with no list, the scene's phasing tree), the layers
    /// added in Max under their parents, the scene's phasing layers the list does not have, and, once filled, each row's
    /// IN and RM (the objects whose install or removal its dates place, as Generate from model reads them), with rows for
    /// dated groups no row stands for and for the work no dated layer places. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerScheduleRows
    {
        public const string AddedStatus = "Added in Max \u2014 not yet in the Planner";

        /// <summary>
        /// The tree: <paramref name="list"/> with <paramref name="plan"/> (its statuses in the scene) when a list is
        /// imported, else the scene's phasing tree; the added layers under their parents. Null schedule: no IN / RM yet.
        /// </summary>
        public static List<PlannerScheduleRow> Tree(PlannerLayerList list, PlannerLayerPlan plan, PlannerSceneModel scene, IList<PlannerAddedLayer> added, PlannerSchedule schedule)
        {
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
                    rows.Add(new PlannerScheduleRow
                    {
                        Source = PlannerScheduleRowSource.List,
                        Depth = row.Depth,
                        Name = row.Layer.Length > 0 ? row.Layer : "(line " + row.Line.ToString(CultureInfo.InvariantCulture) + ")",
                        LayerName = unit != null ? unit.Name : null,
                        Kind = row.KindDisplay,
                        Start = row.Start,
                        Finish = row.Finish,
                        Tbc = row.Tbc,
                        Status = rowPlan != null ? rowPlan.StatusText : row.HasProblem ? "Problem: " + string.Join("; ", row.Problems.ToArray()) : string.Empty,
                        State = rowPlan != null ? rowPlan.State : row.HasProblem ? PlannerRowState.Problem : (PlannerRowState?)null,
                        ListRow = row,
                        RowPlan = rowPlan,
                        Unit = unit
                    });
                }
            }
            else if (scene != null)
            {
                foreach (var pair in SceneTree(scene))
                {
                    if (addedNames.Contains(pair.Key.Name))
                    {
                        continue;
                    }
                    rows.Add(SceneRow(pair.Key, pair.Value, PlannerScheduleRowSource.Scene, "In the scene"));
                }
            }

            // The added layers, each after its parent's rows (its parent's subtree), else at the end.
            foreach (var layer in addedList)
            {
                var unit = scene != null ? scene.UnitOfLayer(layer.Layer) : null;
                var row = new PlannerScheduleRow
                {
                    Source = PlannerScheduleRowSource.Added,
                    Name = layer.Layer,
                    LayerName = unit != null ? unit.Name : null,
                    Kind = layer.Kind,
                    Start = layer.Start,
                    Finish = layer.Finish,
                    Tbc = layer.Tbc,
                    Status = unit != null ? AddedStatus : "Added in Max, and no longer in the scene",
                    Added = layer,
                    Unit = unit
                };
                int parent = string.IsNullOrEmpty(layer.ParentLayer) ? -1
                    : rows.FindIndex(r => r.LayerName != null && string.Equals(r.LayerName, layer.ParentLayer, StringComparison.OrdinalIgnoreCase));
                if (parent < 0)
                {
                    rows.Add(row);
                    continue;
                }
                row.Depth = rows[parent].Depth + 1;
                int at = parent + 1;
                while (at < rows.Count && rows[at].Depth > rows[parent].Depth)
                {
                    at++;
                }
                rows.Insert(at, row);
            }
            Fill(rows, schedule);
            return rows;
        }

        /// <summary>The scene's phasing layers the list does not have, the added ones left out (the tree shows them).</summary>
        public static List<PlannerScheduleRow> Unlisted(PlannerLayerPlan plan, IList<PlannerAddedLayer> added, PlannerSchedule schedule)
        {
            var addedNames = new HashSet<string>((added ?? new List<PlannerAddedLayer>()).Select(a => a.Layer), StringComparer.OrdinalIgnoreCase);
            var rows = new List<PlannerScheduleRow>();
            if (plan == null)
            {
                return rows;
            }
            foreach (var item in plan.NotInList.Where(n => !addedNames.Contains(n.Name)))
            {
                var row = SceneRow(item.Unit, 0, PlannerScheduleRowSource.Unlisted, item.Note);
                row.UnlistedItem = item;
                rows.Add(row);
            }
            Fill(rows, schedule);
            return rows;
        }

        /// <summary>
        /// The rows for what no row of <paramref name="shown"/> stands for: dated groups the Planner reads outside the
        /// phasing tree, installs no dated layer places (at the project start) and removals no dated layer places (after
        /// the end).
        /// </summary>
        public static List<PlannerScheduleRow> Extra(IEnumerable<PlannerScheduleRow> shown, PlannerSchedule schedule)
        {
            var rows = new List<PlannerScheduleRow>();
            if (schedule == null || !schedule.Usable)
            {
                return rows;
            }
            var mapped = new HashSet<PlannerScheduleLayer>(shown.Where(r => r.Layer != null).Select(r => r.Layer));
            foreach (var layer in schedule.Layers.Where(l => !mapped.Contains(l)))
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
                    Status = "A dated group the Planner reads, outside the phasing tree",
                    Layer = layer
                };
                row.In.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Install));
                row.Rm.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Dismantle));
                rows.Add(row);
            }
            var installs = schedule.ByRule(PlannerTaskType.Install);
            if (installs.Count > 0)
            {
                var row = new PlannerScheduleRow
                {
                    Source = PlannerScheduleRowSource.ByRule,
                    Name = "(no dated layer: at the project start)",
                    Start = PlannerNaming.ToIsoDate(schedule.ProjectStart),
                    Tbc = installs.Any(a => a.Tbc),
                    Status = "Installs whose phase and stage no dated layer has: the Planner puts them at the project start (TBC unless phase 0)"
                };
                row.In.AddRange(installs);
                rows.Add(row);
            }
            var removals = schedule.ByRule(PlannerTaskType.Dismantle);
            if (removals.Count > 0)
            {
                var row = new PlannerScheduleRow
                {
                    Source = PlannerScheduleRowSource.ByRule,
                    Name = "(no dated layer: after the end of the works)",
                    Start = schedule.WorksEnd.HasValue ? PlannerNaming.ToIsoDate(schedule.WorksEnd.Value.AddDays(1)) : string.Empty,
                    Tbc = true,
                    Status = "Removals whose phase and stage no dated layer has: the Planner parks them the day after the works end, TBC"
                };
                row.Rm.AddRange(removals);
                rows.Add(row);
            }
            return rows;
        }

        // Each row's layer of the schedule (by its helper, else by its label) and its IN and RM.
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
                if (layer == null && row.LayerName != null)
                {
                    var parsed = PlannerNaming.ParseLayerName(row.LayerName);
                    layer = parsed != null ? schedule.Layers.FirstOrDefault(l => l.Label == parsed.Label) : null;
                }
                row.Layer = layer;
                row.In.Clear();
                row.Rm.Clear();
                if (layer != null)
                {
                    row.In.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Install));
                    row.Rm.AddRange(schedule.ColumnOf(layer, PlannerTaskType.Dismantle));
                }
            }
        }

        private static PlannerScheduleRow SceneRow(PlannerSceneUnit unit, int depth, PlannerScheduleRowSource source, string status)
        {
            var parsed = PlannerNaming.ParseLayerName(unit.Name);
            return new PlannerScheduleRow
            {
                Source = source,
                Depth = depth,
                Name = unit.Name,
                LayerName = unit.Layer != null ? unit.Layer.Name : unit.Name,
                Kind = parsed != null ? "Layer" : "Group",
                Start = parsed != null && parsed.Start.HasValue ? PlannerNaming.ToIsoDate(parsed.Start) : string.Empty,
                Finish = parsed != null && parsed.End.HasValue ? PlannerNaming.ToIsoDate(parsed.End) : string.Empty,
                Tbc = parsed != null && parsed.Tbc,
                Status = status ?? string.Empty,
                Unit = unit
            };
        }

        // The units of the phasing tree, parents first, each with its depth: nested as their Max layers nest (a helper
        // without its layer as its helper is linked).
        private static List<KeyValuePair<PlannerSceneUnit, int>> SceneTree(PlannerSceneModel scene)
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
            var result = new List<KeyValuePair<PlannerSceneUnit, int>>();
            var seen = new HashSet<PlannerSceneUnit>();
            Action<PlannerSceneUnit, int> walk = null;
            walk = (unit, depth) =>
            {
                if (!seen.Add(unit) || depth > 64)
                {
                    return;
                }
                result.Add(new KeyValuePair<PlannerSceneUnit, int>(unit, depth));
                foreach (var child in units.Where(u => { PlannerSceneUnit p; return parentOf.TryGetValue(u, out p) && p == unit; })
                    .OrderBy(u => u.Name, PlannerNaturalOrder.Comparer))
                {
                    walk(child, depth + 1);
                }
            };
            foreach (var top in units.Where(u => !parentOf.ContainsKey(u)).OrderBy(u => u.Name, PlannerNaturalOrder.Comparer))
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
