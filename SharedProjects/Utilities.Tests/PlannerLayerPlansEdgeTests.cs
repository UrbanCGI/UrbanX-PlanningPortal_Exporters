using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The layer planners on the scenes a review found awkward: duplicate codes, Planner layers dragged out of the
    /// root, groups written without their underscore, a root helper linked below a folder, Adopt after Update,
    /// context objects linked into the phasing tree, group heads, case-only differences and swapped names. Every
    /// plan is checked again on the scene as 3ds Max would read it back (<see cref="ReadBack"/>).
    /// </summary>
    public class PlannerLayerPlansEdgeTests
    {
        private static PlannerScene RootScene()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "0" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work_Phasing" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "1", Name = "Work_Phasing", LayerName = "Work_Phasing", IsHelper = true, Props = { { PlannerProps.Root, "true" } } });
            return scene;
        }

        private static PlannerSceneNode FolderHelper(string id, string layer, string parentId, string folderId, string code, string name)
        {
            var node = new PlannerSceneNode { Id = id, Name = layer, LayerName = layer, ParentId = parentId, IsHelper = true };
            if (folderId != null)
            {
                node.Props[PlannerProps.FolderId] = folderId;
            }
            if (code != null)
            {
                node.Props[PlannerProps.Code] = code;
            }
            if (name != null)
            {
                node.Props[PlannerProps.Name] = name;
            }
            return node;
        }

        private static PlannerScheduleFolder Folder(string id, string parentId, string code, string name)
        {
            return new PlannerScheduleFolder { Id = id, ParentId = parentId, Code = code, Name = name };
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

        /// <summary>
        /// The plan's result as <c>PlannerMaxScene.Read</c> would see it after applying: created helpers have real
        /// (numeric) handles and every property went through the exporter's space encoding and back.
        /// </summary>
        internal static PlannerScene ReadBack(PlannerScene result)
        {
            var scene = result.Clone();
            var next = 50000;
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in scene.Nodes.Where(n => PlannerMaxScript.IsNewId(n.Id)))
            {
                ids[node.Id] = (next++).ToString();
            }
            foreach (var node in scene.Nodes)
            {
                string mapped;
                if (ids.TryGetValue(node.Id, out mapped))
                {
                    node.Id = mapped;
                }
                if (node.ParentId != null && ids.TryGetValue(node.ParentId, out mapped))
                {
                    node.ParentId = mapped;
                }
                foreach (var key in node.Props.Keys.ToList())
                {
                    // Tools.SetStringProperty / GetStringProperty: EncodeSpace on the way in, DecodeSpace on the way out.
                    node.Props[key] = node.Props[key].Replace(" ", "%20").Replace("%20", " ");
                }
            }
            return scene;
        }

        private static void AssertSettled(PlannerScenePlan plan, Func<PlannerScene, PlannerScenePlan> again)
        {
            var second = again(ReadBack(plan.Result));
            Assert.Empty(second.Operations);
            Assert.Equal(0, second.Count(PlannerReviewSeverity.Error));
        }

        // ---- matching ------------------------------------------------------------------------------------------

        [Fact]
        public void Update_ADuplicateCodeNeverTakesTheLayerAnotherFolderIsNamedLike()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "03_Kerbs", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "03_Kerbs", "1", null, "03", "Kerbs"));
            scene.Nodes.Add(new PlannerSceneNode { Id = "11", Name = "IN_Kerb_1", LayerName = "03_Kerbs", ParentId = "10" });
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("a", null, "03", "Kerbs_North"));
            schedule.Folders.Add(Folder("b", null, "03", "Kerbs"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            var result = plan.Result;

            // The layer (and the object modelled on it) stays with the folder whose name it carries.
            Assert.Equal("b", Prop(result.FindNode("10"), PlannerProps.FolderId));
            Assert.Equal("03_Kerbs", result.FindNode("10").LayerName);
            Assert.Equal("10", result.FindNode("11").ParentId);
            Assert.DoesNotContain(plan.Operations, op => op.Kind == PlannerSceneOpKind.RenameLayer);
            Assert.Contains(plan.Operations, op => op.Kind == PlannerSceneOpKind.CreateLayer && op.NewName == "03_Kerbs_North");
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "Code 03 is used by 2 Planner folders"));
            AssertSettled(plan, s => PlannerLayerPlans.ScheduleUpdatePlan(s, schedule, null));
        }

        [Fact]
        public void Update_AUniqueCodeStillMatchesWhenTheNameChanged()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "03_Kerbs", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "03_Kerbs", "1", null, "03", "Kerbs"));
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("a", null, "03", "Kerbs_North"));
            schedule.Folders.Add(Folder("b", null, "04", "Kerbs"));   // named like the layer once its code is ignored, but code 04

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            Assert.Equal("a", Prop(plan.Result.FindNode("10"), PlannerProps.FolderId));
            Assert.Equal("03_Kerbs_North", plan.Result.FindNode("10").Name);
            Assert.NotNull(plan.Result.FindLayer("04_Kerbs"));
        }

        [Fact]
        public void Update_FindsAPlannerLayerDraggedOutOfTheRootAndMovesItBack()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Context" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone", ParentName = "Context" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01-01_Kerbs", ParentName = "01_Zone" });
            scene.Nodes.Add(FolderHelper("10", "01_Zone", null, "f1", "01", "Zone"));
            scene.Nodes.Add(FolderHelper("11", "01-01_Kerbs", "10", "f2", "01-01", "Kerbs"));
            scene.Nodes.Add(new PlannerSceneNode { Id = "20", Name = "IN_Kerb", LayerName = "01-01_Kerbs", ParentId = "11" });
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("f1", null, "01", "Zone"));
            schedule.Folders.Add(Folder("f2", "f1", "01-01", "Kerbs"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            var result = plan.Result;

            Assert.Equal("Work_Phasing", result.FindLayer("01_Zone").ParentName);
            Assert.Equal("01_Zone", result.FindLayer("01-01_Kerbs").ParentName);
            Assert.Equal("1", result.FindNode("10").ParentId);
            Assert.DoesNotContain(plan.Operations, op => op.Kind == PlannerSceneOpKind.CreateLayer || op.Kind == PlannerSceneOpKind.CreateHelper);
            Assert.Single(result.Nodes, n => Prop(n, PlannerProps.FolderId) == "f1");
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'01_Zone' belongs to the Planner folder '01_Zone' but sat outside 'Work_Phasing'"));
            AssertSettled(plan, s => PlannerLayerPlans.ScheduleUpdatePlan(s, schedule, null));
        }

        [Fact]
        public void Update_AHelperCarryingAnotherLayersFolderIdIsReportedAsACopy()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone", ParentName = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone_Copy", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "01_Zone", "1", "f1", "01", "Zone"));
            scene.Nodes.Add(FolderHelper("12", "01_Zone_Copy", "1", "f1", "01", "Zone"));
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("f1", null, "01", "Zone"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            Assert.Equal("f1", Prop(plan.Result.FindNode("10"), PlannerProps.FolderId));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'01_Zone_Copy' on layer '01_Zone_Copy' carries the same Planner folder id as the helper of '01_Zone'"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "no longer in the schedule"));
            Assert.Empty(plan.Operations);
        }

        [Fact]
        public void Update_ANameThatDiffersOnlyInCaseIsNotRenamed()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_zone", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "01_zone", "1", "f1", null, null));
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("f1", null, "01", "Zone"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            Assert.DoesNotContain(plan.Operations, op => op.Kind == PlannerSceneOpKind.RenameLayer || op.Kind == PlannerSceneOpKind.RenameHelper);
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "already taken"));
            Assert.Equal("01", Prop(plan.Result.FindNode("10"), PlannerProps.Code));
            AssertSettled(plan, s => PlannerLayerPlans.ScheduleUpdatePlan(s, schedule, null));

            // Matching by name ignores case too, as 3ds Max does.
            var byName = RootScene();
            byName.Layers.Add(new PlannerSceneLayer { Name = "01_zone", ParentName = "Work_Phasing" });
            var named = PlannerLayerPlans.ScheduleUpdatePlan(byName, schedule, null);
            Assert.DoesNotContain(named.Operations, op => op.Kind == PlannerSceneOpKind.CreateLayer);
        }

        [Fact]
        public void Update_TwoFoldersThatSwapNamesSettleInOneRun()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_A", ParentName = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "02_B", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "01_A", "1", "fa", "01", "A"));
            scene.Nodes.Add(FolderHelper("11", "02_B", "1", "fb", "02", "B"));
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("fa", null, "02", "B"));
            schedule.Folders.Add(Folder("fb", null, "01", "A"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            Assert.Equal("02_B", plan.Result.FindNode("10").LayerName);
            Assert.Equal("02_B", plan.Result.FindNode("10").Name);
            Assert.Equal("01_A", plan.Result.FindNode("11").LayerName);
            Assert.DoesNotContain(plan.Result.Layers, l => l.Name.Contains("("));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "already taken"));
            Assert.Equal(new[] { "02_B", "01_A" }, plan.Folders.Select(f => f.LayerName).ToArray());
            AssertSettled(plan, s => PlannerLayerPlans.ScheduleUpdatePlan(s, schedule, null));
        }

        // ---- the root helper ------------------------------------------------------------------------------------

        [Fact]
        public void Plans_UnlinkTheRootHelperWhenItHangsBelowAFolderHelper()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "01_Zone", null, "f1", "01", "Zone"));
            scene.Nodes.Add(new PlannerSceneNode { Id = "1", Name = "Work_Phasing", LayerName = "Work_Phasing", ParentId = "10", IsHelper = true, Props = { { PlannerProps.Root, "true" } } });
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("f1", null, "01", "Zone"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            Assert.Equal(0, plan.Count(PlannerReviewSeverity.Error));
            Assert.Null(plan.Result.FindNode("1").ParentId);
            Assert.Equal("1", plan.Result.FindNode("10").ParentId);
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "The root helper 'Work_Phasing' hung below '01_Zone'"));
            AssertSettled(plan, s => PlannerLayerPlans.ScheduleUpdatePlan(s, schedule, null));

            var adopt = PlannerLayerPlans.AdoptPlan(scene, null);
            Assert.Equal(0, adopt.Count(PlannerReviewSeverity.Error));
            Assert.Null(adopt.Result.FindNode("1").ParentId);

            // A root helper below context (a site dummy, say) stays where it is.
            var site = scene.Clone();
            site.Nodes.Add(new PlannerSceneNode { Id = "5", Name = "Site_Origin", LayerName = "0", IsHelper = true });
            site.Nodes.Single(n => n.Id == "1").ParentId = "5";
            site.Nodes.Single(n => n.Id == "10").ParentId = "1";
            Assert.Empty(PlannerLayerPlans.ScheduleUpdatePlan(site, schedule, null).Operations);
        }

        // ---- adopt ----------------------------------------------------------------------------------------------

        [Fact]
        public void Adopt_AGroupWrittenWithoutItsUnderscoreMovesUnderTheRoot()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "0" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "02_3D_Models" });                                  // numbered context: left alone
            scene.Layers.Add(new PlannerSceneLayer { Name = "05_Night_Closure" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "05_01_Kerbs_01-01-27", ParentName = "05_Night_Closure" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "1", Name = "Ph5_St01_IN_Kerb", LayerName = "05_01_Kerbs_01-01-27" });

            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            var result = plan.Result;

            Assert.Equal("Work_Phasing", result.FindLayer("05_Night_Closure").ParentName);
            Assert.Equal("05_Night_Closure", result.FindLayer("05-01_Kerbs").ParentName);
            Assert.Null(result.FindLayer("02_3D_Models").ParentName);
            Assert.Null(result.FindLayer("0").ParentName);
            Assert.Equal(2, plan.Folders.Count);
            Assert.Equal("2027-01-01", plan.Folders[1].LegacyStart.Value.ToString("yyyy-MM-dd"));
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'05_Night_Closure' holds dated layers"));
            AssertSettled(plan, s => PlannerLayerPlans.AdoptPlan(s, null));
        }

        [Fact]
        public void Adopt_SaysSoWhenNothingWasAdopted()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "0" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "Archive" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "Old", ParentName = "Archive" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "_01_Zone", ParentName = "Old" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_01_Kerbs_01-01-27", ParentName = "_01_Zone" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "07_02_Dated_Top_Level_05-02-27" });

            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            Assert.Empty(plan.Folders);
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'_01_Zone' looks like an old phasing layer (with 1 more inside it)"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'07_02_Dated_Top_Level_05-02-27' looks like an old phasing layer"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "Nothing was adopted"));
            Assert.False(Has(plan, PlannerReviewSeverity.Warning, "'01_01_Kerbs_01-01-27' looks like"));

            Assert.True(Has(PlannerLayerPlans.AdoptPlan(new PlannerScene(), null), PlannerReviewSeverity.Note, "Nothing was adopted"));
        }

        [Fact]
        public void Adopt_MovesCodedLayersFromTheOldRootAndClearsAStaleRootFlag()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "HS2_Phasing_v02" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "_01_Zone", ParentName = "HS2_Phasing_v02" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "02-01.5_Coded", ParentName = "HS2_Phasing_v02" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "Notes", ParentName = "HS2_Phasing_v02" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "10", Name = "_01_Zone", LayerName = "_01_Zone", IsHelper = true, Props = { { PlannerProps.Root, "true" } } });

            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            var result = plan.Result;
            Assert.Equal("Work_Phasing", result.FindLayer("02-01.5_Coded").ParentName);
            Assert.Equal("02-01.5", Prop(result.Nodes.Single(n => n.Name == "02-01.5_Coded"), PlannerProps.Code));
            Assert.Equal("HS2_Phasing_v02", result.FindLayer("Notes").ParentName);
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'Notes' sits under the old phasing root but does not start with a number"));
            Assert.Null(Prop(result.FindNode("10"), PlannerProps.Root));
            Assert.Equal("01", Prop(result.FindNode("10"), PlannerProps.Code));
            AssertSettled(plan, s => PlannerLayerPlans.AdoptPlan(s, null));
        }

        [Fact]
        public void Adopt_AfterUpdateChangesNothing()
        {
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("f1", null, "01", "Zone = 5"));
            schedule.Folders.Add(Folder("f2", null, null, ""));
            schedule.Folders.Add(Folder("f3", null, "03", "Kerbs"));
            schedule.Folders.Add(Folder("f4", "f3", "03-01", "50%20mm Binder"));

            var updated = PlannerLayerPlans.ScheduleUpdatePlan(new PlannerScene(), schedule, null);
            var scene = ReadBack(updated.Result);
            Assert.NotNull(scene.FindLayer("01_Zone_=_5"));
            Assert.NotNull(scene.FindLayer("Folder_f2"));

            var adopt = PlannerLayerPlans.AdoptPlan(scene, null);
            Assert.Empty(adopt.Operations);
            Assert.Equal(new[] { "f1", "f2", "f3", "f4" }, adopt.Folders.Select(f => f.FolderId).ToArray());
            Assert.Empty(PlannerLayerPlans.ScheduleUpdatePlan(ReadBack(adopt.Result), schedule, null).Operations);
            Assert.Empty(PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null).Operations);
        }

        [Fact]
        public void Adopt_UpdateAdoptOnTheV3FixtureSettles()
        {
            var adopted = PlannerLayerPlans.AdoptPlan(PlannerLayerPlansTests.BuildFixture(), null);
            AssertSettled(adopted, s => PlannerLayerPlans.AdoptPlan(s, null));

            var schedule = PlannerLayerPlansTests.ScheduleFrom(adopted);
            var updated = PlannerLayerPlans.ScheduleUpdatePlan(ReadBack(adopted.Result), schedule, null);
            Assert.Equal(0, updated.Count(PlannerReviewSeverity.Error));
            AssertSettled(updated, s => PlannerLayerPlans.ScheduleUpdatePlan(s, schedule, null));
            AssertSettled(updated, s => PlannerLayerPlans.AdoptPlan(s, null));
        }

        // ---- objects --------------------------------------------------------------------------------------------

        [Fact]
        public void Plans_ReportContextObjectsLinkedIntoThePhasingTree()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "01_Zone", "1", "f1", "01", "Zone"));
            scene.Nodes.Add(new PlannerSceneNode { Id = "20", Name = "Existing_Kerb", LayerName = "0", ParentId = "10" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "21", Name = "Existing_Kerb_Cap", LayerName = "0", ParentId = "20" });   // below it: not listed again
            scene.Nodes.Add(new PlannerSceneNode { Id = "22", Name = "IN_Kerb", LayerName = "01_Zone", ParentId = "10" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "23", Name = "Kerb_Note", LayerName = "0", ParentId = "22" });          // under a phasing object
            var schedule = new PlannerSchedule();
            schedule.Folders.Add(Folder("f1", null, "01", "Zone"));

            var plan = PlannerLayerPlans.ScheduleUpdatePlan(scene, schedule, null);
            Assert.Empty(plan.Operations);
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "2 object(s) on layers outside 'Work_Phasing' are linked under the helper '01_Zone'"));
            Assert.True(Has(plan, PlannerReviewSeverity.Warning, "'Existing_Kerb', 'Kerb_Note'"));
            Assert.True(Has(PlannerLayerPlans.AdoptPlan(scene, null), PlannerReviewSeverity.Warning, "are linked under the helper '01_Zone'"));

            var nodes = scene.Nodes.Where(n => !n.IsHelper).Select(n => new SceneNodeInfo { Id = n.Id, Name = n.Name, IsMesh = true, LayerName = n.LayerName }).ToList();
            var report = PlannerNamingValidator.Validate(nodes, scene, new DateTime(2026, 10, 2));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Warning && i.Message.StartsWith("3 object(s) on layers outside 'Work_Phasing' are linked under the helper of '01_Zone'", StringComparison.Ordinal));
        }

        [Fact]
        public void Check_OnlyAdvisesUpdateLayersForLinksItMakes()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_Zone", ParentName = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "02_Road", ParentName = "Work_Phasing" });
            scene.Nodes.Add(FolderHelper("10", "01_Zone", "1", "f1", "01", "Zone"));
            scene.Nodes.Add(FolderHelper("11", "02_Road", "1", "f2", "02", "Road"));
            scene.Nodes.Add(new PlannerSceneNode { Id = "20", Name = "IN_Loose", LayerName = "01_Zone" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "21", Name = "IN_Road_Slab", LayerName = "02_Road", ParentId = "11" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "22", Name = "IN_Kerb_On_Slab", LayerName = "01_Zone", ParentId = "21" });

            var nodes = scene.Nodes.Where(n => !n.IsHelper).Select(n => new SceneNodeInfo { Id = n.Id, Name = n.Name, IsMesh = true, LayerName = n.LayerName }).ToList();
            var report = PlannerNamingValidator.Validate(nodes, scene, new DateTime(2026, 10, 2));
            Assert.Contains(report.Issues, i => i.Message.Contains("'IN_Loose'") && i.Message.Contains("Run Update layers"));
            Assert.Contains(report.Issues, i => i.Message.Contains("'IN_Kerb_On_Slab'") && i.Message.Contains("by hand") && !i.Message.Contains("Run Update layers"));

            var panel = PlannerPanelModel.Build(scene, null);
            Assert.Contains("link that (or this) to the helper by hand", panel.ObjectsOn(new[] { "01_Zone" }).Single(o => o.Name == "IN_Kerb_On_Slab").Note);
            Assert.Contains("run Update layers", panel.ObjectsOn(new[] { "01_Zone" }).Single(o => o.Name == "IN_Loose").Note);
        }

        [Fact]
        public void Plans_AGroupHeadNeverStandsForItsLayer()
        {
            var scene = RootScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "_01_Zone", ParentName = "Work_Phasing" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "30", Name = "_01_Zone", LayerName = "_01_Zone", IsHelper = true, IsGroupHead = true });
            scene.Nodes.Add(new PlannerSceneNode { Id = "31", Name = "IN_Barrier_A", LayerName = "_01_Zone", ParentId = "30" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "32", Name = "IN_Barrier_B", LayerName = "_01_Zone", ParentId = "30" });
            // A group head elsewhere, named like the layer, is not pulled across either.
            scene.Nodes.Add(new PlannerSceneNode { Id = "40", Name = "01_Zone", LayerName = "0", IsHelper = true, IsGroupHead = true });

            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            var result = plan.Result;
            var helper = result.Nodes.Single(n => n.LayerName == "01_Zone" && n.CanStandForLayer);
            Assert.StartsWith("new:", helper.Id);
            Assert.Equal("01", Prop(helper, PlannerProps.Code));
            Assert.Empty(result.FindNode("30").Props);
            Assert.Equal("_01_Zone", result.FindNode("30").Name);       // the group keeps its name
            Assert.Equal(helper.Id, result.FindNode("30").ParentId);     // and hangs below the helper like any object
            Assert.Equal("30", result.FindNode("31").ParentId);          // its members stay in the group
            Assert.Equal("0", result.FindNode("40").LayerName);
            Assert.True(Has(plan, PlannerReviewSeverity.Note, "'_01_Zone' on layer '01_Zone' is a group or container"));
            Assert.False(Has(plan, PlannerReviewSeverity.Note, "'IN_Barrier_A' is linked to"));
            AssertSettled(plan, s => PlannerLayerPlans.AdoptPlan(s, null));

            var tree = PlannerLayerTree.Read(result);
            Assert.Equal(helper.Id, tree.Find("01_Zone").Helper.Id);
        }
    }
}
