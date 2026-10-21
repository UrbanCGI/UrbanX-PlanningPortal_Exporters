using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// Several phasing roots in one scene (naming v2, the multiple-roots spec, R1 to R25): each an area of one schedule. The
    /// scene model finds every root, Clean up works root by root with and without the Planner's list, and the list's Root
    /// rows find, make and rename their roots.
    /// </summary>
    public class PlannerPhasingRootsTests
    {
        private const string North = "Work_Phasing_North";
        private const string South = "Work_Phasing_South";

        // Two roots, each a helper on a Max layer of its name, with phase groups of their own (North 1 and 2, South 7).
        private static TestScene TwoRoots()
        {
            var scene = new TestScene();
            var north = scene.Unit(North, null, null);
            scene.Unit("_01_North_Enabling", North, north);
            scene.Unit("_02_North_Roads", North, north);
            var south = scene.Unit(South, null, null);
            scene.Unit("_07_South_Enabling", South, south);
            return scene;
        }

        private static PlannerSceneNode Layer(TestScene scene, string group, string name, params string[] objects)
        {
            var helper = scene.Unit(name, group, scene.Find(group));
            foreach (var obj in objects)
            {
                scene.Object(obj, helper, name);
            }
            return helper;
        }

        private static Dictionary<long, string> After(PlannerSceneSnapshot scene, PlannerCleanUpResult result)
        {
            var renamed = result.NodeRenames.ToDictionary(r => r.Key, r => r.NewName);
            return scene.Nodes.ToDictionary(n => n.Key, n =>
            {
                string name;
                return renamed.TryGetValue(n.Key, out name) ? name : n.Name;
            });
        }

        private static PlannerCleanUpPlan Plan(PlannerSceneSnapshot scene, PlannerLayerList list = null)
        {
            return PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes(), list);
        }

        // The batch for the result, run on the scene: it must leave the scene the result promises, and report no problem.
        private static CleanUpScriptRun Run(PlannerCleanUpPlan plan, PlannerCleanUpResult result, PlannerSceneSnapshot scene)
        {
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Empty(batch.Refused);
            var run = CleanUpScriptRun.Run(batch.Script, scene);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene)));
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems, string.Join("\n", outcome.Failures));
            return run;
        }

        // -------------------------------------------------------------------------------------------------------------
        // The scene model

        [Fact]
        public void EveryRootIsFoundInNameOrderEachPairedWithTheMaxLayerOfExactlyItsName()
        {
            var scene = new TestScene();
            // Made in this order in 3ds Max; name order puts North first.
            var south = scene.Unit(South, null, null);
            var north = scene.Unit(North, null, null);
            scene.Unit("_07_South_Enabling", South, south);
            var kerbs = Layer(scene, "_07_South_Enabling", "07_01_Kerbs");
            scene.Unit("_01_North_Enabling", North, north);
            var drains = Layer(scene, "_01_North_Enabling", "01_01_Drainage");
            // Neither a root: a top-level mesh with "phasing" in its name, and a helper named as a phase group.
            scene.Node("Phasing_Sign", null, "0", false);
            scene.Helper("_09_Phasing_Spare", null, "0");

            var model = new PlannerSceneModel(scene.Snapshot);

            Assert.Equal(new[] { North, South }, model.PhasingRoots.Select(r => r.Name).ToArray());
            Assert.Equal(North, model.PhasingRoots[0].Layer.Name);
            Assert.Equal(South, model.PhasingRoots[1].Layer.Name);
            Assert.Same(model.PhasingRoots[0], model.FirstRoot);
            Assert.True(model.IsPhasingRootNode(south));
            Assert.False(model.IsPhasingRootNode(scene.Find("Phasing_Sign")));
            Assert.Contains(north.Key, model.PhasingHelpers);
            Assert.Contains(south.Key, model.PhasingHelpers);
            // Each unit knows its root; the phasing tree is every root's.
            Assert.Same(model.PhasingRoots[1], model.Units.Single(u => u.Name == "07_01_Kerbs").Root);
            Assert.Same(model.PhasingRoots[0], model.Units.Single(u => u.Name == "01_01_Drainage").Root);
            Assert.True(model.Units.Single(u => u.Name == "07_01_Kerbs").InPhasingTree);
            Assert.Contains("07_01_Kerbs", model.TreeLayers);
            Assert.Contains(kerbs.Key, model.TreeNodes);
            Assert.Contains(drains.Key, model.TreeNodes);
            // The roots' own layers are no units.
            Assert.DoesNotContain(model.Units, u => u.Name == North || u.Name == South);
        }

        [Fact]
        public void ARootWithNoMaxLayerOfItsNameTakesNoOtherLayerAndASceneWithNoRootHelperHasItsLayerAsTheRoot()
        {
            var scene = new TestScene();
            scene.Layer("Work_Phasing");
            var root = scene.Helper(North, null, "Work_Phasing");
            scene.Unit("_01_Enabling", "Work_Phasing", root);
            var model = new PlannerSceneModel(scene.Snapshot);
            Assert.Single(model.PhasingRoots);
            Assert.Null(model.PhasingRoots[0].Layer);
            Assert.Equal("Work_Phasing", PlannerLayerPlanner.TakeableLayer(model, model.PhasingRoots[0]).Name);

            var layerOnly = new TestScene();
            layerOnly.Layer("Work_Phasing");
            layerOnly.Layer("_01_Enabling", "Work_Phasing");
            var byLayer = new PlannerSceneModel(layerOnly.Snapshot);
            Assert.Single(byLayer.PhasingRoots);
            Assert.Null(byLayer.PhasingRoots[0].Node);
            Assert.Equal("Work_Phasing", byLayer.PhasingRoots[0].Name);
            Assert.Contains("_01_Enabling", byLayer.TreeLayers);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Clean up without the list

        [Fact]
        public void CleanUpWorksOnEveryRootAndMovesALayerOnlyIntoAGroupOfItsOwnRoot()
        {
            var scene = TwoRoots();
            var pipe = Layer(scene, "_01_North_Enabling", "01_01_Drainage_17-10-26", "Ph1_St01_IN_Pipe");
            var hoarding = Layer(scene, "_07_South_Enabling", "07_01_Hoarding_TBC", "Ph7_St01_IN_Hoarding");
            // Straight under the South root: it goes into South's group of phase 7.
            var fence = scene.Unit("07_02_Fence_20-10-26", South, scene.Find(South));
            scene.Object("Ph7_St02_IN_Fence", fence, "07_02_Fence_20-10-26");
            // Straight under the South root, but a layer of phase 1: North has that group, South does not, so it stays.
            var stray = scene.Unit("01_09_Stray", South, scene.Find(South));

            var plan = Plan(scene.Snapshot);
            Assert.Null(plan.Message);
            Assert.Contains(plan.Notes, n => n.StartsWith("Phasing roots (2): " + North + ", " + South + ".", StringComparison.Ordinal));
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("01_01_Drainage", after[pipe.Key]);
            Assert.Equal("07_01_Hoarding", after[hoarding.Key]);
            Assert.Equal("07_02_Fence", after[fence.Key]);
            Assert.Equal("IN_Hoarding", after[scene.Find("Ph7_St01_IN_Hoarding").Key]);
            Assert.Equal("IN_Fence", after[scene.Find("Ph7_St02_IN_Fence").Key]);
            Assert.Contains(result.NodeMoves, m => m.Key == fence.Key && m.ToParentKey == scene.Find("_07_South_Enabling").Key);
            Assert.DoesNotContain(result.NodeMoves, m => m.Key == stray.Key);
            var strayRow = result.Rows.Single(r => r.NodeKey == stray.Key);
            Assert.Contains("no group _01_<name> to move it into; it stays under " + South, strayRow.Why);
            Assert.Equal(South, result.Rows.Single(r => r.NodeKey == fence.Key && r.Status == PlannerCleanUpStatus.Change).LayerItSitsIn);

            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene).Resolve(null).HasChanges);
        }

        [Fact]
        public void CleanUpGivesARootWithNoMaxLayerOfItsNameOne()
        {
            // North sits on a layer of another name, which it takes; South sits on the default layer, so one is made.
            var scene = new TestScene();
            scene.Layer("North_Area");
            var north = scene.Helper(North, null, "North_Area");
            scene.Unit("_01_North_Enabling", "North_Area", north);
            Layer(scene, "_01_North_Enabling", "01_01_Drainage", "IN_Pipe");
            var south = scene.Helper(South, null, "0");
            scene.Unit("_07_South_Enabling", null, south);

            var plan = Plan(scene.Snapshot);
            var result = plan.Resolve(null);
            Assert.Contains(result.MaxLayerRenames, r => r.OldName == "North_Area" && r.NewName == North);
            Assert.Contains(result.NewLayers, l => l.Name == South && l.ParentName == null);
            Assert.Contains(result.HelperPuts, p => p.Key == south.Key && p.ToLayer == South && p.ToNewLayer);
            // South's group's Max layer, at the top, moves into the new one.
            Assert.Contains(result.MaxLayerMoves, m => m.Name == "_07_South_Enabling" && m.NewParentName == South);
            Assert.Contains(result.Rows, r => r.NodeKey == north.Key && r.Why.Contains("its Max layer North_Area is named for it"));
            Assert.Contains(result.Rows, r => r.NodeKey == south.Key && r.Why.Contains("its Max layer " + South + " is added, and it is put on it"));

            var run = Run(plan, result, scene.Snapshot);
            var model = new PlannerSceneModel(run.Scene);
            Assert.All(model.PhasingRoots, r => Assert.NotNull(r.Layer));
            Assert.False(Plan(run.Scene).Resolve(null).HasChanges);
        }

        [Fact]
        public void ARootWhoseHelperSharesAMaxLayerGetsANewOneAndItsGroupsMaxLayersMoveIntoIt()
        {
            // South's helper and South's group's Max layer are in the layer Site, which holds a survey mesh too: Site keeps its
            // name and its mesh, and South's own layer is made at the top, with its helper on it and its group's layer in it.
            var scene = new TestScene();
            var north = scene.Unit(North, null, null);
            scene.Unit("_01_North_Enabling", North, north);
            scene.Layer("Site");
            var survey = scene.Object("Survey_Mesh", null, "Site");
            var south = scene.Helper(South, null, "Site");
            scene.Unit("_07_South_Enabling", "Site", south);
            Layer(scene, "_07_South_Enabling", "07_01_Hoarding", "IN_Hoarding");
            var model = new PlannerSceneModel(scene.Snapshot);
            Assert.Null(PlannerLayerPlanner.TakeableLayer(model, model.PhasingRoots.Single(r => r.Name == South)));

            var plan = Plan(scene.Snapshot);
            var result = plan.Resolve(null);
            Assert.Contains(result.NewLayers, l => l.Name == South && l.ParentName == null);
            Assert.Contains(result.HelperPuts, p => p.Key == south.Key && p.ToLayer == South && p.ToNewLayer);
            Assert.Contains(result.MaxLayerMoves, m => m.Name == "_07_South_Enabling" && m.NewParentName == South);
            // Only the group's layer moves: the layers in it come with it.
            Assert.DoesNotContain(result.MaxLayerMoves, m => m.Name == "07_01_Hoarding");
            Assert.DoesNotContain(result.MaxLayerRenames, r => r.OldName == "Site");
            Assert.DoesNotContain(result.HelperPuts, p => p.Key == survey.Key);

            var run = Run(plan, result, scene.Snapshot);
            var after = new PlannerSceneModel(run.Scene);
            Assert.Equal(South, after.PhasingRoots.Single(r => r.Name == South).Layer.Name);
            Assert.Contains("07_01_Hoarding", after.TreeLayers);
            Assert.False(Plan(run.Scene).Resolve(null).HasChanges);
        }

        // -------------------------------------------------------------------------------------------------------------
        // The Planner's list and its Root rows

        [Fact]
        public void RootRowsFindTheirRootsAndARowMovedToAnotherRootMovesInMax()
        {
            var scene = TwoRoots();
            scene.Find(North).PlannerId = "r1";
            var roads = scene.Find("_02_North_Roads");
            Layer(scene, "_02_North_Roads", "02_01_Binder", "IN_Binder");
            Layer(scene, "_07_South_Enabling", "07_01_Hoarding", "IN_Hoarding");

            var list = TestCsv.List(
                TestCsv.Row("r1", null, North, "Root"),
                TestCsv.Row("g1", "r1", "_01_North_Enabling"),
                TestCsv.Row("r2", null, South, "Root"),
                TestCsv.Row("g7", "r2", "_07_South_Enabling"),
                // Moved to South in the Planner.
                TestCsv.Row("g2", "r2", "_02_North_Roads"),
                TestCsv.Row("f1", "g2", "02_01_Binder", "Layer"),
                TestCsv.Row("f7", "g7", "07_01_Hoarding", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);

            Assert.Contains(result.NodeMoves, m => m.Key == roads.Key && m.ToParentKey == scene.Find(South).Key);
            Assert.Contains(result.MaxLayerMoves, m => m.Name == "_02_North_Roads" && m.NewParentName == South);
            // The South root takes its row's Planner ID; North has it already.
            Assert.Contains(result.Stamps, s => s.Key == scene.Find(South).Key && s.PlannerId == "r2");
            Assert.DoesNotContain(result.Stamps, s => s.Key == scene.Find(North).Key);
            Assert.Empty(result.NewLayers);
            Assert.Empty(result.NewHelpers);

            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void ARootRowTheSceneDoesNotHaveIsMadeARootRenamedInThePlannerIsRenamedAndAnUnlistedRootIsKept()
        {
            var scene = TwoRoots();
            scene.Find(South).PlannerId = "r2";
            var copy = scene.Unit("Old_Phasing_copy", null, null);

            var list = TestCsv.List(
                TestCsv.Row("r1", null, North, "Root"),
                TestCsv.Row("g1", "r1", "_01_North_Enabling"),
                TestCsv.Row("g2", "r1", "_02_North_Roads"),
                // South renamed in the Planner: found by its Planner ID.
                TestCsv.Row("r2", null, "Work_Phasing_South_Area", "Root"),
                TestCsv.Row("g7", "r2", "_07_South_Enabling"),
                // A new area.
                TestCsv.Row("r3", null, "Work_Phasing_East", "Root"),
                TestCsv.Row("g9", "r3", "_09_East_Works"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("Work_Phasing_South_Area", after[scene.Find(South).Key]);
            Assert.Contains(result.MaxLayerRenames, r => r.OldName == South && r.NewName == "Work_Phasing_South_Area");
            Assert.Contains(result.NewLayers, l => l.Name == "Work_Phasing_East" && l.ParentName == null);
            Assert.Contains(result.NewHelpers, h => h.Name == "Work_Phasing_East" && h.ParentKey == null && h.PlannerId == "r3");
            var east = result.NewHelpers.Single(h => h.Name == "Work_Phasing_East");
            Assert.Contains(result.NewHelpers, h => h.Name == "_09_East_Works" && h.ParentKey == east.Key);
            Assert.Equal("Old_Phasing_copy", after[copy.Key]);
            var kept = result.Rows.Single(r => r.Item == "Phasing root" && r.NewName == "Old_Phasing_copy");
            Assert.Equal(PlannerCleanUpStatus.Check, kept.Status);
            Assert.Contains("never deletes", kept.Why);

            var run = Run(plan, result, scene.Snapshot);
            var again = Plan(run.Scene, list).Resolve(null);
            Assert.False(again.HasChanges, string.Join("\n", again.Rows.Where(r => r.Status == PlannerCleanUpStatus.Change).Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
        }

        [Fact]
        public void AListWithNoRootRowsKeepsEachTopLevelRowInTheRootItIsInAndPutsANewOneInTheFirst()
        {
            var scene = TwoRoots();
            Layer(scene, "_07_South_Enabling", "07_01_Hoarding", "IN_Hoarding");
            // A list from a Planner that writes no Root rows: its top-level rows have no root.
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_01_North_Enabling"),
                TestCsv.Row("g2", null, "_02_North_Roads"),
                TestCsv.Row("g7", null, "_07_South_Enabling"),
                TestCsv.Row("f7", "g7", "07_01_Hoarding", "Layer"),
                TestCsv.Row("g8", null, "_08_New_Works"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);

            // South's group stays under South: no list pulls one root's groups into another any more.
            Assert.DoesNotContain(result.NodeMoves, m => m.Key == scene.Find("_07_South_Enabling").Key);
            Assert.DoesNotContain(result.MaxLayerMoves, m => m.Name == "_07_South_Enabling");
            // A new top-level row goes into the first root, North.
            Assert.Contains(result.NewHelpers, h => h.Name == "_08_New_Works" && h.ParentKey == scene.Find(North).Key);
            Assert.Contains(result.NewLayers, l => l.Name == "_08_New_Works" && l.ParentName == North);

            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void RootRowsFindTheirRootsByPlannerIdBeforeAnyRowGoesByName()
        {
            // North's helper carries r2's Planner ID: r2 was renamed Central in the Planner. r1, listed first, has North's name
            // but holds South's group: it is South, renamed North, and never takes North by its name.
            var scene = TwoRoots();
            scene.Find(North).PlannerId = "r2";
            Layer(scene, "_07_South_Enabling", "07_01_Hoarding", "IN_Hoarding");
            var list = TestCsv.List(
                TestCsv.Row("r1", null, North, "Root"),
                TestCsv.Row("g7", "r1", "_07_South_Enabling"),
                TestCsv.Row("f7", "g7", "07_01_Hoarding", "Layer"),
                TestCsv.Row("r2", null, "Work_Phasing_Central", "Root"),
                TestCsv.Row("g1", "r2", "_01_North_Enabling"),
                TestCsv.Row("g2", "r2", "_02_North_Roads"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("Work_Phasing_Central", after[scene.Find(North).Key]);
            Assert.Equal(North, after[scene.Find(South).Key]);
            Assert.Contains(result.Stamps, s => s.Key == scene.Find(South).Key && s.PlannerId == "r1");
            Assert.Empty(result.NewHelpers);
            Assert.Empty(result.NewLayers);
            Assert.DoesNotContain(result.NodeMoves, m => m.Key == scene.Find("_07_South_Enabling").Key);

            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void RootRowsGiveLayerOnlyRootsTheirHelpersOnTheMaxLayersOfTheirNames()
        {
            // No root helper: North is a Max layer alone, the scene's one root, and the layer South is at the top beside it.
            var scene = new TestScene();
            scene.Layer("0");
            scene.Layer(North);
            scene.Layer("_01_North_Enabling", North);
            scene.Layer(South);
            var list = TestCsv.List(
                TestCsv.Row("r1", null, North, "Root"),
                TestCsv.Row("g1", "r1", "_01_North_Enabling"),
                TestCsv.Row("r2", null, South, "Root"),
                TestCsv.Row("g7", "r2", "_07_South_Enabling"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);

            // Each root's helper is made on the Max layer of its name, which the scene has: no layer is made for either.
            Assert.DoesNotContain(result.NewLayers, l => l.Name == North || l.Name == South);
            Assert.Contains(result.NewHelpers, h => h.Name == North && h.ParentKey == null && h.LayerName == North && !h.LayerIsNew && h.PlannerId == "r1");
            Assert.Contains(result.NewHelpers, h => h.Name == South && h.ParentKey == null && h.LayerName == South && !h.LayerIsNew && h.PlannerId == "r2");
            Assert.Contains(result.NewLayers, l => l.Name == "_07_South_Enabling" && l.ParentName == South);
            var run = Run(plan, result, scene.Snapshot);
            Assert.Equal(new[] { North, South }, new PlannerSceneModel(run.Scene).PhasingRoots.Select(r => r.Name).ToArray());
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);

            // A list that names South alone: North, a Max layer alone, gets no helper, and is listed as not in the list.
            var southOnly = TestCsv.List(
                TestCsv.Row("r2", null, South, "Root"),
                TestCsv.Row("g7", "r2", "_07_South_Enabling"));
            var only = Plan(scene.Snapshot, southOnly).Resolve(null);
            Assert.DoesNotContain(only.NewHelpers, h => h.Name == North);
            Assert.Contains(only.NewHelpers, h => h.Name == South && h.LayerName == South && !h.LayerIsNew);
            Assert.Equal(PlannerCleanUpStatus.Check, only.Rows.Single(r => r.Item == "Phasing root" && r.NewName == North).Status);
        }

        [Fact]
        public void ATopLevelRowGoesIntoTheFirstRootTheListNamesNotAnUnlistedOneThatSortsFirst()
        {
            // A backup copy sorts first by name, but no Root row names it: a new top-level row goes into North.
            var scene = TwoRoots();
            scene.Unit("Old_Phasing_copy", null, null);
            var list = TestCsv.List(
                TestCsv.Row("r1", null, North, "Root"),
                TestCsv.Row("g1", "r1", "_01_North_Enabling"),
                TestCsv.Row("g2", "r1", "_02_North_Roads"),
                TestCsv.Row("r2", null, South, "Root"),
                TestCsv.Row("g7", "r2", "_07_South_Enabling"),
                TestCsv.Row("g8", null, "_08_New_Works"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);

            Assert.Contains(result.NewHelpers, h => h.Name == "_08_New_Works" && h.ParentKey == scene.Find(North).Key);
            Assert.Contains(result.NewLayers, l => l.Name == "_08_New_Works" && l.ParentName == North);
            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Two roots that share layer IDs (R23, R24)

        // North and South both have a layer 01_01; each has an object whose tag names 01_01, its own root's.
        private static TestScene SharedIds()
        {
            var scene = TwoRoots();
            Layer(scene, "_01_North_Enabling", "01_01_Drainage", "IN_Pipe");
            Layer(scene, "_01_North_Enabling", "01_02_Kerbs", "IN_Kerb_RM_01_01");
            Layer(scene, "_07_South_Enabling", "01_01_Fence", "IN_Fence");
            Layer(scene, "_07_South_Enabling", "07_02_Posts", "IN_Post_RM_01_01");
            return scene;
        }

        [Fact]
        public void TwoRootsThatShareALayerIdAreAskedWhichKeepsItAndNothingIsRenumberedUntilAnswered()
        {
            var scene = SharedIds();
            var plan = Plan(scene.Snapshot);
            var question = plan.Questions.First();
            Assert.Equal(PlannerCleanUpCase.SharedIds, question.Case);
            Assert.Equal(new[] { "01_01" }, question.SharedLayerIds.ToArray());
            Assert.Equal(new[] { scene.Find(North).Key, scene.Find(South).Key }, question.RootKeys);
            Assert.Contains("which root keeps them?", question.Why);
            Assert.Equal(new[] { PlannerCleanUpOption.KeepPrefix + scene.Find(North).Key, PlannerCleanUpOption.KeepPrefix + scene.Find(South).Key },
                question.Options.Select(o => o.Id).ToArray());

            var open = plan.Resolve(null);
            Assert.Equal(1, open.Open);
            Assert.DoesNotContain(open.NodeRenames, r => r.Key == scene.Find("01_01_Fence").Key || r.Key == scene.Find("01_01_Drainage").Key);
            Assert.Contains(open.Rows, r => r.Status == PlannerCleanUpStatus.Choose && r.Item == "Phasing roots" && r.QuestionId == question.Id);
            Assert.Contains(open.Rows, r => r.NodeKey == scene.Find("01_01_Fence").Key && r.Why.Contains("shares its layer ID 01_01 with 01_01_Drainage in " + North));

            var left = plan.Resolve(new Dictionary<string, string> { { question.Id, PlannerCleanUpOption.Leave } });
            Assert.Equal(0, left.Open);
            Assert.Equal(1, left.Left);
            Assert.DoesNotContain(left.NodeRenames, r => r.Key == scene.Find("01_01_Fence").Key);
        }

        [Fact]
        public void TheRootThatGivesUpASharedIdIsRenumberedPastEveryStageInUseAndItsOwnTagsFollow()
        {
            var scene = SharedIds();
            var plan = Plan(scene.Snapshot);
            var question = plan.Questions.First();

            // North keeps 01_01: South's 01_01 takes the next free stage of phase 1, past 01_02, and South's tag follows it.
            var keepNorth = new Dictionary<string, string> { { question.Id, PlannerCleanUpOption.KeepPrefix + scene.Find(North).Key } };
            var result = plan.Resolve(keepNorth);
            var after = After(scene.Snapshot, result);
            Assert.Equal("01_03_Fence", after[scene.Find("01_01_Fence").Key]);
            Assert.Contains(result.MaxLayerRenames, r => r.OldName == "01_01_Fence" && r.NewName == "01_03_Fence");
            Assert.Equal("IN_Post_RM_01_03", after[scene.Find("IN_Post_RM_01_01").Key]);
            Assert.Equal("01_01_Drainage", after[scene.Find("01_01_Drainage").Key]);
            Assert.Equal("IN_Kerb_RM_01_01", after[scene.Find("IN_Kerb_RM_01_01").Key]);
            Assert.Contains(result.Rows, r => r.NodeKey == scene.Find("01_01_Fence").Key && r.Why.Contains("renumbered from 01_01: " + North + " keeps that layer ID, as answered"));
            Assert.Equal(0, result.Open);
            var run = Run(plan, result, scene.Snapshot);
            var again = Plan(run.Scene);
            Assert.DoesNotContain(again.Questions, q => q.Case == PlannerCleanUpCase.SharedIds);
            Assert.False(again.Resolve(null).HasChanges);

            // South keeps it instead: North's layer and North's tag move.
            var keepSouth = plan.Resolve(new Dictionary<string, string> { { question.Id, PlannerCleanUpOption.KeepPrefix + scene.Find(South).Key } });
            var other = After(scene.Snapshot, keepSouth);
            Assert.Equal("01_03_Drainage", other[scene.Find("01_01_Drainage").Key]);
            Assert.Equal("IN_Kerb_RM_01_03", other[scene.Find("IN_Kerb_RM_01_01").Key]);
            Assert.Equal("01_01_Fence", other[scene.Find("01_01_Fence").Key]);
            Assert.Equal("IN_Post_RM_01_01", other[scene.Find("IN_Post_RM_01_01").Key]);
            Run(plan, keepSouth, scene.Snapshot);
        }

        [Fact]
        public void AStageGivenUpIsRenumberedWholeWithItsSubStages()
        {
            // South's stage 01_01 has a sub-stage, 01_01_2: both take the next free stage, as one.
            var scene = SharedIds();
            Layer(scene, "_07_South_Enabling", "01_01_2_Gate", "IN_Gate");
            var plan = Plan(scene.Snapshot);
            var question = plan.Questions.First(q => q.Case == PlannerCleanUpCase.SharedIds);
            Assert.Equal(new[] { "01_01" }, question.SharedLayerIds.ToArray());
            var result = plan.Resolve(new Dictionary<string, string> { { question.Id, PlannerCleanUpOption.KeepPrefix + scene.Find(North).Key } });
            var after = After(scene.Snapshot, result);

            Assert.Equal("01_03_Fence", after[scene.Find("01_01_Fence").Key]);
            Assert.Equal("01_03_2_Gate", after[scene.Find("01_01_2_Gate").Key]);
            Assert.Equal("01_01_Drainage", after[scene.Find("01_01_Drainage").Key]);
            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene).Resolve(null).HasChanges);
        }
    }
}
