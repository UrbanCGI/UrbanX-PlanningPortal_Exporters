using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// Clean up scene graph with the Planner's layer list imported (D22: Build layers and helpers is part of Clean up): the
    /// layers and helpers brought in line with the list in the same review and batch, never named with dates, nested as the
    /// list nests them, made where the scene has none, and references following an ID the list changes (spec 5.3).
    /// </summary>
    public class PlannerCleanUpListTests
    {
        private const string Root = "Work_Phasing";

        private static TestScene Phasing(params string[] groups)
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            foreach (var group in groups.Length > 0 ? groups : new[] { "_01_Zone", "_02_Works" })
            {
                scene.Unit(group, Root, root);
            }
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

        // Every node's name after the result, by its handle.
        private static Dictionary<long, string> After(PlannerSceneSnapshot scene, PlannerCleanUpResult result)
        {
            var renamed = result.NodeRenames.ToDictionary(r => r.Key, r => r.NewName);
            return scene.Nodes.ToDictionary(n => n.Key, n =>
            {
                string name;
                return renamed.TryGetValue(n.Key, out name) ? name : n.Name;
            });
        }

        private static PlannerCleanUpPlan Plan(PlannerSceneSnapshot scene, PlannerLayerList list)
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
        // The list's names, without dates

        [Fact]
        public void AListMadeBeforeTheNewNamesIsReadWithoutItsDates()
        {
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_02_Works"),
                TestCsv.Row("f1", "g1", "02_01_Kerbs_TBC_17-10-26_20-10-26"),
                TestCsv.Row("f2", "g1", "02_04-2_Roads_TBC"),
                TestCsv.Row("f3", "g1", "02_05_Gates_01-02-26"),
                TestCsv.Row("f4", "g1", "Drainage_TBC_08-06-26", "Layer"),
                TestCsv.Row("f5", "g1", "02_06_Works_from_18-09-26_TBC_01-10-26_02-10-26"));
            var dateless = PlannerCleanUp.WithoutDates(list);
            Assert.NotSame(list, dateless);
            Assert.Equal(new[] { "_02_Works", "02_01_Kerbs", "02_04_2_Roads", "02_05_Gates", "Drainage", "02_06_Works_from" }, dateless.Rows.Select(r => r.Layer).ToArray());
            Assert.Equal(5, dateless.DatesTakenOut);
            Assert.Equal(PlannerCleanUp.DatesTakenOutNote(5), dateless.Notes.Last());
            Assert.Equal("5 layer names in the list carry dates or TBC (a list exported before the new names): they are read without them, as layers and helpers carry no dates (the dates stay in the list's Start, Finish and TBC).",
                PlannerCleanUp.DatesTakenOutNote(5));
            // The rest of each row is the list's: its Planner ID, parent, kind, ID and dates.
            Assert.Equal(list.Rows.Select(r => r.PlannerId + " " + r.ParentId + " " + r.Kind + " " + r.IdKey + " " + r.Start + " " + r.Finish + " " + r.Tbc),
                dateless.Rows.Select(r => r.PlannerId + " " + r.ParentId + " " + r.Kind + " " + r.IdKey + " " + r.Start + " " + r.Finish + " " + r.Tbc));
            Assert.Equal("g1", dateless.Rows[1].Parent.PlannerId);
            // Read again, or a list on the new names: as it is.
            Assert.Same(dateless, PlannerCleanUp.WithoutDates(dateless));
            var fresh = TestCsv.List(TestCsv.Row("g1", null, "_02_Works"), TestCsv.Row("f1", "g1", "02_01_Kerbs", "Layer"));
            Assert.Same(fresh, PlannerCleanUp.WithoutDates(fresh));

            // Two rows whose names only their dates told apart are one name now: the second is a problem, as in a list as read.
            var twice = PlannerCleanUp.WithoutDates(TestCsv.List(
                TestCsv.Row("g1", null, "_02_Works"),
                TestCsv.Row("f1", "g1", "02_01_Kerbs_17-10-26"),
                TestCsv.Row("f2", "g1", "02_01_Kerbs_TBC")));
            Assert.False(twice.Rows[1].HasProblem);
            Assert.True(twice.Rows[2].HasProblem);
        }

        [Fact]
        public void AGroupIsNamedAsTheListWritesItAndTheExportCheckDoesNotSendItToCleanUp()
        {
            // A group's name is the Planner's folder name: a date or TBC typed into it is kept, and the export check does not
            // say Clean up takes it out (it never does). A group row whose name carries a layer ID is named as a layer is.
            var scene = Phasing();
            Layer(scene, "_02_Works", "02_01_Kerbs", "IN_Kerb");
            var list = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_01_Kerbs", "Layer"),
                TestCsv.Row("g7", "g2", "_02_Sub_Group_17-10-26", "Group"),
                TestCsv.Row("f7", "g7", "02_07_Under", "Layer"),
                TestCsv.Row("g3", null, "_03_Future_Works_TBC", "Group"),
                TestCsv.Row("g5", "g2", "02_05_Barriers_17-10-26", "Group"),
                TestCsv.Row("f5", "g5", "02_05_1_Posts", "Layer"));
            Assert.Equal(new[] { "_02_Works", "02_01_Kerbs", "_02_Sub_Group_17-10-26", "02_07_Under", "_03_Future_Works_TBC", "02_05_Barriers", "02_05_1_Posts" },
                PlannerCleanUp.WithoutDates(list).Rows.Select(r => r.Layer).ToArray());
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            Assert.Equal(new[] { "02_05_1_Posts", "02_05_Barriers", "02_07_Under", "_02_Sub_Group_17-10-26", "_03_Future_Works_TBC" },
                result.NewHelpers.Select(h => h.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            var run = Run(plan, result, scene.Snapshot);
            var check = PlannerNamingValidator.ValidateV2(new PlannerScheduleModel(run.Scene), null);
            Assert.Empty(check.CleanUp);
            Assert.Null(check.CleanUpPointer());
            Assert.DoesNotContain(check.Issues, i => i.Message.Contains("still carries dates or TBC"));
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void ADatedListNeverPutsDatesBackIntoACleanScene()
        {
            // A scene already on the new names, and the list as an old-name project exports it: nothing to change.
            var scene = Phasing();
            Layer(scene, "_02_Works", "02_01_Kerbs", "IN_Kerb");
            Layer(scene, "_02_Works", "02_02_Gates", "IN_Gate_RM");
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_02_Works"),
                TestCsv.Row("f1", "g1", "02_01_Kerbs_TBC_17-10-26_20-10-26"),
                TestCsv.Row("f2", "g1", "02_02_Gates_21-10-26"));
            var plan = Plan(scene.Snapshot, list);
            Assert.Equal(PlannerNamingKind.New, plan.Naming);
            Assert.Contains(PlannerCleanUp.DatesTakenOutNote(2), plan.Notes);
            var result = plan.Resolve(null);
            Assert.Empty(result.NodeRenames);
            Assert.Empty(result.MaxLayerRenames);
            // Only the Planner IDs are written (the root has no row).
            Assert.Equal(new[] { "g1", "f1", "f2" }, result.Stamps.Select(s => s.PlannerId).ToArray());
            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Built from the list: found, renamed, nested, made

        [Fact]
        public void TheListsLayersAndHelpersAreFoundRenamedNestedAndMadeInTheSameBatch()
        {
            var scene = Phasing("_01_Zone", "_02_Works", "_03_Roads");
            // Found by its layer ID: dates out (the scene's own clean-up), then the Planner's name.
            var kerbs = Layer(scene, "_02_Works", "02_01_Kerb_Works_17-10-26", "Ph2_St01_IN_Kerb", "Ph2_St01_IN_Gully_Ph2_St02_RM");
            // Found by its Planner ID, named and nested otherwise in the Planner.
            var gates = Layer(scene, "_02_Works", "02_02_Gates_18-10-26", "Ph2_St02_IN_Gate");
            gates.PlannerId = "f2";
            // A helper without its Max layer, and a Max layer without its helper (its object waits on it).
            var lights = scene.Helper("02_03_Lights_19-10-26", scene.Find("_02_Works"), "_02_Works");
            scene.Layer("02_04_Ducts_20-10-26", "_02_Works");
            var duct = scene.Object("Ph2_St04_IN_Duct", null, "02_04_Ducts_20-10-26");
            // Not in the list: kept, cleaned, and listed.
            Layer(scene, "_01_Zone", "01_01_Drainage_TBC", "Ph1_St01_IN_Pipe");

            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_01_Zone"),
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("g3", null, "_03_Roads"),
                TestCsv.Row("f1", "g2", "02_01_Kerbs", "Layer"),
                TestCsv.Row("f2", "g3", "02_02_Gate_Works", "Layer"),
                TestCsv.Row("f3", "g2", "02_03_Lights", "Layer"),
                TestCsv.Row("f4", "g2", "02_04_Ducts", "Layer"),
                TestCsv.Row("f5", "g2", "02_05_Barriers", "Layer"),
                TestCsv.Row("f6", "f5", "02_05_1_Barrier_Posts", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            Assert.Empty(plan.Questions);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("02_01_Kerbs", after[kerbs.Key]);
            Assert.Equal("02_02_Gate_Works", after[gates.Key]);
            Assert.Equal("02_03_Lights", after[lights.Key]);
            Assert.Equal("01_01_Drainage", after[scene.Find("01_01_Drainage_TBC").Key]);
            // One rename per node and per Max layer, from the name it has now to the list's.
            Assert.Single(result.NodeRenames, r => r.Key == kerbs.Key);
            Assert.Contains(result.MaxLayerRenames, r => r.OldName == "02_01_Kerb_Works_17-10-26" && r.NewName == "02_01_Kerbs");
            Assert.Contains(result.MaxLayerRenames, r => r.OldName == "02_04_Ducts_20-10-26" && r.NewName == "02_04_Ducts");
            Assert.DoesNotContain(result.MaxLayerRenames, r => r.OldName == "02_01_Kerb_Works");
            // Nested as the list nests them: 02_02 into _03_Roads, its Max layer too.
            Assert.Contains(result.NodeMoves, m => m.Key == gates.Key && m.ToParentKey == scene.Find("_03_Roads").Key);
            Assert.Contains(result.MaxLayerMoves, m => m.Name == "02_02_Gates_18-10-26" && m.NewParentName == "_03_Roads" && !m.NewParentIsNew);
            // Made: 02_03's Max layer, 02_04's helper, and 02_05 and 02_05_1 whole, 02_05_1 under 02_05.
            Assert.Equal(new[] { "02_03_Lights", "02_05_Barriers", "02_05_1_Barrier_Posts" }, result.NewLayers.Select(l => l.Name).ToArray());
            Assert.True(result.NewLayers[2].ParentIsNew);
            Assert.Equal("02_05_Barriers", result.NewLayers[2].ParentName);
            Assert.Equal(new[] { "02_04_Ducts", "02_05_Barriers", "02_05_1_Barrier_Posts" }, result.NewHelpers.Select(h => h.Name).ToArray());
            Assert.Equal(new[] { "f4", "f5", "f6" }, result.NewHelpers.Select(h => h.PlannerId).ToArray());
            Assert.All(result.NewHelpers, h => Assert.True(h.Key < 0));
            Assert.Equal(result.NewHelpers[1].Key, result.NewHelpers[2].ParentKey);
            Assert.Contains(result.HelperPuts, p => p.Key == lights.Key && p.ToLayer == "02_03_Lights" && p.ToNewLayer);
            // The Planner IDs: on every helper the list found that does not have it yet.
            Assert.Equal(new[] { "f1", "f3", "g1", "g2", "g3" }, result.Stamps.Select(s => s.PlannerId).OrderBy(s => s, StringComparer.Ordinal).ToArray());
            // The Max layer without its helper: its object is linked under the new helper and converted against it.
            Assert.Contains(result.Links, l => l.Key == duct.Key && l.ToKey == result.NewHelpers[0].Key);
            Assert.Equal("IN_Duct", after[duct.Key]);
            // The trailing tag names layer 02_02, which keeps its ID.
            Assert.Equal("IN_Gully_RM_02_02", after[scene.Find("Ph2_St01_IN_Gully_Ph2_St02_RM").Key]);
            Assert.Equal("3 layers and 3 helpers to create, 4 layers and 5 objects to rename, 1 helper and 1 Max layer to move, 1 link, 8 Planner IDs to write, 0 to choose, 1 to check.", result.Summary());

            // The review: one row per layer, with what the list does, and the layer the list does not have.
            var kerbRow = result.Rows.Single(r => r.NodeKey == kerbs.Key && r.Status == PlannerCleanUpStatus.Change);
            Assert.Equal("02_01_Kerb_Works_17-10-26", kerbRow.CurrentName);
            Assert.Equal("02_01_Kerbs", kerbRow.NewName);
            Assert.Equal("Layer and helper", kerbRow.Item);
            Assert.Equal("date taken out of the name; named as the Planner's list names it (found by its layer ID); takes the Planner ID f1 from the list (found by its layer ID)", kerbRow.Why);
            var gateRow = result.Rows.Single(r => r.NodeKey == gates.Key && r.Status == PlannerCleanUpStatus.Change);
            // Where it is now; What changes says where the list moves it.
            Assert.Equal("_02_Works", gateRow.LayerItSitsIn);
            Assert.Contains("renamed in the Planner", gateRow.Why);
            Assert.Contains("moved under _03_Roads, as the Planner's list nests it", gateRow.Why);
            var posts = result.Rows.Single(r => r.NewName == "02_05_1_Barrier_Posts");
            Assert.Equal(string.Empty, posts.CurrentName);
            Assert.Equal("Layer and helper", posts.Item);
            Assert.Equal("02_05_Barriers", posts.LayerItSitsIn);
            Assert.Equal("new in the Planner's list (layer); made under 02_05_Barriers", posts.Why);
            Assert.Contains(result.Rows, r => r.NewName == "02_04_Ducts" && r.Item == "Helper" && r.Why.Contains("its Max layer is there already"));
            Assert.Contains(result.Rows, r => r.NodeKey == lights.Key && r.Why.Contains("its Max layer 02_03_Lights is added"));
            var unlisted = result.Rows.Single(r => r.Status == PlannerCleanUpStatus.Check);
            Assert.Equal("01_01_Drainage_TBC", unlisted.CurrentName);
            Assert.Equal("Layer and helper", unlisted.Item);
            Assert.StartsWith("not in the Planner's list: kept, as Clean up never deletes", unlisted.Why);

            // The batch makes it so, and again there is nothing to do.
            var run = Run(plan, result, scene.Snapshot);
            var made = run.Scene.Nodes.Single(n => n.Name == "02_05_1_Barrier_Posts");
            Assert.Equal("f6", made.PlannerId);
            Assert.Equal("02_05_Barriers", run.Scene.Nodes.Single(n => n.Key == made.ParentKey).Name);
            Assert.Equal("02_05_Barriers", run.Scene.Layers.Single(l => l.Name == "02_05_1_Barrier_Posts").ParentName);
            var again = Plan(run.Scene, list).Resolve(null);
            Assert.False(again.HasChanges, string.Join("\n", again.Rows.Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
            var names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(run.Scene), null);
            Assert.Equal("02_04", PlannerNaming.LayerIdText(names.Objects.Single(o => o.Name == "IN_Duct").Layer));
        }

        [Fact]
        public void AnIdChangedInThePlannerTakesTheReferencesToItAlong()
        {
            // Two layers built from the list before, their IDs swapped in the Planner since; references to each, written old
            // and new, follow their layers, swapped too.
            var scene = Phasing();
            var kerbs = Layer(scene, "_02_Works", "02_01_Kerbs", "IN_Kerb", "IN_Gully_RM_02_02");
            kerbs.PlannerId = "f1";
            var gates = Layer(scene, "_02_Works", "02_02_Gates", "IN_Gate_RM_02_01", "Ph2_St02_IN_Post_Ph2_St01_RM");
            gates.PlannerId = "f2";
            var plain = Layer(scene, "_02_Works", "02_03_Lights", "IN_Lamp_RM");
            plain.PlannerId = "f3";
            var list = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_02_Kerbs", "Layer"),
                TestCsv.Row("f2", "g2", "02_01_Gates", "Layer"),
                TestCsv.Row("f3", "g2", "02_03_Lights", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("02_02_Kerbs", after[kerbs.Key]);
            Assert.Equal("02_01_Gates", after[gates.Key]);
            Assert.Equal("IN_Kerb", after[scene.Find("IN_Kerb").Key]);
            Assert.Equal("IN_Gully_RM_02_01", after[scene.Find("IN_Gully_RM_02_02").Key]);
            Assert.Equal("IN_Gate_RM_02_02", after[scene.Find("IN_Gate_RM_02_01").Key]);
            Assert.Equal("IN_Post_RM_02_02", after[scene.Find("Ph2_St02_IN_Post_Ph2_St01_RM").Key]);
            Assert.Equal("IN_Lamp_RM", after[scene.Find("IN_Lamp_RM").Key]);
            Assert.Contains(result.Rows, r => r.NodeKey == kerbs.Key && r.Why.Contains("its ID changed in the Planner, from 02_01 to 02_02, and the references to it follow"));
            Assert.Contains(result.Rows, r => r.CurrentName == "IN_Gully_RM_02_02" && r.Why.Contains("its trailing tag names layer 02_02, whose ID the Planner's list changes to 02_01"));

            var run = Run(plan, result, scene.Snapshot);
            var names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(run.Scene), null);
            Assert.Empty(names.Unfiled);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void AReferenceToAnIdTheListGivesALayerThatHadAnotherIsListedNotRewritten()
        {
            // New names: a reference to 02_02, which no layer of the scene has, while the Planner moves Kerb (f1) from 02_01 to
            // 02_02. The reference was never written for Kerb: listed, left as it is, and the batch only renames Kerb.
            var scene = Phasing();
            var kerb = Layer(scene, "_02_Works", "02_01_Kerb", "IN_A");
            kerb.PlannerId = "f1";
            var other = Layer(scene, "_02_Works", "02_03_Other", "IN_C_RM_02_02");
            other.PlannerId = "f3";
            var list = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_02_Kerb", "Layer"),
                TestCsv.Row("f3", "g2", "02_03_Other", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("02_02_Kerb", after[kerb.Key]);
            var c = scene.Find("IN_C_RM_02_02");
            Assert.Equal("IN_C_RM_02_02", after[c.Key]);
            var row = result.Rows.Single(r => r.NodeKey == c.Key);
            Assert.Equal(PlannerCleanUpStatus.Check, row.Status);
            Assert.Contains("the Planner's list now gives 02_02 to 02_02_Kerb, which was 02_01", row.Why);
            Assert.Contains("left as it is", row.Why);
            Run(plan, result, scene.Snapshot);

            // Old names: the tag Ph2_St02_RM names a stage no layer has, and the list gives 02_02 to Kerb. The answer that refers
            // to 02_02 says it is Kerb's now, and there is no default: run with the defaults alone (as MAXScript runs it), the
            // object keeps its old name and waits for an answer.
            var old = Phasing();
            var oldKerb = Layer(old, "_02_Works", "02_01_Kerb_17-10-26", "Ph2_St01_IN_A");
            oldKerb.PlannerId = "f1";
            var oldOther = Layer(old, "_02_Works", "02_03_Other_18-10-26", "Ph2_St03_IN_C_Ph2_St02_RM");
            oldOther.PlannerId = "f3";
            var oldC = old.Find("Ph2_St03_IN_C_Ph2_St02_RM");
            var oldPlan = Plan(old.Snapshot, list);
            Assert.Equal(PlannerNamingKind.Old, oldPlan.Naming);
            var question = oldPlan.Questions.Single(q => q.Case == PlannerCleanUpCase.NoLayer);
            Assert.Null(question.DefaultChoice);
            Assert.Contains("the Planner's list now gives 02_02 to 02_02_Kerb, which was 02_01", question.Why);
            Assert.Equal("Refer to 02_02 as the tag does: that is 02_02_Kerb now, so the removal is filed in it", question.OptionFor("as-written").Text);
            Assert.DoesNotContain(question.Options, o => o.Text.Contains("parks"));
            var byDefault = oldPlan.Resolve(null);
            Assert.Equal("Ph2_St03_IN_C_Ph2_St02_RM", After(old.Snapshot, byDefault)[oldC.Key]);
            Assert.Equal(1, byDefault.Open);
            Assert.Equal("02_02_Kerb", After(old.Snapshot, byDefault)[oldKerb.Key]);
            // Answered, it is converted as asked.
            var answered = oldPlan.Resolve(new Dictionary<string, string> { { question.Id, "as-written" } });
            Assert.Equal("IN_C_RM_02_02", After(old.Snapshot, answered)[oldC.Key]);

            // Control: a list that leaves Kerb at 02_01 keeps the default, which the Planner parks TBC, and the new name is
            // left alone without a row.
            var same = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_01_Kerb", "Layer"),
                TestCsv.Row("f3", "g2", "02_03_Other", "Layer"));
            var parked = Plan(old.Snapshot, same).Questions.Single(q => q.Case == PlannerCleanUpCase.NoLayer);
            Assert.Equal("as-written", parked.DefaultChoice);
            Assert.Contains("parks the removal TBC", parked.OptionFor("as-written").Text);
            var unchanged = Plan(scene.Snapshot, same).Resolve(null);
            Assert.Equal("IN_C_RM_02_02", After(scene.Snapshot, unchanged)[c.Key]);
            Assert.DoesNotContain(unchanged.Rows, r => r.NodeKey == c.Key);
        }

        [Fact]
        public void AnIdTheListGivesThatALayerOfTheSceneKeepsIsListedAndLeftAsItIs()
        {
            // Gully was added in Max (no Planner ID) as 02_02; the Planner moves Kerb (f1) from 02_01 to 02_02 and its own
            // Gully folder (f2) to 02_01. Kerb cannot take 02_02 while Gully keeps it (D2), and then 02_01 is still Kerb's: both
            // rows are listed and left as they are, so no two layers share an ID, and the reference to 02_02 keeps naming Gully.
            var scene = Phasing();
            var kerb = Layer(scene, "_02_Works", "02_01_Kerb", "IN_Kerb");
            kerb.PlannerId = "f1";
            var gully = Layer(scene, "_02_Works", "02_02_Gully", "IN_Gully");
            Layer(scene, "_02_Works", "02_03_Other", "IN_A_RM_02_02").PlannerId = "f3";
            var list = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_02_Kerb", "Layer"),
                TestCsv.Row("f2", "g2", "02_01_Gully", "Layer"),
                TestCsv.Row("f3", "g2", "02_03_Other", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("02_01_Kerb", after[kerb.Key]);
            Assert.Equal("02_02_Gully", after[gully.Key]);
            Assert.Equal("IN_A_RM_02_02", after[scene.Find("IN_A_RM_02_02").Key]);
            Assert.Empty(result.NewHelpers);
            Assert.Empty(result.NewLayers);
            Assert.Empty(result.MaxLayerRenames);
            var listed = result.Rows.Where(r => r.Status == PlannerCleanUpStatus.Check && r.Why.StartsWith("the Planner's list has it, but it is not brought in line", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "02_01_Gully", "02_02_Kerb" }, listed.Select(r => r.NewName).ToArray());
            Assert.Contains("the layer 02_01_Kerb, whose row is left as it is, keeps the layer ID 02_01", listed[0].Why);
            Assert.Contains("the layer 02_02_Gully, which the list does not have, keeps the layer ID 02_02, and no two layers may share an ID", listed[1].Why);
            var run = Run(plan, result, scene.Snapshot);
            var check = PlannerNamingValidator.ValidateV2(new PlannerScheduleModel(run.Scene), null);
            Assert.DoesNotContain(check.Issues, i => i.Code == PlannerNamingValidator.SharedIdCode || i.Message.Contains("share the layer ID"));

            // Two rows of the list with one layer ID: the second is never built, so the scene never has the ID twice.
            var twice = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_01_Kerb", "Layer"),
                TestCsv.Row("f2", "g2", "02_02_Gully", "Layer"),
                TestCsv.Row("f3", "g2", "02_03_Other", "Layer"),
                TestCsv.Row("f9", "g2", "02_01_Kerb_Review", "Layer"));
            var second = Plan(scene.Snapshot, twice).Resolve(null);
            Assert.Empty(second.NewHelpers);
            Assert.Contains(second.Rows, r => r.NewName == "02_01_Kerb_Review" && r.Status == PlannerCleanUpStatus.Check && r.Why.Contains("has the same layer ID, 02_01"));
        }

        [Fact]
        public void ASceneWithoutAPhasingRootIsBuiltFromTheList()
        {
            var scene = new TestScene();
            scene.Layer("0");
            scene.Object("Site_Model", null, "0");
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_01_Zone"),
                TestCsv.Row("f1", "g1", "01_01_Drainage", "Layer"),
                TestCsv.Row("f2", "g1", "01_02_Kerbs", "Layer"));
            Assert.Equal(PlannerCleanUp.NothingToDo, PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes()).Message);
            var plan = Plan(scene.Snapshot, list);
            Assert.Null(plan.Message);
            Assert.Equal(PlannerSceneModel.DefaultRootName, plan.RootName);
            var result = plan.Resolve(null);
            Assert.Equal(new[] { Root, "_01_Zone", "01_01_Drainage", "01_02_Kerbs" }, result.NewHelpers.Select(h => h.Name).ToArray());
            Assert.Equal(new[] { Root, "_01_Zone", "01_01_Drainage", "01_02_Kerbs" }, result.NewLayers.Select(l => l.Name).ToArray());
            Assert.Null(result.NewHelpers[0].PlannerId);
            Assert.Equal(new[] { "Phasing root and its layer", "Phase group and its layer", "Layer and helper", "Layer and helper" },
                result.Rows.Where(r => r.Status == PlannerCleanUpStatus.Change).Select(r => r.Item).ToArray());
            Assert.Contains(result.Rows, r => r.NewName == Root && r.Why.StartsWith("the scene has no phasing root", StringComparison.Ordinal));
            var run = Run(plan, result, scene.Snapshot);
            var model = new PlannerSceneModel(run.Scene);
            Assert.Equal(Root, model.RootNode.Name);
            Assert.Equal(Root, model.RootLayer.Name);
            Assert.Equal(3, model.Units.Count(u => u.InPhasingTree && u.Layer != null && u.Helper != null));
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void AMaxLayerTheListMovesWithoutItsHelperHasARowSayingWhere()
        {
            // The helper is in its phase group already; its Max layer was left under the root's. Only the Max layer moves,
            // and its row says so, as the summary counts it.
            var scene = Phasing("_02_Works");
            var kerbs = scene.Unit("02_01_Kerbs", Root, scene.Find("_02_Works"), "f1");
            scene.Object("IN_Kerb", kerbs, "02_01_Kerbs");
            var list = TestCsv.List(TestCsv.Row("g2", null, "_02_Works"), TestCsv.Row("f1", "g2", "02_01_Kerbs", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            Assert.Empty(result.NodeMoves);
            Assert.Equal(new[] { "_02_Works" }, result.MaxLayerMoves.Where(m => m.Name == "02_01_Kerbs").Select(m => m.NewParentName).ToArray());
            var row = result.Rows.Single(r => r.NodeKey == kerbs.Key && r.Status == PlannerCleanUpStatus.Change);
            Assert.Equal("_02_Works", row.LayerItSitsIn);
            Assert.Contains("its Max layer moved under _02_Works, as the Planner's list nests it", row.Why);
            Assert.StartsWith("0 layers and 0 objects to rename, 0 helpers and 1 Max layer to move, ", result.Summary());
            var run = Run(plan, result, scene.Snapshot);
            Assert.Equal("_02_Works", run.Scene.Layers.Single(l => l.Name == "02_01_Kerbs").ParentName);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void ALayerInAnotherPhasesGroupIsListedNotMoved()
        {
            // 02_05 filed in phase 3's group: without the Planner's list, Clean up cannot tell whether the Planner files it
            // there too, so it lists it for a person and moves nothing.
            var scene = Phasing("_02_Works", "_03_Roads");
            var kerbs = Layer(scene, "_03_Roads", "02_05_Kerbs", "IN_Kerb");
            var result = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes()).Resolve(null);
            Assert.Empty(result.NodeMoves);
            Assert.Empty(result.MaxLayerMoves);
            var row = result.Rows.Single(r => r.NodeKey == kerbs.Key && r.Status == PlannerCleanUpStatus.Check);
            Assert.Equal("_03_Roads", row.LayerItSitsIn);
            Assert.Equal("a layer of phase 2 in _03_Roads, a group of phase 3, where the Planner files it too; if it belongs to phase 2, move it into _02_Works by hand", row.Why);
            Assert.EndsWith(", 1 to check.", result.Summary());

            // With no group of its phase, the same, without one to name.
            var alone = Phasing("_03_Roads");
            var posts = Layer(alone, "_03_Roads", "02_06_Posts", "IN_Post");
            var left = PlannerCleanUp.Plan(alone.Snapshot, PhasingNameFixer.DefaultWordFixes()).Resolve(null);
            Assert.Contains(left.Rows, r => r.NodeKey == posts.Key && r.Status == PlannerCleanUpStatus.Check
                && r.Why == "a layer of phase 2 in _03_Roads, a group of phase 3, where the Planner files it too; if it belongs to phase 2, move it by hand");
        }

        [Fact]
        public void TheListKeepsALayerInAnotherPhasesGroupWhereItIs()
        {
            // The Planner's list files 02_05 in _03_Roads: the list wins, and the layer ends where it is.
            var scene = Phasing("_02_Works", "_03_Roads");
            var kerbs = Layer(scene, "_03_Roads", "02_05_Kerbs", "IN_Kerb");
            var list = TestCsv.List(TestCsv.Row("g2", null, "_02_Works"), TestCsv.Row("g3", null, "_03_Roads"), TestCsv.Row("f1", "g3", "02_05_Kerbs", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            var run = Run(plan, result, scene.Snapshot);
            Assert.Equal("_03_Roads", run.Scene.Nodes.Single(n => n.Key == run.Scene.Nodes.Single(k => k.Key == kerbs.Key).ParentKey).Name);
            Assert.Equal("_03_Roads", run.Scene.Layers.Single(l => l.Name == "02_05_Kerbs").ParentName);
            var row = result.Rows.Single(r => r.NodeKey == kerbs.Key && r.Status == PlannerCleanUpStatus.Change);
            Assert.DoesNotContain("moved into", row.Why);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void AMaxLayerWithoutItsHelperSitsInTheMaxLayerItIsInNow()
        {
            var scene = Phasing("_02_Works");
            scene.Layer("02_04_Ducts_20-10-26", "_02_Works");
            scene.Object("Ph2_St04_IN_Duct", null, "02_04_Ducts_20-10-26");
            var result = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes()).Resolve(null);
            var row = result.Rows.Single(r => r.NodeKey == null && r.CurrentName == "02_04_Ducts_20-10-26" && r.Status == PlannerCleanUpStatus.Change);
            Assert.Equal("_02_Works", row.LayerItSitsIn);
        }

        [Fact]
        public void TheListsParentWinsOverThePhaseGroupTheCleanUpWouldChoose()
        {
            // A layer directly under the root: the clean-up alone moves it into the group of its phase; the list nests it in
            // another group. One move, into the list's group, its Max layer too.
            var scene = Phasing("_02_Works", "_03_Roads");
            var ducts = scene.Unit("02_03_Ducts_17-10-26", Root, scene.Find(Root));
            scene.Object("Ph2_St03_IN_Duct", ducts, "02_03_Ducts_17-10-26");
            var alone = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes()).Resolve(null);
            Assert.Contains(alone.NodeMoves, m => m.Key == ducts.Key && m.ToParentKey == scene.Find("_02_Works").Key);
            var list = TestCsv.List(TestCsv.Row("g2", null, "_02_Works"), TestCsv.Row("g3", null, "_03_Roads"), TestCsv.Row("f1", "g3", "02_03_Ducts", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            Assert.Equal(new[] { scene.Find("_03_Roads").Key }, result.NodeMoves.Where(m => m.Key == ducts.Key).Select(m => m.ToParentKey).ToArray());
            Assert.Equal(new[] { "_03_Roads" }, result.MaxLayerMoves.Where(m => m.Name == "02_03_Ducts_17-10-26").Select(m => m.NewParentName).ToArray());
            var row = result.Rows.Single(r => r.NodeKey == ducts.Key && r.Status == PlannerCleanUpStatus.Change);
            // Layer it sits in is where it is now (under the root); What changes says where it goes.
            Assert.Equal(Root, row.LayerItSitsIn);
            Assert.DoesNotContain("moved into", row.Why);
            Assert.Contains("moved under _03_Roads, as the Planner's list nests it", row.Why);
            var run = Run(plan, result, scene.Snapshot);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void APhasingRootWithoutItsMaxLayerGetsOneAndTheLayersGoUnderIt()
        {
            // The root node is there, on layer 0, with no Max layer of its name; the group's Max layer sits at the top.
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Helper(Root, null, "0");
            scene.Layer("_02_Works");
            var works = scene.Helper("_02_Works", root, "_02_Works");
            scene.Layer("02_01_Kerbs_17-10-26", "_02_Works");
            var kerbs = scene.Helper("02_01_Kerbs_17-10-26", works, "02_01_Kerbs_17-10-26");
            scene.Object("Ph2_St01_IN_Kerb", kerbs, "02_01_Kerbs_17-10-26");
            var list = TestCsv.List(TestCsv.Row("g2", null, "_02_Works"), TestCsv.Row("f1", "g2", "02_01_Kerbs", "Layer"));
            var plan = Plan(scene.Snapshot, list);
            var result = plan.Resolve(null);
            Assert.Equal(new[] { Root }, result.NewLayers.Select(l => l.Name).ToArray());
            Assert.Empty(result.NewHelpers);
            Assert.Contains(result.MaxLayerMoves, m => m.Name == "_02_Works" && m.NewParentName == Root && m.NewParentIsNew);
            Assert.Contains(result.Rows, r => r.Item == "Phasing root" && r.Why == "its Max layer Work_Phasing is added (the phasing root had none of its name)");
            Assert.Equal("IN_Kerb", After(scene.Snapshot, result)[scene.Find("Ph2_St01_IN_Kerb").Key]);
            var run = Run(plan, result, scene.Snapshot);
            Assert.Equal(Root, run.Scene.Layers.Single(l => l.Name == "_02_Works").ParentName);
            Assert.False(Plan(run.Scene, list).Resolve(null).HasChanges);
        }

        [Fact]
        public void WhatTheListCannotBringInLineIsListedAndLeftAsItIs()
        {
            // A row whose name another layer keeps, and the row under it: problems, listed, and nothing done for them.
            var scene = Phasing();
            Layer(scene, "_02_Works", "02_01_Kerbs", "IN_Kerb");
            scene.Object("02_09_Fence", null, "0");
            var list = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_01_Kerbs", "Layer"),
                TestCsv.Row("f9", "g2", "02_09_Fence", "Group"),
                TestCsv.Row("f10", "f9", "02_09_1_Fence_Posts", "Layer"));
            var result = Plan(scene.Snapshot, list).Resolve(null);
            var problems = result.Rows.Where(r => r.Status == PlannerCleanUpStatus.Check && r.Why.StartsWith("the Planner's list has it, but it is not brought in line", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "02_09_Fence", "02_09_1_Fence_Posts" }, problems.Select(r => r.NewName).ToArray());
            Assert.DoesNotContain(result.NewHelpers, h => h.Name.StartsWith("02_09", StringComparison.Ordinal));
            Assert.DoesNotContain(result.NewLayers, l => l.Name.StartsWith("02_09", StringComparison.Ordinal));
        }
    }
}
