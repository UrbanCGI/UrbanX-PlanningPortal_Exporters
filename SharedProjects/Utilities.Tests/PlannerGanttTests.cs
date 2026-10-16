using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// The Schedule tab's Gantt (<see cref="PlannerGanttModel"/>): which days each row draws, in which colour, hatched or
    /// not, the header, the rows a collapsed group hides, and what a cell says when hovered. Its days per activity are
    /// checked against the Planner's own scheduler in <see cref="PlannerScheduleParityTests"/>.
    /// </summary>
    public class PlannerGanttTests
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private static readonly DateTime Today = new DateTime(2026, 9, 3);
        private readonly ITestOutputHelper output;

        public PlannerGanttTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // The tab's rows for the schedule tests' scene: its phasing tree (no list), with the rows for work no dated layer
        // places when filled.
        private static List<PlannerScheduleRow> RowsOf(TestScene scene, PlannerSchedule schedule)
        {
            var tree = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(scene.Snapshot), null, schedule);
            return tree.Concat(PlannerScheduleRows.Extra(tree, schedule)).ToList();
        }

        private static PlannerGanttRow RowNamed(PlannerGanttModel model, string name)
        {
            return model.Rows.Single(r => r.Row.Name == name);
        }

        private static string Runs(IEnumerable<PlannerGanttRun> runs)
        {
            return string.Join(", ", runs.Select(r => r.ToString()).ToArray());
        }

        [Fact]
        public void BeforeFillEachLayerShowsItsOwnDatesAndGroupsSpanWhatIsInside()
        {
            var scene = PlannerScheduleTests.Scene();
            var model = PlannerGanttModel.Build(RowsOf(scene, null), null, Today);
            Assert.False(model.Filled);
            // 1 Sep 2026 (the strip-out's start) to 1 Jan 2027 (the empty layer's one day).
            Assert.Equal(new DateTime(2026, 9, 1), model.FirstDay);
            Assert.Equal(123, model.DayCount);
            Assert.Equal(new DateTime(2027, 1, 1), model.LastDay);

            var strip = RowNamed(model, PlannerScheduleTests.Strip);
            Assert.Equal(PlannerGanttSpanKind.Neutral, strip.SpanKind);
            Assert.Equal("0+5", strip.Span.ToString());
            Assert.False(strip.HasWork);
            Assert.Equal("9+3", RowNamed(model, PlannerScheduleTests.Reinstate).Span.ToString());
            Assert.Equal("122+1", RowNamed(model, PlannerScheduleTests.Empty).Span.ToString());
            // An undated layer has nothing to place before Fill.
            var undated = RowNamed(model, PlannerScheduleTests.Undated);
            Assert.Equal(PlannerGanttSpanKind.None, undated.SpanKind);
            Assert.True(undated.OwnTbc);

            // Groups: a grey span over everything inside them (the tree starts below the phasing root).
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
            var scene = PlannerScheduleTests.Scene();
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var model = PlannerGanttModel.Build(RowsOf(scene, schedule), schedule, Today);
            Assert.True(model.Filled);
            Assert.Equal(new DateTime(2026, 9, 1), model.ScrubFirst);
            Assert.Equal(new DateTime(2026, 9, 12), model.ScrubLast);
            Assert.Equal(new DateTime(2026, 9, 12), model.WorksEnd);
            Assert.Equal(new DateTime(2026, 9, 1), model.FirstDay);
            Assert.Equal(123, model.DayCount);

            // 01_02 removes over its dates, 1 to 5 Sep (its own removals spread over them, the other layer's objects' a
            // day at its start); it installs nothing.
            var strip = RowNamed(model, PlannerScheduleTests.Strip);
            Assert.Equal("0+5", Runs(strip.Removal));
            Assert.Empty(strip.Install);
            Assert.Equal(PlannerGanttSpanKind.None, strip.SpanKind);
            Assert.Equal(strip.Row.Rm.Count, strip.Work.Count);
            // 02_01 installs over 10 to 12 Sep, Kerb B's install a day at its start among them.
            var reinstate = RowNamed(model, PlannerScheduleTests.Reinstate);
            Assert.Equal("9+3", Runs(reinstate.Install));
            Assert.Empty(reinstate.Removal);
            // The undated layer's work is at the project start, TBC: hatched.
            var undated = RowNamed(model, PlannerScheduleTests.Undated);
            Assert.Equal("0+1 TBC", Runs(undated.Install));
            Assert.True(undated.Work.Single().Tbc);
            // A dated layer whose dates place no work: its dates outlined.
            var empty = RowNamed(model, PlannerScheduleTests.Empty);
            Assert.Equal(PlannerGanttSpanKind.Outline, empty.SpanKind);
            Assert.Equal("122+1", empty.Span.ToString());
            // The groups span their layers' work.
            Assert.Equal("0+123", RowNamed(model, "_01_Zone").Span.ToString());
            Assert.Equal("9+3", RowNamed(model, "_02_Zone").Span.ToString());

            // Each work item's days are the scheduler's: the day of its first minute to the day of its last.
            foreach (var w in model.Rows.SelectMany(r => r.Work))
            {
                Assert.Equal(schedule.LocalTime(w.Activity.StartMs).Date, w.FirstDay);
                Assert.Equal(schedule.LocalTime(w.Activity.EndMs - 60000).Date, w.LastDay);
            }
        }

        [Fact]
        public void AnInstallAndARemovalOnOneDayAreBothDrawnAndDatedWorkIsNotHatched()
        {
            // One layer that both installs and removes, and a TBC layer whose removal no dated layer has (parked after the end).
            const string layer = "01_01_Works_07-09-26_09-09-26";
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var helper = scene.Unit(layer, "Work_Phasing", root);
            scene.Object("Ph1_St01_IN_Barrier", helper, layer);
            scene.Object("Ph1_St01_RM_Old_Barrier", helper, layer);
            scene.Object("Ph1_St01_IN_Sign_Ph1_St09_RM", helper, layer);
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            Assert.True(schedule.Usable, schedule.Message);
            var model = PlannerGanttModel.Build(RowsOf(scene, schedule), schedule, Today);
            var works = RowNamed(model, layer);
            Assert.True(works.Install.Count > 0 && works.Removal.Count > 0);
            Assert.True(works.Install.Concat(works.Removal).All(r => !r.Tbc));
            Assert.True(works.Install.Min(r => r.First) >= 0 && works.Removal.Max(r => r.Last) <= 2);
            // The parked removal: its own row after the end of the works, TBC, the day after they end (10 Sep).
            var parked = model.Rows.Single(r => r.Row.Source == PlannerScheduleRowSource.ByRule);
            Assert.Equal("3+1 TBC", Runs(parked.Removal));
            Assert.True(model.InTbcZone(3));
            Assert.False(model.InTbcZone(2));
        }

        /// <summary>
        /// The TBC zone is the scrubber's: the days after the works end up to the scrubber's last day, where the removals no
        /// dated layer has are parked. The chart can run further, over a layer's dates that hold no work yet (a list row the
        /// scene does not have, an empty layer); those days are not shaded or described as the zone (2026-10-16 review).
        /// </summary>
        [Fact]
        public void TheTbcZoneIsWhereTheScrubberParksRemovals()
        {
            const string layer = "01_01_Works_07-09-26_09-09-26";
            const string late = "01_05_Late_Works_01-10-26_09-10-26";
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var helper = scene.Unit(layer, "Work_Phasing", root);
            scene.Object("Ph1_St01_IN_Barrier", helper, layer);
            scene.Object("Ph1_St01_IN_Sign_Ph1_St09_RM", helper, layer);
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var rows = RowsOf(scene, schedule);
            // A layer of the list the scene does not have yet, after the works.
            rows.Add(new PlannerScheduleRow { Source = PlannerScheduleRowSource.List, Name = late, Kind = "Layer", Start = "2026-10-01", Finish = "2026-10-09" });
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
            Assert.Contains("after the works end, the TBC zone", model.Describe(-1, 3, Au));
            Assert.Contains("after the works end, the TBC zone", model.Describe(model.Rows.IndexOf(model.Rows.Single(r => r.Row.Source == PlannerScheduleRowSource.ByRule)), 3, Au));

            // A layer whose own dates cover the zone's day is not said to be in it there; nor is anything before Fill.
            const string covering = "01_05_Late_Works_10-09-26_09-10-26";
            rows[rows.Count - 1] = new PlannerScheduleRow { Source = PlannerScheduleRowSource.List, Name = covering, Kind = "Layer" };
            model = PlannerGanttModel.Build(rows, schedule, Today);
            Assert.DoesNotContain("TBC zone", model.Describe(model.Rows.IndexOf(RowNamed(model, covering)), 3, Au));
            Assert.Contains("TBC zone", model.Describe(-1, 3, Au));
            var before = PlannerGanttModel.Build(RowsOf(scene, null), null, Today);
            Assert.DoesNotContain(Enumerable.Range(0, before.DayCount), before.InTbcZone);
            Assert.False(before.TbcZoneDays(out first, out last));
        }

        /// <summary>
        /// A phasing layer of the scene the Planner's list does not have (removed in the Planner, or made by hand in 3ds Max)
        /// is listed below the tree; when the Planner reads its work, the tree also gives it a row after its own, so the
        /// Gantt charts every activity the scrubber finds at work (2026-10-16 review).
        /// </summary>
        [Fact]
        public void ALayerTheListDoesNotHaveIsChartedWhenItsWorkIsRead()
        {
            var scene = PlannerScheduleTests.Scene();
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_01_Zone"),
                TestCsv.Row("l1", "g1", PlannerScheduleTests.Strip),
                TestCsv.Row("l2", "g1", PlannerScheduleTests.Undated),
                TestCsv.Row("l3", "g1", PlannerScheduleTests.Empty),
                TestCsv.Row("g2", null, "_02_Zone"));
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);
            var tree = PlannerScheduleRows.Tree(list, plan, new PlannerSceneModel(scene.Snapshot), null, schedule);
            var unlisted = PlannerScheduleRows.Unlisted(plan, null, schedule);
            Assert.Contains(unlisted, r => r.Name == PlannerScheduleTests.Reinstate);
            var atWork = PlannerScheduleRows.UnlistedAtWork(unlisted);
            var reinstate = Assert.Single(atWork);
            Assert.Equal(PlannerScheduleTests.Reinstate, reinstate.Name);
            Assert.Equal(PlannerScheduleRowSource.Unlisted, reinstate.Source);
            Assert.Equal(0, reinstate.Depth);
            Assert.Equal(PlannerScheduleRows.UnlistedAtWorkStatus, reinstate.Status);
            Assert.Equal(unlisted.Single(r => r.Name == PlannerScheduleTests.Reinstate).InCount, reinstate.InCount);

            var extra = PlannerScheduleRows.Extra(tree.Concat(unlisted), schedule);
            var model = PlannerGanttModel.Build(tree.Concat(atWork).Concat(extra).ToList(), schedule, Today);
            Assert.Equal(schedule.Activities.Count, model.Rows.Sum(r => r.Work.Count));
            Assert.Equal("9+3", Runs(RowNamed(model, PlannerScheduleTests.Reinstate).Install));
            // Without the row its work would be charted nowhere.
            Assert.True(PlannerGanttModel.Build(tree.Concat(extra).ToList(), schedule, Today).Rows.Sum(r => r.Work.Count) < schedule.Activities.Count);
            // Before Fill nothing is read there: no row.
            Assert.Empty(PlannerScheduleRows.UnlistedAtWork(PlannerScheduleRows.Unlisted(plan, null, null)));
        }

        [Fact]
        public void ADayWithDatedWorkIsNotHatchedAndTbcFollowsTheLayersFolder()
        {
            // Two installs in one row as the schedule would hold them: a dated one over 3 to 4 Sep and a TBC one over 1 to
            // 5 Sep (the dated one listed first). The days the dated work runs are not hatched.
            var schedule = PlannerSchedule.Build(PlannerScheduleTests.Scene().Snapshot, PlannerScheduleTests.Utc);
            var dated = new PlannerScheduleActivity { Name = "Ph1_St02_IN_Dated", Type = PlannerTaskType.Install, StartMinute = 2 * 1440, Duration = 2 * 1440 };
            var tbc = new PlannerScheduleActivity { Name = "Ph1_St02_IN_Later", Type = PlannerTaskType.Install, StartMinute = 0, Duration = 5 * 1440, Tbc = true };
            var row = new PlannerScheduleRow { Name = PlannerScheduleTests.Strip, Kind = "Layer" };
            row.In.Add(dated);
            row.In.Add(tbc);
            var model = PlannerGanttModel.Build(new List<PlannerScheduleRow> { row }, schedule, Today);
            Assert.Equal("0+2 TBC, 2+2, 4+1 TBC", Runs(model.Rows[0].Install));

            // An activity whose own flag is clear but whose layer is TBC is hatched, as the export hatches a TBC folder's work.
            var layer = new PlannerScheduleLayer { Label = "01_03_Undated", Raw = PlannerScheduleTests.Undated, Parsed = PlannerNaming.ParseLayerName(PlannerScheduleTests.Undated) };
            var inTbcLayer = new PlannerScheduleActivity { Name = "Ph1_St03_IN_Work", Type = PlannerTaskType.Install, Duration = 1440, Home = layer };
            Assert.True(PlannerGanttModel.WorkOf(schedule, inTbcLayer).Tbc);
            inTbcLayer.Home = schedule.Layers.Single(l => l.Label == "02_01_Reinstate");
            Assert.False(PlannerGanttModel.WorkOf(schedule, inTbcLayer).Tbc);
        }

        [Fact]
        public void TheHeaderHasMonthsOverDaysWeeksOrMonths()
        {
            var scene = PlannerScheduleTests.Scene();
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var model = PlannerGanttModel.Build(RowsOf(scene, schedule), schedule, Today);
            List<PlannerGanttHeaderCell> top, bottom;

            model.Header(PlannerGanttZoom.Days, out top, out bottom);
            Assert.Equal(new[] { "Sep 2026 0+30", "Oct 2026 30+31", "Nov 2026 61+30", "Dec 2026 91+31", "Jan 2027 122+1" },
                top.Select(c => c.Label + " " + c.First + "+" + c.Count).ToArray());
            Assert.Equal("Sep", top[0].ShortLabel);
            Assert.Equal(123, bottom.Count);
            Assert.Equal("1", bottom[0].Label);
            // 1 Sep 2026 is a Tuesday: Saturday 5 and Sunday 6 are the first weekend.
            Assert.Equal(new[] { 4, 5, 11, 12 }, bottom.Where(c => c.Weekend).Select(c => c.First).Take(4).ToArray());
            // The works end on 12 Sep (index 11) and nothing is parked after them: no TBC zone, though the chart runs on to
            // the empty layer's day (TheTbcZoneIsWhereTheScrubberParksRemovals).
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
            var model = PlannerGanttModel.Build(RowsOf(PlannerScheduleTests.Scene(), null), null, Today);
            Assert.Equal(0, model.DayAtX(0, 18));
            Assert.Equal(0, model.DayAtX(17.9, 18));
            Assert.Equal(1, model.DayAtX(18, 18));
            Assert.Equal(122, model.DayAtX(122 * 18 + 5, 18));
            Assert.Equal(-1, model.DayAtX(123 * 18, 18));
            Assert.Equal(-1, model.DayAtX(-1, 18));
            // Today (3 Sep) is on the chart; a day before it is not.
            Assert.Equal(2, model.TodayIndex);
            Assert.Equal(-1, PlannerGanttModel.Build(RowsOf(PlannerScheduleTests.Scene(), null), null, new DateTime(2026, 8, 31)).TodayIndex);
        }

        [Fact]
        public void TheScrubberDrawsTheWorkDoneUpToItSolidAndClicksStayInItsRange()
        {
            var scene = PlannerScheduleTests.Scene();
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var model = PlannerGanttModel.Build(RowsOf(scene, schedule), schedule, Today);
            // At the start of 3 Sep two days are done; at its end, three.
            Assert.Equal(2, model.DoneBefore(new DateTime(2026, 9, 3), false));
            Assert.Equal(3, model.DoneBefore(new DateTime(2026, 9, 3), true));
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
            // The chart runs to 1 Jan (the empty layer); the scrubber to 12 Sep.
            Assert.Equal(new DateTime(2026, 9, 12), model.ClampToScrubber(new DateTime(2026, 12, 25)));
            Assert.Equal(new DateTime(2026, 9, 1), model.ClampToScrubber(new DateTime(2026, 8, 1)));
            Assert.Equal(new DateTime(2026, 9, 7), model.ClampToScrubber(new DateTime(2026, 9, 7)));
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
            var scene = PlannerScheduleTests.Scene();
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var model = PlannerGanttModel.Build(RowsOf(scene, schedule), schedule, Today);
            int strip = model.Rows.IndexOf(RowNamed(model, PlannerScheduleTests.Strip));
            var text = model.Describe(strip, 0, Au);
            output.WriteLine(text);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal(PlannerScheduleTests.Strip, lines[0]);
            Assert.Equal("Tue 1 Sep 2026", lines[1]);
            var atWork = model.Rows[strip].Work.Where(w => w.FirstDay <= model.FirstDay && model.FirstDay <= w.LastDay).ToList();
            Assert.True(atWork.Count > 0);
            Assert.Equal(atWork.Count, lines.Count(l => l.StartsWith("RM ", StringComparison.Ordinal) && l.EndsWith("(hidden from its finish)", StringComparison.Ordinal)));
            Assert.Contains(lines, l => l.StartsWith("RM " + atWork[0].Activity.Name + ": ", StringComparison.Ordinal));

            // A day the layer does nothing on; a weekend; today. After the works end nothing is parked here: no TBC zone.
            Assert.Contains("Nothing installs or removes on this day.", model.Describe(strip, 20, Au));
            Assert.Contains("Sat 5 Sep 2026 (weekend)", model.Describe(strip, 4, Au));
            Assert.DoesNotContain("the TBC zone", model.Describe(strip, 13, Au));
            Assert.Contains("Thu 3 Sep 2026, today", model.Describe(strip, 2, Au));
            // The header, a group, the empty layer.
            Assert.Equal("Tue 1 Sep 2026\r\nClick to move the scrubber to this day.", model.Describe(-1, 0, Au));
            Assert.Contains("Everything inside it runs 10 Sep to 12 Sep 2026.", model.Describe(model.Rows.IndexOf(RowNamed(model, "_02_Zone")), 9, Au));
            Assert.Contains("Its dates (1 Jan 2027) place no install or removal.", model.Describe(model.Rows.IndexOf(RowNamed(model, PlannerScheduleTests.Empty)), 122, Au));
            // Off the chart.
            Assert.Null(model.Describe(strip, 123, Au));

            // Before Fill: the layer's own dates.
            var before = PlannerGanttModel.Build(RowsOf(scene, null), null, Today);
            var neutral = before.Describe(before.Rows.IndexOf(RowNamed(before, PlannerScheduleTests.Strip)), 1, Au);
            Assert.Contains("Its dates: 1 Sep to 5 Sep 2026.", neutral);
            Assert.Contains("Fill IN / RM from names", neutral);
            Assert.Contains("Undated (TBC)", before.Describe(before.Rows.IndexOf(RowNamed(before, PlannerScheduleTests.Undated)), 1, Au));
            Assert.Equal("28 Dec 2026 to 3 Jan 2027", PlannerGanttModel.Range(new DateTime(2026, 12, 28), new DateTime(2027, 1, 3), Au));
        }

        [Fact]
        public void AListsRowsUseTheirLayerNamesDatesAndTbc()
        {
            // As the Planner's list gives them: a TBC layer with dates, a one-day layer, an undated TBC layer, Start and
            // Finish Excel has rewritten (the name's dates are used).
            var rows = new List<PlannerScheduleRow>
            {
                new PlannerScheduleRow { Name = "_02_Phase1_TM", Kind = "Group", Depth = 0 },
                new PlannerScheduleRow { Name = "02_01_4_Construction_TBC_19-10-26_23-10-26", Kind = "Layer", Depth = 1, Tbc = true, Start = "19/10/2026", Finish = "23/10/2026" },
                new PlannerScheduleRow { Name = "02_02_Kerbs_26-10-26", Kind = "Layer", Depth = 1 },
                new PlannerScheduleRow { Name = "02_03_Later_TBC", Kind = "Layer", Depth = 1, Tbc = true },
                new PlannerScheduleRow { Name = "Not_A_Layer", Kind = "Layer", Depth = 0, Start = "2026-10-30", Finish = "" }
            };
            var model = PlannerGanttModel.Build(rows, null, Today);
            Assert.Equal(new DateTime(2026, 10, 19), model.FirstDay);
            Assert.Equal(12, model.DayCount);
            Assert.Equal("0+5 TBC", model.Rows[1].Span.ToString());
            Assert.Equal("7+1", model.Rows[2].Span.ToString());
            Assert.Equal(PlannerGanttSpanKind.None, model.Rows[3].SpanKind);
            Assert.Equal("11+1", model.Rows[4].Span.ToString());
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

        [Fact]
        public void Hs2BuildsQuickly()
        {
            var treePath = Environment.GetEnvironmentVariable("PLANNER_SCHEDULE_HS2");
            if (string.IsNullOrEmpty(treePath) || !File.Exists(treePath))
            {
                output.WriteLine("PLANNER_SCHEDULE_HS2 is not set; the HS2 Gantt run is skipped.");
                return;
            }
            var snapshot = PlannerScheduleParityTests.SnapshotOf(PlannerScheduleParityTests.NodesOfTree(JObject.Parse(File.ReadAllText(treePath))));
            var schedule = PlannerSchedule.Build(snapshot, new PlannerScheduleOptions { Zone = TimeZoneInfo.Utc, Today = new DateTime(2026, 10, 15) });
            Assert.True(schedule.Usable, schedule.Message);
            var tree = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(snapshot), null, schedule);
            var rows = tree.Concat(PlannerScheduleRows.Extra(tree, schedule)).ToList();
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
