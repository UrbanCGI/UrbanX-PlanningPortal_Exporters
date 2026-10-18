using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Clean up scene graph's plan (naming v2, spec 5.1 and 6.1): layers, links, objects, questions and numbering.</summary>
    public class PlannerCleanUpTests
    {
        private const string Root = "Work_Phasing";

        // A phasing root (its layer and helper) with phase groups _01_Zone, _02_Works and _03_Roads.
        private static TestScene Phasing(out PlannerSceneNode root, params string[] groups)
        {
            var scene = new TestScene();
            root = scene.Unit(Root, null, null);
            foreach (var group in groups.Length > 0 ? groups : new[] { "_01_Zone", "_02_Works", "_03_Roads" })
            {
                scene.Unit(group, Root, root);
            }
            return scene;
        }

        // A layer and its helper under a group, with objects under the helper on its layer.
        private static PlannerSceneNode Layer(TestScene scene, string group, string name, params string[] objects)
        {
            var helper = scene.Unit(name, group, scene.Find(group));
            foreach (var obj in objects)
            {
                scene.Object(obj, helper, name);
            }
            return helper;
        }

        // Every node's name after the result, by its current name (the test scenes' names are unique).
        private static Dictionary<string, string> After(PlannerSceneSnapshot scene, PlannerCleanUpResult result)
        {
            var renamed = result.NodeRenames.ToDictionary(r => r.Key, r => r.NewName);
            return scene.Nodes.ToDictionary(n => n.Name, n =>
            {
                string name;
                return renamed.TryGetValue(n.Key, out name) ? name : n.Name;
            }, StringComparer.Ordinal);
        }

        private static PlannerCleanUpPlan Plan(TestScene scene)
        {
            return PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
        }

        // -------------------------------------------------------------------------------------------------------------
        // Layers and helpers

        [Fact]
        public void LayerNamesLoseTheirDatesAndTbcAndNothingInMaxKeepsThem()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root, "_01_Zone", "_02_Works", "_06_Weekend");
            Layer(scene, "_01_Zone", "01_00_START_DATE_01-06-26");
            Layer(scene, "_01_Zone", "01_01_Drainage_TBC_08-06-26_03-07-26", "Ph1_St01_IN_Gully");
            Layer(scene, "_02_Works", "02_01_4_Construction_of_Phase1_TM_TBC_07-09-26_05-02-27", "Ph2_St01_4_IN_Hoarding");
            Layer(scene, "_02_Works", "02_02_8_MainRd_Hoardings_inst_TBC_!!!FULL_CLOSURE!!!_17-10-26_18-10-26", "Ph2_St02_8_IN_Barrier");
            Layer(scene, "_06_Weekend", "06_04_Temporary_Road-Road_Marking_06-02-27", "Ph6_St04_IN_White_Lines");
            Layer(scene, "_06_Weekend", "06_05_Gates_TBC", "Ph6_St05_IN_Gate");
            var plan = Plan(scene);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("01_00_START_DATE", after["01_00_START_DATE_01-06-26"]);
            Assert.Equal("01_01_Drainage", after["01_01_Drainage_TBC_08-06-26_03-07-26"]);
            Assert.Equal("02_01_4_Construction_of_Phase1_TM", after["02_01_4_Construction_of_Phase1_TM_TBC_07-09-26_05-02-27"]);
            Assert.Equal("02_02_8_MainRd_Hoardings_inst_!!!FULL_CLOSURE!!!", after["02_02_8_MainRd_Hoardings_inst_TBC_!!!FULL_CLOSURE!!!_17-10-26_18-10-26"]);
            Assert.Equal("06_04_Temporary_Road-Road_Marking", after["06_04_Temporary_Road-Road_Marking_06-02-27"]);
            Assert.Equal("06_05_Gates", after["06_05_Gates_TBC"]);
            Assert.Equal(6, result.LayersRenamed);

            // The Max layers are renamed with their helpers; the groups and the root are left as they are.
            Assert.Equal(6, result.MaxLayerRenames.Count);
            Assert.All(result.MaxLayerRenames, r => Assert.Equal(after[r.OldName], r.NewName));
            Assert.Empty(result.NodeMoves);

            // The dates and TBC taken out are kept nowhere in 3ds Max (D20): no user property, no row says so, and the batch
            // writes none.
            var row = result.Rows.Single(r => r.CurrentName == "01_01_Drainage_TBC_08-06-26_03-07-26");
            Assert.Equal(PlannerCleanUpStatus.Change, row.Status);
            Assert.Equal("Layer and helper", row.Item);
            Assert.Equal("dates and TBC taken out of the name", row.Why);
            Assert.DoesNotContain(result.Rows, r => r.Why.Contains("kept") || r.Why.Contains("user propert") || r.Why.Contains("08-06-26"));
            var script = PlannerCleanUpScript.Build(plan, result, "__t_").Script;
            Assert.DoesNotContain("setUserProp", script.Replace("setUserProp bNode \"planner_id\"", string.Empty));
            Assert.DoesNotContain("planner_start", script);
            Assert.DoesNotContain("planner_finish", script);
            Assert.DoesNotContain("planner_tbc", script);
            Assert.DoesNotContain("2026", script);
            Assert.DoesNotContain("D\t", result.Fingerprint());
        }

        [Fact]
        public void IdsAreWrittenTheCurrentWayAndNothingIsRenumbered()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_05-1_Kerbs_17-10-26", "Ph2_St05-1_IN_Kerb");
            Layer(scene, "_02_Works", "02_05.2_Kerbs_18-10-26", "Ph2_St05.2_IN_Kerb_B");
            Layer(scene, "_02_Works", "2_6_Gully_19-10-26", "Ph2_St06_IN_Gully");
            Layer(scene, "_02_Works", "02_07 Duct Works_19-10-26", "Ph2_St07_IN_Duct");
            // Two layers of one stage keep their IDs: Clean up never renumbers (unlike Fix phasing names).
            Layer(scene, "_02_Works", "02_08_3_Late_20-10-26", "Ph2_St08_3_IN_Late");
            Layer(scene, "_02_Works", "02_08_1_Early_25-10-26", "Ph2_St08_1_IN_Early");
            var result = Plan(scene).Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("02_05_1_Kerbs", after["02_05-1_Kerbs_17-10-26"]);
            Assert.Equal("02_05_2_Kerbs", after["02_05.2_Kerbs_18-10-26"]);
            Assert.Equal("02_06_Gully", after["2_6_Gully_19-10-26"]);
            Assert.Equal("02_07_Duct_Works", after["02_07 Duct Works_19-10-26"]);
            Assert.Equal("02_08_3_Late", after["02_08_3_Late_20-10-26"]);
            Assert.Equal("02_08_1_Early", after["02_08_1_Early_25-10-26"]);
            Assert.Equal("IN_Kerb", after["Ph2_St05-1_IN_Kerb"]);
            Assert.Equal("IN_Kerb_B", after["Ph2_St05.2_IN_Kerb_B"]);
            Assert.Equal("IN_Late", after["Ph2_St08_3_IN_Late"]);
            Assert.Contains(result.Rows, r => r.CurrentName == "2_6_Gully_19-10-26" && r.Why.Contains("ID written the current way"));
            Assert.Contains(result.Rows, r => r.CurrentName == "02_05-1_Kerbs_17-10-26" && r.Why.Contains(PhasingNameFixer.HyphenReason));
            Assert.Contains(result.Rows, r => r.CurrentName == "02_07 Duct Works_19-10-26" && r.Why.Contains("spaces replaced with underscores"));
        }

        [Fact]
        public void AOneNumberLayerTakesItsPhaseFromItsObjects()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "05_Retainment_17-10-26", "Ph2_St05_IN_Pile_1", "Ph2_St05_IN_Pile_2");
            Layer(scene, "_02_Works", "06_Mixed_18-10-26", "Ph2_St06_IN_A", "Ph3_St06_IN_B");
            var plan = Plan(scene);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("02_05_Retainment", after["05_Retainment_17-10-26"]);
            Assert.Equal("IN_Pile_1", after["Ph2_St05_IN_Pile_1"]);
            Assert.Contains(result.Rows, r => r.CurrentName == "05_Retainment_17-10-26" && r.Why.Contains("phase 2 added in front"));
            // Objects of several phases give it none: left as it is, with its objects.
            Assert.Equal("06_Mixed_18-10-26", after["06_Mixed_18-10-26"]);
            Assert.Equal("Ph2_St06_IN_A", after["Ph2_St06_IN_A"]);
            Assert.Contains(plan.Layers, l => l.CurrentName == "06_Mixed_18-10-26" && l.Left);
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "06_Mixed_18-10-26" && r.Why.Contains("phases 2, 3"));
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "Ph2_St06_IN_A" && r.Why.Contains("left as it is"));
            Assert.Equal(2, result.KeptOldNames);
        }

        [Fact]
        public void ALayerUnderTheRootGoesIntoItsPhaseGroupWithItsMaxLayer()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            var stray = scene.Unit("02_03_Ducts_17-10-26", Root, root);
            scene.Object("Ph2_St03_IN_Duct", stray, "02_03_Ducts_17-10-26");
            var lone = scene.Unit("04_01_Lights_17-10-26", Root, root);
            var result = Plan(scene).Resolve(null);

            var move = Assert.Single(result.NodeMoves);
            Assert.Equal(stray.Key, move.Key);
            Assert.Equal(scene.Find("_02_Works").Key, move.ToParentKey);
            var layerMove = Assert.Single(result.MaxLayerMoves);
            Assert.Equal("02_03_Ducts_17-10-26", layerMove.Name);
            Assert.Equal("_02_Works", layerMove.NewParentName);
            Assert.Contains(result.NodeRenames, r => r.Key == lone.Key && r.NewName == "04_01_Lights");
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "04_01_Lights_17-10-26" && r.Why.Contains("no group _04_<name>"));

            var after = result.ApplyTo(scene.Snapshot);
            var moved = after.Nodes.Single(n => n.Key == stray.Key);
            Assert.Equal("02_03_Ducts", moved.Name);
            Assert.Equal(scene.Find("_02_Works").Key, moved.ParentKey);
            Assert.Equal("_02_Works", after.Layers.Single(l => l.Name == "02_03_Ducts").ParentName);
        }

        [Fact]
        public void AMaxLayerThatCannotTakeItsNewNameLeavesItsLayerAsItIs()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_03_Ducts_17-10-26", "Ph2_St03_IN_Duct");
            scene.Layer("02_03_Ducts", "_02_Works");
            var plan = Plan(scene);
            var result = plan.Resolve(null);
            Assert.Contains(plan.Layers, l => l.CurrentName == "02_03_Ducts_17-10-26" && l.Left);
            Assert.Empty(result.MaxLayerRenames);
            Assert.DoesNotContain(result.NodeRenames, r => r.OldName == "02_03_Ducts_17-10-26");
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.Why.Contains("another Max layer already has that name"));
        }

        // -------------------------------------------------------------------------------------------------------------
        // Objects: the rules of spec 6.1 on the real names of spec 2.2

        private static TestScene SpecScene()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root, "_01_Zone", "_02_Works", "_03_Roads", "_04_Lanes", "_06_Weekend");
            Layer(scene, "_01_Zone", "01_04_1_Retainment_13-10-26_18-10-26", "Ph1_St04_1_IN_Sheet_Pile_4");
            Layer(scene, "_01_Zone", "01_04_2_Retainment_28-10-26_06-11-26", "Ph1_St04_2_IN_Sheet_Pile_4");
            Layer(scene, "_02_Works", "02_01_5_TM_27-09-26_28-09-26", "Ph2_St01_5_RM_RM_Marwood_Barrier3");
            Layer(scene, "_02_Works", "02_01_8_TM_30-09-26_05-02-27", "Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM");
            Layer(scene, "_02_Works", "02_02_1_WaterBarrier_01-10-26_24-01-27", "Ph2_St02_1_IN_RB22_Barrier01_Ph6_St05_2_RM");
            Layer(scene, "_02_Works", "02_02_3_Hoarding_04-10-26_05-02-27", "Ph2_St02_10_IN_RC_Hrd_004_from_PDF_Ph6_St05_2_RM");
            Layer(scene, "_02_Works", "02_02_9_TM_Removal_TBC_17-10-26", "Ph2_St02_9_IN_Cycling_Bollards_Ph4_St02_3_RM", "Ph2_St02_9_IN_Cycling_Bollards_02_Ph4_St02_3_RM");
            Layer(scene, "_02_Works", "02_02_10_Integration_17-10-26");
            Layer(scene, "_02_Works", "02_02_12_Hoarding_17-10-26", "Ph2_St02_12_IN_RC_Hrd2_Ph1_St23_RM");
            Layer(scene, "_02_Works", "02_02_13_Integration_17-10-26", "Ph2_St02_13_IN_Cycling_Bollards");
            Layer(scene, "_02_Works", "02_05_1_SubBase_29-10-26", "Ph2_St05_1_IN_TR_Subbase_280mm_C_");
            Layer(scene, "_03_Roads", "03_04_1_Hoarding_TBC_26-10-26", "Ph3_St04_1_IN_RC_HF_Outer_Full_Closure_Ph3_St04_1_RM");
            Layer(scene, "_03_Roads", "03_04_2_Hoarding_TBC_01-01-27", "Ph3_St04_2_IN_RC_HF_Outer_Full_Closure_Ph3_St09_RM");
            Layer(scene, "_03_Roads", "03_09_Tarmac_20-01-27");
            Layer(scene, "_03_Roads", "03_11_Road_Plates_27-10-26", "Ph3_St11_IN_Road_Plates_Ph3_St09_RM");
            Layer(scene, "_04_Lanes", "04_02_3_Binder_25-01-27");
            Layer(scene, "_06_Weekend", "06_05_2_Gates_05-02-27");
            return scene;
        }

        [Fact]
        public void TheSpecsRealNamesConvertByTheRules()
        {
            var scene = SpecScene();
            var plan = Plan(scene);
            Assert.Equal(PlannerNamingKind.Old, plan.Naming);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("IN_Sheet_Pile_4", after["Ph1_St04_1_IN_Sheet_Pile_4"]);
            Assert.Equal("IN_Sheet_Pile_4_02", after["Ph1_St04_2_IN_Sheet_Pile_4"]);
            Assert.Equal("IN_RC_HF_Outer_Full_Closure_02_RM_03_09", after["Ph3_St04_2_IN_RC_HF_Outer_Full_Closure_Ph3_St09_RM"]);
            Assert.Equal("IN_Cycling_Bollards_03", after["Ph2_St02_13_IN_Cycling_Bollards"]);
            Assert.Equal("IN_Cycling_Bollards_02_RM_04_02_3", after["Ph2_St02_9_IN_Cycling_Bollards_02_Ph4_St02_3_RM"]);
            Assert.Equal("IN_RB22_Barrier01_RM_06_05_2", after["Ph2_St02_1_IN_RB22_Barrier01_Ph6_St05_2_RM"]);
            Assert.Equal("IN_RC_HF_Outer_Full_Closure_RM", after["Ph3_St04_1_IN_RC_HF_Outer_Full_Closure_Ph3_St04_1_RM"]);
            Assert.Equal("IN_Road_Plates_RM_03_09", after["Ph3_St11_IN_Road_Plates_Ph3_St09_RM"]);
            Assert.Equal("IN_TR_Subbase_280mm_C", after["Ph2_St05_1_IN_TR_Subbase_280mm_C_"]);
            // A reference to a stage no layer has is written as the tag writes it, by default, and flagged.
            Assert.Equal("IN_RC_Hrd2_RM_01_23", after["Ph2_St02_12_IN_RC_Hrd2_Ph1_St23_RM"]);
            var flag = plan.Questions.Single(q => q.CurrentName == "Ph2_St02_12_IN_RC_Hrd2_Ph1_St23_RM");
            Assert.Equal(PlannerCleanUpCase.NoLayer, flag.Case);
            Assert.Equal("as-written", flag.DefaultChoice);
            // The three it cannot settle keep their names until answered.
            Assert.Equal("Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM", after["Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM"]);
            Assert.Equal("Ph2_St01_5_RM_RM_Marwood_Barrier3", after["Ph2_St01_5_RM_RM_Marwood_Barrier3"]);
            Assert.Equal("Ph2_St02_10_IN_RC_Hrd_004_from_PDF_Ph6_St05_2_RM", after["Ph2_St02_10_IN_RC_Hrd_004_from_PDF_Ph6_St05_2_RM"]);
            Assert.Equal(3, result.Open);
            Assert.Equal(3, result.Rows.Count(r => r.Status == PlannerCleanUpStatus.Choose));
            Assert.Equal(4, plan.Questions.Count);

            // Numbers: before a trailing tag, past a number in use.
            var hf = result.Numbers.Single(n => n.Name == "IN_RC_HF_Outer_Full_Closure_02_RM_03_09");
            Assert.True(hf.BeforeTrailingTag);
            Assert.Equal("IN_RC_HF_Outer_Full_Closure_RM", hf.First);
            var bollards = result.Numbers.Single(n => n.Name == "IN_Cycling_Bollards_03");
            Assert.Equal(new[] { "Cycling_Bollards_02" }, bollards.Skipped.ToArray());
            Assert.False(bollards.BeforeTrailingTag);
        }

        [Fact]
        public void AClosingTagIsAskedAndOnlyTheAnswerIsApplied()
        {
            var scene = SpecScene();
            var plan = Plan(scene);
            var q = plan.Questions.Single(x => x.CurrentName == "Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM");
            Assert.Equal(PlannerCleanUpCase.ClosingTag, q.Case);
            Assert.Null(q.DefaultChoice);
            Assert.Equal(new[] { "end", "layer", "drop" }, q.Options.Select(o => o.Id).ToArray());
            Assert.Equal("IN_RC_Hrd_from_PDF_01_RM", q.Options[0].Name);
            Assert.True(q.Options[1].NeedsLayer);
            Assert.Null(q.Options[1].Name);
            Assert.Equal("IN_RC_Hrd_from_PDF_01", q.Options[2].Name);
            Assert.Contains("06_05_2", q.LayerIds);

            Func<string, string> answer = choice => After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, choice } }))[q.CurrentName];
            Assert.Equal("IN_RC_Hrd_from_PDF_01_RM", answer("end"));
            Assert.Equal("IN_RC_Hrd_from_PDF_01_RM_06_05_2", answer("layer:06_05_2"));
            Assert.Equal("IN_RC_Hrd_from_PDF_01_RM_06_05_2", answer("layer:6_5_2"));
            // A layer the scene has not got yet (planned in the Planner) is a valid answer: only a reference names it.
            Assert.Equal("IN_RC_Hrd_from_PDF_01_RM_07_01", answer("layer:07_01"));
            Assert.Equal("IN_RC_Hrd_from_PDF_01", answer("drop"));
            Assert.Equal(q.CurrentName, answer(PlannerCleanUpOption.Leave));
            // An answer that is not an ID leaves it as it is, with a note.
            var bad = plan.Resolve(new Dictionary<string, string> { { q.Id, "layer:6" } });
            Assert.Equal(q.CurrentName, After(scene.Snapshot, bad)[q.CurrentName]);
            Assert.Contains(bad.Notes, n => n.Contains("is not a layer ID"));
        }

        [Fact]
        public void AStrayLeadingTagOffersALinkItsOwnLayerOrAReference()
        {
            var scene = SpecScene();
            var plan = Plan(scene);
            var q = plan.Questions.Single(x => x.CurrentName == "Ph2_St02_10_IN_RC_Hrd_004_from_PDF_Ph6_St05_2_RM");
            Assert.Equal(PlannerCleanUpCase.StrayLead, q.Case);
            var target = scene.Find("02_02_10_Integration_17-10-26");
            // It has a trailing tag, so no reference is offered.
            Assert.Equal(new[] { "link:" + target.Key, "own" }, q.Options.Select(o => o.Id).ToArray());
            Assert.All(q.Options, o => Assert.Equal("IN_RC_Hrd_004_from_PDF_RM_06_05_2", o.Name));

            var own = plan.Resolve(new Dictionary<string, string> { { q.Id, "own" } });
            Assert.Equal("IN_RC_Hrd_004_from_PDF_RM_06_05_2", After(scene.Snapshot, own)[q.CurrentName]);
            Assert.Empty(own.Links);

            var linked = plan.Resolve(new Dictionary<string, string> { { q.Id, "link:" + target.Key } });
            var link = Assert.Single(linked.Links);
            Assert.Equal(q.Key, link.Key);
            Assert.Equal(target.Key, link.ToKey);
            Assert.Equal("02_02_10_Integration", link.ToName);
            var layerMove = Assert.Single(linked.LayerMoves);
            Assert.Equal("02_02_3_Hoarding_04-10-26_05-02-27", layerMove.FromLayer);
            Assert.Equal("02_02_10_Integration_17-10-26", layerMove.ToLayer);
            var after = linked.ApplyTo(scene.Snapshot);
            var node = after.Nodes.Single(n => n.Key == q.Key);
            Assert.Equal(target.Key, node.ParentKey);
            Assert.Equal("02_02_10_Integration", node.LayerName);
            Assert.Equal("IN_RC_Hrd_004_from_PDF_RM_06_05_2", node.Name);
            Assert.Contains(linked.Rows, r => r.NodeKey == q.Key && r.LayerItSitsIn == "02_02_10_Integration");
        }

        [Fact]
        public void AStrayLeadingTagWithoutATrailingTagCanBecomeAReference()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_02_3_Hoarding_04-10-26", "Ph2_St02_10_IN_Hoarding_Panel");
            Layer(scene, "_02_Works", "02_02_10_Integration_17-10-26");
            var plan = Plan(scene);
            var q = Assert.Single(plan.Questions);
            Assert.Equal(new[] { "link:" + scene.Find("02_02_10_Integration_17-10-26").Key, "own", "ref" }, q.Options.Select(o => o.Id).ToArray());
            Assert.Equal("Hoarding_Panel_IN_02_02_10", q.Options[2].Name);
            Assert.Equal("Hoarding_Panel_IN_02_02_10", After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "ref" } }))[q.CurrentName]);
            Assert.Equal("IN_Hoarding_Panel", After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "own" } }))[q.CurrentName]);
        }

        [Fact]
        public void ALinkedStrayKeepsItsRemovalInTheLayerItsTagNamed()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_02_3_Hoarding_04-10-26", "Ph2_St02_10_IN_Panel_Ph2_St02_3_RM");
            var target = Layer(scene, "_02_Works", "02_02_10_Integration_17-10-26");
            var plan = Plan(scene);
            var q = Assert.Single(plan.Questions);
            Assert.Equal("IN_Panel_RM", q.OptionFor("own").Name);
            Assert.Equal("IN_Panel_RM_02_02_3", q.OptionFor("link:" + target.Key).Name);
            Assert.Null(q.OptionFor("ref"));
            Assert.Null(q.OptionFor("link:1"));
        }

        [Fact]
        public void ACleanedLayerNameNeverTakesTheNameOfALayerThatKeepsIt()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            // The dated copy comes first in the scene; the layer already named so keeps its name.
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb");
            Layer(scene, "_02_Works", "02_01_Kerbs", "Ph2_St01_IN_Gully");
            var plan = Plan(scene);
            Assert.Contains(plan.Layers, l => l.CurrentName == "02_01_Kerbs_17-10-26" && l.Left);
            Assert.Contains(plan.Layers, l => l.CurrentName == "02_01_Kerbs" && !l.Left);
            var result = plan.Resolve(null);
            Assert.Equal("IN_Gully", After(scene.Snapshot, result)["Ph2_St01_IN_Gully"]);
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "02_01_Kerbs_17-10-26" && r.Why.Contains("already the name of another layer node"));
        }

        [Fact]
        public void ADoubledTypeWordIsProposedWithoutTheSecondWord()
        {
            var scene = SpecScene();
            var plan = Plan(scene);
            var q = plan.Questions.Single(x => x.CurrentName == "Ph2_St01_5_RM_RM_Marwood_Barrier3");
            Assert.Equal(PlannerCleanUpCase.DoubledType, q.Case);
            var option = Assert.Single(q.Options);
            Assert.Equal("undouble", option.Id);
            Assert.Equal("RM_Marwood_Barrier3", option.Name);
            Assert.Equal("RM_Marwood_Barrier3", After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "undouble" } }))[q.CurrentName]);
        }

        [Fact]
        public void AReferenceToALayerNobodyHasCanBeAnsweredOtherwise()
        {
            var scene = SpecScene();
            var plan = Plan(scene);
            var q = plan.Questions.Single(x => x.Case == PlannerCleanUpCase.NoLayer);
            Assert.Equal(new[] { "as-written", "end", "layer", "drop" }, q.Options.Select(o => o.Id).ToArray());
            Assert.Equal("IN_RC_Hrd2_RM", After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "end" } }))[q.CurrentName]);
            Assert.Equal("IN_RC_Hrd2_RM_03_09", After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "layer:03_09" } }))[q.CurrentName]);
            Assert.Equal(q.CurrentName, After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "leave" } }))[q.CurrentName]);
            // "leave" chosen is an answer: the object is left as it is, and told so, not counted as waiting for one.
            var left = plan.Resolve(new Dictionary<string, string> { { q.Id, "leave" } });
            Assert.Equal(plan.Resolve(null).Open, left.Open);
            Assert.Equal(1, left.Left);
            Assert.Contains(left.Rows, r => r.Status == PlannerCleanUpStatus.Choose && r.QuestionId == q.Id && r.Why.EndsWith("; left as it is for now: it keeps its old name", StringComparison.Ordinal));
            Assert.Equal("17 layers and 10 objects to rename, 0 links, 3 to choose, 1 left as it is, 0 to check.", left.Summary());
        }

        [Fact]
        public void ATrailingTagNamingSeveralLayersIsAsked()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Barrier_Ph2_St02_RM");
            Layer(scene, "_02_Works", "02_02_1_A_18-10-26");
            Layer(scene, "_02_Works", "02_02_2_B_19-10-26");
            var plan = Plan(scene);
            var q = Assert.Single(plan.Questions);
            Assert.Equal(PlannerCleanUpCase.SeveralLayers, q.Case);
            Assert.Null(q.DefaultChoice);
            Assert.Equal(new[] { "layer:02_02_1", "layer:02_02_2", "end", "layer", "drop" }, q.Options.Select(o => o.Id).ToArray());
            Assert.Equal("IN_Barrier_RM_02_02_2", q.Options[1].Name);
            Assert.Equal("Ph2_St01_IN_Barrier_Ph2_St02_RM", After(scene.Snapshot, plan.Resolve(null))[q.CurrentName]);
            Assert.Equal("IN_Barrier_RM_02_02_2", After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { q.Id, "layer:02_02_2" } }))[q.CurrentName]);
        }

        [Fact]
        public void TagsNamingASubStageOrItsWholeStageFindTheOneLayer()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_A_Ph2_St03_2_RM", "Ph2_St01_IN_B_Ph2_St04_RM");
            Layer(scene, "_02_Works", "02_03_Roads_18-10-26");
            Layer(scene, "_02_Works", "02_04_1_Lights_19-10-26");
            var result = Plan(scene).Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("IN_A_RM_02_03", after["Ph2_St01_IN_A_Ph2_St03_2_RM"]);
            Assert.Equal("IN_B_RM_02_04_1", after["Ph2_St01_IN_B_Ph2_St04_RM"]);
            Assert.Contains(result.Rows, r => r.CurrentName == "Ph2_St01_IN_A_Ph2_St03_2_RM" && r.Why.Contains("the whole stage's layer covers it"));
        }

        [Fact]
        public void SameTypeAtBothEndsIsListedNotConverted()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb_Ph2_St03_IN");
            Layer(scene, "_02_Works", "02_03_Roads_18-10-26");
            var result = Plan(scene).Resolve(null);
            Assert.Equal("Ph2_St01_IN_Kerb_Ph2_St03_IN", After(scene.Snapshot, result)["Ph2_St01_IN_Kerb_Ph2_St03_IN"]);
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.Why.Contains("the same type at both ends"));
            Assert.Equal(1, result.KeptOldNames);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Numbering (D15)

        [Fact]
        public void TheFirstInLayerOrderKeepsItsNameAndAThirdIsNumbered03()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            // Scene order is not layer order: 02_10 comes after 02_09 as numbers.
            Layer(scene, "_02_Works", "02_10_Late_20-10-26", "Ph2_St10_IN_Kerb");
            Layer(scene, "_02_Works", "02_09_Mid_19-10-26", "Ph2_St09_RM_Kerb");
            Layer(scene, "_02_Works", "02_02_Early_18-10-26", "Ph2_St02_IN_Kerb_Ph2_St09_RM");
            var result = Plan(scene).Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("IN_Kerb_RM_02_09", after["Ph2_St02_IN_Kerb_Ph2_St09_RM"]);
            Assert.Equal("RM_Kerb_02", after["Ph2_St09_RM_Kerb"]);
            Assert.Equal("IN_Kerb_03", after["Ph2_St10_IN_Kerb"]);
            Assert.All(result.Numbers, n => Assert.Empty(n.Skipped));
            Assert.Contains(result.Rows, r => r.CurrentName == "Ph2_St10_IN_Kerb" && r.Why.Contains("numbered _03"));
        }

        [Fact]
        public void AnIllegalNameIsListedNotNumbered()
        {
            // A scene on the new names: IN_X_RM_06 reads with an error (one number is no layer ID), so it is never numbered.
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs", "IN_X_RM_06", "IN_Ok");
            Layer(scene, "_02_Works", "02_02_Roads", "IN_X_RM_06 ", "IN_Ok_RM");
            var plan = Plan(scene);
            Assert.Equal(PlannerNamingKind.New, plan.Naming);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("IN_X_RM_06 ", after["IN_X_RM_06 "]);
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "IN_X_RM_06 " && r.Why.Contains("not numbered"));
            Assert.Equal("IN_Ok_02_RM", after["IN_Ok_RM"]);
        }

        [Fact]
        public void ANumberIsNeverOneAnotherNodeHas()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb");
            Layer(scene, "_02_Works", "02_02_Roads_18-10-26", "Ph2_St02_IN_Kerb");
            scene.Object("IN_Kerb_02", null, "0");
            var result = Plan(scene).Resolve(null);
            Assert.Equal("IN_Kerb_03", After(scene.Snapshot, result)["Ph2_St02_IN_Kerb"]);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Pieces, links, new names, what is listed

        [Fact]
        public void SplitPiecesAreRenamedWithTheirObject()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            var helper = Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26");
            var obj = scene.Object("Ph2_St01_IN_Kerb", helper, "02_01_Kerbs_17-10-26");
            var piece = scene.Object("Ph2_St01_IN_Kerb_1", obj, "02_01_Kerbs_17-10-26");
            scene.Object("Ph2_St01_IN_Kerb_1_2", piece, "02_01_Kerbs_17-10-26");
            Layer(scene, "_02_Works", "02_00_First_16-10-26", "Ph2_St00_IN_Kerb");
            var result = Plan(scene).Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("IN_Kerb_02", after["Ph2_St01_IN_Kerb"]);
            Assert.Equal("IN_Kerb_02_1", after["Ph2_St01_IN_Kerb_1"]);
            Assert.Equal("IN_Kerb_02_1_2", after["Ph2_St01_IN_Kerb_1_2"]);
            Assert.Equal(2, result.ObjectsRenamed);
            Assert.Equal(2, result.Rows.Count(r => r.Item == "Split piece"));
        }

        [Fact]
        public void LinkRunsFirstSoAnObjectIsReadUnderTheHelperOfItsLayer()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            var kerbs = Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26");
            Layer(scene, "_02_Works", "02_02_Roads_18-10-26");
            // At the scene root, on the Kerbs layer: Link puts it under the Kerbs helper, so it is converted for 02_01.
            var loose = scene.Object("Ph2_St01_IN_Kerb", null, "02_01_Kerbs_17-10-26");
            var result = Plan(scene).Resolve(null);
            var link = Assert.Single(result.Links);
            Assert.Equal(loose.Key, link.Key);
            Assert.Equal(kerbs.Key, link.ToKey);
            Assert.Equal("02_01_Kerbs", link.ToName);
            Assert.Equal("IN_Kerb", After(scene.Snapshot, result)["Ph2_St01_IN_Kerb"]);
            Assert.Contains(result.Rows, r => r.CurrentName == "Ph2_St01_IN_Kerb" && r.Why.StartsWith("linked under 02_01_Kerbs", StringComparison.Ordinal));
        }

        [Fact]
        public void NewNamesOnAnOldSceneAreListedAndLeftAsTheyAre()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb", "Ph2_St01_IN_Gully", "Gully_IN", "RM_101");
            var plan = Plan(scene);
            Assert.True(plan.OldNames);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("Gully_IN", after["Gully_IN"]);
            Assert.Equal("RM_101", after["RM_101"]);
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "RM_101" && r.Why.Contains("RM in 02_01"));
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.CurrentName == "Gully_IN" && r.Why.Contains("IN at the end of 02_01"));
            // A listed object is never renamed, but it holds its place in layer order (D15): the converted object of its own
            // name after it ("Gully_IN" before "IN_Gully") is numbered.
            Assert.Equal("IN_Gully_02", after["Ph2_St01_IN_Gully"]);
            Assert.Contains(result.Rows, r => r.CurrentName == "Ph2_St01_IN_Gully" && r.Why.Contains("also used by Gully_IN in 02_01"));
        }

        [Fact]
        public void NewNamesOnANewSceneOnlyGetTheirTextFixed()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs", "IN_Kerb-Edge_RM_02_02", "IN_Gully_", "RM_Old Bench", "IN_Kerb_IN", "Plain-Box");
            Layer(scene, "_02_Works", "02_02_Roads", "IN_Constraction_Joint");
            var plan = Plan(scene);
            Assert.False(plan.OldNames);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("IN_Kerb_Edge_RM_02_02", after["IN_Kerb-Edge_RM_02_02"]);
            Assert.Equal("IN_Gully", after["IN_Gully_"]);
            Assert.Equal("IN_Construction_Joint", after["IN_Constraction_Joint"]);
            // A note after a space, a name the reader flags as illegal and an untagged name are left alone.
            Assert.Equal("RM_Old Bench", after["RM_Old Bench"]);
            Assert.Equal("IN_Kerb_IN", after["IN_Kerb_IN"]);
            Assert.Equal("Plain-Box", after["Plain-Box"]);
            Assert.Equal(3, result.ObjectsRenamed);
            Assert.Equal(0, result.LayersRenamed);
        }

        [Fact]
        public void ObjectsCleanUpCannotPlaceAreListed()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Kerb_Ph2_St05_RM", "Ph2_St01_IN_");
            scene.Object("Ph2_St01_IN_Lost", scene.Find("_02_Works"), "_02_Works");
            var result = Plan(scene).Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("Kerb_Ph2_St05_RM", after["Kerb_Ph2_St05_RM"]);
            Assert.Equal("Ph2_St01_IN_Lost", after["Ph2_St01_IN_Lost"]);
            Assert.Contains(result.Rows, r => r.CurrentName == "Kerb_Ph2_St05_RM" && r.Why.Contains("where the old names never read one"));
            Assert.Contains(result.Rows, r => r.CurrentName == "Ph2_St01_IN_Lost" && r.Why.Contains("under no layer's helper"));
            Assert.Contains(result.Rows, r => r.CurrentName == "Ph2_St01_IN_" && r.Why.Contains("nothing is left between its tags"));
            Assert.Equal(3, result.KeptOldNames);
        }

        // -------------------------------------------------------------------------------------------------------------
        // The whole run

        [Fact]
        public void ACleanedSceneNeedsNothingMore()
        {
            var scene = SpecScene();
            var plan = Plan(scene);
            var choices = new Dictionary<string, string>();
            foreach (var q in plan.Questions)
            {
                choices[q.Id] = q.Case == PlannerCleanUpCase.ClosingTag ? "layer:06_05_2" : q.Case == PlannerCleanUpCase.DoubledType ? "undouble" : q.Case == PlannerCleanUpCase.StrayLead ? "own" : q.DefaultChoice;
            }
            var result = plan.Resolve(choices);
            Assert.Equal(0, result.Open);
            Assert.True(result.HasChanges);
            var after = result.ApplyTo(scene.Snapshot);

            var again = PlannerCleanUp.Plan(after, PhasingNameFixer.DefaultWordFixes());
            Assert.Equal(PlannerNamingKind.New, again.Naming);
            Assert.Empty(again.Questions);
            var second = again.Resolve(null);
            Assert.False(second.HasChanges);
            Assert.Equal(string.Empty, second.Fingerprint());
            Assert.DoesNotContain(second.Rows, r => r.Status != PlannerCleanUpStatus.Check);

            // The cleaned scene reads on the new names with every tagged object scheduled and no error.
            var names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(after), null);
            Assert.Empty(names.OtherNaming);
            Assert.Equal(14, names.Matched.Count);
            Assert.DoesNotContain(names.NamingIssues, i => PlannerNaming.NamingIssueV2[i.Code].Severity == NamingSeverity.Error);
        }

        [Fact]
        public void TheReviewListsEveryChangeAndWritesCsv()
        {
            var scene = SpecScene();
            var result = Plan(scene).Resolve(null);
            Assert.Equal(result.LayersRenamed + result.ObjectsRenamed, result.Rows.Count(r => r.Status == PlannerCleanUpStatus.Change && r.Item != "Split piece"));
            var csv = result.ToCsv();
            Assert.StartsWith("\uFEFF" + PlannerCleanUpRow.CsvHeader + "\r\n", csv);
            Assert.Contains("Choose,Object,02_01_8_TM,Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM,Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM,", csv);
            Assert.Equal("17 layers and 11 objects to rename, 0 links, 3 to choose, 0 to check.", result.Summary());
            Assert.Equal("3 objects keep old Ph/St names; the Planner will not schedule them.", result.KeptOldNamesText());
        }

        [Fact]
        public void NothingToCleanUpWithoutAPhasingRoot()
        {
            var scene = new TestScene();
            scene.Object("Box001", null, "0");
            var plan = PlannerCleanUp.Plan(scene.Snapshot, null);
            Assert.Equal(PlannerCleanUp.NothingToDo, plan.Message);
            var result = plan.Resolve(null);
            Assert.False(result.HasChanges);
            Assert.Equal(PlannerCleanUp.NothingToDo, Assert.Single(result.Notes));
        }

        [Fact]
        public void AnOldNameSceneIsSavedAsACopyFirst()
        {
            Assert.Equal(@"D:\Jobs\HRB_v30_before_v2.max", PlannerCleanUp.BeforeV2FileName(@"D:\Jobs\HRB_v30.max"));
            Assert.Equal("Untitled_before_v2.max", PlannerCleanUp.BeforeV2FileName(null));
            Assert.Equal(@"D:\v1.2\Scene_before_v2.max", PlannerCleanUp.BeforeV2FileName(@"D:\v1.2\Scene"));
        }

        [Fact]
        public void TheTabSaysWhereThePreviewDatesComeFromOnACleanedScene()
        {
            // A scene cleaned with no layer list imported yet: no list's dates, every layer parked (no date comes from 3ds Max, D20).
            Assert.Equal("The scene uses the new names (layer IDs, dates held in the Planner), and no layer list is imported: Fill IN / RM previews the Planner's "
                + "first Generate with every layer parked one day each, TBC, in layer order from the project start. Import the Planner's layer list to preview with its dates.",
                PlannerScheduleRows.NewNamesFillNote(null));
            var list = TestCsv.List(TestCsv.Row("g2", null, "_02_Works"), TestCsv.Row("l1", "g2", "02_01_Kerbs"));
            Assert.True(list.Usable);
            Assert.Equal("The scene uses the new names (layer IDs, dates held in the Planner): Fill IN / RM previews the Planner's first Generate over the list's dates.",
                PlannerScheduleRows.NewNamesFillNote(list));
        }

        [Fact]
        public void AddedLayerRecordsAreRenamedWithTheirLayers()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb");
            var result = Plan(scene).Resolve(null);
            var records = result.RenameAddedLayers(new[]
            {
                new PlannerAddedLayer { Layer = "02_01_Kerbs_17-10-26", ParentLayer = "_02_Works", Kind = "Layer", Id = "02_01", Name = "Kerbs" },
                new PlannerAddedLayer { Layer = "Elsewhere", Kind = "Layer" }
            });
            Assert.Equal("02_01_Kerbs", records[0].Layer);
            Assert.Equal("02_01", records[0].Id);
            Assert.Equal("Kerbs", records[0].Name);
            Assert.Equal("02_01_Kerbs_17-10-26", records[0].WasLayer);
            Assert.Equal("Elsewhere", records[1].Layer);
            Assert.Equal(string.Empty, records[1].WasLayer);
        }

        [Fact]
        public void AnAddedLayerRecordIsRenamedOnlyWhenTheSceneShowsItsLayerRenamed()
        {
            PlannerSceneNode root;
            var scene = Phasing(out root);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb");
            Layer(scene, "_02_Works", "02_02_Roads_18-10-26", "Ph2_St02_IN_Road");
            var result = Plan(scene).Resolve(null);
            var after = result.ApplyTo(scene.Snapshot);
            // The batch could not rename 02_02's Max layer ("taken"): it kept its old name.
            after.Layers.Single(l => l.Name == "02_02_Roads").Name = "02_02_Roads_18-10-26";
            var records = result.RenameAddedLayers(new[]
            {
                new PlannerAddedLayer { Layer = "02_01_Kerbs_17-10-26", Kind = "Layer", Id = "02_01", Name = "Kerbs" },
                new PlannerAddedLayer { Layer = "02_02_Roads_18-10-26", Kind = "Layer", Id = "02_02", Name = "Roads" }
            }, new PlannerSceneModel(after));
            Assert.Equal("02_01_Kerbs", records[0].Layer);
            Assert.Equal("02_02_Roads_18-10-26", records[1].Layer);
            Assert.Equal(string.Empty, records[1].WasLayer);
            // Both are still added layers of the scene.
            Assert.Equal(2, PlannerAddedLayerStore.Present(records, new PlannerSceneModel(after)).Count);
        }

        [Fact]
        public void DatesAndTbcAreTakenOutWithTheirSeparator()
        {
            int dates;
            bool tbc;
            Assert.Equal("Drainage", PlannerCleanUp.StripDatesAndTbc("Drainage_TBC_08-06-26_03-07-26", out dates, out tbc));
            Assert.Equal(2, dates);
            Assert.True(tbc);
            Assert.Equal("MainRd_inst_!!!FULL!!!", PlannerCleanUp.StripDatesAndTbc("MainRd_inst_TBC_!!!FULL!!!_17-10-26_18-10-26", out dates, out tbc));
            Assert.Equal("_Kerbs", PlannerCleanUp.StripDatesAndTbc("17-08-26_Kerbs", out dates, out tbc));
            Assert.Equal("Road-Marking", PlannerCleanUp.StripDatesAndTbc("Road-Marking_06-02-27", out dates, out tbc));
            Assert.Equal("100mm_Course", PlannerCleanUp.StripDatesAndTbc("100mm_Course", out dates, out tbc));
            Assert.Equal(0, dates);
            Assert.False(tbc);
            Assert.Equal("Works", PlannerCleanUp.StripDatesAndTbc("Works tbc", out dates, out tbc));
            Assert.True(tbc);
        }
    }
}
