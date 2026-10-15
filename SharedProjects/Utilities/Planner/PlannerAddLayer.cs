using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>What Add layer asks for.</summary>
    public sealed class PlannerNewLayer
    {
        /// <summary>A group (holds layers) or a layer (holds work).</summary>
        public PlannerLayerKind Kind = PlannerLayerKind.Layer;
        /// <summary>A layer's phase (null for an ID of one number, the stage); a group's phase number (_&lt;n&gt;_), which it needs.</summary>
        public int? Phase;
        public int Stage;
        public int? SubStage;
        /// <summary>The descriptive name; spaces become underscores.</summary>
        public string Name;
        public DateTime? Start;
        public DateTime? Finish;
        public bool Tbc;
    }

    /// <summary>A layer added in 3ds Max and not yet in the Planner's list, kept with the scene.</summary>
    public sealed class PlannerAddedLayer
    {
        /// <summary>Its name: the Max layer's and its helper's.</summary>
        public string Layer;
        /// <summary>The Max layer it was added under; empty for the phasing root.</summary>
        public string ParentLayer = string.Empty;
        /// <summary>"Group" or "Layer".</summary>
        public string Kind;
        /// <summary>Its layer ID as the list writes it ("02_01_2"), or empty.</summary>
        public string Id = string.Empty;
        public string Name = string.Empty;
        /// <summary>ISO yyyy-mm-dd, or empty.</summary>
        public string Start = string.Empty;
        public string Finish = string.Empty;
        public bool Tbc;
    }

    /// <summary>
    /// The layers added in Max as they are kept with the scene: a short header and a CSV (byte order mark left out) with
    /// the columns Layer, Parent layer, Kind, ID, Name, Start, Finish, TBC.
    /// </summary>
    public static class PlannerAddedLayerStore
    {
        private const string Magic = "PlannerAddedLayers 1";
        public const string Header = "Layer,Parent layer,Kind,ID,Name,Start,Finish,TBC";

        public static string Write(IEnumerable<PlannerAddedLayer> layers)
        {
            var text = new StringBuilder();
            text.Append(Magic).Append('\n').Append(Header).Append("\r\n");
            foreach (var layer in layers ?? Enumerable.Empty<PlannerAddedLayer>())
            {
                var cells = new[] { layer.Layer, layer.ParentLayer, layer.Kind, layer.Id, layer.Name, layer.Start, layer.Finish, layer.Tbc ? "Yes" : string.Empty };
                text.Append(string.Join(",", cells.Select(PlannerReview.Field).ToArray())).Append("\r\n");
            }
            return text.ToString();
        }

        /// <summary>Reads what <see cref="Write"/> wrote; an empty list when the text is not in that form.</summary>
        public static List<PlannerAddedLayer> Read(string stored)
        {
            var result = new List<PlannerAddedLayer>();
            if (stored == null || !stored.StartsWith(Magic + "\n", StringComparison.Ordinal))
            {
                return result;
            }
            var records = PlannerLayerList.ReadRecords(stored.Substring(Magic.Length + 1), ',');
            foreach (var record in records.Skip(1))
            {
                var f = record.Fields;
                Func<int, string> cell = i => i < f.Count ? f[i] : string.Empty;
                if (cell(0).Trim().Length == 0)
                {
                    continue;
                }
                result.Add(new PlannerAddedLayer
                {
                    Layer = cell(0),
                    ParentLayer = cell(1),
                    Kind = cell(2),
                    Id = cell(3),
                    Name = cell(4),
                    Start = cell(5),
                    Finish = cell(6),
                    Tbc = string.Equals(cell(7), "Yes", StringComparison.OrdinalIgnoreCase)
                });
            }
            return result;
        }

        /// <summary>
        /// The added layers a re-imported list does not have yet: one the Planner's list now has (its row found the layer
        /// in the scene by its layer ID or its name, or names it) is the Planner's from then on.
        /// </summary>
        public static List<PlannerAddedLayer> StillAdded(IEnumerable<PlannerAddedLayer> added, PlannerLayerPlan plan)
        {
            var adopted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan != null)
            {
                foreach (var row in plan.Rows)
                {
                    adopted.Add(row.Row.Layer);
                    if (row.Unit != null)
                    {
                        if (row.Unit.Layer != null)
                        {
                            adopted.Add(row.Unit.Layer.Name);
                        }
                        if (row.Unit.Helper != null && row.Unit.Helper.Name != null)
                        {
                            adopted.Add(row.Unit.Helper.Name);
                        }
                    }
                }
            }
            return (added ?? Enumerable.Empty<PlannerAddedLayer>()).Where(a => !adopted.Contains(a.Layer)).ToList();
        }

        /// <summary>
        /// The added layers whose Max layer the scene still has (any capitals): the scene is the truth, so a layer taken back
        /// with Edit > Undo, deleted or renamed by hand since is no longer an added layer, and its ID and name are free again.
        /// </summary>
        public static List<PlannerAddedLayer> Present(IEnumerable<PlannerAddedLayer> added, PlannerSceneModel scene)
        {
            var list = (added ?? Enumerable.Empty<PlannerAddedLayer>()).Where(a => a != null && !string.IsNullOrEmpty(a.Layer)).ToList();
            return scene == null ? list : list.Where(a => scene.LayerByName.ContainsKey(a.Layer)).ToList();
        }

        /// <summary>
        /// The added layers still to show as added: still in the scene (<see cref="Present"/>) and not yet in the Planner's
        /// list (<see cref="StillAdded"/>, when a list is imported).
        /// </summary>
        public static List<PlannerAddedLayer> Prune(IEnumerable<PlannerAddedLayer> added, PlannerSceneModel scene, PlannerLayerPlan plan)
        {
            var present = Present(added, scene);
            return plan != null ? StillAdded(present, plan) : present;
        }
    }

    /// <summary>What Add layer would do: the name, and the build that makes the layer and its helper, or why it cannot.</summary>
    public sealed class PlannerAddLayerPlan
    {
        public string LayerName;
        /// <summary>Why the layer cannot be added, or null.</summary>
        public string Problem;
        /// <summary>The parent's name as the review shows it.</summary>
        public string ParentName;
        /// <summary>The build for <see cref="PlannerLayerScript"/>: the root and the parent as they are, the new layer and helper.</summary>
        public PlannerLayerPlan Build;
        public readonly List<PlannerReviewRow> Review = new List<PlannerReviewRow>();
        /// <summary>The record kept with the scene once it is made.</summary>
        public PlannerAddedLayer Added;

        public bool CanApply
        {
            get { return Problem == null && Build != null; }
        }
    }

    /// <summary>
    /// Add layer in the Schedule tab: a phase group or a layer made in 3ds Max under the selected row (the phasing root or
    /// a phase group, as the Planner files layers only there), its name composed in the convention
    /// (&lt;ID&gt;_&lt;Name&gt;[_TBC][_dd-mm-yy[_dd-mm-yy]], a group _&lt;nn&gt;_&lt;Name&gt;), checked to read back with the ID and
    /// dates intended and not to clash with a layer, node, ID, phase number or list row already there, then built with
    /// Build layers and helpers' own batch: the Max layer under its parent's layer and a Dummy of the same name on it,
    /// linked under the parent's helper. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerAddLayer
    {
        private static readonly Regex Spaces = new Regex(PlannerNaming.JavaScriptSpace + "+", RegexOptions.CultureInvariant);

        /// <summary>The name the convention gives the new layer or group, or null with why it cannot be written.</summary>
        public static string Compose(PlannerNewLayer spec, out string problem)
        {
            problem = null;
            var name = Spaces.Replace((spec.Name ?? string.Empty).Trim(PlannerNaming.JavaScriptSpaceChars), "_");
            if (name.Length == 0)
            {
                problem = "give it a name";
                return null;
            }
            if (name.Any(c => c < ' ' || c == '\u007f'))
            {
                problem = "its name holds a character 3ds Max cannot take";
                return null;
            }
            if (spec.Kind == PlannerLayerKind.Group)
            {
                if (!spec.Phase.HasValue)
                {
                    // Generate from model makes folders only for phase groups and dated layers: a plain group would stay in
                    // Max alone, and the layers in it would reach the Planner outside it.
                    problem = "a group needs its phase: the Planner makes a folder only for a phase group (_<nn>_<name>) and for dated layers, so a group "
                        + "without a phase would stay in 3ds Max only, and the layers in it would reach the Planner outside it";
                    return null;
                }
                if (spec.Phase.Value < 0 || spec.Phase.Value > 99)
                {
                    problem = "a phase number has one or two digits";
                    return null;
                }
                var group = "_" + spec.Phase.Value.ToString("00", CultureInfo.InvariantCulture) + "_" + name;
                int phase;
                if (PlannerNaming.ParseLayerName(group) != null)
                {
                    problem = "the name " + group + " would read as a layer (it starts with a number); a group's name must not";
                    return null;
                }
                if (!PhasingNameFixer.TryReadPhaseGroup(group, out phase) || phase != spec.Phase.Value || PlannerSchedule.ParsePhaseGroup(group) == null)
                {
                    problem = "the name " + group + " does not read back as the group of phase " + spec.Phase.Value.ToString(CultureInfo.InvariantCulture);
                    return null;
                }
                return group;
            }

            if (spec.Stage < 0 || spec.Stage > 99 || (spec.Phase.HasValue && (spec.Phase.Value < 0 || spec.Phase.Value > 99))
                || (spec.SubStage.HasValue && (spec.SubStage.Value < 1 || spec.SubStage.Value > 99)))
            {
                problem = "each number of a layer ID has one or two digits (a sub-stage from 1)";
                return null;
            }
            if (spec.SubStage.HasValue && !spec.Phase.HasValue)
            {
                problem = "an ID of one number cannot carry a sub-stage: give the layer its phase";
                return null;
            }
            if (!spec.Start.HasValue && !spec.Tbc)
            {
                problem = "give it a start (and a finish), or mark it TBC";
                return null;
            }
            if (spec.Finish.HasValue && !spec.Start.HasValue)
            {
                problem = "give it a start as well as a finish";
                return null;
            }
            var start = spec.Start.HasValue ? spec.Start.Value.Date : (DateTime?)null;
            var finish = spec.Finish.HasValue ? spec.Finish.Value.Date : start;
            if (start.HasValue && (start.Value.Year < 2000 || start.Value.Year > 2099 || finish.Value.Year > 2099))
            {
                problem = "the convention writes years with two digits, 2000 to 2099";
                return null;
            }
            if (start.HasValue && finish.Value < start.Value)
            {
                problem = "its finish is before its start";
                return null;
            }
            var id = Id(spec);
            Func<bool, string> write = twice =>
            {
                var text = new StringBuilder(id).Append('_').Append(name);
                if (spec.Tbc)
                {
                    text.Append("_TBC");
                }
                if (start.HasValue)
                {
                    text.Append('_').Append(Date(start.Value));
                    if (finish.Value != start.Value || twice)
                    {
                        text.Append('_').Append(Date(finish.Value));
                    }
                }
                return text.ToString();
            };
            var composed = write(false);
            // A single day after a date in the name would read as a range with it: written twice, as the Planner's list
            // writes it.
            var once = PlannerNaming.ParseLayerName(composed);
            if (start.HasValue && finish.Value == start.Value && once != null && once.Start != start)
            {
                composed = write(true);
            }

            // It must read back exactly as meant.
            var parsed = PlannerNaming.ParseLayerName(composed);
            var key = PlannerLayerIdKey.FromName(composed);
            if (parsed == null || key == null || key.Phase != spec.Phase || key.Stage != spec.Stage || key.SubStage != spec.SubStage)
            {
                problem = "the name " + composed + " does not read back with the ID " + id + " (check the name: it must not start with a number)";
                return null;
            }
            if (parsed.Start != start || parsed.End != (start.HasValue ? finish : null) || parsed.Tbc != (spec.Tbc || !start.HasValue))
            {
                problem = "the name " + composed + " does not read back with the dates and TBC meant: its name must not hold TBC or end in a date";
                return null;
            }
            if (parsed.Issues.Count > 0)
            {
                problem = "the name " + composed + " reads with a problem: " + string.Join("; ", parsed.Issues.ToArray());
                return null;
            }
            if (!string.Equals(parsed.Label, id + "_" + name, StringComparison.Ordinal))
            {
                problem = "the name " + composed + " reads as " + parsed.Label + ": its name must not hold a date or TBC";
                return null;
            }
            return composed;
        }

        /// <summary>"02_01_2", "02_01" or "06".</summary>
        public static string Id(PlannerNewLayer spec)
        {
            return new PlannerLayerIdKey(spec.Phase, spec.Stage, spec.SubStage).ToString();
        }

        private static string Date(DateTime day)
        {
            return day.ToString("dd-MM-yy", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Why a new layer or group cannot go under <paramref name="parentLayer"/>, or null. The Planner files every layer in
        /// the phase group above it (or at the top) and every phase group in the one above it (nodeNames.ts layerGroupIndex),
        /// so a parent that is neither the phasing root (null or empty here) nor a phase group (_&lt;n&gt;_&lt;name&gt;) would
        /// not hold it in the Planner, and the next layer list from the Planner would move it out from under it.
        /// <paramref name="groupAbove"/>: the nearest phase group above the parent, to suggest instead, or null.
        /// </summary>
        public static string ParentProblem(string parentLayer, string groupAbove)
        {
            if (string.IsNullOrEmpty(parentLayer) || PlannerSchedule.ParsePhaseGroup(parentLayer) != null)
            {
                return null;
            }
            return "the Planner files a layer in the phase group above it (or at the top), not under " + parentLayer + ", so the next layer list from the "
                + "Planner would move it out from under " + parentLayer + ": choose " + (groupAbove != null ? groupAbove + "'s row" : "a phase group's row")
                + ", or no row for the phasing root";
        }

        /// <summary>The nearest phase group (_&lt;n&gt;_&lt;name&gt;) among the Max layers above a layer, or null.</summary>
        public static string PhaseGroupAbove(PlannerSceneModel scene, string layerName)
        {
            PhasingMaxLayer layer;
            if (scene == null || layerName == null || !scene.LayerByName.TryGetValue(layerName, out layer))
            {
                return null;
            }
            var above = scene.LayerAncestors(layer).FirstOrDefault(l => PlannerSchedule.ParsePhaseGroup(l.Name) != null);
            return above != null ? above.Name : null;
        }

        // The phase number of a phase group's name (_<n>_<name>), as Build reads it, or null.
        private static int? PhaseOf(string name)
        {
            int phase;
            return PhasingNameFixer.TryReadPhaseGroup(name, out phase) ? phase : (int?)null;
        }

        /// <summary>
        /// Plans adding the layer under <paramref name="parentLayer"/> (a Max layer name of the phasing tree, or null or
        /// empty for the phasing root), checking it against the scene, the Planner's list and the layers added before that
        /// are still in the scene.
        /// </summary>
        public static PlannerAddLayerPlan Plan(PlannerSceneSnapshot snapshot, string parentLayer, PlannerNewLayer spec, PlannerLayerList list, IEnumerable<PlannerAddedLayer> added)
        {
            var plan = new PlannerAddLayerPlan();
            var scene = new PlannerSceneModel(snapshot);
            string problem;
            var name = Compose(spec, out problem);
            plan.LayerName = name;
            if (name == null)
            {
                plan.Problem = problem;
                return plan;
            }
            // A layer added before and since undone, deleted or renamed by hand is no longer an added layer.
            var addedList = PlannerAddedLayerStore.Present(added, scene);
            var rows = list != null && list.Usable ? list.Rows : new List<PlannerLayerRow>();

            // Names: a Max layer (any capitals), a node, a row of the list or a layer added before.
            PhasingMaxLayer sameLayer;
            if (scene.LayerByName.TryGetValue(name, out sameLayer))
            {
                plan.Problem = "the scene has a Max layer called " + sameLayer.Name + " already";
                return plan;
            }
            if (scene.Nodes.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal)))
            {
                plan.Problem = "a node of the scene is called " + name + " already";
                return plan;
            }
            var sameRow = rows.FirstOrDefault(r => string.Equals(r.Layer, name, StringComparison.OrdinalIgnoreCase));
            if (sameRow != null)
            {
                plan.Problem = "the Planner's list has a layer called " + sameRow.Layer + " already";
                return plan;
            }
            // The ID, and for a group its name without capitals and its phase number: a layer or group of the phasing tree,
            // a row, a layer added before.
            var parsed = PlannerNaming.ParseLayerName(name);
            if (spec.Kind == PlannerLayerKind.Layer)
            {
                var key = PlannerLayerIdKey.FromName(name);
                var unit = scene.Units.FirstOrDefault(u => u.InPhasingTree && key.Equals(u.IdKey));
                if (unit != null)
                {
                    plan.Problem = "the layer " + unit.Name + " in the scene has the ID " + key + " already";
                    return plan;
                }
                var row = rows.FirstOrDefault(r => key.Equals(r.IdKey) || key.Equals(PlannerLayerIdKey.FromName(r.Layer)));
                if (row != null)
                {
                    plan.Problem = "the Planner's list has the ID " + key + " already (" + PlannerLayerList.Display(row) + ")";
                    return plan;
                }
                var before = addedList.FirstOrDefault(a => key.Equals(PlannerLayerIdKey.FromName(a.Layer)));
                if (before != null)
                {
                    plan.Problem = "the layer " + before.Layer + ", added in Max before, has the ID " + key + " already";
                    return plan;
                }
            }
            else
            {
                var unit = scene.Units.FirstOrDefault(u => u.InPhasingTree && string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
                if (unit != null)
                {
                    plan.Problem = "the phasing tree has a group called " + unit.Name + " already";
                    return plan;
                }
                // Build takes a phase group for the list's group of its phase number when that number is on one row of
                // the list and one group of the phasing tree (a group renamed in the Planner): a second group of a phase
                // would be taken for the other.
                int phase = spec.Phase.Value;
                var sameGroup = scene.Units.FirstOrDefault(u => u.InPhasingTree && u.IdKey == null && PhaseOf(u.Name) == phase);
                if (sameGroup != null)
                {
                    plan.Problem = "the phasing tree has a group of phase " + phase.ToString(CultureInfo.InvariantCulture) + " already (" + sameGroup.Name
                        + "): add the layers under it, as Build takes a group for the list's group of the same phase number";
                    return plan;
                }
                var sameGroupRow = rows.FirstOrDefault(r => PhaseOf(r.Layer) == phase);
                if (sameGroupRow != null)
                {
                    plan.Problem = "the Planner's list has a group of phase " + phase.ToString(CultureInfo.InvariantCulture) + " already (" + sameGroupRow.Layer
                        + "): build the layers and helpers to make it, then add the layers under it, as Build would take a second group of that phase for it";
                    return plan;
                }
            }
            if (addedList.Any(a => string.Equals(a.Layer, name, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Problem = "a layer called " + name + " was added in Max already";
                return plan;
            }

            // The parent: the root, or a phase group of the phasing tree with its helper.
            var build = new PlannerLayerPlan();
            PlannerLayerPlanner.PlanRoot(build, scene);
            int parentLayerIndex = 0;
            int parentHelperIndex = 0;
            plan.ParentName = build.RootName;
            if (!string.IsNullOrEmpty(parentLayer) && !(build.BuildLayers[0].CurrentName != null && string.Equals(parentLayer, build.BuildLayers[0].CurrentName, StringComparison.OrdinalIgnoreCase)))
            {
                var parent = scene.UnitOfLayer(parentLayer);
                if (parent == null || parent.Layer == null)
                {
                    plan.Problem = "its parent " + parentLayer + " is not a Max layer of the scene: build the layers and helpers first";
                    return plan;
                }
                if (parent.Helper == null)
                {
                    plan.Problem = "its parent " + parent.Layer.Name + " has no helper of its name: build the layers and helpers first";
                    return plan;
                }
                if (!parent.InPhasingTree)
                {
                    plan.Problem = "its parent " + parent.Layer.Name + " is outside the phasing root: build the layers and helpers first";
                    return plan;
                }
                var parentProblem = ParentProblem(parent.Layer.Name, PhaseGroupAbove(scene, parent.Layer.Name));
                if (parentProblem != null)
                {
                    plan.Problem = parentProblem;
                    return plan;
                }
                plan.ParentName = parent.Layer.Name;
                build.BuildLayers.Add(new PlannerBuildLayer
                {
                    Index = build.BuildLayers.Count,
                    CurrentName = parent.Layer.Name,
                    FinalName = parent.Layer.Name,
                    ParentNameNow = parent.Layer.ParentName
                });
                parentLayerIndex = build.BuildLayers.Count - 1;
                build.BuildHelpers.Add(new PlannerBuildHelper
                {
                    Index = build.BuildHelpers.Count,
                    Key = parent.Helper.Key,
                    CurrentName = parent.Helper.Name,
                    FinalName = parent.Helper.Name,
                    LayerIndex = parentLayerIndex,
                    LayerNow = parent.Helper.LayerName,
                    IsGroupHead = parent.Helper.IsGroupHead,
                    IsGroupMember = parent.Helper.IsGroupMember
                });
                parentHelperIndex = build.BuildHelpers.Count - 1;
            }
            var layer = new PlannerBuildLayer { Index = build.BuildLayers.Count, FinalName = name, Create = true, ParentIndex = parentLayerIndex };
            build.BuildLayers.Add(layer);
            build.BuildHelpers.Add(new PlannerBuildHelper
            {
                Index = build.BuildHelpers.Count,
                FinalName = name,
                Create = true,
                LayerIndex = layer.Index,
                ParentIndex = parentHelperIndex
            });
            plan.Build = build;

            if (build.RootLayerCreated || build.RootHelperCreated)
            {
                plan.Review.Add(new PlannerReviewRow
                {
                    Status = "New",
                    Item = "Phasing root",
                    Name = build.RootName,
                    Change = "Create " + (build.RootLayerCreated && build.RootHelperCreated ? "layer and helper" : build.RootLayerCreated ? "layer" : "helper"),
                    From = string.Empty,
                    To = "top of the scene",
                    Why = build.RootLayerCreated && build.RootHelperCreated ? "the scene has no phasing root" : "the phasing root has no " + (build.RootLayerCreated ? "layer" : "helper") + " of its name"
                });
            }
            var kind = spec.Kind == PlannerLayerKind.Group ? "Group" : "Layer";
            plan.Review.Add(new PlannerReviewRow
            {
                Status = "New",
                Item = "Layer and helper",
                Name = name,
                Change = "Create",
                From = string.Empty,
                To = "under " + plan.ParentName,
                Why = ReachesThePlanner(kind)
            });
            plan.Added = new PlannerAddedLayer
            {
                Layer = name,
                ParentLayer = parentLayerIndex == 0 ? string.Empty : build.BuildLayers[parentLayerIndex].FinalName,
                Kind = kind,
                Id = spec.Kind == PlannerLayerKind.Layer ? Id(spec) : string.Empty,
                Name = spec.Kind == PlannerLayerKind.Layer && parsed != null ? parsed.Name : Spaces.Replace((spec.Name ?? string.Empty).Trim(PlannerNaming.JavaScriptSpaceChars), "_"),
                Start = spec.Kind == PlannerLayerKind.Layer && parsed != null && parsed.Start.HasValue ? PlannerNaming.ToIsoDate(parsed.Start) : string.Empty,
                Finish = spec.Kind == PlannerLayerKind.Layer && parsed != null && parsed.End.HasValue ? PlannerNaming.ToIsoDate(parsed.End) : string.Empty,
                Tbc = spec.Kind == PlannerLayerKind.Layer && spec.Tbc
            };
            return plan;
        }

        /// <summary>How a layer or group added in Max ("Layer" or "Group") reaches the Planner, for the review and the tab.</summary>
        public static string ReachesThePlanner(string kind)
        {
            return string.Equals(kind, "Group", StringComparison.OrdinalIgnoreCase)
                ? "added in Max (phase group): not yet in the Planner; it reaches it with the next GLB once a layer in it holds a tagged object (Generate from model with Merge)"
                : "added in Max (layer): not yet in the Planner; it reaches it with the next GLB once a tagged object is linked under its helper (Generate from model with Merge)";
        }
    }
}
