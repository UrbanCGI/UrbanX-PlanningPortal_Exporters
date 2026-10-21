using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>What Add layer asks for: the ID and the name (the parent is the dialog's Under). No date: every date comes from the Planner's layer list (D20).</summary>
    public sealed class PlannerNewLayer
    {
        /// <summary>A group (holds layers) or a layer (holds work).</summary>
        public PlannerLayerKind Kind = PlannerLayerKind.Layer;
        /// <summary>A layer's phase, which it needs (a layer ID has two or three numbers); a group's phase number (_&lt;n&gt;_), which it needs too.</summary>
        public int? Phase;
        public int Stage;
        public int? SubStage;
        /// <summary>The descriptive name; spaces become underscores.</summary>
        public string Name;
    }

    /// <summary>A phase group, or in a scene with several phasing roots a root, Add layer can add under (<see cref="PlannerAddLayer.Parents"/>).</summary>
    public sealed class PlannerAddLayerParent
    {
        /// <summary>Its name: the Max layer's and its helper's (a root with no Max layer of its name yet: its helper's).</summary>
        public string Layer;
        /// <summary>
        /// How many of the entries listed with it are above it: 0 for a root, and for a phase group directly in the tree of
        /// a scene's one root.
        /// </summary>
        public int Depth;
        /// <summary>A phasing root: listed only in a scene with several, each with its phase groups beneath it.</summary>
        public bool IsRoot;
    }

    /// <summary>A layer added in 3ds Max and not yet in the Planner's list, kept with the scene. It holds no date (D20).</summary>
    public sealed class PlannerAddedLayer
    {
        /// <summary>Its name: the Max layer's and its helper's.</summary>
        public string Layer;
        /// <summary>
        /// The Max layer it was added under: a phase group's, or its root's for one added straight under a root. A record
        /// written before there could be several roots has it empty for the one phasing root.
        /// </summary>
        public string ParentLayer = string.Empty;
        /// <summary>"Group" or "Layer".</summary>
        public string Kind;
        /// <summary>Its layer ID as the list writes it ("02_01_2"), or empty.</summary>
        public string Id = string.Empty;
        public string Name = string.Empty;
        /// <summary>
        /// Its layer, parent layer, ID and name before Clean up scene graph renamed it with its layer (empty when it never
        /// was): an Edit > Undo of the clean-up puts the layer back under that name, and the record follows it.
        /// </summary>
        public string WasLayer = string.Empty;
        public string WasParentLayer = string.Empty;
        public string WasId = string.Empty;
        public string WasName = string.Empty;
    }

    /// <summary>
    /// The layers added in Max as they are kept with the scene: a short header ("PlannerAddedLayers 2") and a CSV (byte order
    /// mark left out) with the columns Layer, Parent layer, Kind, ID, Name, then the names before Clean up scene graph renamed
    /// it (Was layer, Was parent layer, Was ID, Was name). No date is kept (D20). A record of the first version
    /// ("PlannerAddedLayers 1", with Start, Finish and TBC after Name, and perhaps no Was columns) is still read, its dates
    /// passed over, and is written again in this form.
    /// </summary>
    public static class PlannerAddedLayerStore
    {
        private const string Magic = "PlannerAddedLayers 2";
        private const string MagicVersion1 = "PlannerAddedLayers 1";
        public const string Header = "Layer,Parent layer,Kind,ID,Name,Was layer,Was parent layer,Was ID,Was name";

        public static string Write(IEnumerable<PlannerAddedLayer> layers)
        {
            var text = new StringBuilder();
            text.Append(Magic).Append('\n').Append(Header).Append("\r\n");
            foreach (var layer in layers ?? Enumerable.Empty<PlannerAddedLayer>())
            {
                var cells = new[]
                {
                    layer.Layer, layer.ParentLayer, layer.Kind, layer.Id, layer.Name,
                    layer.WasLayer, layer.WasParentLayer, layer.WasId, layer.WasName
                };
                text.Append(string.Join(",", cells.Select(c => PlannerReview.Field(c ?? string.Empty)).ToArray())).Append("\r\n");
            }
            return text.ToString();
        }

        /// <summary>Reads what <see cref="Write"/> wrote, or a record of the first version (its dates passed over); an empty list when the text is in neither form.</summary>
        public static List<PlannerAddedLayer> Read(string stored)
        {
            var result = new List<PlannerAddedLayer>();
            bool version1 = stored != null && stored.StartsWith(MagicVersion1 + "\n", StringComparison.Ordinal);
            if (stored == null || (!version1 && !stored.StartsWith(Magic + "\n", StringComparison.Ordinal)))
            {
                return result;
            }
            // The first version has Start, Finish and TBC after Name: the Was columns come three later.
            int was = version1 ? 8 : 5;
            var records = PlannerLayerList.ReadRecords(stored.Substring((version1 ? MagicVersion1 : Magic).Length + 1), ',');
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
                    WasLayer = cell(was),
                    WasParentLayer = cell(was + 1),
                    WasId = cell(was + 2),
                    WasName = cell(was + 3)
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
        /// A record Clean up scene graph renamed (its layer, or its parent's) whose layers have their names from before (the
        /// clean-up taken back with Edit > Undo, or put back again with Redo) follows them: its names and its names before
        /// change places. Nothing is changed in the records kept with the scene (<see cref="ForScene"/>).
        /// </summary>
        public static List<PlannerAddedLayer> Present(IEnumerable<PlannerAddedLayer> added, PlannerSceneModel scene)
        {
            return Placed(added, scene, false);
        }

        /// <summary>
        /// The records kept with the scene, each as the scene names its layer now (<see cref="Present"/>), and the records whose
        /// layer the scene does not have now kept as they are: an Edit > Redo may bring the layer back, so such a record stays
        /// with the scene (left out of what the tab shows and checks) until a layer of its name is added again or the Planner's
        /// list has it.
        /// </summary>
        public static List<PlannerAddedLayer> ForScene(IEnumerable<PlannerAddedLayer> added, PlannerSceneModel scene)
        {
            return Placed(added, scene, true);
        }

        private static List<PlannerAddedLayer> Placed(IEnumerable<PlannerAddedLayer> added, PlannerSceneModel scene, bool keepAbsent)
        {
            var list = (added ?? Enumerable.Empty<PlannerAddedLayer>()).Where(a => a != null && !string.IsNullOrEmpty(a.Layer)).ToList();
            if (scene == null)
            {
                return list;
            }
            Func<string, bool> has = name => !string.IsNullOrEmpty(name) && scene.LayerByName.ContainsKey(name);
            var result = new List<PlannerAddedLayer>();
            foreach (var a in list)
            {
                // Its names now, unless the scene has its names from before and not its parent now (a rename taken back).
                bool layerNow = has(a.Layer);
                bool layerWas = has(a.WasLayer);
                bool parentNow = string.IsNullOrEmpty(a.ParentLayer) || has(a.ParentLayer);
                bool parentWas = string.IsNullOrEmpty(a.WasParentLayer) || has(a.WasParentLayer);
                if (layerNow && (parentNow || !layerWas || !parentWas))
                {
                    result.Add(a);
                }
                else if (layerWas)
                {
                    result.Add(new PlannerAddedLayer
                    {
                        Layer = a.WasLayer,
                        ParentLayer = a.WasParentLayer ?? string.Empty,
                        Kind = a.Kind,
                        Id = a.WasId ?? string.Empty,
                        Name = a.WasName ?? string.Empty,
                        WasLayer = a.Layer,
                        WasParentLayer = a.ParentLayer ?? string.Empty,
                        WasId = a.Id ?? string.Empty,
                        WasName = a.Name ?? string.Empty
                    });
                }
                else if (keepAbsent)
                {
                    result.Add(a);
                }
            }
            return result;
        }

        /// <summary>
        /// The records to keep with the scene once a layer is added: the scene's own records (read from it, so a record
        /// another run saved is kept), any record of the same layer name dropped (a layer added before and taken back, now
        /// added again), then the new one.
        /// </summary>
        public static List<PlannerAddedLayer> WithAdded(IEnumerable<PlannerAddedLayer> stored, PlannerAddedLayer fresh)
        {
            var result = (stored ?? Enumerable.Empty<PlannerAddedLayer>())
                .Where(a => a != null && (fresh == null || !string.Equals(a.Layer, fresh.Layer, StringComparison.OrdinalIgnoreCase))).ToList();
            if (fresh != null)
            {
                result.Add(fresh);
            }
            return result;
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
    /// Add layer in the Schedule tab: a phase group or a layer made in 3ds Max under a phasing root or a phase group, as
    /// the Planner files layers only there (<see cref="Parents"/>; the dialog starts on the row chosen, <see
    /// cref="StartUnder"/>), its name composed in the new names' convention
    /// (&lt;ID&gt;_&lt;Name&gt;, the ID two or three numbers; a group _&lt;nn&gt;_&lt;Name&gt;) with no date (D20: every date comes
    /// from the Planner's layer list), checked to read back with the ID and the name intended (<see
    /// cref="PlannerNaming.ParseLayerNameV2"/>) and not to clash with a layer, node, ID, phase number or list row already
    /// there, in any root, then built with the layer builder's own batch (<see cref="PlannerLayerScript"/>): the Max layer
    /// under its parent's layer and a Dummy of the same name on it, linked under the parent's helper, whichever root the
    /// parent is in. Add layer makes no new root, but Work_Phasing in a scene with none. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerAddLayer
    {
        /// <summary>Why Add layer waits on a scene that reads as the old Ph/St names (D23).</summary>
        public const string OldNamesProblem = "the scene uses the old Ph/St names, and Add layer waits until it reads as the new names: run Clean up scene graph first";

        private static readonly Regex Spaces = new Regex(PlannerNaming.JavaScriptSpace + "+", RegexOptions.CultureInvariant);

        /// <summary>
        /// The name the convention gives the new layer or group, or null with why it cannot be written: a layer
        /// &lt;ID&gt;_&lt;Name&gt; ("02_01_3_Barrier_works"), its phase needed, which must read back as that ID and that name (a
        /// name led by a number would be read as part of the ID) with no date or TBC in it; a group _&lt;nn&gt;_&lt;Name&gt;.
        /// </summary>
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
                if (!PhasingNameFixer.TryReadPhaseGroup(group, out phase) || phase != spec.Phase.Value || PlannerSchedule.ParsePhaseGroup(group) == null)
                {
                    problem = "the name " + group + " does not read back as the group of phase " + spec.Phase.Value.ToString(CultureInfo.InvariantCulture);
                    return null;
                }
                return group;
            }

            if (!spec.Phase.HasValue)
            {
                // One number is no layer on the new names (spec 2.1).
                problem = "give it its phase: a layer ID has two or three numbers (phase, stage, and a sub-stage if it has one)";
                return null;
            }
            if (spec.Phase.Value < 0 || spec.Phase.Value > 99 || spec.Stage < 0 || spec.Stage > 99
                || (spec.SubStage.HasValue && (spec.SubStage.Value < 1 || spec.SubStage.Value > 99)))
            {
                problem = "each number of a layer ID has one or two digits (a sub-stage from 1)";
                return null;
            }
            var id = Id(spec);
            var composed = id + "_" + name;

            // It must read back exactly as meant: the ID, then the name as written. After the ID a name led by a number and an
            // underscore reads as a further number of the ID ("02_05" and "3_Storey_Frame" give 02_05_3). A "(n)" at the end is
            // read as the copy number 3ds Max gives a copied node, and dropped ("Hoarding (1)" reads as "Hoarding_").
            var parsed = PlannerNaming.ParseLayerNameV2(composed);
            if (parsed == null || PlannerNaming.LayerIdKey(parsed.Id) != PlannerNaming.LayerIdKey(new PlannerLayerIdKey(spec.Phase, spec.Stage, spec.SubStage)))
            {
                problem = "the name " + composed + " does not read back with the ID " + id
                    + (parsed != null ? " (it reads as " + PlannerNaming.LayerIdText(parsed.Id) + ")" : string.Empty) + ": the name must not start with a number";
                return null;
            }
            if (!string.Equals(parsed.Name, name, StringComparison.Ordinal))
            {
                problem = PlannerNaming.DuplicateSuffix.IsMatch(composed)
                    ? "the name must not end in a number in brackets: the Planner reads \"" + PlannerNaming.DuplicateSuffix.Match(composed).Value.Trim(PlannerNaming.JavaScriptSpaceChars)
                        + "\" at the end of a name as the copy number 3ds Max gives a copied node, and drops it"
                    : "the name " + composed + " reads back as " + id + (parsed.Name.Length > 0 ? "_" + parsed.Name : string.Empty) + ", not as written";
                return null;
            }
            if (parsed.Issues.Contains(PlannerNamingCode.Dates))
            {
                problem = "its name must not hold a date or TBC: the Planner holds every date";
                return null;
            }
            if (parsed.Issues.Count > 0)
            {
                problem = "the name " + composed + " reads with a problem: " + string.Join(" ", parsed.Issues.Select(i => PlannerNaming.NamingIssueV2[i].Text).ToArray());
                return null;
            }
            return composed;
        }

        /// <summary>The layer ID the convention writes: "02_01_2" or "02_01".</summary>
        public static string Id(PlannerNewLayer spec)
        {
            return new PlannerLayerIdKey(spec.Phase, spec.Stage, spec.SubStage).ToString();
        }

        /// <summary>
        /// Why a new layer or group cannot go under <paramref name="parentLayer"/>, or null. The Planner files every layer in
        /// the phase group above it (or at the top) and every phase group in the one above it (nodeNames.ts layerGroupIndex),
        /// so a parent that is neither the phasing root (null or empty here) nor a phase group (_&lt;n&gt;_&lt;name&gt;) would
        /// not hold it in the Planner, and the next layer list from the Planner would move it out from under it.
        /// <paramref name="groupAbove"/>: the nearest phase group above the parent, to suggest instead, or null.
        /// <paramref name="root"/>: the parent's root, to suggest by name in a scene with several, or null for "the phasing root".
        /// </summary>
        public static string ParentProblem(string parentLayer, string groupAbove, string root = null)
        {
            if (string.IsNullOrEmpty(parentLayer) || PlannerSchedule.ParsePhaseGroup(parentLayer) != null)
            {
                return null;
            }
            return "the Planner files a layer in the phase group above it (or at the top), not under " + parentLayer + ", so the next layer list from the "
                + "Planner would move it out from under " + parentLayer + ": add it under " + (groupAbove ?? "a phase group") + " or " + (root ?? "the phasing root") + " instead";
        }

        /// <summary>
        /// Where a layer or a phase group can be added (Add layer's Under list): the phase groups of the phasing tree with
        /// their Max layer and their helper, as <see cref="Plan"/> takes them, in the tree's order: from the root down, a
        /// group's own groups after it, siblings in the Planner's name order. Each has its depth: how many of the entries
        /// listed are above it. A phase group may sit in another: the Planner reads it as one. With one root, the root is not
        /// listed (the dialog offers it as the phasing root, and a scene with none gets one). With several, each root is
        /// listed, in name order, with its phase groups beneath it, and there is no entry for the top of the scene: Add
        /// layer makes no new root. A root with no Max layer of its name has none of its groups listed, and nothing is added
        /// under it until Clean up scene graph gives it one.
        /// </summary>
        public static List<PlannerAddLayerParent> Parents(PlannerSceneModel scene)
        {
            var result = new List<PlannerAddLayerParent>();
            if (scene == null)
            {
                return result;
            }
            var units = new Dictionary<string, PlannerSceneUnit>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in scene.Units.Where(u => u.Layer != null))
            {
                if (!units.ContainsKey(unit.Layer.Name))
                {
                    units[unit.Layer.Name] = unit;
                }
            }
            // Every root's layer counts as seen: a root's layer sitting in another root's tree holds that root's groups,
            // listed under it.
            var seen = new HashSet<string>(scene.PhasingRoots.Where(r => r.Layer != null).Select(r => r.Layer.Name), StringComparer.OrdinalIgnoreCase);
            Action<string, int> walk = null;
            walk = (layerName, depth) =>
            {
                foreach (var child in scene.ChildLayersOf(layerName).OrderBy(l => l.Name, PlannerNaturalOrder.Comparer).ToList())
                {
                    if (!seen.Add(child.Name))
                    {
                        continue;
                    }
                    PlannerSceneUnit unit;
                    bool takes = PlannerSchedule.ParsePhaseGroup(child.Name) != null && units.TryGetValue(child.Name, out unit) && unit.Helper != null && unit.InPhasingTree;
                    if (takes)
                    {
                        result.Add(new PlannerAddLayerParent { Layer = child.Name, Depth = depth });
                    }
                    walk(child.Name, takes ? depth + 1 : depth);
                }
            };
            bool several = scene.PhasingRoots.Count > 1;
            foreach (var root in scene.PhasingRoots)
            {
                if (several)
                {
                    result.Add(new PlannerAddLayerParent { Layer = RootName(root), Depth = 0, IsRoot = true });
                }
                if (root.Layer != null)
                {
                    walk(root.Layer.Name, several ? 1 : 0);
                }
            }
            return result;
        }

        /// <summary>
        /// Where Add layer's Under starts for the row chosen in the tab, by its Max layer (null or empty: no row chosen, the
        /// first root): that layer when it is one of <paramref name="parents"/> (a phase group, or in a scene with several
        /// roots a root); else the nearest of them above it, or the first root (null), and <paramref name="note"/> says why,
        /// for the dialog to show. The note is null when Under starts on the row chosen, or none was chosen.
        /// </summary>
        public static string StartUnder(PlannerSceneModel scene, IList<PlannerAddLayerParent> parents, string chosenLayer, out string note)
        {
            note = null;
            if (scene == null || string.IsNullOrEmpty(chosenLayer))
            {
                return null;
            }
            parents = parents ?? new List<PlannerAddLayerParent>();
            var own = parents.FirstOrDefault(p => string.Equals(p.Layer, chosenLayer, StringComparison.OrdinalIgnoreCase));
            if (own != null)
            {
                return own.Layer;
            }
            PhasingMaxLayer layer;
            if (!scene.LayerByName.TryGetValue(chosenLayer, out layer) || RootNamed(scene, layer.Name) != null)
            {
                return null;
            }
            bool several = scene.PhasingRoots.Count > 1;
            string why;
            if (!scene.TreeLayers.Contains(layer.Name))
            {
                why = layer.Name + " is outside " + (several ? "every phasing root" : "the phasing root");
            }
            else if (PlannerSchedule.ParsePhaseGroup(layer.Name) != null)
            {
                why = layer.Name + " has no helper of its name yet (Clean up scene graph with the Planner's list imported makes it)";
            }
            else
            {
                why = layer.Name + " is not a phase group, and the Planner files a layer in the phase group above it (or at the top)";
            }
            var above = scene.LayerAncestors(layer).Select(l => parents.FirstOrDefault(p => string.Equals(p.Layer, l.Name, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(p => p != null);
            var start = above != null ? above.Layer : null;
            note = why + ", so Under starts on " + (start ?? (several ? RootName(scene.FirstRoot) : "the phasing root")) + ".";
            return start;
        }

        // A root's name as Add layer gives it: its Max layer's, else its helper's (a root has the Max layer of its name).
        private static string RootName(PlannerPhasingRoot root)
        {
            return root.Layer != null ? root.Layer.Name : root.Name;
        }

        // The root a parent names, by its Max layer or its helper (any capitals), or null.
        private static PlannerPhasingRoot RootNamed(PlannerSceneModel scene, string name)
        {
            return scene.PhasingRoots.FirstOrDefault(r => string.Equals(RootName(r), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // Where a unit of the phasing tree is, for a problem's text: its root in a scene with several, else the scene.
        private static string Where(PlannerSceneModel scene, PlannerSceneUnit unit)
        {
            return scene.PhasingRoots.Count > 1 && unit.Root != null ? "in " + RootName(unit.Root) : "in the scene";
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
        /// Plans adding the layer under <paramref name="parentLayer"/> (a phase group's Max layer in any root, a root by its
        /// Max layer or helper, or null or empty for the first root), checking it against the scene (every root), the
        /// Planner's list and the layers added before that are still in the scene.
        /// </summary>
        public static PlannerAddLayerPlan Plan(PlannerSceneSnapshot snapshot, string parentLayer, PlannerNewLayer spec, PlannerLayerList list, IEnumerable<PlannerAddedLayer> added)
        {
            var plan = new PlannerAddLayerPlan();
            var scene = new PlannerSceneModel(snapshot);
            bool several = scene.PhasingRoots.Count > 1;
            string problem;
            var name = Compose(spec, out problem);
            plan.LayerName = name;
            if (name == null)
            {
                plan.Problem = problem;
                return plan;
            }
            // On a scene that reads as the old Ph/St names Add layer waits for Clean up scene graph (D23). The scene is read as
            // it is now, so a clean-up taken back with Edit > Undo since the tab last read it is caught too.
            var naming = PlannerSchedule.NamingOfScene(new PlannerScheduleModel(snapshot));
            if (naming != null && naming.Naming == PlannerNamingKind.Old)
            {
                plan.Problem = OldNamesProblem;
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
            // The ID, and for a group its name without capitals and its phase number: a layer or group of the phasing tree
            // (every root's: IDs, group names and phase numbers are unique across roots), a row, a layer added before.
            var parsed = PlannerNaming.ParseLayerNameV2(name);
            if (spec.Kind == PlannerLayerKind.Layer)
            {
                var key = PlannerLayerIdKey.FromName(name);
                var unit = scene.Units.FirstOrDefault(u => u.InPhasingTree && key.Equals(u.IdKey));
                if (unit != null)
                {
                    plan.Problem = "the layer " + unit.Name + " " + Where(scene, unit) + " has the ID " + key + " already";
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
                    plan.Problem = "the phasing tree has a group called " + unit.Name + " already" + (several ? " (" + Where(scene, unit) + ")" : string.Empty);
                    return plan;
                }
                // Clean up scene graph takes a phase group for the list's group of its phase number when that number is on
                // one row of the list and one group of the phasing tree (a group renamed in the Planner): a second group of a
                // phase would be taken for the other. So a phase number belongs to one group in the whole scene, and each
                // root takes its own phases (R21).
                int phase = spec.Phase.Value;
                var sameGroup = scene.Units.FirstOrDefault(u => u.InPhasingTree && u.IdKey == null && PhaseOf(u.Name) == phase);
                if (sameGroup != null)
                {
                    plan.Problem = "the phasing tree has a group of phase " + phase.ToString(CultureInfo.InvariantCulture) + " already (" + sameGroup.Name
                        + (several
                            ? ", " + Where(scene, sameGroup) + "): a phase number belongs to one phase group across every root, so give the group a phase no root "
                                + "has yet, or add the layers under " + sameGroup.Name
                            : "): add the layers under it, as Clean up scene graph takes a group for the list's group of the same phase number");
                    return plan;
                }
                var sameGroupRow = rows.FirstOrDefault(r => PhaseOf(r.Layer) == phase);
                if (sameGroupRow != null)
                {
                    plan.Problem = "the Planner's list has a group of phase " + phase.ToString(CultureInfo.InvariantCulture) + " already (" + sameGroupRow.Layer
                        + "): run Clean up scene graph with the list imported to make it, then add the layers under it, as Clean up would take a second group of that phase for it";
                    return plan;
                }
            }
            if (addedList.Any(a => string.Equals(a.Layer, name, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Problem = "a layer called " + name + " was added in Max already";
                return plan;
            }

            // The parent: a root (none named: the first root, or Work_Phasing in a scene with none), or a phase group of the
            // phasing tree with its helper, in whichever root it is. A name that is neither makes no new root.
            var root = string.IsNullOrEmpty(parentLayer) ? scene.FirstRoot : RootNamed(scene, parentLayer);
            PlannerSceneUnit parent = null;
            if (!string.IsNullOrEmpty(parentLayer) && root == null)
            {
                parent = scene.UnitOfLayer(parentLayer);
                if (parent == null || parent.Layer == null)
                {
                    plan.Problem = "its parent " + parentLayer + " is not a Max layer of the scene: run Clean up scene graph with the Planner's list imported first";
                    return plan;
                }
                if (parent.Helper == null)
                {
                    plan.Problem = "its parent " + parent.Layer.Name + " has no helper of its name: run Clean up scene graph with the Planner's list imported first";
                    return plan;
                }
                if (!parent.InPhasingTree)
                {
                    plan.Problem = "its parent " + parent.Layer.Name + " is outside " + (several ? "every phasing root" : "the phasing root")
                        + ": run Clean up scene graph with the Planner's list imported first";
                    return plan;
                }
                root = parent.Root ?? scene.FirstRoot;
                var parentProblem = ParentProblem(parent.Layer.Name, PhaseGroupAbove(scene, parent.Layer.Name), several && root != null ? RootName(root) : null);
                if (parentProblem != null)
                {
                    plan.Problem = parentProblem;
                    return plan;
                }
            }
            // A root with no Max layer of its name waits for Clean up, which gives it one and brings its groups' layers under it.
            if (root != null && root.Node != null && root.Layer == null)
            {
                plan.Problem = "the phasing root " + root.Name + " has no Max layer of its name: run Clean up scene graph first, which gives it one";
                return plan;
            }
            var build = new PlannerLayerPlan();
            PlannerLayerPlanner.PlanOneRoot(build, scene, root);
            int parentLayerIndex = 0;
            int parentHelperIndex = 0;
            plan.ParentName = build.RootName;
            if (parent != null)
            {
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
                // Straight under a root: the root's Max layer, so the tab shows it under that root.
                ParentLayer = build.BuildLayers[parentLayerIndex].FinalName,
                Kind = kind,
                Id = spec.Kind == PlannerLayerKind.Layer ? Id(spec) : string.Empty,
                Name = spec.Kind == PlannerLayerKind.Layer && parsed != null ? parsed.Name : Spaces.Replace((spec.Name ?? string.Empty).Trim(PlannerNaming.JavaScriptSpaceChars), "_")
            };
            return plan;
        }

        /// <summary>
        /// How a layer or group added in Max ("Layer" or "Group") reaches the Planner, for the review and the tab (spec 5.5): a
        /// layer with the next GLB even while empty, as long as its helper is exported, arriving TBC (the tab highlights it TBC
        /// until the Planner's list has it, D21).
        /// </summary>
        public static string ReachesThePlanner(string kind)
        {
            return string.Equals(kind, "Group", StringComparison.OrdinalIgnoreCase)
                ? "added in Max (phase group): not yet in the Planner; it reaches it with the next GLB once a layer in it does (Generate from model with Merge)"
                : "added in Max (layer): not yet in the Planner; it reaches it with the next GLB, even while empty, as long as its helper is exported, and arrives TBC (Generate from model with Merge)";
        }
    }
}
