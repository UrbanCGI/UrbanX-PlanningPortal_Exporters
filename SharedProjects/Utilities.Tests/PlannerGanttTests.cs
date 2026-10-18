using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// The Schedule tab's Gantt (<see cref="PlannerGanttModel"/>): which days each row draws, in which colour, hatched or
    /// not, the header, the rows a collapsed group hides, and what a cell says when hovered. Drawn, as the tab draws it, from
    /// the new names' preview (<see cref="PlannerSchedule.BuildV2(PlannerSceneSnapshot, PlannerScheduleOptions)"/>, the only
    /// engine the tab builds) and the Planner's layer list; its days per activity are checked against the Planner's own
    /// scheduler in <see cref="PlannerScheduleV2ParityTests"/>.
    /// </summary>
    public class PlannerGanttTests
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private static readonly DateTime Today = new DateTime(2026, 9, 3);
        private const string ListHeader = "Planner ID,Parent ID,Layer,Kind,ID,Phase,Stage,Sub-stage,Name,Start,Finish,TBC,Order,Project start";
        internal const string Strip = "01_01_Strip_Out";
        internal const string Undated = "01_02_Undated";
        internal const string Empty = "01_03_Empty";
        internal const string Reinstate = "02_01_Reinstate";
        private readonly ITestOutputHelper output;

        public PlannerGanttTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// A scene on the new names: two zones; a strip-out layer with four removals, an undated layer with one install, an
        /// empty layer, and a reinstatement layer with three installs.
        /// </summary>
        internal static TestScene Scene()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone1 = scene.Unit("_01_Zone", "Work_Phasing", root);
            var strip = scene.Unit(Strip, "_01_Zone", zone1, "l1");
            foreach (var name in new[] { "RM_Old_Fence", "RM_Old_Kerb", "RM_Bench", "RM_Sign" })
            {
                scene.Object(name, strip, Strip);
            }
            var undated = scene.Unit(Undated, "_01_Zone", zone1, "l2");
            scene.Object("IN_Undated_Box", undated, Undated);
            scene.Unit(Empty, "_01_Zone", zone1, "l3");
            var zone2 = scene.Unit("_02_Zone", "Work_Phasing", root);
            var reinstate = scene.Unit(Reinstate, "_02_Zone", zone2, "l4");
            foreach (var name in new[] { "IN_Kerb_A", "IN_Kerb_B", "IN_Kerb_C" })
            {
                scene.Object(name, reinstate, Reinstate);
            }
            return scene;
        }

        /// <summary>
        /// The Planner's list for it: the strip-out over 1 to 5 Sep 2026, the undated layer TBC on its placeholder day, the
        /// empty layer on 1 Jan 2027, the reinstatement over 10 to 12 Sep; the project start 1 Sep 2026.
        /// </summary>
        internal static PlannerLayerList List(params string[] leaveOut)
        {
            var rows = new[]
            {
                "g1,,_01_Zone,Group,,,,,Zone,,,,1,2026-09-01",
                "l1,g1," + Strip + ",Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-05,,1,2026-09-01",
                "l2,g1," + Undated + ",Layer,01_02,1,2,,Undated,,,Yes,2,2026-09-01",
                "l3,g1," + Empty + ",Layer,01_03,1,3,,Empty,2027-01-01,2027-01-01,,3,2026-09-01",
                "g2,,_02_Zone,Group,,,,,Zone,,,,2,2026-09-01",
                "l4,g2," + Reinstate + ",Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-12,,1,2026-09-01"
            };
            return PlannerLayerList.Parse("﻿" + ListHeader + "\r\n" + string.Join("\r\n", rows.Where(r => !leaveOut.Any(l => r.Contains("," + l + ","))).ToArray()) + "\r\n");
        }

        // The tab's options: the list's folders and project start, in UTC.
        private static PlannerScheduleOptions Options(PlannerLayerList list, PlannerSceneSnapshot snapshot)
        {
            var plan = list != null ? PlannerLayerPlanner.Plan(list, new PlannerSceneModel(snapshot)) : null;
            return new PlannerScheduleOptions
            {
                Zone = TimeZoneInfo.Utc,
                Today = Today,
                ProjectStart = list != null ? list.ProjectStart : null,
                Folders = PlannerScheduleRows.PlannerFolders(list, plan)
            };
        }

        // Fill IN / RM, as the tab fills: the new names' engine over the list's dates.
        internal static PlannerSchedule Fill(TestScene scene, PlannerLayerList list)
        {
            return PlannerSchedule.BuildV2(scene.Snapshot, Options(list, scene.Snapshot));
        }

        // The tab's rows: the list's tree with its statuses, the rows of scene layers the list does not have whose work is
        // read, and the rows for what no row stands for, as the tab charts them.
        internal static List<PlannerScheduleRow> RowsOf(TestScene scene, PlannerLayerList list, PlannerSchedule schedule)
        {
            var model = new PlannerSceneModel(scene.Snapshot);
            var plan = list != null ? PlannerLayerPlanner.Plan(list, model) : null;
            var tree = PlannerScheduleRows.Tree(list, plan, model, null, schedule, PlannerNamingKind.New);
            var unlisted = PlannerScheduleRows.Unlisted(plan, null, schedule, PlannerNamingKind.New);
            var shown = tree.Concat(PlannerScheduleRows.UnlistedAtWork(unlisted)).ToList();
            return shown.Concat(PlannerScheduleRows.Extra(tree.Concat(unlisted), schedule)).ToList();
        }

        private static PlannerGanttRow RowNamed(PlannerGanttModel model, string name)
        {
            return model.Rows.Single(r => r.Row.Name == name);
        }

        private static string Runs(IEnumerable<PlannerGanttRun> runs)
        {
            return string.Join(", ", runs.Select(r => r.ToString()).ToArray());
        }

        // One layer that installs and removes over 7 to 9 Sep 2026, and a removal for a layer nobody has (09_09), which the
        // preview parks TBC the day after the last dated work.
        private static TestScene Works(out PlannerLayerList list)
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone = scene.Unit("_01_Zone", "Work_Phasing", root);
            var works = scene.Unit("01_01_Works", "_01_Zone", zone, "l1");
            scene.Object("IN_Barrier", works, "01_01_Works");
            scene.Object("RM_Old_Barrier", works, "01_01_Works");
            scene.Object("IN_Sign_RM_09_09", works, "01_01_Works");
            list = PlannerLayerList.Parse(ListHeader + "\r\n"
                + "g1,,_01_Zone,Group,,,,,Zone,,,,1,2026-09-07\r\n"
                + "l1,g1,01_01_Works,Layer,01_01,1,1,,Works,2026-09-07,2026-09-09,,1,2026-09-07\r\n");
            return scene;
        }

        [Fact]
        public void BeforeFillEachLayerShowsItsListDatesAndGroupsSpanWhatIsInside()
        {
            var scene = Scene();
            var model = PlannerGanttModel.Build(RowsOf(scene, List(), null), null, Today);
            Assert.False(model.Filled);
            // 1 Sep 2026 (the strip-out's start) to 1 Jan 2027 (the empty layer's one day).
            Assert.Equal(new DateTime(2026, 9, 1), model.FirstDay);
            Assert.Equal(123, model.DayCount);
            Assert.Equal(new DateTime(2027, 1, 1), model.LastDay);

            var strip = RowNamed(model, Strip);
            Assert.Equal(PlannerGanttSpanKind.Neutral, strip.SpanKind);
            Assert.Equal("0+5", strip.Span.ToString());
            Assert.Equal(PlannerDateSource.Planner, strip.DateSource);
            Assert.False(strip.HasWork);
            Assert.Equal("9+3", RowNamed(model, Reinstate).Span.ToString());
            Assert.Equal("122+1", RowNamed(model, Empty).Span.ToString());
            // A layer the list does not date has nothing to place before Fill, and is TBC as the list says.
            var undated = RowNamed(model, Undated);
            Assert.Equal(PlannerGanttSpanKind.None, undated.SpanKind);
            Assert.True(undated.OwnTbc);

            // Groups: a grey span over everything inside them.
            var zone1 = RowNamed(model, "_01_Zone");
            Assert.True(zone1.IsGroup);
            Assert.True(zone1.HasChildren);
            Assert.Equal(-1, zone1.Parent);
            Assert.Equal(PlannerGanttSpanKind.Group, zone1.SpanKind);
            Assert.Equal("0+123", zone1.Span.ToString());
            Assert.Equal("9+3", RowNamed(model, "_02_Zone").Span.ToString());
            Assert.Equal(model.Rows.IndexOf(zone1), strip.Parent);
            Assert.False(strip.HasChildren);
        }

        [Fact]
        public void FilledLayerRowsCarryTheirInstallAndRemovalDays()
        {
            var scene = Scene();
            var schedule = Fill(scene, List());
            var model = PlannerGanttModel.Build(RowsOf(scene, List(), schedule), schedule, Today);
            Assert.True(model.Filled);
            Assert.Equal(new DateTime(2026, 9, 1), model.ScrubFirst);
            // The span runs to the list's last dated day, the empty layer's.
            Assert.Equal(new DateTime(2027, 1, 1), model.ScrubLast);
            Assert.Equal(new DateTime(2026, 9, 1), model.FirstDay);
            Assert.Equal(123, model.DayCount);

            // The strip-out removes over its dates, 1 to 5 Sep: its four removals split them evenly; it installs nothing.
            var strip = RowNamed(model, Strip);
            Assert.Equal("0+5", Runs(strip.Removal));
            Assert.Empty(strip.Install);
            Assert.Equal(PlannerGanttSpanKind.None, strip.SpanKind);
            Assert.Equal(4, strip.Work.Count);
            Assert.Equal(strip.Row.Rm.Count, strip.Work.Count);
            // The reinstatement installs over 10 to 12 Sep, a day each.
            var reinstate = RowNamed(model, Reinstate);
            Assert.Equal("9+3", Runs(reinstate.Install));
            Assert.Empty(reinstate.Removal);
            // The layer the list does not date is parked on the project start, TBC: hatched.
            var undated = RowNamed(model, Undated);
            Assert.Equal("0+1 TBC", Runs(undated.Install));
            Assert.True(undated.Work.Single().Tbc);
            // A dated layer whose dates place no work: its dates outlined.
            var empty = RowNamed(model, Empty);
            Assert.Equal(PlannerGanttSpanKind.Outline, empty.SpanKind);
            Assert.Equal("122+1", empty.Span.ToString());
            // The groups span their layers' work and dates.
            Assert.Equal("0+123", RowNamed(model, "_01_Zone").Span.ToString());
            Assert.Equal("9+3", RowNamed(model, "_02_Zone").Span.ToString());

            // Each work item's days are the scheduler's: the day of its first minute to the day of its last; every activity
            // is drawn once.
            foreach (var w in model.Rows.SelectMany(r => r.Work))
            {
                Assert.Equal(schedule.LocalTime(w.Activity.StartMs).Date, w.FirstDay);
                Assert.Equal(schedule.LocalTime(w.Activity.EndMs - 60000).Date, w.LastDay);
            }
            Assert.Equal(schedule.Activities.Count, model.Rows.Sum(r => r.Work.Count));
        }

        [Fact]
        public void AnInstallAndARemovalOnOneLayerAreBothDrawnAndDatedWorkIsNotHatched()
        {
            // One layer that both installs and removes, and a removal for a layer nobody has, parked after the last dated work.
            PlannerLayerList list;
            var scene = Works(out list);
            var schedule = Fill(scene, list);
            Assert.True(schedule.Usable, schedule.Message);
            var model = PlannerGanttModel.Build(RowsOf(scene, list, schedule), schedule, Today);
            var works = RowNamed(model, "01_01_Works");
            Assert.Equal("1+2", Runs(works.Install));
            // Its own removal on its first day; the parked one on 10 Sep, the day after the works end, TBC.
            Assert.Equal("0+1, 3+1 TBC", Runs(works.Removal));
            Assert.True(works.Install.All(r => !r.Tbc));
            Assert.Equal(new DateTime(2026, 9, 9), model.WorksEnd);
            Assert.True(model.InTbcZone(3));
            Assert.False(model.InTbcZone(2));
            Assert.Equal(schedule.Activities.Count, model.Rows.Sum(r => r.Work.Count));
        }

        /// <summary>
        /// The TBC zone is the scrubber's: the days after the last dated work up to the scrubber's last day, where the removals
        /// for a layer nobody has are parked. The chart can run further, over a layer's dates that hold no work yet (a list
        /// row the scene does not have, an empty layer); those days are not shaded or described as the zone (2026-10-16
        /// review).
        /// </summary>
        [Fact]
        public void TheTbcZoneIsWhereTheScrubberParksRemovals()
        {
            PlannerLayerList list;
            var scene = Works(out list);
            var schedule = Fill(scene, list);
            var rows = RowsOf(scene, list, schedule);
            // A layer of the list the scene does not have yet, after the works.
            const string late = "01_05_Late_Works";
            rows.Add(new PlannerScheduleRow
            {
                Source = PlannerScheduleRowSource.List, Name = late, Kind = "Layer",
                StartDate = new DateTime(2026, 10, 1), FinishDate = new DateTime(2026, 10, 9), DateSource = PlannerDateSource.Planner
            });
            var model = PlannerGanttModel.Build(rows, schedule, Today);
            Assert.Equal(new DateTime(2026, 9, 9), model.WorksEnd);
            Assert.Equal(new DateTime(2026, 9, 10), model.ScrubLast);
            Assert.Equal(new DateTime(2026, 10, 9), model.LastDay);
            // Only the parking day, 10 Sep (index 3).
            Assert.Equal(new[] { 3 }, Enumerable.Range(0, model.DayCount).Where(model.InTbcZone).ToArray());
            int first, last;
            Assert.True(model.TbcZoneDays(out first, out last));
            Assert.Equal(3, first);
            Assert.Equal(3, last);
            List<PlannerGanttHeaderCell> top, bottom;
            model.Header(PlannerGanttZoom.Days, out top, out bottom);
            Assert.Equal(new[] { 3 }, bottom.Where(c => c.TbcZone).Select(c => c.First).ToArray());
            // A week or a month is not the zone, though the zone's day is in it.
            model.Header(PlannerGanttZoom.Weeks, out top, out bottom);
            Assert.DoesNotContain(bottom, c => c.TbcZone);
            Assert.DoesNotContain(top, c => c.TbcZone);

            var lateRow = model.Rows.IndexOf(RowNamed(model, late));
            int oct5 = model.IndexOf(new DateTime(2026, 10, 5));
            Assert.Equal(PlannerGanttSpanKind.Outline, model.Rows[lateRow].SpanKind);
            Assert.DoesNotContain("TBC zone", model.Describe(lateRow, oct5, Au));
            Assert.DoesNotContain("TBC zone", model.Describe(-1, oct5, Au));
            Assert.Contains("after the last dated work, the TBC zone where removals for a layer nobody has are parked", model.Describe(-1, 3, Au));

            // A layer whose own dates cover the zone's day is not said to be in it there; nor is anything before Fill.
            rows[rows.Count - 1].StartDate = new DateTime(2026, 9, 10);
            model = PlannerGanttModel.Build(rows, schedule, Today);
            Assert.DoesNotContain("TBC zone", model.Describe(model.Rows.IndexOf(RowNamed(model, late)), 3, Au));
            Assert.Contains("TBC zone", model.Describe(-1, 3, Au));
            var before = PlannerGanttModel.Build(RowsOf(scene, list, null), null, Today);
            Assert.DoesNotContain(Enumerable.Range(0, before.DayCount), before.InTbcZone);
            Assert.False(before.TbcZoneDays(out first, out last));
        }

        /// <summary>
        /// A phasing layer of the scene that the Planner's list does not have (removed in the Planner, or made by hand in 3ds
        /// Max) is listed below the tree; when the Planner reads its work, the tree also gives it a row after its own, so the
        /// Gantt charts every activity the scrubber finds at work (2026-10-16 review).
        /// </summary>
        [Fact]
        public void ALayerTheListDoesNotHaveIsChartedWhenItsWorkIsRead()
        {
            var scene = Scene();
            var list = List(Reinstate);
            var schedule = Fill(scene, list);
            var model = new PlannerSceneModel(scene.Snapshot);
            var plan = PlannerLayerPlanner.Plan(list, model);
            var tree = PlannerScheduleRows.Tree(list, plan, model, null, schedule, PlannerNamingKind.New);
            var unlisted = PlannerScheduleRows.Unlisted(plan, null, schedule, PlannerNamingKind.New);
            Assert.Contains(unlisted, r => r.Name == Reinstate);
            var atWork = PlannerScheduleRows.UnlistedAtWork(unlisted);
            var reinstate = Assert.Single(atWork);
            Assert.Equal(Reinstate, reinstate.Name);
            Assert.Equal(PlannerScheduleRowSource.Unlisted, reinstate.Source);
            Assert.Equal(0, reinstate.Depth);
            Assert.Equal(PlannerScheduleRows.UnlistedAtWorkStatus, reinstate.Status);
            Assert.Equal(unlisted.Single(r => r.Name == Reinstate).InCount, reinstate.InCount);
            Assert.Equal(PlannerRowTbc.NotInList, reinstate.TbcMark);

            var extra = PlannerScheduleRows.Extra(tree.Concat(unlisted), schedule);
            var gantt = PlannerGanttModel.Build(tree.Concat(atWork).Concat(extra).ToList(), schedule, Today);
            Assert.Equal(schedule.Activities.Count, gantt.Rows.Sum(r => r.Work.Count));
            // With no dates from the list it is parked, after the undated layer's day: TBC.
            Assert.Equal("1+1 TBC", Runs(RowNamed(gantt, Reinstate).Install));
            // Without the row its work would be charted nowhere.
            Assert.True(PlannerGanttModel.Build(tree.Concat(extra).ToList(), schedule, Today).Rows.Sum(r => r.Work.Count) < schedule.Activities.Count);
            // Before Fill nothing is read there: no row.
            Assert.Empty(PlannerScheduleRows.UnlistedAtWork(PlannerScheduleRows.Unlisted(plan, null, null, PlannerNamingKind.New)));
        }

        [Fact]
        public void ADayWithDatedWorkIsNotHatchedAndTbcFollowsTheLayersFolder()
        {
            // Two installs in one row as the schedule would hold them: a dated one over 3 to 4 Sep and a TBC one over 1 to
            // 5 Sep (the dated one listed first). The days the dated work runs are not hatched.
            var schedule = Fill(Scene(), List());
            var dated = new PlannerScheduleActivity { Name = "Dated_IN_01_01", Type = PlannerTaskType.Install, StartMinute = 2 * 1440, Duration = 2 * 1440 };
            var tbc = new PlannerScheduleActivity { Name = "Later_IN_01_01", Type = PlannerTaskType.Install, StartMinute = 0, Duration = 5 * 1440, Tbc = true };
            var row = new PlannerScheduleRow { Name = Strip, Kind = "Layer" };
            row.In.Add(dated);
            row.In.Add(tbc);
            var model = PlannerGanttModel.Build(new List<PlannerScheduleRow> { row }, schedule, Today);
            Assert.Equal("0+2 TBC, 2+2, 4+1 TBC", Runs(model.Rows[0].Install));

            // An activity whose own flag is clear but whose layer is TBC is hatched, as the export hatches a TBC folder's work.
            var undated = schedule.Layers.Single(l => l.Label == Undated);
            Assert.True(undated.Tbc);
            var inTbcLayer = new PlannerScheduleActivity { Name = "Work_IN_01_02", Type = PlannerTaskType.Install, Duration = 1440, Home = undated };
            Assert.True(PlannerGanttModel.WorkOf(schedule, inTbcLayer).Tbc);
            inTbcLayer.Home = schedule.Layers.Single(l => l.Label == Reinstate);
            Assert.False(PlannerGanttModel.WorkOf(schedule, inTbcLayer).Tbc);
        }

        [Fact]
        public void TheHeaderHasMonthsOverDaysWeeksOrMonths()
        {
            var scene = Scene();
            var schedule = Fill(scene, List());
            var model = PlannerGanttModel.Build(RowsOf(scene, List(), schedule), schedule, Today);
            List<PlannerGanttHeaderCell> top, bottom;

            model.Header(PlannerGanttZoom.Days, out top, out bottom);
            Assert.Equal(new[] { "Sep 2026 0+30", "Oct 2026 30+31", "Nov 2026 61+30", "Dec 2026 91+31", "Jan 2027 122+1" },
                top.Select(c => c.Label + " " + c.First + "+" + c.Count).ToArray());
            Assert.Equal("Sep", top[0].ShortLabel);
            Assert.Equal(123, bottom.Count);
            Assert.Equal("1", bottom[0].Label);
            // 1 Sep 2026 is a Tuesday: Saturday 5 and Sunday 6 are the first weekend.
            Assert.Equal(new[] { 4, 5, 11, 12 }, bottom.Where(c => c.Weekend).Select(c => c.First).Take(4).ToArray());
            // Nothing is parked after the last dated work: no TBC zone.
            Assert.DoesNotContain(bottom, c => c.TbcZone);

            model.Header(PlannerGanttZoom.Weeks, out top, out bottom);
            Assert.Equal(5, top.Count);
            // Weeks run Monday to Sunday: the first is Tuesday 1 to Sunday 6 Sep, then from Monday 7.
            Assert.Equal(new[] { "1 0+6", "7 6+7", "14 13+7" }, bottom.Take(3).Select(c => c.Label + " " + c.First + "+" + c.Count).ToArray());
            Assert.Equal(model.DayCount, bottom.Sum(c => c.Count));

            model.Header(PlannerGanttZoom.Months, out top, out bottom);
            Assert.Equal(new[] { "2026 0+122", "2027 122+1" }, top.Select(c => c.Label + " " + c.First + "+" + c.Count).ToArray());
            Assert.Equal(new[] { "Sep", "Oct", "Nov", "Dec", "Jan" }, bottom.Select(c => c.Label).ToArray());
            Assert.Equal("S", bottom[0].ShortLabel);
            Assert.Equal("26", top[0].ShortLabel);
        }

        [Fact]
        public void ColumnsAndClicksMapToDays()
        {
            Assert.Equal(18.0, PlannerGanttModel.DayWidth(PlannerGanttZoom.Days, 18));
            Assert.Equal(6.0, PlannerGanttModel.DayWidth(PlannerGanttZoom.Weeks, 18));
            Assert.Equal(2.0, PlannerGanttModel.DayWidth(PlannerGanttZoom.Months, 18));
            var model = PlannerGanttModel.Build(RowsOf(Scene(), List(), null), null, Today);
            Assert.Equal(0, model.DayAtX(0, 18));
            Assert.Equal(0, model.DayAtX(17.9, 18));
            Assert.Equal(1, model.DayAtX(18, 18));
            Assert.Equal(122, model.DayAtX(122 * 18 + 5, 18));
            Assert.Equal(-1, model.DayAtX(123 * 18, 18));
            Assert.Equal(-1, model.DayAtX(-1, 18));
            // Today (3 Sep) is on the chart; a day before it is not.
            Assert.Equal(2, model.TodayIndex);
            Assert.Equal(-1, PlannerGanttModel.Build(RowsOf(Scene(), List(), null), null, new DateTime(2026, 8, 31)).TodayIndex);
        }

        [Fact]
        public void TheScrubberDrawsTheWorkDoneUpToItSolidAndClicksStayInItsRange()
        {
            PlannerLayerList list;
            var scene = Works(out list);
            var schedule = Fill(scene, list);
            var model = PlannerGanttModel.Build(RowsOf(scene, list, schedule), schedule, Today);
            // At the start of 9 Sep two days are done; at its end, three.
            Assert.Equal(2, model.DoneBefore(new DateTime(2026, 9, 9), false));
            Assert.Equal(3, model.DoneBefore(new DateTime(2026, 9, 9), true));
            PlannerGanttRun done, rest;
            PlannerGanttModel.Split(new PlannerGanttRun { First = 0, Count = 5 }, 2, out done, out rest);
            Assert.Equal("0+2", done.ToString());
            Assert.Equal("2+3", rest.ToString());
            PlannerGanttModel.Split(new PlannerGanttRun { First = 4, Count = 2 }, 2, out done, out rest);
            Assert.Equal(0, done.Count);
            Assert.Equal("4+2", rest.ToString());
            PlannerGanttModel.Split(new PlannerGanttRun { First = 4, Count = 2, Tbc = true }, 9, out done, out rest);
            Assert.Equal("4+2 TBC", done.ToString());
            Assert.Equal(0, rest.Count);
            // The scrubber runs from 7 Sep to the parked removal's day, 10 Sep.
            Assert.Equal(new DateTime(2026, 9, 10), model.ClampToScrubber(new DateTime(2026, 12, 25)));
            Assert.Equal(new DateTime(2026, 9, 7), model.ClampToScrubber(new DateTime(2026, 8, 1)));
            Assert.Equal(new DateTime(2026, 9, 8), model.ClampToScrubber(new DateTime(2026, 9, 8)));
        }

        [Fact]
        public void ACollapsedRowHidesEverythingUnderIt()
        {
            var depths = new[] { 0, 1, 2, 2, 1, 2, 0 };
            var rows = depths.Select(d => new PlannerScheduleRow { Depth = d, Name = "r" }).ToList();
            Func<ISet<int>, string> shown = collapsed => string.Join("", PlannerGanttModel.Shown(rows, i => collapsed.Contains(i)).Select(b => b ? "1" : "0").ToArray());
            Assert.Equal("1111111", shown(new HashSet<int>()));
            Assert.Equal("1100111", shown(new HashSet<int> { 1 }));
            Assert.Equal("1000001", shown(new HashSet<int> { 0 }));
            Assert.Equal("1000001", shown(new HashSet<int> { 0, 1 }));
            // A collapsed row with nothing under it hides nothing.
            Assert.Equal("1111111", shown(new HashSet<int> { 2, 6 }));
            Assert.Equal("1111101", shown(new HashSet<int> { 4 }));
        }

        [Fact]
        public void RowsAreRememberedByTheirPlannerIdElseTheirName()
        {
            var list = new PlannerScheduleRow { Source = PlannerScheduleRowSource.List, Name = "a", ListRow = new PlannerLayerRow { PlannerId = "f-12" } };
            Assert.Equal("id:f-12", PlannerGanttModel.KeyOf(list));
            var scene = new PlannerScheduleRow { Source = PlannerScheduleRowSource.Scene, Name = "_01_Zone", LayerName = "_01_Zone" };
            Assert.Equal("Scene:_01_Zone", PlannerGanttModel.KeyOf(scene));
        }

        [Fact]
        public void TheColoursAreThePlannersOwn()
        {
            // The Planner's mixHex (ganttExport.ts), run in Node: the export's tints.
            Assert.Equal("98D49B", PlannerGanttColours.InstallTint);
            Assert.Equal("EF9393", PlannerGanttColours.RemovalTint);
            Assert.Equal("C7CED6", PlannerGanttColours.NeutralTint);
            Assert.Equal("ECEEF2", PlannerGanttColours.WeekendBody);
            // JavaScript's Math.round takes a half up (2.5 gives 3), not to the even number.
            Assert.Equal("000003", PlannerGanttColours.MixHex("000002", "000003", 0.5));
            Assert.Equal("43B04A", PlannerGanttColours.Install);
            Assert.Equal("E23B3B", PlannerGanttColours.Removal);
            Assert.Equal(0x43B04A, PlannerGanttColours.Parse("#43b04a", 0));
            Assert.Equal(7, PlannerGanttColours.Parse("nope", 7));
        }

        [Fact]
        public void HoveringACellSaysTheLayerTheDateAndWhatInstallsOrRemoves()
        {
            var scene = Scene();
            var schedule = Fill(scene, List());
            var model = PlannerGanttModel.Build(RowsOf(scene, List(), schedule), schedule, Today);
            int strip = model.Rows.IndexOf(RowNamed(model, Strip));
            var text = model.Describe(strip, 0, Au);
            output.WriteLine(text);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal(Strip, lines[0]);
            Assert.Equal("Tue 1 Sep 2026", lines[1]);
            Assert.Equal("Its dates: 1 Sep to 5 Sep 2026. From the Planner.", lines[2]);
            var atWork = model.Rows[strip].Work.Where(w => w.FirstDay <= model.FirstDay && model.FirstDay <= w.LastDay).ToList();
            Assert.True(atWork.Count > 0);
            Assert.Equal(atWork.Count, lines.Count(l => l.StartsWith("RM ", StringComparison.Ordinal) && l.EndsWith("(hidden from its finish)", StringComparison.Ordinal)));
            Assert.Contains(lines, l => l.StartsWith("RM " + atWork[0].Activity.Name + ": ", StringComparison.Ordinal));

            // A day the layer does nothing on; a weekend; today.
            Assert.Contains("Nothing installs or removes on this day.", model.Describe(strip, 20, Au));
            Assert.Contains("Sat 5 Sep 2026 (weekend)", model.Describe(strip, 4, Au));
            Assert.Contains("Thu 3 Sep 2026, today", model.Describe(strip, 2, Au));
            // The header, a group, the empty layer, the parked TBC layer.
            Assert.Equal("Tue 1 Sep 2026\r\nClick to move the scrubber to this day.", model.Describe(-1, 0, Au));
            Assert.Contains("Everything inside it runs 10 Sep to 12 Sep 2026.", model.Describe(model.Rows.IndexOf(RowNamed(model, "_02_Zone")), 9, Au));
            Assert.Contains("Its dates (1 Jan 2027) place no install or removal.", model.Describe(model.Rows.IndexOf(RowNamed(model, Empty)), 122, Au));
            Assert.Contains(PlannerScheduleRows.TbcText(PlannerRowTbc.NotSettled), model.Describe(model.Rows.IndexOf(RowNamed(model, Undated)), 0, Au));
            // Off the chart.
            Assert.Null(model.Describe(strip, 123, Au));

            // Before Fill: the layer's own dates, from the Planner's list.
            var before = PlannerGanttModel.Build(RowsOf(scene, List(), null), null, Today);
            var neutral = before.Describe(before.Rows.IndexOf(RowNamed(before, Strip)), 1, Au);
            Assert.Contains("Its dates: 1 Sep to 5 Sep 2026. From the Planner.", neutral);
            Assert.Contains("Fill IN / RM from names", neutral);
            var undated = before.Describe(before.Rows.IndexOf(RowNamed(before, Undated)), 1, Au);
            Assert.Contains("Its dates: none.", undated);
            Assert.Contains(PlannerScheduleRows.TbcText(PlannerRowTbc.NotSettled), undated);
            Assert.Equal("28 Dec 2026 to 3 Jan 2027", PlannerGanttModel.Range(new DateTime(2026, 12, 28), new DateTime(2027, 1, 3), Au));
        }

        [Fact]
        public void RowsTakeTheListsDatesAndNeverANamesDates()
        {
            // Rows made by hand: layers with the list's dates (a TBC layer, a one-day layer, an undated TBC layer, a layer at
            // the top), and a row whose name and Start and Finish cells carry old-name dates, which are never read (D20; the
            // tab draws no Gantt on the old names, D23).
            var rows = new List<PlannerScheduleRow>
            {
                new PlannerScheduleRow { Name = "_02_Phase1_TM", Kind = "Group", Depth = 0 },
                new PlannerScheduleRow
                {
                    Name = "02_01_4_Construction", Kind = "Layer", Depth = 1, Tbc = true,
                    StartDate = new DateTime(2026, 10, 19), FinishDate = new DateTime(2026, 10, 23), DateSource = PlannerDateSource.Planner
                },
                new PlannerScheduleRow { Name = "02_02_Kerbs", Kind = "Layer", Depth = 1, StartDate = new DateTime(2026, 10, 26), DateSource = PlannerDateSource.Planner },
                new PlannerScheduleRow { Name = "02_03_Later", Kind = "Layer", Depth = 1, Tbc = true },
                new PlannerScheduleRow { Name = "03_01_Elsewhere", Kind = "Layer", Depth = 0, FinishDate = new DateTime(2026, 10, 30), DateSource = PlannerDateSource.Planner },
                new PlannerScheduleRow { Name = "02_04_Kerbs_TBC_31-10-26", Kind = "Layer", Depth = 0, Start = "2026-10-31", Finish = "2026-11-02" }
            };
            var model = PlannerGanttModel.Build(rows, null, Today);
            Assert.Equal(new DateTime(2026, 10, 19), model.FirstDay);
            Assert.Equal(12, model.DayCount);
            Assert.Equal("0+5 TBC", model.Rows[1].Span.ToString());
            Assert.Equal("7+1", model.Rows[2].Span.ToString());
            Assert.Equal(PlannerGanttSpanKind.None, model.Rows[3].SpanKind);
            Assert.Equal("11+1", model.Rows[4].Span.ToString());
            Assert.Equal(PlannerGanttSpanKind.None, model.Rows[5].SpanKind);
            Assert.False(model.Rows[5].OwnTbc);
            // The group spans its layers; it is not TBC itself.
            Assert.Equal("0+8", model.Rows[0].Span.ToString());
            Assert.Equal(-1, model.Rows[4].Parent);

            // A TBC group makes what is in it TBC, as the export's outline does.
            rows[0].Tbc = true;
            model = PlannerGanttModel.Build(rows, null, Today);
            Assert.True(model.Rows[0].Span.Tbc);
            Assert.True(model.Rows[2].Span.Tbc);

            // Nothing dated: an empty chart.
            Assert.True(PlannerGanttModel.Build(new List<PlannerScheduleRow> { rows[3] }, null, Today).Empty);
            Assert.True(PlannerGanttModel.Build(null, null, Today).Empty);
        }

        [EnvironmentFact("PLANNER_CLEANUP_HS2", "PLANNER_CLEANUP_HS2_LIST")]
        public void Hs2BuildsQuickly()
        {
            // HS2 v3_06u cleaned with its list (as Clean up leaves it), filled over the list's dates, as the tab charts it.
            var scene = new TestScene(PlannerCleanUpAcceptanceTests.LoadTsv(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2")));
            var list = PlannerLayerList.Parse(File.ReadAllBytes(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2_LIST")));
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes(), list);
            var cleaned = new TestScene(plan.Resolve(plan.Questions.Where(q => q.Case == PlannerCleanUpCase.DoubledType).ToDictionary(q => q.Id, q => "undouble")).ApplyTo(scene.Snapshot));
            var schedule = Fill(cleaned, list);
            Assert.True(schedule.Usable, schedule.Message);
            Assert.Equal(PlannerNamingKind.New, schedule.Naming);
            var rows = RowsOf(cleaned, list, schedule);
            var model = PlannerGanttModel.Build(rows, schedule, new DateTime(2026, 10, 15));
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                model = PlannerGanttModel.Build(rows, schedule, new DateTime(2026, 10, 15));
            }
            watch.Stop();
            int cells = model.Rows.Sum(r => r.Install.Concat(r.Removal).Sum(x => x.Count));
            output.WriteLine("HS2 Gantt: " + model.Rows.Count + " rows x " + model.DayCount + " days (" + model.FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + " to " + model.LastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "), " + model.Rows.Sum(r => r.Install.Count + r.Removal.Count)
                + " runs over " + cells + " day cells, " + model.Rows.Count(r => r.SpanKind == PlannerGanttSpanKind.Group) + " group spans; built in "
                + (watch.Elapsed.TotalMilliseconds / 20).ToString("0.00", CultureInfo.InvariantCulture) + " ms");
            // Every activity of the schedule is drawn in the row whose column holds it.
            Assert.Equal(schedule.Activities.Count, model.Rows.Sum(r => r.Work.Count));
            Assert.True(watch.Elapsed.TotalMilliseconds / 20 < 200);
        }
    }
}
