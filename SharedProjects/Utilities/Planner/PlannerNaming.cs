using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>Install (the object appears) or Dismantle (the object is removed).</summary>
    public enum PlannerTaskType
    {
        Install,
        Dismantle
    }

    /// <summary>One <c>Ph&lt;n&gt;_St&lt;n&gt;_IN|RM</c> tag inside an object name.</summary>
    public sealed class PlannerMeshSegment
    {
        /// <summary>The phase digits as written (e.g. "1").</summary>
        public string PhaseToken;
        /// <summary>The stage as written (e.g. "03", "03-2" with a sub-stage, or the old "03.2"); the Planner keeps this spelling in activity names.</summary>
        public string StageToken;
        public int Phase;
        /// <summary>The stage number ("03-2" and "03.2" give 3).</summary>
        public int Stage;
        /// <summary>The sub-stage ("03-2" and "03.2" give 2), or null for a plain stage.</summary>
        public int? SubStage;
        public PlannerTaskType Type;
        /// <summary>The sub-stage is written the old way, with a full stop ("St03.2"); read the same as "St03-2".</summary>
        public bool Legacy;

        public string FolderName { get { return "PH" + PhaseToken; } }
        public string ActivityName { get { return "St" + StageToken; } }
    }

    /// <summary>An object name split into its leading tag, optional trailing tag and the description between them.</summary>
    public sealed class PlannerMeshName
    {
        public PlannerMeshSegment Lead;
        /// <summary>Null unless the name ends in a second tag.</summary>
        public PlannerMeshSegment Trail;
        /// <summary>Free text between the tags (may contain underscores); empty when the tags abut.</summary>
        public string Description;

        /// <summary>Either tag writes its sub-stage the old way, with a full stop.</summary>
        public bool Legacy
        {
            get { return (Lead != null && Lead.Legacy) || (Trail != null && Trail.Legacy); }
        }
    }

    /// <summary>A group (3ds Max layer / group node) name read as an order number, activity name and dates.</summary>
    public sealed class PlannerLayerName
    {
        /// <summary>
        /// The first number as written, e.g. "01", "06-1" (or the old "06.1"): the stage of a one-number name, the phase of
        /// a two-number one ("02" in "02_01-2_..."). The label starts with it.
        /// </summary>
        public string Order;
        /// <summary>The phase a two-number name gives ("02_01-2_..." gives 2), or null: a one-number name's phase is its objects'.</summary>
        public int? Phase;
        /// <summary>
        /// The stage an object's St&lt;n&gt; points at, in the name's phase when it gives one: a one-number name's ("06-1" and
        /// "06.1" give 6), or the second number of a two-number one ("02_01-2_..." gives 1).
        /// </summary>
        public int Stage;
        /// <summary>The sub-stage that goes with <see cref="Stage"/> ("06-1" gives 1, "02_01-2_..." gives 2), or null: the group covers its whole stage.</summary>
        public int? SubStage;
        /// <summary>The activity name with the ID (one number, or two), dates and TBC stripped ("Retainment" for "01_04-2_Retainment_...").</summary>
        public string Name;
        /// <summary>"&lt;order&gt;_&lt;the rest&gt;" with dates and TBC stripped ("01_04-2_Retainment"): the stable label the Planner groups by.</summary>
        public string Label;
        /// <summary>Dates still need confirming: a TBC token, no readable dates, or any issue.</summary>
        public bool Tbc;
        public DateTime? Start;
        /// <summary>Inclusive last day; equals Start for a single day. Null when undated.</summary>
        public DateTime? End;
        public List<string> Issues = new List<string>();
        /// <summary>
        /// The stage's sub-stage is written the old way, with a full stop: a one-number name's ("06.1_...") or a two-number
        /// one's ("01_04.2_...", also after a space or several underscores). Read exactly as the hyphen forms ("06-1_...",
        /// "01_04-2_..."). The parser cannot tell a measurement in that position ("01_1.8_x_2.4_...") from a sub-stage;
        /// the naming check looks closer.
        /// </summary>
        public bool Legacy;
    }

    /// <summary>
    /// C# port of the Planner's <c>packages/domain/src/phasingNaming.ts</c>. The two must stay in step:
    /// whatever this parser accepts is exactly what the Planner turns into folders and activities once the
    /// GLB is uploaded, so the exporter can tell the artist before the export what the Planner will see.
    ///
    /// Object names carry one or two tags <c>Ph&lt;n&gt;_St&lt;n&gt;_IN|RM</c>; the leading tag starts the name,
    /// an optional trailing tag ends it, and the free-form description sits between them. A stage may carry
    /// a sub-stage, <c>St&lt;n&gt;-&lt;m&gt;</c> ("St03-2"): work inside stage 3 with its own place in the order,
    /// 3 &lt; 3-1 &lt; 3-2 &lt; 3-10 &lt; 4. The old spelling with a full stop ("St03.2") still reads, as the same
    /// stage, and is marked <see cref="PlannerMeshSegment.Legacy"/>.
    ///
    /// Group names carry the schedule: <c>N_&lt;Activity&gt;_DD-MM-YY[_DD-MM-YY]</c>, or <c>_TBC</c> while the dates
    /// are unknown (optionally followed by provisional dates). A sub-numbered N ("06-1", or the old "06.1") is
    /// the sub-stage an object's "St06-1" points at; a plain N covers its whole stage, in the phase its objects carry.
    /// A name that starts with two numbers, <c>&lt;PP&gt;_&lt;SS&gt;[-&lt;m&gt;]_...</c>, is phase PP, stage SS[-m]
    /// ("02_01-2_Construction_31-08-26" is phase 2, stage 1-2; the old "01_04.2_..." phase 1, stage 4-2): each one or two
    /// digits, apart by underscores or spaces as after a one-number N, the stage followed by an underscore, a space or
    /// the end, a stage with a full stop (as a dotted N) only by an underscore or the end, so "01_2.4 High Hoarding" is
    /// stage 1 named "2.4 High Hoarding". Parsing is deliberately
    /// lenient about what artists actually type (a space after N, spaces in the name, single-digit days,
    /// text after the dates, the " (1)" suffix on duplicate names) but the dates must read DD-MM-YY. Three
    /// hyphenated numbers are a date, never a stage, and a stage ("04-2") is never a date.
    /// </summary>
    public static class PlannerNaming
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        /// <summary>
        /// JavaScript's \s, which is also what its trim() removes. .NET's \s and string.Trim() leave out U+FEFF but take
        /// U+0085, so the port spells the Planner's set out.
        /// </summary>
        internal const string JavaScriptSpace = @"[\t\n\u000B\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";
        private static readonly char[] JavaScriptSpaceChars =
        {
            '\t', '\n', '\u000B', '\f', '\r', ' ', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
            '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF'
        };

        // [0-9] rather than \d throughout: .NET's \d matches every Unicode digit, the Planner's JavaScript \d
        // only ASCII digits, and the port must read exactly what the Planner reads. Likewise \z rather than $
        // (.NET's $ also matches before a final line feed, JavaScript's only at the very end) and JavaScriptSpace
        // rather than \s.
        // Ph<n>_St<n>[-<m>]_<IN|RM> anchored at the start, with the type followed by "_" or the end of the name; the
        // sub-stage after a hyphen, or after a full stop as it was written before.
        private static readonly Regex Lead = new Regex(@"^Ph([0-9]+)_St([0-9]+(?:[.-][0-9]+)?)_(IN|RM)(?:_|\z)", OptionsIgnoreCase);
        // ..._Ph<n>_St<n>[-<m>]_<IN|RM> pinned to the end, or followed only by a space-separated note.
        private static readonly Regex Trail = new Regex(@"_Ph([0-9]+)_St([0-9]+(?:[.-][0-9]+)?)_(IN|RM)(?=\z|" + JavaScriptSpace + ")", OptionsIgnoreCase);

        private static readonly Regex DuplicateSuffix = new Regex(JavaScriptSpace + @"*\([0-9]+\)" + JavaScriptSpace + @"*\z", Options);
        // "01" or a dashed sub-number "06-1", followed by an underscore or a space (or nothing at all); a dotted
        // sub-number "06.1" (the old form) only when an underscore follows, so a dimension-led group name that is
        // not a layer ("2.4 High Hoarding", "1.8 x 2.4 Heras Panel") is never read as one.
        private static readonly Regex Order = new Regex(@"^([0-9]+(?:-[0-9]+)?)(?:[_ ]+|\z)|^([0-9]+\.[0-9]+)(?:_+|\z)", Options);
        // A two-number name's first number, the phase: one or two digits (the order token as Order's first branch reads it).
        // The Planner's LAYER_PHASE.
        private static readonly Regex LayerPhase = new Regex(@"^[0-9]{1,2}\z", Options);
        // Its stage, at the start of what follows the first number and its separators: one or two digits, a hyphen
        // sub-stage then an underscore, a space or the end, or the old full stop one then an underscore or the end (as a
        // dotted order). "18-09-26" is a date: after "18-09" comes a hyphen, so it never reads as a stage. The Planner's
        // LAYER_STAGE.
        private static readonly Regex LayerStage = new Regex(@"^[0-9]{1,2}(?:-[0-9]+)?(?=[_ ]|\z)|^[0-9]{1,2}\.[0-9]+(?=_|\z)", Options);
        // A stage token: the stage digits, then an optional sub-stage after "." (objects) or "-" / "." (groups).
        private static readonly Regex StageNumber = new Regex(@"^([0-9]+)(?:[.-]([0-9]+))?", Options);
        // DD-MM-YY not glued to other digits or dashes (so "06-1" order tokens and "100mm" never match).
        private static readonly Regex DateToken = new Regex(@"(^|[^0-9-])([0-9]{1,2})-([0-9]{1,2})-([0-9]{2})(?![0-9-])", Options);
        private static readonly Regex TbcToken = new Regex(@"(^|[_ \-])TBC(?=\z|[_ \-])", OptionsIgnoreCase);
        private static readonly Regex Underscores = new Regex(@"_+", Options);
        private static readonly Regex EdgeSeparators = new Regex(@"^[_ ]+|[_ ]+\z", Options);

        /// <summary>Parses an object name, or returns null when it carries no leading tag (not a phasing object).</summary>
        public static PlannerMeshName ParseMeshName(string name)
        {
            if (name == null)
            {
                return null;
            }
            var lead = Lead.Match(name);
            if (!lead.Success)
            {
                return null;
            }
            // The trailing tag needs a preceding "_", so it can never be the leading tag at index 0.
            var trail = Trail.Match(name);

            // Everything between the end of the lead match (which includes the delimiter after the type) and the
            // start of the trail match, or the end of the name.
            int descriptionStart = lead.Length;
            int descriptionEnd = trail.Success ? trail.Index : name.Length;
            return new PlannerMeshName
            {
                Lead = Segment(lead),
                Trail = trail.Success ? Segment(trail) : null,
                Description = descriptionStart <= descriptionEnd ? name.Substring(descriptionStart, descriptionEnd - descriptionStart) : string.Empty
            };
        }

        /// <summary>Parses a group name, or returns null when it does not start with an order number.</summary>
        public static PlannerLayerName ParseLayerName(string raw)
        {
            if (raw == null)
            {
                return null;
            }
            var s = DuplicateSuffix.Replace(raw, string.Empty, 1).Trim(JavaScriptSpaceChars);
            var orderMatch = Order.Match(s);
            if (!orderMatch.Success)
            {
                return null;
            }
            var order = orderMatch.Groups[1].Success ? orderMatch.Groups[1].Value : orderMatch.Groups[2].Value;
            var rest = s.Substring(orderMatch.Length);
            // Two numbers: the first is the phase, the second the stage (LayerStage); one number is the stage, in the phase
            // its objects carry.
            var stageMatch = orderMatch.Groups[1].Success && LayerPhase.IsMatch(order) ? LayerStage.Match(rest) : Match.Empty;
            int? phase = stageMatch.Success ? ParseDigits(order) : (int?)null;
            var stageToken = stageMatch.Success ? stageMatch.Value : order;
            int stage;
            int? subStage;
            ParseStageNumber(stageToken, out stage, out subStage);
            var issues = new List<string>();

            // Dates are pinned to the end by convention, so the LAST two date tokens are the range; anything
            // date-like earlier in the name stays part of the name.
            var found = DateToken.Matches(rest);
            var tail = new List<Match>();
            for (int i = Math.Max(0, found.Count - 2); i < found.Count; i++)
            {
                tail.Add(found[i]);
            }
            var parsedText = new List<string>();
            var parsedDate = new List<DateTime?>();
            foreach (var m in tail)
            {
                var text = m.Value.Substring(m.Groups[1].Length); // drop the lead-in character the pattern consumed
                var date = LayerDate(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
                parsedText.Add(text);
                parsedDate.Add(date);
                if (date == null)
                {
                    issues.Add("unreadable date \"" + text + "\"");
                }
            }
            // Strip the date tokens, last match first so earlier indices stay valid.
            for (int i = tail.Count - 1; i >= 0; i--)
            {
                var m = tail[i];
                int at = m.Index + m.Groups[1].Length;
                int length = m.Length - m.Groups[1].Length;
                rest = rest.Substring(0, at) + rest.Substring(at + length);
            }

            bool tbc = TbcToken.IsMatch(rest);
            if (tbc)
            {
                rest = TbcToken.Replace(rest, "$1", 1);
            }

            // Everything after the first number (a two-number name's stage leads it), and the name after the ID.
            var afterOrder = Tidy(rest);
            var name = phase.HasValue ? Tidy(afterOrder.Length > stageToken.Length ? afterOrder.Substring(stageToken.Length) : string.Empty) : afterOrder;
            if (name.Length == 0)
            {
                issues.Add("no activity name");
            }

            var good = parsedDate.Where(d => d.HasValue).Select(d => d.Value).ToList();
            DateTime? start = good.Count > 0 ? good[0] : (DateTime?)null;
            DateTime? end = good.Count > 1 ? good[1] : start;
            if (start.HasValue && end.HasValue && end.Value < start.Value)
            {
                issues.Add("finish " + parsedText[1] + " is before start " + parsedText[0]);
                end = start;
            }
            if (!start.HasValue && !tbc)
            {
                issues.Add("no dates");
            }
            if (!start.HasValue)
            {
                end = null;
            }

            return new PlannerLayerName
            {
                Order = order,
                Phase = phase,
                Stage = stage,
                SubStage = subStage,
                Name = name,
                Label = afterOrder.Length > 0 ? order + "_" + afterOrder : order,
                Tbc = tbc || !start.HasValue || issues.Count > 0,
                Start = start,
                End = end,
                Issues = issues,
                Legacy = stageToken.IndexOf('.') >= 0
            };
        }

        // Underscore runs as one, no separators at either end, no spaces around (the Planner's tidy).
        private static string Tidy(string text)
        {
            return EdgeSeparators.Replace(Underscores.Replace(text, "_"), string.Empty).Trim(JavaScriptSpaceChars);
        }

        /// <summary>"yyyy-MM-dd", or null.</summary>
        public static string ToIsoDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>"03" gives stage 3; "03-2" and "03.2" give stage 3, sub-stage 2 (the Planner's parseStageNumber).</summary>
        public static void ParseStageNumber(string token, out int stage, out int? subStage)
        {
            var m = StageNumber.Match((token ?? string.Empty).Trim(JavaScriptSpaceChars));
            if (!m.Success)
            {
                stage = 0;
                subStage = null;
                return;
            }
            stage = ParseDigits(m.Groups[1].Value);
            subStage = m.Groups[2].Success ? ParseDigits(m.Groups[2].Value) : (int?)null;
        }

        /// <summary>
        /// Do two stage numbers name the same work? The stage must match; the sub-stages must agree unless one
        /// side has none: a whole stage covers its sub-stages, and a sub-stage belongs to its stage (so "St06"
        /// under group "06-1" still matches, as it always has). Mirrors the Planner's sameStage.
        /// </summary>
        public static bool SameStage(int stageA, int? subStageA, int stageB, int? subStageB)
        {
            return stageA == stageB && (!subStageA.HasValue || !subStageB.HasValue || subStageA.Value == subStageB.Value);
        }

        /// <summary>"St05", or "St05-1" - a stage as the convention writes it, with two digits.</summary>
        public static string StageText(int stage, int? subStage)
        {
            var text = "St" + stage.ToString("00", CultureInfo.InvariantCulture);
            return subStage.HasValue ? text + "-" + subStage.Value.ToString(CultureInfo.InvariantCulture) : text;
        }

        private static PlannerMeshSegment Segment(Match m)
        {
            int stage;
            int? subStage;
            ParseStageNumber(m.Groups[2].Value, out stage, out subStage);
            return new PlannerMeshSegment
            {
                PhaseToken = m.Groups[1].Value,
                StageToken = m.Groups[2].Value,
                Phase = ParseDigits(m.Groups[1].Value),
                Stage = stage,
                SubStage = subStage,
                Type = string.Equals(m.Groups[3].Value, "IN", StringComparison.OrdinalIgnoreCase) ? PlannerTaskType.Install : PlannerTaskType.Dismantle,
                Legacy = m.Groups[2].Value.IndexOf('.') >= 0
            };
        }

        private static DateTime? LayerDate(string dd, string mm, string yy)
        {
            int day = ParseDigits(dd);
            int month = ParseDigits(mm);
            int year = 2000 + ParseDigits(yy);
            if (month < 1 || month > 12 || day < 1 || day > 31)
            {
                return null;
            }
            if (day > DateTime.DaysInMonth(year, month))
            {
                return null; // e.g. 31-02-26
            }
            return new DateTime(year, month, day);
        }

        private static int ParseDigits(string digits)
        {
            int value;
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : int.MaxValue;
        }
    }
}
