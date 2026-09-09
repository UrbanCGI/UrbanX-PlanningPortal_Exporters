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
        /// <summary>The stage digits as written (e.g. "03"); the Planner keeps this spelling in activity names.</summary>
        public string StageToken;
        public int Phase;
        public int Stage;
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
        /// <summary>Stage number read off the order token ("06-1" gives 6): what an object's St&lt;n&gt; points at.</summary>
        public int Stage;
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
    /// an optional trailing tag ends it, and the free-form description sits between them.
    ///
    /// Group names carry the schedule: <c>N_&lt;Activity&gt;_DD-MM-YY[_DD-MM-YY]</c>, or <c>_TBC</c> while the dates
    /// are unknown (optionally followed by provisional dates). Parsing is deliberately lenient about what
    /// artists actually type (a space after N, spaces in the name, "06-1" sub-numbers, single-digit days,
    /// text after the dates, the " (1)" suffix on duplicate names) but the dates must read DD-MM-YY.
    /// </summary>
    public static class PlannerNaming
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        // Ph<n>_St<n>_<IN|RM> anchored at the start, with the type followed by "_" or the end of the name.
        private static readonly Regex Lead = new Regex(@"^Ph(\d+)_St(\d+)_(IN|RM)(?:_|$)", OptionsIgnoreCase);
        // ..._Ph<n>_St<n>_<IN|RM> pinned to the end, or followed only by a space-separated note.
        private static readonly Regex Trail = new Regex(@"_Ph(\d+)_St(\d+)_(IN|RM)(?=$|\s)", OptionsIgnoreCase);

        private static readonly Regex DuplicateSuffix = new Regex(@"\s*\(\d+\)\s*$", Options);
        private static readonly Regex Order = new Regex(@"^(\d+(?:-\d+)?)(?:[_ ]+|$)", Options);
        // DD-MM-YY not glued to other digits or dashes (so "06-1" order tokens and "100mm" never match).
        private static readonly Regex DateToken = new Regex(@"(^|[^\d-])(\d{1,2})-(\d{1,2})-(\d{2})(?![\d-])", Options);
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
            var order = orderMatch.Groups[1].Value;
            int stage = LeadingInteger(order);
            var rest = s.Substring(orderMatch.Length);
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
                Stage = stage,
                Name = name,
                Label = name.Length > 0 ? order + "_" + name : order,
                Tbc = tbc || !start.HasValue || issues.Count > 0,
                Start = start,
                End = end,
                Issues = issues
            };
        }

        /// <summary>"yyyy-MM-dd", or null.</summary>
        public static string ToIsoDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        private static PlannerMeshSegment Segment(Match m)
        {
            return new PlannerMeshSegment
            {
                PhaseToken = m.Groups[1].Value,
                StageToken = m.Groups[2].Value,
                Phase = ParseDigits(m.Groups[1].Value),
                Stage = ParseDigits(m.Groups[2].Value),
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

        // parseInt semantics: the leading run of digits ("06-1" gives 6).
        private static int LeadingInteger(string token)
        {
            int end = 0;
            while (end < token.Length && char.IsDigit(token[end]))
            {
                end++;
            }
            return ParseDigits(token.Substring(0, end));
        }

        private static int ParseDigits(string digits)
        {
            int value;
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : int.MaxValue;
        }
    }
}
