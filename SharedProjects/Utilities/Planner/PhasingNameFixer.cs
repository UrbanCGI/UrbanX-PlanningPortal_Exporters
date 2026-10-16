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
    ///   - the groups' children are the layers, &lt;PP&gt;_&lt;SS&gt;[_&lt;split&gt;]_&lt;name&gt;: the phase is written with two
    ///     digits and an underscore, a stage with several layers is split _1, _2, ... in start-date order, and the
    ///     review can set any layer's stage;
    ///   - the layers' children are the objects: the leading Ph/St tag becomes the layer's, and a trailing
    ///     removal tag follows the layer it points at to its new ID;
    ///   - names get the project's word fixes (again until they settle) and lose their spaces; dates and TBC are
    ///     never changed, and a word fix that would leave a name unreadable is not used on it.
    /// A sub-stage is written after an underscore (02_05_1, Ph2_St05_1): Unreal's Datasmith import turns every hyphen in
    /// an object's name into an underscore, so the names must not need one. The old spellings with a hyphen (02_05-1,
    /// Ph2_St05-1, released on 2026-10-14) and with a full stop (02_05.1, Ph2_St05.1) read as the same stage and are
    /// written with the underscore; no other full stop is touched. A layer of one number has no room for a sub-stage in
    /// the convention, so one written the old way (06-2_..., 06.2_... when an object carries that very sub-stage) gets its
    /// phase in front, the phase its objects' tags with its stage carry (06-2_Hoarding becomes 03_06_2_Hoarding when they
    /// all say phase 3; with no such tag, or tags of several phases, it is left as it is and listed). Every other hyphen
    /// in an object's name becomes an underscore too; the dates in layer names keep theirs (Datasmith leaves layer names
    /// alone). A corrected object name that another object already has, and keeps, is not used (the object is listed).
    /// A layer ID is read as the Planner reads it (a sub-stage after several underscores or a space, "02_01__2_...", is
    /// sub-stage 2 to both); one the Planner reads otherwise, or an old form with a number of three digits or more, is
    /// left as it is, and so is a layer whose corrected name would not read back as its new ID, each with its objects.
    /// Since the hyphen, and then the underscore, came in, this is no longer the reference clean-up rule for rule: the
    /// reference writes the full stop. Anything that does not read is left as it is and listed for a person to check. A
    /// shape (a spline such as a RailClone path) that nothing exported is linked under never reaches the export, so it is
    /// not listed, renamed or counted; one that holds something exported is an empty node in the export, and is treated
    /// like a helper.
    /// </summary>
    public static class PhasingNameFixer
    {
        public const string NothingToDo = "No top-level node has \"phasing\" in its name, so there are no phasing names to fix.";
        /// <summary>The reason given when a sub-stage written with a full stop gets the underscore.</summary>
        public const string FullStopReason = "underscore before the sub-stage number, not a full stop";
        /// <summary>The reason given when a sub-stage written with a hyphen gets the underscore.</summary>
        public const string HyphenReason = "underscore before the sub-stage number, not a hyphen";
        /// <summary>The reason given when the other hyphens in an object's name become underscores.</summary>
        public const string DatasmithReason = "hyphens replaced with underscores, as Unreal's Datasmith import would";

        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        // [0-9] rather than \d (.NET's \d matches every Unicode digit), \z rather than $ (.NET's $ also matches
        // before a final line feed), and JavaScript's own sets for \s and . (.NET's \s leaves out U+FEFF but takes
        // U+0085, and its . also takes \r, U+2028 and U+2029), so these read exactly what the reference's
        // JavaScript patterns read.
        private const string Space = PlannerNaming.JavaScriptSpace;
        private const string NotLineEnd = @"[^\n\r\u2028\u2029]";
        private static readonly Regex GroupPattern = new Regex(@"^_([0-9]+)_(" + NotLineEnd + @"+)\z", Options);
        // <phase>[._]<stage>[_<split>]_<name>, the split also after a hyphen or a full stop as it was written before (then
        // followed by an underscore). A split written the current way is read as the Planner reads a third number (its
        // LAYER_STAGE, PlannerNaming.LayerStage): one or two digits after a run of underscores or spaces, here one that
        // starts with the underscore, then an underscore or a space, so "02_01__2_...", "02_01_ 2_..." and "02_01_2 ..."
        // are sub-stage 2 to the Planner and to the fix alike (which writes them 02_01_2_...). A date after the stage
        // ("02_01_17-08-26_...") stays in the name, and an ID that ends in its third number with nothing after it
        // ("02_01_2", which the Planner reads as sub-stage 2 with no name) is not read as a layer at all. An old split takes
        // any number of digits, as the Planner reads it; ReadLayerName leaves one of three digits or more as it is. Groups:
        // phase, after (the character after the phase), stage, sep (the characters before the split), split, gap (the
        // character after a split written the current way), rest.
        private static readonly Regex LayerPattern = new Regex(@"^(?<phase>[0-9]+)(?<after>[._])(?<stage>[0-9]+)(?:(?<sep>[.-])(?<split>[0-9]+)_|(?<sep>_[_ ]*)(?<split>[0-9]{1,2})(?<gap>[_ ])|_(?![_ ]*[0-9]{1,2}[_ ]*\z))(?<rest>" + NotLineEnd + @"+)\z", Options);
        // A layer of one number whose sub-stage is written an old way, "06-2_<name>" or "06.2_<name>": the convention has
        // no room for it, so its phase goes in front (OneNumberPhase). LayerPattern reads the full stop form too, as a full
        // stop after the phase ("6.06_..."); the objects tell the two apart (ReadOneNumber). Each number takes any digits,
        // as the Planner reads them; ReadLayerName leaves one of three digits or more as it is. Groups: stage, sep, split,
        // rest.
        private static readonly Regex OneNumberPattern = new Regex(@"^(?<stage>[0-9]+)(?<sep>[.-])(?<split>[0-9]+)_(?<rest>" + NotLineEnd + @"+)\z", Options);
        // The first DD-MM-YY token of a layer's description: its start date.
        private static readonly Regex DatePattern = new Regex(@"(?:^|_)([0-9]{2})-([0-9]{2})-([0-9]{2})(?=_|\z)", Options);
        // Groups: 1 phase, 2 stage, 3 the character before the split ("_", or the old "-" or "."), 4 split, 5 type.
        private static readonly Regex LeadPattern = new Regex(@"^Ph([0-9]+)_St([0-9]+)(?:([_.-])([0-9]+))?_(IN|RM)(?=_|\z)", OptionsIgnoreCase);
        // Groups: 1 phase, 2 stage, 3 the character before the split, 4 split.
        private static readonly Regex TrailPattern = new Regex(@"_Ph([0-9]+)_St([0-9]+)(?:([_.-])([0-9]+))?_RM\z", OptionsIgnoreCase);
        // A tag closing a text with its sub-stage written an old way ("..._Ph3_St07.2_IN", "..._Ph3_St07-2_IN"): the
        // character before the sub-stage becomes an underscore. Groups: 1 the tag up to the stage, 2 that character.
        private static readonly Regex ClosingTagOldSplit = new Regex(@"(_Ph[0-9]+_St[0-9]+)([.-])(?=[0-9]+_(?:IN|RM)\z)", OptionsIgnoreCase);
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
        /// Reads a name as a layer the way the fixer does, <c>&lt;PP&gt;_&lt;SS&gt;[_&lt;split&gt;]_&lt;name&gt;</c> (the split also
        /// after a hyphen or a full stop, the old forms), or returns null. Without its objects, a layer of one number with
        /// an old sub-stage ("06-2_...") does not read (see <see cref="ReadLayerId(string, IEnumerable{PlannerMeshName})"/>).
        /// Where the node sits is not looked at: in the phasing hierarchy a layer is a child of a phase group
        /// (<see cref="TryReadPhaseGroup"/>).
        /// </summary>
        public static PhasingLayerId ReadLayerId(string name)
        {
            return ReadLayerId(name, null);
        }

        /// <summary>
        /// As <see cref="ReadLayerId(string)"/>, with the layer's objects (its tagged children that the export keeps as
        /// geometry): a layer of one number with a sub-stage written an old way ("06-2_...", or "06.2_..." when an object
        /// carries that very sub-stage) reads with the phase its objects' tags with its stage carry
        /// (<see cref="OneNumberPhase"/>), and not at all when they give none or several. Nor does a name the fixer would
        /// leave as it is for another reason (<see cref="ReadLayerName"/>).
        /// </summary>
        public static PhasingLayerId ReadLayerId(string name, IEnumerable<PlannerMeshName> objects)
        {
            return ReadLayerName(name, objects).Id;
        }

        /// <summary>
        /// Reads a name as a layer, as <see cref="ReadLayerId(string, IEnumerable{PlannerMeshName})"/> does, with what the
        /// fixer needs to correct it, or why a name that reads as a layer ID is left as it is (<see cref="LayerRead.Left"/>),
        /// so that the fixer's Check lines and the naming check give the same reasons:
        ///   - one number with an old sub-stage whose objects give it no phase, or several (<see cref="OneNumberPhase"/>);
        ///   - an old form with a number of three digits or more ("02_02-123_...", "06-123_...", "100-2_..."): the
        ///     underscore form writes each number with one or two digits, and "02_02_123_..." would be stage 2 named
        ///     "123_..." to the Planner, so converting it would lose the sub-stage;
        ///   - an ID written the current way that the Planner reads otherwise than the fix (a number of three digits,
        ///     "02_123_..." being stage 2 of one number to the Planner): retagging the objects to the fix's reading would
        ///     part them from their layer in the Planner. A full stop after the phase ("6.06_...") is a slip the fix
        ///     corrects, and the Planner's reading of it is not asked.
        /// </summary>
        internal static LayerRead ReadLayerName(string name, IEnumerable<PlannerMeshName> objects)
        {
            var read = new LayerRead();
            var one = ReadOneNumber(name, objects);
            if (one != null)
            {
                read.One = one;
                if (one.StageText.Length > 2 || one.SplitText.Length > 2 || one.Phase > 99)
                {
                    read.Left = LayerLeft.LongNumber;
                }
                else if (!one.Phase.HasValue)
                {
                    read.Left = one.Phases.Count == 0 ? LayerLeft.NoPhase : LayerLeft.SeveralPhases;
                }
                else
                {
                    read.Id = new PhasingLayerId { Phase = one.Phase.Value, Stage = one.Stage, Split = one.Split, SplitForm = one.Form, OneNumber = true };
                }
                return read;
            }
            var match = LayerPattern.Match(name ?? string.Empty);
            int phase, stage, split = 0;
            if (!match.Success || !TryNumber(match.Groups["phase"].Value, out phase) || !TryNumber(match.Groups["stage"].Value, out stage)
                || (match.Groups["split"].Success && !TryNumber(match.Groups["split"].Value, out split)))
            {
                return read;
            }
            read.Match = match;
            var reading = new PhasingLayerId
            {
                Phase = phase,
                Stage = stage,
                Split = match.Groups["split"].Success ? split : (int?)null,
                SplitForm = FormOf(match.Groups["sep"])
            };
            read.Reading = reading;
            if (reading.SplitForm != PlannerLegacyForm.None
                && (match.Groups["phase"].Length > 2 || match.Groups["stage"].Length > 2 || match.Groups["split"].Length > 2))
            {
                read.Left = LayerLeft.LongNumber;
                return read;
            }
            if (match.Groups["after"].Value == "_")
            {
                var planner = PlannerNaming.ParseLayerName(name);
                if (planner == null || planner.Phase != phase || planner.Stage != stage || planner.SubStage != reading.Split)
                {
                    read.Left = LayerLeft.PlannerReadsOtherwise;
                    read.Planner = planner;
                    return read;
                }
            }
            read.Id = reading;
            return read;
        }

        /// <summary>
        /// The phase a layer of one number with an old sub-stage takes from its objects, to go in front of its ID, as the
        /// convention cannot write a sub-stage in one number ("06-2_Hoarding" holding objects tagged Ph3_St06_2 becomes
        /// "03_06_2_Hoarding"): the phase of its objects' leading tags with its stage (sub-stages as
        /// <see cref="PlannerNaming.SameStage"/>), or, when no leading tag has its stage, of their trailing tags with it, as
        /// the Planner looks first at the leading tags. The phase when they all agree; null when no tag has its stage or
        /// they carry several phases, which <paramref name="phases"/> lists, lowest first.
        /// </summary>
        public static int? OneNumberPhase(int stage, int? subStage, IEnumerable<PlannerMeshName> objects, out List<int> phases)
        {
            var list = (objects ?? Enumerable.Empty<PlannerMeshName>()).Where(o => o != null).ToList();
            Func<PlannerMeshSegment, bool> hasStage = s => s != null && PlannerNaming.SameStage(s.Stage, s.SubStage, stage, subStage);
            phases = list.Select(o => o.Lead).Where(hasStage).Select(s => s.Phase).Distinct().OrderBy(p => p).ToList();
            if (phases.Count == 0)
            {
                phases = list.Select(o => o.Trail).Where(hasStage).Select(s => s.Phase).Distinct().OrderBy(p => p).ToList();
            }
            return phases.Count == 1 ? phases[0] : (int?)null;
        }

        /// <summary>
        /// Reads a name as a layer of one number with a sub-stage written an old way, with its objects' phase, or returns
        /// null when it is not one. "06-2_..." always is; "06.2_..." is only when one of the objects carries that very
        /// sub-stage (a tag of stage 6, sub-stage 2, in any spelling): otherwise it is read as before, as a layer whose
        /// phase is followed by a full stop ("6.02_...", phase 6, stage 2), which the reference clean-up corrected.
        /// </summary>
        internal static OneNumberLayer ReadOneNumber(string name, IEnumerable<PlannerMeshName> objects)
        {
            var match = OneNumberPattern.Match(name ?? string.Empty);
            int stage, split;
            if (!match.Success || !TryNumber(match.Groups["stage"].Value, out stage) || !TryNumber(match.Groups["split"].Value, out split))
            {
                return null;
            }
            var list = (objects ?? Enumerable.Empty<PlannerMeshName>()).Where(o => o != null).ToList();
            var form = FormOf(match.Groups["sep"]);
            if (form == PlannerLegacyForm.Dot && !list.Any(o => new[] { o.Lead, o.Trail }.Any(s => s != null && s.Stage == stage && s.SubStage == split)))
            {
                return null;
            }
            List<int> phases;
            var phase = OneNumberPhase(stage, split, list, out phases);
            return new OneNumberLayer
            {
                StageText = match.Groups["stage"].Value,
                Stage = stage,
                SplitText = match.Groups["split"].Value,
                Split = split,
                Form = form,
                Rest = match.Groups["rest"].Value,
                Phase = phase,
                Phases = phases
            };
        }

        /// <summary>The reason a sub-stage written an old way gets the underscore.</summary>
        public static string SubStageReason(PlannerLegacyForm form)
        {
            return form == PlannerLegacyForm.Dot ? FullStopReason : HyphenReason;
        }

        // The old form a split is written in: after a full stop or a hyphen; none after the underscore or with no split.
        private static PlannerLegacyForm FormOf(Group separator)
        {
            if (!separator.Success)
            {
                return PlannerLegacyForm.None;
            }
            return separator.Value == "." ? PlannerLegacyForm.Dot : separator.Value == "-" ? PlannerLegacyForm.Hyphen : PlannerLegacyForm.None;
        }

        /// <summary>Reads a name as a phase group, <c>_&lt;n&gt;_&lt;name&gt;</c>, giving its phase.</summary>
        public static bool TryReadPhaseGroup(string name, out int phase)
        {
            phase = 0;
            var match = GroupPattern.Match(name ?? string.Empty);
            return match.Success && TryNumber(match.Groups[1].Value, out phase);
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

        // "a", "a and b", "a, b and c".
        private static string JoinWithAnd(IList<string> parts)
        {
            if (parts.Count < 2)
            {
                return string.Join(string.Empty, parts.ToArray());
            }
            return string.Join(", ", parts.Take(parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
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
            var maxLayerList = (maxLayers ?? Enumerable.Empty<PhasingMaxLayer>()).ToList();
            var checks = new List<Func<PhasingFixRow>>();
            var notes = new TextFixNotes();

            // Rule 1: the phasing top node. Shapes the export leaves out (splines such as RailClone paths) are not
            // listed, renamed or counted anywhere: the Planner never sees them.
            var tops = scene.Roots.Where(n => !scene.LeftOut(n) && NameOf(n).IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
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
            plan.Counts = Count(scene, top.Node, maxLayerList);
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
                if (scene.LeftOut(child) || !match.Success || !TryNumber(match.Groups[1].Value, out phase))
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
                if (groupKeys.Contains(child.Key) || scene.LeftOut(child))
                {
                    continue;
                }
                var node = child;
                string unread;
                var layer = ReadLayer(child, scene, out unread);
                if (layer == null)
                {
                    checks.Add(() => CheckRow(unread != null ? PhasingFixItem.Layer : scene.HasExportedChildren(node) ? PhasingFixItem.Group : PhasingFixItem.Object,
                        top.NewName, NameOf(node), NameOf(node), node.Key,
                        unread ?? "neither a phase group (_<n>_<name>) nor a layer (<phase>_<stage>_<name>); left as it is"));
                    if (unread != null)
                    {
                        CheckLeftObjects(node, scene, checks);
                    }
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
                    CheckLeftObjects(node, scene, checks);
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
                    if (scene.LeftOut(child))
                    {
                        continue;
                    }
                    var node = child;
                    var home = group;
                    string unread;
                    var layer = ReadLayer(child, scene, out unread);
                    if (layer == null)
                    {
                        checks.Add(() => CheckRow(unread != null || scene.HasExportedChildren(node) ? PhasingFixItem.Layer : PhasingFixItem.Object, home.NewName, NameOf(node), NameOf(node), node.Key,
                            unread ?? "does not read as a layer (<phase>_<stage>[_<split>]_<name>); left as it is"));
                        if (unread != null)
                        {
                            CheckLeftObjects(node, scene, checks);
                        }
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

            // Rule 6: stages set in the review, 0 to 99 as the review takes them (a stage of three digits would not read as
            // one in an ID, "02_100_..." being stage 2 of one number to the Planner).
            foreach (var layer in layers)
            {
                int stage;
                if (overrides.TryGetValue(layer.Node.Key, out stage) && stage >= 0 && stage <= 99 && stage != layer.Stage)
                {
                    layer.Stage = stage;
                    layer.StageOverridden = true;
                    layer.Why.Add("stage set to " + Pad2(stage) + " in the review");
                }
            }

            Renumber(layers);
            foreach (var layer in layers)
            {
                var split = layer.Split != null ? "_" + layer.Split : string.Empty;
                layer.Id = Pad2(layer.Phase) + "_" + Pad2(layer.Stage) + split;
                layer.NewName = layer.Id + "_" + layer.Description;
                layer.Tag = "Ph" + layer.Phase.ToString(CultureInfo.InvariantCulture) + "_St" + Pad2(layer.Stage) + split;
                if (layer.OldSplitForm != PlannerLegacyForm.None && layer.Split != null)
                {
                    layer.Why.Add(SubStageReason(layer.OldSplitForm));
                }
                if (layer.Moved)
                {
                    layer.Why.Add("moved into " + layer.Group.NewName + ": it is stage " + layer.Stage.ToString(CultureInfo.InvariantCulture) + " of " + layer.Group.Description);
                }
            }
            // Last line of defence for the IDs: each corrected name must read back, to the fix and to the Planner, as the
            // phase, stage and split it was given, or its objects, retagged to that ID, would part from it in the Planner
            // (a split of three digits, in a stage of a hundred layers, would read as part of the name). Such a layer is left
            // as it is, with its objects, like one that cannot be moved into its group, and listed.
            foreach (var layer in layers.Where(l => !ReadsBackAs(l.NewName, l.Phase, l.Stage, l.Split)).ToList())
            {
                var item = layer;
                var wanted = item.NewName;
                var place = item.Moved ? top.NewName : item.Group.NewName;
                layers.Remove(item);
                unplaced.Add(item);
                item.NewName = item.OldName;
                checks.Add(() => CheckRow(PhasingFixItem.Layer, place, item.OldName, item.OldName, item.Node.Key,
                    "the corrected name " + wanted + " would not read back as " + item.Id + ", to the Planner (" + PlannerIdText(PlannerNaming.ParseLayerName(wanted))
                    + ") or to the fix; left as it is - rename it by hand"));
                CheckLeftObjects(item.Node, scene, checks);
            }
            KeepOutsideNames(layers, outsideNames, top, checks);

            // Rule 8: the objects (a shape the export leaves out is not one).
            var objects = new List<ObjectWork>();
            var outside = new OutsideLayers(scene, layers.Concat(unplaced).Select(l => l.Node.Key));
            foreach (var layer in layers)
            {
                foreach (var child in scene.ChildrenOf(layer.Node))
                {
                    if (scene.LeftOut(child))
                    {
                        continue;
                    }
                    var work = ReadObject(child, layer, layers, unplaced, outside, fixes, notes, checks);
                    objects.Add(work);
                    if (scene.HasExportedChildren(child))
                    {
                        checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key,
                            "has objects linked under it; its own name is corrected, the objects under it are left as they are"));
                    }
                }
            }

            // Rule 9: names used twice.
            KeepOutsideNames(objects.Where(o => o.Tagged), outsideNames, top, checks);
            KeepTakenNames(objects, top, checks);
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
            plan.LeftMisfiled.UnionWith(objects.Where(o => o.Misfiled && o.NewName == o.OldName).Select(o => o.OldName));
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
            var containers = new List<Container> { new Container(PhasingFixItem.TopLayer, top.Node.Key, top.OldName, top.NewName, string.Empty) };
            containers.AddRange(groups.Select(g => new Container(PhasingFixItem.Group, g.Node.Key, g.OldName, g.NewName, top.NewName)));
            containers.AddRange(layerOrder.Select(l => new Container(PhasingFixItem.Layer, l.Node.Key, l.OldName, l.NewName, l.Group.NewName)));
            PlanMaxLayers(plan, maxLayerList, containers, layers.Where(l => l.Moved).ToList(), checks);

            foreach (var check in checks)
            {
                plan.Rows.Add(check());
            }
            AddTextNotes(plan, notes, fixes);
            return plan;
        }

        // The counts for the layer / activity matrix: the Max layers in the top layer's tree, and the helper nodes and
        // tagged objects under the top node at any depth (the top layer and the top node themselves not counted). The
        // top layer is the Max layer named like the top node (in any capitals), else the first top-level Max layer with
        // "phasing" in its name. A shape the export leaves out is not counted at all; one that holds something exported
        // is an empty node in the export, and counts as a helper.
        private static PhasingCounts Count(Scene scene, PhasingSceneNode top, List<PhasingMaxLayer> maxLayers)
        {
            var counts = new PhasingCounts { TopName = NameOf(top) };
            var helpers = new List<string>();
            var seen = new HashSet<long> { top.Key };
            // Depth first, in the scene's order.
            var pending = new Stack<PhasingSceneNode>(Enumerable.Reverse(scene.ChildrenOf(top)));
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!seen.Add(node.Key))
                {
                    continue;
                }
                if (scene.LeftOut(node))
                {
                    // Not exported: nothing under it is either.
                    continue;
                }
                if (node.IsHelper || node.IsShape)
                {
                    helpers.Add(NameOf(node));
                }
                else if (PlannerNaming.ParseMeshName(NameOf(node)) != null)
                {
                    counts.TaggedObjects++;
                }
                foreach (var child in Enumerable.Reverse(scene.ChildrenOf(node)))
                {
                    pending.Push(child);
                }
            }
            counts.Helpers = helpers.Count;

            // 3ds Max layer names ignore case: the first of a name stands for it, as in rule 10.
            var byName = new Dictionary<string, PhasingMaxLayer>(StringComparer.OrdinalIgnoreCase);
            var order = new List<PhasingMaxLayer>();
            foreach (var maxLayer in maxLayers)
            {
                if (maxLayer != null && maxLayer.Name != null && !byName.ContainsKey(maxLayer.Name))
                {
                    byName[maxLayer.Name] = maxLayer;
                    order.Add(maxLayer);
                }
            }
            PhasingMaxLayer topLayer;
            if (!byName.TryGetValue(counts.TopName, out topLayer))
            {
                topLayer = order.FirstOrDefault(l => l.ParentName == null && l.Name.IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (topLayer == null)
            {
                return counts;
            }
            counts.TopLayerName = topLayer.Name;
            var children = order.Where(l => l.ParentName != null).ToLookup(l => l.ParentName, StringComparer.OrdinalIgnoreCase);
            var layers = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { topLayer.Name };
            var waiting = new Stack<PhasingMaxLayer>(Enumerable.Reverse(children[topLayer.Name]));
            while (waiting.Count > 0)
            {
                var maxLayer = waiting.Pop();
                if (!visited.Add(maxLayer.Name))
                {
                    continue;
                }
                layers.Add(maxLayer.Name);
                foreach (var child in Enumerable.Reverse(children[maxLayer.Name]))
                {
                    waiting.Push(child);
                }
            }
            counts.Layers = layers.Count;

            var helperNames = new HashSet<string>(helpers, StringComparer.OrdinalIgnoreCase);
            var layerNames = new HashSet<string>(layers, StringComparer.OrdinalIgnoreCase);
            counts.LayersWithoutHelper.AddRange(layers.Where(n => !helperNames.Contains(n)));
            counts.HelpersWithoutLayer.AddRange(helpers.Where(n => !layerNames.Contains(n)).Distinct(StringComparer.Ordinal));
            return counts;
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

        // The ID with its split (after an underscore) and the description read back as that ID and that description: a
        // description starting with a number of one or two digits and an underscore or a space would read as the split of
        // an ID that has none (and one that is only such a number does not read as a layer at all).
        private static bool ReadsAsLayer(string id, string split, string description)
        {
            var match = LayerPattern.Match(id + (split != null ? "_" + split : string.Empty) + "_" + description);
            return match.Success && match.Groups["rest"].Value == description
                && (split == null ? !match.Groups["split"].Success : match.Groups["split"].Value == split);
        }

        // A corrected layer name reads, to the fix and to the Planner, as the phase, stage and split it was written with.
        private static bool ReadsBackAs(string name, int phase, int stage, string split)
        {
            int splitValue = 0;
            if (split != null && !TryNumber(split, out splitValue))
            {
                return false;
            }
            int? subStage = split != null ? splitValue : (int?)null;
            var id = ReadLayerId(name);
            var planner = PlannerNaming.ParseLayerName(name);
            return id != null && id.Phase == phase && id.Stage == stage && id.Split == subStage
                && planner != null && planner.Phase == phase && planner.Stage == stage && planner.SubStage == subStage;
        }

        // The objects of a layer left as it is are left as they are too: one whose name still has a hyphen, or a tag's
        // sub-stage written the old way, is listed, so that the modeller knows it waits for its layer (Datasmith would still
        // turn its hyphens into underscores in Unreal).
        private static void CheckLeftObjects(PhasingSceneNode layer, Scene scene, List<Func<PhasingFixRow>> checks)
        {
            var layerName = NameOf(layer);
            foreach (var child in scene.ChildrenOf(layer))
            {
                var name = NameOf(child);
                var parsed = PlannerNaming.ParseMeshName(name);
                if (scene.LeftOut(child) || parsed == null || (name.IndexOf('-') < 0 && !parsed.Legacy))
                {
                    continue;
                }
                var key = child.Key;
                var what = parsed.Legacy ? "its tag writes the sub-stage the old way" : "its name has a hyphen";
                checks.Add(() => CheckRow(PhasingFixItem.Object, layerName, name, name, key,
                    what + ", but its layer is left as it is (see the layer's own row); correct the layer, then fix the names again to convert it"));
            }
        }

        // Layers

        /// <summary>
        /// Reads a child of the top node or of a phase group as a layer, or returns null. <paramref name="unread"/> says
        /// why a name that reads as a layer ID is left as it is (<see cref="ReadLayerName"/>: a layer of one number with an
        /// old sub-stage whose objects give it no phase or several, a number of three digits or more, an ID the Planner
        /// reads otherwise), and is null otherwise.
        /// </summary>
        private static LayerWork ReadLayer(PhasingSceneNode node, Scene scene, out string unread)
        {
            unread = null;
            var name = NameOf(node);
            var read = ReadLayerName(name, ObjectsOf(node, scene));
            if (read.Left != LayerLeft.None)
            {
                unread = LeftText(read, name);
                return null;
            }
            if (read.Id == null)
            {
                return null;
            }
            var one = read.One;
            if (one != null)
            {
                var oneNumber = new LayerWork
                {
                    Node = node,
                    OldName = name,
                    Phase = one.Phase.Value,
                    OldStage = one.Stage,
                    Stage = one.Stage,
                    OldSplit = one.SplitText,
                    OldSplitForm = one.Form,
                    Rest = one.Rest
                };
                oneNumber.Why.Add("phase " + one.Phase.Value.ToString(CultureInfo.InvariantCulture) + " added in front, from its objects' tags: an ID of one number cannot carry a sub-stage");
                if (one.StageText != Pad2(one.Stage))
                {
                    oneNumber.Why.Add("stage number written as two digits");
                }
                return oneNumber;
            }
            var match = read.Match;
            var layer = new LayerWork
            {
                Node = node,
                OldName = name,
                Phase = read.Id.Phase,
                OldStage = read.Id.Stage,
                Stage = read.Id.Stage,
                OldSplit = match.Groups["split"].Success ? match.Groups["split"].Value : null,
                OldSplitForm = read.Id.SplitForm
            };
            var phaseText = match.Groups["phase"].Value;
            if (phaseText != Pad2(layer.Phase))
            {
                layer.Why.Add("phase number written as two digits, like the other phases");
            }
            if (match.Groups["after"].Value == ".")
            {
                layer.Why.Add("underscore after the phase number, not a full stop");
            }
            if (match.Groups["stage"].Value != Pad2(layer.Stage))
            {
                layer.Why.Add("stage number written as two digits");
            }
            // A split written the current way may follow a run of underscores and spaces, or be followed by a space, as the
            // Planner reads it ("02_01__2_...", "02_01_ 2_...", "02_01_2 ..."): the ID is written back with one underscore
            // on each side of it.
            if (match.Groups["gap"].Success)
            {
                var sep = match.Groups["sep"].Value;
                if (sep.IndexOf(' ') >= 0 || match.Groups["gap"].Value == " ")
                {
                    layer.Why.Add("spaces replaced with underscores");
                }
                if (sep.Length > 1)
                {
                    layer.Why.Add("one underscore before the sub-stage number");
                }
            }
            layer.Rest = match.Groups["rest"].Value;
            return layer;
        }

        // The Check line of a name that reads as a layer ID but is left as it is (ReadLayerName).
        private static string LeftText(LayerRead read, string name)
        {
            var one = read.One;
            switch (read.Left)
            {
                case LayerLeft.NoPhase:
                case LayerLeft.SeveralPhases:
                    var stageText = Pad2(one.Stage) + "_" + one.SplitText;
                    // The fixer reads a layer's objects as the objects directly in it: one inside a Max group in the layer, or
                    // linked under another object there, is not read (nor renamed).
                    return "one number with a sub-stage (" + one.StageText + (one.Form == PlannerLegacyForm.Dot ? "." : "-") + one.SplitText
                        + "): the convention needs the phase in front, <phase>_" + stageText + "_<name>, but "
                        + (read.Left == LayerLeft.NoPhase
                            ? "none of the objects directly in it has a tag of stage " + stageText + " to give it (objects inside a group or linked under another object are not read)"
                            : "its objects' tags of that stage carry phases " + JoinWithAnd(one.Phases.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList()))
                        + "; left as it is - rename it by hand";
                case LayerLeft.LongNumber:
                    var written = one != null
                        ? name.Substring(0, one.StageText.Length + 1 + one.SplitText.Length)
                        : name.Substring(0, read.Match.Groups["split"].Index + read.Match.Groups["split"].Length);
                    return "the convention writes each number of a layer ID with one or two digits (" + written + "); left as it is - rename it by hand";
                case LayerLeft.PlannerReadsOtherwise:
                    var ours = Pad2(read.Reading.Phase) + "_" + Pad2(read.Reading.Stage)
                        + (read.Match.Groups["split"].Success ? "_" + read.Match.Groups["split"].Value : string.Empty);
                    var longNumber = read.Match.Groups["phase"].Length > 2 || read.Match.Groups["stage"].Length > 2;
                    return "the Planner reads its ID as " + PlannerIdText(read.Planner) + ", not " + ours
                        + (longNumber ? " (the convention writes each number of a layer ID with one or two digits)" : string.Empty) + "; left as it is - rename it by hand";
                default:
                    return null;
            }
        }

        // A group's ID as the Planner reads it, for the Check lines: "02_01_2", or "one number, stage 02".
        internal static string PlannerIdText(PlannerLayerName planner)
        {
            if (planner == null)
            {
                return "no layer at all";
            }
            var stage = Pad2(planner.Stage) + (planner.SubStage.HasValue ? "_" + planner.SubStage.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            return planner.Phase.HasValue ? Pad2(planner.Phase.Value) + "_" + stage : "one number, stage " + stage;
        }

        // The tagged objects of a layer node that the export keeps as geometry: its children that are neither helpers
        // nor shapes, read as the Planner reads them.
        private static List<PlannerMeshName> ObjectsOf(PhasingSceneNode node, Scene scene)
        {
            return scene.ChildrenOf(node).Where(c => !c.IsHelper && !c.IsShape)
                .Select(c => PlannerNaming.ParseMeshName(NameOf(c))).Where(p => p != null).ToList();
        }

        // Rule 4 for a layer that is fixed: the text after its ID, and the start date read from it.
        private static void FixLayerText(LayerWork layer, List<PhasingWordFix> fixes, TextFixNotes notes, TopWork top, List<Func<PhasingFixRow>> checks)
        {
            var id = Pad2(layer.Phase) + "_" + Pad2(layer.OldStage);
            var split = layer.OldSplit;
            var fix = FixText(layer.Rest, fixes, layer.Why, d => ReadsAsLayer(id, split, d), notes, layer.OldName);
            layer.Description = fix.Text;
            CheckUnreadable(layer, fix, top, checks);
            var date = DatePattern.Match(layer.Description);
            layer.Start = date.Success ? "20" + date.Groups[3].Value + "-" + date.Groups[2].Value + "-" + date.Groups[1].Value : NoDate;
        }

        // Rule 7: a stage with more than one layer gets _1, _2, ... by start date, then old split, then order;
        // a lone layer keeps its split (or none), unless the review moved it there: that split was its old stage's. A
        // layer moved alone whose name starts with a number of one or two digits ("02_05_1_3_Storey_Frame" moved to stage
        // 7) gets split 1 instead: with none, that number would read as its split, to the Planner and to the fix alike
        // ("02_07_3_Storey_Frame" is 02_07_3, named "Storey_Frame"), and its objects would be retagged again on the next run.
        // IDs are compared and named in today's spelling, so 02_05.1, 02_05-1 and 02_05_1 are the same ID (and a layer
        // of one number, 05-1, is compared with the phase its objects gave it).
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
                    var lone = stage[0];
                    lone.Split = lone.StageOverridden ? null : lone.OldSplit;
                    if (lone.Split == null && !ReadsAsLayer(Pad2(lone.Phase) + "_" + Pad2(lone.Stage), null, lone.Description))
                    {
                        lone.Split = "1";
                        lone.Why.Add("split number 1 given: without one, the number its name starts with would read as its split");
                    }
                    continue;
                }
                var list = stage.OrderBy(l => l.Start, StringComparer.Ordinal).ThenBy(l => SplitNumber(l.OldSplit)).ThenBy(l => l.Order).ToList();
                var oldIds = list.Select(l => Pad2(l.Phase) + "_" + Pad2(l.OldStage) + (l.OldSplit != null ? "_" + l.OldSplit : string.Empty)).ToList();
                var repeated = new HashSet<string>(oldIds.Where((id, i) => oldIds.IndexOf(id) != i), StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    var layer = list[i];
                    layer.Split = (i + 1).ToString(CultureInfo.InvariantCulture);
                    var stageId = Pad2(layer.Phase) + "_" + Pad2(layer.Stage);
                    if (oldIds[i] == stageId + "_" + layer.Split)
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

        private static ObjectWork ReadObject(PhasingSceneNode node, LayerWork layer, List<LayerWork> layers, List<LayerWork> unplaced, OutsideLayers outside,
            List<PhasingWordFix> fixes, TextFixNotes notes, List<Func<PhasingFixRow>> checks)
        {
            var work = new ObjectWork { Node = node, OldName = NameOf(node), Layer = layer };
            work.NewName = work.OldName;
            var lead = LeadPattern.Match(work.OldName);
            if (!lead.Success)
            {
                checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key, "no Ph/St tag at the start of the name"));
                return work;
            }
            // Taken away and put back in one stage while it sits in another stage's layer: a naming error that only the
            // modeller can put right, so it is left as it is, not half corrected (its removal tag alone given the layer's
            // stage would take it away in this layer and put it back in the other, which nothing reports), and listed.
            var misfiled = MisfiledIn(work.OldName, layer);
            if (misfiled != null)
            {
                work.Tagged = true;
                work.Kept = true;
                work.Misfiled = true;
                checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key, misfiled));
                return work;
            }
            var kind = lead.Groups[5].Value.ToUpperInvariant();
            var body = work.OldName.Substring(lead.Length);
            var tail = string.Empty;
            var trail = kind == "IN" ? TrailPattern.Match(body) : Match.Empty;
            if (trail.Success)
            {
                body = body.Substring(0, trail.Index);
                tail = ResolveTrail(trail, work, layers, unplaced, outside, checks);
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
            // A tag still closing the text (an RM object's trailing tag, or a second IN) gets the underscore too.
            var closing = ClosingTagOldSplit.Match(work.Body);
            if (closing.Success)
            {
                work.Body = ClosingTagOldSplit.Replace(work.Body, "$1_");
                work.Why.Add(SubStageReason(closing.Groups[2].Value == "." ? PlannerLegacyForm.Dot : PlannerLegacyForm.Hyphen));
            }
            // Every other hyphen: Unreal's Datasmith import turns each one in an object's name into an underscore, so the
            // object would carry another name there than in the Planner. The lead and the tail are tags already written
            // with underscores.
            if (work.Body.IndexOf('-') >= 0)
            {
                work.Body = work.Body.Replace('-', '_');
                work.Why.Add(DatasmithReason);
            }
            // The text may now end in a tag it did not end in before, its hyphen (or a space) gone ("..._Barrier-Ph2_St05.1_RM"
            // ends in "_Ph2_St05.1_RM"). It is corrected in this run, as one written so from the start would be, so that a
            // second run finds nothing more to do: a removal tag of an IN object follows its layer, with its sub-stage and
            // phase respelt; a tag closing the text otherwise gets the underscore before its sub-stage (by now only a full
            // stop can be left there). The Check line below says the name gained a tag.
            if (kind == "IN" && tail.Length == 0)
            {
                var exposed = TrailPattern.Match(work.Body);
                if (exposed.Success)
                {
                    work.Body = work.Body.Substring(0, exposed.Index);
                    tail = ResolveTrail(exposed, work, layers, unplaced, outside, checks);
                }
            }
            closing = ClosingTagOldSplit.Match(work.Body);
            if (closing.Success)
            {
                work.Body = ClosingTagOldSplit.Replace(work.Body, "$1_");
                work.Why.Add(SubStageReason(closing.Groups[2].Value == "." ? PlannerLegacyForm.Dot : PlannerLegacyForm.Hyphen));
            }
            if (lead.Value != work.Lead)
            {
                // Only the character before the split differs (a full stop or a hyphen for the underscore): say just that.
                var underscored = lead.Groups[3].Success && lead.Groups[3].Value != "_"
                    ? lead.Value.Substring(0, lead.Groups[3].Index) + "_" + lead.Value.Substring(lead.Groups[3].Index + 1)
                    : lead.Value;
                work.Why.Insert(0, underscored == work.Lead
                    ? SubStageReason(FormOf(lead.Groups[3]))
                    : "tag " + TagType.Replace(lead.Value, string.Empty) + " changed to its layer's " + layer.Tag);
            }
            work.Tail = tail;
            work.NewName = work.Lead + work.Body + tail;
            work.Tagged = true;
            // The hyphens gone, the end of the name may read as a tag it did not before ("..._Kerb-Ph2_St05_RM"): the
            // Planner then reads work the name did not give it, as Unreal would. Said, so the modeller can check it.
            var was = PlannerNaming.ParseMeshName(work.OldName);
            var now = PlannerNaming.ParseMeshName(work.NewName);
            if (was != null && now != null && was.Trail == null && now.Trail != null)
            {
                var gained = now.Trail;
                var how = work.OldName.IndexOf('-') >= 0 ? "with its hyphens replaced" : "as corrected";
                checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key,
                    how + ", the name ends in the " + (gained.Type == PlannerTaskType.Dismantle ? "removal" : "install") + " tag Ph"
                    + gained.PhaseToken + "_St" + gained.StageToken + ", which the Planner reads as more work - check it is meant"));
            }
            return work;
        }

        /// <summary>
        /// The Check line for an object whose name takes it away and puts it back in one stage (its removal tag first, then
        /// its install tag, the same phase, stage and sub-stage, as the Planner reads them) while the layer it sits in owns
        /// neither tag's work, by its ID as it is now (as the Planner and the export's naming check read it) and as corrected:
        /// the Planner lists it as a naming error, to correct in 3ds Max (move it to that stage's layer, or retag both tags
        /// for this layer, as the Planner words it). Null for any other object.
        /// </summary>
        internal static string MisfiledIn(string name, LayerRef layer)
        {
            var parsed = PlannerNaming.ParseMeshName(name);
            if (parsed == null || parsed.Trail == null)
            {
                return null;
            }
            var lead = parsed.Lead;
            var trail = parsed.Trail;
            if (lead.Type != PlannerTaskType.Dismantle || trail.Type != PlannerTaskType.Install || lead.Phase != trail.Phase || lead.Stage != trail.Stage
                || lead.SubStage != trail.SubStage)
            {
                return null;
            }
            if (lead.Phase == layer.Phase && (PlannerNaming.SameStage(lead.Stage, lead.SubStage, layer.OldStage, layer.OldSplit)
                || PlannerNaming.SameStage(lead.Stage, lead.SubStage, layer.Stage, layer.Split)))
            {
                return null;
            }
            var tag = "Ph" + lead.Phase.ToString(CultureInfo.InvariantCulture) + "_" + PlannerSchedule.StageTag(lead);
            return "taken away and put back in " + tag + " (its removal tag, then its install tag), but it sits in this layer, which is not " + tag
                + "'s: the Planner lists it as a naming error; left as it is \u2014 correct it by hand: move it to the " + tag + " layer, or retag it for this layer as "
                + PlannerSchedule.Retagged(name, layer.Phase, layer.Stage, layer.Split);
        }

        private static string MisfiledIn(string name, LayerWork layer)
        {
            return MisfiledIn(name, new LayerRef
            {
                Phase = layer.Phase,
                OldStage = layer.OldStage,
                OldSplit = layer.OldSplit != null ? SplitNumber(layer.OldSplit) : (int?)null,
                Stage = layer.Stage,
                Split = layer.Split != null ? SplitNumber(layer.Split) : (int?)null
            });
        }

        /// <summary>A phasing layer's ID as it is (<see cref="OldStage"/>, <see cref="OldSplit"/>) and as corrected.</summary>
        internal struct LayerRef
        {
            public int Phase;
            public int OldStage;
            public int? OldSplit;
            public int Stage;
            public int? Split;
        }

        // An IN object's removal tag (Rule 8): it follows the layer it points at today to that layer's new ID, or, with no
        // such layer, is kept with its spelling corrected and listed. Returns the tail to write ("_<tag>_RM"); the reasons
        // and Check lines go to the object.
        private static string ResolveTrail(Match trail, ObjectWork work, List<LayerWork> layers, List<LayerWork> unplaced, OutsideLayers outside,
            List<Func<PhasingFixRow>> checks)
        {
            var split = trail.Groups[4].Success ? trail.Groups[4].Value : null;
            var splitForm = FormOf(trail.Groups[3]);
            var tagStart = "Ph" + NumberText(trail.Groups[1].Value) + "_St" + trail.Groups[2].Value;
            // The tag as written (its phase without leading zeros, as before), and in today's spelling.
            var oldTag = tagStart + (split != null ? trail.Groups[3].Value + split : string.Empty);
            var sameTag = tagStart + (split != null ? "_" + split : string.Empty);
            // The tag with the zeros of its phase dropped, and the reason when there were any: "_Ph06_St40_RM", the tag
            // between the leading "_" and the trailing "_RM", is written Ph6_St40.
            var unpadded = TrailPhaseZeros.Replace(trail.Value, "_Ph");
            var zeros = unpadded != trail.Value
                ? "removal tag " + trail.Value.Substring(1, trail.Value.Length - 4) + " written " + unpadded.Substring(1, unpadded.Length - 4) + ", like the other tags"
                : null;
            List<LayerWork> ambiguous = null;
            int phase, stage;
            // "&", not "&&": both numbers are read, so both are set.
            var readable = TryNumber(trail.Groups[1].Value, out phase) & TryNumber(trail.Groups[2].Value, out stage);
            var target = readable ? ResolveOld(layers, phase, stage, split, out ambiguous) : null;
            if (target == null)
            {
                var tail = unpadded;
                if (zeros != null)
                {
                    work.Why.Add(zeros);
                }
                var underscored = ClosingTagOldSplit.Replace(tail, "$1_");
                if (underscored != tail)
                {
                    tail = underscored;
                    work.Why.Add(SubStageReason(splitForm));
                }
                List<LayerWork> strays;
                var stray = readable ? ResolveOld(unplaced, phase, stage, split, out strays) : null;
                // A group elsewhere in the scene that the Planner reads as the layer of that stage (at the scene root,
                // under another top node, ...): the Planner reads the removal as its work (and dates it from it once it has
                // dates), so the stage has a layer. The scene model does not say what the export leaves out (hidden
                // nodes, export layers, selected only: the export's settings, not the scene's), so the line says the group
                // counts only if it is exported, as the export's naming check finds.
                int splitValue;
                var elsewhere = readable && stray == null
                    ? outside.Find(phase, stage, split != null && TryNumber(split, out splitValue) ? splitValue : (int?)null)
                    : null;
                // The tag stays, but its spelling may be corrected (the phase's zeros, the underscore), which the object's
                // Changed line gives: this line says so rather than "left as it is".
                var respelt = tail != trail.Value;
                var kept = "tag kept" + (respelt ? ", only its spelling corrected" : string.Empty);
                checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key, stray != null
                    ? "removal tag " + sameTag + " points at " + stray.OldName + ", which is left where it is (see its own row); " + kept
                    : elsewhere != null
                    ? "removal tag " + sameTag + " points at " + elsewhere.Name + ", outside the phasing layers" + (elsewhere.Start.HasValue ? string.Empty : ", which has no dates yet") + "; " + kept
                      + " - the Planner dates the removal from that group " + (elsewhere.Start.HasValue ? string.Empty : "once it has dates, and ") + "only if the group is exported"
                    : "removal tag " + sameTag + " names a stage that has no layer in this file; " + (respelt ? kept : "left as it is") + " - needs the stage it comes out at"));
                return tail;
            }
            if (target.Tag != sameTag)
            {
                // The layer's current name with its first date dropped, as the reference words it.
                var layerText = TrailingUnderscores.Replace(DatePattern.Replace(target.OldName, string.Empty, 1), string.Empty);
                work.Why.Add("removal tag " + oldTag + " follows its layer " + layerText + " to its new ID " + target.Id);
            }
            else
            {
                // The layer it names keeps its ID: only the tag's spelling may change, the zeros of its phase and the
                // character before its sub-stage.
                if (zeros != null)
                {
                    work.Why.Add(zeros);
                }
                if (splitForm != PlannerLegacyForm.None)
                {
                    work.Why.Add(SubStageReason(splitForm));
                }
            }
            if (ambiguous != null)
            {
                var candidates = ambiguous;
                var pointedAt = target;
                checks.Add(() => CheckRow(PhasingFixItem.Object, work.Layer.NewName, work.OldName, work.NewName, work.Node.Key,
                    "removal tag " + oldTag + " could mean any of " + candidates.Count.ToString(CultureInfo.InvariantCulture) + " layers ("
                    + string.Join(", ", candidates.Select(l => l.NewName).ToArray()) + "); pointed at the first, " + pointedAt.Id + " - confirm"));
            }
            return "_" + target.Tag + "_RM";
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

        // A corrected name that another phasing object has today and keeps: that object keeps it, and this one is left as
        // it was (and listed), so the fix never renames an object into a name already there (two objects "Kerb-1" and
        // "Kerb_1", once the hyphen goes). Taking a number instead would rename the object that needed nothing.
        private static void KeepTakenNames(List<ObjectWork> objects, TopWork top, List<Func<PhasingFixRow>> checks)
        {
            // An object left as it is keeps its name too, which may block another: again until nothing changes.
            while (true)
            {
                var keeping = new HashSet<string>(objects.Where(o => o.NewName == o.OldName).Select(o => o.OldName), StringComparer.Ordinal);
                var blocked = objects.Where(o => o.Tagged && !o.Kept && o.NewName != o.OldName && keeping.Contains(o.NewName)).ToList();
                if (blocked.Count == 0)
                {
                    return;
                }
                foreach (var work in blocked)
                {
                    var item = work;
                    var clash = item.NewName;
                    item.NewName = item.OldName;
                    item.Kept = true;
                    checks.Add(() => CheckRow(item.Item, PlaceOf(item, top), item.OldName, item.NewName, item.Node.Key,
                        "the corrected name " + clash + " is already the name of another object in the phasing layers; left as it is"));
                }
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

            // The Max layer of a node's name: the same name in any capitals, else, for a layer ID, the same name with its
            // split still written an old way, after a hyphen or a full stop, when no node has that name (a Max layer
            // 02_05-1_... or 02_05.1_... goes with a node already named 02_05_1_...).
            var nodeNames = new HashSet<string>(containers.Select(c => c.OldName), StringComparer.OrdinalIgnoreCase);
            Func<string, PhasingMaxLayer> find = name =>
            {
                PhasingMaxLayer found;
                if (byName.TryGetValue(name, out found))
                {
                    return found;
                }
                foreach (var old in OldSpellings(name))
                {
                    if (!nodeNames.Contains(old) && byName.TryGetValue(old, out found))
                    {
                        return found;
                    }
                }
                return null;
            };

            // A moved layer's Max layer goes under its group's Max layer, when both exist (current names).
            foreach (var layer in moved)
            {
                var own = find(layer.OldName);
                var parent = find(layer.Group.OldName);
                if (own != null && parent != null && !string.Equals(own.ParentName, parent.Name, StringComparison.OrdinalIgnoreCase))
                {
                    plan.MaxLayerMoves.Add(new PhasingMaxLayerMove { Name = own.Name, NewParentName = parent.Name });
                }
            }

            // A Max layer named like a top node, group or layer that is renamed is renamed the same way, unless the
            // nodes that share its name are going different ways. A Max layer still written the old way (its split after a
            // hyphen or a full stop) is renamed to its node's name even when the node keeps it.
            var wanted = new List<KeyValuePair<Container, PhasingMaxLayer>>();
            foreach (var sameName in containers.GroupBy(c => c.OldName, StringComparer.OrdinalIgnoreCase))
            {
                var maxLayer = find(sameName.Key);
                if (maxLayer == null || (byName.ContainsKey(sameName.Key) && sameName.All(c => c.NewName == c.OldName)))
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
                if (rename.Key.NewName == rename.Key.OldName)
                {
                    // The node keeps its name, so it has no row of its own: the Max layer's change is listed instead.
                    var oldForm = FormOf(LayerPattern.Match(rename.Value.Name).Groups["sep"]);
                    plan.Rows.Add(ChangedRow(rename.Key.Item, rename.Key.LayerText, rename.Value.Name, rename.Key.NewName, rename.Key.Key,
                        new[] { "the Max layer takes its node's name: " + SubStageReason(oldForm) }));
                }
            }
        }

        // The name with its layer ID's split written the old ways, after a hyphen and after a full stop instead of the
        // underscore (02_05_1_... gives 02_05-1_... and 02_05.1_...); none when it has no split after an underscore.
        private static IEnumerable<string> OldSpellings(string name)
        {
            var match = LayerPattern.Match(name ?? string.Empty);
            if (!match.Success || !match.Groups["sep"].Success || match.Groups["sep"].Value != "_")
            {
                yield break;
            }
            var at = match.Groups["sep"].Index;
            yield return name.Substring(0, at) + "-" + name.Substring(at + 1);
            yield return name.Substring(0, at) + "." + name.Substring(at + 1);
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
            /// <summary>The name writes its split an old way, after a hyphen or a full stop; None after the underscore.</summary>
            public PlannerLegacyForm OldSplitForm;
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
            /// <summary>The object tag form of the new ID, e.g. Ph2_St05_1.</summary>
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
            /// <summary>Taken away and put back in one stage while it sits in another stage's layer: left for the modeller (<see cref="MisfiledIn(string, LayerRef)"/>).</summary>
            public bool Misfiled;

            public override PhasingFixItem Item
            {
                get { return PhasingFixItem.Object; }
            }
        }

        /// <summary>A layer of one number with a sub-stage written an old way ("06-2_..."), read with its objects (<see cref="ReadOneNumber"/>).</summary>
        internal sealed class OneNumberLayer
        {
            /// <summary>The stage digits as written ("06").</summary>
            public string StageText;
            public int Stage;
            /// <summary>The sub-stage digits as written ("2").</summary>
            public string SplitText;
            public int Split;
            /// <summary>Hyphen or Dot: how the name writes its sub-stage.</summary>
            public PlannerLegacyForm Form;
            /// <summary>The text after the ID, as the name has it.</summary>
            public string Rest;
            /// <summary>The phase its objects give it (<see cref="OneNumberPhase"/>), or null.</summary>
            public int? Phase;
            /// <summary>The phases its objects' tags with its stage carry, lowest first: none, one, or several.</summary>
            public List<int> Phases;
        }

        /// <summary>Why the fixer leaves a name that reads as a layer ID as it is (<see cref="ReadLayerName"/>).</summary>
        internal enum LayerLeft
        {
            /// <summary>Read as a layer, or not a layer ID at all.</summary>
            None,
            /// <summary>One number with an old sub-stage, and no tag of its stage on the objects directly in it.</summary>
            NoPhase,
            /// <summary>One number with an old sub-stage, and tags of its stage in several phases on the objects directly in it.</summary>
            SeveralPhases,
            /// <summary>An old form with a number of three digits or more, which the underscore form cannot carry.</summary>
            LongNumber,
            /// <summary>An ID written the current way that the Planner reads otherwise.</summary>
            PlannerReadsOtherwise
        }

        /// <summary>A name read as a layer, as <see cref="ReadLayerName"/> reads it.</summary>
        internal sealed class LayerRead
        {
            /// <summary>The ID, or null: not a layer ID, or one the fixer leaves as it is (<see cref="Left"/>).</summary>
            public PhasingLayerId Id;
            /// <summary>Why a name that reads as a layer ID is left as it is; None otherwise.</summary>
            public LayerLeft Left;
            /// <summary>A layer of one number with an old sub-stage, or null.</summary>
            public OneNumberLayer One;
            /// <summary>The LayerPattern match of a layer of two or three numbers, or null.</summary>
            public Match Match;
            /// <summary>What the fix reads a layer of two or three numbers as, even one it leaves as it is.</summary>
            public PhasingLayerId Reading;
            /// <summary>The Planner's reading of an ID it reads otherwise (PlannerReadsOtherwise).</summary>
            public PlannerLayerName Planner;
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
            public readonly long Key;
            public readonly string OldName;
            public readonly string NewName;
            public readonly string LayerText;

            public Container(PhasingFixItem item, long key, string oldName, string newName, string layerText)
            {
                Item = item;
                Key = key;
                OldName = oldName;
                NewName = newName;
                LayerText = layerText;
            }
        }

        /// <summary>
        /// The groups that are not phasing layers of the fixer's top node but that the Planner reads as layers: a name the
        /// Planner reads as a dated group (<see cref="PlannerNaming.ParseLayerName"/>) that is the nearest such group of
        /// a tagged object, anywhere in the scene (at the root, under another top node, under a phase group without
        /// reading as a layer there). Read on first use. Every node of the scene counts, exported or not: what the export
        /// leaves out depends on its settings, which the fixer does not have, so the Check line says so.
        /// </summary>
        private sealed class OutsideLayers
        {
            private readonly Scene scene;
            private readonly HashSet<long> phasingLayers;
            private List<OutsideLayer> found;

            public OutsideLayers(Scene scene, IEnumerable<long> phasingLayerKeys)
            {
                this.scene = scene;
                phasingLayers = new HashSet<long>(phasingLayerKeys);
            }

            /// <summary>
            /// The layer the Planner reads as that phase and stage (sub-stages as <see cref="PlannerNaming.SameStage"/>), or null:
            /// a dated one first, then the one for that very sub-stage, then the earliest start, then the scene's order.
            /// </summary>
            public OutsideLayer Find(int phase, int stage, int? split)
            {
                if (found == null)
                {
                    found = Read();
                }
                return found.Where(l => l.Phase == phase && PlannerNaming.SameStage(l.Stage, l.SubStage, stage, split))
                    .OrderBy(l => l.Start.HasValue ? 0 : 1)
                    .ThenBy(l => l.SubStage == split ? 0 : 1)
                    .ThenBy(l => l.Start ?? DateTime.MaxValue)
                    .ThenBy(l => l.Order)
                    .FirstOrDefault();
            }

            private List<OutsideLayer> Read()
            {
                var byKey = scene.All.ToDictionary(n => n.Key);
                var groups = new Dictionary<long, PlannerLayerName>();
                foreach (var node in scene.All.Where(n => !scene.LeftOut(n)))
                {
                    var parsed = PlannerNaming.ParseLayerName(NameOf(node));
                    if (parsed != null)
                    {
                        groups[node.Key] = parsed;
                    }
                }
                // Each tagged object (geometry) under its nearest group, as the Planner files it.
                var members = new Dictionary<long, List<PlannerMeshName>>();
                foreach (var node in scene.All.Where(n => !n.IsHelper && !n.IsShape))
                {
                    var mesh = PlannerNaming.ParseMeshName(NameOf(node));
                    if (mesh == null)
                    {
                        continue;
                    }
                    var seen = new HashSet<long>();
                    var at = node.ParentKey;
                    while (at.HasValue && !groups.ContainsKey(at.Value) && seen.Add(at.Value))
                    {
                        PhasingSceneNode parent;
                        at = byKey.TryGetValue(at.Value, out parent) ? parent.ParentKey : null;
                    }
                    if (!at.HasValue || !groups.ContainsKey(at.Value) || phasingLayers.Contains(at.Value))
                    {
                        continue;
                    }
                    List<PlannerMeshName> list;
                    if (!members.TryGetValue(at.Value, out list))
                    {
                        list = new List<PlannerMeshName>();
                        members[at.Value] = list;
                    }
                    list.Add(mesh);
                }
                var result = new List<OutsideLayer>();
                for (int i = 0; i < scene.All.Count; i++)
                {
                    var node = scene.All[i];
                    List<PlannerMeshName> list;
                    if (!members.TryGetValue(node.Key, out list))
                    {
                        continue;
                    }
                    var group = groups[node.Key];
                    var phase = PlannerNamingValidator.GroupPhase(group, list);
                    if (phase.HasValue)
                    {
                        result.Add(new OutsideLayer { Name = NameOf(node), Phase = phase.Value, Stage = group.Stage, SubStage = group.SubStage, Start = group.Start, Order = i });
                    }
                }
                return result;
            }
        }

        private sealed class OutsideLayer
        {
            public string Name;
            public int Phase;
            public int Stage;
            public int? SubStage;
            /// <summary>Its first date; null when it has none (TBC).</summary>
            public DateTime? Start;
            /// <summary>Its place in the scene's list.</summary>
            public int Order;
        }

        private sealed class Scene
        {
            public readonly List<PhasingSceneNode> All = new List<PhasingSceneNode>();
            public readonly List<PhasingSceneNode> Roots;
            private readonly Dictionary<long, List<PhasingSceneNode>> children = new Dictionary<long, List<PhasingSceneNode>>();
            private readonly Dictionary<long, bool> leftOut = new Dictionary<long, bool>();
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

            /// <summary>
            /// Linked under it is something the export keeps: a node that is not a left-out shape, or children that are not
            /// in the list (taken to be kept).
            /// </summary>
            public bool HasExportedChildren(PhasingSceneNode node)
            {
                return UnlistedChildren(node) || ChildrenOf(node).Any(c => !LeftOut(c));
            }

            /// <summary>
            /// A shape (a spline such as a RailClone path) with nothing exported linked under it at any depth: the export
            /// leaves it out and the Planner never sees it, so the fixer does not list, rename or count it. A shape that
            /// does hold something exported is an empty node in the export, like a helper.
            /// </summary>
            public bool LeftOut(PhasingSceneNode node)
            {
                if (!node.IsShape)
                {
                    return false;
                }
                bool result;
                if (leftOut.TryGetValue(node.Key, out result))
                {
                    return result;
                }
                result = true;
                var seen = new HashSet<long> { node.Key };
                var pending = new Stack<PhasingSceneNode>();
                pending.Push(node);
                while (pending.Count > 0 && result)
                {
                    var current = pending.Pop();
                    if (UnlistedChildren(current))
                    {
                        result = false;
                        break;
                    }
                    foreach (var child in ChildrenOf(current))
                    {
                        if (!seen.Add(child.Key))
                        {
                            continue;
                        }
                        if (!child.IsShape)
                        {
                            result = false;
                            break;
                        }
                        pending.Push(child);
                    }
                }
                leftOut[node.Key] = result;
                return result;
            }

            // Flagged as having children, none of them in the list.
            private bool UnlistedChildren(PhasingSceneNode node)
            {
                return node.HasChildren && !children.ContainsKey(node.Key);
            }
        }
    }
}
