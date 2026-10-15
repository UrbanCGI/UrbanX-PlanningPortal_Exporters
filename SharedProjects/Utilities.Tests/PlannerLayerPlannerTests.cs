using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Build layers and helpers: matching the Planner's list to the scene, and the change plan.</summary>
    public class PlannerLayerPlannerTests
    {
        private const string Zone = "_01_Zone5_Working";
        private const string Drainage = "01_01_Drainage_TBC_08-06-26_03-07-26";
        private const string Retainment = "01_04_1_Retainment_17-08-26_18-10-26";
        private const string Tm = "_02_Phase1_TM";
        private const string Construction1 = "02_01_1_Construction_31-08-26";
        private const string Construction2 = "02_01_2_Construction_31-08-26_17-10-26";

        private static string[][] Rows()
        {
            return new[]
            {
                TestCsv.Row("g1", null, Zone),
                TestCsv.Row("l11", "g1", Drainage),
                TestCsv.Row("l12", "g1", Retainment),
                TestCsv.Row("g2", null, Tm),
                TestCsv.Row("l21", "g2", Construction1),
                TestCsv.Row("l22", "g2", Construction2)
            };
        }

        private static PlannerLayerList StandardList()
        {
            return TestCsv.List(Rows());
        }

        private static readonly string[] StandardTree = new[]
        {
            Construction1 + " < " + Tm,
            Construction2 + " < " + Tm,
            Drainage + " < " + Zone,
            Retainment + " < " + Zone,
            Tm + " < ROOT",
            Zone + " < ROOT"
        }.OrderBy(t => t, StringComparer.Ordinal).ToArray();

        // The scene a modeller built by hand: a phasing root, the groups and layers with their helpers, and objects.
        private static TestScene HandBuiltScene(string rootName = "HS2_Phasing")
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit(rootName, null, null);
            var zone = scene.Unit(Zone, rootName, root);
            var drainage = scene.Unit(Drainage, Zone, zone);
            scene.Object("Ph1_St01_IN_Pipe", drainage, Drainage);
            var retainment = scene.Unit(Retainment, Zone, zone);
            scene.Object("Ph1_St04_1_IN_Wall", retainment, Retainment);
            var tm = scene.Unit(Tm, rootName, root);
            var c1 = scene.Unit(Construction1, Tm, tm);
            scene.Object("Ph2_St01_1_IN_Barrier", c1, Construction1);
            var c2 = scene.Unit(Construction2, Tm, tm);
            scene.Object("Ph2_St01_2_IN_Barrier", c2, Construction2);
            scene.Layer("Site");
            scene.Object("Existing_Kerb", null, "Site");
            return scene;
        }

        private static PlannerRowPlan RowOf(PlannerLayerPlan plan, string plannerId)
        {
            return plan.Rows.Single(r => r.Row.PlannerId == plannerId);
        }

        private static PlannerSceneSnapshot Build(PlannerLayerList list, PlannerSceneSnapshot scene)
        {
            return TestSimulator.Apply(scene, PlannerLayerPlanner.Plan(list, scene));
        }

        [Fact]
        public void AnEmptySceneGetsTheRootAndEveryLayerAndHelper()
        {
            var empty = new PlannerSceneSnapshot();
            empty.Layers.Add(new PhasingMaxLayer { Name = "0" });
            var plan = PlannerLayerPlanner.Plan(StandardList(), empty);

            Assert.True(plan.RootLayerCreated);
            Assert.True(plan.RootHelperCreated);
            Assert.Equal(PlannerSceneModel.DefaultRootName, plan.RootName);
            Assert.All(plan.Rows, r => Assert.Equal(PlannerRowState.New, r.State));
            Assert.All(plan.Rows, r => Assert.Equal("New", r.StatusText));
            Assert.Equal(6, plan.NewCount);
            Assert.True(plan.HasChanges);
            Assert.Equal("6 new, 0 to change, 0 up to date, 0 problems.", plan.Summary());
            Assert.Equal("Phasing root", plan.Review[0].Item);
            Assert.Equal(7, plan.Review.Count);
            Assert.Contains(plan.Notes, n => n.Contains("no phasing root"));

            var after = TestSimulator.Apply(empty, plan);
            Assert.Equal(StandardTree, TestSimulator.LayerTree(after, "Work_Phasing").ToArray());
            var rootHelper = after.Nodes.Single(n => n.Name == "Work_Phasing");
            Assert.Null(rootHelper.ParentKey);
            Assert.Equal(StandardTree, TestSimulator.HelperTree(after, rootHelper.Key, n => true).ToArray());
            foreach (var helper in after.Nodes.Where(n => n != rootHelper))
            {
                Assert.Equal(helper.Name, helper.LayerName);
                Assert.Equal(StandardList().Rows.Single(r => r.Layer == helper.Name).PlannerId, helper.PlannerId);
            }

            var again = PlannerLayerPlanner.Plan(StandardList(), after);
            Assert.False(again.HasChanges);
            Assert.False(again.RootLayerCreated || again.RootHelperCreated);
            Assert.All(again.Rows, r => Assert.Equal(PlannerRowState.UpToDate, r.State));
            Assert.All(again.Rows, r => Assert.Equal(PlannerMatchKind.PlannerId, r.MatchedBy));
            Assert.Empty(again.NotInList);
            Assert.Empty(again.Review);
        }

        [Fact]
        public void AHandBuiltSceneIsAdoptedByIdAndNameWithoutChanges()
        {
            var scene = HandBuiltScene();
            var plan = PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot);

            Assert.Equal("HS2_Phasing", plan.RootName);
            Assert.False(plan.RootLayerCreated || plan.RootHelperCreated);
            Assert.All(plan.Rows, r => Assert.Equal("Up to date", r.StatusText));
            Assert.Equal(0, plan.NewCount + plan.ChangeCount + plan.RenameCount + plan.MoveCount + plan.ProblemCount);
            Assert.Equal(6, plan.AdoptCount);
            Assert.Equal(PlannerMatchKind.Name, RowOf(plan, "g1").MatchedBy);
            Assert.Equal(PlannerMatchKind.LayerId, RowOf(plan, "l21").MatchedBy);
            Assert.All(plan.Review, r => Assert.Equal("Adopt", r.Status));
            Assert.Empty(plan.NotInList);
            // Only the Planner IDs are written.
            Assert.True(plan.HasChanges);
            Assert.All(plan.BuildLayers, l => Assert.False(l.Create || l.Move || l.Rename));
            Assert.All(plan.BuildHelpers, h => Assert.False(h.Create || h.Move || h.Rename || h.PutOnLayer));

            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.False(PlannerLayerPlanner.Plan(StandardList(), after).HasChanges);
        }

        [Fact]
        public void ReDatedRenamedMovedAndRemovedRowsAreFollowed()
        {
            var built = Build(StandardList(), HandBuiltScene().Snapshot);
            const string redated = "01_01_Drainage_TBC_08-06-26_10-07-26";
            const string renamed = "02_01_1_Construction_of_TM_31-08-26";
            var list = TestCsv.List(
                TestCsv.Row("g1", null, Zone),
                TestCsv.Row("l11", "g1", redated),
                TestCsv.Row("g2", null, Tm),
                TestCsv.Row("l21", "g2", renamed),
                TestCsv.Row("l12", "g2", Retainment));
            var plan = PlannerLayerPlanner.Plan(list, built);

            Assert.Equal("Rename from " + Drainage, RowOf(plan, "l11").StatusText);
            Assert.Equal("Rename from " + Construction1, RowOf(plan, "l21").StatusText);
            Assert.Equal("Move under " + Tm, RowOf(plan, "l12").StatusText);
            Assert.Equal("Up to date", RowOf(plan, "g1").StatusText);
            Assert.Equal("Up to date", RowOf(plan, "g2").StatusText);
            Assert.Equal(2, plan.RenameCount);
            Assert.Equal(1, plan.MoveCount);
            Assert.Equal(0, plan.NewCount);
            Assert.Contains(plan.Review, r => r.Change == "Rename" && r.Name == redated && r.Why == "new dates in the Planner");
            Assert.Contains(plan.Review, r => r.Change == "Rename" && r.Name == renamed && r.Why == "renamed in the Planner");
            Assert.Contains(plan.Review, r => r.Change == "Move" && r.Name == Retainment && r.From == "under " + Zone && r.To == "under " + Tm);
            var gone = Assert.Single(plan.NotInList);
            Assert.Equal(Construction2, gone.Name);
            Assert.Equal("layer and helper", gone.What);
            Assert.Contains("earlier list (Planner ID l22)", gone.Note);
            Assert.Contains("1 phasing layer in the scene is not in the list", plan.Summary());

            var after = TestSimulator.Apply(built, plan);
            // Nothing is deleted: the removed row's layer and helper are still there, with its object.
            Assert.Equal(built.Layers.Count, after.Layers.Count);
            Assert.Equal(built.Nodes.Count, after.Nodes.Count);
            Assert.Contains(after.Layers, l => l.Name == Construction2 && l.ParentName == Tm);
            var wall = after.Nodes.Single(n => n.Name == "Ph1_St04_1_IN_Wall");
            Assert.Equal(Retainment, after.Nodes.Single(n => n.Key == wall.ParentKey).Name);
            Assert.Equal(Tm, after.Layers.Single(l => l.Name == Retainment).ParentName);
            var again = PlannerLayerPlanner.Plan(list, after);
            Assert.False(again.HasChanges);
            Assert.Single(again.NotInList);
        }

        [Fact]
        public void ANameAnotherLayerOrNodeKeepsMakesAProblem()
        {
            var scene = new TestScene(Build(StandardList(), HandBuiltScene().Snapshot));
            scene.Layer("Existing_Site");
            scene.Object("Kerb_Blocks", null, "Site");
            var list = TestCsv.List(
                TestCsv.Row("g1", null, Zone),
                TestCsv.Row("l11", "g1", "Existing_Site", "Layer"),
                TestCsv.Row("l12", "g1", Retainment),
                TestCsv.Row("n1", "g1", "Kerb_Blocks", "Layer"),
                TestCsv.Row("n2", "n1", "01_09_Kerbs_01-02-27"),
                TestCsv.Row("n3", "g1", "hs2_phasing", "Group"),
                TestCsv.Row("g2", null, Tm),
                TestCsv.Row("l21", "g2", Construction1),
                TestCsv.Row("l22", "g2", Construction2));
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);

            Assert.Equal(PlannerRowState.Problem, RowOf(plan, "l11").State);
            Assert.Equal("Problem: the Max layer Existing_Site already has this name and keeps it; left as it is", RowOf(plan, "l11").StatusText);
            Assert.Contains("the node Kerb_Blocks already has this name", RowOf(plan, "n1").Problem);
            Assert.Contains("its parent Kerb_Blocks cannot be built", RowOf(plan, "n2").Problem);
            Assert.Contains("the phasing root layer already has this name", RowOf(plan, "n3").Problem);
            Assert.Equal(4, plan.ProblemCount);
            Assert.Equal(4, plan.Review.Count(r => r.IsProblem));
            // The layer left as it is is not "not in the list": its row is there.
            Assert.Empty(plan.NotInList);
            Assert.False(plan.HasChanges);
        }

        [Fact]
        public void SwappedNamesGoThroughTemporaryNames()
        {
            var built = Build(StandardList(), HandBuiltScene().Snapshot);
            var rows = Rows();
            rows[4] = TestCsv.Row("l21", "g2", Construction2);
            rows[5] = TestCsv.Row("l22", "g2", Construction1);
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows), built);
            Assert.Equal("Rename from " + Construction1, RowOf(plan, "l21").StatusText);
            Assert.Equal("Rename from " + Construction2, RowOf(plan, "l22").StatusText);
            Assert.Equal(0, plan.ProblemCount);
            var after = TestSimulator.Apply(built, plan);
            Assert.Equal("Ph2_St01_1_IN_Barrier", after.Nodes.Single(n => n.ParentKey == after.Nodes.Single(h => h.Name == Construction2).Key).Name);
            var batch = PlannerLayerScript.Build(plan, "__t_");
            Assert.Equal(2, batch.TemporaryNames.Count);
        }

        [Fact]
        public void ALayerWithoutItsHelperOrAHelperWithoutItsLayerIsCompleted()
        {
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            // The zone has its layer but no helper; the TM group has a helper but no layer.
            scene.Layer(Zone, "Work_Phasing");
            var tm = scene.Helper(Tm, root, "Work_Phasing");
            // A helper on the wrong layer.
            scene.Layer(Construction1);
            scene.Helper(Construction1, tm, "0");
            var list = TestCsv.List(TestCsv.Row("g1", null, Zone), TestCsv.Row("g2", null, Tm), TestCsv.Row("l21", "g2", Construction1));
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);

            Assert.Equal("Helper added", RowOf(plan, "g1").StatusText);
            Assert.Equal("Layer added", RowOf(plan, "g2").StatusText);
            // Its layer sits under a layer the build creates: it is moved there, and its helper is put on it.
            Assert.Equal("Move under " + Tm + "; helper put on its layer", RowOf(plan, "l21").StatusText);
            Assert.Contains(plan.Review, r => r.Change == "Add helper" && r.Name == Zone);
            Assert.Contains(plan.Review, r => r.Change == "Add layer" && r.Name == Tm);
            Assert.Contains(plan.Review, r => r.Change == "Put on its layer" && r.From == "layer 0");

            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Equal(Tm, after.Layers.Single(l => l.Name == Construction1).ParentName);
            Assert.Equal(Construction1, after.Nodes.Single(n => n.Name == Construction1).LayerName);
            Assert.Equal(Tm, after.Nodes.Single(n => n.Name == Tm).LayerName);
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);
        }

        [Fact]
        public void ACopyOfAHelperWithTheSamePlannerIdIsListedNotTaken()
        {
            var scene = new TestScene(Build(StandardList(), HandBuiltScene().Snapshot));
            var original = scene.Find(Construction1);
            var copy = scene.Helper(Construction1 + "001", scene.Find("HS2_Phasing"), "HS2_Phasing", original.PlannerId);
            var plan = PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot);
            Assert.Same(original, RowOf(plan, "l21").Unit.Helper);
            Assert.False(plan.HasChanges);
            var listed = Assert.Single(plan.NotInList);
            Assert.Equal(copy.Name, listed.Name);
            Assert.Equal("helper only (no layer)", listed.What);
            Assert.Contains("carries the Planner ID of " + Construction1 + " too", listed.Note);
        }

        [Fact]
        public void AGroupOutsideTheRootIsFoundByNameAndMovedIn()
        {
            var scene = HandBuiltScene();
            var weekend = scene.Unit("_06_Weekend_Closure", null, null);
            scene.Object("Ph6_St01_IN_Sign", weekend, "_06_Weekend_Closure");
            var rows = Rows().ToList();
            rows.Add(TestCsv.Row("g6", null, "_06_Weekend_Closure"));
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), scene.Snapshot);
            var row = RowOf(plan, "g6");
            Assert.Equal(PlannerMatchKind.Name, row.MatchedBy);
            Assert.True(row.FromOutsideRoot);
            Assert.Equal("Move under HS2_Phasing from outside the phasing root", row.StatusText);
            Assert.Contains(plan.Review, r => r.Change == "Move" && r.Name == "_06_Weekend_Closure" && r.Why == "outside the phasing root (found by its name): brought in under HS2_Phasing");
            Assert.Contains(plan.Review, r => r.Status == "Adopt" && r.Name == "_06_Weekend_Closure" && r.Why == "found by its name, outside the phasing root");
            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Equal("HS2_Phasing", after.Layers.Single(l => l.Name == "_06_Weekend_Closure").ParentName);
            Assert.Equal("HS2_Phasing", after.Nodes.Single(n => n.Key == after.Nodes.Single(h => h.Name == "_06_Weekend_Closure").ParentKey).Name);
        }

        [Fact]
        public void AnOldSubStageLayerIsFoundByItsIdAndRenamed()
        {
            var scene = HandBuiltScene();
            var old = "02_01.3_Construction_07-09-26_25-09-26";
            scene.Unit(old, Tm, scene.Find(Tm));
            var rows = Rows().ToList();
            rows.Add(TestCsv.Row("l23", "g2", "02_01_3_Construction_07-09-26_25-09-26"));
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), scene.Snapshot);
            var row = RowOf(plan, "l23");
            Assert.Equal(PlannerMatchKind.LayerId, row.MatchedBy);
            Assert.Equal("Rename from " + old, row.StatusText);
            Assert.Contains(plan.Review, r => r.Change == "Rename" && r.Item == "Layer and helper" && r.Why == "sub-stage written with an underscore (found by its layer ID)");
        }

        [Fact]
        public void AnIdTwoSceneLayersShareIsNotUsedToMatch()
        {
            var scene = HandBuiltScene();
            scene.Unit("02_03_1_A_01-02-27", Tm, scene.Find(Tm));
            scene.Unit("02_03_1_B_01-02-27", Tm, scene.Find(Tm));
            var rows = Rows().ToList();
            rows.Add(TestCsv.Row("l31", "g2", "02_03_1_C_01-02-27"));
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), scene.Snapshot);
            Assert.Equal(PlannerRowState.New, RowOf(plan, "l31").State);
            Assert.Equal(new[] { "02_03_1_A_01-02-27", "02_03_1_B_01-02-27" }, plan.NotInList.Select(n => n.Name).ToArray());
            Assert.All(plan.NotInList, n => Assert.Equal(string.Empty, n.Note));
        }

        [Fact]
        public void TheRootIsFoundAsFixPhasingNamesFindsIt()
        {
            var scene = new TestScene();
            scene.Object("Box001", null, "0");
            var root = scene.Unit("HS2_EUS_HRB_Phasing_Main", null, null);
            scene.Unit("Other_phasing", null, null);
            var plan = PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot);
            Assert.Equal("HS2_EUS_HRB_Phasing_Main", plan.RootName);
            Assert.Equal(root.Key, plan.BuildHelpers[0].Key);

            var layerOnly = new TestScene();
            layerOnly.Layer("Site_Phasing");
            var onlyLayer = PlannerLayerPlanner.Plan(StandardList(), layerOnly.Snapshot);
            Assert.Equal("Site_Phasing", onlyLayer.RootName);
            Assert.False(onlyLayer.RootLayerCreated);
            Assert.True(onlyLayer.RootHelperCreated);
            Assert.Equal("Site_Phasing", onlyLayer.BuildHelpers[0].FinalName);
            Assert.Contains(onlyLayer.Review, r => r.Item == "Phasing root" && r.Change == "Create helper");
            var after = TestSimulator.Apply(layerOnly.Snapshot, onlyLayer);
            Assert.Equal("Site_Phasing", after.Nodes.Single(n => n.Name == "Site_Phasing").LayerName);
            Assert.False(PlannerLayerPlanner.Plan(StandardList(), after).HasChanges);
        }

        [Fact]
        public void ARowNamedLikeTheRootIsTheRoot()
        {
            // The list carries the root itself as its top row: its rows go under the scene's root.
            var rows = new List<string[]> { TestCsv.Row("r", null, "hs2_phasing", "Group") };
            rows.AddRange(Rows().Select(r => r[1].Length == 0 ? new[] { r[0], "r" }.Concat(r.Skip(2)).ToArray() : r));
            var list = TestCsv.List(rows.ToArray());
            var scene = HandBuiltScene();
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);
            var root = RowOf(plan, "r");
            Assert.True(root.IsRoot);
            Assert.Equal("Up to date (the phasing root)", root.StatusText);
            Assert.Equal(0, plan.ChangeCount + plan.NewCount + plan.ProblemCount);
            Assert.Equal("r", plan.BuildHelpers[0].StampId);
            Assert.Contains(plan.Review, r => r.Item == "Phasing root" && r.Change == "Stamp Planner ID");
            Assert.Empty(plan.NotInList);
            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);

            // An empty scene always gets Work_Phasing: a row named like a phasing root is an ordinary group under it.
            var site = rows.ToArray();
            site[0] = TestCsv.Row("r", null, "Site_Phasing", "Group");
            var empty = PlannerLayerPlanner.Plan(TestCsv.List(site), new PlannerSceneSnapshot());
            Assert.Equal(PlannerSceneModel.DefaultRootName, empty.RootName);
            Assert.False(RowOf(empty, "r").IsRoot);
            Assert.Equal(7, empty.NewCount);
            Assert.Equal(1, empty.Review.Count(r => r.Item == "Phasing root"));
            var built = TestSimulator.Apply(new PlannerSceneSnapshot(), empty);
            Assert.Equal("r", built.Nodes.Single(n => n.Name == "Site_Phasing").PlannerId);
            Assert.Equal(new[] { "Site_Phasing < ROOT" }.Concat(StandardTree.Select(t => t.Replace("< ROOT", "< Site_Phasing"))).OrderBy(t => t, StringComparer.Ordinal).ToArray(),
                TestSimulator.LayerTree(built, PlannerSceneModel.DefaultRootName).ToArray());
            Assert.False(PlannerLayerPlanner.Plan(TestCsv.List(site), built).HasChanges);
        }

        [Fact]
        public void ARowWithPhasingInItsNameIsNeverTakenForTheRoot()
        {
            // "Phasing" is a likely word in a group's name (traffic signal phasing): a scene without a root still gets
            // Work_Phasing, with every top-level row under it.
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_01_Zone5_Working"),
                TestCsv.Row("g2", null, "_02_Phase1_TM"),
                TestCsv.Row("g3", null, "_03_Signal_Phasing_Works"),
                TestCsv.Row("l31", "g3", "03_01_Signals_01-02-27"));
            var empty = new PlannerSceneSnapshot();
            var plan = PlannerLayerPlanner.Plan(list, empty);
            Assert.Equal(PlannerSceneModel.DefaultRootName, plan.RootName);
            Assert.All(plan.Rows, r => Assert.False(r.IsRoot));
            Assert.Equal(4, plan.NewCount);
            var after = TestSimulator.Apply(empty, plan);
            Assert.Equal(new[] { "03_01_Signals_01-02-27 < _03_Signal_Phasing_Works", "_01_Zone5_Working < ROOT", "_02_Phase1_TM < ROOT", "_03_Signal_Phasing_Works < ROOT" },
                TestSimulator.LayerTree(after, PlannerSceneModel.DefaultRootName).ToArray());
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);

            // The same with a single top-level row.
            var single = TestCsv.List(TestCsv.Row("g3", null, "_03_Signal_Phasing_Works"), TestCsv.Row("l31", "g3", "03_01_Signals_01-02-27"));
            var one = PlannerLayerPlanner.Plan(single, new PlannerSceneSnapshot());
            Assert.Equal(PlannerSceneModel.DefaultRootName, one.RootName);
            Assert.Equal(2, one.NewCount);
        }

        [Fact]
        public void AGroupRenamedInThePlannerBeforeTheFirstImportIsFoundByItsPhaseNumber()
        {
            // A scene built by hand (no Planner IDs yet); the Planner renamed the group _02_Phase1_TM.
            const string renamed = "_02_Phase1_TM_Works";
            var scene = HandBuiltScene();
            var rows = Rows();
            rows[3] = TestCsv.Row("g2", null, renamed);
            var list = TestCsv.List(rows);
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);
            var row = RowOf(plan, "g2");
            Assert.Equal(PlannerMatchKind.PhaseNumber, row.MatchedBy);
            Assert.Equal("Rename from " + Tm, row.StatusText);
            Assert.Contains(plan.Review, r => r.Change == "Rename" && r.Item == "Layer and helper" && r.Name == renamed && r.Why == "renamed in the Planner (found by its phase number)");
            Assert.Equal("Up to date", RowOf(plan, "l21").StatusText);
            Assert.Equal(0, plan.NewCount + plan.MoveCount + plan.ProblemCount);
            Assert.Equal(1, plan.RenameCount);
            Assert.Empty(plan.NotInList);
            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Equal(scene.Snapshot.Layers.Count, after.Layers.Count);
            Assert.Equal(scene.Snapshot.Nodes.Count, after.Nodes.Count);
            Assert.Equal(renamed, after.Layers.Single(l => l.Name == Construction1).ParentName);
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);

            // Two rows with the phase number: it says nothing, and the rows are new.
            var two = Rows().ToList();
            two[3] = TestCsv.Row("g2", null, renamed);
            two.Add(TestCsv.Row("g3", null, "_02_Phase1_TM_Night"));
            var ambiguous = PlannerLayerPlanner.Plan(TestCsv.List(two.ToArray()), scene.Snapshot);
            Assert.Equal(PlannerRowState.New, RowOf(ambiguous, "g2").State);
            Assert.Equal(PlannerRowState.New, RowOf(ambiguous, "g3").State);
            Assert.Contains(ambiguous.NotInList, n => n.Name == Tm);
        }

        [Fact]
        public void AnOrdinaryLayerOutsideTheRootIsNotTakenForAGroupOfItsName()
        {
            // A context layer at the top, with a Max group of its name and loose survey meshes, and a Planner group of
            // the same name made by hand.
            var scene = HandBuiltScene();
            scene.Layer("Existing_Buildings");
            var group = scene.Node("Existing_Buildings", null, "Existing_Buildings", true);
            group.IsGroupHead = true;
            var building = scene.Object("Building_01", group, "Existing_Buildings");
            building.IsGroupMember = true;
            scene.Object("Survey_0", null, "Existing_Buildings");
            var rows = Rows().ToList();
            rows.Add(TestCsv.Row("g9", null, "Existing_Buildings", "Group"));
            rows.Add(TestCsv.Row("l91", "g9", "01_09_Demolition_01-02-27"));
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), scene.Snapshot);
            var row = RowOf(plan, "g9");
            Assert.Equal(PlannerMatchKind.None, row.MatchedBy);
            Assert.Equal("Problem: the Max layer Existing_Buildings already has this name and keeps it; left as it is", row.StatusText);
            Assert.Contains("its parent Existing_Buildings cannot be built", RowOf(plan, "l91").Problem);
            Assert.DoesNotContain(plan.BuildHelpers, h => h.Key == group.Key);
            Assert.DoesNotContain(plan.BuildLayers, l => l.CurrentName == "Existing_Buildings");
            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Null(after.Layers.Single(l => l.Name == "Existing_Buildings").ParentName);
            Assert.Null(after.Nodes.Single(n => n.Key == group.Key).ParentKey);
            Assert.Null(after.Nodes.Single(n => n.Key == group.Key).PlannerId);
        }

        [Fact]
        public void AHelperWhoseNameDriftedFromItsLayerIsPairedWithItByItsLayerId()
        {
            // Built by hand: the layer was re-dated and its helper was not; neither has a Planner ID.
            const string layerName = "02_01_1_Construction_31-08-26_04-09-26";
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("HS2_Phasing", null, null);
            var tm = scene.Unit(Tm, "HS2_Phasing", root);
            scene.Layer(layerName, Tm);
            var helper = scene.Helper(Construction1, tm, layerName);
            scene.Object("Ph2_St01_1_IN_Barrier", helper, layerName);
            scene.Object("Ph2_St01_1_IN_Kerb", helper, layerName);
            var list = TestCsv.List(TestCsv.Row("g2", null, Tm), TestCsv.Row("l21", "g2", Construction1));
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);
            var row = RowOf(plan, "l21");
            Assert.Equal(PlannerMatchKind.LayerId, row.MatchedBy);
            Assert.Same(helper, row.Unit.Helper);
            Assert.Equal("Rename from " + layerName, row.StatusText);
            Assert.Contains(plan.Review, r => r.Change == "Rename" && r.Item == "Layer" && r.From == layerName && r.To == Construction1);
            Assert.DoesNotContain(plan.Review, r => r.Change == "Rename" && r.Item == "Helper");
            Assert.Equal(0, plan.NewCount + plan.ProblemCount);
            Assert.Empty(plan.NotInList);

            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Equal(scene.Snapshot.Layers.Count, after.Layers.Count);
            Assert.Equal(Construction1, after.Nodes.Single(n => n.Name == "Ph2_St01_1_IN_Barrier").LayerName);
            Assert.Null(CountsOf(after).PairingNote());
            var links = PlannerLinkPlanner.Plan(after);
            Assert.Equal(0, links.ProblemCount);
            Assert.Equal(2, links.AlreadyLinked);
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);

            // New dates in the Planner: the layer and its helper are both renamed, nothing is added.
            const string redated = "02_01_1_Construction_07-09-26_11-09-26";
            var again = PlannerLayerPlanner.Plan(TestCsv.List(TestCsv.Row("g2", null, Tm), TestCsv.Row("l21", "g2", redated)), scene.Snapshot);
            Assert.Equal("Rename from " + layerName, RowOf(again, "l21").StatusText);
            Assert.Contains(again.Review, r => r.Change == "Rename" && r.Item == "Layer" && r.From == layerName && r.To == redated);
            Assert.Contains(again.Review, r => r.Change == "Rename" && r.Item == "Helper" && r.From == Construction1 && r.To == redated);
            Assert.Equal(0, again.NewCount);
            Assert.Empty(again.NotInList);

            // IDs that differ, or two such helpers on one layer: no pairing.
            var other = new TestScene();
            var otherRoot = other.Unit("HS2_Phasing", null, null);
            var otherTm = other.Unit(Tm, "HS2_Phasing", otherRoot);
            other.Layer(layerName, Tm);
            other.Helper("02_01_2_Construction_31-08-26", otherTm, layerName);
            const string third = "02_01_3_Construction_01-10-26_09-10-26";
            other.Layer(third, Tm);
            other.Helper("02_01_3_Construction_01-10-26", otherTm, third);
            other.Helper("02_01_3_Construction_02-10-26", otherTm, third);
            var model = new PlannerSceneModel(other.Snapshot);
            Assert.Null(model.UnitOfLayer(layerName).Helper);
            Assert.Null(model.UnitOfLayer(third).Helper);
        }

        [Fact]
        public void AHelperClonedFromAnotherIsAdoptedForItsOwnLayersRow()
        {
            // After a build, the modeller makes a layer by hand and clones a helper for it: the clone carries the
            // original's Planner ID.
            const string added = "02_01_3_Construction_05-10-26_10-10-26";
            var scene = new TestScene(Build(StandardList(), HandBuiltScene().Snapshot));
            var original = scene.Find(Construction1);
            scene.Layer(added, Tm);
            var clone = scene.Helper(added, scene.Find(Tm), added, original.PlannerId);
            scene.Object("Ph2_St01_3_IN_Barrier", clone, added);

            // Until the Planner has the layer, it is listed as a copy.
            var before = PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot);
            Assert.False(before.HasChanges);
            Assert.Contains("carries the Planner ID of " + Construction1 + " too", Assert.Single(before.NotInList).Note);

            // Once the Planner has it, its row takes it by its layer ID and gives it its own Planner ID.
            var rows = Rows().ToList();
            rows.Add(TestCsv.Row("l23", "g2", added));
            var list = TestCsv.List(rows.ToArray());
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);
            var row = RowOf(plan, "l23");
            Assert.Equal(PlannerMatchKind.LayerId, row.MatchedBy);
            Assert.Equal("Up to date", row.StatusText);
            Assert.Equal("l23", row.Helper.StampId);
            Assert.Same(original, RowOf(plan, "l21").Unit.Helper);
            Assert.Equal("Up to date", RowOf(plan, "l21").StatusText);
            Assert.Equal(0, plan.ProblemCount);
            Assert.Empty(plan.NotInList);
            Assert.Contains(plan.Review, r => r.Status == "Adopt" && r.Name == added && r.Why == "found by its layer ID (it carried the Planner ID l21)");
            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Equal("l23", after.Nodes.Single(n => n.Key == clone.Key).PlannerId);
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);

            // The original re-dated in the Planner, and the clone's layer first in the Layer Manager: the original
            // still keeps its row (its layer ID), and the clone goes to its own.
            var early = Build(StandardList(), HandBuiltScene().Snapshot);
            var earlyScene = new TestScene(early);
            early.Layers.Insert(0, new PhasingMaxLayer { Name = added, ParentName = Tm });
            earlyScene.Helper(added, earlyScene.Find(Tm), added, earlyScene.Find(Construction1).PlannerId);
            const string redated = "02_01_1_Construction_01-09-26";
            var redatedRows = Rows().ToList();
            redatedRows[4] = TestCsv.Row("l21", "g2", redated);
            redatedRows.Add(TestCsv.Row("l23", "g2", added));
            var redatedPlan = PlannerLayerPlanner.Plan(TestCsv.List(redatedRows.ToArray()), early);
            Assert.Equal(Construction1, RowOf(redatedPlan, "l21").Unit.Name);
            Assert.Equal("Rename from " + Construction1, RowOf(redatedPlan, "l21").StatusText);
            Assert.Equal(PlannerMatchKind.LayerId, RowOf(redatedPlan, "l23").MatchedBy);
            Assert.Equal("Up to date", RowOf(redatedPlan, "l23").StatusText);
            Assert.Empty(redatedPlan.NotInList);
        }

        [Fact]
        public void ANameThatStaysAsItIsNeverMakesAProblem()
        {
            // After a build: a mesh with a group's name, and a copy of a helper merged in with its name and Planner ID.
            var scene = new TestScene(Build(StandardList(), HandBuiltScene().Snapshot));
            var original = scene.Find(Construction1);
            scene.Object(Zone, null, "Site");
            scene.Helper(Construction1, scene.Find("HS2_Phasing"), "HS2_Phasing", original.PlannerId);
            var plan = PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot);
            Assert.Equal(0, plan.ProblemCount);
            Assert.All(plan.Rows, r => Assert.Equal("Up to date", r.StatusText));
            Assert.False(plan.HasChanges);
            Assert.Same(original, RowOf(plan, "l21").Unit.Helper);
            Assert.Equal(new[] { "another node has this name too; the build renames neither" }, RowOf(plan, "g1").Notes.ToArray());
            Assert.Equal(new[] { "another node has this name too (a copy of its helper?); the build renames neither" }, RowOf(plan, "l21").Notes.ToArray());
            // One line for all of them in the plan's notes (shown in full), the detail on each row.
            Assert.Contains("2 rows keep a name another node also has (see each row's tooltip); the build renames none of them.", plan.Notes);
            Assert.DoesNotContain(plan.Notes, n => n.StartsWith(Zone + ":", StringComparison.Ordinal));
            Assert.Contains("(a copy?)", Assert.Single(plan.NotInList).Note);

            // Moves still happen; a rename onto a name another node keeps is still a Problem; a node with the name in
            // other capitals is another name.
            scene.Object("02_01_2_Construction_31-08-26_18-10-26", null, "Site");
            scene.Object("_02_phase1_tm_works", null, "Site");
            var rows = Rows();
            rows[3] = TestCsv.Row("g2", null, "_02_Phase1_TM_Works");
            rows[4] = TestCsv.Row("l21", "g1", Construction1);
            rows[5] = TestCsv.Row("l22", "g2", "02_01_2_Construction_31-08-26_18-10-26");
            var moved = PlannerLayerPlanner.Plan(TestCsv.List(rows), scene.Snapshot);
            Assert.Equal("Move under " + Zone, RowOf(moved, "l21").StatusText);
            Assert.Equal("Rename from " + Tm, RowOf(moved, "g2").StatusText);
            Assert.Equal("Problem: the node 02_01_2_Construction_31-08-26_18-10-26 already has this name and keeps it; left as it is", RowOf(moved, "l22").StatusText);
            Assert.Equal(1, moved.ProblemCount);
        }

        [Fact]
        public void RowsWithProblemsAreSkippedButKeepTheirLayers()
        {
            var scene = HandBuiltScene();
            var rows = Rows();
            rows[2] = new[] { "l12", "g1", Retainment, "Folder", "", "", "", "", "", "", "", "", "" };
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows), scene.Snapshot);
            var row = RowOf(plan, "l12");
            Assert.Equal(PlannerRowState.Problem, row.State);
            Assert.StartsWith("Problem: its Kind is \"Folder\"", row.StatusText);
            Assert.NotNull(row.Unit);
            Assert.Empty(plan.NotInList);
            Assert.False(row.Layer.Rename || row.Layer.Move || row.Helper.StampId != null);
        }

        [Fact]
        public void AnUnusableListPlansNothing()
        {
            var plan = PlannerLayerPlanner.Plan(PlannerLayerList.Parse("a,b\r\n1,2\r\n"), HandBuiltScene().Snapshot);
            Assert.Empty(plan.Rows);
            Assert.False(plan.HasChanges);
            Assert.Equal("No usable layer list.", plan.Summary());
            Assert.Contains(plan.Notes, n => n.Contains("Planner ID"));
        }

        [Fact]
        public void TheFingerprintChangesWithTheScene()
        {
            var scene = HandBuiltScene();
            var before = PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot).Fingerprint();
            Assert.Equal(before, PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot).Fingerprint());
            scene.Find(Construction1).Name = "Renamed_by_hand";
            Assert.NotEqual(before, PlannerLayerPlanner.Plan(StandardList(), scene.Snapshot).Fingerprint());
        }

        /// <summary>The phasing counts the export log and Fix phasing names show (layers and helpers in pairs), for a scene.</summary>
        internal static PhasingCounts CountsOf(PlannerSceneSnapshot scene)
        {
            var nodes = scene.Nodes.Select(n => new PhasingSceneNode
            {
                Key = n.Key,
                Name = n.Name,
                ParentKey = n.ParentKey,
                ChildIndex = n.ChildIndex,
                HasChildren = n.HasChildren,
                IsGroupHead = n.IsGroupHead,
                IsGroupMember = n.IsGroupMember,
                IsHelper = n.IsHelper,
                IsShape = n.IsShape
            }).ToList();
            return PhasingNameFixer.Plan(nodes, scene.Layers, null, null).Counts;
        }

        [Fact]
        public void TheExportCountsPairUpAfterABuild()
        {
            // A hand-built scene with a layer missing its helper and a helper missing its layer: the counts do not pair up.
            var scene = HandBuiltScene();
            scene.Snapshot.Nodes.Remove(scene.Find(Drainage));
            scene.Find("Ph1_St01_IN_Pipe").ParentKey = scene.Find(Zone).Key;
            scene.Snapshot.Layers.RemoveAll(l => l.Name == Construction2);
            var before = CountsOf(scene.Snapshot);
            Assert.NotNull(before.PairingNote());

            var after = Build(StandardList(), scene.Snapshot);
            var counts = CountsOf(after);
            Assert.Equal("HS2_Phasing", counts.TopLayerName);
            Assert.Equal(6, counts.Layers);
            Assert.Equal(6, counts.Helpers);
            Assert.Null(counts.PairingNote());
            Assert.Equal(4, counts.TaggedObjects);
        }

        [Fact]
        public void RenameReasonsSayWhatChanged()
        {
            Assert.Equal("new dates in the Planner", PlannerLayerPlanner.RenameReason("02_01_A_01-02-27", "02_01_A_02-02-27", PlannerMatchKind.PlannerId));
            Assert.Equal("marked TBC in the Planner (found by its name)", PlannerLayerPlanner.RenameReason("02_01_A_01-02-27", "02_01_A_TBC_01-02-27", PlannerMatchKind.Name));
            Assert.Equal("capitals as the Planner writes them", PlannerLayerPlanner.RenameReason("02_01_a_01-02-27", "02_01_A_01-02-27", PlannerMatchKind.PlannerId));
            Assert.Equal("renamed in the Planner (found by its layer ID)", PlannerLayerPlanner.RenameReason("02_01_A_01-02-27", "02_01_B_01-02-27", PlannerMatchKind.LayerId));
            Assert.Equal("sub-stage written with an underscore", PlannerLayerPlanner.RenameReason("02_01-2_A_01-02-27", "02_01_2_A_01-02-27", PlannerMatchKind.PlannerId));
        }

        [Fact]
        public void APhaseNumberOnlyFindsAGroupWithoutPlannerIdUniqueInListAndTree()
        {
            // Built from the list: every helper has its Planner ID. The Planner then removes _02_Phase1_TM (g2) and
            // makes a new group of phase 2: the old group is not renamed into it, it is listed.
            var built = Build(StandardList(), HandBuiltScene().Snapshot);
            var rows = Rows().ToList();
            rows[3] = TestCsv.Row("g7", null, "_02_Night_Works");
            rows[4] = TestCsv.Row("l21", "g7", Construction1);
            rows[5] = TestCsv.Row("l22", "g7", Construction2);
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), built);
            Assert.Equal(PlannerRowState.New, RowOf(plan, "g7").State);
            Assert.Contains(plan.NotInList, n => n.Name == Tm && n.Note.Contains("Planner ID g2"));
            Assert.Equal("Move under _02_Night_Works", RowOf(plan, "l21").StatusText);

            // A scene built by hand (no Planner IDs) with a second phase 2 group made in Max: the renamed row finds
            // neither, as the phase number is on two groups of the tree.
            var scene = HandBuiltScene();
            scene.Unit("_02_Extra_Works", "HS2_Phasing", scene.Find("HS2_Phasing"));
            var renamed = Rows();
            renamed[3] = TestCsv.Row("g2", null, "_02_Phase1_TM_Works");
            var twoGroups = PlannerLayerPlanner.Plan(TestCsv.List(renamed), scene.Snapshot);
            Assert.Equal(PlannerRowState.New, RowOf(twoGroups, "g2").State);
            Assert.Contains(twoGroups.NotInList, n => n.Name == "_02_Extra_Works");
            Assert.Contains(twoGroups.NotInList, n => n.Name == Tm);

            // A row of the same phase number elsewhere in the list (already matched by name) also stops it.
            var sameNumber = Rows().ToList();
            sameNumber[3] = TestCsv.Row("g2", null, "_02_Phase1_TM_Works");
            sameNumber.Add(TestCsv.Row("g8", null, "_02_Extra_Works"));
            var withRow = PlannerLayerPlanner.Plan(TestCsv.List(sameNumber.ToArray()), scene.Snapshot);
            Assert.Equal(PlannerMatchKind.Name, RowOf(withRow, "g8").MatchedBy);
            Assert.Equal(PlannerRowState.New, RowOf(withRow, "g2").State);
        }

        [Fact]
        public void AMaxGroupOfObjectsIsNeverPairedAsADriftedHelper()
        {
            // A modeller's group named with the layer's ID sits on the layer, which has no helper of its own.
            const string layerName = "02_01_1_Construction_31-08-26_04-09-26";
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("HS2_Phasing", null, null);
            var tm = scene.Unit(Tm, "HS2_Phasing", root);
            scene.Layer(layerName, Tm);
            var group = scene.Helper("02_01_1_Barriers", tm, layerName);
            group.IsGroupHead = true;
            var member = scene.Object("Ph2_St01_1_IN_Barrier", group, layerName);
            member.IsGroupMember = true;
            var model = new PlannerSceneModel(scene.Snapshot);
            Assert.True(model.IsObjectGroup(group));
            Assert.Null(model.UnitOfLayer(layerName).Helper);
            Assert.DoesNotContain(model.PhasingHelpers, k => k == group.Key);

            // A group head holding only helpers is not a group of objects.
            var helpers = new TestScene();
            var head = helpers.Helper("Points", null, "0");
            head.IsGroupHead = true;
            helpers.Helper("Point001", head, "0").IsGroupMember = true;
            Assert.False(new PlannerSceneModel(helpers.Snapshot).IsObjectGroup(head));
        }

        [Fact]
        public void AFlatSceneWithoutARootStillAdoptsItsLayersAndHelpersByName()
        {
            // Built by hand without a phasing root: a group named as no phasing name, with its helper, and a layer in it.
            var scene = new TestScene();
            scene.Layer("0");
            var enabling = scene.Unit("Enabling_Works", null, null);
            var kerbs = scene.Unit("01_02_Kerbs_01-09-26", "Enabling_Works", enabling);
            scene.Object("Ph1_St02_IN_Kerb", kerbs, "01_02_Kerbs_01-09-26");
            scene.Layer("Survey");
            scene.Object("Survey_Mesh", null, "Survey");
            var list = TestCsv.List(TestCsv.Row("g1", null, "Enabling_Works", "Group"), TestCsv.Row("l1", "g1", "01_02_Kerbs_01-09-26"),
                TestCsv.Row("g2", null, "Survey", "Group"));
            var plan = PlannerLayerPlanner.Plan(list, scene.Snapshot);
            Assert.Equal(PlannerMatchKind.Name, RowOf(plan, "g1").MatchedBy);
            Assert.Equal(PlannerMatchKind.Name, RowOf(plan, "l1").MatchedBy);
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(PlannerSceneModel.DefaultRootName, plan.RootName);
            Assert.Equal(PlannerMatchKind.Name, RowOf(plan, "g2").MatchedBy);
            var after = TestSimulator.Apply(scene.Snapshot, plan);
            Assert.Equal(PlannerSceneModel.DefaultRootName, after.Layers.Single(l => l.Name == "Enabling_Works").ParentName);
            Assert.Equal("Enabling_Works", after.Layers.Single(l => l.Name == "01_02_Kerbs_01-09-26").ParentName);
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);

            // With a root, a layer and its helper of the name outside it are still taken; a bare layer is not.
            var rooted = HandBuiltScene();
            rooted.Unit("Enabling_Works", null, null);
            rooted.Layer("Survey");
            var rows = Rows().ToList();
            rows.Add(TestCsv.Row("g8", null, "Enabling_Works", "Group"));
            rows.Add(TestCsv.Row("g9", null, "Survey", "Group"));
            var rootedPlan = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), rooted.Snapshot);
            Assert.Equal(PlannerMatchKind.Name, RowOf(rootedPlan, "g8").MatchedBy);
            Assert.Equal("Move under HS2_Phasing from outside the phasing root", RowOf(rootedPlan, "g8").StatusText);
            Assert.Equal("Problem: the Max layer Survey already has this name and keeps it; left as it is", RowOf(rootedPlan, "g9").StatusText);
        }

        [Fact]
        public void TheReviewListSavesAsCsv()
        {
            var plan = PlannerLayerPlanner.Plan(StandardList(), new PlannerSceneSnapshot());
            var csv = plan.ReviewCsv();
            Assert.StartsWith("\uFEFF" + PlannerLayerPlan.ReviewCsvHeader + "\r\n", csv);
            var records = PlannerLayerList.ReadRecords(csv.Substring(1), ',');
            Assert.Equal(plan.Review.Count + 1, records.Count);
            Assert.All(records, r => Assert.Equal(7, r.Fields.Count));
        }
    }
}
