using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The adopt and schedule-update planners on a fixture taken from a real v3 construction model (the layer and
    /// helper hierarchy of an HS2 road-construction scene, with its naming slips left in): an old phasing root
    /// holding dated group layers, one dated layer outside any group, a group at the top level, duplicate
    /// numbers, single-digit and dotted group numbers, objects whose tags disagree with their layer.
    /// </summary>
    public class PlannerLayerPlansTests
    {
        private const string OldRoot = "HS2_EUS_HRB_Phasing_RoadConstraction_Main_Section_v.02";

        // layer | parent layer ("" = top level) | has a same-named helper
        private static readonly string[][] Layers =
        {
            new[] { "0", "", "no" },
            new[] { OldRoot, "", "yes" },
            new[] { "06_06_Full_Closure_of_Main_Road_TBC_07-02-27", OldRoot, "yes" },
            new[] { "_01_Zone5_Working", OldRoot, "yes" },
            new[] { "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26", "_01_Zone5_Working", "yes" },
            new[] { "01_04.0_Zone5_Working_Phase1_Retainment_Installation_17-08-26_18-10-26", "_01_Zone5_Working", "yes" },
            new[] { "01_01_Drainage_TBC_08-06-26_03-07-26", "_01_Zone5_Working", "yes" },
            new[] { "01_00_START_DATE_01-06-26", "_01_Zone5_Working", "no" },
            new[] { "_02_Phase1_TM", OldRoot, "yes" },
            new[] { "02_01.0_Construction_of_Phase1_TM_31-08-26", "_02_Phase1_TM", "yes" },
            new[] { "02_01.5_Construction_of_Phase1_TM_Stage_2B_28-09-26_17-10-26", "_02_Phase1_TM", "yes" },
            new[] { "02_01.5_Construction_of_Phase1_TM_Stage_2B_30-09-26_05-02-27", "_02_Phase1_TM", "yes" },
            new[] { "02_02.1_Integration_of_Phase1_TM_!!!FULL_CLOSURE!!!_17-10-26", "_02_Phase1_TM", "yes" },
            new[] { "02_02_TM_Removal_TBC_17-10-26_25-01-27", "_02_Phase1_TM", "yes" },
            new[] { "_04_Shuttle_Lane_HRB", OldRoot, "yes" },
            new[] { "04_02_Road_Planing_23-01-27_25-01-27", "_04_Shuttle_Lane_HRB", "yes" },
            new[] { "4_02.1_Binder_Cource_23-01-27", "_04_Shuttle_Lane_HRB", "yes" },
            new[] { "4_02.2_Binder_Cource_25-01-27", "_04_Shuttle_Lane_HRB", "yes" },
            new[] { "_06_Weekend_Closure", "", "yes" },
            new[] { "06.01.2_Traffic_Islands_Install_05-02-27", "_06_Weekend_Closure", "yes" },
            new[] { "06_05_Site Entrance-Hoarding_Install_05-02-27", "_06_Weekend_Closure", "yes" },
            new[] { "06_05.2_Weekend_closure_TBC_04-02-27_07-02-27", "_06_Weekend_Closure", "yes" },
        };

        // object | layer | parent ("helper" = the layer's helper, "" = none, otherwise an object name)
        private static readonly string[][] Objects =
        {
            new[] { "St1_Kerb_003", "0", "" },
            new[] { "EUS_Con_HRB_Bridge_Slab", "0", "" },
            new[] { "Ph6_St06_RM_RC_Road_Barriers", "06_06_Full_Closure_of_Main_Road_TBC_07-02-27", "helper" },
            new[] { "Ph6_St06_RM_EUS_Con_HRB_GTB_Fence_UCF", "06_06_Full_Closure_of_Main_Road_TBC_07-02-27", "helper" },
            new[] { "Ph1_St04_IN_Sheet_Pile_1", "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26", "helper" },
            new[] { "Ph1_St04_IN_Sheet_Pile_2", "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26", "helper" },
            new[] { "Ph1_St04_IN_Sheet_Pile_3", "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26", "" },
            new[] { "Ph1_St01_IN_Sheet_Pile_1", "01_04.0_Zone5_Working_Phase1_Retainment_Installation_17-08-26_18-10-26", "helper" },
            new[] { "Ph1_St01_IN_TR_Manhole_MH101", "01_01_Drainage_TBC_08-06-26_03-07-26", "helper" },
            new[] { "Ph1_St01_IN_TR_Manhole_MH201", "01_01_Drainage_TBC_08-06-26_03-07-26", "helper" },
            new[] { "Ph2_St01.3_RM_RC_Road_Barriers2", "02_01.0_Construction_of_Phase1_TM_31-08-26", "helper" },
            new[] { "Ph2_St01.1_RM_RM_Marwood_Barrier", "02_01.0_Construction_of_Phase1_TM_31-08-26", "helper" },
            new[] { "Ph2_St01.5_IN_Marwood_Barrier4_Ph2_St02.1_RM", "02_01.5_Construction_of_Phase1_TM_Stage_2B_28-09-26_17-10-26", "helper" },
            new[] { "Ph2_St01.5_IN_RC_Hrd_from_PDF_04_Ph6_St05_RM", "02_01.5_Construction_of_Phase1_TM_Stage_2B_30-09-26_05-02-27", "helper" },
            new[] { "Ph2_St02.1_RM_Cycle_Bollards_001", "02_02.1_Integration_of_Phase1_TM_!!!FULL_CLOSURE!!!_17-10-26", "helper" },
            new[] { "Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM", "02_02_TM_Removal_TBC_17-10-26_25-01-27", "helper" },
            new[] { "Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM (1)", "02_02_TM_Removal_TBC_17-10-26_25-01-27", "helper" },
            new[] { "Ph2_St02_IN_LM_based on 1MC03-SCJ_SDH-EN-DPL-SS01_SL12-011003_PDF_Ph1_St26_RM", "02_02_TM_Removal_TBC_17-10-26_25-01-27", "helper" },
            new[] { "Bollard_Cap", "02_02_TM_Removal_TBC_17-10-26_25-01-27", "Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM" },
            new[] { "Ph4_St02_RM_Road_Planing_North", "04_02_Road_Planing_23-01-27_25-01-27", "helper" },
            new[] { "Ph4_St02.1_IN_TR_Binder_Course_60mm_C_01", "4_02.1_Binder_Cource_23-01-27", "helper" },
            new[] { "Ph4_St02.2_IN_TR_Binder_Course_60mm_C_02", "4_02.2_Binder_Cource_25-01-27", "helper" },
            new[] { "Ph6_St01.2_IN_Traffic_Islands_Install_Urban64_Kerbs", "06.01.2_Traffic_Islands_Install_05-02-27", "helper" },
            new[] { "Ph6_St05_IN_RC_Hrd_Main_Road_Gates (1)", "06_05_Site Entrance-Hoarding_Install_05-02-27", "helper" },
            new[] { "Ph6_St05_IN_RC_PlasticRoadBarrier_Full_Closure_Ph99_St99_RM", "06_05.2_Weekend_closure_TBC_04-02-27_07-02-27", "helper" },
        };

        /// <summary>The v3 fixture; also used by the panel, naming check and MAXScript tests.</summary>
        internal static PlannerScene BuildFixture()
        {
            var scene = new PlannerScene();
            var helperOfLayer = new Dictionary<string, string>();
            int id = 0;
            foreach (var row in Layers)
            {
                scene.Layers.Add(new PlannerSceneLayer { Name = row[0], ParentName = row[1].Length == 0 ? null : row[1] });
            }
            // Helpers are linked like their layers, as in the model's node hierarchy.
            foreach (var row in Layers)
            {
                if (row[2] != "yes")
                {
                    continue;
                }
                var helperId = "h" + (++id);
                helperOfLayer[row[0]] = helperId;
                string parentId = null;
                if (row[1].Length > 0)
                {
                    helperOfLayer.TryGetValue(row[1], out parentId);
                }
                scene.Nodes.Add(new PlannerSceneNode { Id = helperId, Name = row[0], LayerName = row[0], ParentId = parentId, IsHelper = true });
            }
            var objectIds = new Dictionary<string, string>();
            foreach (var row in Objects)
            {
                var objectId = "o" + (++id);
                objectIds[row[0]] = objectId;
                string parentId = null;
                if (row[2] == "helper")
                {
                    parentId = helperOfLayer[row[1]];
                }
                else if (row[2].Length > 0)
                {
                    parentId = objectIds[row[2]];
                }
                scene.Nodes.Add(new PlannerSceneNode { Id = objectId, Name = row[0], LayerName = row[1], ParentId = parentId });
            }
            return scene;
        }

        private static PlannerSceneNode Helper(PlannerScene scene, string layer)
        {
            var helpers = scene.NodesOn(layer).Where(n => n.IsHelper && n.Name == layer).ToList();
            Assert.Single(helpers);
            return helpers[0];
        }

        private static PlannerSceneNode Node(PlannerScene scene, string name)
        {
            return scene.Nodes.Single(n => n.Name == name);
        }

        private static string Prop(PlannerSceneNode node, string name)
        {
            string value;
            return node.Props.TryGetValue(name, out value) ? value : null;
        }

        private static bool Has(PlannerScenePlan plan, PlannerReviewSeverity severity, string text)
        {
            return plan.Review.Any(r => r.Severity == severity && r.Message.Contains(text));
        }

        // ---- adopt ----------------------------------------------------------------------------------------------

        [Fact]
        public void Adopt_BuildsTheCodedHierarchyUnderANewRoot()
        {
            var scene = BuildFixture();
            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            var result = plan.Result;

            Assert.Equal("Work_Phasing", plan.RootLayerName);
            Assert.Equal(PlannerSceneOpKind.CreateLayer, plan.Operations[0].Kind);
            Assert.Null(result.FindLayer("Work_Phasing").ParentName);

            // Old root stays, empty; its groups, its loose dated layer and the top-level group moved under the root.
            Assert.NotNull(result.FindLayer(OldRoot));
            Assert.Empty(result.ChildLayers(OldRoot));
            Assert.Equal(new[] { "06-06_Full_Closure_of_Main_Road", "01_Zone5_Working", "02_Phase1_TM", "04_Shuttle_Lane_HRB", "06_Weekend_Closure" },
                result.ChildLayers("Work_Phasing").Select(l => l.Name).ToArray());
            Assert.Equal(new[]
                {
                    "01-04.1_Phase1_Retainment_Installation",
                    "01-04.0_Zone5_Working_Phase1_Retainment_Installation",
                    "01-01_Drainage",
                    "01-00_START_DATE"
                },
                result.ChildLayers("01_Zone5_Working").Select(l => l.Name).ToArray());
            Assert.Equal(new[]
                {
                    "02-01.0_Construction_of_Phase1_TM",
                    "02-01.5_Construction_of_Phase1_TM_Stage_2B",
                    "02-01.5_Construction_of_Phase1_TM_Stage_2B (1)",
                    "02-02.1_Integration_of_Phase1_TM_!!!FULL_CLOSURE!!!",
                    "02-02_TM_Removal"
                },
                result.ChildLayers("02_Phase1_TM").Select(l => l.Name).ToArray());
            Assert.Equal(new[] { "04-02_Road_Planing", "04-02.1_Binder_Cource", "04-02.2_Binder_Cource" },
                result.ChildLayers("04_Shuttle_Lane_HRB").Select(l => l.Name).ToArray());
            Assert.Equal(new[] { "06-01.2_Traffic_Islands_Install", "06-05_Site_Entrance-Hoarding_Install", "06-05.2_Weekend_closure" },
                result.ChildLayers("06_Weekend_Closure").Select(l => l.Name).ToArray());

            // Context is untouched.
            Assert.Null(result.FindLayer("0").ParentName);
            Assert.DoesNotContain(plan.Operations, op => op.NodeName == "St1_Kerb_003" || op.NodeName == "EUS_Con_HRB_Bridge_Slab" || op.LayerName == "0");

            // Folder list in outline order, with depth.
            Assert.Equal(20, plan.Folders.Count);
            Assert.Equal("06-06", plan.Folders[0].Code);
            Assert.Equal(0, plan.Folders[0].Depth);
            Assert.Equal("01-04.1", plan.Folders[2].Code);
            Assert.Equal(1, plan.Folders[2].Depth);
        }

        [Fact]
        public void Adopt_ReusesRenamesLinksAndRecordsTheHelpers()
        {
            var scene = BuildFixture();
            var original = Helper(scene, "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26");
            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            var result = plan.Result;

            var root = Helper(result, "Work_Phasing");
            Assert.StartsWith("new:", root.Id);
            Assert.Equal(PlannerProps.True, Prop(root, PlannerProps.Root));
            Assert.Null(root.ParentId);

            // Existing same-named helpers are reused (same node), renamed with their layer and linked parent to child.
            var zone = Helper(result, "01_Zone5_Working");
            var retainment = Helper(result, "01-04.1_Phase1_Retainment_Installation");
            Assert.Equal(original.Id, retainment.Id);
            Assert.Equal(root.Id, zone.ParentId);
            Assert.Equal(zone.Id, retainment.ParentId);
            Assert.Equal(root.Id, Helper(result, "06_Weekend_Closure").ParentId);          // was at the scene root
            Assert.Equal(root.Id, Helper(result, "06-06_Full_Closure_of_Main_Road").ParentId); // was under the old root's helper

            // A layer without a helper gets a Point helper.
            var start = Helper(result, "01-00_START_DATE");
            Assert.StartsWith("new:", start.Id);
            Assert.Equal(zone.Id, start.ParentId);
            Assert.Contains(plan.Operations, op => op.Kind == PlannerSceneOpKind.CreateHelper && op.NewName == "01-00_START_DATE" && op.LayerName == "01-00_START_DATE");

            // planner_* props: code, name and the dates the old name carried.
            Assert.Equal("01-04.1", Prop(retainment, PlannerProps.Code));
            Assert.Equal("Phase1_Retainment_Installation", Prop(retainment, PlannerProps.Name));
            Assert.Equal("2026-09-18", Prop(retainment, PlannerProps.LegacyStart));
            Assert.Equal("2026-10-23", Prop(retainment, PlannerProps.LegacyFinish));
            Assert.Equal(PlannerProps.False, Prop(retainment, PlannerProps.LegacyTbc));
            Assert.Equal(PlannerProps.True, Prop(Helper(result, "01-01_Drainage"), PlannerProps.LegacyTbc));
            Assert.Equal("01", Prop(zone, PlannerProps.Code));
            Assert.Equal("Zone5_Working", Prop(zone, PlannerProps.Name));
            Assert.Null(Prop(zone, PlannerProps.LegacyStart));
            Assert.Null(Prop(zone, PlannerProps.LegacyTbc));
            Assert.Null(Prop(zone, PlannerProps.FolderId));

            var closure = Helper(result, "06-06_Full_Closure_of_Main_Road");
            Assert.Equal("2027-02-07", Prop(closure, PlannerProps.LegacyStart));
            Assert.Equal("2027-02-07", Prop(closure, PlannerProps.LegacyFinish));
            Assert.Equal(PlannerProps.True, Prop(closure, PlannerProps.LegacyTbc));

            // The loose object is linked to its layer's helper; the sub-part linked to another object is left alone.
            Assert.Equal(retainment.Id, Node(result, "Ph1_St04_IN_Sheet_Pile_3").ParentId);
            Assert.Equal(Node(result, "Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM").Id, Node(result, "Bollard_Cap").ParentId);
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'Bollard_Cap' is linked to 'Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM'"));
        }

        [Fact]
        public void Adopt_ReviewListsWhatNeedsAHumanLook()
        {
            var plan = PlannerLayerPlans.AdoptPlan(BuildFixture(), null);

            Assert.True(Has(plan, PlannerReviewSeverity.Info, "Creates the phasing root layer 'Work_Phasing'"));
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'" + OldRoot + "' is read as the old phasing root"));
            Assert.True(Has(plan, PlannerReviewSeverity.Info, "Moves layer '_06_Weekend_Closure' under 'Work_Phasing'"));
            Assert.True(Has(plan, PlannerReviewSeverity.Info, "Renames layer '01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26' to '01-04.1_Phase1_Retainment_Installation'"));
            Assert.True(Has(plan, PlannerReviewSeverity.Info, "Creates the helper '01-00_START_DATE'"));

            // Duplicate codes and the name clash they cause.
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "Code 02-01.5 is used by 2 layers"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'02-01.5_Construction_of_Phase1_TM_Stage_2B' is already taken"));

            // Normalised numbers.
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'4_02.1_Binder_Cource_23-01-27': group number padded (code 04-02.1)"));
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'06.01.2_Traffic_Islands_Install_05-02-27': dotted group number normalised (code 06-01.2)"));

            // Tags that disagree with the layer (the layer decides), and removals no folder can take.
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'Ph1_St01_IN_Sheet_Pile_1': its tag reads 01-01 but it sits on '01-04.0_Zone5_Working_Phase1_Retainment_Installation' (01-04.0)"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'Ph2_St01.3_RM_RC_Road_Barriers2': its tag reads 02-01.3"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "'Ph1_St04_IN_Sheet_Pile_1': its tag"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "'Ph6_St05_IN_RC_PlasticRoadBarrier_Full_Closure_Ph99_St99_RM': its tag")); // 06-05 covers 06-05.2
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "no folder has the code 99-99"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "no folder has the code 01-26"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "no folder has the code 04-02.2"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "no folder has the code 06-05"));

            Assert.Equal(0, plan.Count(PlannerReviewSeverity.Error));
        }

        [Fact]
        public void Adopt_OperationsReplayToTheSameSceneAndAreIdempotent()
        {
            var scene = BuildFixture();
            var plan = PlannerLayerPlans.AdoptPlan(scene, null);

            var replayed = scene.Apply(plan.Operations);
            AssertSameScene(plan.Result, replayed);

            var again = PlannerLayerPlans.AdoptPlan(replayed, null);
            Assert.Empty(again.Operations);
            Assert.Equal(plan.Folders.Select(f => f.LayerName), again.Folders.Select(f => f.LayerName));
            Assert.Equal(plan.Folders.Select(f => f.Code), again.Folders.Select(f => f.Code));
            Assert.Equal("2026-09-18", PlannerScheduleDates.IsoDay(again.Folders.Single(f => f.Code == "01-04.1").LegacyStart));
        }

        [Fact]
        public void Adopt_ExplicitOldRootAndAnExistingRoot()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "Old_Programme" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "_03_Cardington_St_Works", ParentName = "Old_Programme" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "03_02_North_Installation_of_SubGrade_and_SubBase_02-11-26_05-11-26", ParentName = "_03_Cardington_St_Works" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "Misc", ParentName = "Work Phasing" });
            // A same-named helper on the wrong layer is moved onto its layer instead of being duplicated.
            scene.Nodes.Add(new PlannerSceneNode { Id = "1", Name = "_03_Cardington_St_Works", LayerName = "Old_Programme", IsHelper = true });
            scene.Nodes.Add(new PlannerSceneNode { Id = "2", Name = "Ph3_St02_IN_TR_Subgrade_Cardington", LayerName = "03_02_North_Installation_of_SubGrade_and_SubBase_02-11-26_05-11-26" });

            var plan = PlannerLayerPlans.AdoptPlan(scene, new PlannerAdoptOptions { OldRootNames = new[] { "Old_Programme" } });
            var result = plan.Result;

            Assert.Equal("Work Phasing", plan.RootLayerName);
            Assert.DoesNotContain(plan.Operations, op => op.Kind == PlannerSceneOpKind.CreateLayer);
            Assert.Equal(new[] { "03_Cardington_St_Works", "Misc" }, result.ChildLayers("Work Phasing").Select(l => l.Name).ToArray());
            var group = result.FindNode("1");
            Assert.Equal("03_Cardington_St_Works", group.Name);
            Assert.Equal("03_Cardington_St_Works", group.LayerName);
            Assert.Contains(plan.Operations, op => op.Kind == PlannerSceneOpKind.MoveNodeToLayer && op.NodeId == "1");
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'Misc' does not start with a number"));
            Assert.Equal("Misc", Prop(Helper(result, "Misc"), PlannerProps.Name));
            Assert.Null(Prop(Helper(result, "Misc"), PlannerProps.Code));

            var subgrade = Helper(result, "03-02_North_Installation_of_SubGrade_and_SubBase");
            Assert.Equal(subgrade.Id, result.FindNode("2").ParentId);
            Assert.Equal(group.Id, subgrade.ParentId);
            AssertSameScene(result, scene.Apply(plan.Operations));
        }

        // ---- schedule update -------------------------------------------------------------------------------------

        /// <summary>A Planner schedule built from the adopted layers, then edited the way a planner would.</summary>
        internal static PlannerSchedule ScheduleFrom(PlannerScenePlan adopted)
        {
            var schedule = new PlannerSchedule { ProjectId = "p-1", ProjectName = "EUS_HmRd_HRB" };
            var ids = new Dictionary<string, string>();
            int n = 0;
            foreach (var folder in adopted.Folders)
            {
                var id = "f" + (++n);
                ids[folder.LayerName] = id;
                string parentId;
                ids.TryGetValue(folder.ParentLayerName, out parentId);
                schedule.Folders.Add(new PlannerScheduleFolder { Id = id, ParentId = parentId, Code = folder.Code, Name = folder.Name });
            }
            return schedule;
        }

        private static PlannerScheduleFolder FolderNamed(PlannerSchedule schedule, string layerName)
        {
            return schedule.Folders.Single(f => f.LayerName == layerName);
        }

        [Fact]
        public void Update_MatchesRenamesMovesCreatesAndRecordsFolderIds()
        {
            var adopted = PlannerLayerPlans.AdoptPlan(BuildFixture(), null);
            var scene = adopted.Result;
            var schedule = ScheduleFrom(adopted);

            // The planner renamed one folder, moved another, added one and dropped the full closure.
            var renamed = FolderNamed(schedule, "01-04.1_Phase1_Retainment_Installation");
            renamed.Name = "Phase1 Retainment Install";
            var planing = FolderNamed(schedule, "04-02_Road_Planing");
            planing.ParentId = FolderNamed(schedule, "02_Phase1_TM").Id;
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "f-new", ParentId = FolderNamed(schedule, "02_Phase1_TM").Id, Code = "02-03", Name = "Utility_Crossing" });
            schedule.Folders.Remove(FolderNamed(schedule, "06-06_Full_Closure_of_Main_Road"));
            schedule.Activities.Add(new PlannerScheduleActivity { Id = "a1", FolderId = renamed.Id, Objects = { "Ph1_St04_IN_Sheet_Pile_1" } });
            schedule.Activities.Add(new PlannerScheduleActivity { Id = "a2", FolderId = FolderNamed(schedule, "02-01.0_Construction_of_Phase1_TM").Id, Objects = { "Ph4_St02_RM_Road_Planing_North" } });
            schedule.Activities.Add(new PlannerScheduleActivity { Id = "a3", FolderId = "f-new", Objects = { "Ph2_St03.2_IN_Utility_Crossing_SB_H_Ph1_St14_RM" } });

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            var result = plan.Result;

            Assert.Equal("Work_Phasing", plan.RootLayerName);
            Assert.DoesNotContain(plan.Operations, op => op.Kind == PlannerSceneOpKind.CreateLayer && op.NewName == "Work_Phasing");

            // Renamed in the Planner -> renamed layer and helper; helper keeps its node.
            var before = Helper(scene, "01-04.1_Phase1_Retainment_Installation");
            var retainment = Helper(result, "01-04.1_Phase1_Retainment_Install");
            Assert.Equal(before.Id, retainment.Id);
            Assert.Equal("Phase1 Retainment Install", Prop(retainment, PlannerProps.Name));

            // Moved in the Planner -> moved layer, helper relinked.
            Assert.Equal("02_Phase1_TM", result.FindLayer("04-02_Road_Planing").ParentName);
            Assert.Equal(Helper(result, "02_Phase1_TM").Id, Helper(result, "04-02_Road_Planing").ParentId);

            // New folder -> new layer and helper under its parent.
            Assert.Equal("02_Phase1_TM", result.FindLayer("02-03_Utility_Crossing").ParentName);
            var crossing = Helper(result, "02-03_Utility_Crossing");
            Assert.Equal("f-new", Prop(crossing, PlannerProps.FolderId));
            Assert.Equal(Helper(result, "02_Phase1_TM").Id, crossing.ParentId);

            // Duplicate code + name: matched in order, the second through its " (1)" suffix, so nothing new is made.
            var first = schedule.Folders.First(f => f.LayerName == "02-01.5_Construction_of_Phase1_TM_Stage_2B");
            Assert.Equal(2, schedule.Folders.Count(f => f.LayerName == first.LayerName));
            Assert.Equal(first.Id, Prop(Helper(result, "02-01.5_Construction_of_Phase1_TM_Stage_2B"), PlannerProps.FolderId));
            Assert.NotNull(Prop(Helper(result, "02-01.5_Construction_of_Phase1_TM_Stage_2B (1)"), PlannerProps.FolderId));
            Assert.Equal(2, plan.Operations.Count(op => op.Kind == PlannerSceneOpKind.CreateLayer || op.Kind == PlannerSceneOpKind.CreateHelper));

            // Every matched helper: folder id, code, name; legacy dates and root flag cleared.
            foreach (var folder in plan.Folders)
            {
                var helper = result.FindNode(folder.HelperId);
                Assert.Equal(folder.FolderId, Prop(helper, PlannerProps.FolderId));
                Assert.Equal(folder.Code, Prop(helper, PlannerProps.Code));
                Assert.Null(Prop(helper, PlannerProps.LegacyStart));
                Assert.Null(Prop(helper, PlannerProps.LegacyFinish));
                Assert.Null(Prop(helper, PlannerProps.LegacyTbc));
                Assert.Null(Prop(helper, PlannerProps.Root));
            }

            // Not in the schedule: kept and reported, never deleted.
            Assert.NotNull(result.FindLayer("06-06_Full_Closure_of_Main_Road"));
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'06-06_Full_Closure_of_Main_Road' sits under 'Work_Phasing' but is not in the Planner schedule"));

            // Activity checks: filed elsewhere in the Planner, and bound objects the scene lacks.
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'Ph4_St02_RM_Road_Planing_North' is filed under '02-01.0_Construction_of_Phase1_TM' in the Planner but sits on '04-02_Road_Planing'"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "'Ph1_St04_IN_Sheet_Pile_1' is filed under"));
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "1 object(s) bound to Planner activities are not in this scene: 'Ph2_St03.2_IN_Utility_Crossing_SB_H_Ph1_St14_RM'"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "Code 02-01.5 is used by 2 Planner folders"));
            Assert.Equal(0, plan.Count(PlannerReviewSeverity.Error));

            AssertSameScene(result, scene.Apply(plan.Operations));
            Assert.Empty(PlannerLayerPlans.ScheduleUpdatePlan(result, schedule, null).Operations);
        }

        [Fact]
        public void Update_ReportsFoldersDroppedAfterTheyWereLinked()
        {
            var adopted = PlannerLayerPlans.AdoptPlan(BuildFixture(), null);
            var schedule = ScheduleFrom(adopted);
            var linked = PlannerLayerPlans.ScheduleUpdatePlan(adopted.Result, schedule, null).Result;

            schedule.Folders.Remove(FolderNamed(schedule, "06-05.2_Weekend_closure"));
            var plan = PlannerLayerPlans.ScheduleUpdatePlan(linked, schedule, null);
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'06-05.2_Weekend_closure' belongs to a Planner folder that is no longer in the schedule"));
            Assert.NotNull(plan.Result.FindLayer("06-05.2_Weekend_closure"));
            Assert.Empty(plan.Operations);
        }

        [Fact]
        public void Update_MatchesByIdBeforeCodeAndCodeBeforeName()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "07_Old_Name", ParentName = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "08_Kerbs", ParentName = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "09_Surfacing", ParentName = "Work_Phasing" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "r", Name = "Work_Phasing", LayerName = "Work_Phasing", IsHelper = true, Props = { { PlannerProps.Root, "true" } } });
            scene.Nodes.Add(new PlannerSceneNode { Id = "a", Name = "07_Old_Name", LayerName = "07_Old_Name", ParentId = "r", IsHelper = true, Props = { { PlannerProps.Code, "07" }, { PlannerProps.Name, "Old_Name" } } });
            scene.Nodes.Add(new PlannerSceneNode { Id = "b", Name = "08_Kerbs", LayerName = "08_Kerbs", ParentId = "r", IsHelper = true, Props = { { PlannerProps.FolderId, "f-9" }, { PlannerProps.Code, "08" } } });
            scene.Nodes.Add(new PlannerSceneNode { Id = "c", Name = "09_Surfacing", LayerName = "09_Surfacing", ParentId = "r", IsHelper = true });
            // Objects: one loose, one under another Planner helper, one under its own.
            scene.Nodes.Add(new PlannerSceneNode { Id = "o1", Name = "IN_Kerb_1", LayerName = "08_Kerbs" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "o2", Name = "IN_Kerb_2", LayerName = "08_Kerbs", ParentId = "a" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "o3", Name = "IN_Kerb_3", LayerName = "08_Kerbs", ParentId = "b" });

            var schedule = new PlannerSchedule();
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "f-7", Code = "07", Name = "New_Name" });   // by code
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "f-9", Code = "10", Name = "Kerbing" });    // by id, despite the code change
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "f-8", Code = "08", Name = "Kerbs" });      // 08_Kerbs carries f-9, so not by code or name
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "f-10", Code = "09", Name = "Surfacing" }); // by name (no props)

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            var result = plan.Result;

            Assert.Equal("07_New_Name", result.FindNode("a").Name);
            Assert.Equal("f-7", Prop(result.FindNode("a"), PlannerProps.FolderId));
            Assert.Equal("10_Kerbing", result.FindNode("b").Name);
            Assert.Equal("10", Prop(result.FindNode("b"), PlannerProps.Code));
            Assert.Equal("f-10", Prop(result.FindNode("c"), PlannerProps.FolderId));
            var created = Helper(result, "08_Kerbs");
            Assert.StartsWith("new:", created.Id);
            Assert.Equal("f-8", Prop(created, PlannerProps.FolderId));

            Assert.Equal("b", result.FindNode("o1").ParentId);
            Assert.Equal("b", result.FindNode("o2").ParentId);
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'IN_Kerb_2' sat under the helper of '07_New_Name'"));
            AssertSameScene(result, scene.Apply(plan.Operations));
        }

        [Fact]
        public void Update_OnASceneWithoutRootBuildsEverythingAndAsksForAdoptWhenLegacyLayersExist()
        {
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "g", Code = "01", Name = "Zone5_Working" });
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "c", ParentId = "g", Code = "01-04.1", Name = "Phase1_Retainment" });
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "x", ParentId = "missing", Code = null, Name = "Loose folder" });

            var empty = PlannerLayerPlans.ScheduleUpdatePlan(new PlannerScene(), schedule, null);
            Assert.Equal(new[] { "Work_Phasing", "01_Zone5_Working", "01-04.1_Phase1_Retainment", "Loose_folder" }, empty.Result.Layers.Select(l => l.Name).ToArray());
            Assert.Equal("01_Zone5_Working", empty.Result.FindLayer("01-04.1_Phase1_Retainment").ParentName);
            Assert.Equal("Work_Phasing", empty.Result.FindLayer("Loose_folder").ParentName);
            Assert.True(Has(empty, PlannerReviewSeverity.Warning, "names a parent ('missing') that is not in the schedule"));
            Assert.False(Has(empty, PlannerReviewSeverity.Warning, "Adopt existing layers"));

            var legacy = PlannerLayerPlans.ScheduleUpdatePlan(BuildFixture(), schedule, null);
            Assert.True(Has(legacy, PlannerReviewSeverity.Warning, "Run 'Adopt existing layers' first"));
        }

        [Fact]
        public void Update_RejectsDuplicateIdsAndParentLoops()
        {
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "a", ParentId = "b", Name = "A" });
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "b", ParentId = "a", Name = "B" });
            schedule.Folders.Add(new PlannerScheduleFolder { Id = "a", Name = "A again" });

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(new PlannerScene(), schedule, null);
            Assert.True(Has(plan, PlannerReviewSeverity.Error, "lists folder id 'a' twice"));
            Assert.True(Has(plan, PlannerReviewSeverity.Error, "is inside itself"));
            Assert.Equal(new[] { "Work_Phasing", "B", "A" }, plan.Result.Layers.Select(l => l.Name).ToArray());
            Assert.Equal("Work_Phasing", plan.Result.FindLayer("B").ParentName);
            Assert.Equal("B", plan.Result.FindLayer("A").ParentName);
        }

        [Fact]
        public void Apply_RefusesOperationsThatDoNotFit()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "A" });
            Assert.Throws<InvalidOperationException>(() => scene.Apply(new[] { PlannerSceneOp.CreateLayer("a", null) })); // case-insensitive clash
            Assert.Throws<InvalidOperationException>(() => scene.Apply(new[] { PlannerSceneOp.SetLayerParent("A", "A") }));
            Assert.Throws<InvalidOperationException>(() => scene.Apply(new[] { PlannerSceneOp.RenameLayer("B", "C") }));
        }

        private static void AssertSameScene(PlannerScene expected, PlannerScene actual)
        {
            Assert.Equal(expected.Layers.Select(l => l.Name + "<" + l.ParentName).ToArray(), actual.Layers.Select(l => l.Name + "<" + l.ParentName).ToArray());
            Assert.Equal(expected.Nodes.Select(Describe).ToArray(), actual.Nodes.Select(Describe).ToArray());
        }

        private static string Describe(PlannerSceneNode node)
        {
            return node.Id + "|" + node.Name + "|" + node.LayerName + "|" + node.ParentId + "|" + node.IsHelper + "|"
                   + string.Join(";", node.Props.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value).ToArray());
        }
    }
}
