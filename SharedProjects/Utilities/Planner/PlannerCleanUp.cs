using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>
    /// Plans Clean up scene graph (naming v2, spec 5.1 and 6.1; it replaces Fix phasing names, D7): every step is planned on
    /// a simulated snapshot of the scene, in the order the 3ds Max side runs them as one batch in one undo record.
    ///
    ///   1. Layers and helpers. Dates and TBC come out of the names wherever the Planner reads them, word fixes and spaces
    ///      are corrected, the ID is written the current way (a one-number layer takes its phase from its objects, as Fix
    ///      phasing names did), a layer directly under the root goes into its phase group, and the Max layer is renamed with
    ///      its helper. Inside a layer, a node led by a number is a layer only when the new names read it as one (a Max group
    ///      "3 Bay Gate" is not; it is listed when the Planner would not schedule the tagged objects under it). Nothing is
    ///      renumbered. The dates taken out are not kept anywhere in 3ds Max (D20): every date comes from the Planner's list.
    ///      With the Planner's layer list imported (D22), the layers and helpers are then brought in line with it, as Build
    ///      layers and helpers did (<see cref="PlannerLayerPlanner"/>, on the scene as the names above leave it): each row
    ///      finds its layer and helper by planner_id, then layer ID, then exact name; they take the row's name, never with
    ///      dates (<see cref="WithoutDates"/>), and its Planner ID, and are nested as the list nests them; a row the scene
    ///      does not have gets a new layer and helper. Nothing is deleted: a layer the list does not have is listed. A layer
    ///      whose ID the list changes takes the references to it along (spec 5.3).
    ///   2. Link objects to their helpers: today's Link (<see cref="PlannerLinkPlanner"/>), on the scene as step 1 leaves it.
    ///   3. Objects, on the hierarchy as step 2 leaves it, each read against its own layer (the nearest layer node above it):
    ///      old Ph/St tags become IN_ / RM_ at the start and _IN / _RM [_&lt;ID&gt;] at the end, hyphens become underscores,
    ///      underscores at the ends of the own name go, and the word fixes apply. Split pieces are renamed with their object.
    ///   4. Uniqueness (D15): an own name that an object earlier in layer order has (IDs compared as numbers, then full names)
    ///      is numbered from _02, past numbers in use, before any trailing tag; never on a name that reads with an error. An
    ///      object that keeps its name but whose own name is known (a new name listed on an old-name scene, an object waiting
    ///      for an answer) holds its place in that order. A number that would end the own name in a layer ID before a plain
    ///      trailing tag is passed over, and one that would make the object a split piece of the node above it is not given.
    ///
    /// What it cannot convert safely is asked (<see cref="PlannerCleanUpQuestion"/>): a closing _RM / _IN with no Ph/St, a
    /// leading tag naming another layer, a doubled type word, a trailing tag naming no layer (answered by default with the
    /// ID as the tag writes it, which the Planner parks TBC, unless the Planner's list now gives that ID to a layer that had
    /// another) or several. Only the answers given are applied; an object with an open question keeps its old name. A new
    /// name whose reference names an ID the list now gives a layer that had another is listed, not rewritten (spec 5.3).
    /// Every name it plans reads back through the naming v2 reader with the tags and the reference intended, or the object
    /// is listed and keeps its name. It never guesses. Pure logic, no 3ds Max.
    /// </summary>
    public static partial class PlannerCleanUp
    {
        public const string UndoLabel = "Clean up scene graph";
        public const string NothingToDo = "No top-level node has \"phasing\" in its name, so there is no phasing to clean up. "
            + "Import the Planner's layer list to build the phasing layers and helpers from it.";
        /// <summary>
        /// Said before Clean up runs on a scene on the old names (spec 5.1): the 3ds Max tool is released before the Planner that
        /// reads the new names (D25), so a live project's scene waits for the planning team's go-ahead.
        /// </summary>
        public const string OldNamesWarning = "Clean up converts this scene to the new names, which the Planner reads only from its next release. "
            + "On a live project, do not clean up or export this scene until the planning team says to go ahead.";
        /// <summary>What the copy of an old-name scene is called: &lt;scene&gt;_before_v2.max.</summary>
        public const string BeforeV2Suffix = "_before_v2";

        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const string NotLineEnd = @"[^\n\r\u2028\u2029]";

        // A phase group, _<n>_<name>.
        private static readonly Regex GroupPattern = new Regex(@"^_([0-9]+)_(" + NotLineEnd + @"+)\z", Options);
        // A date and the TBC word as the Planner reads them in a layer name: the naming reader's own patterns, so the two
        // never disagree on what a date is.
        private static readonly Regex DateToken = PlannerNaming.DateToken;
        private static readonly Regex TbcToken = PlannerNaming.TbcToken;
        // A layer of one number as the Planner reads one ("05_Retainment"): the stage, underscores or spaces, then its name.
        private static readonly Regex OneNumberLayer = new Regex(@"^([0-9]+)[_ ]+(" + NotLineEnd + @"+)\z", Options);
        // A layer ID of two or three numbers with a space somewhere an underscore belongs, as the Planner's old reader takes it
        // ("02_07 Duct Works_19-10-26", "02_07 3_Kerbs"): phase, stage, sub-stage, then the rest.
        private static readonly Regex SpacedLayer = new Regex(@"^([0-9]{1,2})[_ ]+([0-9]{1,2})(?:[_ ]+([0-9]{1,2}))?[_ ]+(" + NotLineEnd + @"+)\z", Options);
        // The old trailing tag, as the Planner's old reader finds it: pinned to the end, or before a note.
        private static readonly Regex OldTrail = PlannerNaming.Trail;
        // A type word with its underscore starting a name: a leading tag on the new names, or, at the start of an own name
        // after the leading one, a second type word ("RM_Marwood_Barrier3" after a leading RM_).
        private static readonly Regex TypeWordFirst = new Regex(@"^(IN|RM)_", Options);
        // A type word closing an own name with no Ph/St, perhaps with a layer ID after it ("..._RM", "..._RM_06_05_2"): the
        // new names' trailing tag.
        private static readonly Regex ClosingWord = PlannerNaming.TagTrailV2;
        private static readonly Regex EdgeUnderscores = PlannerNaming.EdgeUnderscores;
        private static readonly Regex EdgeSeparators = PlannerNaming.EdgeSeparators;
        private static readonly Regex NumberLed = new Regex("^[0-9]", Options);
        // A sub-stage after a full stop as the convention writes one: the phase, then a two-digit stage, the full stop and the
        // sub-stage, then an underscore or the end, and no "_x_" after it ("02_04.2_Roads", never "01_10.8_x_2.4_Panel").
        private static readonly Regex SubStageFullStop = new Regex(@"^[0-9]{1,2}[_ ]+[0-9]{2}\.[0-9]+(?=_|\z)(?!_[xX]_)", Options);

        /// <summary>The copy an old-name scene is saved as before Clean up: "Site.max" gives "Site_before_v2.max".</summary>
        public static string BeforeV2FileName(string sceneFileName)
        {
            var name = string.IsNullOrEmpty(sceneFileName) ? "Untitled.max" : sceneFileName;
            var dot = name.LastIndexOf('.');
            var slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            return dot > slash ? name.Substring(0, dot) + BeforeV2Suffix + name.Substring(dot) : name + BeforeV2Suffix + ".max";
        }

        /// <summary>
        /// Plans Clean up scene graph on <paramref name="snapshot"/> with the scene's word fixes. Nothing is changed: the plan's
        /// <see cref="PlannerCleanUpPlan.Resolve"/> gives the operations for the modeller's answers.
        /// </summary>
        public static PlannerCleanUpPlan Plan(PlannerSceneSnapshot snapshot, IEnumerable<PhasingWordFix> wordFixes)
        {
            return Plan(snapshot, wordFixes, null);
        }

        /// <summary>
        /// Plans Clean up scene graph on <paramref name="snapshot"/> with the scene's word fixes and, when it is usable, the
        /// Planner's layer list (D22): the layers and helpers are then also brought in line with the list, in the same review
        /// and the same batch; a scene with no phasing root gets one, with the list built under it. Without a usable list,
        /// Clean up cleans the scene's own names only. Nothing is changed: the plan's <see cref="PlannerCleanUpPlan.Resolve"/>
        /// gives the operations for the modeller's answers.
        /// </summary>
        public static PlannerCleanUpPlan Plan(PlannerSceneSnapshot snapshot, IEnumerable<PhasingWordFix> wordFixes, PlannerLayerList list)
        {
            var plan = new PlannerCleanUpPlan();
            var state = new State { Original = snapshot ?? new PlannerSceneSnapshot() };
            plan.State = state;
            foreach (var fix in wordFixes ?? Enumerable.Empty<PhasingWordFix>())
            {
                var problem = PhasingNameFixer.DescribeWordFixProblem(fix);
                if (problem == null)
                {
                    state.Fixes.Add(fix);
                }
                else if (fix != null && !string.IsNullOrEmpty(fix.Find))
                {
                    plan.Notes.Add(string.Format("The word fix \"{0}\" to \"{1}\" is not used: {2}.", fix.Find, fix.Replace, problem));
                }
            }
            var scene = new PlannerSceneModel(state.Original);
            var buildList = list != null && list.Usable ? WithoutDates(list) : null;
            if (scene.RootNode == null && buildList == null)
            {
                plan.Message = NothingToDo;
                return plan;
            }
            var notes = new PhasingNameFixer.TextFixNotes();
            if (scene.RootNode != null)
            {
                plan.RootKey = scene.RootNode.Key;
                plan.RootName = scene.RootNode.Name;
                var naming = PlannerSchedule.NamingOfScene(new PlannerScheduleModel(state.Original));
                plan.Naming = naming != null ? naming.Naming : (PlannerNamingKind?)null;
                state.OldScene = plan.Naming == PlannerNamingKind.Old;
                // Step 1: layers and helpers, and their Max layers.
                PlanLayers(state, scene, notes);
            }
            var step1 = Apply(state.Original, state.StepOneRenames(), state.NodeMoves, null, null, state.MaxLayerRenames, state.MaxLayerMoves);
            foreach (var layer in state.Layers)
            {
                layer.SceneId = layer.Id;
            }
            if (buildList != null)
            {
                // Step 1, with the Planner's list: the layers and helpers brought in line with it.
                plan.List = buildList;
                step1 = PlanList(state, plan, buildList, step1);
                if (plan.RootName == null)
                {
                    plan.RootName = state.Top.NewName;
                }
            }
            state.Step1 = step1;
            // Step 2: Link objects to their helpers, as it runs on the scene step 1 leaves.
            state.LinkPlan = PlannerLinkPlanner.Plan(step1);
            if (state.LinkPlan.Message != null)
            {
                plan.Notes.Add("Link objects to their helpers: " + state.LinkPlan.Message);
            }
            plan.Notes.AddRange(state.LinkPlan.Notes);
            state.Step2 = Apply(step1, null, null, state.LinkPlan.Links, null, null, null);
            // Step 3: the objects, on the hierarchy step 2 leaves.
            state.Step2Model = new PlannerSceneModel(state.Step2);
            PlanObjects(state, plan, state.Step2Model, notes);
            PhasingNameFixer.AddTextNotes(plan.Notes, notes, state.Fixes);

            foreach (var layer in state.Layers.OrderBy(l => l, LayerOrder.Instance))
            {
                plan.Layers.Add(new PlannerCleanUpLayer
                {
                    Key = layer.Node.Key,
                    CurrentName = layer.OldName,
                    NewName = layer.NewName,
                    Id = layer.Id != null ? PlannerNaming.LayerIdText(layer.Id) : string.Empty,
                    MaxLayer = layer.MaxLayer != null ? layer.MaxLayer.Name : null,
                    HadDates = layer.HadDates,
                    HadTbc = layer.HadTbc,
                    Left = layer.Left
                });
            }
            return plan;
        }

        // -------------------------------------------------------------------------------------------------------------
        // Step 1: layers and helpers

        private static void PlanLayers(State state, PlannerSceneModel scene, PhasingNameFixer.TextFixNotes notes)
        {
            var root = scene.RootNode;
            var top = new NodeWork { Node = root, OldName = NameOf(root), Item = PhasingFixItem.TopLayer };
            var topFix = PhasingNameFixer.FixText(top.OldName, state.Fixes, top.Why, t => t.IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0, notes, top.OldName);
            top.NewName = topFix.Text;
            Unreadable(top, topFix);
            state.Top = top;

            foreach (var child in scene.ChildrenOf(root))
            {
                var match = GroupPattern.Match(NameOf(child));
                int phase;
                if (scene.LeftOut(child) || !match.Success || !PhasingNameFixer.TryReadPhaseGroup(NameOf(child), out phase))
                {
                    continue;
                }
                var digits = match.Groups[1].Value;
                var group = new NodeWork { Node = child, OldName = NameOf(child), Item = PhasingFixItem.Group, Phase = phase };
                var fix = PhasingNameFixer.FixText(match.Groups[2].Value, state.Fixes, group.Why, d => ReadsAsGroup(digits, d), notes, group.OldName);
                group.NewName = "_" + digits + "_" + fix.Text;
                Unreadable(group, fix);
                state.Groups.Add(group);
            }
            var groupKeys = new HashSet<long>(state.Groups.Select(g => g.Node.Key));

            // The layer nodes: every helper under the root (and a spline something exported is linked under, an empty node in
            // the export) whose name reads as a layer, depth first in the scene's order, as the Planner's layer node index
            // takes any empty node with a two- or three-number ID. Where the convention puts a layer (directly under the root
            // or a phase group, or a helper with a Max layer of its own) the old names' forms are read too; deeper down, inside
            // a layer, a name is a layer when it has a phase and a stage (NestedLayer: a sub-stage written the old way,
            // "02_04-2_Roads" under "02_04_Roads", is one), but a 3ds Max group led by one number or a dimension
            // ("3 Bay Gate", "2.4_High_Hoarding") is passed over, as the Planner passes it over, and its objects belong to the
            // layer above.
            foreach (var node in DepthFirst(scene, root))
            {
                if (node.Key == root.Key || groupKeys.Contains(node.Key) || scene.LeftOut(node) || !(node.IsHelper || node.IsShape))
                {
                    continue;
                }
                var parent = scene.ParentOf(node);
                var unit = scene.Units.FirstOrDefault(u => u.Helper != null && u.Helper.Key == node.Key);
                var inPlace = (unit != null && unit.Layer != null) || (parent != null && (parent.Key == root.Key || groupKeys.Contains(parent.Key)));
                if (!inPlace && !NestedLayer(node, scene))
                {
                    continue;
                }
                var layer = ReadLayer(node, NameOf(node), OldObjectsOf(node, scene), state, notes);
                if (layer == null)
                {
                    continue;
                }
                layer.Order = state.Layers.Count;
                layer.ParentKey = parent != null ? parent.Key : (long?)null;
                layer.Group = parent != null ? state.Groups.FirstOrDefault(g => g.Node.Key == parent.Key) : null;
                layer.UnderRoot = parent != null && parent.Key == root.Key;
                layer.Unit = unit;
                layer.MaxLayer = layer.Unit != null ? layer.Unit.Layer : null;
                state.Layers.Add(layer);
            }

            // A cleaned name another node has (the cleaned name of a layer node named otherwise today, or the name of any other
            // node) is not used: that layer is left as it is, and listed. Layer nodes named alike today stay alike.
            var layerKeys = new HashSet<long>(state.Layers.Select(l => l.Node.Key));
            var others = new HashSet<string>(scene.Nodes.Where(n => !layerKeys.Contains(n.Key) && n.Key != root.Key && !groupKeys.Contains(n.Key)).Select(NameOf), StringComparer.Ordinal);
            var byNewName = new Dictionary<string, LayerWork>(StringComparer.Ordinal);
            // The layers that keep their names first: a cleaned name never takes one of theirs.
            foreach (var layer in state.Layers.Where(l => !l.Left).OrderBy(l => l.NewName == l.OldName ? 0 : 1))
            {
                LayerWork first;
                if (layer.NewName != layer.OldName && others.Contains(layer.NewName))
                {
                    Leave(layer, "the cleaned name " + layer.NewName + " is already the name of another node; left as it is - rename it by hand");
                }
                else if (byNewName.TryGetValue(layer.NewName, out first) && first.OldName != layer.OldName)
                {
                    Leave(layer, "the cleaned name " + layer.NewName + (first.NewName == first.OldName ? " is already the name of another layer node" : " is also the cleaned name of " + first.OldName)
                        + "; left as it is - rename one of them by hand");
                }
                else if (!byNewName.ContainsKey(layer.NewName))
                {
                    byNewName[layer.NewName] = layer;
                }
            }

            // The Max layers go with their nodes; a layer whose Max layer cannot take its new name is left as it is.
            PlanMaxLayers(state, scene, notes);

            // A layer directly under the root goes into the group of its phase, as Fix phasing names did, its Max layer too.
            foreach (var layer in state.Layers.Where(l => !l.Left && l.UnderRoot))
            {
                var homes = state.Groups.Where(g => g.Phase == layer.Id.Phase.Value).ToList();
                if (homes.Count != 1)
                {
                    var phase = layer.Id.Phase.Value;
                    layer.Checks.Add(homes.Count == 0
                        ? "a layer of phase " + phase.ToString(CultureInfo.InvariantCulture) + ", but there is no group _" + Pad2(phase) + "_<name> to move it into; it stays under " + top.NewName
                        : "a layer of phase " + phase.ToString(CultureInfo.InvariantCulture) + ", and " + homes.Count.ToString(CultureInfo.InvariantCulture) + " groups have that phase ("
                          + string.Join(", ", homes.Select(g => g.NewName).ToArray()) + "); it stays under " + top.NewName + " - move it by hand");
                    continue;
                }
                layer.Group = homes[0];
                layer.Moved = true;
                layer.Why.Add("moved into " + homes[0].NewName + ": it is a layer of phase " + homes[0].Phase.ToString(CultureInfo.InvariantCulture));
                state.NodeMoves.Add(new PhasingNodeMove
                {
                    Key = layer.Node.Key,
                    Name = layer.OldName,
                    FromParentKey = root.Key,
                    ToParentKey = homes[0].Node.Key,
                    ToParentName = homes[0].OldName,
                    IntoGroupHead = homes[0].Node.IsGroupHead,
                    IsGroupMember = layer.Node.IsGroupMember
                });
                PhasingMaxLayer parent;
                if (layer.MaxLayer != null && scene.LayerByName.TryGetValue(homes[0].OldName, out parent)
                    && !string.Equals(layer.MaxLayer.ParentName, parent.Name, StringComparison.OrdinalIgnoreCase))
                {
                    state.MaxLayerMoves.Add(new PhasingMaxLayerMove { Name = layer.MaxLayer.Name, NewParentName = parent.Name });
                }
            }

            // Layer nodes sharing an ID are one layer to the Planner (D2): listed, not renumbered.
            foreach (var shared in state.Layers.Where(l => !l.Left).GroupBy(l => PlannerNaming.LayerIdKey(l.Id)).Where(g => g.Select(l => l.NewName).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                var all = shared.ToList();
                foreach (var layer in all)
                {
                    var rest = all.Where(l => l != layer).Select(l => l.NewName).ToArray();
                    layer.Checks.Add("shares its layer ID " + PlannerNaming.LayerIdText(layer.Id) + " with " + string.Join(", ", rest)
                        + ": the Planner reads them as one layer - give one of them another ID by hand");
                }
            }
        }

        /// <summary>
        /// Reads a layer node's (or a Max layer's) name and plans its cleaned name, or returns null when the name does not read
        /// as a layer at all. <paramref name="objects"/> are its objects as the old names read them, for a one-number layer.
        /// </summary>
        private static LayerWork ReadLayer(PlannerSceneNode node, string name, List<PlannerMeshName> objects, State state, PhasingNameFixer.TextFixNotes notes)
        {
            var layer = new LayerWork { Node = node, OldName = name, NewName = name, Item = PhasingFixItem.Layer };
            string rest;
            bool idReason = false;
            var read = PhasingNameFixer.ReadLayerName(name, objects);
            if (read.Left != PhasingNameFixer.LayerLeft.None)
            {
                return Leave(layer, PhasingNameFixer.LeftText(read, name));
            }
            if (read.Id != null)
            {
                layer.Id = new PlannerLayerIdKey(read.Id.Phase, read.Id.Stage, read.Id.Split);
                if (read.One != null)
                {
                    rest = read.One.Rest;
                    layer.Why.Add("phase " + read.Id.Phase.ToString(CultureInfo.InvariantCulture) + " added in front, from its objects' tags: an ID of one number cannot carry a sub-stage");
                    idReason = true;
                }
                else
                {
                    rest = read.Match.Groups["rest"].Value;
                }
                if (read.Id.SplitForm != PlannerLegacyForm.None)
                {
                    layer.Why.Add(PhasingNameFixer.SubStageReason(read.Id.SplitForm));
                    idReason = true;
                }
            }
            else
            {
                var v2 = PlannerNaming.ParseLayerNameV2(name);
                var spaced = SpacedLayer.Match(name);
                var old = PlannerNaming.ParseLayerName(name);
                if (v2 != null)
                {
                    layer.Id = v2.Id;
                    rest = v2.Name;
                }
                else if (spaced.Success && old != null && old.Phase.HasValue && IdOf(spaced.Groups[1].Value, spaced.Groups[2].Value, spaced.Groups[3]) != null
                    && PlannerNaming.LayerIdKey(IdOf(spaced.Groups[1].Value, spaced.Groups[2].Value, spaced.Groups[3])) == PlannerNaming.LayerIdKey(new PlannerLayerIdKey(old.Phase, old.Stage, old.SubStage)))
                {
                    // A space where the underscore after the ID belongs, as the old names read it ("02_07 Duct Works_19-10-26").
                    layer.Id = IdOf(spaced.Groups[1].Value, spaced.Groups[2].Value, spaced.Groups[3]);
                    rest = spaced.Groups[4].Value;
                }
                else
                {
                    // One number, as the Planner reads a stage in the phase its objects carry ("05_Retainment"): a layer ID on
                    // the new names needs two or three numbers, so the phase goes in front when its objects agree on one.
                    var one = OneNumberLayer.Match(name);
                    if (!one.Success || old == null || old.Phase.HasValue)
                    {
                        return null;
                    }
                    int stage;
                    if (one.Groups[1].Length > 2 || !TryNumber(one.Groups[1].Value, out stage))
                    {
                        return Leave(layer, "the convention writes each number of a layer ID with one or two digits (" + one.Groups[1].Value + "); left as it is - rename it by hand");
                    }
                    List<int> phases;
                    var phase = PhasingNameFixer.OneNumberPhase(stage, null, objects, out phases);
                    if (!phase.HasValue || phase.Value > 99)
                    {
                        return Leave(layer, "one number: a layer ID needs the phase in front, <phase>_" + Pad2(stage) + "_<name>, but "
                            + (phases.Count == 0
                                ? "none of the objects directly in it has a tag of stage " + Pad2(stage) + " to give it"
                                : "its objects' tags of that stage carry phases " + string.Join(", ", phases.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray()))
                            + "; left as it is - rename it by hand");
                    }
                    layer.Id = new PlannerLayerIdKey(phase.Value, stage, null);
                    rest = one.Groups[2].Value;
                    layer.Why.Add("phase " + phase.Value.ToString(CultureInfo.InvariantCulture) + " added in front, from its objects' tags: a layer ID needs two or three numbers");
                    idReason = true;
                }
            }

            int dates;
            bool tbc;
            var stripped = StripDatesAndTbc(rest, out dates, out tbc);
            layer.HadDates = dates > 0;
            layer.HadTbc = tbc;
            var idText = PlannerNaming.LayerIdText(layer.Id);
            var key = PlannerNaming.LayerIdKey(layer.Id);
            var reasons = new List<string>();
            var fix = PhasingNameFixer.FixText(stripped, state.Fixes, reasons, d => ReadsAsLayer(idText, EdgeSeparators.Replace(d, string.Empty), key), notes, name);
            layer.Part = EdgeSeparators.Replace(fix.Text, string.Empty);
            layer.NewName = layer.Part.Length > 0 ? idText + "_" + layer.Part : idText;
            if (dates > 0 || tbc)
            {
                layer.Why.Insert(0, (dates > 0 ? (dates == 1 ? "date" : "dates") + (tbc ? " and TBC" : string.Empty) : "TBC") + " taken out of the name");
            }
            // The ID as the name writes it, when no reason above says already why it changes (a phase put in front, the
            // character before the sub-stage).
            var written = name.EndsWith(rest, StringComparison.Ordinal) ? EdgeSeparators.Replace(name.Substring(0, name.Length - rest.Length), string.Empty) : idText;
            if (written != idText && !idReason)
            {
                layer.Why.Add("ID written the current way");
            }
            layer.Why.AddRange(reasons);
            if (fix.Unreadable.Count > 0)
            {
                layer.Checks.Add("the " + PhasingNameFixer.FixNames(fix.Unreadable) + " would leave the name unreadable, so " + (fix.Unreadable.Count == 1 ? "it is" : "they are") + " not used on it");
            }

            // The cleaned name must read back, on the new names, as the ID it was given and nothing else.
            var back = PlannerNaming.ParseLayerNameV2(layer.NewName);
            if (back == null || PlannerNaming.LayerIdKey(back.Id) != key || back.Issues.Contains(PlannerNamingCode.Space) || back.Issues.Contains(PlannerNamingCode.Dates))
            {
                var wanted = layer.NewName;
                layer.NewName = name;
                return Leave(layer, "the cleaned name " + wanted + " would not read back as layer " + idText
                    + (back == null ? string.Empty : back.Issues.Contains(PlannerNamingCode.Dates) ? " without dates" : back.Issues.Contains(PlannerNamingCode.Space) ? " without spaces" : " (it reads as " + PlannerNaming.LayerIdText(back.Id) + ")")
                    + "; left as it is - rename it by hand");
            }
            if (back.Issues.Contains(PlannerNamingCode.NoName))
            {
                layer.Why.Add("nothing is left after the layer ID, so the Planner names its folder by the ID alone");
            }
            return layer;
        }

        // Leaves a layer as it is, listed with why.
        private static LayerWork Leave(LayerWork layer, string why)
        {
            layer.Left = true;
            layer.LeftWhy = why;
            layer.NewName = layer.OldName;
            return layer;
        }

        /// <summary>
        /// The text after a layer ID with its dates and the TBC word taken out, as the Planner reads them (any DD-MM-YY token,
        /// and TBC as a word of its own, in any capitals), each with the underscore or space before it.
        /// </summary>
        internal static string StripDatesAndTbc(string text, out int dates, out bool tbc)
        {
            int found = 0;
            var result = DateToken.Replace(text ?? string.Empty, m =>
            {
                found++;
                var before = m.Groups[1].Value;
                return before == "_" || before.Trim(PlannerNaming.JavaScriptSpaceChars).Length == 0 ? string.Empty : before;
            });
            dates = found;
            bool word = false;
            result = TbcToken.Replace(result, m =>
            {
                word = true;
                return string.Empty;
            });
            tbc = word;
            return result;
        }

        // A node inside a layer that is a layer itself: a name the new names read as one, or one the old names read with a
        // phase and a stage, its sub-stage perhaps written the old way, with a hyphen or a full stop ("02_04-2_Roads_02-02-26"
        // under "02_04_Roads_01-02-26"), or a space after its ID; but not a 3ds Max group of objects so named, and not a
        // dimension in the full stop's place ("01_1.8_x_2.4_Panel"). A name led by one number ("3 Bay Gate") is never one here.
        private static bool NestedLayer(PlannerSceneNode node, PlannerSceneModel scene)
        {
            var name = NameOf(node);
            if (PlannerNaming.ParseLayerNameV2(name) != null)
            {
                return true;
            }
            var old = PlannerNaming.ParseLayerName(name);
            if (old == null || !old.Phase.HasValue || scene.IsObjectGroup(node))
            {
                return false;
            }
            return old.LegacyForm != PlannerLegacyForm.Dot || SubStageFullStop.IsMatch(name.Trim(PlannerNaming.JavaScriptSpaceChars));
        }

        // A phase group's description that still reads as the group of those digits.
        private static bool ReadsAsGroup(string digits, string description)
        {
            var match = GroupPattern.Match("_" + digits + "_" + description);
            return match.Success && match.Groups[1].Value == digits && match.Groups[2].Value == description;
        }

        // An ID and a name part that read back, on the new names, as that ID with that name.
        private static bool ReadsAsLayer(string idText, string part, string key)
        {
            var back = PlannerNaming.ParseLayerNameV2(part.Length > 0 ? idText + "_" + part : idText);
            return back != null && PlannerNaming.LayerIdKey(back.Id) == key && back.Name == part;
        }

        // The 3ds Max layers renamed with their nodes (step 1): the layer of each helper (and of the root and the groups),
        // and any layer in the phasing tree without its helper whose name reads as a layer. Max layer names must stay unique,
        // in any capitals: a rename whose new name another Max layer keeps is dropped, and its node is left as it is too.
        private static void PlanMaxLayers(State state, PlannerSceneModel scene, PhasingNameFixer.TextFixNotes notes)
        {
            var wanted = new List<MaxRename>();
            var top = state.Top;
            if (top.NewName != top.OldName && scene.RootLayer != null && string.Equals(scene.RootLayer.Name, top.OldName, StringComparison.OrdinalIgnoreCase))
            {
                wanted.Add(new MaxRename { Layer = scene.RootLayer, NewName = top.NewName, Item = PhasingFixItem.TopLayer, Work = top });
            }
            foreach (var group in state.Groups.Where(g => g.NewName != g.OldName))
            {
                PhasingMaxLayer maxLayer;
                if (scene.LayerByName.TryGetValue(group.OldName, out maxLayer) && scene.TreeLayers.Contains(maxLayer.Name))
                {
                    wanted.Add(new MaxRename { Layer = maxLayer, NewName = group.NewName, Item = PhasingFixItem.Group, Work = group });
                }
            }
            foreach (var layer in state.Layers.Where(l => !l.Left && l.MaxLayer != null))
            {
                if (layer.MaxLayer.Name != layer.NewName)
                {
                    wanted.Add(new MaxRename { Layer = layer.MaxLayer, NewName = layer.NewName, Item = PhasingFixItem.Layer, Work = layer });
                }
            }
            // A Max layer in the phasing tree whose helper is missing: its own name is cleaned the same way.
            var helperLayers = new HashSet<string>(state.Layers.Where(l => l.MaxLayer != null).Select(l => l.MaxLayer.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var unit in scene.Units.Where(u => u.Layer != null && u.Helper == null && scene.TreeLayers.Contains(u.Layer.Name) && !helperLayers.Contains(u.Layer.Name)))
            {
                var onLayer = scene.Nodes.Where(n => string.Equals(n.LayerName, unit.Layer.Name, StringComparison.OrdinalIgnoreCase) && !n.IsHelper)
                    .Select(n => PlannerNaming.ParseMeshName(NameOf(n))).Where(p => p != null).ToList();
                var alone = ReadLayer(null, unit.Layer.Name, onLayer, state, notes);
                if (alone == null)
                {
                    continue;
                }
                alone.MaxLayer = unit.Layer;
                alone.Item = PhasingFixItem.Layer;
                state.MaxOnly.Add(alone);
                if (!alone.Left && alone.NewName != unit.Layer.Name)
                {
                    wanted.Add(new MaxRename { Layer = unit.Layer, NewName = alone.NewName, Item = PhasingFixItem.Layer, Work = alone });
                }
            }

            var dropped = new HashSet<MaxRename>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                var renaming = wanted.Where(w => !dropped.Contains(w)).ToList();
                var renamedAway = new HashSet<string>(renaming.Select(w => w.Layer.Name), StringComparer.OrdinalIgnoreCase);
                var taken = new HashSet<string>(scene.Layers.Where(l => !renamedAway.Contains(l.Name)).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var rename in renaming)
                {
                    if (!taken.Add(rename.NewName))
                    {
                        dropped.Add(rename);
                        var why = "the Max layer " + rename.Layer.Name + " cannot become " + rename.NewName + ": another Max layer already has that name; left as it is - rename it by hand";
                        var work = rename.Work;
                        var asLayer = work as LayerWork;
                        if (asLayer != null)
                        {
                            Leave(asLayer, why);
                        }
                        else
                        {
                            work.NewName = work.OldName;
                            work.Checks.Add(why);
                        }
                        changed = true;
                        break;
                    }
                }
            }
            foreach (var rename in wanted.Where(w => !dropped.Contains(w)))
            {
                state.MaxLayerRenames.Add(new PhasingMaxLayerRename { OldName = rename.Layer.Name, NewName = rename.NewName, Item = rename.Item });
                rename.Work.MaxLayerRenamed = true;
            }
        }

        // A word fix that would stop a root or group name reading is not used on it, and the name is listed.
        private static void Unreadable(NodeWork work, PhasingNameFixer.TextFix fix)
        {
            if (fix.Unreadable.Count > 0)
            {
                work.Checks.Add("the " + PhasingNameFixer.FixNames(fix.Unreadable) + " would leave the name unreadable, so " + (fix.Unreadable.Count == 1 ? "it is" : "they are") + " not used on it");
            }
        }

        // The tagged objects directly in a node that the export keeps as geometry, as the old names read them (as Fix phasing
        // names reads a layer's objects).
        private static List<PlannerMeshName> OldObjectsOf(PlannerSceneNode node, PlannerSceneModel scene)
        {
            return scene.ChildrenOf(node).Where(c => !c.IsHelper && !c.IsShape).Select(c => PlannerNaming.ParseMeshName(NameOf(c))).Where(p => p != null).ToList();
        }

        // -------------------------------------------------------------------------------------------------------------
        // Step 3: objects

        private static void PlanObjects(State state, PlannerCleanUpPlan plan, PlannerSceneModel scene, PhasingNameFixer.TextFixNotes notes)
        {
            PlannerSceneNode root;
            if (!scene.NodeByKey.TryGetValue(state.Top.Node.Key, out root))
            {
                return;
            }
            var layers = state.Layers.ToDictionary(l => l.Node.Key);
            var groupKeys = new HashSet<long>(state.Groups.Select(g => g.Node.Key));
            state.LayerIds.AddRange(state.Layers.Where(l => !l.Left && l.NotALayerWhy == null).OrderBy(l => l, LayerOrder.Instance).Select(l => PlannerNaming.LayerIdText(l.Id)).Distinct(StringComparer.Ordinal));
            var byKey = new Dictionary<long, ObjectWork>();
            foreach (var node in DepthFirst(scene, root))
            {
                if (node.Key == root.Key || groupKeys.Contains(node.Key) || layers.ContainsKey(node.Key) || scene.LeftOut(node))
                {
                    continue;
                }
                var name = NameOf(node);
                // Phase groups, and empty nodes led by a number that are no layer, are never objects.
                if (PlannerSchedule.ParsePhaseGroup(name) != null || ((node.IsHelper || node.IsShape) && NumberLed.IsMatch(name.Trim(PlannerNaming.JavaScriptSpaceChars))))
                {
                    continue;
                }
                // A split piece (<Name>_N under <Name>) folds into its object, and is renamed with it.
                var owner = PieceOwner(node, scene);
                if (owner != null)
                {
                    ObjectWork of;
                    if (byKey.TryGetValue(owner.Key, out of))
                    {
                        of.Pieces.Add(node);
                    }
                    continue;
                }
                var work = new ObjectWork { Node = node, OldName = name, Order = state.Objects.Count };
                for (var p = scene.ParentOf(node); p != null && p.Key != root.Key; p = scene.ParentOf(p))
                {
                    LayerWork layer;
                    if (layers.TryGetValue(p.Key, out layer))
                    {
                        work.Layer = layer;
                        break;
                    }
                }
                state.Objects.Add(work);
                byKey[node.Key] = work;
                Classify(work, state, notes);
                if (work.Kind != ObjectKind.Untagged && work.Kind != ObjectKind.Check)
                {
                    var hidden = HiddenBy(node, scene, root, layers, groupKeys);
                    if (hidden != null)
                    {
                        NotALayer blocking;
                        if (!state.NotLayers.TryGetValue(hidden.Key, out blocking))
                        {
                            blocking = new NotALayer { Node = hidden, Layer = work.Layer };
                            state.NotLayers[hidden.Key] = blocking;
                        }
                        blocking.Objects++;
                    }
                }
            }
            // The questions in layer order, then by name.
            var asked = PlannerNaturalOrder.StableSort(state.Questions, (a, b) =>
            {
                int c = PlannerNaming.CompareLayerIds(a.Work.Layer.Id, b.Work.Layer.Id);
                return c != 0 ? c : PlannerNaturalOrder.Compare(a.Work.OldName, b.Work.OldName);
            });
            plan.Questions.AddRange(asked.Select(q => q.Public));
        }

        // The node led by a number, inside a layer, that keeps the Planner from scheduling a tagged object under it, or null:
        // the nearest node above the object that the old names read as a layer ("3 Bay Gate", "2.4_High_Hoarding"), when it
        // is no layer on the new names. The Planner's Generate leaves such an object in no layer rather than file it in the
        // layer above (readModelV2), so the group is listed for the modeller to rename.
        private static PlannerSceneNode HiddenBy(PlannerSceneNode node, PlannerSceneModel scene, PlannerSceneNode root, Dictionary<long, LayerWork> layers, HashSet<long> groupKeys)
        {
            for (var p = scene.ParentOf(node); p != null && p.Key != root.Key && !layers.ContainsKey(p.Key) && !groupKeys.Contains(p.Key); p = scene.ParentOf(p))
            {
                var name = NameOf(p);
                if (PlannerNaming.ParseLayerName(name) != null)
                {
                    return NumberLed.IsMatch(name.Trim(PlannerNaming.JavaScriptSpaceChars)) && PlannerNaming.ParseLayerNameV2(name) == null ? p : null;
                }
            }
            return null;
        }

        // The object a split piece folds into (the Planner's mergedName: its name is an ancestor's, with _N or _primitiveN
        // after it, again and again), or null when the node is no piece.
        private static PlannerSceneNode PieceOwner(PlannerSceneNode node, PlannerSceneModel scene)
        {
            var current = NameOf(node);
            PlannerSceneNode owner = null;
            while (PlannerSchedule.SplitSuffix.IsMatch(current))
            {
                var stripped = PlannerSchedule.SplitSuffix.Replace(current, string.Empty, 1);
                PlannerSceneNode found = null;
                var seen = new HashSet<long>();
                for (var p = scene.ParentOf(node); p != null && seen.Add(p.Key); p = scene.ParentOf(p))
                {
                    if (NameOf(p) == stripped)
                    {
                        found = p;
                        break;
                    }
                }
                if (stripped.Length == 0 || found == null)
                {
                    break;
                }
                current = stripped;
                owner = found;
            }
            return owner;
        }

        // What an object is and how it converts: an old Ph/St name, a new name on a new-name scene (text fixes only), a new
        // name on an old-name scene (listed, unchanged), untagged (left alone), or one Clean up cannot read (listed).
        private static void Classify(ObjectWork work, State state, PhasingNameFixer.TextFixNotes notes)
        {
            var name = work.OldName;
            var mesh = PlannerNaming.ParseMeshName(name);
            bool oldKind = PlannerNaming.IsOldKindName(name);
            var newLead = TypeWordFirst.Match(name);
            var trail = OldTrail.Match(name);
            bool mixed = mesh == null && oldKind && newLead.Success && trail.Success && trail.Index > newLead.Length
                && !PlannerNaming.IsOldKindName(name.Substring(0, trail.Index));
            var layer = work.Layer;
            if (!oldKind && PlannerNaming.ParseObjectNameV2(name).Kind != PlannerNamingKind.New)
            {
                work.Kind = ObjectKind.Untagged;
                return;
            }
            work.OldKind = oldKind;
            if (layer == null)
            {
                Check(work, "tagged, but under no layer's helper, and Link objects to their helpers did not place it (its Max layer is not a phasing layer"
                    + " with a helper); not converted - link it under its layer's helper, then run Clean up again");
                return;
            }
            if (layer.Left)
            {
                Check(work, "its layer " + layer.OldName + " is left as it is (see the layer's own row), so it is not converted yet");
                return;
            }
            if (layer.NotALayerWhy != null)
            {
                Check(work, layer.NotALayerWhy + ", so it is not converted - check it by hand");
                return;
            }
            if (mesh != null || mixed)
            {
                ConvertOld(work, mesh, newLead, trail, state, notes);
                return;
            }
            if (oldKind)
            {
                Check(work, "holds a Ph/St tag where the old names never read one, so what it meant is not known; not converted - rename it by hand");
                return;
            }
            // Read as the scene names it now: a reference names a layer by the ID it has now.
            var p = PlannerNaming.ParseObjectNameV2(name, layer.SceneId);
            // A reference to an ID no layer of the scene had, which the Planner's list now gives a layer that had another: it
            // was never written for that layer, so it is listed, not rewritten (spec 5.3).
            var renumbered = p.Ref != null ? state.RenumberedOnto(p.Ref) : null;
            if (state.OldScene)
            {
                work.Kind = ObjectKind.NewListed;
                work.Base = PartsOf(name, p, layer, state);
                Check(work, "has no Ph/St tag, but the new names read it as " + EventsText(p, layer)
                    + (renumbered != null ? "; " + RenumberedText(renumbered, p.Ref) + ", so its reference would name that layer, which it was not written for" : string.Empty)
                    + "; left as it is - it will be scheduled so", ObjectKind.NewListed);
                return;
            }
            if (renumbered != null)
            {
                work.Base = PartsOf(name, p, layer, state);
                Check(work, "its trailing tag names layer " + PlannerNaming.LayerIdText(p.Ref) + ", which no layer of the scene has; " + RenumberedText(renumbered, p.Ref)
                    + ", so the reference would name that layer, which it was not written for; left as it is - check which layer it means (Add to IN / RM on the Schedule tab sets it)",
                    ObjectKind.NewListed);
                return;
            }
            FixNewName(work, p, state, notes);
        }

        // An old Ph/St name (or a new leading tag with an old trailing tag), converted by the rules of spec 6.1.
        private static void ConvertOld(ObjectWork work, PlannerMeshName mesh, Match newLead, Match trail, State state, PhasingNameFixer.TextFixNotes notes)
        {
            var name = work.OldName;
            var layer = work.Layer;
            var why = work.Why;
            Tag lead = null;
            PlannerTaskType leadType;
            string description;
            if (mesh != null)
            {
                lead = TagOf(mesh.Lead);
                leadType = lead.Type;
                description = mesh.Description;
            }
            else
            {
                leadType = TypeOf(newLead.Groups[1].Value);
                description = name.Substring(newLead.Length, trail.Index - newLead.Length);
            }
            var trailTag = trail.Success ? TagOf(trail) : null;
            var note = trail.Success ? name.Substring(trail.Index + trail.Length) : string.Empty;
            var parts = new Parts { Lead = leadType, Home = layer, Note = note };
            var layerId = PlannerNaming.LayerIdText(layer.Id);

            // The leading tag. Old tags name a layer by the ID its name gives now (the list may give it another).
            bool leadOwn = lead == null || IsOwnOf(layer.SceneId, lead);
            if (lead != null && leadOwn)
            {
                why.Add("leading tag " + lead.Text + " names its own layer " + layerId + ", so it becomes " + Word(leadType) + "_");
            }

            // A closing type word with no Ph/St (and perhaps a layer ID after it): what it means is asked.
            var own = description;
            Match closing = Match.Empty;
            if (trailTag == null)
            {
                closing = ClosingWord.Match(own);
                if (closing.Success && closing.Index > 0)
                {
                    own = own.Substring(0, closing.Index);
                }
                else
                {
                    closing = Match.Empty;
                }
            }
            own = FixOwn(own, state, why, notes, name);
            if (own.Length == 0)
            {
                Check(work, "nothing is left between its tags, so it is no object on the new names; not converted - rename it by hand");
                return;
            }
            parts.Own = own;
            if (trailTag != null && trailTag.Type == leadType)
            {
                Check(work, "the same type at both ends (" + (lead != null ? lead.Text : Word(leadType) + "_") + " and " + trailTag.Text + "): an object is installed once and removed once on the new names (D8); not converted - rename it by hand, and keep the second event as a Planner task");
                return;
            }

            var questions = new List<QuestionWork>();
            // The trailing tag.
            if (trailTag != null)
            {
                parts.Trail = trailTag.Type;
                if (IsOwnOf(layer.SceneId, trailTag))
                {
                    parts.TrailLayer = layer.Id;
                    why.Add("trailing tag " + trailTag.Text + " names its own layer, so it becomes a plain _" + Word(trailTag.Type) + " (at the end of the layer)");
                }
                else
                {
                    var target = ResolveTag(state, trailTag);
                    if (target.Layer != null)
                    {
                        parts.TrailLayer = target.Layer.Id;
                        var to = PlannerNaming.LayerIdText(target.Layer.Id);
                        why.Add("trailing tag " + trailTag.Text + " names layer " + to + ", so it becomes _" + Word(trailTag.Type) + "_" + to
                            + (target.How != null ? " (" + target.How + ")" : string.Empty));
                    }
                    else if (target.Several.Count > 0)
                    {
                        questions.Add(SeveralLayersQuestion(work, trailTag, target.Several));
                    }
                    else
                    {
                        var written = WritableId(trailTag);
                        if (written != null)
                        {
                            parts.TrailLayer = written;
                        }
                        questions.Add(NoLayerQuestion(work, trailTag, written, state.RenumberedOnto(written)));
                    }
                }
            }

            // The leading tag naming another layer.
            if (!leadOwn)
            {
                questions.Insert(0, StrayLeadQuestion(work, lead, state, trailTag == null && !closing.Success));
            }
            if (closing.Success)
            {
                questions.Add(ClosingQuestion(work, closing, state));
            }
            if (TypeWordFirst.IsMatch(own))
            {
                questions.Add(DoubledQuestion(work, own, leadType));
            }
            work.Kind = ObjectKind.Convert;
            work.Base = parts;
            Finish(work, questions, state);
        }

        // A new name on a new-name scene: only its own name's text is fixed (hyphens, word fixes, underscores at the ends).
        // A name that reads with an error is left as it is: recomposing it could drop a word the reader passed over.
        private static void FixNewName(ObjectWork work, PlannerObjectNameV2 p, State state, PhasingNameFixer.TextFixNotes notes)
        {
            var name = work.OldName;
            var parts = PartsOf(name, p, work.Layer, state);
            work.Base = parts;
            work.Kind = ObjectKind.NewUnchanged;
            if (parts == null || p.Issues.Any(IsError))
            {
                return;
            }
            var why = new List<string>();
            var own = FixOwn(p.Own, state, why, notes, name);
            // A reference to a layer whose ID the Planner's list changes follows it (spec 5.3).
            var follows = p.Ref != null && PlannerNaming.LayerIdKey(parts.TrailLayer) != PlannerNaming.LayerIdKey(p.Ref);
            if ((own == p.Own && !p.Issues.Contains(PlannerNamingCode.Trimmed) && !follows) || own.Length == 0)
            {
                return;
            }
            if (p.Issues.Contains(PlannerNamingCode.Trimmed) && own == p.Own)
            {
                why.Add("underscores beside the tags taken out");
            }
            if (follows)
            {
                why.Add("its trailing tag names layer " + PlannerNaming.LayerIdText(p.Ref) + ", whose ID the Planner's list changes to " + PlannerNaming.LayerIdText(parts.TrailLayer));
            }
            var fixedParts = parts.Copy();
            fixedParts.Own = own;
            if (!ReadsBack(Compose(fixedParts), fixedParts))
            {
                return;
            }
            work.Why.AddRange(why);
            work.Kind = ObjectKind.Convert;
            work.Base = fixedParts;
        }

        // The parts of a new name as read, for recomposing it; null when it cannot be split so (it starts with a space).
        private static Parts PartsOf(string name, PlannerObjectNameV2 p, LayerWork layer, State state)
        {
            int space = name.IndexOfAny(PlannerNaming.JavaScriptSpaceChars);
            if (space == 0)
            {
                return null;
            }
            return new Parts
            {
                Lead = p.Lead,
                Own = p.Own,
                Trail = p.Trail,
                // A plain trailing tag (or one naming its own layer) is the end of its own layer; a reference names the layer
                // by the ID it ends with.
                TrailLayer = p.Trail.HasValue ? state.FinalId(p.Ref) : null,
                Note = space < 0 ? string.Empty : name.Substring(space),
                Home = layer
            };
        }

        // The own name's text: the word fixes and spaces (as Fix phasing names corrects them), hyphens as underscores (Unreal's
        // Datasmith import turns each into one), and no underscores at either end.
        private static string FixOwn(string own, State state, List<string> why, PhasingNameFixer.TextFixNotes notes, string name)
        {
            var fix = PhasingNameFixer.FixText(own, state.Fixes, why, null, notes, name);
            var text = fix.Text;
            if (text.IndexOf('-') >= 0)
            {
                text = text.Replace('-', '_');
                why.Add(PhasingNameFixer.DatasmithReason);
            }
            var trimmed = EdgeUnderscores.Replace(text, string.Empty);
            if (trimmed != text && trimmed.Length > 0)
            {
                bool start = text.StartsWith("_", StringComparison.Ordinal), end = text.EndsWith("_", StringComparison.Ordinal);
                why.Add((start && end ? "underscores at both ends" : (text.Length - trimmed.Length > 1 ? "underscores" : "underscore") + (start ? " at the start" : " at the end"))
                    + " of the own name taken out");
            }
            return trimmed;
        }

        // The questions' options are kept where they read back; the object's rule conversion must read back too. An object
        // can ask two or three things (a doubled type word and a closing tag, say): an option is kept when it reads back with
        // some answer to each other question, or with none, as Resolve applies them (in the questions' order), and its name
        // is the first such: with no other answer when that reads back, else with the first answers that make it.
        private static void Finish(ObjectWork work, List<QuestionWork> questions, State state)
        {
            foreach (var q in questions)
            {
                var kept = new List<PlannerCleanUpOption>();
                var others = questions.Where(o => o != q).ToList();
                foreach (var option in q.Public.Options)
                {
                    if (option.NeedsLayer)
                    {
                        kept.Add(option);
                        continue;
                    }
                    var name = NameWithOthers(work, questions, q, option.Id, others, 0, new Dictionary<QuestionWork, string>(), state);
                    if (name != null)
                    {
                        option.Name = name;
                        kept.Add(option);
                    }
                }
                q.Public.Options.Clear();
                q.Public.Options.AddRange(kept);
                if (q.Public.DefaultChoice != null && q.Public.OptionFor(q.Public.DefaultChoice) == null)
                {
                    q.Public.DefaultChoice = null;
                }
                if (q.Public.Options.Count > 0 && q.Public.Options.Any(o => o.NeedsLayer))
                {
                    q.Public.LayerIds.AddRange(state.LayerIds);
                }
            }
            if (questions.Any(q => q.Public.Options.Count == 0))
            {
                var bad = questions.First(q => q.Public.Options.Count == 0);
                Check(work, bad.Public.Why + ", and no answer Clean up can offer reads back on the new names; not converted - rename it by hand");
                return;
            }
            if (questions.Count == 0 && !ReadsBack(Compose(work.Base), work.Base))
            {
                Check(work, "converted, it would be " + Compose(work.Base) + ", which does not read back as meant on the new names ("
                    + ReadIssues(Compose(work.Base), work.Base) + "); not converted - rename it by hand");
                return;
            }
            work.Questions.AddRange(questions);
            foreach (var q in questions)
            {
                state.Questions.Add(q);
            }
        }

        // The name an object gets with <paramref name="choice"/> to <paramref name="q"/> and, for each other question from
        // <paramref name="at"/> on, no answer or one of its own (none tried first, then its options in order), or null when no
        // such answers give a name that reads back. The answers are applied in the questions' order, as Resolve applies them.
        private static string NameWithOthers(ObjectWork work, List<QuestionWork> questions, QuestionWork q, string choice, List<QuestionWork> others, int at,
            Dictionary<QuestionWork, string> answers, State state)
        {
            if (at == others.Count)
            {
                var parts = work.Base.Copy();
                string problem;
                foreach (var each in questions)
                {
                    string answer;
                    var given = each == q ? choice : answers.TryGetValue(each, out answer) ? answer : null;
                    if (given != null && !ApplyChoice(each, given, parts, state, out problem))
                    {
                        return null;
                    }
                }
                var name = Compose(parts);
                return ReadsBack(name, parts) ? name : null;
            }
            var other = others[at];
            foreach (var answer in new string[] { null }.Concat(other.Public.Options.Where(o => !o.NeedsLayer).Select(o => o.Id)))
            {
                if (answer == null)
                {
                    answers.Remove(other);
                }
                else
                {
                    answers[other] = answer;
                }
                var name = NameWithOthers(work, questions, q, choice, others, at + 1, answers, state);
                if (name != null)
                {
                    answers.Remove(other);
                    return name;
                }
            }
            answers.Remove(other);
            return null;
        }

        // The questions

        private static QuestionWork NewQuestion(ObjectWork work, PlannerCleanUpCase kind, string why)
        {
            var q = new QuestionWork
            {
                Work = work,
                Public = new PlannerCleanUpQuestion
                {
                    Id = work.Node.Key.ToString(CultureInfo.InvariantCulture) + ":" + kind,
                    Key = work.Node.Key,
                    Case = kind,
                    CurrentName = work.OldName,
                    LayerName = work.Layer.NewName,
                    Why = why
                }
            };
            return q;
        }

        private static QuestionWork ClosingQuestion(ObjectWork work, Match closing, State state)
        {
            var type = TypeOf(closing.Groups[1].Value);
            var word = Word(type);
            var layer = PlannerNaming.LayerIdText(work.Layer.Id);
            var written = closing.Groups[2].Success ? IdOf(closing.Groups[2].Value, closing.Groups[3].Value, closing.Groups[4]) : null;
            var q = NewQuestion(work, PlannerCleanUpCase.ClosingTag, "the name closes with _" + word + (written != null ? "_" + PlannerNaming.LayerIdText(written) : string.Empty)
                + " but no Ph/St tag, which the old names never read, so when it is " + Done(type) + " is not known");
            q.Type = type;
            q.RefId = written;
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "end", Text = Capital(Done(type)) + " at the end of its own layer, " + layer });
            if (written != null)
            {
                var renumbered = state.RenumberedOnto(written);
                q.Public.Options.Add(new PlannerCleanUpOption
                {
                    Id = "as-written",
                    Text = Capital(Done(type)) + " in layer " + PlannerNaming.LayerIdText(written) + ", as the name writes it" + (renumbered != null ? " (" + RenumberedText(renumbered, written) + ")" : string.Empty)
                });
            }
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "layer", Text = Capital(Done(type)) + " in another layer: choose the layer", NeedsLayer = true });
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "drop", Text = "No " + Event(type) + ": the word is taken out" });
            return q;
        }

        private static QuestionWork StrayLeadQuestion(ObjectWork work, Tag lead, State state, bool canRefer)
        {
            var type = lead.Type;
            var own = PlannerNaming.LayerIdText(work.Layer.Id);
            var target = ResolveTag(state, lead);
            var candidates = target.Layer != null ? new List<LayerWork> { target.Layer } : target.Several;
            var names = target.Layer != null ? "layer " + PlannerNaming.LayerIdText(target.Layer.Id)
                : target.Several.Count > 0 ? "a stage several layers have (" + string.Join(", ", target.Several.Select(l => PlannerNaming.LayerIdText(l.Id)).ToArray()) + ")"
                : "a stage no layer has";
            var blocker = candidates.Count > 0 ? LinkBlocker(work, state) : null;
            var q = NewQuestion(work, PlannerCleanUpCase.StrayLead, "its leading tag " + lead.Text + " names " + names + ", but it sits under the helper of " + own
                + (blocker != null ? "; it is not offered a link under that layer's helper, because " + blocker + ": to move it there, link it by hand, then run Clean up again" : string.Empty));
            q.Type = type;
            foreach (var layer in blocker == null ? candidates : new List<LayerWork>())
            {
                q.Public.Options.Add(new PlannerCleanUpOption
                {
                    Id = "link:" + layer.Node.Key.ToString(CultureInfo.InvariantCulture),
                    Text = "Link it under " + layer.NewName + "'s helper: " + Done(type) + " while " + PlannerNaming.LayerIdText(layer.Id) + " runs"
                });
            }
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "own", Text = Capital(Done(type)) + " while its own layer " + own + " runs" });
            if (canRefer)
            {
                q.RefId = target.Layer != null ? target.Layer.Id : target.Several.Count == 0 ? WritableId(lead) : null;
                if (q.RefId != null)
                {
                    var renumbered = target.Layer == null ? state.RenumberedOnto(q.RefId) : null;
                    q.Public.Options.Add(new PlannerCleanUpOption
                    {
                        Id = "ref",
                        Text = Capital(Done(type)) + " in layer " + PlannerNaming.LayerIdText(q.RefId) + " while it stays under the helper of " + own
                            + (renumbered != null ? " (" + RenumberedText(renumbered, q.RefId) + ")"
                                : target.Layer == null ? " (no layer has that ID: the Planner parks it TBC)" : string.Empty)
                    });
                }
            }
            return q;
        }

        // Why an answer may not link an object under another layer's helper on its own, or null when it may. It must sit
        // right under its layer's helper: inside a 3ds Max group or under another object it would be taken out of its group
        // or hierarchy (Link moves those whole, by their top node). And no tagged object may sit under it: linked elsewhere,
        // that one would be read against another layer than the one its own name was converted for.
        private static string LinkBlocker(ObjectWork work, State state)
        {
            var node = work.Node;
            if (!node.ParentKey.HasValue || node.ParentKey.Value != work.Layer.Node.Key)
            {
                return node.IsGroupMember ? "it is a member of a 3ds Max group" : "it is linked under another object";
            }
            var scene = state.Step2Model;
            foreach (var below in DepthFirst(scene, node).Skip(1))
            {
                var name = NameOf(below);
                if (PieceOwner(below, scene) == null && (PlannerNaming.IsOldKindName(name) || PlannerNaming.ParseObjectNameV2(name).Kind == PlannerNamingKind.New))
                {
                    return "tagged objects sit under it (" + name + ")";
                }
            }
            return null;
        }

        // A second type word after the leading one: the same word twice ("RM_RM_"), or the other one ("IN_RM_"), which is no
        // doubled word and may have meant a removal, so the question says which.
        private static QuestionWork DoubledQuestion(ObjectWork work, string own, PlannerTaskType leadType)
        {
            var word = own.Substring(0, 2);
            var lead = Word(leadType);
            var doubled = word == lead;
            var q = NewQuestion(work, PlannerCleanUpCase.DoubledType, doubled
                ? "the type word is doubled (" + word + "_" + word + "_)"
                : "its leading " + lead + "_ is followed by a second type word, " + word + "_, which the new names read as part of its own name");
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "undouble", Text = doubled ? "Take out the second " + word + "_" : "Take out the " + word + "_ after " + lead + "_" });
            return q;
        }

        // A trailing tag naming a stage no layer has. Referred to as written by default, which the Planner parks TBC; but when
        // the Planner's list now gives that ID to a layer that had another (renumbered, spec 5.3), the tag was never written
        // for that layer: the answer says so, and there is no default, so the object keeps its name until someone answers.
        private static QuestionWork NoLayerQuestion(ObjectWork work, Tag tag, PlannerLayerIdKey written, LayerWork renumbered)
        {
            var type = tag.Type;
            var q = NewQuestion(work, PlannerCleanUpCase.NoLayer, "its trailing tag " + tag.Text + " names a stage no layer has"
                + (renumbered != null ? ", and " + RenumberedText(renumbered, written) + ", which the tag was not written for" : string.Empty));
            q.Type = type;
            q.RefId = written;
            if (written != null)
            {
                var id = PlannerNaming.LayerIdText(written);
                if (renumbered == null)
                {
                    q.Public.Options.Add(new PlannerCleanUpOption { Id = "as-written", Text = "Refer to " + id + " as the tag does: no layer has it, so the Planner parks the " + Event(type) + " TBC" });
                    q.Public.DefaultChoice = "as-written";
                }
                else
                {
                    q.Public.Options.Add(new PlannerCleanUpOption { Id = "as-written", Text = "Refer to " + id + " as the tag does: that is " + renumbered.NewName + " now, so the " + Event(type) + " is filed in it" });
                }
            }
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "end", Text = Capital(Done(type)) + " at the end of its own layer, " + PlannerNaming.LayerIdText(work.Layer.Id) });
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "layer", Text = Capital(Done(type)) + " in another layer: choose the layer", NeedsLayer = true });
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "drop", Text = "No " + Event(type) + ": the tag is taken out" });
            return q;
        }

        // "the Planner's list now gives 02_02 to 02_02_Kerb, which was 02_01": a layer the list renumbered onto an ID that no
        // layer of the scene had (State.RenumberedOnto).
        private static string RenumberedText(LayerWork layer, PlannerLayerIdKey id)
        {
            return "the Planner's list now gives " + PlannerNaming.LayerIdText(id) + " to " + layer.NewName + ", which was " + PlannerNaming.LayerIdText(layer.SceneId);
        }

        private static QuestionWork SeveralLayersQuestion(ObjectWork work, Tag tag, List<LayerWork> layers)
        {
            var type = tag.Type;
            var ids = layers.Select(l => PlannerNaming.LayerIdText(l.Id)).ToList();
            var q = NewQuestion(work, PlannerCleanUpCase.SeveralLayers, "its trailing tag " + tag.Text + " names a stage several layers have (" + string.Join(", ", ids.ToArray()) + ")");
            q.Type = type;
            foreach (var id in ids)
            {
                q.Public.Options.Add(new PlannerCleanUpOption { Id = PlannerCleanUpOption.LayerPrefix + id, Text = Capital(Done(type)) + " in layer " + id });
            }
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "end", Text = Capital(Done(type)) + " at the end of its own layer, " + PlannerNaming.LayerIdText(work.Layer.Id) });
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "layer", Text = Capital(Done(type)) + " in another layer: choose the layer", NeedsLayer = true });
            q.Public.Options.Add(new PlannerCleanUpOption { Id = "drop", Text = "No " + Event(type) + ": the tag is taken out" });
            return q;
        }

        // Applies one answer to an object's parts; false (with why) when it does not fit.
        private static bool ApplyChoice(QuestionWork q, string choice, Parts parts, State state, out string problem)
        {
            problem = null;
            PlannerLayerIdKey named = null;
            if (choice.StartsWith(PlannerCleanUpOption.LayerPrefix, StringComparison.Ordinal))
            {
                named = PlannerLayerIdKey.FromText(choice.Substring(PlannerCleanUpOption.LayerPrefix.Length));
                if (named == null || !named.Phase.HasValue)
                {
                    problem = "\"" + choice.Substring(PlannerCleanUpOption.LayerPrefix.Length) + "\" is not a layer ID of two or three numbers";
                    return false;
                }
                choice = "layer";
            }
            switch (q.Public.Case)
            {
                case PlannerCleanUpCase.ClosingTag:
                    switch (choice)
                    {
                        case "end":
                            // The end of the layer it sits in now, by ID: written plain there, and as a reference when another
                            // answer links it under another layer's helper (in either order).
                            parts.Trail = q.Type;
                            parts.TrailLayer = q.Work.Layer.Id;
                            return true;
                        case "as-written":
                            parts.Trail = q.Type;
                            parts.TrailLayer = q.RefId;
                            return q.RefId != null;
                        case "layer":
                            parts.Trail = q.Type;
                            parts.TrailLayer = named;
                            return true;
                        case "drop":
                            parts.Trail = null;
                            parts.TrailLayer = null;
                            return true;
                    }
                    break;
                case PlannerCleanUpCase.StrayLead:
                    if (choice == "own")
                    {
                        return true;
                    }
                    if (choice == "ref")
                    {
                        if (q.RefId == null || parts.Trail.HasValue)
                        {
                            problem = "it already has a trailing tag";
                            return false;
                        }
                        parts.Lead = null;
                        parts.Trail = q.Type;
                        parts.TrailLayer = q.RefId;
                        return true;
                    }
                    long key;
                    if (choice.StartsWith("link:", StringComparison.Ordinal) && long.TryParse(choice.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out key))
                    {
                        var layer = state.Layers.FirstOrDefault(l => !l.Left && l.Node.Key == key);
                        if (layer == null || q.Public.OptionFor(choice) == null)
                        {
                            problem = "that layer is not one its tag names";
                            return false;
                        }
                        // Its trailing tag keeps naming the layer it named (its old own layer, by ID, when it was plain).
                        if (parts.Trail.HasValue && parts.TrailLayer == null)
                        {
                            parts.TrailLayer = parts.Home.Id;
                        }
                        parts.Home = layer;
                        parts.LinkTo = layer;
                        return true;
                    }
                    break;
                case PlannerCleanUpCase.DoubledType:
                    if (choice == "undouble")
                    {
                        var own = EdgeUnderscores.Replace(TypeWordFirst.Replace(parts.Own, string.Empty, 1), string.Empty);
                        if (own.Length == 0)
                        {
                            problem = "nothing would be left of the name";
                            return false;
                        }
                        parts.Own = own;
                        return true;
                    }
                    break;
                case PlannerCleanUpCase.NoLayer:
                case PlannerCleanUpCase.SeveralLayers:
                    switch (choice)
                    {
                        case "as-written":
                            parts.TrailLayer = q.RefId;
                            return q.RefId != null;
                        case "end":
                            parts.TrailLayer = q.Work.Layer.Id;
                            return true;
                        case "layer":
                            parts.TrailLayer = named;
                            return true;
                        case "drop":
                            parts.Trail = null;
                            parts.TrailLayer = null;
                            return true;
                    }
                    break;
            }
            problem = "\"" + choice + "\" is not one of its answers";
            return false;
        }

        // -------------------------------------------------------------------------------------------------------------
        // Resolve: the answers, the numbering (step 4) and the operations

        internal static PlannerCleanUpResult Resolve(PlannerCleanUpPlan plan, IDictionary<string, string> choices)
        {
            var result = new PlannerCleanUpResult();
            var state = plan.State;
            if (plan.Message != null || state == null || state.Top == null)
            {
                if (plan.Message != null)
                {
                    result.Notes.Add(plan.Message);
                }
                return result;
            }
            result.Notes.AddRange(plan.Notes);
            choices = choices ?? new Dictionary<string, string>();

            // Step 1's operations and rows (with the Planner's list, its changes too).
            var finalNames = state.Original.Nodes.Where(n => n != null).GroupBy(n => n.Key).ToDictionary(g => g.Key, g => NameOf(g.First()));
            foreach (var rename in state.StepOneRenames())
            {
                result.NodeRenames.Add(rename);
                finalNames[rename.Key] = rename.NewName;
                if (rename.Item == PhasingFixItem.Layer)
                {
                    result.LayersRenamed++;
                }
            }
            foreach (var helper in state.NewHelpers)
            {
                finalNames[helper.Key] = helper.Name;
            }
            result.NodeMoves.AddRange(state.NodeMoves);
            result.MaxLayerRenames.AddRange(state.MaxLayerRenames);
            result.MaxLayerMoves.AddRange(state.MaxLayerMoves);
            result.NewLayers.AddRange(state.NewLayers);
            result.NewHelpers.AddRange(state.NewHelpers);
            result.HelperPuts.AddRange(state.HelperPuts);
            result.Stamps.AddRange(state.Stamps);
            result.Links.AddRange(state.LinkPlan != null ? state.LinkPlan.Links : new List<PlannerObjectLink>());
            AddLayerRows(state, result);
            result.Rows.AddRange(state.ListRows);
            // A node led by a number inside a layer is no layer, and Clean up does not rename it: it is listed, because the
            // Planner does not schedule the tagged objects under it until it is renamed.
            foreach (var blocking in state.NotLayers.Values)
            {
                var one = blocking.Objects == 1;
                result.Rows.Add(new PlannerCleanUpRow
                {
                    Status = PlannerCleanUpStatus.Check,
                    Item = blocking.Node.IsGroupHead ? "Group" : "Helper",
                    LayerItSitsIn = blocking.Layer != null ? blocking.Layer.NewName : state.Top.NewName,
                    CurrentName = NameOf(blocking.Node),
                    NewName = NameOf(blocking.Node),
                    Why = "led by a number, so it is no layer on the new names, and the Planner does not schedule the "
                        + PhasingFixScript.Count(blocking.Objects, "tagged object", "tagged objects") + " under it: rename it so its name does not start with a number"
                        + (blocking.Layer != null ? ", and " + (one ? "it is" : "they are") + " scheduled in " + PlannerNaming.LayerIdText(blocking.Layer.Id) : string.Empty),
                    NodeKey = blocking.Node.Key
                });
            }

            // Each object's name for these answers, before numbering. An object that keeps its name but whose own name is
            // known holds its place in the numbering (D15) all the same: a new name listed on an old-name scene, and an object
            // still waiting for an answer (unless an answer may change its own name). So a later object of its own name is
            // numbered at once, and an object that already has its name keeps it when the answer comes.
            var entries = new List<Entry>();
            var objectRows = new Dictionary<ObjectWork, List<PlannerCleanUpRow>>();
            var inUse = new HashSet<string>(StringComparer.Ordinal);
            foreach (var work in state.Objects)
            {
                var rows = new List<PlannerCleanUpRow>();
                objectRows[work] = rows;
                switch (work.Kind)
                {
                    case ObjectKind.Untagged:
                        break;
                    case ObjectKind.Check:
                    case ObjectKind.NewListed:
                        var checkRow = ObjectRow(work, PlannerCleanUpStatus.Check, work.OldName, work.CheckWhy, null);
                        rows.Add(checkRow);
                        if (work.Kind == ObjectKind.Check && work.OldKind)
                        {
                            result.KeptOldNames++;
                        }
                        if (work.Base != null && work.Base.Own != null)
                        {
                            inUse.Add(work.Base.Own);
                            if (work.Kind == ObjectKind.NewListed && work.Base.Home != null)
                            {
                                entries.Add(new Entry { Work = work, Parts = work.Base, Before = work.OldName, Name = work.OldName, Holder = true, HolderRow = checkRow });
                            }
                        }
                        break;
                    case ObjectKind.NewUnchanged:
                        if (work.Base == null)
                        {
                            break;
                        }
                        inUse.Add(work.Base.Own);
                        entries.Add(new Entry { Work = work, Parts = work.Base, Before = work.OldName, Name = work.OldName });
                        break;
                    case ObjectKind.Convert:
                        var parts = work.Base.Copy();
                        var open = new List<QuestionWork>();
                        var answered = new List<string>();
                        foreach (var q in work.Questions)
                        {
                            string choice;
                            bool byDefault = false;
                            if (!choices.TryGetValue(q.Public.Id, out choice) || choice == null)
                            {
                                choice = q.Public.DefaultChoice;
                                byDefault = true;
                            }
                            string problem;
                            if (choice == null || choice == PlannerCleanUpOption.Leave)
                            {
                                open.Add(q);
                            }
                            else if (!ApplyChoice(q, choice, parts, state, out problem))
                            {
                                open.Add(q);
                                result.Notes.Add(work.OldName + ": the answer \"" + choice + "\" is not used - " + problem + ".");
                            }
                            else
                            {
                                var option = q.Public.OptionFor(choice);
                                answered.Add(q.Public.Why + (byDefault ? "; unless answered otherwise: " : "; answered: ")
                                    + (option != null ? (option.NeedsLayer ? option.Text.Split(':')[0] + ", " + choice.Substring(PlannerCleanUpOption.LayerPrefix.Length) : option.Text) : choice));
                            }
                        }
                        if (open.Count > 0)
                        {
                            foreach (var q in open)
                            {
                                // "leave" chosen is an answer too: the object is left as it is on purpose, for now.
                                string given;
                                var left = choices.TryGetValue(q.Public.Id, out given) && given == PlannerCleanUpOption.Leave;
                                rows.Add(ObjectRow(work, PlannerCleanUpStatus.Choose, work.OldName, q.Public.Why + (left ? "; left as it is for now: it keeps its old name" : "; keeps its name until answered"), q.Public.Id));
                                if (left)
                                {
                                    result.Left++;
                                }
                                else
                                {
                                    result.Open++;
                                }
                            }
                            if (work.OldKind)
                            {
                                result.KeptOldNames++;
                            }
                            if (!open.Any(q => q.Public.Case == PlannerCleanUpCase.DoubledType) && parts.Home != null && parts.Home.Id != null && !string.IsNullOrEmpty(parts.Own))
                            {
                                inUse.Add(parts.Own);
                                entries.Add(new Entry { Work = work, Parts = parts, Before = Compose(parts), Name = work.OldName, Holder = true });
                            }
                            break;
                        }
                        var name = Compose(parts);
                        if (!ReadsBack(name, parts))
                        {
                            rows.Add(ObjectRow(work, PlannerCleanUpStatus.Check, work.OldName, "the answers give " + name + ", which does not read back as meant on the new names ("
                                + ReadIssues(name, parts) + "); left as it is", work.Questions.Count > 0 ? work.Questions[0].Public.Id : null));
                            if (work.OldKind)
                            {
                                result.KeptOldNames++;
                            }
                            break;
                        }
                        inUse.Add(parts.Own);
                        var entry = new Entry { Work = work, Parts = parts, Before = name, Name = name, Converted = true };
                        entry.Why.AddRange(work.Why);
                        entry.Why.AddRange(answered);
                        entry.QuestionId = work.Questions.Count > 0 ? work.Questions[0].Public.Id : null;
                        entries.Add(entry);
                        break;
                }
            }

            // Step 4: numbering own-name clashes in layer order (D15).
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            var entryKeys = new HashSet<long>(entries.Select(e => e.Work.Node.Key));
            foreach (var pair in finalNames.Where(p => !entryKeys.Contains(p.Key)))
            {
                Count(taken, pair.Value, 1);
            }
            foreach (var e in entries)
            {
                Count(taken, e.Name, 1);
            }
            var ordered = PlannerNaturalOrder.StableSort(entries, (a, b) =>
            {
                int c = PlannerNaming.CompareLayerIds(a.Parts.Home.Id, b.Parts.Home.Id);
                return c != 0 ? c : PlannerNaturalOrder.Compare(a.Before, b.Before);
            });
            var firstOf = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var nextNumber = new Dictionary<string, int>(StringComparer.Ordinal);
            var entryOf = entries.ToDictionary(x => x.Work.Node.Key);
            var knownIds = state.Layers.Where(l => !l.Left && l.NotALayerWhy == null && l.Id != null).Select(l => l.Id).ToList();
            foreach (var e in ordered)
            {
                Entry first;
                if (!firstOf.TryGetValue(e.Parts.Own, out first))
                {
                    firstOf[e.Parts.Own] = e;
                    continue;
                }
                var firstText = first.Shown + " in " + PlannerNaming.LayerIdText(first.Parts.Home.Id);
                if (e.Holder)
                {
                    // An object waiting for an answer is numbered once an answer converts it; a listed one keeps its name.
                    if (e.HolderRow != null)
                    {
                        e.HolderRow.Why += "; its own name " + e.Parts.Own + " is also used by " + firstText
                            + ", which comes first in layer order, but a listed object is not renamed, so it is not numbered - rename one of them by hand";
                    }
                    continue;
                }
                var read = PlannerNaming.ParseObjectNameV2(e.Before, e.Parts.Home.Id);
                var errors = read.Issues.Where(IsError).ToList();
                if (errors.Count > 0)
                {
                    e.Check = "its own name " + e.Parts.Own + " is also used by " + firstText + ", which comes first in layer order, but its name reads with an error ("
                        + string.Join(" ", errors.Select(c => PlannerNaming.NamingIssueV2[c].Text).ToArray()) + "), so it is not numbered - rename it by hand";
                    continue;
                }
                // The count goes on from the number this own name was given last (a third object of one name is _03), past
                // the numbers other objects' own names have.
                int n;
                if (!nextNumber.TryGetValue(e.Parts.Own, out n))
                {
                    n = 2;
                }
                // A number is passed over when another object has it, and when, before a plain trailing tag, it would make the
                // own name end in a layer ID: "IN_Hrd_01_02_RM" beside layer 01_02 reads as the old order, and following the
                // naming check's advice would move the removal into 01_02.
                var skipped = new List<string>();
                var asLayer = new List<string>();
                Parts numbered;
                string numberedName;
                while (true)
                {
                    numbered = e.Parts.Copy();
                    numbered.Own = e.Parts.Own + "_" + n.ToString("00", CultureInfo.InvariantCulture);
                    numberedName = Compose(numbered);
                    if (inUse.Contains(numbered.Own) || taken.ContainsKey(numberedName))
                    {
                        skipped.Add(numbered.Own);
                    }
                    else if (EndsInLayerBeforeTag(numbered, knownIds))
                    {
                        asLayer.Add("_" + n.ToString("00", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        break;
                    }
                    n++;
                }
                if (!ReadsBack(numberedName, numbered))
                {
                    e.Check = "its own name " + e.Parts.Own + " is also used by " + firstText + ", but numbered it would be " + numberedName
                        + ", which does not read back with the same tags and reference, so it is not numbered - rename it by hand";
                    continue;
                }
                // Numbered, an object under a node of its name would fold into it as a split piece (and unnumbered it would
                // take that node's name): it keeps its name, listed.
                var above = FoldsInto(e, numberedName, state, entryOf, finalNames);
                if (above != null)
                {
                    e.Check = "its own name " + e.Parts.Own + " is also used by " + firstText + ", but numbered it would be " + numberedName
                        + ", which the Planner reads as a split piece of " + above + " above it, so it is not numbered; left as it is - rename it by hand";
                    Count(taken, e.Name, -1);
                    Count(taken, e.Work.OldName, 1);
                    e.Name = e.Work.OldName;
                    continue;
                }
                var number = new PlannerCleanUpNumber
                {
                    Key = e.Work.Node.Key,
                    Before = e.Before,
                    Name = numberedName,
                    Own = e.Parts.Own,
                    NumberedOwn = numbered.Own,
                    First = first.Shown,
                    BeforeTrailingTag = numbered.Trail.HasValue
                };
                number.Skipped.AddRange(skipped);
                result.Numbers.Add(number);
                e.Why.Add("own name " + e.Parts.Own + " is also used by " + firstText + ", which comes first in layer order, so it is numbered _"
                    + numbered.Own.Substring(e.Parts.Own.Length + 1) + (numbered.Trail.HasValue ? " before the trailing tag" : string.Empty)
                    + (skipped.Count > 0 ? " (" + string.Join(", ", skipped.ToArray()) + " already used)" : string.Empty)
                    + (asLayer.Count > 0 ? " (" + string.Join(", ", asLayer.ToArray()) + " would make its own name end in a layer ID before the tag)" : string.Empty));
                inUse.Add(numbered.Own);
                nextNumber[e.Parts.Own] = n + 1;
                Count(taken, e.Name, -1);
                Count(taken, numberedName, 1);
                e.Parts = numbered;
                e.Name = numberedName;
            }

            // Split pieces take their object's new name, and no new name may be another node's (the export would rename one
            // "X (1)"): an object whose new name another node keeps is left as it is, and listed.
            foreach (var e in entries.Where(x => x.Name != x.Work.OldName))
            {
                finalNames[e.Work.Node.Key] = e.Name;
                foreach (var piece in e.Work.Pieces)
                {
                    finalNames[piece.Key] = e.Name + NameOf(piece).Substring(e.Work.OldName.Length);
                }
            }
            while (true)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var name in finalNames.Values)
                {
                    Count(counts, name, 1);
                }
                var clash = entries.Where(x => x.Name != x.Work.OldName)
                    .FirstOrDefault(x => counts[x.Name] > 1 || x.Work.Pieces.Any(p => counts[finalNames[p.Key]] > 1));
                if (clash == null)
                {
                    break;
                }
                clash.Check = (clash.Check != null ? clash.Check + "; " : string.Empty)
                    + "its new name " + clash.Name + " would also be the name of another node; left as it is - rename it by hand";
                result.Numbers.RemoveAll(x => x.Key == clash.Work.Node.Key);
                clash.Name = clash.Work.OldName;
                finalNames[clash.Work.Node.Key] = clash.Work.OldName;
                foreach (var piece in clash.Work.Pieces)
                {
                    finalNames[piece.Key] = NameOf(piece);
                }
            }

            // The objects' operations and rows (an object holding its place keeps its name, and has its row already).
            foreach (var e in entries.Where(x => !x.Holder))
            {
                var rows = objectRows[e.Work];
                var renamed = e.Name != e.Work.OldName;
                if (renamed)
                {
                    result.NodeRenames.Add(new PhasingNodeRename { Key = e.Work.Node.Key, Item = PhasingFixItem.Object, OldName = e.Work.OldName, NewName = e.Name });
                    result.ObjectsRenamed++;
                    rows.Add(ObjectRow(e.Work, PlannerCleanUpStatus.Change, e.Name, string.Join("; ", e.Why.Distinct(StringComparer.Ordinal).ToArray()), e.QuestionId, e.Parts.Home));
                    foreach (var piece in e.Work.Pieces)
                    {
                        var pieceName = finalNames[piece.Key];
                        result.NodeRenames.Add(new PhasingNodeRename { Key = piece.Key, Item = PhasingFixItem.Object, OldName = NameOf(piece), NewName = pieceName });
                        rows.Add(new PlannerCleanUpRow
                        {
                            Status = PlannerCleanUpStatus.Change,
                            Item = "Split piece",
                            LayerItSitsIn = e.Parts.Home.NewName,
                            CurrentName = NameOf(piece),
                            NewName = pieceName,
                            Why = "a piece of " + e.Work.OldName + ", renamed with it",
                            NodeKey = piece.Key
                        });
                    }
                    if (e.Parts.LinkTo != null)
                    {
                        AddStrayLink(e, state, result);
                    }
                }
                if (e.Check != null)
                {
                    rows.Add(ObjectRow(e.Work, PlannerCleanUpStatus.Check, e.Name, e.Check, e.QuestionId));
                    if (e.Converted && !renamed && e.Work.OldKind)
                    {
                        result.KeptOldNames++;
                    }
                }
            }

            // The rows in layer order: links, then each layer's objects.
            foreach (var link in state.LinkPlan != null ? state.LinkPlan.Review : new List<PlannerReviewRow>())
            {
                result.Rows.Add(new PlannerCleanUpRow
                {
                    Status = link.IsProblem ? PlannerCleanUpStatus.Check : PlannerCleanUpStatus.Change,
                    Item = link.Item,
                    LayerItSitsIn = link.From,
                    CurrentName = link.Name,
                    NewName = link.Name,
                    Why = (link.IsProblem ? string.Empty : "linked " + link.To + ": ") + link.Why
                });
            }
            var objectOrder = PlannerNaturalOrder.StableSort(state.Objects.Where(w => objectRows[w].Count > 0), (a, b) =>
            {
                if (a.Layer == null || b.Layer == null || a.Layer.Id == null || b.Layer.Id == null)
                {
                    return (a.Layer == null || a.Layer.Id == null ? 1 : 0) - (b.Layer == null || b.Layer.Id == null ? 1 : 0);
                }
                int c = PlannerNaming.CompareLayerIds(a.Layer.Id, b.Layer.Id);
                return c != 0 ? c : PlannerNaturalOrder.Compare(a.OldName, b.OldName);
            });
            foreach (var work in objectOrder)
            {
                result.Rows.AddRange(objectRows[work]);
            }
            return result;
        }

        // An object an answer links under another layer's helper: linked there, and put on that layer with everything under
        // it (its split pieces, a group head's members), so the hierarchy keeps one Max layer. As Link's own links, it names
        // the nodes as step 1 leaves them (the helper renamed, the object not yet).
        private static void AddStrayLink(Entry e, State state, PlannerCleanUpResult result)
        {
            var node = e.Work.Node;
            var to = e.Parts.LinkTo;
            var parent = state.Step2.Nodes.FirstOrDefault(n => node.ParentKey.HasValue && n.Key == node.ParentKey.Value);
            var moved = DepthFirst(state.Step2Model, node);
            result.Links.Add(new PlannerObjectLink
            {
                Key = node.Key,
                Name = e.Work.OldName,
                FromParentKey = node.ParentKey,
                FromParentName = parent != null ? NameOf(parent) : null,
                ToKey = to.Node.Key,
                ToName = to.NewName,
                LayerName = to.MaxLayer != null ? (to.MaxLayerRenamed ? to.NewName : to.MaxLayer.Name) : null,
                IntoGroupHead = to.Node.IsGroupHead,
                IsGroupMember = node.IsGroupMember,
                IsGroupHead = node.IsGroupHead,
                Objects = Math.Max(1, moved.Count(n => !n.IsHelper && !n.IsShape))
            });
            if (to.MaxLayer == null)
            {
                return;
            }
            foreach (var each in moved)
            {
                var original = state.Original.Nodes.FirstOrDefault(n => n != null && n.Key == each.Key);
                var from = original != null ? original.LayerName : null;
                if (to.MaxLayerIsNew || !string.Equals(from, to.MaxLayer.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result.LayerMoves.Add(new PlannerNodeLayerMove
                    {
                        Key = each.Key,
                        Name = original != null ? NameOf(original) : NameOf(each),
                        FromLayer = from,
                        ToLayer = to.MaxLayer.Name,
                        ToNewLayer = to.MaxLayerIsNew
                    });
                }
            }
        }

        private static void AddLayerRows(State state, PlannerCleanUpResult result)
        {
            var top = state.Top;
            foreach (var work in new[] { top }.Concat(state.Groups).Concat(state.ListOnly))
            {
                var place = work == top ? string.Empty : PlaceOf(state, work.ListParentKey, top.NewName);
                var item = work == top ? "Phasing root" : work.Item == PhasingFixItem.Group ? (state.Groups.Contains(work) ? "Phase group" : "Group") : "Helper";
                if (work.Created || work.NewName != work.OldName || work.ListChanged)
                {
                    result.Rows.Add(new PlannerCleanUpRow
                    {
                        Status = PlannerCleanUpStatus.Change,
                        Item = item + (work.MaxLayerRenamed ? " and its layer" : string.Empty),
                        LayerItSitsIn = place,
                        CurrentName = work.OldName,
                        NewName = work.NewName,
                        Why = string.Join("; ", work.Why.Distinct(StringComparer.Ordinal).ToArray()),
                        NodeKey = work.Created ? (long?)null : work.Node.Key
                    });
                }
                foreach (var check in work.Checks)
                {
                    result.Rows.Add(new PlannerCleanUpRow { Status = PlannerCleanUpStatus.Check, Item = item, LayerItSitsIn = place, CurrentName = work.OldName, NewName = work.NewName, Why = check, NodeKey = work.Node.Key });
                }
            }
            foreach (var layer in state.Layers.Concat(state.MaxOnly).OrderBy(l => l, LayerOrder.Instance))
            {
                var helper = layer.Node != null;
                var place = PlaceOf(state, layer);
                if (layer.Created)
                {
                    // A layer and helper (or a helper on a layer the scene has) the Planner's list adds.
                    result.Rows.Add(new PlannerCleanUpRow
                    {
                        Status = PlannerCleanUpStatus.Change,
                        Item = layer.MaxLayerIsNew ? "Layer and helper" : "Helper",
                        LayerItSitsIn = place,
                        CurrentName = string.Empty,
                        NewName = layer.NewName,
                        Why = string.Join("; ", layer.Why.Distinct(StringComparer.Ordinal).ToArray())
                    });
                    continue;
                }
                var item = !helper ? "Max layer (no helper)" : layer.MaxLayerRenamed ? "Layer and helper" : layer.MaxLayerIsNew ? "Helper"
                    : layer.MaxLayer != null && layer.MaxLayer.Name != layer.OldName ? "Helper (its layer is named " + layer.MaxLayer.Name + ")" : "Helper";
                if (layer.Left)
                {
                    result.Rows.Add(new PlannerCleanUpRow { Status = PlannerCleanUpStatus.Check, Item = item, LayerItSitsIn = place, CurrentName = layer.OldName, NewName = layer.OldName, Why = layer.LeftWhy, NodeKey = helper ? layer.Node.Key : (long?)null });
                    continue;
                }
                if (layer.NotALayerWhy != null)
                {
                    result.Rows.Add(new PlannerCleanUpRow { Status = PlannerCleanUpStatus.Check, Item = item, LayerItSitsIn = place, CurrentName = layer.OldName, NewName = layer.NewName,
                        Why = layer.NotALayerWhy + ", so the objects under it are not converted - check them by hand", NodeKey = helper ? layer.Node.Key : (long?)null });
                }
                var why = new List<string>(layer.Why);
                if (layer.NewName != layer.OldName || layer.Moved || layer.ListChanged)
                {
                    result.Rows.Add(new PlannerCleanUpRow
                    {
                        Status = PlannerCleanUpStatus.Change,
                        Item = item,
                        LayerItSitsIn = place,
                        CurrentName = layer.OldName,
                        NewName = layer.NewName,
                        Why = string.Join("; ", why.Distinct(StringComparer.Ordinal).ToArray()),
                        NodeKey = helper ? layer.Node.Key : (long?)null
                    });
                }
                else if (layer.MaxLayerRenamed)
                {
                    result.Rows.Add(new PlannerCleanUpRow
                    {
                        Status = PlannerCleanUpStatus.Change,
                        Item = "Max layer",
                        LayerItSitsIn = place,
                        CurrentName = layer.MaxLayer.Name,
                        NewName = layer.NewName,
                        Why = "the Max layer takes its helper's name",
                        NodeKey = helper ? layer.Node.Key : (long?)null
                    });
                }
                foreach (var check in layer.Checks)
                {
                    result.Rows.Add(new PlannerCleanUpRow { Status = PlannerCleanUpStatus.Check, Item = item, LayerItSitsIn = place, CurrentName = layer.OldName, NewName = layer.NewName, Why = check, NodeKey = helper ? layer.Node.Key : (long?)null });
                }
            }
        }

        // What a layer sits in, as Clean up names it: its phase group (moved into one or not), the root, or the layer node
        // (or other node) it is linked under.
        private static string PlaceOf(State state, LayerWork layer)
        {
            if (layer.Group != null)
            {
                return layer.Group.NewName;
            }
            if (!layer.ParentKey.HasValue || layer.UnderRoot)
            {
                return state.Top.NewName;
            }
            return PlaceOf(state, layer.ParentKey, state.Top.NewName);
        }

        // The name a node ends with that something sits under: a root, group, layer or list helper's last name, else the node's
        // own; the fallback when there is none.
        private static string PlaceOf(State state, long? key, string fallback)
        {
            if (!key.HasValue)
            {
                return fallback;
            }
            var above = state.Works().FirstOrDefault(w => w.Node.Key == key.Value);
            if (above != null)
            {
                return above.NewName;
            }
            var node = state.Original.Nodes.FirstOrDefault(n => n != null && n.Key == key.Value);
            return node != null ? NameOf(node) : fallback;
        }

        private static PlannerCleanUpRow ObjectRow(ObjectWork work, PlannerCleanUpStatus status, string newName, string why, string question, LayerWork home = null)
        {
            var layer = home ?? work.Layer;
            return new PlannerCleanUpRow
            {
                Status = status,
                Item = "Object",
                LayerItSitsIn = layer != null ? layer.NewName : string.Empty,
                CurrentName = work.OldName,
                NewName = newName,
                Why = why,
                NodeKey = work.Node.Key,
                QuestionId = question
            };
        }

        private static void Check(ObjectWork work, string why, ObjectKind kind = ObjectKind.Check)
        {
            work.Kind = kind;
            work.CheckWhy = why;
        }

        // -------------------------------------------------------------------------------------------------------------
        // Names

        // The name parts give: [IN_|RM_]<own>[_IN|_RM[_<ID>]][ note]. A trailing tag naming the object's own layer is written plain.
        private static string Compose(Parts parts)
        {
            var text = (parts.Lead.HasValue ? Word(parts.Lead.Value) + "_" : string.Empty) + parts.Own;
            if (parts.Trail.HasValue)
            {
                text += "_" + Word(parts.Trail.Value);
                var reference = ReferenceOf(parts);
                if (reference != null)
                {
                    text += "_" + PlannerNaming.LayerIdText(reference);
                }
            }
            return text + parts.Note;
        }

        // The layer the trailing tag names in the name: none for the end of the own layer.
        private static PlannerLayerIdKey ReferenceOf(Parts parts)
        {
            if (!parts.Trail.HasValue || parts.TrailLayer == null)
            {
                return null;
            }
            return parts.Home != null && PlannerNaming.LayerIdKey(parts.TrailLayer) == PlannerNaming.LayerIdKey(parts.Home.Id) ? null : parts.TrailLayer;
        }

        // The name reads back through the naming v2 reader, against its own layer, with the tags, the reference and the own
        // name intended, and no error.
        private static bool ReadsBack(string name, Parts parts)
        {
            if (parts.Home == null || parts.Home.Id == null || string.IsNullOrEmpty(parts.Own))
            {
                return false;
            }
            var p = PlannerNaming.ParseObjectNameV2(name, parts.Home.Id);
            var reference = ReferenceOf(parts);
            return p.Kind == PlannerNamingKind.New && p.Own == parts.Own && p.Lead == parts.Lead && p.Trail == parts.Trail
                && (p.Ref == null ? reference == null : reference != null && PlannerNaming.LayerIdKey(p.Ref) == PlannerNaming.LayerIdKey(reference))
                && !p.Issues.Any(IsError);
        }

        // A name with a plain trailing tag whose own name ends in one of the scene's layer IDs, which the naming check warns
        // is the old order (the layer before the tag): the reader's own test, through NamingIssuesV2.
        private static bool EndsInLayerBeforeTag(Parts parts, List<PlannerLayerIdKey> layers)
        {
            if (!parts.Trail.HasValue || ReferenceOf(parts) != null || parts.Home == null)
            {
                return false;
            }
            var named = new[] { new PlannerObjectLayer { Name = Compose(parts), Layer = parts.Home.Id } };
            return PlannerNaming.NamingIssuesV2(named, layers).Any(i => i.Code == PlannerNamingCode.OldOrder);
        }

        // The node above an object (in the hierarchy it will have) whose planned name a numbered name would fold into as a
        // split piece (the Planner's mergedName: that name, then _N or _primitiveN, again and again), or null.
        private static string FoldsInto(Entry e, string numberedName, State state, Dictionary<long, Entry> entryOf, Dictionary<long, string> finalNames)
        {
            var stems = new HashSet<string>(StringComparer.Ordinal);
            for (var stem = numberedName; PlannerSchedule.SplitSuffix.IsMatch(stem);)
            {
                stem = PlannerSchedule.SplitSuffix.Replace(stem, string.Empty, 1);
                if (stem.Length == 0 || !stems.Add(stem))
                {
                    break;
                }
            }
            if (stems.Count == 0)
            {
                return null;
            }
            var parentOf = new Dictionary<long, long?>();
            foreach (var node in state.Step2.Nodes.Where(n => n != null))
            {
                parentOf[node.Key] = node.ParentKey;
            }
            var seen = new HashSet<long>();
            long? key = e.Parts.LinkTo != null ? e.Parts.LinkTo.Node.Key : e.Work.Node.ParentKey;
            while (key.HasValue && seen.Add(key.Value))
            {
                Entry other;
                string name;
                var planned = entryOf.TryGetValue(key.Value, out other) ? other.Name : finalNames.TryGetValue(key.Value, out name) ? name : null;
                if (planned != null && stems.Contains(planned))
                {
                    return planned;
                }
                long? next;
                key = parentOf.TryGetValue(key.Value, out next) ? next : null;
            }
            return null;
        }

        // What the reader says of a name that does not read back as meant.
        private static string ReadIssues(string name, Parts parts)
        {
            var p = PlannerNaming.ParseObjectNameV2(name, parts.Home != null ? parts.Home.Id : null);
            var errors = p.Issues.Where(IsError).Select(c => PlannerNaming.NamingIssueV2[c].Text).ToList();
            if (errors.Count > 0)
            {
                return string.Join(" ", errors.ToArray());
            }
            if (p.Kind == PlannerNamingKind.Old)
            {
                return "it still holds a Ph/St tag";
            }
            return p.Kind == null ? "no tag reads" : "it reads with own name " + p.Own;
        }

        private static bool IsError(PlannerNamingCode code)
        {
            return PlannerNaming.NamingIssueV2[code].Severity == NamingSeverity.Error;
        }

        // "IN in 02_01_4" or "IN in 02_01_4 and RM at its end", for an object the new names already read.
        private static string EventsText(PlannerObjectNameV2 p, LayerWork layer)
        {
            var own = PlannerNaming.LayerIdText(layer.SceneId);
            var parts = new List<string>();
            foreach (var e in PlannerNaming.ObjectEventsV2(p))
            {
                var word = Word(e.Type);
                parts.Add(e.When == PlannerEventTime.During ? word + " in " + own : e.When == PlannerEventTime.End ? word + " at the end of " + own : word + " in " + PlannerNaming.LayerIdText(e.Ref));
            }
            return string.Join(" and ", parts.ToArray());
        }

        // -------------------------------------------------------------------------------------------------------------
        // Old tags and the layers they name

        private static Tag TagOf(PlannerMeshSegment segment)
        {
            return new Tag
            {
                Type = segment.Type,
                Phase = segment.Phase,
                Stage = segment.Stage,
                SubStage = segment.SubStage,
                Text = "Ph" + segment.PhaseToken + "_St" + segment.StageToken + "_" + Word(segment.Type)
            };
        }

        private static Tag TagOf(Match trail)
        {
            int stage;
            int? subStage;
            PlannerNaming.ParseStageNumber(trail.Groups[2].Value, out stage, out subStage);
            int phase;
            if (!TryNumber(trail.Groups[1].Value, out phase))
            {
                phase = int.MaxValue;
            }
            var type = TypeOf(trail.Groups[3].Value.ToUpperInvariant());
            return new Tag { Type = type, Phase = phase, Stage = stage, SubStage = subStage, Text = trail.Value.Substring(1, trail.Value.Length - 4) + "_" + Word(type) };
        }

        // An old tag names a layer when the phase is the layer's and the stages agree (a whole stage covers its sub-stages, for
        // reading old tags only).
        private static bool IsOwnOf(PlannerLayerIdKey layer, Tag tag)
        {
            return layer != null && layer.Phase == tag.Phase && PlannerNaming.SameStage(tag.Stage, tag.SubStage, layer.Stage, layer.SubStage);
        }

        /// <summary>
        /// The layer an old tag names among the scene's layers: the one of its exact ID (several nodes of one ID are one layer);
        /// for a sub-stage with no layer, the whole stage's layer; for a whole stage with no layer, its only sub-stage layer, or
        /// the several it has; else none.
        /// </summary>
        private static Target ResolveTag(State state, Tag tag)
        {
            // By the IDs the layers' names give now, which the old tags were written against; a layer the Planner's list adds
            // has its own.
            var same = state.Layers.Where(l => !l.Left && l.NotALayerWhy == null && l.SceneId.Phase == tag.Phase && l.SceneId.Stage == tag.Stage).ToList();
            var exact = same.FirstOrDefault(l => l.SceneId.SubStage == tag.SubStage);
            if (exact != null)
            {
                return new Target { Layer = exact };
            }
            if (tag.SubStage.HasValue)
            {
                var whole = same.FirstOrDefault(l => !l.SceneId.SubStage.HasValue);
                return whole != null
                    ? new Target { Layer = whole, How = PlannerNaming.LayerIdText(new PlannerLayerIdKey(tag.Phase, tag.Stage, tag.SubStage)) + " has no layer of its own: the whole stage's layer covers it" }
                    : new Target();
            }
            var subs = same.Where(l => l.SceneId.SubStage.HasValue).GroupBy(l => PlannerNaming.LayerIdKey(l.SceneId)).Select(g => g.First()).OrderBy(l => l, LayerOrder.Instance).ToList();
            if (subs.Count == 1)
            {
                return new Target { Layer = subs[0], How = "the only layer of stage " + PlannerNaming.LayerIdText(new PlannerLayerIdKey(tag.Phase, tag.Stage, null)) };
            }
            var target = new Target();
            target.Several.AddRange(subs);
            return target;
        }

        // The ID an old tag writes, as a layer ID: null when a number has more than two digits.
        private static PlannerLayerIdKey WritableId(Tag tag)
        {
            if (tag.Phase < 0 || tag.Phase > 99 || tag.Stage > 99 || (tag.SubStage.HasValue && tag.SubStage.Value > 99))
            {
                return null;
            }
            return new PlannerLayerIdKey(tag.Phase, tag.Stage, tag.SubStage);
        }

        private static PlannerLayerIdKey IdOf(string phase, string stage, Group subStage)
        {
            int p, s, m = 0;
            if (!TryNumber(phase, out p) || !TryNumber(stage, out s) || (subStage.Success && !TryNumber(subStage.Value, out m)))
            {
                return null;
            }
            return new PlannerLayerIdKey(p, s, subStage.Success ? m : (int?)null);
        }

        private static PlannerTaskType TypeOf(string word)
        {
            return string.Equals(word, "IN", StringComparison.OrdinalIgnoreCase) ? PlannerTaskType.Install : PlannerTaskType.Dismantle;
        }

        private static string Word(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "IN" : "RM";
        }

        private static string Done(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "installed" : "removed";
        }

        private static string Event(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "install" : "removal";
        }

        private static string Capital(string text)
        {
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        // -------------------------------------------------------------------------------------------------------------
        // The simulated scene

        /// <summary>
        /// A snapshot with operations applied: nodes renamed, linked under new parents (a node moved goes last among its new
        /// siblings), put on other Max layers, Max layers renamed and moved, new Max layers and helpers made (a new helper goes
        /// last among its parent's children, with its key below zero), and Planner IDs written. Max layer renames and moves name
        /// the layers by their current names, as the batch finds them, or a new layer by its name.
        /// </summary>
        internal static PlannerSceneSnapshot Apply(PlannerSceneSnapshot before, IEnumerable<PhasingNodeRename> renames, IEnumerable<PhasingNodeMove> moves,
            IEnumerable<PlannerObjectLink> links, IEnumerable<PlannerNodeLayerMove> layerMoves, IEnumerable<PhasingMaxLayerRename> maxRenames, IEnumerable<PhasingMaxLayerMove> maxMoves,
            IEnumerable<PlannerCleanUpNewLayer> newLayers = null, IEnumerable<PlannerCleanUpNewHelper> newHelpers = null, IEnumerable<PlannerCleanUpStamp> stamps = null)
        {
            before = before ?? new PlannerSceneSnapshot();
            var names = new Dictionary<long, string>();
            foreach (var r in renames ?? Enumerable.Empty<PhasingNodeRename>())
            {
                names[r.Key] = r.NewName;
            }
            var parents = new Dictionary<long, long>();
            foreach (var m in moves ?? Enumerable.Empty<PhasingNodeMove>())
            {
                parents[m.Key] = m.ToParentKey;
            }
            foreach (var l in links ?? Enumerable.Empty<PlannerObjectLink>())
            {
                parents[l.Key] = l.ToKey;
            }
            var layerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in maxRenames ?? Enumerable.Empty<PhasingMaxLayerRename>())
            {
                layerNames[r.OldName] = r.NewName;
            }
            Func<string, string> layerName = n =>
            {
                string renamed;
                return n != null && layerNames.TryGetValue(n, out renamed) ? renamed : n;
            };
            // Where a node or a layer ends: its layer's (or parent's) name after the renames; a new layer's name as it is.
            Func<string, bool, string> finalLayer = (n, isNew) => isNew ? n : layerName(n);
            var onLayer = new Dictionary<long, string>();
            foreach (var l in layerMoves ?? Enumerable.Empty<PlannerNodeLayerMove>())
            {
                onLayer[l.Key] = finalLayer(l.ToLayer, l.ToNewLayer);
            }
            var layerParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in maxMoves ?? Enumerable.Empty<PhasingMaxLayerMove>())
            {
                layerParents[m.Name] = finalLayer(m.NewParentName, m.NewParentIsNew);
            }
            var ids = new Dictionary<long, string>();
            foreach (var s in stamps ?? Enumerable.Empty<PlannerCleanUpStamp>())
            {
                ids[s.Key] = s.PlannerId;
            }

            var after = new PlannerSceneSnapshot { ExportHidden = before.ExportHidden };
            foreach (var layer in before.Layers)
            {
                if (layer == null)
                {
                    continue;
                }
                string parent;
                after.Layers.Add(new PhasingMaxLayer { Name = layerName(layer.Name), ParentName = layer.Name != null && layerParents.TryGetValue(layer.Name, out parent) ? parent : layerName(layer.ParentName) });
            }
            foreach (var layer in newLayers ?? Enumerable.Empty<PlannerCleanUpNewLayer>())
            {
                after.Layers.Add(new PhasingMaxLayer { Name = layer.Name, ParentName = layer.ParentName == null ? null : finalLayer(layer.ParentName, layer.ParentIsNew) });
            }
            var lastIndex = new Dictionary<long, int>();
            foreach (var node in before.Nodes.Where(n => n != null && n.ParentKey.HasValue))
            {
                int at;
                lastIndex[node.ParentKey.Value] = lastIndex.TryGetValue(node.ParentKey.Value, out at) ? Math.Max(at, node.ChildIndex) : node.ChildIndex;
            }
            Func<long?, int> lastUnder = parent =>
            {
                int at;
                var key = parent ?? long.MinValue;
                var index = (lastIndex.TryGetValue(key, out at) ? at : (parent.HasValue ? -1 : before.Nodes.Count(n => n != null && !n.ParentKey.HasValue) - 1)) + 1;
                lastIndex[key] = index;
                return index;
            };
            // The new helpers first, as the batch makes them before it links anything under them.
            var made = new List<PlannerSceneNode>();
            foreach (var helper in newHelpers ?? Enumerable.Empty<PlannerCleanUpNewHelper>())
            {
                made.Add(new PlannerSceneNode
                {
                    Key = helper.Key,
                    Name = helper.Name,
                    ParentKey = helper.ParentKey,
                    ChildIndex = lastUnder(helper.ParentKey),
                    IsHelper = true,
                    LayerName = finalLayer(helper.LayerName, helper.LayerIsNew),
                    PlannerId = helper.PlannerId
                });
            }
            foreach (var node in before.Nodes)
            {
                if (node == null)
                {
                    continue;
                }
                string name, layer, id;
                long parent;
                var copy = new PlannerSceneNode
                {
                    Key = node.Key,
                    Name = names.TryGetValue(node.Key, out name) ? name : node.Name,
                    ParentKey = node.ParentKey,
                    ChildIndex = node.ChildIndex,
                    HasChildren = node.HasChildren,
                    IsGroupHead = node.IsGroupHead,
                    IsGroupMember = node.IsGroupMember,
                    IsHelper = node.IsHelper,
                    IsShape = node.IsShape,
                    LayerName = onLayer.TryGetValue(node.Key, out layer) ? layer : layerName(node.LayerName),
                    PlannerId = ids.TryGetValue(node.Key, out id) ? id : node.PlannerId,
                    Hidden = node.Hidden,
                    NoExport = node.NoExport,
                    NotInThisExport = node.NotInThisExport
                };
                if (parents.TryGetValue(node.Key, out parent) && parent != node.ParentKey)
                {
                    copy.ParentKey = parent;
                    copy.ChildIndex = lastUnder(parent);
                }
                after.Nodes.Add(copy);
            }
            after.Nodes.AddRange(made);
            // A node that gains children is a parent now.
            var withChildren = new HashSet<long>(after.Nodes.Where(n => n.ParentKey.HasValue).Select(n => n.ParentKey.Value));
            foreach (var node in after.Nodes)
            {
                if (withChildren.Contains(node.Key))
                {
                    node.HasChildren = true;
                }
            }
            return after;
        }

        // A node and everything linked under it, depth first, each node before the nodes under it, those in the scene's order.
        private static List<PlannerSceneNode> DepthFirst(PlannerSceneModel scene, PlannerSceneNode root)
        {
            var result = new List<PlannerSceneNode>();
            var seen = new HashSet<long>();
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
                var children = scene.ChildrenOf(node);
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    pending.Push(children[i]);
                }
            }
            return result;
        }

        private static void Count(Dictionary<string, int> counts, string name, int by)
        {
            int count;
            counts.TryGetValue(name, out count);
            counts[name] = count + by;
            if (counts[name] <= 0)
            {
                counts.Remove(name);
            }
        }

        private static string NameOf(PlannerSceneNode node)
        {
            return node.Name ?? string.Empty;
        }

        private static string Pad2(int value)
        {
            return value.ToString("00", CultureInfo.InvariantCulture);
        }

        private static bool TryNumber(string digits, out int value)
        {
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Work items

        /// <summary>What the plan keeps for <see cref="Resolve"/>: the scene, step 1's and step 2's operations, and the objects.</summary>
        internal sealed class State
        {
            public PlannerSceneSnapshot Original;
            /// <summary>The scene as step 1 leaves it (with the Planner's list, its new layers and helpers in it): Link plans on it.</summary>
            public PlannerSceneSnapshot Step1;
            /// <summary>The scene as steps 1 and 2 leave it: the objects are read on it.</summary>
            public PlannerSceneSnapshot Step2;
            /// <summary><see cref="Step2"/> as a hierarchy.</summary>
            public PlannerSceneModel Step2Model;
            public readonly List<PhasingWordFix> Fixes = new List<PhasingWordFix>();
            /// <summary>The scene reads as the old names.</summary>
            public bool OldScene;
            public NodeWork Top;
            public readonly List<NodeWork> Groups = new List<NodeWork>();
            public readonly List<LayerWork> Layers = new List<LayerWork>();
            /// <summary>Max layers in the phasing tree without a helper, cleaned by their own names.</summary>
            public readonly List<LayerWork> MaxOnly = new List<LayerWork>();
            public readonly List<PhasingNodeMove> NodeMoves = new List<PhasingNodeMove>();
            public readonly List<PhasingMaxLayerRename> MaxLayerRenames = new List<PhasingMaxLayerRename>();
            public readonly List<PhasingMaxLayerMove> MaxLayerMoves = new List<PhasingMaxLayerMove>();
            public PlannerLinkPlan LinkPlan;
            public readonly List<ObjectWork> Objects = new List<ObjectWork>();
            /// <summary>Nodes led by a number inside a layer that keep the Planner from scheduling the tagged objects under them, by handle.</summary>
            public readonly Dictionary<long, NotALayer> NotLayers = new Dictionary<long, NotALayer>();
            public readonly List<QuestionWork> Questions = new List<QuestionWork>();
            /// <summary>The scene's layer IDs, in layer order, for answers that name a layer.</summary>
            public readonly List<string> LayerIds = new List<string>();

            // The Planner's list (D22).
            /// <summary>Build layers and helpers' plan for the list, on the scene as step 1 left it before the list; null without a list.</summary>
            public PlannerLayerPlan Build;
            /// <summary>Helpers only the list names (outside the phasing root, or not read as a layer or group): renamed or created by the list alone.</summary>
            public readonly List<NodeWork> ListOnly = new List<NodeWork>();
            public readonly List<PlannerCleanUpNewLayer> NewLayers = new List<PlannerCleanUpNewLayer>();
            public readonly List<PlannerCleanUpNewHelper> NewHelpers = new List<PlannerCleanUpNewHelper>();
            public readonly List<PlannerNodeLayerMove> HelperPuts = new List<PlannerNodeLayerMove>();
            public readonly List<PlannerCleanUpStamp> Stamps = new List<PlannerCleanUpStamp>();
            /// <summary>The list's rows Clean up cannot bring in line, and the scene's phasing layers the list does not have.</summary>
            public readonly List<PlannerCleanUpRow> ListRows = new List<PlannerCleanUpRow>();

            /// <summary>The root, the groups, the layer nodes and the helpers only the list names.</summary>
            public IEnumerable<NodeWork> Works()
            {
                return new[] { Top }.Where(w => w != null).Concat(Groups).Concat(Layers).Concat(ListOnly);
            }

            /// <summary>Step 1's node renames: the root, the groups, the layer nodes and the helpers the list renames.</summary>
            public List<PhasingNodeRename> StepOneRenames()
            {
                return Works().Where(w => !w.Created && w.NewName != w.OldName && !(w is LayerWork && ((LayerWork)w).Left))
                    .Select(w => new PhasingNodeRename { Key = w.Node.Key, Item = w.Item, OldName = w.OldName, NewName = w.NewName }).ToList();
            }

            /// <summary>
            /// The ID a layer ID of the scene ends with: the ID of the layer that has it now (the list may give it another,
            /// spec 5.3), else the ID as it is (no layer has it). References to it are written with this one.
            /// </summary>
            public PlannerLayerIdKey FinalId(PlannerLayerIdKey sceneId)
            {
                if (sceneId == null)
                {
                    return null;
                }
                var key = PlannerNaming.LayerIdKey(sceneId);
                var layer = Layers.FirstOrDefault(l => !l.Left && l.SceneId != null && l.Id != null && PlannerNaming.LayerIdKey(l.SceneId) == key);
                return layer != null ? layer.Id : sceneId;
            }

            /// <summary>
            /// The layer the Planner's list gives an ID no layer of the scene had (spec 5.3), or null: a layer found by its
            /// Planner ID whose ID the list changes to <paramref name="written"/>, when no layer's name gives that ID now. A
            /// reference written to that ID was never written for that layer, so it is listed, not rewritten, and an old tag
            /// naming it is asked about with no default answer. Null too when a layer has the ID now: references follow it.
            /// </summary>
            public LayerWork RenumberedOnto(PlannerLayerIdKey written)
            {
                if (written == null || !written.Phase.HasValue)
                {
                    return null;
                }
                var key = PlannerNaming.LayerIdKey(written);
                if (Layers.Any(l => !l.Left && l.SceneId != null && PlannerNaming.LayerIdKey(l.SceneId) == key))
                {
                    return null;
                }
                return Layers.FirstOrDefault(l => !l.Left && !l.Created && l.NotALayerWhy == null && l.Id != null && l.SceneId != null
                    && PlannerNaming.LayerIdKey(l.Id) == key && PlannerNaming.LayerIdKey(l.SceneId) != key);
            }
        }

        /// <summary>The root, a phase group or a layer node, and what step 1 does to it.</summary>
        internal class NodeWork
        {
            public PlannerSceneNode Node;
            public string OldName;
            public string NewName;
            public PhasingFixItem Item;
            /// <summary>A phase group's phase.</summary>
            public int Phase;
            public readonly List<string> Why = new List<string>();
            /// <summary>Notes for the review, apart from the change.</summary>
            public readonly List<string> Checks = new List<string>();
            public bool MaxLayerRenamed;
            /// <summary>A helper the Planner's list adds (its node's key is below zero); <see cref="OldName"/> is empty.</summary>
            public bool Created;
            /// <summary>The Planner's list changes it in a way its name does not show (moved, put on its layer, its Planner ID, its layer added).</summary>
            public bool ListChanged;
            /// <summary>The node it ends up linked under, when the Planner's list nests it elsewhere; else null.</summary>
            public long? ListParentKey;
        }

        internal sealed class LayerWork : NodeWork
        {
            /// <summary>
            /// The layer ID it ends with, which the names Clean up writes use. Before the Planner's list is read, and without
            /// one, the ID its name gives; the list may give it another (<see cref="SceneId"/> is the one its name has now).
            /// </summary>
            public PlannerLayerIdKey Id;
            /// <summary>The ID its name gives in the scene as it is (cleaned), which old tags and references written today name.</summary>
            public PlannerLayerIdKey SceneId;
            /// <summary>Its Max layer is one Clean up creates (<see cref="NodeWork.Created"/>, or a helper whose layer the list adds).</summary>
            public bool MaxLayerIsNew;
            /// <summary>The Planner's list names it so that it is no layer on the new names: its objects are not converted, and why.</summary>
            public string NotALayerWhy;
            /// <summary>The name after the ID, cleaned.</summary>
            public string Part;
            public int Order;
            /// <summary>The node it is linked under in the scene as it is (a group, the root, or another layer's helper).</summary>
            public long? ParentKey;
            public NodeWork Group;
            /// <summary>Directly under the root.</summary>
            public bool UnderRoot;
            /// <summary>Linked into its phase group.</summary>
            public bool Moved;
            public PlannerSceneUnit Unit;
            public PhasingMaxLayer MaxLayer;
            public bool HadDates;
            public bool HadTbc;
            /// <summary>Left as it is, with its objects, and listed.</summary>
            public bool Left;
            public string LeftWhy;
        }

        internal enum ObjectKind
        {
            Untagged,
            /// <summary>Converted (old names), or its own name's text fixed (new names).</summary>
            Convert,
            /// <summary>A new name on a new-name scene that needs nothing.</summary>
            NewUnchanged,
            /// <summary>A new name on an old-name scene: listed, unchanged (spec 6.1).</summary>
            NewListed,
            /// <summary>Listed, left as it is.</summary>
            Check
        }

        internal sealed class ObjectWork
        {
            public PlannerSceneNode Node;
            public string OldName;
            /// <summary>Its own layer: the nearest layer node above it after Link; null when there is none.</summary>
            public LayerWork Layer;
            public int Order;
            public ObjectKind Kind;
            /// <summary>Its name holds an old Ph/St tag.</summary>
            public bool OldKind;
            /// <summary>The rule's conversion, which the answers start from; for a new name, its parts as read.</summary>
            public Parts Base;
            public readonly List<string> Why = new List<string>();
            public string CheckWhy;
            public readonly List<QuestionWork> Questions = new List<QuestionWork>();
            public readonly List<PlannerSceneNode> Pieces = new List<PlannerSceneNode>();
        }

        /// <summary>A new name in parts: the tags, the own name, the layer a trailing tag names, and a note after a space.</summary>
        internal sealed class Parts
        {
            public PlannerTaskType? Lead;
            public string Own;
            public PlannerTaskType? Trail;
            /// <summary>The layer the trailing tag names; null for the end of the own layer. Written plain when it is the own layer.</summary>
            public PlannerLayerIdKey TrailLayer;
            public string Note = string.Empty;
            /// <summary>The layer it is read against: its own layer, or the one an answer links it under.</summary>
            public LayerWork Home;
            /// <summary>The layer whose helper an answer links it under, or null.</summary>
            public LayerWork LinkTo;

            public Parts Copy()
            {
                return (Parts)MemberwiseClone();
            }
        }

        internal sealed class QuestionWork
        {
            public ObjectWork Work;
            public PlannerCleanUpQuestion Public;
            /// <summary>The type word the question is about.</summary>
            public PlannerTaskType Type;
            /// <summary>The ID an "as written" or "ref" answer refers to.</summary>
            public PlannerLayerIdKey RefId;
        }

        private sealed class Entry
        {
            public ObjectWork Work;
            public Parts Parts;
            /// <summary>The name before numbering (for an object holding its place, the name its answers so far give).</summary>
            public string Before;
            public string Name;
            public bool Converted;
            public string QuestionId;
            public string Check;
            public readonly List<string> Why = new List<string>();
            /// <summary>It keeps its name (listed, or waiting for an answer) but holds its place in the numbering.</summary>
            public bool Holder;
            /// <summary>A listed object's row, which says when another object comes first with its own name.</summary>
            public PlannerCleanUpRow HolderRow;

            /// <summary>How the review names it: the name it gets, or, holding its place, the name it has.</summary>
            public string Shown
            {
                get { return Holder ? Work.OldName : Before; }
            }
        }

        /// <summary>A node led by a number inside a layer (a 3ds Max group), the layer it sits in, and the tagged objects under it.</summary>
        internal sealed class NotALayer
        {
            public PlannerSceneNode Node;
            public LayerWork Layer;
            public int Objects;
        }

        private sealed class Tag
        {
            public PlannerTaskType Type;
            public int Phase;
            public int Stage;
            public int? SubStage;
            /// <summary>As written: "Ph2_St01_4_IN".</summary>
            public string Text;
        }

        private sealed class Target
        {
            public LayerWork Layer;
            /// <summary>How the tag names it, when not by its exact ID.</summary>
            public string How;
            public readonly List<LayerWork> Several = new List<LayerWork>();
        }

        private sealed class MaxRename
        {
            public PhasingMaxLayer Layer;
            public string NewName;
            public PhasingFixItem Item;
            public NodeWork Work;
        }

        /// <summary>Layers by ID (as numbers), then in the scene's order; a layer without an ID last.</summary>
        private sealed class LayerOrder : IComparer<LayerWork>
        {
            public static readonly LayerOrder Instance = new LayerOrder();

            public int Compare(LayerWork a, LayerWork b)
            {
                if (a.Id == null || b.Id == null)
                {
                    int c0 = (a.Id == null ? 1 : 0) - (b.Id == null ? 1 : 0);
                    return c0 != 0 ? c0 : a.Order.CompareTo(b.Order);
                }
                int c = PlannerNaming.CompareLayerIds(a.Id, b.Id);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            }
        }
    }
}
