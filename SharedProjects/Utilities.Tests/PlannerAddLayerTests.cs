using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// Add layer in the Schedule tab on the new names (no date: D20), the layers added in Max kept with the scene, and the
    /// tab's rows.
    /// </summary>
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

        private static PlannerNewLayer Layer(int? phase, int stage, int? sub, string name)
        {
            return new PlannerNewLayer { Kind = PlannerLayerKind.Layer, Phase = phase, Stage = stage, SubStage = sub, Name = name };
        }

        private static PlannerNewLayer Group(int? phase, string name)
        {
            return new PlannerNewLayer { Kind = PlannerLayerKind.Group, Phase = phase, Name = name };
        }

        [Fact]
        public void TheNameIsTheIdAndTheNameWithNoDate()
        {
            Assert.Equal("02_01_3_Barrier_works", Compose(Layer(2, 1, 3, "  Barrier   works ")));
            Assert.Equal("02_01_Barrier", Compose(Layer(2, 1, null, "Barrier")));
            Assert.Equal("06_05_2_Site_Entrance-Hoarding_Install", Compose(Layer(6, 5, 2, "Site Entrance-Hoarding Install")));
            Assert.Equal("_03_Cardington_St", Compose(Group(3, "Cardington St")));
            Assert.Equal("_01_1_Enabling", Compose(Group(1, "1 Enabling")));
            // Each reads back as the layer meant, with nothing to say about it.
            foreach (var name in new[] { "02_01_3_Barrier_works", "02_01_Barrier", "06_05_2_Site_Entrance-Hoarding_Install" })
            {
                var parsed = PlannerNaming.ParseLayerNameV2(name);
                Assert.Equal(name, parsed.Label);
                Assert.Empty(parsed.Issues);
            }

            // One number is no layer on the new names, and a group needs its phase too.
            Assert.Contains("give it its phase", Problem(Layer(null, 6, null, "Hoarding")));
            Assert.Contains("a group needs its phase", Problem(Group(null, "Enabling Works")));
            // A name led by a number after a two-number ID would be read as part of the ID (spec 2.1's trap).
            Assert.Equal("the name 02_05_3_Storey_Frame does not read back with the ID 02_05 (it reads as 02_05_3): the name must not start with a number",
                Problem(Layer(2, 5, null, "3 Storey Frame")));
            // A number in brackets at the end is read as 3ds Max's copy number and dropped: refused, and said so.
            Assert.Equal("the name must not end in a number in brackets: the Planner reads \"(1)\" at the end of a name as the copy number 3ds Max gives a copied node, and drops it",
                Problem(Layer(2, 5, null, "Hoarding (1)")));
            Assert.Contains("\"(2)\" at the end of a name", Problem(Layer(2, 5, 1, "Kerb(2)")));
            Assert.Equal("02_05_Hoarding_(1)_East", Compose(Layer(2, 5, null, "Hoarding (1) East")));
            // No date and no TBC in a name: the Planner holds every date.
            Assert.Equal("its name must not hold a date or TBC: the Planner holds every date", Problem(Layer(2, 1, null, "Works 01-02-26")));
            Assert.Contains("must not hold a date or TBC", Problem(Layer(2, 1, null, "TBC works")));
            Assert.Contains("give it a name", Problem(Layer(2, 1, null, "   ")));
            Assert.Contains("one or two digits", Problem(Layer(2, 100, null, "X")));
            Assert.Contains("one or two digits", Problem(Layer(2, 1, 0, "X")));
            Assert.Contains("one or two digits", Problem(Group(100, "Zone")));
        }

        [Fact]
        public void TheLayerAndItsHelperAreBuiltUnderTheParentWithTheLayerBuildersBatch()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_01_Zone", Layer(1, 5, null, "Gates"), null, null);
            Assert.True(plan.CanApply, plan.Problem);
            Assert.Equal("01_05_Gates", plan.LayerName);
            Assert.Equal("_01_Zone", plan.ParentName);
            var row = Assert.Single(plan.Review);
            Assert.Equal("New", row.Status);
            Assert.Equal("under _01_Zone", row.To);
            Assert.Contains("even while empty", row.Why);
            Assert.Contains("arrives TBC", row.Why);
            Assert.Equal("01_05", plan.Added.Id);
            Assert.Equal("Gates", plan.Added.Name);
            Assert.Equal("_01_Zone", plan.Added.ParentLayer);

            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            Assert.Equal("_01_Zone", after.Layers.Single(l => l.Name == plan.LayerName).ParentName);
            var helper = after.Nodes.Single(n => n.Name == plan.LayerName);
            Assert.Equal(plan.LayerName, helper.LayerName);
            Assert.Equal("_01_Zone", after.Nodes.Single(n => n.Key == helper.ParentKey).Name);
            Assert.Null(helper.PlannerId);
            Assert.Equal(scene.Snapshot.Nodes.Count + 1, after.Nodes.Count);
            Assert.Equal(scene.Snapshot.Layers.Count + 1, after.Layers.Count);
            // Nothing else moves, and the script makes just these two, with no date anywhere.
            var batch = PlannerLayerScript.Build(plan.Build, "__t_");
            Assert.Equal(1, batch.LayerCreates);
            Assert.Equal(1, batch.HelperCreates);
            Assert.Equal(0, batch.LayerRenames + batch.LayerMoves + batch.HelperRenames + batch.HelperMoves + batch.HelperPuts + batch.Stamps);
            Assert.DoesNotContain("planner_start", batch.Script);
            Assert.DoesNotContain("planner_tbc", batch.Script);
            // The scene reads as the new names with it, and the new layer is a layer node.
            var naming = PlannerSchedule.NamingOfScene(new PlannerScheduleModel(after));
            Assert.Equal(PlannerNamingKind.New, naming.Naming);

            // Under the root itself (the record names the root's Max layer as its parent), and with no root at all (the root
            // is made too).
            var top = PlannerAddLayer.Plan(scene.Snapshot, null, Group(4, "Night works"), null, null);
            Assert.True(top.CanApply, top.Problem);
            Assert.Equal("Work_Phasing", top.ParentName);
            Assert.Equal("Work_Phasing", top.Added.ParentLayer);
            Assert.Equal("Night_works", top.Added.Name);
            var bare = new TestScene();
            bare.Layer("0");
            var fresh = PlannerAddLayer.Plan(bare.Snapshot, null, Layer(1, 1, null, "Setup"), null, null);
            Assert.True(fresh.CanApply, fresh.Problem);
            Assert.Equal(2, fresh.Review.Count);
            Assert.Equal("Phasing root", fresh.Review[0].Item);
            var built = TestSimulator.Apply(bare.Snapshot, fresh.Build);
            Assert.Equal(PlannerSceneModel.DefaultRootName, built.Layers.Single(l => l.Name == "01_01_Setup").ParentName);
            Assert.Equal(PlannerSceneModel.DefaultRootName, fresh.Added.ParentLayer);
        }

        [Fact]
        public void ANameOrIdAlreadyUsedIsRefused()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            Func<PlannerNewLayer, string, PlannerLayerList, IEnumerable<PlannerAddedLayer>, string> problem = (spec, parent, rowsList, before) =>
                PlannerAddLayer.Plan(scene.Snapshot, parent, spec, rowsList, before).Problem;
            Assert.Contains("has the ID 01_02 already", problem(Layer(1, 2, null, "Other"), "_01_Zone", null, null));
            Assert.Contains("a Max layer called _02_Zone", problem(Group(2, "zone"), null, null, null));
            scene.Object("01_07_Steps", null, "0");
            Assert.Contains("a node of the scene is called", problem(Layer(1, 7, null, "Steps"), "_01_Zone", null, null));
            var list = TestCsv.List(TestCsv.Row("g1", null, "_05_Later"), TestCsv.Row("l1", "g1", "05_01_Later"));
            Assert.Contains("the Planner's list has the ID 05_01 already", problem(Layer(5, 1, null, "Sooner"), "_01_Zone", list, null));
            Assert.Contains("the Planner's list has a layer called 05_01_Later already", problem(Layer(5, 1, null, "Later"), "_01_Zone", list, null));
            // A layer added before that the scene still has (here moved outside the phasing root) keeps its ID; one the scene
            // no longer has (taken back with Edit > Undo, deleted) does not.
            var added = new[] { new PlannerAddedLayer { Layer = "01_08_Ramp", Kind = "Layer" } };
            Assert.Null(problem(Layer(1, 8, null, "Ramp_2"), "_01_Zone", null, added));
            scene.Layer("01_08_Ramp");
            Assert.Contains("added in Max before", problem(Layer(1, 8, null, "Ramp_2"), "_01_Zone", null, added));
            // A parent with no helper of its name.
            scene.Layer("_07_Bare", "Work_Phasing");
            Assert.Contains("has no helper of its name", problem(Layer(7, 12, null, "Under"), "_07_Bare", null, null));
        }

        [Fact]
        public void ALayerGoesOnlyUnderThePhasingRootOrAPhaseGroup()
        {
            // The Planner files every layer in the phase group above it (or at the top): under a layer it would come back
            // beside it, and the next list would move it.
            var scene = PlannerScheduleV2Tests.Scene();
            var under = PlannerAddLayer.Plan(scene.Snapshot, "01_01_Strip_Out", Layer(1, 5, null, "Gates"), null, null);
            Assert.False(under.CanApply);
            Assert.Contains("the Planner files a layer in the phase group above it", under.Problem);
            Assert.Contains("add it under _01_Zone or the phasing root instead", under.Problem);
            var group = PlannerAddLayer.Plan(scene.Snapshot, "01_01_Strip_Out", Group(7, "Inner"), null, null);
            Assert.False(group.CanApply);
            Assert.Null(PlannerAddLayer.ParentProblem(null, null));
            Assert.Null(PlannerAddLayer.ParentProblem("_01_Zone", null));
            Assert.Equal("_01_Zone", PlannerAddLayer.PhaseGroupAbove(new PlannerSceneModel(scene.Snapshot), "01_01_Strip_Out"));
            // A phase group in a phase group is read as one by the Planner.
            Assert.True(PlannerAddLayer.Plan(scene.Snapshot, "_01_Zone", Group(7, "Inner"), null, null).CanApply);
        }

        // The test scene with more groups: one in a phase group, phase numbers that sort differently as text (_9, _10), a
        // phase group with no helper, one outside the phasing root, a phase group in a group that is not one, and layers.
        private static TestScene ParentsScene()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            var root = scene.Find("Work_Phasing");
            var zone1 = scene.Find("_01_Zone");
            var inner = scene.Unit("_03_Inner", "_01_Zone", zone1);
            scene.Unit("03_01_In", "_03_Inner", inner);
            scene.Unit("_10_Late", "Work_Phasing", root);
            scene.Unit("_9_Nine", "Work_Phasing", root);
            scene.Layer("_04_Bare", "Work_Phasing");
            scene.Unit("04_01_Under_Bare", "_04_Bare", root);
            scene.Unit("_05_Outside", null, null);
            var extra = scene.Unit("Zone_Extra", "Work_Phasing", root);
            scene.Unit("_06_Deep", "Zone_Extra", extra);
            scene.Unit("09_01_Top", "Work_Phasing", root);
            return scene;
        }

        [Fact]
        public void AddLayerOffersEveryPhaseGroupThatCanTakeALayerInTheTreesOrder()
        {
            // Under offers the phasing root and these: the phase groups of the phasing tree with their Max layer and helper,
            // from the root down (a group's own groups after it), siblings in the Planner's name order (_9 before _10).
            var scene = ParentsScene();
            var model = new PlannerSceneModel(scene.Snapshot);
            var parents = PlannerAddLayer.Parents(model);
            Assert.Equal(new[] { "_01_Zone 0", "_03_Inner 1", "_02_Zone 0", "_9_Nine 0", "_10_Late 0", "_06_Deep 0" },
                parents.Select(p => p.Layer + " " + p.Depth).ToArray());
            // Plan takes each of them, and refuses the ones left out: a phase group with no helper, one outside the phasing
            // root, a group that is not a phase group, a layer.
            int stage = 50;
            foreach (var parent in parents)
            {
                var plan = PlannerAddLayer.Plan(scene.Snapshot, parent.Layer, Layer(7, stage++, null, "Probe"), null, null);
                Assert.True(plan.CanApply, parent.Layer + ": " + plan.Problem);
            }
            Assert.Contains("has no helper of its name", PlannerAddLayer.Plan(scene.Snapshot, "_04_Bare", Layer(7, 60, null, "Probe"), null, null).Problem);
            Assert.Contains("is outside the phasing root", PlannerAddLayer.Plan(scene.Snapshot, "_05_Outside", Layer(7, 61, null, "Probe"), null, null).Problem);
            Assert.Contains("the Planner files a layer in the phase group above it", PlannerAddLayer.Plan(scene.Snapshot, "Zone_Extra", Layer(7, 62, null, "Probe"), null, null).Problem);
            Assert.Contains("the Planner files a layer in the phase group above it", PlannerAddLayer.Plan(scene.Snapshot, "01_02_Kerbs", Layer(7, 63, null, "Probe"), null, null).Problem);
            // A scene with no phasing root yet: only the root, which Add layer makes.
            var bare = new TestScene();
            bare.Layer("0");
            Assert.Empty(PlannerAddLayer.Parents(new PlannerSceneModel(bare.Snapshot)));
        }

        [Fact]
        public void UnderStartsOnTheRowChosenOrThePhaseGroupAboveIt()
        {
            // The tab's chosen row only says where Under starts; the dialog offers the root and every phase group whatever is
            // chosen. A row that cannot take a layer starts it on the phase group above, or the root, with a note saying why.
            var model = new PlannerSceneModel(ParentsScene().Snapshot);
            var parents = PlannerAddLayer.Parents(model);
            Func<string, string> start = chosen =>
            {
                string note;
                var under = PlannerAddLayer.StartUnder(model, parents, chosen, out note);
                return (under ?? "root") + (note != null ? " | " + note : string.Empty);
            };
            // None chosen, the root itself, or a layer the scene does not have: the root, with nothing to say.
            Assert.Equal("root", start(null));
            Assert.Equal("root", start("Work_Phasing"));
            Assert.Equal("root", start("07_07_Gone"));
            // A phase group: itself (any capitals), nested or not.
            Assert.Equal("_01_Zone", start("_01_zone"));
            Assert.Equal("_03_Inner", start("_03_Inner"));
            Assert.Equal("_06_Deep", start("_06_Deep"));
            // A layer: the phase group above it.
            Assert.Equal("_01_Zone | 01_01_Strip_Out is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on _01_Zone.",
                start("01_01_Strip_Out"));
            Assert.Equal("_03_Inner | 03_01_In is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on _03_Inner.",
                start("03_01_In"));
            Assert.Equal("root | 09_01_Top is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on the phasing root.",
                start("09_01_Top"));
            // A group that is not a phase group, a phase group with no helper (and a layer under it), one outside the root.
            Assert.Equal("root | Zone_Extra is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on the phasing root.",
                start("Zone_Extra"));
            Assert.Equal("root | _04_Bare has no helper of its name yet (Clean up scene graph with the Planner's list imported makes it), so Under starts on the phasing root.",
                start("_04_Bare"));
            Assert.Equal("root | 04_01_Under_Bare is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on the phasing root.",
                start("04_01_Under_Bare"));
            Assert.Equal("root | _05_Outside is outside the phasing root, so Under starts on the phasing root.", start("_05_Outside"));
        }

        [Fact]
        public void AGroupOfAPhaseNumberAlreadyThereIsRefused()
        {
            // Clean up scene graph takes a phase group for the list's group of the same phase number (a group renamed in the
            // Planner), so a second group of a phase would be renamed into the first.
            var scene = PlannerScheduleV2Tests.Scene();
            Func<int, PlannerLayerList, string> problem = (phase, planner) =>
                PlannerAddLayer.Plan(scene.Snapshot, null, Group(phase, "Max Temporary Works"), planner, null).Problem;
            Assert.Contains("the phasing tree has a group of phase 1 already (_01_Zone)", problem(1, null));
            var list = TestCsv.List(TestCsv.Row("g5", null, "_05_Zone5_Planner"), TestCsv.Row("l5", "g5", "05_01_Later"));
            Assert.Contains("the Planner's list has a group of phase 5 already (_05_Zone5_Planner)", problem(5, list));
            Assert.Null(problem(6, list));
        }

        [Fact]
        public void AnAddedLayerTakenBackIsNoLongerAdded()
        {
            // The scene is the truth: a layer added in Max and taken back with Edit > Undo (or deleted) is not added any
            // more, and its ID can be used again, with the same name or a corrected one.
            var scene = PlannerScheduleV2Tests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 3, null, "New Wrok"), null, null);
            Assert.True(plan.CanApply, plan.Problem);
            var added = new List<PlannerAddedLayer> { plan.Added };
            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            Assert.Single(PlannerAddedLayerStore.Present(added, new PlannerSceneModel(after)));
            var undone = new PlannerSceneModel(scene.Snapshot);
            Assert.Empty(PlannerAddedLayerStore.Present(added, undone));
            Assert.Empty(PlannerAddedLayerStore.Prune(added, undone, null));
            Assert.True(PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 3, null, "New Work"), null, added).CanApply);
            Assert.True(PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 3, null, "New Wrok"), null, added).CanApply);
            // Still there: shown as added, highlighted TBC (D21), until a list from the Planner has it.
            var tree = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(after), PlannerAddedLayerStore.Prune(added, new PlannerSceneModel(after), null), null,
                PlannerNamingKind.New);
            var addedRow = tree.Single(r => r.Source == PlannerScheduleRowSource.Added);
            Assert.Equal(PlannerScheduleRows.AddedStatus, addedRow.Status);
            Assert.Equal(PlannerRowTbc.NotInList, addedRow.TbcMark);
            Assert.Null(addedRow.StartDate);
            Assert.Equal(PlannerDateSource.None, addedRow.DateSource);
        }

        [Fact]
        public void ALayerTakenBackAndAddedAgainIsRecordedOnceAndRedoKeepsItsRecord()
        {
            // Add layer, Edit > Undo, Add layer again with the same name: one record, not two (the tab adds the new one to the
            // records the scene keeps, any of that name dropped).
            var scene = PlannerScheduleV2Tests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 30, null, "Temporary Works"), null, null);
            var stored = PlannerAddedLayerStore.WithAdded(new List<PlannerAddedLayer>(), plan.Added);
            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            var again = PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 30, null, "Temporary Works"), null, stored);
            Assert.True(again.CanApply, again.Problem);
            stored = PlannerAddedLayerStore.WithAdded(stored, again.Added);
            Assert.Equal("02_30_Temporary_Works", Assert.Single(stored).Layer);
            var tree = PlannerScheduleRows.Tree(null, null, new PlannerSceneModel(after), PlannerAddedLayerStore.Present(stored, new PlannerSceneModel(after)), null, PlannerNamingKind.New);
            Assert.Single(tree, r => r.Source == PlannerScheduleRowSource.Added);

            // Add layer, Edit > Undo, the scene read again, Edit > Redo: the record stays with the scene while its layer is gone
            // (left out of what the tab shows and checks), so the layer is an added layer again after Redo.
            var undone = new PlannerSceneModel(scene.Snapshot);
            Assert.Empty(PlannerAddedLayerStore.Present(stored, undone));
            var keptWhileUndone = PlannerAddedLayerStore.ForScene(stored, undone);
            Assert.Equal("02_30_Temporary_Works", Assert.Single(keptWhileUndone).Layer);
            Assert.Equal("02_30_Temporary_Works", Assert.Single(PlannerAddedLayerStore.Present(keptWhileUndone, new PlannerSceneModel(after))).Layer);
        }

        [Fact]
        public void AddLayerWaitsForCleanUpOnASceneOnTheOldNamesAsItIsNow()
        {
            // D23, read on the scene as it is now: after Clean up and Edit > Undo, the scene is on the old names again even if
            // the tab still shows the cleaned names, and Add layer waits.
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var works = scene.Unit("_02_Works", "Work_Phasing", root);
            var roads = scene.Unit("02_05_Roads_17-10-26", "_02_Works", works);
            scene.Object("Ph2_St05_IN_Road", roads, roads.Name);
            Assert.Equal(PlannerNamingKind.Old, PlannerSchedule.NamingOfScene(new PlannerScheduleModel(scene.Snapshot)).Naming);
            foreach (var parent in new[] { "_02_Works", null })
            {
                var plan = PlannerAddLayer.Plan(scene.Snapshot, parent, Layer(2, 30, null, "Temporary Works"), null, null);
                Assert.False(plan.CanApply);
                Assert.Equal(PlannerAddLayer.OldNamesProblem, plan.Problem);
            }
            var cleaned = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes()).Resolve(null).ApplyTo(scene.Snapshot);
            Assert.True(PlannerAddLayer.Plan(cleaned, "_02_Works", Layer(2, 30, null, "Temporary Works"), null, null).CanApply);
        }

        [Fact]
        public void AnAddedLayerIsAdoptedByTheNextListFromThePlanner()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_01_Zone", Layer(1, 5, null, "Gates"), null, null);
            var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
            var added = new List<PlannerAddedLayer> { plan.Added };

            // Kept with the scene, without dates.
            var stored = PlannerAddedLayerStore.Write(added);
            Assert.StartsWith("PlannerAddedLayers 2\nLayer,Parent layer,Kind,ID,Name,Was layer,Was parent layer,Was ID,Was name\r\n", stored);
            var read = PlannerAddedLayerStore.Read(stored);
            Assert.Equal(PlannerAddedLayerStore.Write(read), stored);
            Assert.Equal("01_05_Gates", read.Single().Layer);
            Assert.Empty(PlannerAddedLayerStore.Read("something else"));

            // A list without it: listed as not in the list (never deleted), still added, and TBC: not in the list.
            var rows = new List<string[]>
            {
                TestCsv.Row("g1", null, "_01_Zone"),
                TestCsv.Row("l1", "g1", "01_01_Strip_Out"),
                TestCsv.Row("l2", "g1", "01_02_Kerbs"),
                TestCsv.Row("g2", null, "_02_Zone"),
                TestCsv.Row("l3", "g2", "02_01_Reinstate")
            };
            var withoutList = TestCsv.List(rows.ToArray());
            var without = PlannerLayerPlanner.Plan(withoutList, after);
            Assert.Contains(without.NotInList, n => n.Name == plan.LayerName);
            Assert.Single(PlannerAddedLayerStore.StillAdded(added, without));
            Assert.Empty(PlannerScheduleRows.Unlisted(without, added, null, PlannerNamingKind.New));
            // The summary counts it, and the line above the list of such layers says where it is instead: they agree.
            Assert.EndsWith("; 1 phasing layer in the scene is not in the list.", without.Summary());
            Assert.Equal("Every phasing layer in the scene is in the Planner's list, apart from 1 layer added in Max, which the tree shows.",
                PlannerScheduleRows.UnlistedText(0, without.NotInList.Count));
            Assert.Equal("Every phasing layer in the scene is in the Planner's list.", PlannerScheduleRows.UnlistedText(0, 0));
            Assert.Equal("In the scene but not in the Planner's list \u2014 left as they are (3), apart from 2 layers added in Max, which the tree shows:", PlannerScheduleRows.UnlistedText(3, 2));
            var tree = PlannerScheduleRows.Tree(withoutList, without, new PlannerSceneModel(after), added, null, PlannerNamingKind.New);
            var addedRow = tree.Single(r => r.Source == PlannerScheduleRowSource.Added);
            Assert.Equal(PlannerScheduleRows.AddedStatus, addedRow.Status);
            Assert.Equal(1, addedRow.Depth);
            Assert.Equal(PlannerRowTbc.NotInList, addedRow.TbcMark);

            // The next list from the Planner has it (Generate from model read it from the GLB): found by its layer ID,
            // given its Planner ID, nothing new or listed; it is the Planner's from then on, TBC as the list says.
            rows.Add(TestCsv.Row("l5", "g1", plan.LayerName));
            var adoptingList = TestCsv.List(rows.ToArray());
            var with = PlannerLayerPlanner.Plan(adoptingList, after);
            var adopted = with.Rows.Single(r => r.Row.PlannerId == "l5");
            Assert.Equal(PlannerMatchKind.LayerId, adopted.MatchedBy);
            Assert.Equal("Up to date", adopted.StatusText);
            Assert.Equal("l5", adopted.Helper.StampId);
            Assert.Equal(0, with.NewCount);
            Assert.Empty(with.NotInList);
            Assert.Empty(PlannerAddedLayerStore.StillAdded(added, with));
        }

        [Fact]
        public void TheTreeSortsItsRowsItselfNeverByTheListsOrderOrWhenALayerWasAdded()
        {
            // D24: the list keeps no particular folder order (its Order column says the opposite of the names here), and layers
            // are added in Max in any order: siblings come in the order the Planner sorts names in, the added ones among them.
            var scene = PlannerScheduleV2Tests.Scene();
            var later = PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", Layer(2, 31, null, "Later"), null, null);
            var after = TestSimulator.Apply(scene.Snapshot, later.Build);
            var earlier = PlannerAddLayer.Plan(after, "_02_Zone", Layer(2, 30, null, "Earlier"), null, new[] { later.Added });
            Assert.True(earlier.CanApply, earlier.Problem);
            after = TestSimulator.Apply(after, earlier.Build);
            var top = PlannerAddLayer.Plan(after, null, Layer(9, 1, null, "Top"), null, new[] { later.Added, earlier.Added });
            after = TestSimulator.Apply(after, top.Build);
            var added = new List<PlannerAddedLayer> { later.Added, earlier.Added, top.Added };
            var model = new PlannerSceneModel(after);
            var list = PlannerLayerList.Parse(TestCsv.Header + "\r\n"
                + "g2,,_02_Zone,Group,,,,,,,,,1\r\n"
                + "l4,g2,02_02_Planned,Layer,02_02,2,2,,,,,,1\r\n"
                + "l3,g2,02_01_Reinstate,Layer,02_01,2,1,,,,,,2\r\n"
                + "g1,,_01_Zone,Group,,,,,,,,,2\r\n"
                + "l2,g1,01_02_Kerbs,Layer,01_02,1,2,,,,,,1\r\n"
                + "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,,,,,2\r\n");
            Assert.Equal(new[] { "_01_Zone", "01_01_Strip_Out", "01_02_Kerbs", "_02_Zone", "02_01_Reinstate", "02_02_Planned" }, list.Outline().Select(r => r.Layer).ToArray());
            var tree = PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, model), model, added, null, PlannerNamingKind.New);
            Assert.Equal(new[] { "_01_Zone", "01_01_Strip_Out", "01_02_Kerbs", "_02_Zone", "02_01_Reinstate", "02_02_Planned", "02_30_Earlier", "02_31_Later", "09_01_Top" },
                tree.Select(r => r.Name).ToArray());
            Assert.Equal(new[] { 0, 1, 1, 0, 1, 1, 1, 1, 0 }, tree.Select(r => r.Depth).ToArray());
            // With no list, the scene's own tree, the added ones among their siblings too.
            var noList = PlannerScheduleRows.Tree(null, null, model, added, null, PlannerNamingKind.New);
            var zone = noList.Single(r => r.Name == "_02_Zone");
            Assert.Equal(new[] { "02_01_Reinstate", "02_30_Earlier", "02_31_Later" },
                noList.Skip(noList.IndexOf(zone) + 1).TakeWhile(r => r.Depth > zone.Depth).Select(r => r.Name).ToArray());
        }

        [Fact]
        public void AnAddLayerRecordOfTheFirstVersionIsStillReadWithoutItsDates()
        {
            // An earlier Add layer kept its dates in the record (version 1): the record reads, its dates passed over (D20), and
            // it is written again as version 2.
            var version1 = "PlannerAddedLayers 1\nLayer,Parent layer,Kind,ID,Name,Start,Finish,TBC,Was layer,Was parent layer,Was ID,Was name\r\n"
                + "02_05_New_Layer,_02_Works,Layer,02_05,New_Layer,2026-10-01,2026-10-05,,02_05_New_Layer_01-10-26_05-10-26,_02_Works,02_05,New_Layer\r\n"
                + "_07_Night,,Group,,Night,,,,,,,\r\n";
            var read = PlannerAddedLayerStore.Read(version1);
            Assert.Equal(2, read.Count);
            Assert.Equal("02_05_New_Layer", read[0].Layer);
            Assert.Equal("_02_Works", read[0].ParentLayer);
            Assert.Equal("02_05", read[0].Id);
            Assert.Equal("New_Layer", read[0].Name);
            Assert.Equal("02_05_New_Layer_01-10-26_05-10-26", read[0].WasLayer);
            Assert.Equal("_02_Works", read[0].WasParentLayer);
            Assert.Equal("02_05", read[0].WasId);
            Assert.Equal("New_Layer", read[0].WasName);
            Assert.Equal("Group", read[1].Kind);
            Assert.Equal(string.Empty, read[1].WasLayer);
            var written = PlannerAddedLayerStore.Write(read);
            Assert.StartsWith("PlannerAddedLayers 2\n", written);
            Assert.DoesNotContain("2026-10-01", written);
            Assert.Equal(written, PlannerAddedLayerStore.Write(PlannerAddedLayerStore.Read(written)));
            // A first-version record written before the Was columns.
            var older = PlannerAddedLayerStore.Read("PlannerAddedLayers 1\nLayer,Parent layer,Kind,ID,Name,Start,Finish,TBC\r\n01_05_Gates_05-10-26,_01_Zone,Layer,01_05,Gates,2026-10-05,,Yes\r\n");
            Assert.Equal("01_05_Gates_05-10-26", Assert.Single(older).Layer);
            Assert.Equal(string.Empty, older[0].WasLayer);
        }

        [Fact]
        public void AnOldNameScenesTreeTakesNoDateFromItsNames()
        {
            // A scene on the old names, as the tab reads it before Clean up (D23): its phasing tree, unfilled (no schedule).
            var scene = PlannerScheduleTests.Scene();
            var model = new PlannerSceneModel(scene.Snapshot);
            // No list: the scene's phasing tree, nested as its layers.
            var tree = PlannerScheduleRows.Tree(null, null, model, null, null, PlannerNamingKind.Old);
            Assert.Equal(new[] { "_01_Zone", PlannerScheduleTests.Strip, PlannerScheduleTests.Undated, PlannerScheduleTests.Empty, "_02_Zone", PlannerScheduleTests.Reinstate },
                tree.Select(r => r.Name).ToArray());
            Assert.Equal(new[] { 0, 1, 1, 1, 0, 1 }, tree.Select(r => r.Depth).ToArray());
            Assert.Equal(new[] { "Group", "Layer", "Layer", "Layer", "Group", "Layer" }, tree.Select(r => r.Kind).ToArray());
            // No date or TBC is read from a Max layer's name, on the old names too (D20): its Start, Finish and TBC cells stay
            // empty (the tab waits for Clean up on such a scene, D23).
            Assert.All(tree, r => Assert.Equal(string.Empty, r.Start + r.Finish));
            Assert.All(tree, r => Assert.False(r.Tbc));
            Assert.Equal("2026-09-01", PlannerNaming.ToIsoDate(PlannerNaming.ParseLayerName(PlannerScheduleTests.Strip).Start));
            // Unfilled, no row has IN or RM; a phase group is never edited (on the old names the tab keeps Add to IN / RM off
            // whatever a row reads as, D23; the new names' rows are in PlannerTagEditTests).
            Assert.False(tree.Single(r => r.Name == "_01_Zone").Editable);
            Assert.All(tree, r => Assert.Equal(0, r.InCount + r.RmCount));
        }

        // Several phasing roots (R15)

        // Two roots, each an area of one schedule with its own phase numbers (R21), South made first: Add layer goes by the
        // roots' names, never the scene's order. North: _01_North_Enabling (01_01_Drainage), _02_North_Roads (02_01_Binder).
        // South: _07_South_Enabling (07_01_Hoarding, and the group _09_South_Inner in it), _08_South_Roads (08_01_Binder),
        // and 07_09_Loose straight under the root. Outside both: the Max layer Context with 05_01_Context_Works.
        private static TestScene TwoRoots()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var south = scene.Unit("Work_Phasing_South", null, null);
            var southEnabling = scene.Unit("_07_South_Enabling", "Work_Phasing_South", south);
            var hoarding = scene.Unit("07_01_Hoarding", "_07_South_Enabling", southEnabling);
            scene.Object("IN_Hoarding_RM_02_01", hoarding, "07_01_Hoarding");
            var inner = scene.Unit("_09_South_Inner", "_07_South_Enabling", southEnabling);
            scene.Unit("09_01_Inner_Works", "_09_South_Inner", inner);
            var southRoads = scene.Unit("_08_South_Roads", "Work_Phasing_South", south);
            var southBinder = scene.Unit("08_01_Binder", "_08_South_Roads", southRoads);
            scene.Object("IN_Binder_02", southBinder, "08_01_Binder");
            scene.Unit("07_09_Loose", "Work_Phasing_South", south);
            var north = scene.Unit("Work_Phasing_North", null, null);
            var northEnabling = scene.Unit("_01_North_Enabling", "Work_Phasing_North", north);
            var drainage = scene.Unit("01_01_Drainage", "_01_North_Enabling", northEnabling);
            scene.Object("IN_Pipe", drainage, "01_01_Drainage");
            var northRoads = scene.Unit("_02_North_Roads", "Work_Phasing_North", north);
            var northBinder = scene.Unit("02_01_Binder", "_02_North_Roads", northRoads);
            scene.Object("IN_Binder", northBinder, "02_01_Binder");
            scene.Layer("Context");
            scene.Unit("05_01_Context_Works", "Context", null);
            return scene;
        }

        // The new layer sits under the Max layer of the parent, its helper on it and linked under the parent's helper.
        private static void AssertPlaced(PlannerSceneSnapshot after, string name, string parent)
        {
            Assert.Equal(parent, after.Layers.Single(l => l.Name == name).ParentName);
            var helper = after.Nodes.Single(n => n.Name == name);
            Assert.Equal(name, helper.LayerName);
            Assert.Equal(parent, after.Nodes.Single(n => n.Key == helper.ParentKey).Name);
        }

        [Fact]
        public void UnderListsEachRootWithItsPhaseGroupsBeneathItWhenTheSceneHasSeveral()
        {
            var scene = TwoRoots();
            var parents = PlannerAddLayer.Parents(new PlannerSceneModel(scene.Snapshot));
            Assert.Equal(new[]
                {
                    "Work_Phasing_North 0 root", "_01_North_Enabling 1", "_02_North_Roads 1",
                    "Work_Phasing_South 0 root", "_07_South_Enabling 1", "_09_South_Inner 2", "_08_South_Roads 1"
                },
                parents.Select(p => p.Layer + " " + p.Depth + (p.IsRoot ? " root" : string.Empty)).ToArray());
            // Plan takes every entry: a group or a layer straight under a root, a layer or a group in a group.
            int number = 20;
            foreach (var parent in parents)
            {
                var layer = PlannerAddLayer.Plan(scene.Snapshot, parent.Layer, Layer(number, 1, null, "Probe"), null, null);
                Assert.True(layer.CanApply, parent.Layer + ": " + layer.Problem);
                var group = PlannerAddLayer.Plan(scene.Snapshot, parent.Layer, Group(number + 40, "Probe"), null, null);
                Assert.True(group.CanApply, parent.Layer + ": " + group.Problem);
                number++;
            }
            // With one root, as before: the phase groups alone (the dialog offers the root as the phasing root).
            Assert.DoesNotContain(PlannerAddLayer.Parents(new PlannerSceneModel(PlannerScheduleV2Tests.Scene().Snapshot)), p => p.IsRoot);

            // A root's Max layer that sits in another root's tree holds that root's groups: listed under it alone.
            scene.Snapshot.Layers.Single(l => l.Name == "Work_Phasing_South").ParentName = "Work_Phasing_North";
            Assert.Equal(new[]
                {
                    "Work_Phasing_North 0 root", "_01_North_Enabling 1", "_02_North_Roads 1",
                    "Work_Phasing_South 0 root", "_07_South_Enabling 1", "_09_South_Inner 2", "_08_South_Roads 1"
                },
                PlannerAddLayer.Parents(new PlannerSceneModel(scene.Snapshot)).Select(p => p.Layer + " " + p.Depth + (p.IsRoot ? " root" : string.Empty)).ToArray());
        }

        [Fact]
        public void UnderStartsOnTheChosenRowsGroupOrRootElseOnTheFirstRoot()
        {
            var model = new PlannerSceneModel(TwoRoots().Snapshot);
            var parents = PlannerAddLayer.Parents(model);
            Func<string, string> start = chosen =>
            {
                string note;
                var under = PlannerAddLayer.StartUnder(model, parents, chosen, out note);
                return (under ?? "first") + (note != null ? " | " + note : string.Empty);
            };
            // None chosen: the dialog's first entry, the first root by name.
            Assert.Equal("first", start(null));
            Assert.Equal("Work_Phasing_North", parents[0].Layer);
            // A root or a phase group: itself (any capitals).
            Assert.Equal("Work_Phasing_South", start("work_phasing_south"));
            Assert.Equal("_09_South_Inner", start("_09_South_Inner"));
            // A layer: the group above it, or its root when it sits straight under the root.
            Assert.Equal("_07_South_Enabling | 07_01_Hoarding is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on _07_South_Enabling.",
                start("07_01_Hoarding"));
            Assert.Equal("Work_Phasing_South | 07_09_Loose is not a phase group, and the Planner files a layer in the phase group above it (or at the top), so Under starts on Work_Phasing_South.",
                start("07_09_Loose"));
            // Outside every root: the first root, said by name.
            Assert.Equal("first | 05_01_Context_Works is outside every phasing root, so Under starts on Work_Phasing_North.", start("05_01_Context_Works"));
        }

        [Fact]
        public void ANewLayerGoesUnderItsParentsLayerAndHelperInWhicheverRootItIs()
        {
            var scene = TwoRoots();
            // A group straight under South: its Max layer under South's layer, its helper under South's helper, nothing made
            // for the root, and the record names South's Max layer as its parent (the tab shows it under that root).
            var group = PlannerAddLayer.Plan(scene.Snapshot, "Work_Phasing_South", Group(10, "South Night"), null, null);
            Assert.True(group.CanApply, group.Problem);
            Assert.Equal("Work_Phasing_South", group.ParentName);
            Assert.Equal("under Work_Phasing_South", Assert.Single(group.Review).To);
            Assert.Contains(PlannerLayerPlanner.RootNoteStart + "Work_Phasing_South (layer and helper).", group.Build.Notes);
            Assert.Equal("Work_Phasing_South", group.Added.ParentLayer);
            var after = TestSimulator.Apply(scene.Snapshot, group.Build);
            AssertPlaced(after, "_10_South_Night", "Work_Phasing_South");
            var batch = PlannerLayerScript.Build(group.Build, "__t_");
            Assert.Equal(1, batch.LayerCreates);
            Assert.Equal(1, batch.HelperCreates);
            Assert.Equal(0, batch.LayerRenames + batch.LayerMoves + batch.HelperRenames + batch.HelperMoves + batch.HelperPuts + batch.Stamps);
            Assert.Single(PlannerAddedLayerStore.Present(new[] { group.Added }, new PlannerSceneModel(after)));

            // A layer straight under South (any capitals), and one in South's nested group.
            var loose = PlannerAddLayer.Plan(scene.Snapshot, "work_phasing_south", Layer(7, 10, null, "Gates"), null, null);
            Assert.True(loose.CanApply, loose.Problem);
            Assert.Equal("Work_Phasing_South", loose.Added.ParentLayer);
            AssertPlaced(TestSimulator.Apply(scene.Snapshot, loose.Build), "07_10_Gates", "Work_Phasing_South");
            var nested = PlannerAddLayer.Plan(scene.Snapshot, "_09_South_Inner", Layer(9, 2, null, "Inner Fence"), null, null);
            Assert.True(nested.CanApply, nested.Problem);
            Assert.Equal("_09_South_Inner", nested.Added.ParentLayer);
            AssertPlaced(TestSimulator.Apply(scene.Snapshot, nested.Build), "09_02_Inner_Fence", "_09_South_Inner");

            // None named: the first root by name, North, although South was made first.
            var first = PlannerAddLayer.Plan(scene.Snapshot, null, Layer(3, 1, null, "Top"), null, null);
            Assert.True(first.CanApply, first.Problem);
            Assert.Equal("Work_Phasing_North", first.ParentName);
            Assert.Equal("Work_Phasing_North", first.Added.ParentLayer);
            Assert.Single(first.Review);
            AssertPlaced(TestSimulator.Apply(scene.Snapshot, first.Build), "03_01_Top", "Work_Phasing_North");

            // Only under a root or a phase group: a layer of South is no parent, and the root to add under instead is named.
            Assert.Equal("the Planner files a layer in the phase group above it (or at the top), not under 07_01_Hoarding, so the next layer list from the Planner "
                + "would move it out from under 07_01_Hoarding: add it under _07_South_Enabling or Work_Phasing_South instead",
                PlannerAddLayer.Plan(scene.Snapshot, "07_01_Hoarding", Layer(7, 11, null, "Under"), null, null).Problem);
            Assert.Contains("add it under a phase group or Work_Phasing_South instead", PlannerAddLayer.Plan(scene.Snapshot, "07_09_Loose", Layer(7, 11, null, "Under"), null, null).Problem);
            Assert.Equal("its parent 05_01_Context_Works is outside every phasing root: run Clean up scene graph with the Planner's list imported first",
                PlannerAddLayer.Plan(scene.Snapshot, "05_01_Context_Works", Layer(7, 11, null, "Under"), null, null).Problem);
        }

        [Fact]
        public void TheIdNameAndPhaseChecksCoverEveryRootTheListAndTheLayersAddedInMax()
        {
            var scene = TwoRoots();
            Func<string, PlannerNewLayer, PlannerLayerList, IEnumerable<PlannerAddedLayer>, string> problem = (parent, spec, rowsList, before) =>
                PlannerAddLayer.Plan(scene.Snapshot, parent, spec, rowsList, before).Problem;
            // An ID of the other root, wherever it is added.
            Assert.Equal("the layer 07_01_Hoarding in Work_Phasing_South has the ID 07_01 already", problem("_01_North_Enabling", Layer(7, 1, null, "Fence"), null, null));
            Assert.Equal("the layer 01_01_Drainage in Work_Phasing_North has the ID 01_01 already", problem("Work_Phasing_South", Layer(1, 1, null, "Other"), null, null));
            // One phase group per phase number in the whole scene (R21): South cannot have a group of North's phase 1, nor
            // North one of the phase of South's nested group.
            Assert.Equal("the phasing tree has a group of phase 1 already (_01_North_Enabling, in Work_Phasing_North): a phase number belongs to one phase group across "
                + "every root, so give the group a phase no root has yet, or add the layers under _01_North_Enabling",
                problem("Work_Phasing_South", Group(1, "South Enabling"), null, null));
            Assert.Contains("a group of phase 9 already (_09_South_Inner, in Work_Phasing_South)", problem("Work_Phasing_North", Group(9, "North Inner"), null, null));
            // A group's name in the other root, any capitals (here a helper with no Max layer yet).
            scene.Helper("_11_north_late", scene.Find("Work_Phasing_North"), "Work_Phasing_North");
            Assert.Equal("the phasing tree has a group called _11_north_late already (in Work_Phasing_North)", problem("Work_Phasing_South", Group(11, "North Late"), null, null));

            // The Planner's list, Root rows and all: its IDs and phase numbers, whichever root its rows are under.
            var southRow = TestCsv.Row("r2", null, "Work_Phasing_South");
            southRow[3] = "Root";
            var list = TestCsv.List(southRow, TestCsv.Row("g12", "r2", "_12_South_Late"), TestCsv.Row("l12", "g12", "12_01_Late"));
            Assert.True(list.Usable);
            Assert.Equal(1, list.RootRows);
            Assert.Equal("the Planner's list has the ID 12_01 already (12_01_Late)", problem("_01_North_Enabling", Layer(12, 1, null, "Sooner"), list, null));
            Assert.Contains("the Planner's list has a group of phase 12 already (_12_South_Late)", problem("Work_Phasing_North", Group(12, "North Late"), list, null));

            // A layer added in Max before that the scene still has (here moved out of the roots) keeps its ID, in every root.
            var added = new[] { new PlannerAddedLayer { Layer = "07_20_Ramp", ParentLayer = "Work_Phasing_South", Kind = "Layer" } };
            Assert.Null(problem("Work_Phasing_North", Layer(7, 20, null, "Ramp 2"), null, added));
            scene.Layer("07_20_Ramp");
            Assert.Equal("the layer 07_20_Ramp, added in Max before, has the ID 07_20 already", problem("Work_Phasing_North", Layer(7, 20, null, "Ramp 2"), null, added));
        }

        [Fact]
        public void AddLayerMakesNoNewRootAndWaitsForARootsMaxLayer()
        {
            // A third root, East, whose helper has no Max layer of its name (R1: no other layer stands in for it). It sorts
            // first by name, so it is the first root; Under lists it with none of its groups (their layers are not in its tree).
            var scene = TwoRoots();
            scene.Layer("East_Old");
            var east = scene.Helper("Work_Phasing_East", null, "East_Old");
            var eastWorks = scene.Helper("_15_East_Works", east);
            scene.Layer("_15_East_Works", "East_Old");
            var model = new PlannerSceneModel(scene.Snapshot);
            var parents = PlannerAddLayer.Parents(model);
            Assert.Equal("Work_Phasing_East", parents[0].Layer);
            Assert.True(parents[0].IsRoot);
            Assert.True(parents[1].IsRoot);
            Assert.NotNull(eastWorks);

            // Straight under East (or none named, East being first): refused until Clean up gives East its Max layer, which also
            // brings its groups' layers under it (R25). Made here, the layer would hold the new layer but none of East's groups.
            foreach (var parent in new[] { "Work_Phasing_East", null })
            {
                var plan = PlannerAddLayer.Plan(scene.Snapshot, parent, Layer(16, 1, null, "East Works"), null, null);
                Assert.False(plan.CanApply);
                Assert.Equal("the phasing root Work_Phasing_East has no Max layer of its name: run Clean up scene graph first, which gives it one", plan.Problem);
            }
            // Under one of East's groups, the same.
            Assert.Equal("the phasing root Work_Phasing_East has no Max layer of its name: run Clean up scene graph first, which gives it one",
                PlannerAddLayer.Plan(scene.Snapshot, "_15_East_Works", Layer(15, 1, null, "East Fence"), null, null).Problem);

            // A root that is not in the scene is never made: only Work_Phasing in a scene with no root at all.
            Assert.Equal("its parent Work_Phasing_West is not a Max layer of the scene: run Clean up scene graph with the Planner's list imported first",
                PlannerAddLayer.Plan(scene.Snapshot, "Work_Phasing_West", Group(17, "West"), null, null).Problem);
            var north = PlannerAddLayer.Plan(TwoRoots().Snapshot, "Work_Phasing_North", Group(17, "North Later"), null, null);
            Assert.True(north.CanApply, north.Problem);
            Assert.DoesNotContain(north.Build.BuildLayers, l => l.Create && l.FinalName != north.LayerName);
            Assert.DoesNotContain(north.Build.BuildHelpers, h => h.Create && h.FinalName != north.LayerName);
        }

        [Fact]
        public void ARootThatIsAMaxLayerAloneGetsItsHelperAsBefore()
        {
            // R9: a scene with no root helper has one root, its first top-level Max layer with "phasing" in its name. Add layer
            // makes the root's helper at the top on that layer, whether the new layer goes straight under the root or in a group.
            var scene = new TestScene();
            scene.Layer("0");
            scene.Layer("Site_Phasing");
            var enabling = scene.Unit("_01_Site_Enabling", "Site_Phasing", null);
            scene.Unit("01_01_Clear", "_01_Site_Enabling", enabling);
            var model = new PlannerSceneModel(scene.Snapshot);
            Assert.Null(Assert.Single(model.PhasingRoots).Node);
            Assert.Equal(new[] { "_01_Site_Enabling 0" }, PlannerAddLayer.Parents(model).Select(p => p.Layer + " " + p.Depth + (p.IsRoot ? " root" : string.Empty)).ToArray());

            var top = PlannerAddLayer.Plan(scene.Snapshot, null, Layer(1, 5, null, "Gates"), null, null);
            var inGroup = PlannerAddLayer.Plan(scene.Snapshot, "_01_Site_Enabling", Layer(1, 6, null, "Fence"), null, null);
            foreach (var plan in new[] { top, inGroup })
            {
                Assert.True(plan.CanApply, plan.Problem);
                Assert.Equal(2, plan.Review.Count);
                Assert.Equal("Phasing root", plan.Review[0].Item);
                Assert.Equal("Site_Phasing", plan.Review[0].Name);
                Assert.Equal("Create helper", plan.Review[0].Change);
                Assert.Equal("top of the scene", plan.Review[0].To);
                Assert.Equal("the phasing root has no helper of its name", plan.Review[0].Why);
                Assert.Contains(PlannerLayerPlanner.RootNoteStart + "the layer Site_Phasing. It has no helper node of its name, so one is created at the top.", plan.Build.Notes);
                var after = TestSimulator.Apply(scene.Snapshot, plan.Build);
                var rootHelper = after.Nodes.Single(n => n.Name == "Site_Phasing");
                Assert.Null(rootHelper.ParentKey);
                Assert.Equal("Site_Phasing", rootHelper.LayerName);
                Assert.Equal(scene.Snapshot.Layers.Count + 1, after.Layers.Count);
                Assert.Equal(scene.Snapshot.Nodes.Count + 2, after.Nodes.Count);
            }

            // Straight under the root: the new helper is linked under the root's new helper, the record names the root's Max
            // layer, and the scene then reads with the root's helper and its layer.
            Assert.Equal("Site_Phasing", top.ParentName);
            Assert.Equal("Site_Phasing", top.Added.ParentLayer);
            var batch = PlannerLayerScript.Build(top.Build, "__t_");
            Assert.Equal(1, batch.LayerCreates);
            Assert.Equal(2, batch.HelperCreates);
            Assert.Equal(0, batch.LayerRenames + batch.LayerMoves + batch.HelperRenames + batch.HelperMoves + batch.HelperPuts + batch.Stamps);
            var afterTop = TestSimulator.Apply(scene.Snapshot, top.Build);
            AssertPlaced(afterTop, "01_05_Gates", "Site_Phasing");
            var rootAfter = Assert.Single(new PlannerSceneModel(afterTop).PhasingRoots);
            Assert.Equal("Site_Phasing", rootAfter.Node.Name);
            Assert.Equal("Site_Phasing", rootAfter.Layer.Name);
            Assert.Single(PlannerAddedLayerStore.Present(new[] { top.Added }, new PlannerSceneModel(afterTop)));

            // In the group: under the group's layer and helper, as before.
            Assert.Equal("_01_Site_Enabling", inGroup.ParentName);
            Assert.Equal("_01_Site_Enabling", inGroup.Added.ParentLayer);
            AssertPlaced(TestSimulator.Apply(scene.Snapshot, inGroup.Build), "01_06_Fence", "_01_Site_Enabling");
        }

        [Fact]
        public void AddLayerPlansTheOneRootAsTheLayerBuilderDoes()
        {
            // Add layer plans the root it adds in with the layer builder's PlanOneRoot: in a one-root scene the two give the same
            // entries and notes, so Add layer and Clean up say the same. A root helper with no Max layer of its name waits for
            // Clean up, as in a scene of several roots.
            var bare = new TestScene();
            bare.Layer("0");
            var layerAlone = new TestScene();
            layerAlone.Layer("0");
            layerAlone.Layer("Site_Phasing");
            var helperAlone = new TestScene();
            helperAlone.Layer("0");
            helperAlone.Helper("Work_Phasing", null, "0");
            Assert.Equal("the phasing root Work_Phasing has no Max layer of its name: run Clean up scene graph first, which gives it one",
                PlannerAddLayer.Plan(helperAlone.Snapshot, null, Layer(40, 1, null, "Probe"), null, null).Problem);
            Func<PlannerLayerPlan, string> root = build => build.RootName + " " + build.RootLayerCreated + " " + build.RootHelperCreated
                + " | " + build.BuildLayers[0].CurrentName + " " + build.BuildLayers[0].FinalName + " " + build.BuildLayers[0].Create + " " + build.BuildLayers[0].ParentNameNow
                + " | " + build.BuildHelpers[0].Key + " " + build.BuildHelpers[0].CurrentName + " " + build.BuildHelpers[0].FinalName + " " + build.BuildHelpers[0].Create
                + " " + build.BuildHelpers[0].LayerIndex + " " + build.BuildHelpers[0].LayerNow;
            foreach (var scene in new[] { bare, layerAlone, PlannerScheduleV2Tests.Scene() })
            {
                var plan = PlannerAddLayer.Plan(scene.Snapshot, null, Layer(40, 1, null, "Probe"), null, null);
                Assert.True(plan.CanApply, plan.Problem);
                var shared = new PlannerLayerPlan();
                var model = new PlannerSceneModel(scene.Snapshot);
                PlannerLayerPlanner.PlanOneRoot(shared, model, model.FirstRoot);
                Assert.Equal(root(shared), root(plan.Build));
                Assert.Equal(shared.Notes, plan.Build.Notes);
            }
        }
    }
}
