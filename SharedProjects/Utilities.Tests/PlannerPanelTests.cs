using System;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>What the read-only Planner layers panel shows: dates as people read them, rows, objects and notes.</summary>
    public class PlannerPanelTests
    {
        // ---- dates --------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("2026-09-18T00:00", "18/09/2026")]
        [InlineData("2026-09-18T07:30", "18/09/2026 07:30")]
        [InlineData("2026-09-18", "18/09/2026")]
        [InlineData(null, "")]
        [InlineData("soon", "soon")]
        public void StartShowsTheDayAloneAtMidnight(string start, string expected)
        {
            Assert.Equal(expected, PlannerScheduleDates.DisplayStart(start));
        }

        [Theory]
        [InlineData("2026-09-24T00:00", "2026-09-18T00:00", "23/09/2026")]       // exclusive midnight: the day before
        [InlineData("2026-09-24T17:00", "2026-09-18T07:30", "24/09/2026 17:00")] // a time of day: as written
        [InlineData("2026-10-17T00:00", "2026-10-17T00:00", "17/10/2026")]       // milestone: the start day
        [InlineData("2027-01-01T00:00", null, "31/12/2026")]
        [InlineData(null, "2026-09-18T00:00", "")]
        [InlineData("later", null, "later")]
        public void FinishShowsTheInclusiveDayForMidnightAndTheTimeOtherwise(string finish, string start, string expected)
        {
            Assert.Equal(expected, PlannerScheduleDates.DisplayFinish(finish, start));
        }

        [Fact]
        public void LegacyDaysAndExportInstants()
        {
            Assert.Equal("18/09/2026", PlannerScheduleDates.DisplayDay("2026-09-18"));
            Assert.Equal("", PlannerScheduleDates.DisplayDay(null));
            Assert.Equal("02/10/2026 05:30", PlannerScheduleDates.DisplayInstant("2026-10-02T05:30:00.000Z", TimeZoneInfo.Utc));
            var plusTen = TimeZoneInfo.CreateCustomTimeZone("UTC+10", TimeSpan.FromHours(10), "UTC+10", "UTC+10");
            Assert.Equal("02/10/2026 15:30", PlannerScheduleDates.DisplayInstant("2026-10-02T05:30:00.000Z", plusTen));
            Assert.Equal("", PlannerScheduleDates.DisplayInstant(null, plusTen));
            Assert.Equal("yesterday", PlannerScheduleDates.DisplayInstant("yesterday", plusTen));
        }

        // ---- rows -------------------------------------------------------------------------------------------------

        private static PlannerPanelRow Row(PlannerPanelModel model, string layerName)
        {
            return model.Rows.Single(r => r.LayerName == layerName);
        }

        [Fact]
        public void AdoptedSceneShowsTheTreeWithTheOldLayerDates()
        {
            var adopted = PlannerLayerPlans.AdoptPlan(PlannerLayerPlansTests.BuildFixture(), null).Result;
            var model = PlannerPanelModel.Build(adopted, null);

            Assert.Equal("Work_Phasing", model.RootLayerName);
            Assert.Equal(21, model.Rows.Count);
            Assert.True(model.Rows[0].IsRoot);
            Assert.Equal(0, model.Rows[0].Depth);
            Assert.Equal("06-06_Full_Closure_of_Main_Road", model.Rows[1].LayerName);
            Assert.Equal(1, model.Rows[1].Depth);

            var retainment = Row(model, "01-04.1_Phase1_Retainment_Installation");
            Assert.Equal(2, retainment.Depth);
            Assert.Equal("01-04.1", retainment.Code);
            Assert.Equal("Phase1_Retainment_Installation", retainment.Name);
            Assert.Equal("18/09/2026", retainment.Start);
            Assert.Equal("23/10/2026", retainment.Finish); // legacy dates are inclusive already
            Assert.False(retainment.Tbc);
            Assert.Equal("Old layer name", retainment.DatesFrom);
            Assert.Equal(3, retainment.ObjectCount);
            Assert.Equal("", retainment.Note);

            Assert.True(Row(model, "01-01_Drainage").Tbc);
            var zone = Row(model, "01_Zone5_Working");
            Assert.Equal("", zone.DatesFrom);
            Assert.Equal(0, zone.ObjectCount);
        }

        [Fact]
        public void ObjectsShowTheirTagsLinksAndNotes()
        {
            var adopted = PlannerLayerPlans.AdoptPlan(PlannerLayerPlansTests.BuildFixture(), null).Result;
            var model = PlannerPanelModel.Build(adopted, null);

            var piles = model.ObjectsOn(new[] { "01-04.1_Phase1_Retainment_Installation" });
            Assert.Equal(new[] { "Ph1_St04_IN_Sheet_Pile_1", "Ph1_St04_IN_Sheet_Pile_2", "Ph1_St04_IN_Sheet_Pile_3" }, piles.Select(o => o.Name).ToArray());
            Assert.All(piles, o => Assert.True(o.FiledUnderOwnLayer));
            Assert.All(piles, o => Assert.Equal("01-04.1_Phase1_Retainment_Installation", o.LinkedTo));
            Assert.Equal("01-04", piles[0].TagCode);

            var mismatch = Assert.Single(model.ObjectsOn(new[] { "01-04.0_Zone5_Working_Phase1_Retainment_Installation" }));
            Assert.Contains("its tag reads 01-01 but the layer is 01-04.0", mismatch.Note);

            var bollards = model.ObjectsOn(new[] { "02-02_TM_Removal" });
            var cap = bollards.Single(o => o.Name == "Bollard_Cap");
            Assert.True(cap.FiledUnderOwnLayer); // under an object that is under the helper
            Assert.Equal("Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM", cap.LinkedTo);
            Assert.Equal("04-02.2", bollards.Single(o => o.Name == "Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM").RemovalCode);

            var outside = model.ObjectsOutside();
            Assert.Equal(new[] { "EUS_Con_HRB_Bridge_Slab", "St1_Kerb_003" }, outside.Select(o => o.Name).ToArray());
        }

        [Fact]
        public void ScheduleDatesReplaceTheOldOnesOnceLinked()
        {
            var adoptedPlan = PlannerLayerPlans.AdoptPlan(PlannerLayerPlansTests.BuildFixture(), null);
            var schedule = PlannerLayerPlansTests.ScheduleFrom(adoptedPlan);
            schedule.ProjectName = "EUS_HmRd_HRB";
            schedule.WorkspaceName = "HS2 Euston";
            schedule.ExportedAt = "2026-10-02T05:30:00.000Z";
            var retainmentFolder = schedule.Folders.Single(f => f.LayerName == "01-04.1_Phase1_Retainment_Installation");
            retainmentFolder.Start = "2026-09-18T00:00";
            retainmentFolder.Finish = "2026-10-24T00:00";
            schedule.Activities.Add(new PlannerScheduleActivity
            {
                Id = "a1", FolderId = retainmentFolder.Id, Name = "Sheet_Pile_1_Ph1_St04_IN", Type = PlannerTaskType.Install,
                Start = "2026-09-18T00:00", Finish = "2026-09-24T00:00", Objects = { "Ph1_St04_IN_Sheet_Pile_1" }
            });
            schedule.Folders.Remove(schedule.Folders.Single(f => f.LayerName == "06-06_Full_Closure_of_Main_Road"));

            // Before Update layers: matched by layer name, and the panel says to run it.
            var before = PlannerPanelModel.Build(adoptedPlan.Result, schedule);
            var unlinked = Row(before, "01-04.1_Phase1_Retainment_Installation");
            Assert.Equal("Planner", unlinked.DatesFrom);
            Assert.Equal("23/10/2026", unlinked.Finish);
            Assert.Contains("run Update layers", unlinked.Note);
            Assert.Contains("not in the Planner schedule", Row(before, "06-06_Full_Closure_of_Main_Road").Note);

            var updated = PlannerLayerPlans.ScheduleUpdatePlan(adoptedPlan.Result, schedule, null).Result;
            var model = PlannerPanelModel.Build(updated, schedule);
            var row = Row(model, "01-04.1_Phase1_Retainment_Installation");
            Assert.Equal(retainmentFolder.Id, row.FolderId);
            Assert.Equal("Planner", row.DatesFrom);
            Assert.Equal("18/09/2026", row.Start);
            Assert.Equal("23/10/2026", row.Finish);
            Assert.Equal("", row.Note);
            Assert.Equal("", Row(model, "01_Zone5_Working").Start); // a folder without activities has no span

            var pile = model.ObjectsOn(new[] { row.LayerName }).Single(o => o.Name == "Ph1_St04_IN_Sheet_Pile_1");
            var activity = Assert.Single(pile.Activities);
            Assert.Equal("18/09/2026", activity.Start);
            Assert.Equal("23/09/2026", activity.Finish);
            Assert.Equal("01-04.1_Phase1_Retainment_Installation", activity.Folder);
            var other = model.ObjectsOn(new[] { row.LayerName }).Single(o => o.Name == "Ph1_St04_IN_Sheet_Pile_2");
            Assert.Contains("not bound to a Planner activity yet", other.Note);

            Assert.Equal(new[] { "Project: EUS_HmRd_HRB (HS2 Euston)", "Schedule exported: 02/10/2026 05:30   Folders: 19   Activities: 1", "Time zone: not set" },
                PlannerPanelModel.HeaderLines(schedule, TimeZoneInfo.Utc));
            Assert.Contains("No Planner schedule loaded", PlannerPanelModel.HeaderLines(null, TimeZoneInfo.Utc)[0]);
        }

        [Fact]
        public void ObjectsNotUnderTheirHelperAreFlagged()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone", ParentName = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01-01_Kerbs", ParentName = "01_Zone" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "02_No_Helper", ParentName = "Work_Phasing" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "r", Name = "Work_Phasing", LayerName = "Work_Phasing", IsHelper = true, Props = { { PlannerProps.Root, "true" } } });
            scene.Nodes.Add(new PlannerSceneNode { Id = "z", Name = "01_Zone", LayerName = "01_Zone", ParentId = "r", IsHelper = true });
            scene.Nodes.Add(new PlannerSceneNode { Id = "k", Name = "01-01_Kerbs", LayerName = "01-01_Kerbs", ParentId = "z", IsHelper = true });
            scene.Nodes.Add(new PlannerSceneNode { Id = "1", Name = "IN_Kerb_1", LayerName = "01-01_Kerbs" });               // loose
            scene.Nodes.Add(new PlannerSceneNode { Id = "2", Name = "IN_Hoarding", LayerName = "01_Zone", ParentId = "k" }); // under a sub-folder's helper
            scene.Nodes.Add(new PlannerSceneNode { Id = "3", Name = "IN_Barrier", LayerName = "02_No_Helper" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "4", Name = "Ph2_St01_IN_Gate", LayerName = "0" });

            var model = PlannerPanelModel.Build(scene, null);
            Assert.Equal("no helper yet", Row(model, "02_No_Helper").Note);
            var loose = model.ObjectsOn(new[] { "01-01_Kerbs" }).Single();
            Assert.False(loose.FiledUnderOwnLayer);
            Assert.Contains("files it nowhere", loose.Note);
            var misfiled = model.ObjectsOn(new[] { "01_Zone" }).Single();
            Assert.False(misfiled.FiledUnderOwnLayer);
            Assert.Contains("files it under '01-01_Kerbs'", misfiled.Note);
            Assert.Contains("its layer has no helper yet", model.ObjectsOn(new[] { "02_No_Helper" }).Single().Note);
            var gate = model.ObjectsOutside().Single();
            Assert.True(gate.Tagged);
            Assert.Contains("outside the Planner layers", gate.Note);
        }

        [Fact]
        public void FindNodeFollowsEditsToTheNodeList()
        {
            var scene = new PlannerScene();
            var a = new PlannerSceneNode { Id = "1", Name = "a" };
            scene.Nodes.Add(a);
            scene.Nodes.Add(new PlannerSceneNode { Id = "1", Name = "a twin" });
            Assert.Same(a, scene.FindNode("1"));           // the first node with an id wins
            Assert.Null(scene.FindNode("2"));
            a.Id = "2";                                    // an id changed in place
            Assert.Same(a, scene.FindNode("2"));
            Assert.Equal("a twin", scene.FindNode("1").Name);
            var b = new PlannerSceneNode { Id = "3", Name = "b" };
            scene.Nodes.Add(b);                            // a node added
            Assert.Same(b, scene.FindNode("3"));
            scene.Nodes.Remove(b);                         // and removed
            Assert.Null(scene.FindNode("3"));
            Assert.Null(scene.FindNode(null));
        }

        [Fact]
        public void SceneWithoutARootHasNoRows()
        {
            var model = PlannerPanelModel.Build(PlannerLayerPlansTests.BuildFixture(), null);
            Assert.Null(model.RootLayerName);
            Assert.Empty(model.Rows);
            Assert.Empty(model.ObjectsOn(new[] { "0" }));
            Assert.Equal(25, model.ObjectsOutside().Count);
        }
    }
}
