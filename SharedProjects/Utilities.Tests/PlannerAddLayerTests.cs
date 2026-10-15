using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Add layer in the Schedule tab, the layers added in Max kept with the scene, and the tab's rows.</summary>
    public class PlannerAddLayerTests
    {
        private static string Compose(PlannerNewLayer spec)
        {
            string problem;
            var name = PlannerAddLayer.Compose(spec, out problem);
            Assert.True(name != null, problem);
            return name;
        }

        private static string Problem(PlannerNewLayer spec)
        {
            string problem;
            Assert.Null(PlannerAddLayer.Compose(spec, out problem));
            return problem;
        }

        private static PlannerNewLayer Layer(int? phase, int stage, int? sub, string name, DateTime? start, DateTime? finish, bool tbc = false)
        {
            return new PlannerNewLayer { Kind = PlannerLayerKind.Layer, Phase = phase, Stage = stage, SubStage = sub, Name = name, Start = start, Finish = finish, Tbc = tbc };
        }

        private static readonly DateTime Oct5 = new DateTime(2026, 10, 5);
        private static readonly DateTime Oct9 = new DateTime(2026, 10, 9);

        [Fact]
        public void TheNameIsComposedInTheConventionAndReadsBack()
        {
            Assert.Equal("02_01_3_Barrier_works_05-10-26_09-10-26", Compose(Layer(2, 1, 3, "  Barrier   works ", Oct5, Oct9)));
            Assert.Equal("02_01_3_Barrier_05-10-26", Compose(Layer(2, 1, 3, "Barrier", Oct5, Oct5)));
            Assert.Equal("02_01_Barrier_05-10-26", Compose(Layer(2, 1, null, "Barrier", Oct5, null)));
            Assert.Equal("02_01_Barrier_TBC", Compose(Layer(2, 1, null, "Barrier", null, null, true)));
            Assert.Equal("02_01_Barrier_TBC_05-10-26_09-10-26", Compose(Layer(2, 1, null, "Barrier", Oct5, Oct9, true)));
            Assert.Equal("06_Hoarding_05-10-26", Compose(Layer(null, 6, null, "Hoarding", Oct5, null)));
            Assert.Equal("_03_Cardington_St", Compose(new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 3, Name = "Cardington St" }));
            // A group without a phase would stay in Max only: the Planner makes folders only for phase groups and layers.
            Assert.Contains("a group needs its phase", Problem(new PlannerNewLayer { Kind = PlannerLayerKind.Group, Name = "Enabling Works" }));

            Assert.Contains("cannot carry a sub-stage", Problem(Layer(null, 6, 2, "Hoarding", Oct5, null)));
            Assert.Contains("mark it TBC", Problem(Layer(2, 1, null, "Barrier", null, null)));
            Assert.Contains("finish is before its start", Problem(Layer(2, 1, null, "Barrier", Oct9, Oct5)));
            // A date in the name stays part of it, as the Planner reads it; a single day is then written twice, as the
            // Planner's list writes it, so that it does not read as a range with the name's date.
            Assert.Equal("02_01_Works_01-02-26_05-10-26_09-10-26", Compose(Layer(2, 1, null, "Works 01-02-26", Oct5, Oct9)));
            Assert.Equal("02_01_Works_01-02-26_05-10-26_05-10-26", Compose(Layer(2, 1, null, "Works 01-02-26", Oct5, null)));
            Assert.Contains("must not hold TBC", Problem(Layer(2, 1, null, "TBC works", Oct5, Oct9)));
            Assert.Contains("give it a name", Problem(Layer(2, 1, null, "   ", Oct5, Oct9)));
            Assert.Contains("a group needs its phase", Problem(new PlannerNewLayer { Kind = PlannerLayerKind.Group, Name = "1 Enabling" }));
            Assert.Equal("_01_1_Enabling", Compose(new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 1, Name = "1 Enabling" }));
            Assert.Contains("one or two digits", Problem(Layer(2, 100, null, "X", Oct5, null)));
            Assert.Contains("one or two digits", Problem(new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 100, Name = "Zone" }));
        }

        [Fact]
        public void TheLayerAndItsHelperAreBuiltUnderTheParentWithBuildsOwnBatch()
        {
            var scene = PlannerScheduleTests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_01_Zone", Layer(1, 5, null, "Gates", Oct5, Oct9), null, null);
            Assert.True(plan.CanApply, plan.Problem);
            Assert.Equal("01_05_Gates_05-10-26_09-10-26", plan.LayerName);
            Assert.Equal("_01_Zone", plan.ParentName);
            var row = Assert.Single(plan.Review);
            Assert.Equal("New", row.Status);
            Assert.Equal("under _01_Zone", row.To);
            Assert.Equal("01_05", plan.Added.Id);
            Assert.Equal("_01_Zone", plan.Added.ParentLayer);
            Assert.Equal("2026-10-05", plan.Added.Start);

            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            Assert.Equal("_01_Zone", after.Layers.Single(l => l.Name == plan.LayerName).ParentName);
            var helper = after.Nodes.Single(n => n.Name == plan.LayerName);
            Assert.Equal(plan.LayerName, helper.LayerName);
            Assert.Equal("_01_Zone", after.Nodes.Single(n => n.Key == helper.ParentKey).Name);
            Assert.Null(helper.PlannerId);
            Assert.Equal(scene.Snapshot.Nodes.Count + 1, after.Nodes.Count);
            Assert.Equal(scene.Snapshot.Layers.Count + 1, after.Layers.Count);
            // Nothing else moves, and the script makes just these two.
            var batch = PlannerLayerScript.Build(plan.Build, "__t_");
            Assert.Equal(1, batch.LayerCreates);
            Assert.Equal(1, batch.HelperCreates);
            Assert.Equal(0, batch.LayerRenames + batch.LayerMoves + batch.HelperRenames + batch.HelperMoves + batch.HelperPuts + batch.Stamps);

            // Under the root itself, and with no root at all (the root is made too).
            var top = PlannerAddLayer.Plan(scene.Snapshot, null, new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 4, Name = "Night works" }, null, null);
            Assert.True(top.CanApply, top.Problem);
            Assert.Equal("Work_Phasing", top.ParentName);
            Assert.Equal(string.Empty, top.Added.ParentLayer);
            var bare = new TestScene();
            bare.Layer("0");
            var fresh = PlannerAddLayer.Plan(bare.Snapshot, null, Layer(1, 1, null, "Setup", Oct5, null), null, null);
            Assert.True(fresh.CanApply, fresh.Problem);
            Assert.Equal(2, fresh.Review.Count);
            Assert.Equal("Phasing root", fresh.Review[0].Item);
            var built = TestSimulator.Apply(bare.Snapshot, fresh.Build);
            Assert.Equal(PlannerSceneModel.DefaultRootName, built.Layers.Single(l => l.Name == "01_01_Setup_05-10-26").ParentName);
        }

        [Fact]
        public void ANameOrIdAlreadyUsedIsRefused()
        {
            var scene = PlannerScheduleTests.Scene();
            Func<PlannerNewLayer, string, PlannerLayerList, IEnumerable<PlannerAddedLayer>, string> problem = (spec, parent, rowsList, before) =>
                PlannerAddLayer.Plan(scene.Snapshot, parent, spec, rowsList, before).Problem;
            Assert.Contains("has the ID 01_02 already", problem(Layer(1, 2, null, "Other", Oct5, null), "_01_Zone", null, null));
            Assert.Contains("a Max layer called _02_Zone", problem(new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 2, Name = "zone" }, null, null, null));
            scene.Object("01_07_Steps_05-10-26", null, "0");
            Assert.Contains("a node of the scene is called", problem(Layer(1, 7, null, "Steps", Oct5, null), "_01_Zone", null, null));
            var list = TestCsv.List(TestCsv.Row("g1", null, "_05_Later"), TestCsv.Row("l1", "g1", "05_01_Later_01-02-27"));
            Assert.Contains("the Planner's list has the ID 05_01 already", problem(Layer(5, 1, null, "Sooner", Oct5, null), "_01_Zone", list, null));
            // A layer added before that the scene still has (here moved outside the phasing root) keeps its ID; one the scene
            // no longer has (taken back with Edit > Undo, deleted) does not.
            var added = new[] { new PlannerAddedLayer { Layer = "01_08_Ramp_05-10-26", Kind = "Layer" } };
            Assert.Null(problem(Layer(1, 8, null, "Ramp_2", Oct5, null), "_01_Zone", null, added));
            scene.Layer("01_08_Ramp_05-10-26");
            Assert.Contains("added in Max before", problem(Layer(1, 8, null, "Ramp_2", Oct5, null), "_01_Zone", null, added));
            // A parent with no helper of its name.
            scene.Layer("01_11_Bare_01-10-26", "_01_Zone");
            Assert.Contains("has no helper of its name", problem(Layer(1, 12, null, "Under", Oct5, null), "01_11_Bare_01-10-26", null, null));
        }

        [Fact]
        public void ALayerGoesOnlyUnderThePhasingRootOrAPhaseGroup()
        {
            // The Planner files every layer in the phase group above it (or at the top): under a layer it would come back
            // beside it, and the next list would move it.
            var scene = PlannerScheduleTests.Scene();
            var under = PlannerAddLayer.Plan(scene.Snapshot, PlannerScheduleTests.Strip, Layer(1, 5, null, "Gates", Oct5, Oct9), null, null);
            Assert.False(under.CanApply);
            Assert.Contains("the Planner files a layer in the phase group above it", under.Problem);
            Assert.Contains("choose _01_Zone's row", under.Problem);
            var group = PlannerAddLayer.Plan(scene.Snapshot, PlannerScheduleTests.Strip, new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 7, Name = "Inner" }, null, null);
            Assert.False(group.CanApply);
            Assert.Null(PlannerAddLayer.ParentProblem(null, null));
            Assert.Null(PlannerAddLayer.ParentProblem("_01_Zone", null));
            Assert.Equal("_01_Zone", PlannerAddLayer.PhaseGroupAbove(new PlannerSceneModel(scene.Snapshot), PlannerScheduleTests.Strip));
            // A phase group in a phase group is read as one by the Planner.
            Assert.True(PlannerAddLayer.Plan(scene.Snapshot, "_01_Zone", new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = 7, Name = "Inner" }, null, null).CanApply);
        }

        [Fact]
        public void AGroupOfAPhaseNumberAlreadyThereIsRefused()
        {
            // Build takes a phase group for the list's group of the same phase number (a group renamed in the Planner),
            // so a second group of a phase would be renamed into the first.
            var scene = PlannerScheduleTests.Scene();
            Func<int, PlannerLayerList, string> problem = (phase, planner) =>
                PlannerAddLayer.Plan(scene.Snapshot, null, new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = phase, Name = "Max Temporary Works" }, planner, null).Problem;
            Assert.Contains("the phasing tree has a group of phase 1 already (_01_Zone)", problem(1, null));
            var list = TestCsv.List(TestCsv.Row("g5", null, "_05_Zone5_Planner"), TestCsv.Row("l5", "g5", "05_01_Later_01-02-27"));
            Assert.Contains("the Planner's list has a group of phase 5 already (_05_Zone5_Planner)", problem(5, list));
            Assert.Null(problem(6, list));
        }

        [Fact]
        public void AnAddedLayerTakenBackIsNoLongerAdded()
        {
            // The scene is the truth: a layer added in Max and taken back with Edit > Undo (or deleted) is not added any
            // more, and its ID can be used again, with the same name or a corrected one.
            var scene = PlannerScheduleTests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 3, null, "New Wrok", new DateTime(2026, 9, 20), new DateTime(2026, 9, 22)), null, null);
            Assert.True(plan.CanApply, plan.Problem);
            var added = new List<PlannerAddedLayer> { plan.Added };
            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            Assert.Single(PlannerAddedLayerStore.Present(added, new PlannerSceneModel(after)));
            var undone = new PlannerSceneModel(scene.Snapshot);
            Assert.Empty(PlannerAddedLayerStore.Present(added, undone));
            Assert.Empty(PlannerAddedLayerStore.Prune(added, undone, null));
            Assert.True(PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 3, null, "New Work", new DateTime(2026, 9, 20), new DateTime(2026, 9, 22)), null, added).CanApply);
            Assert.True(PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 3, null, "New Wrok", new DateTime(2026, 9, 20), new DateTime(2026, 9, 22)), null, added).CanApply);
            // Still there: shown as added until a list from the Planner has it.
            var tree = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(after), PlannerAddedLayerStore.Prune(added, new PlannerSceneModel(after), null), null);
            Assert.Equal(PlannerScheduleRows.AddedStatus, tree.Single(r => r.Source == PlannerScheduleRowSource.Added).Status);
        }

        [Fact]
        public void AnAddedLayerIsAdoptedByTheNextListFromThePlanner()
        {
            var scene = PlannerScheduleTests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_01_Zone", Layer(1, 5, null, "Gates", Oct5, Oct9), null, null);
            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            var added = new List<PlannerAddedLayer> { plan.Added };

            // Kept with the scene.
            var stored = PlannerAddedLayerStore.Write(added);
            var read = PlannerAddedLayerStore.Read(stored);
            Assert.Equal(PlannerAddedLayerStore.Write(read), stored);
            Assert.Equal("01_05_Gates_05-10-26_09-10-26", read.Single().Layer);
            Assert.Empty(PlannerAddedLayerStore.Read("something else"));

            // A list without it: listed as not in the list by the build (never deleted), and still added.
            var rows = new List<string[]>
            {
                TestCsv.Row("g1", null, "_01_Zone"),
                TestCsv.Row("l1", "g1", PlannerScheduleTests.Strip),
                TestCsv.Row("l2", "g1", PlannerScheduleTests.Undated),
                TestCsv.Row("l3", "g1", PlannerScheduleTests.Empty),
                TestCsv.Row("g2", null, "_02_Zone"),
                TestCsv.Row("l4", "g2", PlannerScheduleTests.Reinstate)
            };
            var withoutList = TestCsv.List(rows.ToArray());
            var without = PlannerLayerPlanner.Plan(withoutList, after);
            Assert.Contains(without.NotInList, n => n.Name == plan.LayerName);
            Assert.Single(PlannerAddedLayerStore.StillAdded(added, without));
            Assert.Empty(PlannerScheduleRows.Unlisted(without, added, null));
            var tree = PlannerScheduleRows.Tree(withoutList, without, new PlannerSceneModel(after), added, null);
            var addedRow = tree.Single(r => r.Source == PlannerScheduleRowSource.Added);
            Assert.Equal(PlannerScheduleRows.AddedStatus, addedRow.Status);
            Assert.Equal(1, addedRow.Depth);
            Assert.Equal("_01_Zone", tree[tree.IndexOf(addedRow) - 4].Name);

            // The next list from the Planner has it (Generate from model read it from the GLB): found by its layer ID,
            // given its Planner ID, nothing new or listed; it is the Planner's from then on.
            rows.Add(TestCsv.Row("l5", "g1", plan.LayerName));
            var with = PlannerLayerPlanner.Plan(TestCsv.List(rows.ToArray()), after);
            var adopted = with.Rows.Single(r => r.Row.PlannerId == "l5");
            Assert.Equal(PlannerMatchKind.LayerId, adopted.MatchedBy);
            Assert.Equal("Up to date", adopted.StatusText);
            Assert.Equal("l5", adopted.Helper.StampId);
            Assert.Equal(0, with.NewCount);
            Assert.Empty(with.NotInList);
            Assert.Empty(PlannerAddedLayerStore.StillAdded(added, with));
        }

        [Fact]
        public void TheRowsCarryEachLayersInAndRm()
        {
            var scene = PlannerScheduleTests.Scene();
            var model = new PlannerSceneModel(scene.Snapshot);
            var schedule = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            // No list: the scene's phasing tree, nested as its layers.
            var tree = PlannerScheduleRows.Tree(null, null, model, null, schedule);
            Assert.Equal(new[] { "_01_Zone", PlannerScheduleTests.Strip, PlannerScheduleTests.Undated, PlannerScheduleTests.Empty, "_02_Zone", PlannerScheduleTests.Reinstate },
                tree.Select(r => r.Name).ToArray());
            Assert.Equal(new[] { 0, 1, 1, 1, 0, 1 }, tree.Select(r => r.Depth).ToArray());
            var strip = tree.Single(r => r.Name == PlannerScheduleTests.Strip);
            Assert.Equal(4, strip.RmCount);
            Assert.Equal(0, strip.InCount);
            Assert.True(strip.Editable);
            Assert.False(tree.Single(r => r.Name == "_01_Zone").Editable);
            Assert.Equal(4, tree.Single(r => r.Name == PlannerScheduleTests.Reinstate).InCount);
            Assert.Equal(0, tree.Single(r => r.Name == PlannerScheduleTests.Empty).InCount);

            // Work no dated layer places, and dated groups no row stands for.
            scene.Object("Ph1_St02_IN_Nobody_Ph9_St01_RM", scene.Find(PlannerScheduleTests.Strip), PlannerScheduleTests.Strip);
            var outside = scene.Helper("03_01_Elsewhere_01-11-26", null, "0");
            scene.Object("Ph3_St01_IN_Far", outside, "0");
            var schedule2 = PlannerSchedule.Build(scene.Snapshot, PlannerScheduleTests.Utc);
            var tree2 = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(scene.Snapshot), null, schedule2);
            var extra = PlannerScheduleRows.Extra(tree2, schedule2);
            Assert.Equal(new[] { PlannerScheduleRowSource.Outside, PlannerScheduleRowSource.ByRule }, extra.Select(r => r.Source).ToArray());
            Assert.Equal(1, extra[0].InCount);
            Assert.Equal(1, extra[1].RmCount);
            // Parked the day after the last dated layer ends, here the group outside the tree (1 Nov).
            Assert.Equal("2026-11-02", extra[1].Start);
        }
    }
}
