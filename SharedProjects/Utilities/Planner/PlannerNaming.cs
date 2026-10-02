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
        /// <summary>The stage as written (e.g. "03", or "03.2" with a sub-stage); the Planner keeps this spelling in activity names.</summary>
        public string StageToken;
        public int Phase;
        /// <summary>The stage number ("03.2" gives 3).</summary>
        public int Stage;
        /// <summary>The sub-stage ("03.2" gives 2), or null for a plain stage.</summary>
        public int? SubStage;
        public PlannerTaskType Type;

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
    }

    /// <summary>A group (3ds Max layer / group node) name read as an order number, activity name and dates.</summary>
    public sealed class PlannerLayerName
    {
        /// <summary>The order token as written, e.g. "01" or "06-1".</summary>
        public string Order;
        /// <summary>Stage number read off the order token ("06-1" and "06.1" give 6): what an object's St&lt;n&gt; points at.</summary>
        public int Stage;
        /// <summary>The sub-stage read off a sub-numbered order token ("06-1" gives 1), or null: the group covers its whole stage.</summary>
        public int? SubStage;
        /// <summary>The activity name with order, dates and TBC stripped.</summary>
        public string Name;
        /// <summary>"&lt;order&gt;_&lt;name&gt;": the stable label the Planner groups by.</summary>
        public string Label;
        /// <summary>Dates still need confirming: a TBC token, no readable dates, or any issue.</summary>
        public bool Tbc;
        public DateTime? Start;
        /// <summary>Inclusive last day; equals Start for a single day. Null when undated.</summary>
        public DateTime? End;
        public List<string> Issues = new List<string>();
    }

    /// <summary>
    /// C# port of the Planner's <c>packages/domain/src/phasingNaming.ts</c>. The two must stay in step:
    /// whatever this parser accepts is exactly what the Planner turns into folders and activities once the
    /// GLB is uploaded, so the exporter can tell the artist before the export what the Planner will see.
    ///
    /// Object names carry one or two tags <c>Ph&lt;n&gt;_St&lt;n&gt;_IN|RM</c>; the leading tag starts the name,
    /// an optional trailing tag ends it, and the free-form description sits between them. A stage may carry
    /// a sub-stage, <c>St&lt;n&gt;.&lt;m&gt;</c> ("St03.2"): work inside stage 3 with its own place in the order,
    /// 3 &lt; 3.1 &lt; 3.2 &lt; 4.
    ///
    /// Group names carry the schedule: <c>N_&lt;Activity&gt;_DD-MM-YY[_DD-MM-YY]</c>, or <c>_TBC</c> while the dates
    /// are unknown (optionally followed by provisional dates). A sub-numbered N ("06-1" or "06.1") is the
    /// sub-stage an object's "St06.1" points at; a plain N covers its whole stage. Parsing is deliberately
    /// lenient about what artists actually type (a space after N, spaces in the name, single-digit days,
    /// text after the dates, the " (1)" suffix on duplicate names) but the dates must read DD-MM-YY.
    /// </summary>
    public static class PlannerNaming
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        // [0-9] rather than \d throughout: .NET's \d matches every Unicode digit, the Planner's JavaScript \d
        // only ASCII digits, and the port must read exactly what the Planner reads.
        // Ph<n>_St<n>[.<m>]_<IN|RM> anchored at the start, with the type followed by "_" or the end of the name.
        private static readonly Regex Lead = new Regex(@"^Ph([0-9]+)_St([0-9]+(?:\.[0-9]+)?)_(IN|RM)(?:_|$)", OptionsIgnoreCase);
        // ..._Ph<n>_St<n>[.<m>]_<IN|RM> pinned to the end, or followed only by a space-separated note.
        private static readonly Regex Trail = new Regex(@"_Ph([0-9]+)_St([0-9]+(?:\.[0-9]+)?)_(IN|RM)(?=$|\s)", OptionsIgnoreCase);

        internal static readonly Regex DuplicateSuffix = new Regex(@"\s*\([0-9]+\)\s*$", Options);
        // "01" or a dashed sub-number "06-1", followed by an underscore or a space (or nothing at all); a dotted
        // sub-number "06.1" only when an underscore follows, so a dimension-led group name that is not a layer
        // ("2.4 High Hoarding", "1.8 x 2.4 Heras Panel") is never read as one.
        private static readonly Regex Order = new Regex(@"^([0-9]+(?:-[0-9]+)?)(?:[_ ]+|$)|^([0-9]+\.[0-9]+)(?:_+|$)", Options);
        // A stage token: the stage digits, then an optional sub-stage after "." (objects) or "-" / "." (groups).
        private static readonly Regex StageNumber = new Regex(@"^([0-9]+)(?:[.-]([0-9]+))?", Options);
        // DD-MM-YY not glued to other digits or dashes (so "06-1" order tokens and "100mm" never match).
        private static readonly Regex DateToken = new Regex(@"(^|[^0-9-])([0-9]{1,2})-([0-9]{1,2})-([0-9]{2})(?![0-9-])", Options);
        private static readonly Regex TbcToken = new Regex(@"(^|[_ \-])TBC(?=$|[_ \-])", OptionsIgnoreCase);
        private static readonly Regex Underscores = new Regex(@"_+", Options);
        private static readonly Regex EdgeSeparators = new Regex(@"^[_ ]+|[_ ]+$", Options);

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
            var s = DuplicateSuffix.Replace(raw, string.Empty, 1).Trim();
            var orderMatch = Order.Match(s);
            if (!orderMatch.Success)
            {
                return null;
            }
            var order = orderMatch.Groups[1].Success ? orderMatch.Groups[1].Value : orderMatch.Groups[2].Value;
            int stage;
            int? subStage;
            ParseStageNumber(order, out stage, out subStage);
            var tokens = ReadScheduleTokens(s.Substring(orderMatch.Length), true);
            var name = tokens.Name;

            return new PlannerLayerName
            {
                Order = order,
                Stage = stage,
                SubStage = subStage,
                Name = name,
                Label = name.Length > 0 ? order + "_" + name : order,
                Tbc = tokens.Tbc || !tokens.Start.HasValue || tokens.Issues.Count > 0,
                Start = tokens.Start,
                End = tokens.End,
                Issues = tokens.Issues
            };
        }

        /// <summary>What <see cref="ReadScheduleTokens"/> found in the text after a group's number.</summary>
        internal sealed class ScheduleTokens
        {
            /// <summary>The text with the date and TBC tokens removed, underscores collapsed and the edges trimmed.</summary>
            public string Name;
            public DateTime? Start;
            /// <summary>Inclusive last day; equals Start for a single day. Null when undated.</summary>
            public DateTime? End;
            /// <summary>True when a TBC token was present (the token alone, not the missing-dates fallback).</summary>
            public bool Tbc;
            public List<string> Issues = new List<string>();
        }

        /// <summary>
        /// The date and TBC rules of <see cref="ParseLayerName"/>, applied to the text after the order number: the
        /// last one or two DD-MM-YY tokens are the range, a TBC token anywhere marks the dates unconfirmed. Shared
        /// with the legacy layer reader of <see cref="PlannerCodes"/>, which must strip exactly the same tokens.
        /// <paramref name="datesExpected"/> adds the "no dates" issue when neither dates nor TBC were found.
        /// </summary>
        internal static ScheduleTokens ReadScheduleTokens(string rest, bool datesExpected)
        {
            rest = rest ?? string.Empty;
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

            var name = EdgeSeparators.Replace(Underscores.Replace(rest, "_"), string.Empty).Trim();
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
            if (!start.HasValue && !tbc && datesExpected)
            {
                issues.Add("no dates");
            }
            if (!start.HasValue)
            {
                end = null;
            }

            return new ScheduleTokens
            {
                Name = name,
                Start = start,
                End = end,
                Tbc = tbc,
                Issues = issues
            };
        }

        /// <summary>"yyyy-MM-dd", or null.</summary>
        public static string ToIsoDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>"03" gives stage 3; "03.2" and "03-2" give stage 3, sub-stage 2 (the Planner's parseStageNumber).</summary>
        public static void ParseStageNumber(string token, out int stage, out int? subStage)
        {
            var m = StageNumber.Match((token ?? string.Empty).Trim());
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

        /// <summary>"St05", or "St05.1" - a stage as the convention writes it, with two digits.</summary>
        public static string StageText(int stage, int? subStage)
        {
            var text = "St" + stage.ToString("00", CultureInfo.InvariantCulture);
            return subStage.HasValue ? text + "." + subStage.Value.ToString(CultureInfo.InvariantCulture) : text;
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
                Type = string.Equals(m.Groups[3].Value, "IN", StringComparison.OrdinalIgnoreCase) ? PlannerTaskType.Install : PlannerTaskType.Dismantle
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
