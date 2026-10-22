using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The Schedule tab on the new names (naming v2): the layer list's one date reader and its Project start column (spec 5.4),
    /// the preview dates' precedence, the first Generate's slots (D11, D19) and parking (D12), the IN and RM columns, what is
    /// not scheduled, the rows and the Gantt. Parity with the Planner's own code is in <see cref="PlannerScheduleV2ParityTests"/>.
    /// </summary>
    public class PlannerScheduleV2Tests
    {
        private const string Header = "Planner ID,Parent ID,Layer,Kind,ID,Phase,Stage,Sub-stage,Name,Start,Finish,TBC,Order,Project start";

        private static readonly DateTime Start = new DateTime(2026, 8, 31);

        private static PlannerLayerList List(params string[] rows)
        {
            return PlannerLayerList.Parse("\uFEFF" + Header + "\r\n" + string.Join("\r\n", rows) + "\r\n");
        }

        // The list the Planner writes for the test scene on the new names: no dates in the Layer column, the project start on every row.
        private static PlannerLayerList SceneList()
        {
            return List(
                "g1,,_01_Zone,Group,,,,,Zone,,,,1,2026-08-31",
                "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,g1,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31",
                "g2,,_02_Zone,Group,,,,,Zone,,,,2,2026-08-31",
                "l3,g2,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,1,2026-08-31",
                "l4,g2,02_02_Planned,Layer,02_02,2,2,,Planned,2026-09-15,2026-09-15,,2,2026-08-31");
        }

        /// <summary>A new-name scene: two zones, three layers and their helpers, objects linked under them, and one loose.</summary>
        internal static TestScene Scene()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone1 = scene.Unit("_01_Zone", "Work_Phasing", root);
            var strip = scene.Unit("01_01_Strip_Out", "_01_Zone", zone1, "l1");
            scene.Object("RM_Old_Fence", strip, "01_01_Strip_Out");
            scene.Object("IN_Hoarding_RM_02_01", strip, "01_01_Strip_Out");
            scene.Object("Plain_Box", strip, "01_01_Strip_Out");
            var kerbs = scene.Unit("01_02_Kerbs", "_01_Zone", zone1, "l2");
            scene.Object("IN_Kerb_A", kerbs, "01_02_Kerbs");
            scene.Object("IN_Kerb_B", kerbs, "01_02_Kerbs");
            scene.Object("RM_Bench_IN", kerbs, "01_02_Kerbs");
            scene.Object("IN_Barrier_RM_01_23", kerbs, "01_02_Kerbs");
            scene.Object("Ph1_St02_IN_Old", kerbs, "01_02_Kerbs");
            var zone2 = scene.Unit("_02_Zone", "Work_Phasing", root);
            var reinstate = scene.Unit("02_01_Reinstate", "_02_Zone", zone2, "l3");
            scene.Object("IN_New_Kerb", reinstate, "02_01_Reinstate");
            scene.Object("Kerb_Edge_RM", reinstate, "02_01_Reinstate");
            scene.Object("IN_Loose", null, "02_01_Reinstate");
            return scene;
        }

        private static PlannerScheduleOptions Options(PlannerLayerList list, PlannerSceneSnapshot snapshot)
        {
            var scene = new PlannerSceneModel(snapshot);
            var plan = list != null ? PlannerLayerPlanner.Plan(list, scene) : null;
            return new PlannerScheduleOptions
            {
                Zone = TimeZoneInfo.Utc,
                Today = new DateTime(2026, 10, 15),
                ProjectStart = list != null ? list.ProjectStart : null,
                Folders = PlannerScheduleRows.PlannerFolders(list, plan)
            };
        }

        // The Schedule tab's preview: the new names' engine, the only one the tab builds (D23).
        private static PlannerSchedule Fill(PlannerLayerList list, TestScene scene)
        {
            return PlannerSchedule.BuildV2(scene.Snapshot, Options(list, scene.Snapshot));
        }

        private static string Times(PlannerSchedule schedule, PlannerScheduleActivity a)
        {
            return schedule.LocalTime(a.StartMs).ToString("dd HH:mm", CultureInfo.InvariantCulture) + "-" + schedule.LocalTime(a.EndMs).ToString("dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static PlannerScheduleActivity Activity(PlannerSchedule schedule, string obj, PlannerTaskType type)
        {
            return schedule.Activities.Single(a => a.Object == obj && a.Type == type);
        }

        // ---------------------------------------------------------------------------------------------------------
        // The layer list: one date reader, Project start

        [Fact]
        public void TheListsIsoDatesAndProjectStartAreRead()
        {
            var list = SceneList();
            Assert.True(list.Usable);
            Assert.Equal(PlannerDateForm.Iso, list.DateForm);
            Assert.Equal(Start, list.ProjectStart);
            var strip = list.Rows.Single(r => r.PlannerId == "l1");
            Assert.Equal(new DateTime(2026, 9, 1), strip.StartDate);
            Assert.Equal(new DateTime(2026, 9, 2), strip.FinishDate);
            var kerbs = list.Rows.Single(r => r.PlannerId == "l2");
            Assert.Null(kerbs.StartDate);
            Assert.True(kerbs.Tbc);
            // Project start is a column the reader knows now: no "Columns not read" note, and no note about the dates.
            Assert.DoesNotContain(list.Notes, n => n.Contains("not read"));
            Assert.Empty(list.DateNotes);
        }

        [Fact]
        public void AListMadeBeforeProjectStartStillReads()
        {
            var list = PlannerLayerList.Parse(TestCsv.Header + "\r\nl1,,01_01_Kerbs_01-09-26_03-09-26,Layer,01_01,1,1,,Kerbs,2026-09-01,2026-09-03,,1\r\n");
            Assert.True(list.Usable);
            Assert.Null(list.ProjectStart);
            Assert.Equal(new DateTime(2026, 9, 3), list.Rows[0].FinishDate);
            Assert.Empty(list.Notes);
        }

        [Fact]
        public void ASpreadsheetsDayFirstDatesAreReadOnlyWhenTheyCannotBeMixedUp()
        {
            Assert.Equal(PlannerDateForm.DayFirst, PlannerListDates.FormOf(new[] { "31/08/2026", "1/09/2026", "", "2026-09-04" }));
            Assert.Equal(new DateTime(2026, 9, 1), PlannerListDates.Read("1/09/2026", PlannerDateForm.DayFirst));
            Assert.Equal(new DateTime(2026, 9, 4), PlannerListDates.Read("2026-09-04", PlannerDateForm.DayFirst));
            Assert.Equal(new DateTime(2026, 10, 15), PlannerListDates.Read("15.10.26", PlannerListDates.FormOf(new[] { "15.10.26" })));
            // No first number above 12: day and month cannot be told apart, so nothing reads.
            Assert.Equal(PlannerDateForm.Unreadable, PlannerListDates.FormOf(new[] { "05/06/2026", "07/06/2026" }));
            // A second number above 12: month first, which the list never is.
            Assert.Equal(PlannerDateForm.Unreadable, PlannerListDates.FormOf(new[] { "31/08/2026", "08/31/2026" }));
            Assert.Equal(PlannerDateForm.Unreadable, PlannerListDates.FormOf(new[] { "next week" }));
            Assert.Null(PlannerListDates.Read("2026-09-04", PlannerDateForm.Unreadable));
            Assert.Equal(PlannerDateForm.None, PlannerListDates.FormOf(new[] { "", " " }));
            Assert.Null(PlannerListDates.Read("2026-02-30", PlannerDateForm.Iso));
        }

        [Fact]
        public void DatesThatCannotBeToldApartLeaveEveryRowUndatedWithOneNote()
        {
            var list = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,05/09/2026,06/09/2026,,1,01/09/2026",
                "l2,,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,2026-09-08,2026-09-09,,2,01/09/2026");
            Assert.Equal(PlannerDateForm.Unreadable, list.DateForm);
            Assert.All(list.Rows, r => Assert.Null(r.StartDate));
            Assert.Null(list.ProjectStart);
            Assert.Single(list.DateNotes, n => n.Contains("cannot be told apart"));
            Assert.DoesNotContain(list.Notes, n => n.Contains("cannot be told apart"));
            var dayFirst = List("l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,31/08/2026,01/09/2026,,1,30/08/2026");
            Assert.Equal(new DateTime(2026, 8, 30), dayFirst.ProjectStart);
            Assert.Equal(new DateTime(2026, 8, 31), dayFirst.Rows[0].StartDate);
            Assert.Single(dayFirst.DateNotes, n => n.Contains("day first"));
        }

        // ---------------------------------------------------------------------------------------------------------
        // The first Generate's slots and parking

        [Fact]
        public void TheSlotsSplitTheLayerAndEndOfLayerEventsShareTheLastSlot()
        {
            List<PlannerSchedule.PlannerSlotV2> chain, ends;
            PlannerSchedule.LayerSlotsV2(1440, 3, new[] { false, false }, out chain, out ends);
            Assert.Equal(new long[] { 0, 480, 960 }, chain.Select(s => s.Start));
            Assert.Equal(new[] { -1, 0, 1 }, chain.Select(s => s.After));
            Assert.All(ends, e => Assert.Equal(960, e.Start));
            Assert.All(ends, e => Assert.Equal(1440, e.End));
            // D19: the object holding the last slot halves it with its end-of-layer event.
            PlannerSchedule.LayerSlotsV2(1440, 1, new[] { true }, out chain, out ends);
            Assert.Equal(720, chain[0].End);
            Assert.Equal(720, ends[0].Start);
            Assert.Equal(0, ends[0].After);
            // With nothing in groups 1 and 2, end-of-layer events run over the whole layer (D11).
            PlannerSchedule.LayerSlotsV2(1440, 0, new[] { false }, out chain, out ends);
            Assert.Empty(chain);
            Assert.Equal(0, ends[0].Start);
            Assert.Equal(1440, ends[0].End);
            // Shorter in minutes than its events: a minute each while it lasts, the rest on its last minute.
            PlannerSchedule.LayerSlotsV2(3, 5, new bool[0], out chain, out ends);
            Assert.Equal(new long[] { 0, 1, 2, 2, 2 }, chain.Select(s => s.Start));
            Assert.Equal(new[] { -1, 0, 1, 1, 1 }, chain.Select(s => s.After));
        }

        [Fact]
        public void FillFilesEachEventByTheLayerItNamesAndDatesItFromTheList()
        {
            var schedule = Fill(SceneList(), Scene());
            Assert.Equal(PlannerNamingKind.New, schedule.Naming);
            Assert.True(schedule.Usable, schedule.Message);
            Assert.Equal(Start, schedule.ProjectStart);
            // 01_01 runs 1 to 2 Sep: its removal first, then its install.
            Assert.Equal("01 00:00-01 24:00".Replace("01 24:00", "02 00:00"), Times(schedule, Activity(schedule, "RM_Old_Fence", PlannerTaskType.Dismantle)));
            Assert.Equal("02 00:00-03 00:00", Times(schedule, Activity(schedule, "IN_Hoarding_RM_02_01", PlannerTaskType.Install)));
            // 02_01 on 10 Sep: its own install, then the hoarding's removal 01_01 names it for (group 2), its end-of-layer removal
            // over the last slot.
            var removal = Activity(schedule, "IN_Hoarding_RM_02_01", PlannerTaskType.Dismantle);
            Assert.Equal("Hoarding_RM_02_01", removal.Name);
            Assert.Equal("02_01_Reinstate", removal.Home.Label);
            Assert.Equal("10 12:00-11 00:00", Times(schedule, removal));
            Assert.Equal("10 00:00-10 12:00", Times(schedule, Activity(schedule, "IN_New_Kerb", PlannerTaskType.Install)));
            Assert.Equal("10 12:00-11 00:00", Times(schedule, Activity(schedule, "Kerb_Edge_RM", PlannerTaskType.Dismantle)));
            // The columns hold everything filed in the folder: groups 1, 2 and 3.
            var reinstate = schedule.Layers.Single(l => l.Label == "02_01_Reinstate");
            Assert.Equal(new[] { "IN_Hoarding_RM_02_01", "Kerb_Edge_RM" }, schedule.ColumnOf(reinstate, PlannerTaskType.Dismantle).SelectMany(a => a.Members).OrderBy(n => n));
            Assert.Equal(PlannerDateSource.Planner, reinstate.DateSource);
        }

        [Fact]
        public void AnUndatedRowIsParkedTbcAndAReferenceToNoLayerAfterTheLastDatedWork()
        {
            var schedule = Fill(SceneList(), Scene());
            var kerbs = schedule.Layers.Single(l => l.Label == "01_02_Kerbs");
            Assert.Equal(Start, kerbs.ParkedOn);
            Assert.True(kerbs.Tbc);
            Assert.Equal(PlannerDateSource.None, kerbs.DateSource);
            Assert.All(schedule.ColumnOf(kerbs, PlannerTaskType.Install), a => Assert.True(a.Tbc));
            Assert.Single(schedule.V2.ParkedLayers, p => p.Folder == "01_02_Kerbs" && p.Day == Start);
            // RM_Bench_IN is taken away while 01_02 runs and put back at its end.
            Assert.True(Activity(schedule, "RM_Bench_IN", PlannerTaskType.Install).StartMs >= Activity(schedule, "RM_Bench_IN", PlannerTaskType.Dismantle).EndMs - 1);
            // The barrier's removal names 01_23, which nobody has: filed with its object, parked the day after the last dated
            // work (the list-only 02_02 on 15 Sep), TBC.
            var missing = Activity(schedule, "IN_Barrier_RM_01_23", PlannerTaskType.Dismantle);
            Assert.True(missing.Unresolved);
            Assert.Equal("01_02_Kerbs", missing.Home.Label);
            Assert.Equal("16 00:00-17 00:00", Times(schedule, missing));
            Assert.Equal(new DateTime(2026, 9, 15), schedule.WorksEnd);
            Assert.Single(schedule.V2.MissingLayers, m => m.Layer == "01_23");
            // The list-only layer keeps its own dates, and is listed.
            var planned = schedule.Layers.Single(l => l.Label == "02_02_Planned");
            Assert.False(planned.InScene);
            Assert.Equal(new DateTime(2026, 9, 15), planned.Start);
            Assert.Equal(new[] { "02_02_Planned" }, schedule.V2.FoldersWithoutLayer);
            Assert.Contains(schedule.ActiveOn(new DateTime(2026, 9, 15)), l => l == planned);
            Assert.Contains(schedule.ActiveOn(Start), l => l == kerbs);
        }

        [Fact]
        public void TheNewReasonsSayWhyObjectsAreNotScheduled()
        {
            var schedule = Fill(SceneList(), Scene());
            Func<string, string> reason = name => schedule.NotScheduled.Single(n => n.Name == name).Reason;
            Assert.Equal(PlannerSchedule.OldTagReasonV2, reason("Ph1_St02_IN_Old"));
            Assert.StartsWith(PlannerSchedule.NoTagReasonV2, reason("Plain_Box"));
            Assert.StartsWith(PlannerSchedule.NoLayerReasonV2, reason("IN_Loose"));
            Assert.Contains("names layer 01_23", reason("IN_Barrier_RM_01_23"));
            Assert.Equal(new[] { "Ph1_St02_IN_Old" }, schedule.V2.OtherNaming);
            Assert.Equal(new[] { "IN_Loose" }, schedule.V2.Unfiled);
        }

        [Fact]
        public void ALayerWhoseWorkThePlannerBindsAlreadyTakesNoParkedDay()
        {
            var scene = Scene();
            var options = Options(null, scene.Snapshot);
            options.ProjectStart = Start;
            var parked = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Assert.Equal(new[] { "01_01_Strip_Out", "01_02_Kerbs", "02_01_Reinstate" }, parked.V2.ParkedLayers.Select(p => p.Folder));
            Assert.Equal(new[] { Start, Start.AddDays(1), Start.AddDays(2) }, parked.V2.ParkedLayers.Select(p => p.Day));
            // Every event of 01_01 bound already: it sits on the next day all the same, but takes none of its own.
            var work = new PlannerExistingWork { Type = PlannerTaskType.Dismantle };
            work.Objects.Add("RM_Old_Fence");
            var install = new PlannerExistingWork { Type = PlannerTaskType.Install };
            install.Objects.Add("IN_Hoarding_RM_02_01");
            options.ExistingWork.Add(work);
            options.ExistingWork.Add(install);
            var kept = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Assert.Equal(new[] { "01_02_Kerbs", "02_01_Reinstate" }, kept.V2.ParkedLayers.Select(p => p.Folder));
            Assert.Equal(new[] { Start, Start.AddDays(1) }, kept.V2.ParkedLayers.Select(p => p.Day));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Precedence, rows and the Gantt

        [Fact]
        public void PreviewDatesComeFromTheListElseTheLayerIsParked()
        {
            // No date is set in 3ds Max (D20). A row the list has wins, even undated (01_02, parked); a layer it has no row for
            // (02_01) is parked TBC, and with no list at all every layer is.
            var scene = Scene();
            var list = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31");
            var schedule = Fill(list, scene);
            Assert.Equal(PlannerDateSource.Planner, schedule.Layers.Single(l => l.Label == "01_01_Strip_Out").DateSource);
            Assert.Equal(PlannerDateSource.None, schedule.Layers.Single(l => l.Label == "01_02_Kerbs").DateSource);
            Assert.Equal(Start, schedule.Layers.Single(l => l.Label == "01_02_Kerbs").ParkedOn);
            var reinstate = schedule.Layers.Single(l => l.Id != null && PlannerNaming.LayerIdText(l.Id) == "02_01");
            Assert.Equal(PlannerDateSource.None, reinstate.DateSource);
            Assert.True(reinstate.ParkedOn.HasValue);
            Assert.True(reinstate.Tbc);
            var unlisted = Fill(null, scene);
            Assert.All(unlisted.Layers.Where(l => l.InScene), l =>
            {
                Assert.Equal(PlannerDateSource.None, l.DateSource);
                Assert.True(l.ParkedOn.HasValue);
            });
        }

        [Fact]
        public void TbcRowsAreTheListsTbcAndTheLayersItDoesNotHave()
        {
            // D21: the tree highlights a row the list marks TBC (02_03 dated but not confirmed, 01_02 undated) and a layer of
            // the scene the list does not have (02_05, and one added in Max), never a group or a settled row.
            var scene = Scene();
            var works = scene.Unit("02_05_Works", "_02_Zone", scene.Find("_02_Zone"));
            scene.Object("IN_Work_Box", works, "02_05_Works");
            scene.Unit("02_06_Added", "_02_Zone", scene.Find("_02_Zone"));
            var list = List(
                "g1,,_01_Zone,Group,,,,,Zone,,,,1,2026-08-31",
                "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,g1,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31",
                "g2,,_02_Zone,Group,,,,,Zone,,,,2,2026-08-31",
                "l3,g2,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,1,2026-08-31",
                "l5,g2,02_03_Later,Layer,02_03,2,3,,Later,2026-09-20,2026-09-22,Yes,3,2026-08-31");
            var added = new List<PlannerAddedLayer> { new PlannerAddedLayer { Layer = "02_06_Added", ParentLayer = "_02_Zone", Kind = "Layer", Id = "02_06", Name = "Added" } };
            var sceneModel = new PlannerSceneModel(scene.Snapshot);
            var plan = PlannerLayerPlanner.Plan(list, sceneModel);
            Func<PlannerSchedule, List<PlannerScheduleRow>> rowsOf = s =>
                PlannerScheduleRows.Tree(list, plan, sceneModel, added, s, PlannerNamingKind.New)
                    .Concat(PlannerScheduleRows.Unlisted(plan, added, s, PlannerNamingKind.New)).ToList();
            foreach (var schedule in new[] { null, Fill(list, scene) })
            {
                var rows = rowsOf(schedule);
                Func<string, PlannerRowTbc> mark = name => rows.First(r => r.Name == name).TbcMark;
                Assert.Equal(PlannerRowTbc.None, mark("_01_Zone"));
                Assert.Equal(PlannerRowTbc.None, mark("01_01_Strip_Out"));
                Assert.Equal(PlannerRowTbc.NotSettled, mark("01_02_Kerbs"));
                Assert.Equal(PlannerRowTbc.None, mark("02_01_Reinstate"));
                Assert.Equal(PlannerRowTbc.NotSettled, mark("02_03_Later"));
                Assert.Equal(PlannerRowTbc.NotInList, mark("02_05_Works"));
                Assert.Equal(PlannerRowTbc.NotInList, mark("02_06_Added"));
                Assert.Equal(PlannerScheduleRowSource.Added, rows.First(r => r.Name == "02_06_Added").Source);
            }
            // With no list, every layer of the scene, none of its groups.
            var noList = PlannerScheduleRows.Tree(null, null, sceneModel, null, null, PlannerNamingKind.New);
            Assert.All(noList.Where(r => r.Kind == "Layer"), r => Assert.Equal(PlannerRowTbc.NoList, r.TbcMark));
            Assert.All(noList.Where(r => r.Kind == "Group"), r => Assert.Equal(PlannerRowTbc.None, r.TbcMark));
            // What the tooltip, the TBC column and the Gantt's hover say.
            Assert.Equal("TBC: the Planner has not settled this layer's dates yet.", PlannerScheduleRows.TbcText(PlannerRowTbc.NotSettled));
            Assert.Equal("TBC: this layer is not in the Planner's list yet, so the Planner has no dates for it.", PlannerScheduleRows.TbcText(PlannerRowTbc.NotInList));
            Assert.Equal("TBC: no layer list is imported, so the Planner's dates for this layer are not known here.", PlannerScheduleRows.TbcText(PlannerRowTbc.NoList));
            Assert.Null(PlannerScheduleRows.TbcText(PlannerRowTbc.None));
            List<PlannerScheduleRow> ganttRows;
            var filled = Fill(list, scene);
            var gantt = Gantt(list, scene, filled, out ganttRows);
            int later = ganttRows.FindIndex(r => r.Name == "02_03_Later");
            Assert.Contains("TBC: the Planner has not settled this layer's dates yet.", gantt.Describe(later, gantt.IndexOf(new DateTime(2026, 9, 21)), CultureInfo.InvariantCulture));
            int strip = ganttRows.FindIndex(r => r.Name == "01_01_Strip_Out");
            Assert.DoesNotContain("TBC:", gantt.Describe(strip, gantt.IndexOf(new DateTime(2026, 9, 1)), CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ARowBuildFindsByItsPlannerIdKeepsItsDatesUnderAnotherId()
        {
            // The Planner changed 02_01's ID to 02_03; the helper still carries the row's Planner ID.
            var list = List("l3,,02_03_Reinstate,Layer,02_03,2,3,,Reinstate,2026-09-12,2026-09-12,,1,2026-08-31");
            var schedule = Fill(list, Scene());
            var layer = schedule.Layers.Single(l => l.Label == "02_03_Reinstate");
            Assert.True(layer.InScene);
            Assert.Equal(PlannerDateSource.Planner, layer.DateSource);
            Assert.Equal(new DateTime(2026, 9, 12), layer.Start);
            Assert.Equal("02_03_Reinstate", Activity(schedule, "IN_New_Kerb", PlannerTaskType.Install).Home.Label);
        }

        [Fact]
        public void RowsFindTheirLayersByIdAndNeverReadDatesFromNames()
        {
            var scene = Scene();
            var list = SceneList();
            var options = Options(list, scene.Snapshot);
            var schedule = PlannerSchedule.BuildV2(scene.Snapshot, options);
            var sceneModel = new PlannerSceneModel(scene.Snapshot);
            var plan = PlannerLayerPlanner.Plan(list, sceneModel);
            var rows = PlannerScheduleRows.Tree(list, plan, sceneModel, null, schedule, PlannerNamingKind.New);
            var planned = rows.Single(r => r.Name == "02_02_Planned");
            Assert.NotNull(planned.Layer);
            Assert.Equal(new DateTime(2026, 9, 15), planned.StartDate);
            Assert.Equal(PlannerDateSource.Planner, planned.DateSource);
            var reinstate = rows.Single(r => r.Name == "02_01_Reinstate");
            Assert.Equal(2, reinstate.RmCount);
            // Every activity is filed in a layer's folder, which dates it (the old names' work placed by no layer is gone).
            Assert.All(schedule.Activities, a => Assert.True(a.DatedBy != null && a.DatedBy.Id != null, a.Name));
            // With no list, the scene's own rows carry no dates, whatever their names say.
            var dated = Scene();
            dated.Unit("01_03_Works_TBC_01-09-26", "_01_Zone", dated.Find("_01_Zone"));
            var sceneRows = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(dated.Snapshot), null, null, PlannerNamingKind.New);
            var works = sceneRows.Single(r => r.Name == "01_03_Works_TBC_01-09-26");
            Assert.Equal("Layer", works.Kind);
            Assert.Equal(string.Empty, works.Start);
            Assert.False(works.Tbc);
            Assert.Null(works.StartDate);
        }

        [Fact]
        public void TheGanttSaysWhereEachLayersDatesComeFrom()
        {
            var scene = Scene();
            var list = SceneList();
            var schedule = PlannerSchedule.BuildV2(scene.Snapshot, Options(list, scene.Snapshot));
            var sceneModel = new PlannerSceneModel(scene.Snapshot);
            var rows = PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, sceneModel), sceneModel, null, schedule, PlannerNamingKind.New);
            var gantt = PlannerGanttModel.Build(rows, schedule, new DateTime(2026, 10, 15));
            int strip = rows.FindIndex(r => r.Name == "01_01_Strip_Out");
            int kerbs = rows.FindIndex(r => r.Name == "01_02_Kerbs");
            Assert.Equal(PlannerDateSource.Planner, gantt.Rows[strip].DateSource);
            Assert.Contains("From the Planner.", gantt.Describe(strip, gantt.IndexOf(new DateTime(2026, 9, 1)), CultureInfo.InvariantCulture));
            var parked = gantt.Describe(kerbs, gantt.IndexOf(Start), CultureInfo.InvariantCulture);
            Assert.Contains("Its dates: none. The preview parks its work on 31 Aug 2026", parked);
            Assert.True(gantt.Rows[kerbs].OwnTbc);
            // The parked removal's day after the last dated work is in the TBC zone.
            Assert.True(gantt.InTbcZone(gantt.IndexOf(new DateTime(2026, 9, 16))));
            Assert.Contains("removals for a layer nobody has", gantt.Describe(-1, gantt.IndexOf(new DateTime(2026, 9, 16)), CultureInfo.InvariantCulture));
        }

        [Fact]
        public void TheListsGroupsHoldTheirLayersAndNoEmptyGroupIsAdded()
        {
            Func<PlannerSchedule, string> tree = s => string.Join(" | ", s.Folders.Select(f => f.Name + " in " + (f.ParentName ?? "(top)")).OrderBy(t => t, StringComparer.Ordinal));
            // The list's groups, named as the scene's: each layer stays in its group, and the Generate adds no group of its own.
            var schedule = Fill(SceneList(), Scene());
            Assert.Equal("01_01_Strip_Out in _01_Zone | 01_02_Kerbs in _01_Zone | 02_01_Reinstate in _02_Zone | 02_02_Planned in _02_Zone | _01_Zone in (top) | _02_Zone in (top)",
                tree(schedule));
            // A group the Planner renamed is the folder its layers sit in (at least half of what it holds is there).
            var renamed = List(
                "g1,,_01_Zone_Five,Group,,,,,Zone_Five,,,,1,2026-08-31",
                "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,g1,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31");
            Assert.Equal("01_01_Strip_Out in _01_Zone_Five | 01_02_Kerbs in _01_Zone_Five | 02_01_Reinstate in _02_Zone | _01_Zone_Five in (top) | _02_Zone in (top)",
                tree(Fill(renamed, Scene())));
            // Layers the Planner keeps in two other folders: the scene's group gets no folder (it would hold nothing), and a
            // layer at the top is filed in the group's folder the Generate adds, as the Planner's merge files it.
            var split = List(
                "a,,Misc_A,Group,,,,,Misc_A,,,,1,2026-08-31",
                "b,,Misc_B,Group,,,,,Misc_B,,,,2,2026-08-31",
                "l1,a,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,b,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,1,2026-08-31",
                "l3,,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,3,2026-08-31");
            Assert.Equal("01_01_Strip_Out in Misc_A | 01_02_Kerbs in Misc_B | 02_01_Reinstate in _02_Zone | Misc_A in (top) | Misc_B in (top) | _02_Zone in (top)",
                tree(Fill(split, Scene())));
        }

        [Fact]
        public void TheScrubbersSpanStartsWithTheListsEarliestDateBeforeTheProjectStart()
        {
            // The project start never moves on the new names: a layer the list dates before it widens the span instead.
            var list = List("l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-08-20,2026-08-21,,1,2026-08-31");
            var schedule = Fill(list, Scene());
            Assert.Equal(Start, schedule.ProjectStart);
            DateTime first, last;
            schedule.Span(out first, out last);
            Assert.Equal(new DateTime(2026, 8, 20), first);
            // Its other two layers are parked on the project start and the day after.
            Assert.Equal(Start.AddDays(1), last);
        }

        // ---------------------------------------------------------------------------------------------------------
        // The 4D view and the preview (once the old names' engine's tests; the same code reads this engine's schedule)

        [Fact]
        public void TheScrubberFindsTheDaysObjectsChangeOn()
        {
            var scene = Scene();
            var schedule = Fill(SceneList(), scene);
            Func<int, int, string[]> changing = (month, day) => schedule.ChangingOn(new DateTime(2026, month, day)).Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            // The kerbs' layer, undated, is parked on the project start: its work, and the barrier's install, change that day.
            Assert.Equal(new[] { "IN_Barrier_RM_01_23", "IN_Kerb_A", "IN_Kerb_B", "RM_Bench_IN" }, changing(8, 31));
            // A removal changes on the day of its last minute (the fence goes at midnight after 1 Sep), an install on the day it starts.
            Assert.Equal(new[] { "RM_Old_Fence" }, changing(9, 1));
            Assert.Equal(new[] { "IN_Hoarding_RM_02_01" }, changing(9, 2));
            Assert.Empty(changing(9, 5));
            Assert.Equal(new[] { "IN_Hoarding_RM_02_01", "IN_New_Kerb", "Kerb_Edge_RM" }, changing(9, 10));
            // The barrier's removal names a layer nobody has: parked the day after the works end (15 Sep).
            Assert.Equal(new DateTime(2026, 9, 15), schedule.WorksEnd);
            Assert.Equal(new[] { "IN_Barrier_RM_01_23" }, changing(9, 16));
            // A name the schedule binds nothing to always shows; a bound one as its events say.
            Assert.True(schedule.VisibleAt("Plain_Box", schedule.Midnight(new DateTime(2026, 9, 1))));
            Assert.False(schedule.VisibleAt("IN_New_Kerb", schedule.Midnight(new DateTime(2026, 9, 9))));
            Assert.True(schedule.VisibleAt("IN_New_Kerb", schedule.Midnight(new DateTime(2026, 9, 11))));
            // Where each object's work is placed: the layer whose folder holds it.
            Assert.Equal(new[] { "IN 01_02_Kerbs", "RM 01_02_Kerbs" }, schedule.PlacesOf("IN_Barrier_RM_01_23").ToArray());
        }

        [Fact]
        public void LeavingThePreviewGivesEveryNodeItsOwnHiddenStateBack()
        {
            var scene = Scene();
            var schedule = Fill(SceneList(), scene);
            // The new kerb is hidden by the modeller as well as, until 10 Sep, by the view.
            var modellerHidden = scene.Find("IN_New_Kerb");
            // The node's own hidden flags, as 3ds Max holds them, the preview's changes carried out on them.
            var own = scene.Snapshot.Nodes.ToDictionary(n => n.Key, n => n.Key == modellerHidden.Key);
            var original = new Dictionary<long, bool>(own);
            var preview = new PlannerPreview();
            foreach (var day in new[] { new DateTime(2026, 8, 30), new DateTime(2026, 8, 31), new DateTime(2026, 9, 1), new DateTime(2026, 9, 2),
                new DateTime(2026, 9, 10), new DateTime(2026, 9, 11), new DateTime(2026, 9, 3), new DateTime(2026, 9, 17) })
            {
                var toHide = new List<long>();
                var toShow = new List<long>();
                var view = schedule.HiddenAt(schedule.Midnight(day));
                preview.Step(view, k => own[k], toHide, toShow);
                Assert.DoesNotContain(modellerHidden.Key, toHide);
                Assert.DoesNotContain(modellerHidden.Key, toShow);
                toHide.ForEach(k => { Assert.False(own[k]); own[k] = true; });
                toShow.ForEach(k => { Assert.True(own[k]); own[k] = false; });
                // Exactly what the view hides is hidden, and what the modeller hid stays hidden.
                Assert.All(view, k => Assert.True(own[k]));
                Assert.All(own.Where(p => !view.Contains(p.Key)), p => Assert.Equal(original[p.Key], p.Value));
            }
            Assert.NotEqual(0, preview.Count);
            preview.Restore().ForEach(k => own[k] = false);
            Assert.Equal(original, own);
            Assert.Equal(0, preview.Count);
        }

        [Fact]
        public void TheViewHidesWhatIsUnderAnObjectItHides()
        {
            var scene = Scene();
            var kerb = scene.Find("IN_New_Kerb");
            var bolt = scene.Object("Bolt", kerb, "02_01_Reinstate");
            var schedule = Fill(SceneList(), scene);
            // Part of the kerb, shown and hidden with it: not an object the Planner leaves out.
            Assert.DoesNotContain(schedule.NotScheduled, n => n.Name == "Bolt");
            var early = schedule.HiddenAt(schedule.Midnight(new DateTime(2026, 9, 9)));
            Assert.Contains(kerb.Key, early);
            Assert.Contains(bolt.Key, early);
            Assert.DoesNotContain(scene.Find("Plain_Box").Key, early);
            Assert.DoesNotContain(scene.Find("02_01_Reinstate").Key, early);
            var late = schedule.HiddenAt(schedule.Midnight(new DateTime(2026, 9, 11)));
            Assert.DoesNotContain(kerb.Key, late);
            Assert.DoesNotContain(bolt.Key, late);
            Assert.Contains(scene.Find("RM_Old_Fence").Key, late);
        }

        [Fact]
        public void APreviewStepStaysQuickOnALargeScene()
        {
            // 20,000 objects in 100 layers over a year, a third of them removed by another layer: a scrub step works out the
            // view and the changes.
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var rows = new List<string>();
            var start = new DateTime(2026, 1, 5);
            Func<int, string> idOf = l => (1 + (l - 1) / 50).ToString("00") + "_" + (1 + (l - 1) % 50).ToString("00");
            for (int l = 1; l <= 100; l++)
            {
                var day = start.AddDays(l * 3);
                var name = idOf(l) + "_Layer" + l;
                var helper = scene.Unit(name, "Work_Phasing", root);
                rows.Add("l" + l + ",," + name + ",Layer," + idOf(l) + "," + (1 + (l - 1) / 50) + "," + (1 + (l - 1) % 50) + ",,Layer" + l + "," + day.ToString("yyyy-MM-dd") + ","
                    + day.AddDays(2).ToString("yyyy-MM-dd") + ",," + l + ",2026-01-05");
                for (int o = 0; o < 200; o++)
                {
                    scene.Object("IN_Obj_" + l + "_" + o + (o % 3 == 0 ? "_RM_" + idOf(1 + (l + 6) % 100) : string.Empty), helper, name);
                }
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var schedule = Fill(List(rows.ToArray()), scene);
            var build = watch.ElapsedMilliseconds;
            Assert.True(schedule.Usable, schedule.Message);
            Assert.Equal(20000, schedule.Objects.Count);
            var own = scene.Snapshot.Nodes.ToDictionary(n => n.Key, n => false);
            var preview = new PlannerPreview();
            watch.Restart();
            int steps = 0;
            for (var day = start; day < start.AddDays(320); day = day.AddDays(4), steps++)
            {
                var toHide = new List<long>();
                var toShow = new List<long>();
                preview.Step(schedule.HiddenAt(schedule.Midnight(day)), k => own[k], toHide, toShow);
            }
            Assert.NotEqual(0, preview.Count);
            var perStep = watch.ElapsedMilliseconds / (double)steps;
            Assert.True(perStep < 250, "a step took " + perStep + " ms (build " + build + " ms)");
        }

        [Fact]
        public void ALayerOutsideThePhasingTreeGetsARowOfItsOwn()
        {
            // A layer node the Planner reads outside the phasing root: no row of the tree stands for it, so it gets one after
            // them, and every activity is charted in some row.
            var scene = Scene();
            var far = scene.Unit("03_01_Elsewhere", null, null);
            scene.Object("IN_Far", far, "03_01_Elsewhere");
            var list = SceneList();
            var schedule = Fill(list, scene);
            var sceneModel = new PlannerSceneModel(scene.Snapshot);
            var plan = PlannerLayerPlanner.Plan(list, sceneModel);
            var tree = PlannerScheduleRows.Tree(list, plan, sceneModel, null, schedule, PlannerNamingKind.New);
            var unlisted = PlannerScheduleRows.Unlisted(plan, null, schedule, PlannerNamingKind.New);
            Assert.DoesNotContain(tree.Concat(unlisted), r => r.Name == "03_01_Elsewhere");
            var outside = Assert.Single(PlannerScheduleRows.Extra(tree.Concat(unlisted), schedule));
            Assert.Equal(PlannerScheduleRowSource.Outside, outside.Source);
            Assert.Equal("03_01_Elsewhere", outside.Name);
            Assert.Equal("Layer", outside.Kind);
            Assert.Equal("A layer the Planner reads, outside the phasing tree", outside.Status);
            Assert.Equal(1, outside.InCount);
            // Not in the list, so the Planner has no dates for it: parked, TBC, highlighted as not in the list (D21).
            Assert.True(outside.Tbc);
            Assert.Null(outside.StartDate);
            Assert.Equal(PlannerRowTbc.NotInList, outside.TbcMark);
            Assert.Equal(schedule.Activities.Count, tree.Concat(unlisted).Concat(new[] { outside }).Sum(r => r.In.Count + r.Rm.Count));
            // Once the tree has a row for it, no other.
            Assert.Empty(PlannerScheduleRows.Extra(tree.Concat(unlisted).Concat(new[] { outside }), schedule));
        }

        // ---------------------------------------------------------------------------------------------------------
        // What the tab says (review of 2026-10-18)

        // The scene with no list imported, Planner project start given: every layer parked, nothing dated.
        private static PlannerSchedule FillWithNoList(TestScene scene)
        {
            var options = Options(null, scene.Snapshot);
            options.ProjectStart = Start;
            return PlannerSchedule.BuildV2(scene.Snapshot, options);
        }

        // The tree's rows, the rows of scene layers the list does not have whose work the Planner reads, and the Gantt beside them.
        private static PlannerGanttModel Gantt(PlannerLayerList list, TestScene scene, PlannerSchedule schedule, out List<PlannerScheduleRow> rows)
        {
            var sceneModel = new PlannerSceneModel(scene.Snapshot);
            var plan = list != null ? PlannerLayerPlanner.Plan(list, sceneModel) : null;
            var tree = PlannerScheduleRows.Tree(list, plan, sceneModel, null, schedule, PlannerNamingKind.New);
            var unlisted = PlannerScheduleRows.Unlisted(plan, null, schedule, PlannerNamingKind.New, sceneModel);
            rows = tree.Concat(PlannerScheduleRows.UnlistedAtWork(unlisted)).ToList();
            return PlannerGanttModel.Build(rows, schedule, new DateTime(2026, 10, 15));
        }

        [Fact]
        public void FoldersSharingAnIdSayThePlannerMayDateTheLayerByAnotherOfThem()
        {
            // Two rows of the list carry 01_01. The preview takes the row the list keeps, the first in the file; the Planner the
            // first in its own folder order, which the list does not give (it writes siblings by name).
            var list = List(
                "x1,,01_01_Strip_Alt,Layer,01_01,1,1,,Strip_Alt,2026-09-03,2026-09-04,,1,2026-08-31",
                "x5,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,2,2026-08-31");
            var notes = Fill(list, Scene()).NotesV2();
            Assert.Contains(notes, n => n.StartsWith("The list's rows share the layer ID 01_01: 01_01_Strip_Alt, 01_01_Strip_Out.", StringComparison.Ordinal)
                && n.Contains("which the list does not give"));
            // Nor is the preview's pick said to be the name the Planner keeps.
            Assert.DoesNotContain(notes, n => n.Contains("01_01_Strip_Alt") && n.Contains("own name"));
        }

        [Fact]
        public void OfRowsSharingAnIdThePreviewDatesTheLayerByTheRowTheListCheckKeeps()
        {
            // Two rows carry 02_01. The list check keeps the first in the file (Zeta) and makes the later one (Alpha) a problem,
            // but the outline sorts siblings by name (D24), so Alpha comes first there: the preview still takes Zeta's row.
            var list = List(
                "g1,,_02_Works,Group,,,,,Works,,,,1,2026-08-31",
                "p1,g1,02_01_Zeta,Layer,02_01,2,1,,Zeta,2026-09-01,2026-09-02,,1,2026-08-31",
                "p2,g1,02_01_Alpha,Layer,02_01,2,1,,Alpha,2026-09-14,2026-09-15,,2,2026-08-31");
            var zeta = list.Rows.Single(r => r.PlannerId == "p1");
            var alpha = list.Rows.Single(r => r.PlannerId == "p2");
            Assert.False(zeta.HasProblem);
            Assert.Contains(alpha.Problems, p => p.StartsWith("line 3 has the same layer ID, 02_01", StringComparison.Ordinal));
            Assert.Equal(new[] { "_02_Works", "02_01_Alpha", "02_01_Zeta" }, list.Outline().Select(r => r.Layer).ToArray());

            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var works = scene.Unit("_02_Works", "Work_Phasing", root, "g1");
            var helper = scene.Unit("02_01_Zeta", "_02_Works", works, "p1");
            scene.Object("IN_Zeta", helper, "02_01_Zeta");
            var schedule = Fill(list, scene);
            Assert.True(schedule.Usable, schedule.Message);
            // Zeta's work runs over Zeta's days, 1 and 2 September, not over Alpha's.
            Assert.Equal("01 00:00-03 00:00", Times(schedule, Activity(schedule, "IN_Zeta", PlannerTaskType.Install)));
            // The tree's Zeta row shows the layer of Zeta's row, with its work; Alpha's row has its own, with none.
            var sceneModel = new PlannerSceneModel(scene.Snapshot);
            var tree = PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, sceneModel), sceneModel, null, schedule, PlannerNamingKind.New);
            var zetaLine = tree.Single(r => r.ListRow == zeta);
            Assert.Same(zeta, zetaLine.Layer.ListRow);
            Assert.Equal(1, zetaLine.InCount);
            var alphaLine = tree.Single(r => r.ListRow == alpha);
            Assert.True(alphaLine.Layer == null || alphaLine.Layer.ListRow == alpha);
            Assert.Equal(0, alphaLine.InCount);
            // The note names the row the preview took first, and says which one that is.
            Assert.Contains(schedule.NotesV2(), n => n.StartsWith("The list's rows share the layer ID 02_01: 02_01_Zeta, 02_01_Alpha.", StringComparison.Ordinal)
                && n.Contains("the row the list keeps"));
        }

        [Fact]
        public void DuplicateActivityNamesAreListedInTheOrderOfTheirFirstCopy()
        {
            // Kerb01 and Kerb1 compare equal, so their activities sort together, met A B B A.
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var kerbs = scene.Unit("03_01_Kerbs", "Work_Phasing", root);
            scene.Object("IN_Kerb01", kerbs, "03_01_Kerbs");
            scene.Object("IN_Kerb1", kerbs, "03_01_Kerbs");
            scene.Object("IN_Kerb1", kerbs, "03_01_Kerbs");
            scene.Object("IN_Kerb01", kerbs, "03_01_Kerbs");
            var schedule = Fill(List("x1,,03_01_Kerbs,Layer,03_01,3,1,,Kerbs,2026-10-06,2026-10-07,,1,2026-10-05"), scene);
            Assert.Equal(new[] { "Kerb01_IN_03_01", "Kerb1_IN_03_01" }, schedule.V2.DuplicateNames);
        }

        [Fact]
        public void AListIsImportedOneWayWhateverTheScenesNaming()
        {
            // The tab reads the new names only (D23): the Project start column is always read, so it is never a column not read.
            var list = PlannerLayerList.Parse(TestCsv.Header + ",Project start\r\nl1,,01_01_Kerbs_01-09-26_03-09-26,Layer,01_01,1,1,,Kerbs,2026-09-01,2026-09-03,,1,2026-08-31\r\n");
            Assert.Empty(list.Notes);
            Assert.Equal(new DateTime(2026, 8, 31), list.ProjectStart);
            // With another column the reader does not know, and separated by semicolons: in the order the import says them.
            var other = PlannerLayerList.Parse("Planner ID;Parent ID;Layer;Kind;Owner;ID;Phase;Stage;Sub-stage;Name;Start;Finish;TBC;Order;Project start\r\n"
                + "l1;;01_01_Kerbs;Layer;Ben;01_01;1;1;;Kerbs;2026-09-01;2026-09-03;;1;2026-08-31\r\n");
            Assert.Equal(new[] { "The list is separated by semicolons, as Excel saves it in some regions; read that way.", "Columns not read: Owner." }, other.Notes);
            // A file that is not a list: the Planner's header now (Project start, which a list may lack, last).
            var bad = PlannerLayerList.Parse("Planner ID,Parent ID,Layer\r\nl1,,01_01_Kerbs\r\n");
            Assert.Equal(new[] { "It has no \"Kind\" column in its first line, so it is not a layer list from the Planner (the first line should be "
                + "Planner ID,Parent ID,Layer,Kind,ID,Phase,Stage,Sub-stage,Name,Start,Finish,TBC,Order,Project start)." }, bad.Errors);
        }

        [Fact]
        public void ALayerNewInMaxBesideOneDayTbcRowsMayParkElsewhereInThePlanner()
        {
            // The list shows 01_01 on one TBC day, as it writes a layer the Planner still has parked; it has no row for 01_02.
            // The preview parks 01_02 from the project start; the Planner parks a layer it does not have yet after its still-
            // parked layers' days, which the list does not say. Nor does the Planner check an order against a parked layer.
            var scene = Scene();
            scene.Object("IN_Gate_RM_01_01", scene.Find("02_01_Reinstate"), "02_01_Reinstate");
            var list = List(
                "g1,,_01_Zone,Group,,,,,Zone,,,,1,2026-08-31",
                "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-08-31,2026-08-31,Yes,1,2026-08-31",
                "g2,,_02_Zone,Group,,,,,Zone,,,,2,2026-08-31",
                "l3,g2,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,1,2026-08-31");
            var schedule = Fill(list, scene);
            Assert.Equal(Start, schedule.Layers.Single(l => l.Label == "01_02_Kerbs").ParkedOn);
            Assert.Contains("the Planner's day may differ", schedule.SummaryV2(true));
            List<PlannerScheduleRow> rows;
            var gantt = Gantt(list, scene, schedule, out rows);
            int kerbs = rows.FindIndex(r => r.Name == "01_02_Kerbs");
            var hover = gantt.Describe(kerbs, gantt.IndexOf(Start), CultureInfo.InvariantCulture);
            Assert.Contains("The preview parks its work on 31 Aug 2026", hover);
            Assert.Contains("the Planner's day may differ", hover);
            Assert.DoesNotContain("as the Planner parks a layer with no dates", hover);
            // The gate goes up in 02_01 on 10 Sep and comes down in 01_01 on 31 Aug: the other way round, unless 01_01 is still parked.
            Assert.Contains("IN_Gate_RM_01_01", schedule.V2.WrongOrder);
            var notes = schedule.NotesV2();
            Assert.Contains(notes, n => n.Contains("IN_Gate_RM_01_01") && n.Contains("may still be parked"));
            Assert.DoesNotContain(notes, n => n.StartsWith("1 object's two events run the other way round from the name:", StringComparison.Ordinal));
            // With the list's 01_01 dated over two days, nothing may be parked: the plain wording.
            var dated = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-08-31,2026-09-01,Yes,1,2026-08-31",
                "l3,,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,2,2026-08-31");
            var plain = Fill(dated, scene);
            Assert.DoesNotContain("may differ", plain.SummaryV2(true));
            Assert.Contains(plain.NotesV2(), n => n.StartsWith("1 object's two events run the other way round from the name: IN_Gate_RM_01_01.", StringComparison.Ordinal));
        }

        [Fact]
        public void ObjectsReadOnlyInPartAreCountedApartFromThoseNotScheduled()
        {
            // IN_Barrier_RM_01_23 is scheduled (installed in 01_02); only its removal names a layer nobody has.
            var schedule = Fill(SceneList(), Scene());
            var barrier = schedule.NotScheduled.Single(n => n.Name == "IN_Barrier_RM_01_23");
            Assert.True(barrier.InPart);
            Assert.True(schedule.Objects.ContainsKey("IN_Barrier_RM_01_23"));
            Assert.Equal(3, schedule.NotScheduled.Count(n => !n.InPart));
            var summary = schedule.SummaryV2(true);
            Assert.Contains("8 objects scheduled (1 read only in part)", summary);
            Assert.Contains(", 3 not scheduled;", summary);
        }

        [Fact]
        public void WithNothingDatedARemovalForALayerNobodyHasIsSaidToParkAfterTheLastParkedDay()
        {
            // No list: every layer parked (31 Aug, 1 and 2 Sep), so the barrier's removal for 01_23 parks on 3 Sep.
            var scene = Scene();
            var schedule = FillWithNoList(scene);
            var missing = Activity(schedule, "IN_Barrier_RM_01_23", PlannerTaskType.Dismantle);
            Assert.Equal(new DateTime(2026, 9, 3), schedule.LocalTime(missing.StartMs).Date);
            var reason = schedule.NotScheduled.Single(n => n.Name == "IN_Barrier_RM_01_23").Reason;
            Assert.Contains("the Planner parks it TBC the day after the last parked day", reason);
            Assert.DoesNotContain("dated work", reason);
            Assert.Contains(schedule.NotesV2(), n => n.Contains("(a removal the day after the last parked day, an install on the project start)"));
            List<PlannerScheduleRow> rows;
            var gantt = Gantt(null, scene, schedule, out rows);
            var zone = gantt.Describe(-1, gantt.IndexOf(new DateTime(2026, 9, 3)), CultureInfo.InvariantCulture);
            Assert.Contains("after the last parked day, the TBC zone", zone);
            // With the list's dates, after the last dated work.
            var dated = Fill(SceneList(), Scene());
            Assert.Contains("the Planner parks it TBC the day after the last dated work", dated.NotScheduled.Single(n => n.Name == "IN_Barrier_RM_01_23").Reason);
            Assert.Contains(dated.NotesV2(), n => n.Contains("(a removal the day after the last dated work, an install on the project start)"));
        }

        [Fact]
        public void RenamedLayersNumberedObjectsAndAnOldFormListAreSaidPlainly()
        {
            // A layer the Planner renamed after its ID.
            var renamed = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l3,,02_01_Reinstated_Kerbs,Layer,02_01,2,1,,Reinstated_Kerbs,2026-09-10,2026-09-10,,2,2026-08-31");
            Assert.Contains("Layer 02_01 is 02_01_Reinstated_Kerbs in the Planner and 02_01_Reinstate in the scene: the Planner keeps its own name, which Clean up scene graph carries to 3ds Max.",
                Fill(renamed, Scene()).NotesV2());
            // Two objects the export numbers.
            var scene = Scene();
            scene.Object("IN_Kerb_A", scene.Find("01_02_Kerbs"), "01_02_Kerbs");
            scene.Object("IN_New_Kerb", scene.Find("02_01_Reinstate"), "02_01_Reinstate");
            Assert.Contains("2 scheduled objects reach the Planner with \" (n)\" after their names, as other objects have the same names: IN_Kerb_A (1), IN_New_Kerb (1). "
                + "Give each object its own name in 3ds Max.", Fill(SceneList(), scene).NotesV2());
            // A list from before the new names: its Layer names carry dates or TBC. One note, not a rename note per layer.
            var oldForm = List(
                "l1,,01_01_Strip_Out_01-09-26_02-09-26,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,,01_02_Kerbs_TBC,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31",
                "l3,,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,3,2026-08-31");
            var notes = Fill(oldForm, Scene()).NotesV2();
            Assert.Single(notes, n => n.Contains("carry dates or TBC"));
            Assert.Contains(notes, n => n.Contains("01_01_Strip_Out_01-09-26_02-09-26") && n.Contains("01_02_Kerbs_TBC") && n.Contains("export the list from the Planner again"));
            Assert.DoesNotContain(notes, n => n.Contains("own name"));
        }

        [Fact]
        public void TheTreesTbcColumnShowsParkedLayers()
        {
            // No list: each layer is parked on a TBC day of its own, so the tree says TBC beside the Gantt's hatch.
            var scene = Scene();
            var schedule = FillWithNoList(scene);
            List<PlannerScheduleRow> rows;
            var gantt = Gantt(null, scene, schedule, out rows);
            foreach (var name in new[] { "01_01_Strip_Out", "01_02_Kerbs", "02_01_Reinstate" })
            {
                int i = rows.FindIndex(r => r.Name == name);
                Assert.True(rows[i].Layer.ParkedOn.HasValue, name);
                Assert.True(rows[i].Tbc, name + " shows TBC in the tree");
                Assert.True(gantt.Rows[i].OwnTbc, name + " is hatched");
            }
            // A layer the list dates, not TBC, is not.
            var dated = Fill(SceneList(), Scene());
            var listRows = PlannerScheduleRows.Tree(SceneList(), PlannerLayerPlanner.Plan(SceneList(), new PlannerSceneModel(Scene().Snapshot)), new PlannerSceneModel(Scene().Snapshot), null, dated, PlannerNamingKind.New);
            Assert.False(listRows.Single(r => r.Name == "01_01_Strip_Out").Tbc);
            Assert.True(listRows.Single(r => r.Name == "01_02_Kerbs").Tbc);
        }

        [Fact]
        public void AnUnlistedSceneLayerNeverTakesItsDatesFromItsName()
        {
            // 02_05's name carries a date and TBC; the list has no row for it. Its row and its Gantt bar have no dates of their
            // own: the preview parks its work.
            var scene = Scene();
            var works = scene.Unit("02_05_Works_TBC_01-09-26", "_02_Zone", scene.Find("_02_Zone"));
            scene.Object("IN_Work_Box", works, "02_05_Works_TBC_01-09-26");
            var list = SceneList();
            var schedule = Fill(list, scene);
            List<PlannerScheduleRow> rows;
            var gantt = Gantt(list, scene, schedule, out rows);
            int i = rows.FindIndex(r => r.Name == "02_05_Works_TBC_01-09-26" && r.Source == PlannerScheduleRowSource.Unlisted);
            Assert.True(i >= 0);
            Assert.Equal(string.Empty, rows[i].Start);
            Assert.Null(rows[i].StartDate);
            Assert.Null(gantt.Rows[i].OwnStart);
            Assert.True(rows[i].Layer.ParkedOn.HasValue);
            Assert.True(rows[i].Tbc);
            Assert.Contains("Its dates: none. The preview parks its work on", gantt.Describe(i, gantt.IndexOf(rows[i].Layer.ParkedOn.Value), CultureInfo.InvariantCulture));
        }

        [Fact]
        public void AnUndatedListOnlyLayerWithNoWorkIsAtWorkOnNoDay()
        {
            var list = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l6,,02_03_Later,Layer,02_03,2,3,,Later,,,Yes,2,2026-08-31");
            var schedule = Fill(list, Scene());
            var later = schedule.Layers.Single(l => l.Label == "02_03_Later");
            Assert.False(later.Dated);
            Assert.Null(later.ParkedOn);
            Assert.Equal(Start, later.Start);
            Assert.DoesNotContain(later, schedule.ActiveOn(Start));
        }

        [Fact]
        public void AListRowWhoseFinishIsBeforeItsStartIsADayInTheGantt()
        {
            var list = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-05,2026-09-02,,1,2026-08-31");
            var scene = Scene();
            List<PlannerScheduleRow> rows;
            var gantt = Gantt(list, scene, Fill(list, scene), out rows);
            int i = rows.FindIndex(r => r.Name == "01_01_Strip_Out");
            Assert.Equal(new DateTime(2026, 9, 5), gantt.Rows[i].OwnStart);
            Assert.Equal(new DateTime(2026, 9, 5), gantt.Rows[i].OwnEnd);
        }

        [Fact]
        public void NotScheduledSaysWhenTheDateIsNotKnownYetWhereToLinkAndWhatTheExportCallsIt()
        {
            var scene = Scene();
            scene.Object("IN_Plates_RM_99_99", scene.Find("01_02_Kerbs"), "01_02_Kerbs");
            // A second loose IN_Loose: the export writes it as IN_Loose (1).
            var second = scene.Object("IN_Loose", null, "0");
            var schedule = Fill(SceneList(), scene);
            Assert.Equal("read only in part: its removal is for layer 99_99, work whose date is not known yet: the Planner parks it TBC the day after the last dated work",
                schedule.NotScheduled.Single(n => n.Name == "IN_Plates_RM_99_99").Reason);
            Assert.Equal(PlannerSchedule.NoLayerReasonV2 + " (it sits on the layer 02_01_Reinstate: Link objects to their helpers puts it under the layer's helper)",
                schedule.NotScheduled.Single(n => n.Name == "IN_Loose" && n.Key != second.Key).Reason);
            Assert.EndsWith("; the export calls it IN_Loose (1), as a node it writes before it is called IN_Loose too, so the Planner reads it as an object of its own: give it a unique name",
                schedule.NotScheduled.Single(n => n.Key == second.Key).Reason);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Several phasing roots (naming v2, several roots: spec 4.6, 4.7, R19)

        /// <summary>
        /// Two areas of one schedule (spec section 1): Work_Phasing_South made first, so 3ds Max's order is not the name order,
        /// then Work_Phasing_North; each root a helper on the Max layer of its name, its phase groups and layers under it, the
        /// objects linked under their layers' helpers. South's hoarding comes down in North's 02_01.
        /// </summary>
        internal static TestScene TwoRoots()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var south = scene.Unit("Work_Phasing_South", null, null);
            var southEnabling = scene.Unit("_07_South_Enabling", "Work_Phasing_South", south, "g7");
            var hoarding = scene.Unit("07_01_Hoarding", "_07_South_Enabling", southEnabling, "l7");
            scene.Object("IN_Hoarding_RM_02_01", hoarding, "07_01_Hoarding");
            var southRoads = scene.Unit("_08_South_Roads", "Work_Phasing_South", south, "g8");
            var southBinder = scene.Unit("08_01_Binder", "_08_South_Roads", southRoads, "l8");
            scene.Object("IN_Binder_02", southBinder, "08_01_Binder");
            var north = scene.Unit("Work_Phasing_North", null, null);
            var enabling = scene.Unit("_01_North_Enabling", "Work_Phasing_North", north, "g1");
            var drainage = scene.Unit("01_01_Drainage", "_01_North_Enabling", enabling, "l1");
            scene.Object("IN_Pipe", drainage, "01_01_Drainage");
            var roads = scene.Unit("_02_North_Roads", "Work_Phasing_North", north, "g2");
            var binder = scene.Unit("02_01_Binder", "_02_North_Roads", roads, "l2");
            scene.Object("IN_Binder", binder, "02_01_Binder");
            return scene;
        }

        // The list a Planner project of the two areas writes: a Root row for each root folder, its groups under it.
        private static PlannerLayerList TwoRootsList()
        {
            return List(
                "f1,,Work_Phasing_North,Root,,,,,Work_Phasing_North,,,,1,2026-08-31",
                "g1,f1,_01_North_Enabling,Group,,,,,North_Enabling,,,,1,2026-08-31",
                "l1,g1,01_01_Drainage,Layer,01_01,1,1,,Drainage,2026-09-01,2026-09-02,,1,2026-08-31",
                "g2,f1,_02_North_Roads,Group,,,,,North_Roads,,,,2,2026-08-31",
                "l2,g2,02_01_Binder,Layer,02_01,2,1,,Binder,2026-09-10,2026-09-11,,1,2026-08-31",
                "f8,,Work_Phasing_South,Root,,,,,Work_Phasing_South,,,,2,2026-08-31",
                "g7,f8,_07_South_Enabling,Group,,,,,South_Enabling,,,,1,2026-08-31",
                "l7,g7,07_01_Hoarding,Layer,07_01,7,1,,Hoarding,2026-09-03,2026-09-04,,1,2026-08-31",
                "g8,f8,_08_South_Roads,Group,,,,,South_Roads,,,,2,2026-08-31",
                "l8,g8,08_01_Binder,Layer,08_01,8,1,,Binder,,,Yes,1,2026-08-31");
        }

        private static string Outline(IEnumerable<PlannerScheduleRow> rows)
        {
            return string.Join(" | ", rows.Select(r => new string('.', r.Depth) + r.Name + " " + r.Kind).ToArray());
        }

        [Fact]
        public void WithNoListEachRootHasARowWithItsUnitsUnderIt()
        {
            var scene = TwoRoots();
            var far = scene.Unit("09_01_Elsewhere", null, null);
            scene.Object("IN_Far", far, "09_01_Elsewhere");
            var model = new PlannerSceneModel(scene.Snapshot);
            var tree = PlannerScheduleRows.Tree(null, null, model, null, null, PlannerNamingKind.New);
            // Roots in name order, each at the top; the units nested as their Max layers nest.
            Assert.Equal("Work_Phasing_North Root | ._01_North_Enabling Group | ..01_01_Drainage Layer | ._02_North_Roads Group | ..02_01_Binder Layer"
                + " | Work_Phasing_South Root | ._07_South_Enabling Group | ..07_01_Hoarding Layer | ._08_South_Roads Group | ..08_01_Binder Layer", Outline(tree));
            var south = tree.Single(r => r.Name == "Work_Phasing_South");
            Assert.Equal(PlannerScheduleRowSource.Scene, south.Source);
            Assert.Equal("Work_Phasing_South", south.LayerName);
            Assert.Same(model.PhasingRoots[1], south.Root);
            Assert.Null(south.Unit);
            // A root is a group to the tab: never TBC, and not an editable layer; its layers are TBC as every layer is with no list.
            Assert.True(south.IsGroup);
            Assert.Equal(PlannerRowTbc.None, south.TbcMark);
            Assert.False(south.Editable);
            Assert.Equal(PlannerRowTbc.NoList, tree.Single(r => r.Name == "08_01_Binder").TbcMark);

            // Filled: South's layers are the tree's, so only the layer outside every root gets a flat row of its own.
            var schedule = FillWithNoList(scene);
            tree = PlannerScheduleRows.Tree(null, null, model, null, schedule, PlannerNamingKind.New);
            Assert.False(tree.Single(r => r.Name == "Work_Phasing_South").Tbc);
            Assert.Equal(1, tree.Single(r => r.Name == "08_01_Binder").InCount);
            var outside = Assert.Single(PlannerScheduleRows.Extra(tree, schedule));
            Assert.Equal("09_01_Elsewhere", outside.Name);
            Assert.Equal(schedule.Activities.Count, tree.Concat(new[] { outside }).Sum(r => r.In.Count + r.Rm.Count));

            // One root: the tree as it always was, its groups at the top and no Root row.
            var one = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(Scene().Snapshot), null, null, PlannerNamingKind.New);
            Assert.Equal("_01_Zone Group | .01_01_Strip_Out Layer | .01_02_Kerbs Layer | _02_Zone Group | .02_01_Reinstate Layer", Outline(one));
        }

        [Fact]
        public void TheListsRootRowsStandForTheirRootsAndTheGanttDrawsEachAsAGroup()
        {
            var scene = TwoRoots();
            var list = TwoRootsList();
            Assert.Equal(2, list.RootRows);
            var schedule = Fill(list, scene);
            Assert.True(schedule.Usable, schedule.Message);
            List<PlannerScheduleRow> rows;
            var gantt = Gantt(list, scene, schedule, out rows);
            Assert.Equal("Work_Phasing_North Root | ._01_North_Enabling Group | ..01_01_Drainage Layer | ._02_North_Roads Group | ..02_01_Binder Layer"
                + " | Work_Phasing_South Root | ._07_South_Enabling Group | ..07_01_Hoarding Layer | ._08_South_Roads Group | ..08_01_Binder Layer",
                Outline(rows.Where(r => r.Source == PlannerScheduleRowSource.List)));
            var model = new PlannerSceneModel(scene.Snapshot);
            foreach (var name in new[] { "Work_Phasing_North", "Work_Phasing_South" })
            {
                int i = rows.FindIndex(r => r.Name == name);
                var row = rows[i];
                // The row stands for the root of its name, by the root's Max layer.
                Assert.Equal(name, row.LayerName);
                Assert.Equal(name, row.Root.Name);
                Assert.Equal(PlannerRowTbc.None, row.TbcMark);
                Assert.False(row.Tbc);
                // A grey span over everything under it, never TBC, and the hover says so.
                var g = gantt.Rows[i];
                Assert.True(g.IsGroup);
                Assert.True(g.HasChildren);
                Assert.False(g.OwnTbc);
                Assert.Equal(PlannerGanttSpanKind.Group, g.SpanKind);
                int end = rows.FindIndex(i + 1, r => r.Depth == 0);
                var under = gantt.Rows.Skip(i + 1).Take((end < 0 ? rows.Count : end) - i - 1).ToList();
                var runs = under.SelectMany(u => u.Install.Concat(u.Removal).Concat(u.SpanKind != PlannerGanttSpanKind.None ? new[] { u.Span } : new PlannerGanttRun[0])).ToList();
                Assert.Equal(runs.Min(r => r.First), g.Span.First);
                Assert.Equal(runs.Max(r => r.Last), g.Span.Last);
                Assert.StartsWith(name + "\r\n", gantt.Describe(i, g.Span.First, CultureInfo.InvariantCulture));
                Assert.Contains("Everything inside it runs", gantt.Describe(i, g.Span.First, CultureInfo.InvariantCulture));
            }
            // North spans its drainage (1 Sep) to its binder, where the hoarding South puts up comes down (11 Sep).
            var north = gantt.Rows[rows.FindIndex(r => r.Name == "Work_Phasing_North")];
            Assert.Equal(new DateTime(2026, 9, 1), gantt.DayAt(north.Span.First));
            Assert.Equal(new DateTime(2026, 9, 11), gantt.DayAt(north.Span.Last));
            Assert.Equal("07_01_Hoarding", Activity(schedule, "IN_Hoarding_RM_02_01", PlannerTaskType.Install).Home.Label);
            Assert.Equal("02_01_Binder", Activity(schedule, "IN_Hoarding_RM_02_01", PlannerTaskType.Dismantle).Home.Label);
            // A root carries no dates of its own in the preview, even when a list edited by hand gives it some (R17).
            var dated = List("f1,,Work_Phasing_North,Root,,,,,Work_Phasing_North,2026-09-01,2026-09-30,,1,2026-08-31");
            var folder = Assert.Single(PlannerScheduleRows.PlannerFolders(dated, PlannerLayerPlanner.Plan(dated, model)));
            Assert.Null(folder.Start);
            Assert.Null(folder.Finish);
        }

        [Fact]
        public void ALayerAddedStraightUnderARootSitsUnderThatRootsRow()
        {
            // The record names the root's Max layer as its parent: under South's row, among its rows in name order.
            var scene = TwoRoots();
            scene.Unit("07_02_Fence", "Work_Phasing_South", scene.Find("Work_Phasing_South"));
            var added = new List<PlannerAddedLayer> { new PlannerAddedLayer { Layer = "07_02_Fence", ParentLayer = "Work_Phasing_South", Kind = "Layer", Id = "07_02", Name = "Fence" } };
            var model = new PlannerSceneModel(scene.Snapshot);
            Action<List<PlannerScheduleRow>, int> underSouth = (rows, depth) =>
            {
                int i = rows.FindIndex(r => r.Name == "07_02_Fence");
                Assert.Equal(PlannerScheduleRowSource.Added, rows[i].Source);
                Assert.Equal(depth, rows[i].Depth);
                Assert.Equal("Work_Phasing_South", rows.Take(i).Last(r => r.Depth == depth - 1).Name);
                Assert.Equal(PlannerRowTbc.NotInList, rows[i].TbcMark);
            };
            underSouth(PlannerScheduleRows.Tree(null, null, model, added, null, PlannerNamingKind.New), 1);
            // With the list's Root rows: under South's Root row.
            var list = TwoRootsList();
            underSouth(PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, model), model, added, null, PlannerNamingKind.New), 1);
            // A list with no Root rows has no row for the root: among the top rows, as a layer under the one root always was.
            var noRoots = List(
                "g1,,_01_North_Enabling,Group,,,,,North_Enabling,,,,1,2026-08-31",
                "g7,,_07_South_Enabling,Group,,,,,South_Enabling,,,,2,2026-08-31");
            var rowsNoRoots = PlannerScheduleRows.Tree(noRoots, PlannerLayerPlanner.Plan(noRoots, model), model, added, null, PlannerNamingKind.New);
            Assert.Equal(0, rowsNoRoots.Single(r => r.Name == "07_02_Fence").Depth);
            var top = rowsNoRoots.Where(r => r.Depth == 0).Select(r => r.Name).ToList();
            Assert.Equal(3, top.Count);
            Assert.Equal(top.OrderBy(n => n, PlannerNaturalOrder.Comparer), top);

            // One root: a layer added under it, its parent named or (an older record) empty, among the top rows as before.
            var one = Scene();
            one.Unit("03_01_Added", "Work_Phasing", one.Find("Work_Phasing"));
            var oneModel = new PlannerSceneModel(one.Snapshot);
            foreach (var parent in new[] { "Work_Phasing", string.Empty })
            {
                var record = new List<PlannerAddedLayer> { new PlannerAddedLayer { Layer = "03_01_Added", ParentLayer = parent, Kind = "Layer", Id = "03_01", Name = "Added" } };
                var rows = PlannerScheduleRows.Tree(null, null, oneModel, record, null, PlannerNamingKind.New);
                Assert.Equal("_01_Zone Group | .01_01_Strip_Out Layer | .01_02_Kerbs Layer | _02_Zone Group | .02_01_Reinstate Layer | 03_01_Added Layer", Outline(rows));
            }
        }

        [Fact]
        public void NotScheduledCoversEveryRootAndNoRootIsAnObject()
        {
            var scene = TwoRoots();
            // On South's layer but linked under nothing, and untagged geometry under South's binder.
            scene.Object("IN_Loose_Kerb", null, "08_01_Binder");
            scene.Object("Plain_South", scene.Find("08_01_Binder"), "08_01_Binder");
            // A third root whose name reads as an IN tag: a root all the same (a helper at the top, "phasing" in its name).
            scene.Unit("IN_Phasing_Spare", null, null);
            var schedule = FillWithNoList(scene);
            Assert.True(schedule.Usable, schedule.Message);
            Func<string, string> reason = name => schedule.NotScheduled.Single(n => n.Name == name).Reason;
            Assert.Equal(PlannerSchedule.NoLayerReasonV2 + " (it sits on the layer 08_01_Binder: Link objects to their helpers puts it under the layer's helper)", reason("IN_Loose_Kerb"));
            Assert.StartsWith(PlannerSchedule.NoTagReasonV2, reason("Plain_South"));
            // The roots are never objects (R19): not untagged, not unfiled, not listed, in the preview nor in the export check's reading.
            var roots = new[] { "IN_Phasing_Spare", "Work_Phasing_North", "Work_Phasing_South" };
            Assert.Equal(roots, new PlannerSceneModel(scene.Snapshot).PhasingRoots.Select(r => r.Name).ToArray());
            Assert.Empty(schedule.V2.Skipped.Intersect(roots));
            Assert.Equal(new[] { "IN_Loose_Kerb" }, schedule.V2.Unfiled);
            Assert.DoesNotContain(schedule.NotScheduled, n => roots.Contains(n.Name));
            var read = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(scene.Snapshot), null);
            Assert.Empty(read.Skipped.Intersect(roots));
            Assert.DoesNotContain(read.Objects, o => roots.Contains(o.Name));
            Assert.Equal(new[] { "IN_Loose_Kerb" }, read.Unfiled);
            // Nor is the one root of a one-root scene.
            Assert.DoesNotContain("Work_Phasing", FillWithNoList(Scene()).V2.Skipped);
        }

        [Fact]
        public void ALayerRecordedWithNoParentSitsUnderTheRowOfTheRootItIsIn()
        {
            // A backup root beside Work_Phasing, and 03_01_Kerb added straight under Work_Phasing by a tool whose records leave
            // the parent empty: under Work_Phasing's row, the root its own layer and helper are in, never above both roots.
            var scene = Scene();
            var copy = scene.Unit("Old_Phasing_copy", null, null);
            scene.Unit("_05_Old_Zone", "Old_Phasing_copy", copy);
            scene.Unit("03_01_Kerb", "Work_Phasing", scene.Find("Work_Phasing"));
            var model = new PlannerSceneModel(scene.Snapshot);
            var added = new List<PlannerAddedLayer> { new PlannerAddedLayer { Layer = "03_01_Kerb", ParentLayer = string.Empty, Kind = "Layer", Id = "03_01", Name = "Kerb" } };
            var rows = PlannerScheduleRows.Tree(null, null, model, added, null, PlannerNamingKind.New);
            Assert.Equal("Old_Phasing_copy Root | ._05_Old_Zone Group | Work_Phasing Root | ._01_Zone Group | ..01_01_Strip_Out Layer | ..01_02_Kerbs Layer"
                + " | ._02_Zone Group | ..02_01_Reinstate Layer | .03_01_Kerb Layer", Outline(rows));
            // The Gantt's row for it has the root's row as its parent.
            var gantt = PlannerGanttModel.Build(rows, null, new DateTime(2026, 10, 15));
            Assert.Equal(rows.FindIndex(r => r.Name == "Work_Phasing"), gantt.Rows[rows.FindIndex(r => r.Name == "03_01_Kerb")].Parent);
            // With the list's Root rows: under Work_Phasing's Root row.
            var list = List(
                "f0,,Old_Phasing_copy,Root,,,,,Old_Phasing_copy,,,,1,2026-08-31",
                "g5,f0,_05_Old_Zone,Group,,,,,Old_Zone,,,,1,2026-08-31",
                "f1,,Work_Phasing,Root,,,,,Work_Phasing,,,,2,2026-08-31",
                "g1,f1,_01_Zone,Group,,,,,Zone,,,,1,2026-08-31");
            rows = PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, model), model, added, null, PlannerNamingKind.New);
            Assert.Equal("Old_Phasing_copy Root | ._05_Old_Zone Group | Work_Phasing Root | ._01_Zone Group | .03_01_Kerb Layer", Outline(rows));
        }

        [Fact]
        public void ARootRowRenamedInThePlannerFindsItsRootByThePlannerIdOfTheRootsHelper()
        {
            // South renamed Work_Phasing_Southside in the Planner (R5), its helper given f8 by an earlier list: the row stands
            // for South by that ID alone, by South's Max layer, so a layer added straight under South sits under it.
            var scene = TwoRoots();
            scene.Find("Work_Phasing_South").PlannerId = "f8";
            scene.Unit("07_02_Fence", "Work_Phasing_South", scene.Find("Work_Phasing_South"));
            var model = new PlannerSceneModel(scene.Snapshot);
            var list = List(
                "f1,,Work_Phasing_North,Root,,,,,Work_Phasing_North,,,,1,2026-08-31",
                "g1,f1,_01_North_Enabling,Group,,,,,North_Enabling,,,,1,2026-08-31",
                "f8,,Work_Phasing_Southside,Root,,,,,Work_Phasing_Southside,,,,2,2026-08-31",
                "g7,f8,_07_South_Enabling,Group,,,,,South_Enabling,,,,1,2026-08-31");
            var plan = PlannerLayerPlanner.Plan(list, model);
            // The record naming South's Max layer, and an older one with no parent.
            foreach (var parent in new[] { "Work_Phasing_South", string.Empty })
            {
                var added = new List<PlannerAddedLayer> { new PlannerAddedLayer { Layer = "07_02_Fence", ParentLayer = parent, Kind = "Layer", Id = "07_02", Name = "Fence" } };
                var rows = PlannerScheduleRows.Tree(list, plan, model, added, null, PlannerNamingKind.New);
                var southside = rows.Single(r => r.Name == "Work_Phasing_Southside");
                Assert.Same(model.PhasingRoots.Single(r => r.Name == "Work_Phasing_South"), southside.Root);
                Assert.Equal("Work_Phasing_South", southside.LayerName);
                Assert.Equal("Work_Phasing_North Root | ._01_North_Enabling Group | Work_Phasing_Southside Root | ._07_South_Enabling Group | .07_02_Fence Layer",
                    Outline(rows));
            }
        }

        [Fact]
        public void AOneRootListsTopRowNamedLikeTheRootStandsForTheRoot()
        {
            // A list from a Planner without Root rows whose top folder is named like the one root: the row stands for the root,
            // as Clean up reads it, by the root's Max layer, and a layer added straight under the root goes under that row.
            var scene = Scene();
            scene.Unit("03_01_Added", "Work_Phasing", scene.Find("Work_Phasing"));
            var model = new PlannerSceneModel(scene.Snapshot);
            var list = List(
                "r0,,Work_Phasing,Group,,,,,Work_Phasing,,,,1,2026-08-31",
                "g1,r0,_01_Zone,Group,,,,,Zone,,,,1,2026-08-31",
                "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31");
            var plan = PlannerLayerPlanner.Plan(list, model);
            Assert.True(plan.Rows.Single(r => r.Row.PlannerId == "r0").IsRoot);
            Func<string, List<PlannerAddedLayer>> record = parent => new List<PlannerAddedLayer>
            {
                new PlannerAddedLayer { Layer = "03_01_Added", ParentLayer = parent, Kind = "Layer", Id = "03_01", Name = "Added" }
            };
            foreach (var parent in new[] { "Work_Phasing", string.Empty })
            {
                var rows = PlannerScheduleRows.Tree(list, plan, model, record(parent), null, PlannerNamingKind.New);
                Assert.Same(model.PhasingRoots[0], rows[0].Root);
                Assert.Equal("Work_Phasing", rows[0].LayerName);
                Assert.Equal("Work_Phasing Group | ._01_Zone Group | ..01_01_Strip_Out Layer | .03_01_Added Layer", Outline(rows));
            }
            // A one-root list without such a row: among the top rows, as before.
            var plain = SceneList();
            var plainRows = PlannerScheduleRows.Tree(plain, PlannerLayerPlanner.Plan(plain, model), model, record(string.Empty), null, PlannerNamingKind.New);
            Assert.Equal("_01_Zone Group | .01_01_Strip_Out Layer | .01_02_Kerbs Layer | _02_Zone Group | .02_01_Reinstate Layer | .02_02_Planned Layer | 03_01_Added Layer",
                Outline(plainRows));
            Assert.DoesNotContain(plainRows, r => r.Root != null);
        }

        [Fact]
        public void ARootTheListDoesNotNameIsListedAsARootByTheScene()
        {
            // A phasing root no Root row names is listed as not in the list (R12). The scene says it is a root, whatever the
            // item holds: no unit, or a unit of the root's helper and Max layer.
            var scene = TwoRoots();
            scene.Unit("Old_Phasing_copy", null, null);
            var model = new PlannerSceneModel(scene.Snapshot);
            var plan = new PlannerLayerPlan();
            plan.NotInList.Add(new PlannerUnlistedItem { Name = "Old_Phasing_copy", What = "layer and helper", Note = string.Empty });
            plan.NotInList.Add(new PlannerUnlistedItem
            {
                Name = "Work_Phasing_South",
                What = "layer and helper",
                Note = string.Empty,
                Unit = new PlannerSceneUnit { Helper = scene.Find("Work_Phasing_South"), Layer = model.LayerByName["Work_Phasing_South"] }
            });
            // Items with no unit that are no root: a group or a layer by the name, as any other.
            plan.NotInList.Add(new PlannerUnlistedItem { Name = "Spare_Phasing_Notes", What = "layer only (no helper)", Note = string.Empty });
            plan.NotInList.Add(new PlannerUnlistedItem { Name = "09_01_Spare", What = "layer only (no helper)", Note = string.Empty });
            var rows = PlannerScheduleRows.Unlisted(plan, null, null, PlannerNamingKind.New, model);
            Assert.Equal("09_01_Spare Layer | Old_Phasing_copy Root | Spare_Phasing_Notes Group | Work_Phasing_South Root", Outline(rows));
            var copy = rows.Single(r => r.Name == "Old_Phasing_copy");
            Assert.Same(model.PhasingRoots.Single(r => r.Name == "Old_Phasing_copy"), copy.Root);
            Assert.Equal("Old_Phasing_copy", copy.LayerName);
            Assert.True(copy.IsGroup);
            Assert.Equal(PlannerRowTbc.None, copy.TbcMark);
            Assert.Same(model.PhasingRoots.Single(r => r.Name == "Work_Phasing_South"), rows.Single(r => r.Name == "Work_Phasing_South").Root);
            Assert.Null(rows.Single(r => r.Name == "Spare_Phasing_Notes").Root);
            Assert.Equal(PlannerRowTbc.NotInList, rows.Single(r => r.Name == "09_01_Spare").TbcMark);
            // With no scene to compare, nothing reads as a root.
            Assert.DoesNotContain(PlannerScheduleRows.Unlisted(plan, null, null, PlannerNamingKind.New), r => r.Kind == "Root" || r.Root != null);
        }

        [Fact]
        public void DayFirstNeedsAFirstNumberAboveTwelveAndNoSecondOne()
        {
            // 12/08 could be 8 December: nothing reads. 13/08 cannot: day first.
            Assert.Equal(PlannerDateForm.Unreadable, PlannerListDates.FormOf(new[] { "12/08/2026", "05/09/2026" }));
            Assert.Equal(PlannerDateForm.DayFirst, PlannerListDates.FormOf(new[] { "13/08/2026", "05/09/2026" }));
            // A second number of 12 is a month: day first still.
            Assert.Equal(PlannerDateForm.DayFirst, PlannerListDates.FormOf(new[] { "13/12/2026", "05/09/2026" }));
            Assert.Equal(new DateTime(2026, 12, 13), PlannerListDates.Read("13/12/2026", PlannerDateForm.DayFirst));
            // Rows giving two project starts: the first is used, with a note.
            var list = List(
                "l1,,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-30",
                "l2,,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31");
            Assert.Equal(new DateTime(2026, 8, 30), list.ProjectStart);
            Assert.Equal(new[] { "The rows give 2 different project starts; the first, 30 Aug 2026, is used." }, list.DateNotes);
        }
    }
}
