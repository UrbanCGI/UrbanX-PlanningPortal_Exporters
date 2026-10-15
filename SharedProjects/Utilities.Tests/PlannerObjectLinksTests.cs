using System;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Link objects to their helpers: what is linked, what is left, and the MAXScript.</summary>
    public class PlannerObjectLinksTests
    {
        private const string Root = "Work_Phasing";
        private const string A = "01_01_Kerbs_01-02-27";
        private const string B = "01_02_Gully_02-02-27";
        private const string C = "01_03_Ducts_03-02-27";

        // A phasing root with layers A and B (each with its helper) and C (no helper), and objects of every kind.
        private static TestScene Scene()
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            var a = scene.Unit(A, Root, root);
            var b = scene.Unit(B, Root, root);
            scene.Layer(C, Root);
            scene.Layer("Site");

            scene.Object("loose", null, A);                         // at the top: linked
            var parent = scene.Object("parent", null, A);           // a hierarchy: linked by its top
            scene.Object("child", parent, A);
            var group = scene.Node("Group001", null, A, true);      // a Max group: linked by its head
            group.IsGroupHead = true;
            var m1 = scene.Object("member1", group, A);
            m1.IsGroupMember = true;
            var m2 = scene.Object("member2", group, A);
            m2.IsGroupMember = true;
            scene.Object("done", a, A);                             // already under its helper
            scene.Object("wrong", b, A);                            // under another layer's helper: moved
            var site = scene.Object("site_pole", null, "Site");     // linked under an object of another layer
            scene.Object("on_pole", site, A);
            var path = scene.Node("RailClone_path", null, A, false); // a spline nothing hangs under: not exported
            path.IsShape = true;
            var shaped = scene.Node("Sweep_path", null, A, false);  // a spline with an object under it
            shaped.IsShape = true;
            scene.Object("swept", shaped, A);
            scene.Node("Pivot", null, A, true);                     // a stray dummy: not an object
            scene.Object("duct", null, C);                          // a layer without its helper
            scene.Object("on_root", root, Root);                    // the root layer holds no work
            return scene;
        }

        [Fact]
        public void EveryObjectOfAPhasingLayerGoesUnderItsHelper()
        {
            var scene = Scene();
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);

            Assert.Null(plan.Message);
            Assert.Equal(new[] { "loose", "parent", "Group001", "wrong", "Sweep_path" }, plan.Links.Select(l => l.Name).ToArray());
            Assert.All(plan.Links, l => Assert.Equal(A, l.ToName));
            Assert.Equal(2, plan.Links.Single(l => l.Name == "parent").Objects);
            Assert.Equal(2, plan.Links.Single(l => l.Name == "Group001").Objects);
            Assert.Equal(2, plan.Links.Single(l => l.Name == "Sweep_path").Objects);
            Assert.Equal(B, plan.Links.Single(l => l.Name == "wrong").FromParentName);
            Assert.Equal(8, plan.ObjectsToLink);
            Assert.Equal(1, plan.AlreadyLinked);
            Assert.Equal(2, plan.ProblemCount);
            Assert.Contains(plan.Review, r => r.IsProblem && r.Name == "on_pole" && r.Why.Contains("site_pole, which sits on the layer Site"));
            Assert.Contains(plan.Review, r => r.IsProblem && r.Name == "1 object" && r.Why.Contains("has no helper"));
            Assert.Contains(plan.Review, r => r.Item == "Group" && r.Name == "Group001" && r.Why == "on the layer " + A + " (with 1 more object of the layer under it)");
            Assert.Contains(plan.Notes, n => n.StartsWith("1 object sits on the phasing root layer " + Root));
            // The group head and the spline with an object under it are exported as empty nodes: the export log will
            // count them as helper nodes, which the review says.
            Assert.Contains(plan.Notes, n => n.StartsWith("2 nodes linked are Max groups, Points or other helpers, or splines with objects under them (Group001, Sweep_path)."));
            Assert.Equal("8 objects to link (in 5 links), 1 already under their helpers, 2 problems.", plan.Summary());

            var after = TestSimulator.Apply(scene.Snapshot, plan);
            var again = PlannerLinkPlanner.Plan(after);
            Assert.False(again.HasChanges);
            Assert.Equal(9, again.AlreadyLinked);
            Assert.Equal(2, again.ProblemCount);
            Assert.DoesNotContain(again.Notes, n => n.Contains("Max groups"));
            var note = PlannerLayerPlannerTests.CountsOf(after).PairingNote();
            Assert.Contains("Group001", note);
            Assert.Contains("Sweep_path", note);
        }

        [Fact]
        public void LinkingObjectsAloneLeavesTheExportCountsPaired()
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            scene.Unit(A, Root, root);
            scene.Object("Ph1_St01_IN_Kerb", null, A);
            var parent = scene.Object("Ph1_St01_IN_Gully", null, A);
            scene.Object("Ph1_St01_IN_Grate", parent, A);
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);
            Assert.Equal(2, plan.Links.Count);
            Assert.DoesNotContain(plan.Notes, n => n.Contains("Max groups"));
            Assert.Null(PlannerLayerPlannerTests.CountsOf(TestSimulator.Apply(scene.Snapshot, plan)).PairingNote());
        }

        [Fact]
        public void AHelperUnderAnObjectIsNotLinkedInACircle()
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            var box = scene.Object("box", root, A);
            scene.Layer(A, Root);
            scene.Helper(A, box, A);
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);
            Assert.Empty(plan.Links);
            Assert.Contains(plan.Review, r => r.IsProblem && r.Why.Contains("is linked under box"));
        }

        [Fact]
        public void WithoutAPhasingRootThereIsNothingToLink()
        {
            var scene = new TestScene();
            scene.Layer(A);
            scene.Object("loose", null, A);
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);
            Assert.Equal(PlannerLinkPlanner.NoRoot, plan.Message);
            Assert.Equal(PlannerLinkPlanner.NoRoot, plan.Summary());
            Assert.False(plan.HasChanges);
        }

        [Fact]
        public void TheBatchLinksByHandleInOneUndoRecord()
        {
            var plan = PlannerLinkPlanner.Plan(Scene().Snapshot);
            var batch = PlannerLinkScript.Build(plan);
            var script = batch.Script;
            MaxScriptText.AssertBalanced(script);
            Assert.Equal(plan.Links.Select(l => l.Key.ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bHandles").ToArray());
            Assert.Equal(plan.Links.Select(l => l.Name).ToArray(), MaxScriptText.ArrayItems(script, "bNames").ToArray());
            Assert.Equal(plan.Links.Select(l => l.ToKey.ToString()).ToArray(), MaxScriptText.ArrayItems(script, "bParents").ToArray());
            Assert.Contains("undo \"Link objects to their helpers\" on", script);
            Assert.Contains("attachNodesToGroup #(bNode) bParent", script);
            Assert.Contains("bNode.parent = bParent", script);
            Assert.Contains("format \"" + PlannerLinkScript.EndMarker + "\\n\" to:bLog", script);
            Assert.DoesNotContain("\n", script.Replace("\r\n", string.Empty));

            var result = PlannerLinkScript.ReadResult(batch, "FAIL\tlink\t1\tchanged\nFAIL\tlink\t2\tgone\nFAIL\tlink\t3\trefused\nFAIL\tlink\t4\terror\tboom\nDONE\t1\n" + PlannerLinkScript.EndMarker + "\n");
            Assert.True(result.Completed);
            Assert.Equal(1, result.Linked);
            Assert.Equal("loose was not linked under " + A + ": one of them has been renamed since the list was made", result.Failures[0]);
            Assert.Equal("parent was not linked under " + A + ": one of them is no longer in the scene", result.Failures[1]);
            Assert.Equal("Group001 could not be linked under " + A, result.Failures[2]);
            Assert.Equal("wrong was not linked under " + A + ": 3ds Max reported an error: boom", result.Failures[3]);
            var text = result.Describe(null, true);
            Assert.StartsWith("Done: 1 node linked under its layer's helper.", text);
            Assert.Contains("4 links were not made:", text);
        }
    }
}
