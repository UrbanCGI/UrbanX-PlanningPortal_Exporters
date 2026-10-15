using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>The MAXScript batch Build layers and helpers runs, and what it reports.</summary>
    public class PlannerLayerScriptTests
    {
        private const string Zone = "_01_Zone5_Working";
        private const string Tm = "_02_Phase1_TM";
        private const string Construction1 = "02_01_1_Construction_31-08-26";
        private const string Odd = "02_02_Quote\"d_and_back\\slash_%_&_!!!_(1)_01-02-27";

        // A scene and a list with every kind of change: a helper and a layer to add, a layer and helper to rename, move
        // and put right, a new row with an awkward name and a Planner ID with a space.
        private static PlannerLayerPlan EveryChange(out PlannerSceneSnapshot scene)
        {
            var built = new TestScene();
            var root = built.Unit("Work_Phasing", null, null);
            built.Layer(Zone, "Work_Phasing");
            var tm = built.Helper(Tm, root, "Work_Phasing");
            built.Layer("02_01_1_Construction_30-08-26", Zone);
            built.Helper("02_01_1_Construction_30-08-26", root, "0", "c1");
            scene = built.Snapshot;
            var list = TestCsv.List(
                TestCsv.Row("g1", null, Zone),
                TestCsv.Row("g2", null, Tm),
                TestCsv.Row("c1", "g2", Construction1),
                TestCsv.Row("id with space", "g2", Odd, "Layer"));
            return PlannerLayerPlanner.Plan(list, scene);
        }

        [Fact]
        public void TheBatchCarriesEveryChangeOfThePlan()
        {
            PlannerSceneSnapshot scene;
            var plan = EveryChange(out scene);
            Assert.Equal(0, plan.ProblemCount);
            var batch = PlannerLayerScript.Build(plan, "__t_");
            var script = batch.Script;
            MaxScriptText.AssertBalanced(script);

            Assert.Equal(plan.BuildLayers.Select(l => l.FinalName).ToArray(), MaxScriptText.ArrayItems(script, "bLayerNew").ToArray());
            Assert.Equal(plan.BuildLayers.Select(l => l.CurrentName ?? string.Empty).ToArray(), MaxScriptText.ArrayItems(script, "bLayerOld").ToArray());
            Assert.Equal(plan.BuildLayers.Select(l => l.ParentIndex.HasValue ? (l.ParentIndex.Value + 1).ToString() : "0").ToArray(), MaxScriptText.ArrayItems(script, "bLayerParent").ToArray());
            Assert.Equal(plan.BuildLayers.Where(l => l.Create).Select(l => (l.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bLayerCreateIdx").ToArray());
            Assert.Equal(plan.BuildLayers.Where(l => l.Rename).Select(l => (l.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bLayerRenameIdx").ToArray());
            Assert.Equal(new[] { "__t_1" }, MaxScriptText.ArrayItems(script, "bLayerRenameTemp").ToArray());
            Assert.Equal(plan.BuildLayers.Where(l => !l.Create && l.Move).Select(l => (l.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bLayerMoveIdx").ToArray());
            Assert.Equal(plan.BuildHelpers.Select(h => h.Key.HasValue ? h.Key.Value.ToString() : "0").ToArray(), MaxScriptText.ArrayItems(script, "bNodeHandle").ToArray());
            Assert.Equal(plan.BuildHelpers.Select(h => h.FinalName).ToArray(), MaxScriptText.ArrayItems(script, "bNodeNew").ToArray());
            Assert.Equal(plan.BuildHelpers.Select(h => (h.LayerIndex + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bNodeLayer").ToArray());
            Assert.Contains("id%20with%20space", MaxScriptText.ArrayItems(script, "bNodeId"));
            Assert.Contains(Odd, MaxScriptText.ArrayItems(script, "bLayerNew"));
            Assert.Equal(plan.BuildHelpers.Where(h => h.Create).Select(h => (h.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bNodeCreateIdx").ToArray());
            Assert.Equal(plan.BuildHelpers.Where(h => h.Rename).Select(h => (h.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bNodeRenameIdx").ToArray());
            Assert.Equal(plan.BuildHelpers.Where(h => !h.Create && h.PutOnLayer).Select(h => (h.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bNodePutIdx").ToArray());
            Assert.Equal(plan.BuildHelpers.Where(h => !h.Create && h.Move).Select(h => (h.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bNodeMoveIdx").ToArray());
            Assert.Equal(plan.BuildHelpers.Where(h => !h.Create && h.StampId != null).Select(h => (h.Index + 1).ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bNodeStampIdx").ToArray());

            // Every kind is there at least once.
            Assert.Equal(1, batch.LayerRenames);
            Assert.True(batch.LayerCreates >= 2);
            Assert.Equal(1, batch.LayerMoves);
            Assert.Equal(1, batch.HelperRenames);
            Assert.True(batch.HelperCreates >= 2);
            Assert.Equal(2, batch.HelperPuts);
            Assert.Equal(1, batch.HelperMoves);
            // The new helpers' IDs and the TM group's; the construction layer's helper already has its own.
            Assert.Equal(3, batch.Stamps);

            // One undo record; everything found before it; the steps in their order.
            Assert.Equal(1, Occurrences(script, "undo \"Build Planner layers\" on"));
            Assert.True(script.IndexOf("maxOps.getNodeByHandle", StringComparison.Ordinal) < script.IndexOf("undo \"", StringComparison.Ordinal));
            Assert.True(script.IndexOf("LayerManager.getLayerFromName", StringComparison.Ordinal) < script.IndexOf("undo \"", StringComparison.Ordinal));
            var steps = new[]
            {
                "bLayer.setName bLayerRenameTemp[k]",
                "bLayer.setName bLayerNew[i]",
                "LayerManager.newLayerFromName bLayerNew[i]",
                "bOther = bLayer.getParent()",
                "bNode.name = bNodeNew[i]",
                "Dummy name:bNodeNew[i] pos:[0,0,0] boxsize:[1,1,1]",
                "bHelpersPut += 1",
                "bHelpersMoved += 1",
                "for k = 1 to bNodeStampIdx.count do"
            };
            var at = steps.Select(s => script.IndexOf(s, StringComparison.Ordinal)).ToList();
            Assert.All(at, i => Assert.True(i > 0));
            Assert.Equal(at.OrderBy(i => i).ToList(), at);
            Assert.Contains("attachNodesToGroup #(bNode) bParent", script);
            Assert.Contains("setUserProp bNode \"planner_id\" bNodeId[i]", script);
            Assert.Contains("format \"" + PlannerLayerScript.EndMarker + "\\n\" to:bLog", script);
            Assert.DoesNotContain("delete", script);
            // CRLF throughout.
            Assert.DoesNotContain("\n", script.Replace("\r\n", string.Empty));
        }

        [Fact]
        public void AnEmptyPlanGivesAValidBatch()
        {
            var scene = new TestScene();
            scene.Unit("Work_Phasing", null, null);
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(), scene.Snapshot);
            Assert.False(plan.HasChanges);
            var batch = PlannerLayerScript.Build(plan, null);
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Empty(MaxScriptText.ArrayItems(batch.Script, "bLayerCreateIdx"));
            Assert.Equal(new[] { "Work_Phasing" }, MaxScriptText.ArrayItems(batch.Script, "bLayerOld").ToArray());
        }

        [Fact]
        public void AHelperWhoseHandleMaxScriptCannotTakeIsLeftOut()
        {
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            scene.Layer(Zone, "Work_Phasing");
            scene.Snapshot.Nodes.Add(new PlannerSceneNode { Key = 3000000000L, Name = Zone, ParentKey = root.Key, IsHelper = true, LayerName = Zone });
            var plan = PlannerLayerPlanner.Plan(TestCsv.List(TestCsv.Row("g1", null, Zone)), scene.Snapshot);
            var batch = PlannerLayerScript.Build(plan, "__t_");
            Assert.Contains(batch.Refused, r => r.Contains("node handle cannot be passed to MAXScript"));
            Assert.DoesNotContain("3000000000", batch.Script);
            Assert.Empty(MaxScriptText.ArrayItems(batch.Script, "bNodeStampIdx"));
            var result = PlannerLayerScript.ReadResult(batch, "DONE\t0\t0\t0\t0\t0\t0\t0\t0\n" + PlannerLayerScript.EndMarker + "\n");
            Assert.True(result.HasProblems);
            Assert.Single(result.Failures);
        }

        [Fact]
        public void TheResultReadsCountsAndNamesEveryFailure()
        {
            PlannerSceneSnapshot scene;
            var plan = EveryChange(out scene);
            var batch = PlannerLayerScript.Build(plan, "__t_");
            var renamed = plan.BuildLayers.Single(l => l.Rename);
            var created = plan.BuildLayers.First(l => l.Create && l.Index > 0);
            var helper = plan.BuildHelpers.Single(h => h.Rename);
            var newHelper = plan.BuildHelpers.First(h => h.Create && h.Index > 0);
            Func<int, string> n = i => (i + 1).ToString();
            var output = string.Join("\n", new[]
            {
                "FAIL\tlayerfind\t" + n(renamed.Index) + "\tgone",
                "FAIL\tfind\t" + n(helper.Index) + "\tchanged",
                "FAIL\tlayer\t" + n(renamed.Index) + "\ttaken",
                "FAIL\tlayer\t" + n(renamed.Index) + "\tstuck",
                "FAIL\tlayercreate\t" + n(created.Index) + "\ttaken",
                "FAIL\thelpercreate\t" + n(newHelper.Index) + "\tnolayer",
                "FAIL\thelpermove\t" + n(helper.Index) + "\terror\t-- Runtime error: no",
                "FAIL\tstamp\t" + n(helper.Index) + "\terror\tbad",
                "FAIL\tweird\t99\tcode",
                "DONE\t1\t2\t3\t4\t5\t6\t7\t8",
                PlannerLayerScript.EndMarker,
                string.Empty
            });
            var result = PlannerLayerScript.ReadResult(batch, output);
            Assert.True(result.Completed);
            Assert.Null(result.Aborted);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new[] { result.LayersRenamed, result.LayersCreated, result.LayersMoved, result.HelpersRenamed, result.HelpersCreated, result.HelpersMoved, result.HelpersPutOnLayer, result.IdsStamped });
            Assert.Equal("the layer " + renamed.CurrentName + " is no longer in the scene, so it was not changed", result.Failures[0]);
            Assert.Equal("the helper " + helper.CurrentName + " has been renamed since the list was made, so it was not changed", result.Failures[1]);
            Assert.Equal("the layer " + renamed.CurrentName + " could not become " + renamed.FinalName + ": that name is taken; it keeps its name", result.Failures[2]);
            Assert.Equal("the layer " + renamed.CurrentName + " could not become " + renamed.FinalName + " and is left as __t_1 - rename it by hand", result.Failures[3]);
            Assert.Equal("the layer " + created.FinalName + " was not created: a layer of that name is already there", result.Failures[4]);
            Assert.Equal("the helper " + newHelper.FinalName + " was not created: its layer is missing", result.Failures[5]);
            Assert.Contains("3ds Max reported an error: -- Runtime error: no", result.Failures[6]);
            Assert.Contains("did not get its Planner ID", result.Failures[7]);
            Assert.StartsWith("an item of the build was not carried out (weird 99, code)", result.Failures[8]);

            var text = result.Describe(null, true);
            Assert.Contains("Done: 2 layers created, 5 helpers created, 1 layer renamed, 4 helpers renamed, 3 layers moved, 6 helpers moved, 7 helpers put on their layers and 8 helpers given their Planner IDs.", text);
            Assert.Contains("9 changes were not made:", text);
            Assert.EndsWith("Edit > Fetch puts the scene back as it was before the build.", text);
        }

        [Fact]
        public void TheResultCopesWithOddOutput()
        {
            PlannerSceneSnapshot scene;
            var batch = PlannerLayerScript.Build(EveryChange(out scene), "__t_");
            var none = PlannerLayerScript.ReadResult(batch, null);
            Assert.False(none.Completed);
            Assert.True(none.HasProblems);
            Assert.StartsWith("3ds Max did not report back", none.Describe(null, false));

            var printed = PlannerLayerScript.ReadResult(batch, "\"DONE\\t0\\t1\\t0\\t0\\t0\\t0\\t0\\t0\\n" + PlannerLayerScript.EndMarker + "\\n\"");
            Assert.True(printed.Completed);
            Assert.Equal(1, printed.LayersCreated);

            var aborted = PlannerLayerScript.ReadResult(batch, "ABORT\t-- Unknown property\r\nDONE\t0\t0\t0\t0\t0\t0\t0\t0\r\n" + PlannerLayerScript.EndMarker + "\r\n");
            Assert.True(aborted.Completed);
            Assert.Equal("-- Unknown property", aborted.Aborted);
            Assert.Contains("The build stopped part-way", aborted.Describe(null, false));
            Assert.Contains("Nothing was changed.", aborted.Describe(null, false));
        }

        [Fact]
        public void AnAppliedPlanLeavesNothingToDo()
        {
            PlannerSceneSnapshot scene;
            var plan = EveryChange(out scene);
            var after = TestSimulator.Apply(scene, plan);
            var list = plan.List;
            var again = PlannerLayerPlanner.Plan(list, after);
            Assert.False(again.HasChanges);
            var result = PlannerLayerScript.ReadResult(PlannerLayerScript.Build(plan, "__t_"), "DONE\t1\t3\t1\t1\t3\t1\t2\t4\n" + PlannerLayerScript.EndMarker + "\n");
            Assert.EndsWith("Read again from the scene: 0 new, 0 to change, 4 up to date, 0 problems.", result.Describe(again, false));
        }

        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }
}
