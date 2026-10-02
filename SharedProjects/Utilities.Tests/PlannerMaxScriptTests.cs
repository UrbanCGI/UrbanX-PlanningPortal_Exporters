using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The MAXScript the Planner layers panel runs to apply a plan. 3ds Max is not available to the tests, so these
    /// pin the text: one undo record, one try/catch per operation reporting its index, names as escaped literals,
    /// nodes by handle, created helpers by slot, and user properties left to the SDK.
    /// </summary>
    public class PlannerMaxScriptTests
    {
        private static PlannerSceneNode Node(string id, string name)
        {
            return new PlannerSceneNode { Id = id, Name = name };
        }

        [Theory]
        [InlineData("01_Zone5_Working", "\"01_Zone5_Working\"")]
        [InlineData("Say \"hi\"", "\"Say \\\"hi\\\"\"")]
        [InlineData(@"C:\models\a", "\"C:\\\\models\\\\a\"")]
        [InlineData("two\r\nlines\tand tab", "\"two\\r\\nlines\\tand tab\"")]
        [InlineData("02-02.1_Integration_!!!FULL_CLOSURE!!! (1)", "\"02-02.1_Integration_!!!FULL_CLOSURE!!! (1)\"")]
        [InlineData(null, "\"\"")]
        public void QuoteEscapesForMaxScript(string text, string expected)
        {
            Assert.Equal(expected, PlannerMaxScript.Quote(text));
        }

        [Fact]
        public void BuildScriptsLayerHelperAndLinkOperationsAndLeavesPropertiesToTheCaller()
        {
            var root = Node("new:1", "Work_Phasing");
            var helper = Node("57", "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26");
            var pile = Node("88", "Ph1_St04_IN_Sheet_Pile_3");
            var ops = new List<PlannerSceneOp>
            {
                PlannerSceneOp.CreateLayer("Work_Phasing", null),                                                        // 0
                PlannerSceneOp.CreateHelper("new:1", "Work_Phasing", "Work_Phasing"),                                    // 1
                PlannerSceneOp.SetUserProp(root, PlannerProps.Root, "true"),                                             // 2
                PlannerSceneOp.SetLayerParent("_01_Zone5_Working", "Work_Phasing"),                                      // 3
                PlannerSceneOp.RenameLayer("01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26", "01-04.1_Phase1_Retainment_Installation"), // 4
                PlannerSceneOp.RenameHelper(helper, "01-04.1_Phase1_Retainment_Installation"),                          // 5
                PlannerSceneOp.LinkNode(helper, root),                                                                   // 6
                PlannerSceneOp.LinkNode(pile, helper),                                                                   // 7
                PlannerSceneOp.MoveNodeToLayer(helper, "01-04.1_Phase1_Retainment_Installation"),                       // 8
                PlannerSceneOp.ClearUserProp(helper, PlannerProps.LegacyStart),                                          // 9
                PlannerSceneOp.CreateLayer("01-00_START_DATE", "01_Zone5_Working"),                                      // 10
                PlannerSceneOp.LinkNode(pile, null)                                                                      // 11
            };

            var batch = PlannerMaxScript.Build(ops, "Planner layers");
            var script = batch.Script;

            Assert.Equal(new[] { 0, 1, 3, 4, 5, 6, 7, 8, 10, 11 }, batch.ScriptedOperations.ToArray());
            Assert.Equal(new[] { 2, 9 }, batch.PropertyOperations.ToArray());
            Assert.Equal(1, batch.NewHelperSlots["new:1"]);

            Assert.StartsWith("(", script.TrimStart());
            Assert.Contains("undo \"Planner layers\" on", script);
            Assert.Contains("try (plannerCreateLayer \"Work_Phasing\" undefined) catch (plannerFail plannerOut 0 (getCurrentException()))", script);
            Assert.Contains("try (plannerPoint plannerMade 1 \"Work_Phasing\" \"Work_Phasing\") catch (plannerFail plannerOut 1 (getCurrentException()))", script);
            Assert.Contains("try (plannerSetParent \"_01_Zone5_Working\" \"Work_Phasing\") catch (plannerFail plannerOut 3 (getCurrentException()))", script);
            Assert.Contains("try (plannerRenameLayer \"01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26\" \"01-04.1_Phase1_Retainment_Installation\") catch (plannerFail plannerOut 4", script);
            Assert.Contains("try (plannerName (plannerNode 57) \"01-04.1_Phase1_Retainment_Installation\") catch (plannerFail plannerOut 5", script);
            Assert.Contains("try (plannerLink (plannerNode 57) (plannerNew plannerMade 1)) catch (plannerFail plannerOut 6", script);
            Assert.Contains("try (plannerLink (plannerNode 88) (plannerNode 57)) catch (plannerFail plannerOut 7", script);
            Assert.Contains("try (plannerMoveTo (plannerNode 57) \"01-04.1_Phase1_Retainment_Installation\") catch (plannerFail plannerOut 8", script);
            Assert.Contains("try (plannerCreateLayer \"01-00_START_DATE\" \"01_Zone5_Working\") catch (plannerFail plannerOut 10", script);
            Assert.Contains("try (plannerLink (plannerNode 88) undefined) catch (plannerFail plannerOut 11", script);
            // The helper functions keep every local inside a function body, and check what 3ds Max did.
            Assert.Contains("fn plannerCreateLayer n parentName = (local l = LayerManager.newLayerFromName n; if l == undefined do throw", script);
            // The new name must come back exactly (case included); the layer is resolved before the Point is made.
            Assert.Contains("fn plannerRenameLayer oldName newName = (local l = plannerLayer oldName; l.setName newName; if l.name != newName do throw", script);
            Assert.Contains("fn plannerPoint arr k n layerName = (local l = plannerLayer layerName; local p = Point name:n; l.addNode p; arr[k] = p; p)", script);
            Assert.DoesNotContain("stricmp", script);
            Assert.Contains("fn plannerLink o p = (o.parent = p; o)", script);
            Assert.DoesNotContain("try (local", script);
            Assert.DoesNotContain("planner_root", script);
            Assert.DoesNotContain("setUserProp", script);
            Assert.Contains("for k = 1 to plannerMade.count where isValidNode plannerMade[k] do format \"N\\t%\\t%\\n\" k plannerMade[k].inode.handle to:plannerOut", script);
            Assert.Contains("\"PLANNER-LAYERS-DONE\\n\" + (plannerOut as string)", script);

            // Balanced brackets and quotes, so 3ds Max can at least parse the block.
            Assert.Equal(script.Count(c => c == '('), script.Count(c => c == ')'));
            Assert.Equal(script.Count(c => c == '['), script.Count(c => c == ']'));
            Assert.Equal(0, script.Replace("\\\\", string.Empty).Replace("\\\"", string.Empty).Count(c => c == '"') % 2);
        }

        [Fact]
        public void AnEmptyParentNameMeansTheTopLevel()
        {
            var batch = PlannerMaxScript.Build(new[] { PlannerSceneOp.CreateLayer("Work_Phasing", ""), PlannerSceneOp.SetLayerParent("Misc", "") }, null);
            Assert.Contains("try (plannerCreateLayer \"Work_Phasing\" undefined)", batch.Script);
            Assert.Contains("try (plannerSetParent \"Misc\" undefined)", batch.Script);
            Assert.DoesNotContain("plannerLayer \"\"", batch.Script);
        }

        [Fact]
        public void BuildWithOnlyPropertiesHasNoScript()
        {
            var node = Node("12", "01_Zone5_Working");
            var batch = PlannerMaxScript.Build(new[] { PlannerSceneOp.SetUserProp(node, PlannerProps.Code, "01") }, null);
            Assert.Null(batch.Script);
            Assert.Equal(new[] { 0 }, batch.PropertyOperations.ToArray());
        }

        [Fact]
        public void BuildRefusesIdsThatAreNotHandles()
        {
            var bad = Node("o5", "Sheet_Pile");
            Assert.Throws<InvalidOperationException>(() => PlannerMaxScript.Build(new[] { PlannerSceneOp.LinkNode(bad, null) }, null));
            Assert.Throws<InvalidOperationException>(() => PlannerMaxScript.Build(new[] { PlannerSceneOp.LinkNode(Node("12", "a"), Node("new:3", "b")) }, null));
            Assert.Throws<InvalidOperationException>(() => PlannerMaxScript.Build(new[] { PlannerSceneOp.CreateHelper("12", "a", "L") }, null));
        }

        [Fact]
        public void AdoptPlanOnTheFixtureBuildsAScriptForEveryStructuralOperation()
        {
            // The fixture's ids are not handles; map them to numbers the way the 3ds Max scene reader would.
            var scene = PlannerLayerPlansTests.BuildFixture();
            var number = 100;
            var map = scene.Nodes.ToDictionary(n => n.Id, n => (number++).ToString());
            foreach (var node in scene.Nodes)
            {
                node.ParentId = node.ParentId != null ? map[node.ParentId] : null;
                node.Id = map[node.Id];
            }
            var plan = PlannerLayerPlans.AdoptPlan(scene, null);
            var batch = PlannerMaxScript.Build(plan.Operations, "Planner layers");

            Assert.Equal(plan.Operations.Count, batch.ScriptedOperations.Count + batch.PropertyOperations.Count);
            Assert.Equal(plan.Operations.Count(o => o.Kind == PlannerSceneOpKind.CreateHelper), batch.NewHelperSlots.Count);
            foreach (var index in batch.ScriptedOperations)
            {
                Assert.Contains("catch (plannerFail plannerOut " + index + " (getCurrentException()))", batch.Script);
            }
            Assert.Equal(batch.Script.Count(c => c == '('), batch.Script.Count(c => c == ')'));
        }

        [Fact]
        public void ParseResultReadsCreatedHandlesAndFailures()
        {
            var output = "PLANNER-LAYERS-DONE\nF\t4\t-- Runtime error: There is no layer 01_Old\nN\t1\t1201\nN\t2\t1202\nF\t9\t3ds Max refused the new layer name (is it taken?)\n";
            var result = PlannerMaxScript.ParseResult(output);
            Assert.True(result.Completed);
            Assert.Equal(1201u, result.CreatedHandles[1]);
            Assert.Equal(1202u, result.CreatedHandles[2]);
            Assert.Equal("There is no layer 01_Old", result.Failures[4]);
            Assert.Equal("3ds Max refused the new layer name (is it taken?)", result.Failures[9]);
        }

        [Fact]
        public void ParseResultToleratesThePrintedFormAndMissingOutput()
        {
            var printed = PlannerMaxScript.ParseResult("\"PLANNER-LAYERS-DONE\\nN\\t1\\t77\\n\"");
            Assert.True(printed.Completed);
            Assert.Equal(77u, printed.CreatedHandles[1]);

            Assert.False(PlannerMaxScript.ParseResult(null).Completed);
            Assert.False(PlannerMaxScript.ParseResult("-- Syntax error: at ), expected <factor>").Completed);
        }

        [Fact]
        public void CreatedHelpersAreFoundByNameWhenMaxDoesNotReportBack()
        {
            var moved = Node("57", "Old_Helper");
            var ops = new List<PlannerSceneOp>
            {
                PlannerSceneOp.CreateLayer("Work_Phasing", null),
                PlannerSceneOp.CreateHelper("new:1", "Work_Phasing", "Work_Phasing"),
                PlannerSceneOp.CreateHelper("new:2", "01_Zone", "01_Zone"),
                PlannerSceneOp.RenameLayer("01_Zone", "01_Zone_North"),                          // the layer is renamed after the helper is made
                PlannerSceneOp.RenameHelper(Node("new:2", "01_Zone"), "01_Zone_North"),
                PlannerSceneOp.CreateHelper("new:3", "02_Missing", "02_Missing"),                 // never made in the scene
                PlannerSceneOp.MoveNodeToLayer(moved, "Work_Phasing")                             // not a created helper
            };

            var places = PlannerMaxScript.CreatedHelperPlaces(ops);
            Assert.Equal(3, places.Count);
            Assert.Equal(new KeyValuePair<string, string>("01_Zone_North", "01_Zone_North"), places["new:2"]);

            // The scene as read back: an older, marked helper of the same name, the new one, and a group head.
            var scene = new PlannerScene();
            scene.Nodes.Add(new PlannerSceneNode { Id = "900", Name = "Work_Phasing", LayerName = "Work_Phasing", IsHelper = true });
            scene.Nodes.Add(new PlannerSceneNode { Id = "40", Name = "01_Zone_North", LayerName = "01_Zone_North", IsHelper = true, Props = { { PlannerProps.Code, "01" } } });
            scene.Nodes.Add(new PlannerSceneNode { Id = "905", Name = "01_Zone_North", LayerName = "01_zone_north", IsHelper = true });
            scene.Nodes.Add(new PlannerSceneNode { Id = "910", Name = "01_Zone_North", LayerName = "01_Zone_North", IsHelper = true, IsGroupHead = true });

            var resolved = PlannerMaxScript.ResolveCreatedHelpers(scene, ops);
            Assert.Equal("900", resolved["new:1"]);
            Assert.Equal("905", resolved["new:2"]);   // unmarked before marked; layer names compare without case; group heads never
            Assert.False(resolved.ContainsKey("new:3"));
        }
    }
}
