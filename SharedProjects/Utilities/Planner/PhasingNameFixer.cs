using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>
    /// Works out how to correct the phasing layer and object names of a scene, so that exporting it gives the
    /// names the Planner expects. Pure logic over an abstract scene (nodes keyed by their 3ds Max handle, plus the
    /// Max layers); the 3ds Max side only reads the scene and carries out the plan.
    ///
    /// A port of the clean-up first done on an exported GLB, rule for rule:
    ///   - the phasing top node is the first top-level node with "phasing" in its name;
    ///   - its children named _&lt;n&gt;_&lt;name&gt; are the phase groups; a child that reads as a layer is moved into
    ///     the group of its phase;
    ///   - the groups' children are the layers, &lt;PP&gt;_&lt;SS&gt;[.&lt;split&gt;]_&lt;name&gt;: the phase is written with two
    ///     digits and an underscore, a stage with several layers is split .1, .2, ... in start-date order, and the
    ///     review can set any layer's stage;
    ///   - the layers' children are the objects: the leading Ph/St tag becomes the layer's, and a trailing
    ///     removal tag follows the layer it points at to its new ID;
    ///   - names get the project's word fixes (again until they settle) and lose their spaces; dates and TBC are
    ///     never changed, and a word fix that would leave a name unreadable is not used on it.
    /// Anything that does not read is left as it is and listed for a person to check.
    /// </summary>
    public static class PhasingNameFixer
    {
        public const string NothingToDo = "No top-level node has \"phasing\" in its name, so there are no phasing names to fix.";

        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        // [0-9] rather than \d (.NET's \d matches every Unicode digit), \z rather than $ (.NET's $ also matches
        // before a final line feed), and JavaScript's own sets for \s and . (.NET's \s leaves out U+FEFF but takes
        // U+0085, and its . also takes \r, U+2028 and U+2029), so these read exactly what the reference's
        // JavaScript patterns read.
        private const string Space = @"[\t\n\u000B\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";
        private const string NotLineEnd = @"[^\n\r\u2028\u2029]";
        private static readonly Regex GroupPattern = new Regex(@"^_([0-9]+)_(" + NotLineEnd + @"+)\z", Options);
        private static readonly Regex LayerPattern = new Regex(@"^([0-9]+)[._]([0-9]+)(?:\.([0-9]+))?_(" + NotLineEnd + @"+)\z", Options);
        // The first DD-MM-YY token of a layer's description: its start date.
        private static readonly Regex DatePattern = new Regex(@"(?:^|_)([0-9]{2})-([0-9]{2})-([0-9]{2})(?=_|\z)", Options);
        private static readonly Regex LeadPattern = new Regex(@"^Ph([0-9]+)_St([0-9]+)(?:\.([0-9]+))?_(IN|RM)(?=_|\z)", OptionsIgnoreCase);
        private static readonly Regex TrailPattern = new Regex(@"_Ph([0-9]+)_St([0-9]+)(?:\.([0-9]+))?_RM\z", OptionsIgnoreCase);
        private static readonly Regex TagType = new Regex(@"_(IN|RM)\z", OptionsIgnoreCase);
        // Leading zeros of a removal tag's phase ("_Ph06" gives "_Ph6"), keeping the last digit ("_Ph0" stays).
        private static readonly Regex TrailPhaseZeros = new Regex(@"^_Ph0+(?=[0-9])", Options);
        private static readonly Regex SpacedDash = new Regex(Space + "+-" + Space + "+", Options);
        private static readonly Regex Whitespace = new Regex(Space + "+", Options);
        private static readonly Regex TrailingUnderscores = new Regex(@"_+\z", Options);
        // Dates and TBC: a word fix is never used where it would change them.
        private static readonly Regex DateOrTbc = new Regex(@"[0-9]{2}-[0-9]{2}-[0-9]{2}|TBC", Options);

        private const string NoDate = "9999-99-99";
        // The text fix runs again until a name stops changing; word fixes that still change it after this many
        // rounds feed each other, and are used once only on that name.
        private const int MaxTextRounds = 8;

        /// <summary>The fixes the HS2 model needed; a scene with no saved list starts with these.</summary>
        public static List<PhasingWordFix> DefaultWordFixes()
        {
            return new List<PhasingWordFix>
            {
                new PhasingWordFix("Constraction", "Construction"),
                new PhasingWordFix("SubGgrade", "SubGrade"),
                new PhasingWordFix("Cource", "Course"),
                new PhasingWordFix("Islandsl", "Islands"),
                new PhasingWordFix("Sub-grade_and_Sub-base", "SubGrade_and_SubBase")
            };
        }

        /// <summary>Why a word fix cannot be used, or null when it is fine.</summary>
        public static string DescribeWordFixProblem(PhasingWordFix fix)
        {
            if (fix == null || string.IsNullOrEmpty(fix.Find))
            {
                return "there is no text to find";
            }
            if (!string.IsNullOrEmpty(fix.Replace) && fix.Replace.IndexOf(fix.Find, StringComparison.Ordinal) >= 0)
            {
                return "the new text contains the text it replaces, so every run would change the names again";
            }
            return null;
        }

        /// <summary>
        /// The word fixes in order (reason "spelling"), then " - " becomes "-" and any other run of spaces "_"
        /// (reason "spaces replaced with underscores"), again until the text stops changing, so a second run finds
        /// nothing more to do. A word fix is not used where it would change a date (DD-MM-YY) or TBC. Reasons are
        /// added to <paramref name="reasons"/>.
        /// </summary>
        public static string FixText(string text, IEnumerable<PhasingWordFix> wordFixes, ICollection<string> reasons)
        {
            return FixText(text, UsableFixes(wordFixes), reasons, null, null, null).Text;
        }

        private static List<PhasingWordFix> UsableFixes(IEnumerable<PhasingWordFix> wordFixes)
        {
            return (wordFixes ?? Enumerable.Empty<PhasingWordFix>()).Where(f => DescribeWordFixProblem(f) == null).ToList();
        }

        /// <summary>
        /// The text fix. <paramref name="reads"/> says whether a text still reads as the part of the name it came
        /// from (null: any text does); a word fix that would stop it reading is not used on it. What is worth
        /// telling the modeller goes to <paramref name="notes"/>, under the node's <paramref name="name"/>.
        /// </summary>
        private static TextFix FixText(string text, List<PhasingWordFix> fixes, ICollection<string> reasons, Func<string, bool> reads,
            TextFixNotes notes, string name)
        {
            var original = text ?? string.Empty;
            var result = original;
            var unreadable = new List<PhasingWordFix>();
            bool spelling = false, spaces = false;
            string firstRound = null;
            bool firstSpelling = false, firstSpaces = false;
            int firstUnreadable = 0;
            for (int round = 1; ; round++)
            {
                var before = result;
                var used = new List<PhasingWordFix>();
                foreach (var fix in fixes)
                {
                    // string.Replace is ordinal: case-sensitive, culture-insensitive.
                    var replaced = result.Replace(fix.Find, fix.Replace ?? string.Empty);
                    if (replaced == result)
                    {
                        continue;
                    }
                    if (!SameDatesAndTbc(result, replaced))
                    {
                        if (notes != null && !notes.DateOrTbc.Contains(fix))
                        {
                            notes.DateOrTbc.Add(fix);
                        }
                        continue;
                    }
                    if (reads != null && !reads(Despaced(replaced)))
                    {
                        if (!unreadable.Contains(fix))
                        {
                            unreadable.Add(fix);
                        }
                        continue;
                    }
                    result = replaced;
                    spelling = true;
                    used.Add(fix);
                }
                if (Whitespace.IsMatch(result))
                {
                    result = Despaced(result);
                    spaces = true;
                }
                if (round == 1)
                {
                    firstRound = result;
                    firstSpelling = spelling;
                    firstSpaces = spaces;
                    firstUnreadable = unreadable.Count;
                }
                if (result == before)
                {
                    break;
                }
                if (round == MaxTextRounds)
                {
                    // The fixes feed each other: used once only here, and noted.
                    result = firstRound;
                    spelling = firstSpelling;
                    spaces = firstSpaces;
                    unreadable.RemoveRange(firstUnreadable, unreadable.Count - firstUnreadable);
                    if (notes != null)
                    {
                        notes.Unsettled.Add(new UnsettledText { Name = name ?? original, Fixes = used });
                    }
                    break;
                }
            }
            if (result == original)
            {
                // Changed and changed back: nothing to report.
                spelling = false;
                spaces = false;
            }
            if (reasons != null)
            {
                if (spelling)
                {
                    reasons.Add("spelling");
                }
                if (spaces)
                {
                    reasons.Add("spaces replaced with underscores");
                }
            }
            return new TextFix { Text = result, Unreadable = unreadable };
        }

        private static string Despaced(string text)
        {
            return Whitespace.Replace(SpacedDash.Replace(text, "-"), "_");
        }

        private static bool SameDatesAndTbc(string before, string after)
        {
            var was = DateOrTbc.Matches(before);
            var now = DateOrTbc.Matches(after);
            if (was.Count != now.Count)
            {
                return false;
            }
            for (int i = 0; i < was.Count; i++)
            {
                if (was[i].Value != now[i].Value)
                {
                    return false;
                }
            }
            return true;
        }

        private static string FixNames(List<PhasingWordFix> fixes)
        {
            var named = fixes.Select(f => "\"" + f.Find + "\" to \"" + (f.Replace ?? string.Empty) + "\"").ToList();
            switch (named.Count)
            {
                case 0:
                    return "word fixes";
                case 1:
                    return "word fix " + named[0];
                default:
                    return "word fixes " + string.Join(", ", named.Take(named.Count - 1).ToArray()) + " and " + named[named.Count - 1];
            }
        }

        /// <summary>
        /// Plans the fix. <paramref name="stageOverrides"/> maps a layer node's key to the stage the review gave it.
        /// Nothing is changed: the plan lists the renames and moves for the 3ds Max side to carry out.
        /// </summary>
        public static PhasingFixPlan Plan(IEnumerable<PhasingSceneNode> nodes, IEnumerable<PhasingMaxLayer> maxLayers,
            IEnumerable<PhasingWordFix> wordFixes, IDictionary<long, int> stageOverrides)
        {
            var plan = new PhasingFixPlan();
            var fixes = new List<PhasingWordFix>();
            foreach (var fix in wordFixes ?? Enumerable.Empty<PhasingWordFix>())
            {
                var problem = DescribeWordFixProblem(fix);
                if (problem == null)
                {
                    fixes.Add(fix);
                }
                else if (fix != null && !string.IsNullOrEmpty(fix.Find))
                {
                    plan.Notes.Add(string.Format("The word fix \"{0}\" to \"{1}\" is not used: {2}.", fix.Find, fix.Replace, problem));
                }
            }
            var overrides = stageOverrides ?? new Dictionary<long, int>();
            var scene = new Scene(nodes);
            var checks = new List<Func<PhasingFixRow>>();
            var notes = new TextFixNotes();

            // Rule 1: the phasing top node.
            var tops = scene.Roots.Where(n => NameOf(n).IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (tops.Count == 0)
            {
                plan.Message = NothingToDo;
                return plan;
            }
            var top = new TopWork { Node = tops[0], OldName = NameOf(tops[0]) };
            var topFix = FixText(top.OldName, fixes, top.Why, t => t.IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0, notes, top.OldName);
            top.NewName = topFix.Text;
            CheckUnreadable(top, topFix, top, checks);
            plan.TopKey = top.Node.Key;
            plan.TopName = top.OldName;
            foreach (var other in tops.Skip(1))
            {
                var otherNode = other;
                checks.Add(() => CheckRow(PhasingFixItem.TopLayer, string.Empty, NameOf(otherNode), NameOf(otherNode), otherNode.Key,
                    "another top-level node with \"phasing\" in its name; only " + top.OldName + " is corrected - check which one holds the phasing"));
            }

            // Rule 2: the phase groups.
            var groups = new List<GroupWork>();
            foreach (var child in scene.ChildrenOf(top.Node))
            {
                var match = GroupPattern.Match(NameOf(child));
                int phase;
                if (!match.Success || !TryNumber(match.Groups[1].Value, out phase))
                {
                    continue;
                }
                var group = new GroupWork { Node = child, OldName = NameOf(child), Phase = phase };
                var digits = match.Groups[1].Value;
                var groupFix = FixText(match.Groups[2].Value, fixes, group.Why, d => ReadsAsGroup(digits, d), notes, group.OldName);
                group.Description = groupFix.Text;
                group.NewName = "_" + digits + "_" + group.Description;
                CheckUnreadable(group, groupFix, top, checks);
                groups.Add(group);
            }
            var groupKeys = new HashSet<long>(groups.Select(g => g.Node.Key));

            // Rule 3: the layers. Layers to move come first, as in the reference (their order breaks date ties).
            var layers = new List<LayerWork>();
            // Layers under the top node that cannot be moved: left as they are, but removal tags may name them.
            var unplaced = new List<LayerWork>();
            foreach (var child in scene.ChildrenOf(top.Node))
            {
                if (groupKeys.Contains(child.Key))
                {
                    continue;
                }
                var node = child;
                var layer = ReadLayer(child);
                if (layer == null)
                {
                    checks.Add(() => CheckRow(scene.HasChildren(node) ? PhasingFixItem.Group : PhasingFixItem.Object, top.NewName, NameOf(node), NameOf(node), node.Key,
                        "neither a phase group (_<n>_<name>) nor a layer (<phase>_<stage>_<name>); left as it is"));
                    continue;
                }
                var homes = groups.Where(g => g.Phase == layer.Phase).ToList();
                if (homes.Count != 1)
                {
                    unplaced.Add(layer);
                    var phaseText = layer.Phase.ToString(CultureInfo.InvariantCulture);
                    var phase = layer.Phase;
                    checks.Add(() => CheckRow(PhasingFixItem.Layer, top.NewName, NameOf(node), NameOf(node), node.Key, homes.Count == 0
                        ? "a layer of phase " + phaseText + ", but there is no group _" + Pad2(phase) + "_<name> to move it into; left as it is"
                        : "a layer of phase " + phaseText + ", and " + homes.Count.ToString(CultureInfo.InvariantCulture) + " groups have that phase ("
                          + string.Join(", ", homes.Select(g => g.NewName).ToArray()) + "); left as it is - move it by hand"));
                    continue;
                }
                layer.Order = layers.Count;
                layer.Group = homes[0];
                layer.Moved = true;
                layers.Add(layer);
            }
            foreach (var group in groups)
            {
                foreach (var child in scene.ChildrenOf(group.Node))
                {
                    var node = child;
                    var home = group;
                    var layer = ReadLayer(child);
                    if (layer == null)
                    {
                        checks.Add(() => CheckRow(scene.HasChildren(node) ? PhasingFixItem.Layer : PhasingFixItem.Object, home.NewName, NameOf(node), NameOf(node), node.Key,
                            "does not read as a layer (<phase>_<stage>[.<split>]_<name>); left as it is"));
                        continue;
                    }
                    layer.Order = layers.Count;
                    layer.Group = group;
                    layers.Add(layer);
                }
            }
            foreach (var layer in layers)
            {
                FixLayerText(layer, fixes, notes, top, checks);
            }

            // The phasing tree is the top node, its groups and layers, and the layers' objects; a corrected name that
            // a node outside it already has is not used (rule 9, for the top node and groups here).
            var phasingKeys = new HashSet<long> { top.Node.Key };
            phasingKeys.UnionWith(groups.Select(g => g.Node.Key));
            phasingKeys.UnionWith(layers.Select(l => l.Node.Key));
            phasingKeys.UnionWith(layers.SelectMany(l => scene.ChildrenOf(l.Node)).Select(n => n.Key));
            var outsideNames = new HashSet<string>(scene.All.Where(n => !phasingKeys.Contains(n.Key)).Select(NameOf), StringComparer.Ordinal);
            KeepOutsideNames(new NodeWork[] { top }.Concat(groups), outsideNames, top, checks);

            // Rule 6: stages set in the review.
            foreach (var layer in layers)
            {
                int stage;
                if (overrides.TryGetValue(layer.Node.Key, out stage) && stage >= 0 && stage != layer.Stage)
                {
                    layer.Stage = stage;
                    layer.StageOverridden = true;
                    layer.Why.Add("stage set to " + Pad2(stage) + " in the review");
                }
            }

            Renumber(layers);
            foreach (var layer in layers)
            {
                var split = layer.Split != null ? "." + layer.Split : string.Empty;
                layer.Id = Pad2(layer.Phase) + "_" + Pad2(layer.Stage) + split;
                layer.NewName = layer.Id + "_" + layer.Description;
                layer.Tag = "Ph" + layer.Phase.ToString(CultureInfo.InvariantCulture) + "_St" + Pad2(layer.Stage) + split;
                if (layer.Moved)
                {
                    layer.Why.Add("moved into " + layer.Group.NewName + ": it is stage " + layer.Stage.ToString(CultureInfo.InvariantCulture) + " of " + layer.Group.Description);
                }
            }
            KeepOutsideNames(layers, outsideNames, top, checks);

            // Rule 8: the objects.
            var objects = new List<ObjectWork>();
            foreach (var layer in layers)
            {
                foreach (var child in scene.ChildrenOf(layer.Node))
                {
                    var work = ReadObject(child, layer, layers, unplaced, fixes, notes, checks);
                    objects.Add(work);
                    if (scene.HasChildren(child))
                    {
                        checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key,
                            "has objects linked under it; its own name is corrected, the objects under it are left as they are"));
                    }
                }
            }

            // Rule 9: names used twice.
            KeepOutsideNames(objects.Where(o => o.Tagged), outsideNames, top, checks);
            NumberDuplicates(objects, outsideNames);
            var works = new List<NodeWork> { top };
            works.AddRange(groups);
            works.AddRange(layers);
            works.AddRange(objects);
            RevertClashes(scene, works, top, checks);

            // Rule 11: the review rows, in the reference order.
            var layerOrder = layers.OrderBy(l => l.Phase).ThenBy(l => l.Stage).ThenBy(l => SplitNumber(l.Split)).ToList();
            if (top.NewName != top.OldName)
            {
                plan.Rows.Add(ChangedRow(PhasingFixItem.TopLayer, string.Empty, top.OldName, top.NewName, top.Node.Key, top.Why));
                plan.NodeRenames.Add(new PhasingNodeRename { Key = top.Node.Key, Item = PhasingFixItem.TopLayer, OldName = top.OldName, NewName = top.NewName });
            }
            foreach (var group in groups.Where(g => g.NewName != g.OldName))
            {
                plan.Rows.Add(ChangedRow(PhasingFixItem.Group, top.NewName, group.OldName, group.NewName, group.Node.Key, group.Why));
                plan.NodeRenames.Add(new PhasingNodeRename { Key = group.Node.Key, Item = PhasingFixItem.Group, OldName = group.OldName, NewName = group.NewName });
            }
            foreach (var layer in layerOrder)
            {
                if (layer.NewName != layer.OldName || layer.Moved)
                {
                    plan.Rows.Add(ChangedRow(PhasingFixItem.Layer, layer.Group.NewName, layer.OldName, layer.NewName, layer.Node.Key, layer.Why));
                }
                if (layer.NewName != layer.OldName)
                {
                    plan.NodeRenames.Add(new PhasingNodeRename { Key = layer.Node.Key, Item = PhasingFixItem.Layer, OldName = layer.OldName, NewName = layer.NewName });
                }
                plan.Layers.Add(new PhasingLayerInfo
                {
                    Key = layer.Node.Key,
                    CurrentName = layer.OldName,
                    CorrectedName = layer.NewName,
                    Group = layer.Group.NewName,
                    Phase = layer.Phase,
                    WrittenStage = layer.OldStage,
                    Stage = layer.Stage,
                    Split = layer.Split,
                    Description = layer.Description,
                    Moved = layer.Moved,
                    StageOverridden = layer.StageOverridden
                });
            }
            var objectsByLayer = objects.ToLookup(o => o.Layer);
            foreach (var layer in layerOrder)
            {
                foreach (var work in objectsByLayer[layer].Where(o => o.NewName != o.OldName))
                {
                    plan.Rows.Add(ChangedRow(PhasingFixItem.Object, layer.NewName, work.OldName, work.NewName, work.Node.Key, work.Why));
                    plan.NodeRenames.Add(new PhasingNodeRename { Key = work.Node.Key, Item = PhasingFixItem.Object, OldName = work.OldName, NewName = work.NewName });
                }
            }
            foreach (var layer in layers.Where(l => l.Moved))
            {
                plan.NodeMoves.Add(new PhasingNodeMove
                {
                    Key = layer.Node.Key,
                    Name = layer.OldName,
                    FromParentKey = top.Node.Key,
                    ToParentKey = layer.Group.Node.Key,
                    ToParentName = layer.Group.OldName,
                    IntoGroupHead = layer.Group.Node.IsGroupHead,
                    IsGroupMember = layer.Node.IsGroupMember
                });
            }

            // Rule 10: the 3ds Max layers that carry the same names.
            var containers = new List<Container> { new Container(PhasingFixItem.TopLayer, top.OldName, top.NewName, string.Empty) };
            containers.AddRange(groups.Select(g => new Container(PhasingFixItem.Group, g.OldName, g.NewName, top.NewName)));
            containers.AddRange(layerOrder.Select(l => new Container(PhasingFixItem.Layer, l.OldName, l.NewName, l.Group.NewName)));
            PlanMaxLayers(plan, maxLayers, containers, layers.Where(l => l.Moved).ToList(), checks);

            foreach (var check in checks)
            {
                plan.Rows.Add(check());
            }
            AddTextNotes(plan, notes, fixes);
            return plan;
        }

        // Rule 4's notes: word fixes held back from a date or TBC, and word fixes that never settle.
        private static void AddTextNotes(PhasingFixPlan plan, TextFixNotes notes, List<PhasingWordFix> fixes)
        {
            foreach (var fix in notes.DateOrTbc)
            {
                plan.Notes.Add("The " + FixNames(new List<PhasingWordFix> { fix }) + " is not used where it would change a date or TBC.");
            }
            var sets = notes.Unsettled
                .GroupBy(u => string.Join(",", u.Fixes.Select(f => fixes.IndexOf(f).ToString(CultureInfo.InvariantCulture)).ToArray()), StringComparer.Ordinal);
            foreach (var set in sets)
            {
                var first = set.First();
                var count = set.Count();
                var one = first.Fixes.Count == 1;
                plan.Notes.Add("The " + FixNames(first.Fixes) + (one ? " changes " : " change ")
                    + (count == 1 ? first.Name : count.ToString(CultureInfo.InvariantCulture) + " names (the first is " + first.Name + ")")
                    + " again every time " + (one ? "it is" : "they are") + " used, so there " + (one ? "it is" : "they are")
                    + " used once only - check the word fixes.");
            }
        }

        // A word fix that would stop a name reading is not used on it, and the name is listed.
        private static void CheckUnreadable(NodeWork work, TextFix fix, TopWork top, List<Func<PhasingFixRow>> checks)
        {
            if (fix.Unreadable.Count == 0)
            {
                return;
            }
            var unused = fix.Unreadable;
            checks.Add(() => CheckRow(work.Item, PlaceOf(work, top), work.OldName, work.NewName, work.Node.Key,
                "the " + FixNames(unused) + " would leave the name unreadable, so " + (unused.Count == 1 ? "it is" : "they are") + " not used on it"));
        }

        private static bool ReadsAsGroup(string digits, string description)
        {
            var match = GroupPattern.Match("_" + digits + "_" + description);
            return match.Success && match.Groups[1].Value == digits && match.Groups[2].Value == description;
        }

        private static bool ReadsAsLayer(string id, string description)
        {
            var match = LayerPattern.Match(id + "_" + description);
            return match.Success && !match.Groups[3].Success && match.Groups[4].Value == description;
        }

        // Layers

        private static LayerWork ReadLayer(PhasingSceneNode node)
        {
            var name = NameOf(node);
            var match = LayerPattern.Match(name);
            int phase, stage, splitValue;
            if (!match.Success || !TryNumber(match.Groups[1].Value, out phase) || !TryNumber(match.Groups[2].Value, out stage)
                || (match.Groups[3].Success && !TryNumber(match.Groups[3].Value, out splitValue)))
            {
                return null;
            }
            var layer = new LayerWork
            {
                Node = node,
                OldName = name,
                Phase = phase,
                OldStage = stage,
                Stage = stage,
                OldSplit = match.Groups[3].Success ? match.Groups[3].Value : null
            };
            var phaseText = match.Groups[1].Value;
            if (phaseText != Pad2(phase))
            {
                layer.Why.Add("phase number written as two digits, like the other phases");
            }
            if (name[phaseText.Length] == '.')
            {
                layer.Why.Add("underscore after the phase number, not a full stop");
            }
            if (match.Groups[2].Value != Pad2(stage))
            {
                layer.Why.Add("stage number written as two digits");
            }
            layer.Rest = match.Groups[4].Value;
            return layer;
        }

        // Rule 4 for a layer that is fixed: the text after its ID, and the start date read from it.
        private static void FixLayerText(LayerWork layer, List<PhasingWordFix> fixes, TextFixNotes notes, TopWork top, List<Func<PhasingFixRow>> checks)
        {
            var id = Pad2(layer.Phase) + "_" + Pad2(layer.OldStage);
            var fix = FixText(layer.Rest, fixes, layer.Why, d => ReadsAsLayer(id, d), notes, layer.OldName);
            layer.Description = fix.Text;
            CheckUnreadable(layer, fix, top, checks);
            var date = DatePattern.Match(layer.Description);
            layer.Start = date.Success ? "20" + date.Groups[3].Value + "-" + date.Groups[2].Value + "-" + date.Groups[1].Value : NoDate;
        }

        // Rule 7: a stage with more than one layer gets .1, .2, ... by start date, then old split, then order;
        // a lone layer keeps its split (or none), unless the review moved it there: that split was its old stage's.
        private static void Renumber(List<LayerWork> layers)
        {
            var stages = new List<List<LayerWork>>();
            var byStage = new Dictionary<string, List<LayerWork>>(StringComparer.Ordinal);
            foreach (var layer in layers)
            {
                var key = layer.Phase.ToString(CultureInfo.InvariantCulture) + "|" + layer.Stage.ToString(CultureInfo.InvariantCulture);
                List<LayerWork> list;
                if (!byStage.TryGetValue(key, out list))
                {
                    list = new List<LayerWork>();
                    byStage[key] = list;
                    stages.Add(list);
                }
                list.Add(layer);
            }
            foreach (var stage in stages)
            {
                if (stage.Count == 1)
                {
                    stage[0].Split = stage[0].StageOverridden ? null : stage[0].OldSplit;
                    continue;
                }
                var list = stage.OrderBy(l => l.Start, StringComparer.Ordinal).ThenBy(l => SplitNumber(l.OldSplit)).ThenBy(l => l.Order).ToList();
                var oldIds = list.Select(l => Pad2(l.Phase) + "_" + Pad2(l.OldStage) + (l.OldSplit != null ? "." + l.OldSplit : string.Empty)).ToList();
                var repeated = new HashSet<string>(oldIds.Where((id, i) => oldIds.IndexOf(id) != i), StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    var layer = list[i];
                    layer.Split = (i + 1).ToString(CultureInfo.InvariantCulture);
                    var stageId = Pad2(layer.Phase) + "_" + Pad2(layer.Stage);
                    if (oldIds[i] == stageId + "." + layer.Split)
                    {
                        continue;
                    }
                    string reason;
                    if (repeated.Contains(oldIds[i]))
                    {
                        reason = "ID " + oldIds[i] + " was used more than once; stage " + stageId + " renumbered in start-date order";
                    }
                    else if (layer.OldSplit == null)
                    {
                        reason = "stage " + stageId + " has several layers, so this one gets a split number too; renumbered in start-date order";
                    }
                    else
                    {
                        reason = "stage " + stageId + " renumbered in start-date order";
                    }
                    layer.Why.Insert(0, reason);
                }
            }
        }

        // The layer a removal tag points at today, by the layers' current IDs: the exact ID (the first of several,
        // flagged); else, in the same stage, the lowest split when the tag gives none, or the unsplit layer when it does.
        private static LayerWork ResolveOld(List<LayerWork> layers, int phase, int stage, string split, out List<LayerWork> ambiguous)
        {
            ambiguous = null;
            var exact = layers.Where(l => l.Phase == phase && l.OldStage == stage && (l.OldSplit ?? string.Empty) == (split ?? string.Empty)).ToList();
            if (exact.Count > 0)
            {
                if (exact.Count > 1)
                {
                    ambiguous = exact;
                }
                return exact[0];
            }
            var sameStage = layers.Where(l => l.Phase == phase && l.OldStage == stage).ToList();
            if (sameStage.Count == 0)
            {
                return null;
            }
            if (split == null)
            {
                return sameStage.OrderBy(l => SplitNumber(l.OldSplit)).ThenBy(l => l.Order).First();
            }
            return sameStage.FirstOrDefault(l => l.OldSplit == null);
        }

        // Objects

        private static ObjectWork ReadObject(PhasingSceneNode node, LayerWork layer, List<LayerWork> layers, List<LayerWork> unplaced, List<PhasingWordFix> fixes,
            TextFixNotes notes, List<Func<PhasingFixRow>> checks)
        {
            var work = new ObjectWork { Node = node, OldName = NameOf(node), Layer = layer };
            work.NewName = work.OldName;
            var lead = LeadPattern.Match(work.OldName);
            if (!lead.Success)
            {
                checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key, "no Ph/St tag at the start of the name"));
                return work;
            }
            var kind = lead.Groups[4].Value.ToUpperInvariant();
            var body = work.OldName.Substring(lead.Length);
            var tail = string.Empty;
            var trail = kind == "IN" ? TrailPattern.Match(body) : Match.Empty;
            if (trail.Success)
            {
                body = body.Substring(0, trail.Index);
                var split = trail.Groups[3].Success ? trail.Groups[3].Value : null;
                var oldTag = "Ph" + NumberText(trail.Groups[1].Value) + "_St" + trail.Groups[2].Value + (split != null ? "." + split : string.Empty);
                List<LayerWork> ambiguous = null;
                int phase, stage;
                // "&", not "&&": both numbers are read, so both are set.
                var readable = TryNumber(trail.Groups[1].Value, out phase) & TryNumber(trail.Groups[2].Value, out stage);
                var target = readable ? ResolveOld(layers, phase, stage, split, out ambiguous) : null;
                if (target == null)
                {
                    tail = TrailPhaseZeros.Replace(trail.Value, "_Ph");
                    if (tail != trail.Value)
                    {
                        // "_Ph06_St40_RM": the tag between the leading "_" and the trailing "_RM".
                        work.Why.Add("removal tag " + trail.Value.Substring(1, trail.Value.Length - 4) + " written " + oldTag + ", like the other tags");
                    }
                    List<LayerWork> strays;
                    var stray = readable ? ResolveOld(unplaced, phase, stage, split, out strays) : null;
                    checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key, stray != null
                        ? "removal tag " + oldTag + " points at " + stray.OldName + ", which is left where it is (see its own row); tag kept"
                        : "removal tag " + oldTag + " names a stage that has no layer in this file; left as it is - needs the stage it comes out at"));
                }
                else
                {
                    tail = "_" + target.Tag + "_RM";
                    if (target.Tag != oldTag)
                    {
                        // The layer's current name with its first date dropped, as the reference words it.
                        var layerText = TrailingUnderscores.Replace(DatePattern.Replace(target.OldName, string.Empty, 1), string.Empty);
                        work.Why.Add("removal tag " + oldTag + " follows its layer " + layerText + " to its new ID " + target.Id);
                    }
                    if (ambiguous != null)
                    {
                        var candidates = ambiguous;
                        var pointedAt = target;
                        checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key,
                            "removal tag " + oldTag + " could mean any of " + candidates.Count.ToString(CultureInfo.InvariantCulture) + " layers ("
                            + string.Join(", ", candidates.Select(l => l.NewName).ToArray()) + "); pointed at the first, " + pointedAt.Id + " - confirm"));
                    }
                }
            }
            work.Lead = layer.Tag + "_" + kind;
            var newLead = work.Lead;
            var bodyFix = FixText(body, fixes, work.Why, b =>
            {
                var again = LeadPattern.Match(newLead + b);
                return again.Success && again.Length == newLead.Length;
            }, notes, work.OldName);
            work.Body = bodyFix.Text;
            CheckUnreadable(work, bodyFix, null, checks);
            if (lead.Value != work.Lead)
            {
                work.Why.Insert(0, "tag " + TagType.Replace(lead.Value, string.Empty) + " changed to its layer's " + layer.Tag);
            }
            work.Tail = tail;
            work.NewName = work.Lead + work.Body + tail;
            work.Tagged = true;
            return work;
        }

        // A corrected name used by a node outside the phasing tree is left as it was (and listed).
        private static void KeepOutsideNames(IEnumerable<NodeWork> works, HashSet<string> outsideNames, TopWork top, List<Func<PhasingFixRow>> checks)
        {
            foreach (var work in works.Where(w => w.NewName != w.OldName && outsideNames.Contains(w.NewName)).ToList())
            {
                var item = work;
                var clash = item.NewName;
                item.NewName = item.OldName;
                item.Kept = true;
                checks.Add(() => CheckRow(item.Item, PlaceOf(item, top), item.OldName, item.NewName, item.Node.Key,
                    "the corrected name " + clash + " is already used outside the phasing layers; left as it is"));
            }
        }

        // A corrected name used by an earlier phasing object gets _02, _03, ... on its description, skipping any
        // name already taken.
        private static void NumberDuplicates(List<ObjectWork> objects, HashSet<string> outsideNames)
        {
            var planned = new HashSet<string>(objects.Select(o => o.NewName), StringComparer.Ordinal);
            var numbered = new HashSet<string>(StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var work in objects.Where(o => o.Tagged && !o.Kept))
            {
                int count;
                seen.TryGetValue(work.NewName, out count);
                count++;
                seen[work.NewName] = count;
                if (count == 1)
                {
                    continue;
                }
                var name = Numbered(work, count);
                while (planned.Contains(name) || outsideNames.Contains(name) || numbered.Contains(name))
                {
                    name = Numbered(work, ++count);
                }
                numbered.Add(name);
                work.NewName = name;
                work.Why.Add("number added: another object on the same layer had the same name");
            }
        }

        // Last line of defence: a renamed node whose new name another node will also carry keeps its old name.
        private static void RevertClashes(Scene scene, List<NodeWork> works, TopWork top, List<Func<PhasingFixRow>> checks)
        {
            var finalNames = scene.All.ToDictionary(n => n.Key, NameOf);
            while (true)
            {
                foreach (var work in works)
                {
                    finalNames[work.Node.Key] = work.NewName;
                }
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var name in finalNames.Values)
                {
                    int count;
                    counts.TryGetValue(name, out count);
                    counts[name] = count + 1;
                }
                var clash = works.FirstOrDefault(w => w.NewName != w.OldName && counts[w.NewName] > 1);
                if (clash == null)
                {
                    return;
                }
                var name2 = clash.NewName;
                clash.NewName = clash.OldName;
                clash.Kept = true;
                var other = clash is ObjectWork ? "another object" : "another node";
                checks.Add(() => CheckRow(clash.Item, PlaceOf(clash, top), clash.OldName, clash.NewName, clash.Node.Key,
                    "the corrected name " + name2 + " would also be the name of " + other + "; left as it is"));
            }
        }

        // The corrected name of the layer or group a node sits in; empty for the top node.
        private static string PlaceOf(NodeWork work, TopWork top)
        {
            var asObject = work as ObjectWork;
            if (asObject != null)
            {
                return asObject.Layer.NewName;
            }
            var asLayer = work as LayerWork;
            if (asLayer != null)
            {
                return asLayer.Group.NewName;
            }
            return work is GroupWork ? top.NewName : string.Empty;
        }

        private static string Numbered(ObjectWork work, int count)
        {
            return work.Lead + work.Body + "_" + count.ToString("00", CultureInfo.InvariantCulture) + work.Tail;
        }

        // 3ds Max layers

        private static void PlanMaxLayers(PhasingFixPlan plan, IEnumerable<PhasingMaxLayer> maxLayers, List<Container> containers,
            List<LayerWork> moved, List<Func<PhasingFixRow>> checks)
        {
            // 3ds Max layer names ignore case, so a Max layer goes with the nodes of its name in any case.
            var byName = new Dictionary<string, PhasingMaxLayer>(StringComparer.OrdinalIgnoreCase);
            var order = new List<PhasingMaxLayer>();
            foreach (var maxLayer in maxLayers ?? Enumerable.Empty<PhasingMaxLayer>())
            {
                if (maxLayer != null && maxLayer.Name != null && !byName.ContainsKey(maxLayer.Name))
                {
                    byName[maxLayer.Name] = maxLayer;
                    order.Add(maxLayer);
                }
            }

            // A moved layer's Max layer goes under its group's Max layer, when both exist (current names).
            foreach (var layer in moved)
            {
                PhasingMaxLayer own, parent;
                if (byName.TryGetValue(layer.OldName, out own) && byName.TryGetValue(layer.Group.OldName, out parent)
                    && !string.Equals(own.ParentName, parent.Name, StringComparison.OrdinalIgnoreCase))
                {
                    plan.MaxLayerMoves.Add(new PhasingMaxLayerMove { Name = own.Name, NewParentName = parent.Name });
                }
            }

            // A Max layer named like a top node, group or layer that is renamed is renamed the same way, unless the
            // nodes that share its name are going different ways.
            var wanted = new List<KeyValuePair<Container, PhasingMaxLayer>>();
            foreach (var sameName in containers.GroupBy(c => c.OldName, StringComparer.OrdinalIgnoreCase))
            {
                PhasingMaxLayer maxLayer;
                if (!byName.TryGetValue(sameName.Key, out maxLayer) || sameName.All(c => c.NewName == c.OldName))
                {
                    continue;
                }
                var newNames = sameName.Select(c => c.NewName).Distinct(StringComparer.Ordinal).ToList();
                var first = sameName.First();
                var layerName = maxLayer.Name;
                if (newNames.Count > 1)
                {
                    checks.Add(() => CheckRow(first.Item, first.LayerText, first.OldName, first.OldName, null,
                        "the Max layer " + layerName + " has the name of " + sameName.Count().ToString(CultureInfo.InvariantCulture)
                        + " nodes that get different names (" + string.Join(", ", newNames.ToArray()) + "); the Max layer is left as it is"));
                    continue;
                }
                if (newNames[0] != maxLayer.Name)
                {
                    wanted.Add(new KeyValuePair<Container, PhasingMaxLayer>(first, maxLayer));
                }
            }

            // Max layer names must stay unique (ignoring case); a rename whose new name stays taken is dropped.
            var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = true;
            while (changed)
            {
                changed = false;
                var renaming = wanted.Where(w => !dropped.Contains(w.Value.Name)).ToList();
                var renamedAway = new HashSet<string>(renaming.Select(w => w.Value.Name), StringComparer.OrdinalIgnoreCase);
                var taken = new HashSet<string>(order.Where(l => !renamedAway.Contains(l.Name)).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var rename in renaming)
                {
                    if (!taken.Add(rename.Key.NewName))
                    {
                        dropped.Add(rename.Value.Name);
                        var container = rename.Key;
                        var layerName = rename.Value.Name;
                        checks.Add(() => CheckRow(container.Item, container.LayerText, container.OldName, container.OldName, null,
                            "the Max layer " + layerName + " cannot become " + container.NewName + ": another Max layer already has that name; the Max layer is left as it is"));
                        changed = true;
                        break;
                    }
                }
            }
            foreach (var rename in wanted.Where(w => !dropped.Contains(w.Value.Name)))
            {
                plan.MaxLayerRenames.Add(new PhasingMaxLayerRename { OldName = rename.Value.Name, NewName = rename.Key.NewName, Item = rename.Key.Item });
            }
        }

        // Helpers

        private static PhasingFixRow ChangedRow(PhasingFixItem item, string layer, string current, string corrected, long key, IEnumerable<string> why)
        {
            return new PhasingFixRow
            {
                Status = PhasingFixStatus.Changed,
                Item = item,
                LayerItSitsIn = layer,
                CurrentName = current,
                CorrectedName = corrected,
                WhatChanged = string.Join("; ", why.Distinct(StringComparer.Ordinal).ToArray()),
                NodeKey = key
            };
        }

        private static PhasingFixRow CheckRow(PhasingFixItem item, string layer, string current, string corrected, long? key, string note)
        {
            return new PhasingFixRow
            {
                Status = PhasingFixStatus.Check,
                Item = item,
                LayerItSitsIn = layer,
                CurrentName = current,
                CorrectedName = corrected,
                WhatChanged = note,
                NodeKey = key
            };
        }

        private static string NameOf(PhasingSceneNode node)
        {
            return node.Name ?? string.Empty;
        }

        private static string Pad2(int value)
        {
            return value.ToString("00", CultureInfo.InvariantCulture);
        }

        private static int SplitNumber(string split)
        {
            int value;
            return split != null && TryNumber(split, out value) ? value : 0;
        }

        private static bool TryNumber(string digits, out int value)
        {
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        // The digits as a number would print them: "06" gives "6", "0" stays "0".
        private static string NumberText(string digits)
        {
            var trimmed = digits.TrimStart('0');
            return trimmed.Length > 0 ? trimmed : "0";
        }

        /// <summary>A node of the phasing tree the fixer may rename.</summary>
        private abstract class NodeWork
        {
            public PhasingSceneNode Node;
            public string OldName;
            public string NewName;
            public readonly List<string> Why = new List<string>();
            /// <summary>Left with its current name because the corrected one clashes.</summary>
            public bool Kept;

            public abstract PhasingFixItem Item { get; }
        }

        private sealed class TopWork : NodeWork
        {
            public override PhasingFixItem Item
            {
                get { return PhasingFixItem.TopLayer; }
            }
        }

        private sealed class GroupWork : NodeWork
        {
            public int Phase;
            public string Description;

            public override PhasingFixItem Item
            {
                get { return PhasingFixItem.Group; }
            }
        }

        private sealed class LayerWork : NodeWork
        {
            public int Phase;
            public int OldStage;
            public string OldSplit;
            public int Stage;
            public string Split;
            public bool StageOverridden;
            /// <summary>The text after the ID, as the name has it.</summary>
            public string Rest;
            /// <summary>The corrected text after the ID.</summary>
            public string Description;
            /// <summary>"20YY-MM-DD" from the first date, or NoDate.</summary>
            public string Start;
            public int Order;
            public GroupWork Group;
            public bool Moved;
            public string Id;
            /// <summary>The object tag form of the new ID, e.g. Ph2_St05.1.</summary>
            public string Tag;

            public override PhasingFixItem Item
            {
                get { return PhasingFixItem.Layer; }
            }
        }

        private sealed class ObjectWork : NodeWork
        {
            public LayerWork Layer;
            public string Lead;
            public string Body;
            public string Tail;
            /// <summary>Carries a leading Ph/St tag, so the fixer may rename it.</summary>
            public bool Tagged;

            public override PhasingFixItem Item
            {
                get { return PhasingFixItem.Object; }
            }
        }

        private sealed class TextFix
        {
            public string Text;
            /// <summary>Word fixes not used on this text because it would no longer read.</summary>
            public List<PhasingWordFix> Unreadable;
        }

        /// <summary>What the text fix noticed across the plan, for its notes.</summary>
        private sealed class TextFixNotes
        {
            /// <summary>Word fixes held back somewhere because they would change a date or TBC.</summary>
            public readonly List<PhasingWordFix> DateOrTbc = new List<PhasingWordFix>();
            public readonly List<UnsettledText> Unsettled = new List<UnsettledText>();
        }

        /// <summary>A name the word fixes kept changing, and the fixes still changing it.</summary>
        private sealed class UnsettledText
        {
            public string Name;
            public List<PhasingWordFix> Fixes;
        }

        private sealed class Container
        {
            public readonly PhasingFixItem Item;
            public readonly string OldName;
            public readonly string NewName;
            public readonly string LayerText;

            public Container(PhasingFixItem item, string oldName, string newName, string layerText)
            {
                Item = item;
                OldName = oldName;
                NewName = newName;
                LayerText = layerText;
            }
        }

        private sealed class Scene
        {
            public readonly List<PhasingSceneNode> All = new List<PhasingSceneNode>();
            public readonly List<PhasingSceneNode> Roots;
            private readonly Dictionary<long, List<PhasingSceneNode>> children = new Dictionary<long, List<PhasingSceneNode>>();
            private static readonly List<PhasingSceneNode> None = new List<PhasingSceneNode>();

            public Scene(IEnumerable<PhasingSceneNode> nodes)
            {
                var keys = new HashSet<long>();
                foreach (var node in nodes ?? Enumerable.Empty<PhasingSceneNode>())
                {
                    if (node != null && keys.Add(node.Key))
                    {
                        All.Add(node);
                    }
                }
                var position = new Dictionary<long, int>();
                for (int i = 0; i < All.Count; i++)
                {
                    position[All[i].Key] = i;
                }
                Roots = All.Where(n => !n.ParentKey.HasValue).OrderBy(n => n.ChildIndex).ThenBy(n => position[n.Key]).ToList();
                foreach (var siblings in All.Where(n => n.ParentKey.HasValue).GroupBy(n => n.ParentKey.Value))
                {
                    children[siblings.Key] = siblings.OrderBy(n => n.ChildIndex).ThenBy(n => position[n.Key]).ToList();
                }
            }

            public List<PhasingSceneNode> ChildrenOf(PhasingSceneNode node)
            {
                List<PhasingSceneNode> list;
                return children.TryGetValue(node.Key, out list) ? list : None;
            }

            public bool HasChildren(PhasingSceneNode node)
            {
                return node.HasChildren || children.ContainsKey(node.Key);
            }
        }
    }
}
