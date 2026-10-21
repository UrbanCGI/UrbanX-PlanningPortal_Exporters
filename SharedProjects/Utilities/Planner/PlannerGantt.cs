using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>How much time a column of the Schedule tab's Gantt stands for.</summary>
    public enum PlannerGanttZoom
    {
        /// <summary>A column per day, as the Planner's Gantt-to-Excel export draws it.</summary>
        Days,
        /// <summary>A third of a day's width per day: a week to a column of day labels.</summary>
        Weeks,
        /// <summary>A ninth of a day's width per day: a month to a column of day labels.</summary>
        Months
    }

    /// <summary>What a row's span stands for when it is not its own work.</summary>
    public enum PlannerGanttSpanKind
    {
        None,
        /// <summary>Before Fill IN / RM: a layer's own dates (from the Planner's list), in the neutral colour.</summary>
        Neutral,
        /// <summary>A group (or a phasing root): a grey span over everything inside it, as the Planner's export draws a folder.</summary>
        Group,
        /// <summary>After Fill: a dated layer whose dates place no work, its own dates outlined.</summary>
        Outline
    }

    /// <summary>A run of days in a row, from day <see cref="First"/> (an index into the chart's days), <see cref="Count"/> days long.</summary>
    public struct PlannerGanttRun
    {
        public int First;
        public int Count;
        /// <summary>Dates to be confirmed: hatched.</summary>
        public bool Tbc;

        public int Last
        {
            get { return First + Count - 1; }
        }

        public override string ToString()
        {
            return First.ToString(CultureInfo.InvariantCulture) + "+" + Count.ToString(CultureInfo.InvariantCulture) + (Tbc ? " TBC" : string.Empty);
        }
    }

    /// <summary>One activity of a row and the days it runs, as the Planner's scheduler dates it.</summary>
    public sealed class PlannerGanttWork
    {
        public PlannerScheduleActivity Activity;
        /// <summary>Its start date: the day of its first minute.</summary>
        public DateTime FirstDay;
        /// <summary>Its end date: the day of its last minute (state/schedule.ts: the calendar day of the last occupied minute).</summary>
        public DateTime LastDay;
        /// <summary>Hatched as the Planner's export hatches it: its own flag, or its layer's folder's.</summary>
        public bool Tbc;

        public bool Install
        {
            get { return Activity.Type == PlannerTaskType.Install; }
        }
    }

    /// <summary>One row of the Gantt: the tab's row, and what it draws.</summary>
    public sealed class PlannerGanttRow
    {
        public PlannerScheduleRow Row;
        /// <summary>The row it sits under in the tree (by depth), or -1.</summary>
        public int Parent = -1;
        /// <summary>Rows sit under it: it can be collapsed.</summary>
        public bool HasChildren;
        /// <summary>A group or a phasing root (<see cref="PlannerScheduleRow.IsGroup"/>).</summary>
        public bool IsGroup;
        /// <summary>Its own dates, from the Planner's list (the row's Start and Finish); null when it has none.</summary>
        public DateTime? OwnStart;
        public DateTime? OwnEnd;
        /// <summary>Its own dates are to be confirmed (the list says TBC, or a group above it does).</summary>
        public bool OwnTbc;
        /// <summary>
        /// Where its own dates come from (spec 5.8): the Planner's list, or none; never its name, and never 3ds Max (D20).
        /// </summary>
        public PlannerDateSource DateSource;
        public PlannerGanttSpanKind SpanKind;
        /// <summary>With <see cref="SpanKind"/>: the days it spans.</summary>
        public PlannerGanttRun Span;
        /// <summary>The days its install work (IN) runs, and its removal work (RM).</summary>
        public readonly List<PlannerGanttRun> Install = new List<PlannerGanttRun>();
        public readonly List<PlannerGanttRun> Removal = new List<PlannerGanttRun>();
        /// <summary>Its activities (IN first, then RM, each in the Planner's order) with their days.</summary>
        public readonly List<PlannerGanttWork> Work = new List<PlannerGanttWork>();

        public bool HasWork
        {
            get { return Install.Count > 0 || Removal.Count > 0; }
        }
    }

    /// <summary>A cell of the Gantt's header: a label over a run of days.</summary>
    public sealed class PlannerGanttHeaderCell
    {
        public int First;
        public int Count;
        public string Label;
        /// <summary>A shorter label, for when the long one does not fit.</summary>
        public string ShortLabel;
        /// <summary>A Saturday or Sunday (day columns only): shaded.</summary>
        public bool Weekend;
        /// <summary>All its days are in the scrubber's TBC zone, where removals for a layer nobody has are parked (<see cref="PlannerGanttModel.InTbcZone"/>).</summary>
        public bool TbcZone;

        public int Last
        {
            get { return First + Count - 1; }
        }
    }

    /// <summary>
    /// The colours of the Schedule tab's Gantt, the Planner's own (RRGGBB): the Gantt-to-Excel export's (apps/web/src/lib/
    /// ganttExport.ts) and the in-app Gantt's (config/activity.ts, styles.css).
    /// </summary>
    public static class PlannerGanttColours
    {
        /// <summary>
        /// Install work: the Planner's NEUTRAL_COLOUR, the colour of an activity with none of its own, which is what
        /// Generate from model makes (installs take their own colour in the Gantt).
        /// </summary>
        public const string Install = "43B04A";
        /// <summary>Removal work: DISMANTLE_COLOUR, "Dismantle/removal tasks always read red in the Gantt".</summary>
        public const string Removal = "E23B3B";
        /// <summary>The not-yet-done share of a bar: the colour mixed 45% towards white (the export's tint).</summary>
        public static readonly string InstallTint = MixHex(Install, "FFFFFF", 0.45);
        public static readonly string RemovalTint = MixHex(Removal, "FFFFFF", 0.45);
        /// <summary>The header (HEAD_FILL) and its weekend days (WEEKEND_FILL).</summary>
        public const string Header = "E7EBF0";
        public const string WeekendHeader = "CFD5DE";
        /// <summary>The body's weekend columns: the header's weekend grey, lighter.</summary>
        public static readonly string WeekendBody = MixHex(WeekendHeader, "FFFFFF", 0.6);
        /// <summary>A group's span (FOLDER_FILL), and a TBC group's hatch (FOLDER_HATCH over HEAD_FILL).</summary>
        public const string Group = "D9DEE5";
        public const string GroupHatch = "9AA5B4";
        /// <summary>A layer's own dates before Fill IN / RM (and their outline after, when they place no work): the folder hatch's slate.</summary>
        public const string Neutral = "9AA5B4";
        public static readonly string NeutralTint = MixHex(Neutral, "FFFFFF", 0.45);
        /// <summary>The scrubber's line (the Planner's accent, its playhead) and today's (the in-app Gantt's grey).</summary>
        public const string Scrubber = "F7921D";
        public const string Today = "7F8A99";
        /// <summary>The scrubber's TBC zone in the header (the days after the works end it runs over): its shading.</summary>
        public const string TbcZone = "FFECD6";

        /// <summary>The Planner's mixHex: <paramref name="hex"/> blended towards <paramref name="towards"/> by t (0 = hex, 1 = towards), each channel rounded as JavaScript's Math.round.</summary>
        public static string MixHex(string hex, string towards, double t)
        {
            int a = Parse(hex, 0x000000);
            int b = Parse(towards, 0xFFFFFF);
            var text = string.Empty;
            foreach (var shift in new[] { 16, 8, 0 })
            {
                double value = ((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t;
                text += ((int)Math.Floor(value + 0.5)).ToString("X2", CultureInfo.InvariantCulture);
            }
            return text;
        }

        /// <summary>RRGGBB (with or without #) as a number; the fallback when it does not read.</summary>
        public static int Parse(string hex, int fallback)
        {
            var text = (hex ?? string.Empty).TrimStart('#');
            int value;
            return text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }

    /// <summary>
    /// The Schedule tab's Gantt: one row per row of the tab's tree (same rows, same order), one column per calendar day, as
    /// the Planner's Gantt-to-Excel export draws its activities, each layer row carrying its own IN and RM work (the days
    /// its install activities run and the days its removal activities run, as the Planner's scheduler dates them: the
    /// start date to the day of the last minute), TBC work hatched, groups a grey span over everything inside them, and
    /// before Fill IN / RM each layer's own dates in a neutral colour. The days run from the earliest to the latest day
    /// anything is drawn on, and include the scrubber's whole range once filled. Pure logic, no 3ds Max: the control in
    /// the 3ds Max project paints it.
    /// </summary>
    public sealed class PlannerGanttModel
    {
        private static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        private const int MostWorkLines = 8;

        /// <summary>The first day (index 0) and how many days the chart has; none when nothing is dated.</summary>
        public DateTime FirstDay;
        public int DayCount;
        /// <summary>Filled IN / RM: the rows carry their work, and the scrubber runs over <see cref="ScrubFirst"/> to <see cref="ScrubLast"/>.</summary>
        public bool Filled;
        public DateTime ScrubFirst;
        public DateTime ScrubLast;
        /// <summary>The last day of the works (a dated layer's end), when filled; after it, to the scrubber's end, is the TBC zone.</summary>
        public DateTime? WorksEnd;
        public DateTime Today;
        /// <summary>On the new names, what the works end at, the day after which the TBC zone starts (<see cref="PlannerSchedule.WorksEndText"/>).</summary>
        public string WorksEndText = "the last dated work";
        /// <summary>On the new names, the list has rows on one TBC day, which the Planner may still have parked: a layer the preview parks may park elsewhere there.</summary>
        public bool MayParkElsewhere;
        public readonly List<PlannerGanttRow> Rows = new List<PlannerGanttRow>();

        public bool Empty
        {
            get { return DayCount <= 0; }
        }

        public DateTime LastDay
        {
            get { return FirstDay.AddDays(Math.Max(0, DayCount - 1)); }
        }

        /// <summary>The day's index (it may be outside the chart: below 0 or from <see cref="DayCount"/>).</summary>
        public int IndexOf(DateTime day)
        {
            return (int)Math.Round((day.Date - FirstDay).TotalDays);
        }

        public DateTime DayAt(int index)
        {
            return FirstDay.AddDays(index);
        }

        /// <summary>Today's index, or -1 when today is not on the chart.</summary>
        public int TodayIndex
        {
            get
            {
                int i = IndexOf(Today);
                return i >= 0 && i < DayCount ? i : -1;
            }
        }

        public bool IsWeekend(int index)
        {
            var day = DayAt(index).DayOfWeek;
            return day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
        }

        /// <summary>
        /// The day is in the scrubber's TBC zone: after the works end and within the scrubber's range, where the removals for
        /// a layer nobody has are parked (the day after the works end). The chart may run further, over the dates of layers
        /// that hold no work yet (a list row not in the scene, an empty layer), which is not the zone.
        /// </summary>
        public bool InTbcZone(int index)
        {
            var day = DayAt(index);
            return Filled && WorksEnd.HasValue && day > WorksEnd.Value && day <= ScrubLast;
        }

        /// <summary>The TBC zone's days on the chart (<see cref="InTbcZone"/>), first to last; false when it has none.</summary>
        public bool TbcZoneDays(out int first, out int last)
        {
            first = last = -1;
            if (Empty || !Filled || !WorksEnd.HasValue)
            {
                return false;
            }
            first = Math.Max(0, IndexOf(WorksEnd.Value) + 1);
            last = Math.Min(DayCount - 1, IndexOf(ScrubLast));
            return first <= last;
        }

        /// <summary>A day for the scrubber: the day, kept within its range (the chart can run wider).</summary>
        public DateTime ClampToScrubber(DateTime day)
        {
            if (!Filled)
            {
                return day.Date;
            }
            return day.Date < ScrubFirst ? ScrubFirst : day.Date > ScrubLast ? ScrubLast : day.Date;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Building

        /// <summary>
        /// The chart of <paramref name="rows"/> (the tab's rows, in its order): filled when <paramref name="schedule"/> is
        /// given and usable (the rows' IN and RM are then its activities), else each layer's own dates.
        /// </summary>
        public static PlannerGanttModel Build(IList<PlannerScheduleRow> rows, PlannerSchedule schedule, DateTime today)
        {
            var model = new PlannerGanttModel { Today = today.Date };
            model.Filled = schedule != null && schedule.Usable;
            rows = rows ?? new List<PlannerScheduleRow>();

            // The tree by depth: each row's parent, and which rows have rows under them.
            var open = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                while (open.Count > 0 && rows[open[open.Count - 1]].Depth >= row.Depth)
                {
                    open.RemoveAt(open.Count - 1);
                }
                var g = new PlannerGanttRow
                {
                    Row = row,
                    Parent = open.Count > 0 ? open[open.Count - 1] : -1,
                    // A phasing root draws as a group does: a span over what it holds.
                    IsGroup = row.IsGroup
                };
                if (g.Parent >= 0)
                {
                    model.Rows[g.Parent].HasChildren = true;
                }
                OwnDates(g);
                // TBC follows the folders down, as the export's outline does.
                if (g.Parent >= 0 && model.Rows[g.Parent].OwnTbc)
                {
                    g.OwnTbc = true;
                }
                model.Rows.Add(g);
                open.Add(i);
            }

            // Each row's work: its IN, then its RM, as the Planner's scheduler dates them.
            DateTime? first = null;
            DateTime? last = null;
            Action<DateTime, DateTime> take = (from, to) =>
            {
                if (!first.HasValue || from < first.Value)
                {
                    first = from;
                }
                if (!last.HasValue || to > last.Value)
                {
                    last = to;
                }
            };
            if (model.Filled)
            {
                DateTime spanFirst, spanLast;
                schedule.Span(out spanFirst, out spanLast);
                model.ScrubFirst = spanFirst;
                model.ScrubLast = spanLast;
                model.WorksEnd = schedule.WorksEnd;
                model.WorksEndText = schedule.WorksEndText;
                model.MayParkElsewhere = schedule.V2 != null && schedule.V2.OneTbcDayRows.Count > 0;
                take(spanFirst, spanLast);
                foreach (var g in model.Rows)
                {
                    foreach (var activity in g.Row.In.Concat(g.Row.Rm))
                    {
                        var work = WorkOf(schedule, activity);
                        g.Work.Add(work);
                        take(work.FirstDay, work.LastDay);
                    }
                }
            }
            foreach (var g in model.Rows.Where(r => !r.IsGroup && r.OwnStart.HasValue))
            {
                take(g.OwnStart.Value, g.OwnEnd.Value);
            }
            if (!first.HasValue)
            {
                return model;
            }
            model.FirstDay = first.Value;
            model.DayCount = (int)Math.Round((last.Value - first.Value).TotalDays) + 1;

            // The runs: each layer row's IN and RM days (a day with dated work is not hatched), or its own dates.
            foreach (var g in model.Rows)
            {
                if (g.IsGroup)
                {
                    continue;
                }
                if (g.Work.Count > 0)
                {
                    g.Install.AddRange(model.RunsOf(g.Work.Where(w => w.Install)));
                    g.Removal.AddRange(model.RunsOf(g.Work.Where(w => !w.Install)));
                }
                else if (g.OwnStart.HasValue)
                {
                    g.SpanKind = model.Filled ? PlannerGanttSpanKind.Outline : PlannerGanttSpanKind.Neutral;
                    g.Span = new PlannerGanttRun
                    {
                        First = model.IndexOf(g.OwnStart.Value),
                        Count = (int)Math.Round((g.OwnEnd.Value - g.OwnStart.Value).TotalDays) + 1,
                        Tbc = g.OwnTbc
                    };
                }
            }

            // A group spans everything inside it, nested groups included (the rows under a row come after it).
            var lo = new int[model.Rows.Count];
            var hi = new int[model.Rows.Count];
            for (int i = 0; i < model.Rows.Count; i++)
            {
                lo[i] = int.MaxValue;
                hi[i] = int.MinValue;
                var g = model.Rows[i];
                foreach (var run in g.Install.Concat(g.Removal))
                {
                    lo[i] = Math.Min(lo[i], run.First);
                    hi[i] = Math.Max(hi[i], run.Last);
                }
                if (g.SpanKind != PlannerGanttSpanKind.None)
                {
                    lo[i] = Math.Min(lo[i], g.Span.First);
                    hi[i] = Math.Max(hi[i], g.Span.Last);
                }
            }
            for (int i = model.Rows.Count - 1; i >= 0; i--)
            {
                var g = model.Rows[i];
                if (g.IsGroup && lo[i] <= hi[i])
                {
                    g.SpanKind = PlannerGanttSpanKind.Group;
                    g.Span = new PlannerGanttRun { First = lo[i], Count = hi[i] - lo[i] + 1, Tbc = g.OwnTbc };
                }
                if (g.Parent >= 0 && lo[i] <= hi[i])
                {
                    lo[g.Parent] = Math.Min(lo[g.Parent], lo[i]);
                    hi[g.Parent] = Math.Max(hi[g.Parent], hi[i]);
                }
            }
            return model;
        }

        /// <summary>
        /// The days an activity runs, as the Planner's scheduler gives its start and end dates (state/schedule.ts): the
        /// day of the instant its start minute falls on after the project start, and the day of its last minute.
        /// </summary>
        public static PlannerGanttWork WorkOf(PlannerSchedule schedule, PlannerScheduleActivity activity)
        {
            long duration = Math.Max(1, activity.Duration);
            var from = schedule.LocalTime(schedule.AnchorMs + activity.StartMinute * 60000L).Date;
            var to = schedule.LocalTime(schedule.AnchorMs + (activity.StartMinute + duration - 1) * 60000L).Date;
            return new PlannerGanttWork
            {
                Activity = activity,
                FirstDay = from,
                LastDay = to < from ? from : to,
                // The export's TBC: the activity's own flag, or its folder's (its home layer's TBC), as buildScheduleRows reads it.
                Tbc = activity.Tbc || (activity.Home != null && activity.Home.Tbc)
            };
        }

        // A row's own dates: the list's, as its one date reader reads them (the rows carry them), never a name's (spec 5.8) and
        // never 3ds Max's (D20). TBC is the row's, which covers a parked layer too once filled (PlannerScheduleRows' Fill). A
        // row on the old names has none: the tab draws no Gantt on the old names (D23) and keeps the model only for its folds.
        private static void OwnDates(PlannerGanttRow g)
        {
            var row = g.Row;
            if (!g.IsGroup && (row.StartDate.HasValue || row.FinishDate.HasValue))
            {
                g.OwnStart = (row.StartDate ?? row.FinishDate).Value.Date;
                g.OwnEnd = (row.FinishDate ?? row.StartDate).Value.Date;
                if (g.OwnEnd.Value < g.OwnStart.Value)
                {
                    g.OwnEnd = g.OwnStart;
                }
                g.DateSource = row.DateSource;
            }
            g.OwnTbc = row.Tbc;
        }

        // The days the work runs, as runs: a day with any dated (not TBC) work is dated, else TBC.
        private List<PlannerGanttRun> RunsOf(IEnumerable<PlannerGanttWork> work)
        {
            var days = new byte[DayCount];
            foreach (var w in work)
            {
                int from = Math.Max(0, IndexOf(w.FirstDay));
                int to = Math.Min(DayCount - 1, IndexOf(w.LastDay));
                byte mark = w.Tbc ? (byte)2 : (byte)1;
                for (int d = from; d <= to; d++)
                {
                    days[d] = days[d] == 0 ? mark : Math.Min(days[d], mark);
                }
            }
            var runs = new List<PlannerGanttRun>();
            for (int d = 0; d < DayCount;)
            {
                if (days[d] == 0)
                {
                    d++;
                    continue;
                }
                int end = d;
                while (end + 1 < DayCount && days[end + 1] == days[d])
                {
                    end++;
                }
                runs.Add(new PlannerGanttRun { First = d, Count = end - d + 1, Tbc = days[d] == 2 });
                d = end + 1;
            }
            return runs;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Rows shown

        /// <summary>A row's key for remembering it collapsed across readings of the scene: its Planner ID, else its name.</summary>
        public static string KeyOf(PlannerScheduleRow row)
        {
            if (row.ListRow != null && !string.IsNullOrEmpty(row.ListRow.PlannerId))
            {
                return "id:" + row.ListRow.PlannerId;
            }
            return row.Source.ToString() + ":" + (row.LayerName ?? row.Name ?? string.Empty);
        }

        /// <summary>Which rows show, given which are collapsed: a row shows when no row above it in the tree is collapsed.</summary>
        public static bool[] Shown(IList<PlannerScheduleRow> rows, Func<int, bool> collapsed)
        {
            var shown = new bool[rows.Count];
            // The open rows above the current one: their depth, and whether what is under them is hidden.
            var depths = new List<int>();
            var hiding = new List<bool>();
            for (int i = 0; i < rows.Count; i++)
            {
                while (depths.Count > 0 && depths[depths.Count - 1] >= rows[i].Depth)
                {
                    depths.RemoveAt(depths.Count - 1);
                    hiding.RemoveAt(hiding.Count - 1);
                }
                bool hidden = hiding.Count > 0 && hiding[hiding.Count - 1];
                shown[i] = !hidden;
                depths.Add(rows[i].Depth);
                hiding.Add(hidden || collapsed(i));
            }
            return shown;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Drawing helpers

        /// <summary>
        /// A day's width at a zoom, from the width a day's column takes at the Days zoom (its number written whole): a
        /// third of it for weeks, a ninth for months.
        /// </summary>
        public static double DayWidth(PlannerGanttZoom zoom, double dayColumn)
        {
            switch (zoom)
            {
                case PlannerGanttZoom.Weeks:
                    return dayColumn / 3.0;
                case PlannerGanttZoom.Months:
                    return dayColumn / 9.0;
                default:
                    return dayColumn;
            }
        }

        /// <summary>The day under an x position (from the chart's left edge, scrolled), or -1 when off the chart.</summary>
        public int DayAtX(double x, double dayWidth)
        {
            if (Empty || dayWidth <= 0 || x < 0)
            {
                return -1;
            }
            int day = (int)Math.Floor(x / dayWidth);
            return day < DayCount ? day : -1;
        }

        /// <summary>
        /// The days before which a run is drawn solid (the work done by the scrubber's instant, as the in-app Gantt fills
        /// a bar up to its playhead), the rest as the tint: the scrubber's day itself counts once its end is shown.
        /// </summary>
        public int DoneBefore(DateTime scrubDay, bool endOfDay)
        {
            return IndexOf(scrubDay) + (endOfDay ? 1 : 0);
        }

        /// <summary>A run split at <paramref name="doneBefore"/>: its done part, then the rest (either may be empty: Count 0).</summary>
        public static void Split(PlannerGanttRun run, int doneBefore, out PlannerGanttRun done, out PlannerGanttRun rest)
        {
            int cut = Math.Max(run.First, Math.Min(run.First + run.Count, doneBefore));
            done = new PlannerGanttRun { First = run.First, Count = cut - run.First, Tbc = run.Tbc };
            rest = new PlannerGanttRun { First = cut, Count = run.First + run.Count - cut, Tbc = run.Tbc };
        }

        /// <summary>
        /// The header's two rows at a zoom: months over days (Days, as the export's two header rows), months over weeks
        /// starting on Monday (Weeks), years over months (Months).
        /// </summary>
        public void Header(PlannerGanttZoom zoom, out List<PlannerGanttHeaderCell> top, out List<PlannerGanttHeaderCell> bottom)
        {
            top = new List<PlannerGanttHeaderCell>();
            bottom = new List<PlannerGanttHeaderCell>();
            if (Empty)
            {
                return;
            }
            if (zoom == PlannerGanttZoom.Months)
            {
                top.AddRange(Spans(d => d.Year, d => d.Year.ToString(CultureInfo.InvariantCulture), d => d.Year.ToString(CultureInfo.InvariantCulture).Substring(2)));
                bottom.AddRange(Spans(d => d.Year * 12 + d.Month, d => MonthNames[d.Month - 1], d => MonthNames[d.Month - 1].Substring(0, 1)));
                return;
            }
            top.AddRange(Spans(d => d.Year * 12 + d.Month, d => MonthNames[d.Month - 1] + " " + d.Year.ToString(CultureInfo.InvariantCulture), d => MonthNames[d.Month - 1]));
            if (zoom == PlannerGanttZoom.Weeks)
            {
                // A week runs Monday to Sunday: its label is the day of the month it starts on (the chart's first day for the first).
                bottom.AddRange(Spans(d => (int)Math.Floor((d - new DateTime(2001, 1, 1)).TotalDays / 7.0), d => d.Day.ToString(CultureInfo.InvariantCulture), d => d.Day.ToString(CultureInfo.InvariantCulture)));
                return;
            }
            for (int i = 0; i < DayCount; i++)
            {
                var day = DayAt(i);
                var label = day.Day.ToString(CultureInfo.InvariantCulture);
                bottom.Add(new PlannerGanttHeaderCell { First = i, Count = 1, Label = label, ShortLabel = label, Weekend = IsWeekend(i), TbcZone = InTbcZone(i) });
            }
        }

        // The runs of days with the same key, labelled by their first day.
        private IEnumerable<PlannerGanttHeaderCell> Spans(Func<DateTime, int> key, Func<DateTime, string> label, Func<DateTime, string> shortLabel)
        {
            for (int i = 0; i < DayCount;)
            {
                var day = DayAt(i);
                int k = key(day);
                int j = i;
                while (j + 1 < DayCount && key(DayAt(j + 1)) == k)
                {
                    j++;
                }
                // In the zone when all its days are (the view shades the zone's own days at these zooms).
                yield return new PlannerGanttHeaderCell { First = i, Count = j - i + 1, Label = label(day), ShortLabel = shortLabel(day), TbcZone = InTbcZone(i) && InTbcZone(j) };
                i = j + 1;
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // The tooltip

        /// <summary>
        /// What a cell says when hovered: the row (its layer), the date, and what installs and removes that day (each
        /// activity at work, with its objects and dates); for a header cell (row -1) the date. Null off the chart.
        /// </summary>
        public string Describe(int rowIndex, int dayIndex, CultureInfo culture)
        {
            if (Empty || dayIndex < 0 || dayIndex >= DayCount || rowIndex >= Rows.Count)
            {
                return null;
            }
            culture = culture ?? CultureInfo.CurrentCulture;
            var day = DayAt(dayIndex);
            var dayLine = day.ToString("ddd d MMM yyyy", culture) + (IsWeekend(dayIndex) ? " (weekend)" : string.Empty) + (day == Today ? ", today" : string.Empty);
            // A layer row whose own dates cover the day is not said to be in the zone there: its dates are its own.
            var own = rowIndex >= 0 && rowIndex < Rows.Count ? Rows[rowIndex] : null;
            bool ownDay = own != null && !own.IsGroup && own.OwnStart.HasValue && own.OwnStart.Value <= day && day <= own.OwnEnd.Value;
            if (InTbcZone(dayIndex) && !ownDay)
            {
                dayLine += ": after " + WorksEndText + ", the TBC zone where removals for a layer nobody has are parked";
            }
            var lines = new List<string>();
            if (rowIndex < 0)
            {
                lines.Add(dayLine);
                lines.Add(Filled ? "Click to move the scrubber to this day." : "Fill IN / RM from names to scrub through the schedule.");
                return string.Join("\r\n", lines.ToArray());
            }
            var g = Rows[rowIndex];
            lines.Add(g.Row.Name);
            lines.Add(dayLine);
            if (g.IsGroup)
            {
                lines.Add(g.SpanKind == PlannerGanttSpanKind.Group
                    ? "Everything inside it runs " + Range(DayAt(g.Span.First), DayAt(g.Span.Last), culture) + (g.Span.Tbc ? ", TBC" : string.Empty) + "."
                    : "Nothing inside it is dated.");
                return string.Join("\r\n", lines.ToArray());
            }
            lines.Add(DatesFrom(g, culture, MayParkElsewhere));
            // Why the tree beside highlights the row TBC (D21), in the same words as its tooltip.
            var tbc = PlannerScheduleRows.TbcText(g.Row.TbcMark);
            if (tbc != null)
            {
                lines.Add(tbc);
            }
            if (!Filled)
            {
                lines.Add("Fill IN / RM from names to see what it installs and removes.");
                return string.Join("\r\n", lines.ToArray());
            }
            if (g.Work.Count == 0)
            {
                lines.Add(g.OwnStart.HasValue
                    ? "Its dates (" + Range(g.OwnStart.Value, g.OwnEnd.Value, culture) + ") place no install or removal."
                    : "No install or removal is placed by this row.");
                return string.Join("\r\n", lines.ToArray());
            }
            var atWork = g.Work.Where(w => w.FirstDay <= day && day <= w.LastDay).ToList();
            if (atWork.Count == 0)
            {
                lines.Add("Nothing installs or removes on this day.");
                return string.Join("\r\n", lines.ToArray());
            }
            foreach (var w in atWork.Take(MostWorkLines))
            {
                int count = w.Activity.Members.Count;
                lines.Add((w.Install ? "IN " : "RM ") + w.Activity.Name + ": " + count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " object, " : " objects, ")
                    + Range(w.FirstDay, w.LastDay, culture) + (w.Tbc ? ", TBC" : string.Empty)
                    + (w.Install ? " (shows from its start)" : " (hidden from its finish)"));
            }
            if (atWork.Count > MostWorkLines)
            {
                lines.Add("\u2026 and " + (atWork.Count - MostWorkLines).ToString(CultureInfo.InvariantCulture) + " more.");
            }
            return string.Join("\r\n", lines.ToArray());
        }

        // On the new names, a layer row's dates and where they come from (spec 5.8): "the Planner" or "none" (no date is set
        // in 3ds Max, D20), with the day the preview parks a layer that has none; when the list has rows on one TBC day
        // (mayParkElsewhere), the Planner may still have some parked, and parks this layer after their days.
        private static string DatesFrom(PlannerGanttRow g, CultureInfo culture, bool mayParkElsewhere)
        {
            if (g.DateSource == PlannerDateSource.Planner)
            {
                return "Its dates: " + Range(g.OwnStart.Value, g.OwnEnd.Value, culture) + (g.OwnTbc ? ", TBC" : string.Empty) + ". From the Planner.";
            }
            var layer = g.Row.Layer;
            if (layer != null && layer.ParkedOn.HasValue)
            {
                var parked = "Its dates: none. The preview parks its work on " + layer.ParkedOn.Value.ToString("d MMM yyyy", culture);
                return mayParkElsewhere
                    ? parked + ", TBC, one day each in layer order from the project start; " + PlannerSchedule.ParkedElsewhere + "."
                    : parked + ", TBC, as the Planner parks a layer with no dates: one day each, in layer order from the project start.";
            }
            return g.IsGroup ? "Its dates: none of its own." : "Its dates: none.";
        }

        /// <summary>"2 Sep 2026", "31 Aug to 4 Sep 2026" or "28 Dec 2026 to 3 Jan 2027".</summary>
        public static string Range(DateTime from, DateTime to, CultureInfo culture)
        {
            if (from.Date == to.Date)
            {
                return from.ToString("d MMM yyyy", culture);
            }
            return from.ToString(from.Year == to.Year ? "d MMM" : "d MMM yyyy", culture) + " to " + to.ToString("d MMM yyyy", culture);
        }
    }
}
