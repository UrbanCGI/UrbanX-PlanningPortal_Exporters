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

        // ---------------------------------------------------------------------------------------------------------
        // Several phasing roots (naming v2 with several roots, spec 4.4)

        private const string North = "Work_Phasing_North";
        private const string South = "Work_Phasing_South";
        private const string Drainage = "01_01_Drainage";
        private const string Hoarding = "07_01_Hoarding";

        // Two roots, South made first in 3ds Max, each with a phase group and a layer, its helper and a Max layer of its name.
        private static TestScene TwoRoots()
        {
            var scene = new TestScene();
            var south = scene.Unit(South, null, null);
            var north = scene.Unit(North, null, null);
            var enabling = scene.Unit("_01_North_Enabling", North, north);
            scene.Unit(Drainage, "_01_North_Enabling", enabling);
            var area = scene.Unit("_07_South_Enabling", South, south);
            scene.Unit(Hoarding, "_07_South_Enabling", area);
            return scene;
        }

        [Fact]
        public void EveryRootsObjectsGoUnderTheirHelpersAndEachRootLayerGetsItsNote()
        {
            var scene = TwoRoots();
            scene.Object("IN_Pipe", null, Drainage);                            // North, at the top
            scene.Object("IN_Hoarding_RM_02_01", null, Hoarding);               // South, at the top
            scene.Object("IN_Panel", scene.Find(South), Hoarding);              // straight under South's root helper
            scene.Object("IN_Gate", scene.Find(Drainage), Hoarding);            // under the other root's helper: never moved across
            scene.Object("IN_Done", scene.Find(Hoarding), Hoarding);            // already under its helper
            var group = scene.Node("Group001", null, Hoarding, true);           // a Max group: linked by its head
            group.IsGroupHead = true;
            scene.Object("member", group, Hoarding).IsGroupMember = true;
            scene.Object("north_sign", scene.Find(North), North);               // on each root layer: left, with a note
            scene.Object("south_sign", null, South);
            scene.Object("south_sign_2", null, South);
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);

            Assert.Null(plan.Message);
            Assert.Equal(new[] { "IN_Pipe > " + Drainage, "IN_Hoarding_RM_02_01 > " + Hoarding, "IN_Panel > " + Hoarding, "Group001 > " + Hoarding },
                plan.Links.Select(l => l.Name + " > " + l.ToName).ToArray());
            Assert.Equal(South, plan.Links.Single(l => l.Name == "IN_Panel").FromParentName);
            Assert.Equal(1, plan.AlreadyLinked);
            // A node under one root on a Max layer of another (a backup on the same layers) stays in its own root.
            Assert.Equal(1, plan.ProblemCount);
            var gate = plan.Review.Single(r => r.IsProblem);
            Assert.Equal("IN_Gate", gate.Name);
            Assert.Equal("it is under the phasing root " + North + ", and its Max layer is a layer of " + South + "; left as it is", gate.Why);
            // The notes: one per root layer holding objects, in name order (North first, though South was made first).
            Assert.Equal(new[]
            {
                "1 object sits on the phasing root layer " + North + " itself, which holds no work; it is left as it is.",
                "2 objects sit on the phasing root layer " + South + " itself, which holds no work; they are left as they are."
            }, plan.Notes.Where(n => n.Contains("phasing root layer")).ToArray());
            Assert.Contains(plan.Notes, n => n.StartsWith("1 node linked is a Max group, a Point or another helper, or a spline with objects under it (Group001). Under a phasing root"));

            var again = PlannerLinkPlanner.Plan(TestSimulator.Apply(scene.Snapshot, plan));
            Assert.False(again.HasChanges);
            Assert.Equal(5, again.AlreadyLinked);
            Assert.Equal(1, again.ProblemCount);
        }

        [Fact]
        public void ARootWithNoMaxLayerOfItsNameIsPassedOver()
        {
            // South's helper sits on a Max layer of another name, with its layers under that: Link passes it over, as a
            // Problem, and links North's.
            var scene = new TestScene();
            var north = scene.Unit(North, null, null);
            scene.Unit(Drainage, North, north);
            scene.Layer("South_Area");
            var south = scene.Helper(South, null, "South_Area");
            scene.Layer(Hoarding, "South_Area");
            scene.Helper(Hoarding, south);
            scene.Object("IN_Pipe", null, Drainage);
            scene.Object("IN_Hoarding", null, Hoarding);
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);

            Assert.Null(plan.Message);
            Assert.Equal("IN_Pipe", plan.Links.Single().Name);
            var problem = plan.Review.Single(r => r.IsProblem);
            Assert.Equal("Phasing root", problem.Item);
            Assert.Equal(South, problem.Name);
            Assert.Equal("layer South_Area", problem.From);
            Assert.Equal("it has no Max layer of its name, so Link passes over its layers: run Clean up scene graph, which gives it one (it renames South_Area, "
                + "which holds its groups, to " + South + ")", problem.Why);
            Assert.Equal("1 object to link, 0 already under their helpers, 1 problem.", plan.Summary());

            // With no root that has its Max layer, there is nothing to link: one root as before, several named.
            var alone = new TestScene();
            alone.Layer("Site");
            alone.Helper(Root, null, "Site");
            Assert.Equal("The phasing root " + Root + " has no layer of its name, so the scene has no phasing layers yet: run Clean up scene graph first, which gives it one.",
                PlannerLinkPlanner.Plan(alone.Snapshot).Message);
            alone.Helper(North, null, "Site");
            Assert.Equal("The phasing roots " + Root + " and " + North + " have no layers of their names, so the scene has no phasing layers yet: run Clean up scene graph first, which gives each one.",
                PlannerLinkPlanner.Plan(alone.Snapshot).Message);
        }

        [Fact]
        public void ARootOnAnotherRootsMaxLayerIsPassedOverThoughItsLayersSitInThatTree()
        {
            // South's helper was cloned from North's: it sits on North's Max layer, and its group's and layer's Max layers
            // were made under that, with its helpers under South's. The node tree makes them South's, so Link passes them
            // over with South, and links North's.
            var scene = new TestScene();
            var north = scene.Unit(North, null, null);
            scene.Unit(Drainage, North, north);
            var south = scene.Helper(South, null, North);
            scene.Layer("_07_South_Enabling", North);
            var area = scene.Helper("_07_South_Enabling", south);
            scene.Layer(Hoarding, "_07_South_Enabling");
            scene.Helper(Hoarding, area);
            scene.Object("IN_Pipe", null, Drainage);
            scene.Object("IN_Hoarding", null, Hoarding);
            scene.Object("north_sign", null, North);
            var plan = PlannerLinkPlanner.Plan(scene.Snapshot);

            Assert.Null(plan.Message);
            Assert.Equal("IN_Pipe > " + Drainage, plan.Links.Select(l => l.Name + " > " + l.ToName).Single());
            var problem = plan.Review.Single(r => r.IsProblem);
            Assert.Equal(South, problem.Name);
            Assert.Equal("layer " + North, problem.From);
            Assert.StartsWith("it has no Max layer of its name, so Link passes over its layers", problem.Why);
            Assert.Equal("1 object to link, 0 already under their helpers, 1 problem.", plan.Summary());
            // South's helper on North's root layer is no object of it.
            Assert.Equal(new[] { "1 object sits on the phasing root layer " + North + " itself, which holds no work; it is left as it is." },
                plan.Notes.Where(n => n.Contains("phasing root layer")).ToArray());
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
