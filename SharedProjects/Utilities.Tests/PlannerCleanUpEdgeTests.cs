using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// Clean up scene graph on the scenes review found it went wrong on (naming v2, spec 5.1 and 6.1): number-led 3ds Max
    /// groups inside a layer, two answers on one object, the numbering (D15) beside listed and waiting objects, numbers that
    /// read as a layer ID or a split piece, a second type word that is not the same word, and links out of a group.
    /// </summary>
    public class PlannerCleanUpEdgeTests
    {
        private const string Root = "Work_Phasing";

        private static TestScene Phasing(params string[] groups)
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            foreach (var group in groups.Length > 0 ? groups : new[] { "_01_Zone", "_02_Works", "_04_Lanes" })
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

        private static PlannerCleanUpPlan Plan(PlannerSceneSnapshot scene)
        {
            return PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes());
        }

        // -------------------------------------------------------------------------------------------------------------
        // Layers: only where the convention puts them, or as the Planner reads one

        [Fact]
        public void GroupsLedByANumberInsideALayerAreNoLayers()
        {
            const string kerbs = "02_03_Kerbs_01-02-26";
            var scene = Phasing();
            var layer = Layer(scene, "_02_Works", kerbs, "Ph2_St03_IN_Kerb");
            var members = new List<PlannerSceneNode>();
            var groups = new List<PlannerSceneNode>();
            foreach (var pair in new[]
            {
                new[] { "3 Bay Gate", "Ph2_St03_IN_Gate_Leaf_1" },
                new[] { "2.4_High_Hoarding", "Ph2_St03_IN_Hoarding_Panel_1" },
                new[] { "1.8_x_2.4_Heras_Panel", "Ph2_St03_IN_Heras_1" },
                new[] { "06 Lane Barrier", "Ph2_St03_IN_Lane_Barrier" }
            })
            {
                var group = scene.Helper(pair[0], layer, kerbs);
                group.IsGroupHead = true;
                var member = scene.Object(pair[1], group, kerbs);
                member.IsGroupMember = true;
                groups.Add(group);
                members.Add(member);
            }
            var plan = Plan(scene.Snapshot);
            Assert.Equal(new[] { kerbs }, plan.Layers.Select(l => l.CurrentName).ToArray());
            Assert.Empty(plan.Questions);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.All(groups, g => Assert.Equal(g.Name, after[g.Key]));
            Assert.Equal(new[] { "IN_Gate_Leaf_1", "IN_Hoarding_Panel_1", "IN_Heras_1", "IN_Lane_Barrier" }, members.Select(m => after[m.Key]).ToArray());

            // Each group is listed: the Planner leaves a tagged object under a number-led node the old names read as a layer
            // in no layer, so its objects are scheduled once it is renamed.
            var listed = result.Rows.Where(r => r.Status == PlannerCleanUpStatus.Check).ToList();
            Assert.Equal(groups.Select(g => g.Key).OrderBy(k => k).ToArray(), listed.Select(r => r.NodeKey.Value).OrderBy(k => k).ToArray());
            Assert.All(listed, r => Assert.Equal("02_03_Kerbs", r.LayerItSitsIn));
            Assert.All(listed, r => Assert.EndsWith("Planner does not schedule the 1 tagged object under it: rename it so its name does not start with a number, and it is scheduled in 02_03", r.Why));
            var cleaned = result.ApplyTo(scene.Snapshot);
            var names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(cleaned), null);
            Assert.Equal(new[] { "IN_Kerb" }, names.Matched.ToArray());
            Assert.Equal(4, names.Unfiled.Count);
            Assert.Empty(names.SharedIds);
            // Renamed as the rows say, every object is scheduled in 02_03.
            foreach (var node in cleaned.Nodes.Where(n => groups.Any(g => g.Key == n.Key)))
            {
                node.Name = "Group_" + node.Key;
            }
            names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(cleaned), null);
            Assert.Equal(5, names.Matched.Count);
            Assert.All(names.Objects.Where(o => names.Matched.Contains(o.Name)), o => Assert.Equal("02_03", PlannerNaming.LayerIdText(o.Layer)));

            // Run again on the cleaned scene: a group led by a number is still no layer, and nothing changes.
            var again = Plan(result.ApplyTo(scene.Snapshot)).Resolve(null);
            Assert.False(again.HasChanges, string.Join("\n", again.Rows.Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
            Assert.Equal(4, again.Rows.Count(r => r.Status == PlannerCleanUpStatus.Check));
        }

        [Fact]
        public void ALayerNodeInsideALayerIsOneWhenTheNewNamesReadIt()
        {
            const string kerbs = "02_03_Kerbs_01-02-26";
            var scene = Phasing();
            var layer = Layer(scene, "_02_Works", kerbs);
            var lights = scene.Helper("02_04_Lights_05-02-26", layer, kerbs);
            scene.Object("Ph2_St04_IN_Lamp", lights, kerbs);
            var plan = Plan(scene.Snapshot);
            var result = plan.Resolve(null);
            Assert.Equal("02_04_Lights", After(scene.Snapshot, result)[lights.Key]);
            // The review says where it sits: in the layer above, not at the phasing root.
            Assert.Contains(result.Rows, r => r.NodeKey == lights.Key && r.Status == PlannerCleanUpStatus.Change && r.LayerItSitsIn == "02_03_Kerbs");
        }

        [Fact]
        public void ASubStageWrittenTheOldWayInsideItsStageLayerIsStillALayer()
        {
            // 02_04-2 and 02_04.2 helpers inside the stage layer 02_04, on its Max layer (no Max layer of their own), as the old
            // names read them: each is its own layer, written the current way, and its objects are read against it.
            const string roads = "02_04_Roads_01-02-26";
            var scene = Phasing();
            var stage = Layer(scene, "_02_Works", roads, "Ph2_St04_IN_Road");
            var hyphen = scene.Helper("02_04-2_Roads_02-02-26", stage, roads);
            var kerb = scene.Object("Ph2_St04-2_IN_Kerb", hyphen, roads);
            var stop = scene.Helper("02_04.3_Gullies_03-02-26", stage, roads);
            var gully = scene.Object("Ph2_St04.3_IN_Gully", stop, roads);
            var spaced = scene.Helper("02_05 Lights_04-02-26", stage, roads);
            var lamp = scene.Object("Ph2_St05_IN_Lamp", spaced, roads);
            // Not layers: a dimension in the full stop's place, and a 3ds Max group of objects named like a sub-stage.
            var panel = scene.Helper("02_10.8_x_2.4_Panel", stage, roads);
            var heras = scene.Object("Ph2_St04_IN_Heras", panel, roads);
            var group = scene.Helper("02_04-4_Barrier_Group", stage, roads);
            group.IsGroupHead = true;
            var barrier = scene.Object("Ph2_St04_IN_Barrier", group, roads);
            barrier.IsGroupMember = true;

            var plan = Plan(scene.Snapshot);
            Assert.Equal(new[] { "02_04_Roads", "02_04_2_Roads", "02_04_3_Gullies", "02_05_Lights" }, plan.Layers.Select(l => l.NewName).ToArray());
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);
            Assert.Equal("02_04_2_Roads", after[hyphen.Key]);
            Assert.Equal("02_04_3_Gullies", after[stop.Key]);
            Assert.Equal("02_05_Lights", after[spaced.Key]);
            Assert.Equal("IN_Kerb", after[kerb.Key]);
            Assert.Equal("IN_Gully", after[gully.Key]);
            Assert.Equal("IN_Lamp", after[lamp.Key]);
            Assert.Equal("IN_Heras", after[heras.Key]);
            Assert.Equal("IN_Barrier", after[barrier.Key]);
            Assert.Equal(panel.Name, after[panel.Key]);
            Assert.Equal(group.Name, after[group.Key]);
            Assert.Contains(result.Rows, r => r.NodeKey == hyphen.Key && r.Status == PlannerCleanUpStatus.Change && r.LayerItSitsIn == "02_04_Roads");
            Assert.DoesNotContain(result.Rows, r => r.NodeKey == hyphen.Key && r.Status == PlannerCleanUpStatus.Check);

            // The Planner reads each object in its own layer.
            var names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(result.ApplyTo(scene.Snapshot)), null);
            var layerOf = names.Objects.Where(o => o.Layer != null).ToDictionary(o => o.Name, o => PlannerNaming.LayerIdText(o.Layer));
            // The objects under the dimension and the group of objects are listed, as before (the Planner files them in no layer).
            Assert.Equal(new[] { "IN_Barrier", "IN_Heras" }, names.Unfiled.OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal("02_04_2", layerOf["IN_Kerb"]);
            Assert.Equal("02_04_3", layerOf["IN_Gully"]);
            Assert.Equal("02_05", layerOf["IN_Lamp"]);
            Assert.False(Plan(result.ApplyTo(scene.Snapshot)).Resolve(null).HasChanges);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Two answers on one object

        [Fact]
        public void ADoubledTypeWordKeepsTheOtherQuestionsAnswers()
        {
            // A doubled RM_ beside a closing _IN, a trailing tag naming a stage no layer has, and a stray leading tag: the other
            // question still offers every answer (and its default), each with the name it gives once the word is undoubled.
            var scene = Phasing();
            var kerbs = Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26");
            var closing = scene.Object("Ph2_St01_RM_RM_Barrier_IN", kerbs, "02_01_Kerbs_01-02-26");
            var noLayer = scene.Object("Ph2_St01_RM_RM_Fence_Ph1_St23_IN", kerbs, "02_01_Kerbs_01-02-26");
            var stray = scene.Object("Ph2_St02_RM_RM_Post", kerbs, "02_01_Kerbs_01-02-26");
            Layer(scene, "_02_Works", "02_02_Gullies_02-02-26");
            var plan = Plan(scene.Snapshot);
            Func<PlannerSceneNode, PlannerCleanUpCase, PlannerCleanUpQuestion> question = (node, kind) => plan.Questions.Single(q => q.Key == node.Key && q.Case == kind);

            var close = question(closing, PlannerCleanUpCase.ClosingTag);
            Assert.Equal(new[] { "end", "layer", "drop" }, close.Options.Select(o => o.Id).ToArray());
            Assert.Equal("RM_Barrier_IN", close.OptionFor("end").Name);
            Assert.Equal("RM_Barrier", close.OptionFor("drop").Name);
            var missing = question(noLayer, PlannerCleanUpCase.NoLayer);
            Assert.Equal("as-written", missing.DefaultChoice);
            Assert.Equal(new[] { "as-written", "end", "layer", "drop" }, missing.Options.Select(o => o.Id).ToArray());
            Assert.Equal("RM_Fence_IN_01_23", missing.OptionFor("as-written").Name);
            var lead = question(stray, PlannerCleanUpCase.StrayLead);
            Assert.Contains(lead.Options, o => o.Id == "own");
            Assert.Equal("RM_Post", lead.OptionFor("own").Name);
            Assert.Equal("Post_RM_02_02", lead.OptionFor("ref").Name);
            Assert.DoesNotContain(plan.Resolve(null).Rows, r => r.Status == PlannerCleanUpStatus.Check && r.NodeKey == stray.Key);

            // Answered together, each object gets the name the answers give; with the word left doubled, it keeps its name.
            var choices = new Dictionary<string, string>
            {
                { close.Id, "end" },
                { question(closing, PlannerCleanUpCase.DoubledType).Id, "undouble" },
                { question(noLayer, PlannerCleanUpCase.DoubledType).Id, "undouble" },
                { lead.Id, "own" },
                { question(stray, PlannerCleanUpCase.DoubledType).Id, "undouble" }
            };
            var after = After(scene.Snapshot, plan.Resolve(choices));
            Assert.Equal("RM_Barrier_IN", after[closing.Key]);
            Assert.Equal("RM_Fence_IN_01_23", after[noLayer.Key]);
            Assert.Equal("RM_Post", after[stray.Key]);
            choices.Remove(question(stray, PlannerCleanUpCase.DoubledType).Id);
            var waiting = plan.Resolve(choices);
            Assert.Equal(stray.Name, After(scene.Snapshot, waiting)[stray.Key]);
            Assert.Contains(waiting.Rows, r => r.Status == PlannerCleanUpStatus.Choose && r.NodeKey == stray.Key);
        }

        [Fact]
        public void ALinkAndTheEndOfItsLayerKeepTheEventInTheLayerItLeaves()
        {
            var scene = Phasing();
            var kerbs = Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26");
            var gate = scene.Object("Ph2_St02_IN_Gate_RM", kerbs, "02_01_Kerbs_01-02-26");
            var post = scene.Object("Ph2_St02_IN_Post_Ph2_St09_RM", kerbs, "02_01_Kerbs_01-02-26");
            var rail = scene.Object("Ph2_St02_IN_Rail_Ph2_St03_RM", kerbs, "02_01_Kerbs_01-02-26");
            var roads = Layer(scene, "_02_Works", "02_02_Roads_02-02-26");
            Layer(scene, "_02_Works", "02_03_1_A_03-02-26");
            Layer(scene, "_02_Works", "02_03_2_B_04-02-26");
            var plan = Plan(scene.Snapshot);
            // A stray leading tag, and a closing _RM, a tag naming a stage no layer has, a tag naming several layers.
            Assert.Equal(6, plan.Questions.Count);

            Func<bool, PlannerCleanUpResult> answer = link => plan.Resolve(plan.Questions.ToDictionary(q => q.Id,
                q => q.Case == PlannerCleanUpCase.StrayLead ? (link ? "link:" + roads.Key : "own") : "end"));
            var linked = After(scene.Snapshot, answer(true));
            Assert.Equal("IN_Gate_RM_02_01", linked[gate.Key]);
            Assert.Equal("IN_Post_RM_02_01", linked[post.Key]);
            Assert.Equal("IN_Rail_RM_02_01", linked[rail.Key]);
            // Kept in its own layer, the end of the layer is written plain.
            var own = After(scene.Snapshot, answer(false));
            Assert.Equal("IN_Gate_RM", own[gate.Key]);
            Assert.Equal("IN_Post_RM", own[post.Key]);
            Assert.Equal("IN_Rail_RM", own[rail.Key]);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Numbering (D15)

        [Fact]
        public void AnObjectWaitingForAnAnswerHoldsItsOwnNameSoTheCleanNameIsNotRenamedLater()
        {
            var scene = Phasing();
            var first = Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26");
            var waiting = scene.Object("Ph2_St01_IN_Barrier_RM", first, "02_01_Kerbs_01-02-26");
            var second = Layer(scene, "_02_Works", "02_02_Roads_02-02-26");
            var later = scene.Object("Ph2_St02_IN_Barrier", second, "02_02_Roads_02-02-26");
            var plan = Plan(scene.Snapshot);
            var run1 = plan.Resolve(null);
            Assert.Equal("IN_Barrier_02", After(scene.Snapshot, run1)[later.Key]);
            Assert.Equal(waiting.Name, After(scene.Snapshot, run1)[waiting.Key]);

            // Answered on a second run, only the object that waited is renamed.
            var cleaned = run1.ApplyTo(scene.Snapshot);
            var again = Plan(cleaned);
            var q = Assert.Single(again.Questions);
            var run2 = again.Resolve(new Dictionary<string, string> { { q.Id, "end" } });
            var rename = Assert.Single(run2.NodeRenames);
            Assert.Equal(waiting.Key, rename.Key);
            Assert.Equal("IN_Barrier_RM", rename.NewName);

            // The names one run with the answer gives.
            var once = After(scene.Snapshot, plan.Resolve(new Dictionary<string, string> { { plan.Questions.Single().Id, "end" } }));
            Assert.Equal("IN_Barrier_RM", once[waiting.Key]);
            Assert.Equal("IN_Barrier_02", once[later.Key]);
        }

        [Fact]
        public void ANewNameListedOnAnOldSceneTakesItsPlaceInTheNumbering()
        {
            var scene = Phasing();
            var a = Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26");
            var listedKerb = scene.Object("IN_Kerb", a, "02_01_Kerbs_01-02-26");
            scene.Object("Ph2_St01_IN_A", a, "02_01_Kerbs_01-02-26");
            var b = Layer(scene, "_02_Works", "02_02_Roads_02-02-26");
            var kerb = scene.Object("Ph2_St02_IN_Kerb", b, "02_02_Roads_02-02-26");
            var pipe = scene.Object("Ph2_St02_IN_Pipe", b, "02_02_Roads_02-02-26");
            var c = Layer(scene, "_02_Works", "02_03_Drains_03-02-26");
            scene.Object("Kerbstone_IN", c, "02_03_Drains_03-02-26");
            var listedPipe = scene.Object("Pipe_RM", c, "02_03_Drains_03-02-26");
            var d = Layer(scene, "_02_Works", "02_04_Lights_04-02-26");
            var kerbstone = scene.Object("Ph2_St04_IN_Kerbstone", d, "02_04_Lights_04-02-26");
            scene.Object("Ph2_St04_IN_B", d, "02_04_Lights_04-02-26");
            var plan = Plan(scene.Snapshot);
            Assert.True(plan.OldNames);
            var result = plan.Resolve(null);
            var after = After(scene.Snapshot, result);

            // The listed name comes first in layer order: the converted one is numbered, not refused or left sharing it.
            Assert.Equal("IN_Kerb", after[listedKerb.Key]);
            Assert.Equal("IN_Kerb_02", after[kerb.Key]);
            Assert.Equal("IN_Kerbstone_02", after[kerbstone.Key]);
            // A listed name later in layer order is not renamed: its row says why it shares its own name.
            Assert.Equal("IN_Pipe", after[pipe.Key]);
            Assert.Equal("Pipe_RM", after[listedPipe.Key]);
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.NodeKey == listedPipe.Key && r.Why.Contains("is not numbered"));
            Assert.DoesNotContain(result.Rows, r => r.Why.Contains("would also be the name of another node"));
        }

        [Fact]
        public void ANumberIsNeverOneThatEndsTheOwnNameInALayerIdBeforeAPlainTag()
        {
            var scene = Phasing();
            Layer(scene, "_01_Zone", "01_02_Fence_01-02-26");
            Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26", "Ph2_St01_IN_Barrier_01_Ph2_St01_RM");
            var roads = Layer(scene, "_02_Works", "02_02_Roads_02-02-26");
            var barrier = scene.Object("Ph2_St02_IN_Barrier_01_Ph2_St02_RM", roads, "02_02_Roads_02-02-26");
            Layer(scene, "_04_Lanes", "04_01_Piles_01-03-26", "Ph4_St01_IN_Sheet_Pile_4_Ph4_St01_RM");
            Layer(scene, "_04_Lanes", "04_02_Piles_02-03-26");
            var late = Layer(scene, "_04_Lanes", "04_05_Piles_05-03-26");
            var pile = scene.Object("Ph4_St05_IN_Sheet_Pile_4_Ph4_St05_RM", late, "04_05_Piles_05-03-26");
            var result = Plan(scene.Snapshot).Resolve(null);
            var after = After(scene.Snapshot, result);

            // _02 would read as layer 01_02 (and 04_02) before the tag: the next number is taken.
            Assert.Equal("IN_Barrier_01_03_RM", after[barrier.Key]);
            Assert.Equal("IN_Sheet_Pile_4_03_RM", after[pile.Key]);
            Assert.Contains(result.Rows, r => r.NodeKey == barrier.Key && r.Why.Contains("_02 would make its own name end in a layer ID before the tag"));
            var names = PlannerSchedule.ReadNamesV2(new PlannerScheduleModel(result.ApplyTo(scene.Snapshot)), null);
            Assert.DoesNotContain(names.NamingIssues, i => i.Code == PlannerNamingCode.OldOrder);
        }

        [Fact]
        public void ANumberThatWouldMakeAnObjectASplitPieceOfTheNodeAboveIsListed()
        {
            var scene = Phasing();
            var layer = Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26");
            var head = scene.Helper("Ph2_St01_IN_Hoarding", layer, "02_01_Kerbs_01-02-26");
            head.IsGroupHead = true;
            var member = scene.Object("Ph2_St01_IN_Hoarding", head, "02_01_Kerbs_01-02-26");
            member.IsGroupMember = true;
            var result = Plan(scene.Snapshot).Resolve(null);
            var after = After(scene.Snapshot, result);

            Assert.Equal("IN_Hoarding", after[head.Key]);
            Assert.Equal("Ph2_St01_IN_Hoarding", after[member.Key]);
            Assert.DoesNotContain(result.NodeRenames, r => r.NewName == "IN_Hoarding_02");
            Assert.Contains(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.NodeKey == member.Key && r.Why.Contains("split piece of IN_Hoarding"));
        }

        // -------------------------------------------------------------------------------------------------------------
        // Add layer records

        [Fact]
        public void AnAddedLayerRecordFollowsItsLayerBackWhenTheCleanUpIsUndone()
        {
            const string dated = "02_05_New_Layer_01-10-26_05-10-26";
            var scene = Phasing();
            Layer(scene, "_02_Works", dated, "Ph2_St05_IN_Kerb");
            var result = Plan(scene.Snapshot).Resolve(null);
            // The record as an earlier Add layer kept it (the first version, with its dates, which are passed over: D20).
            var added = PlannerAddedLayerStore.Read("PlannerAddedLayers 1\nLayer,Parent layer,Kind,ID,Name,Start,Finish,TBC\r\n"
                + dated + ",_02_Works,Layer,02_05,New_Layer,2026-10-01,2026-10-05,\r\n");
            var renamed = result.RenameAddedLayers(added);
            var cleaned = new PlannerSceneModel(result.ApplyTo(scene.Snapshot));
            Assert.Equal("02_05_New_Layer", Assert.Single(PlannerAddedLayerStore.Present(renamed, cleaned)).Layer);

            // Edit > Undo puts the Max layer back under its old name: the record follows it, still added.
            var undone = PlannerAddedLayerStore.Present(renamed, new PlannerSceneModel(scene.Snapshot));
            var back = Assert.Single(undone);
            Assert.Equal(dated, back.Layer);
            Assert.Equal("New_Layer", back.Name);
            Assert.Equal("02_05", back.Id);
            // Kept with the scene and read again, then Redo: it follows its layer forward again.
            var stored = PlannerAddedLayerStore.Read(PlannerAddedLayerStore.Write(undone));
            Assert.Equal("02_05_New_Layer", Assert.Single(PlannerAddedLayerStore.Present(stored, cleaned)).Layer);

            // A Max layer the batch could not rename keeps its record as well.
            var stuck = result.ApplyTo(scene.Snapshot);
            stuck.Layers.Single(l => l.Name == "02_05_New_Layer").Name = dated;
            Assert.Equal(dated, Assert.Single(PlannerAddedLayerStore.Present(renamed, new PlannerSceneModel(stuck))).Layer);
        }

        [Fact]
        public void AnAddedLayerRecordFollowsItsParentsNameBackWhenTheCleanUpIsUndone()
        {
            // The Planner renamed the group _02_Works (found by its phase number); a layer added in Max under it keeps its own
            // name. Its record takes the group's new name, and its names from before, so Edit > Undo puts it back under the group.
            var scene = Phasing();
            Layer(scene, "_02_Works", "02_30_Temporary_Works", "IN_Fence");
            var list = TestCsv.List(TestCsv.Row("g2", null, "_02_Main_Works", "Group"));
            var result = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes(), list).Resolve(null);
            Assert.Contains(result.MaxLayerRenames, r => r.OldName == "_02_Works" && r.NewName == "_02_Main_Works");
            var added = new[] { new PlannerAddedLayer { Layer = "02_30_Temporary_Works", ParentLayer = "_02_Works", Kind = "Layer", Id = "02_30", Name = "Temporary_Works" } };
            var cleaned = result.ApplyTo(scene.Snapshot);
            var renamed = result.RenameAddedLayers(added, new PlannerSceneModel(cleaned));
            var record = Assert.Single(renamed);
            Assert.Equal("02_30_Temporary_Works", record.Layer);
            Assert.Equal("_02_Main_Works", record.ParentLayer);
            Assert.Equal("02_30_Temporary_Works", record.WasLayer);
            Assert.Equal("_02_Works", record.WasParentLayer);
            Assert.Equal("_02_Main_Works", Assert.Single(PlannerAddedLayerStore.Present(renamed, new PlannerSceneModel(cleaned))).ParentLayer);

            // Edit > Undo: the group has its old name again, and the record its old parent, so the tree shows it under it.
            var undone = new PlannerSceneModel(scene.Snapshot);
            var back = Assert.Single(PlannerAddedLayerStore.Present(renamed, undone));
            Assert.Equal("_02_Works", back.ParentLayer);
            var tree = PlannerScheduleRows.Tree(null, null, undone, PlannerAddedLayerStore.Present(renamed, undone), null, PlannerNamingKind.New);
            var row = tree.Single(r => r.Source == PlannerScheduleRowSource.Added);
            var group = tree.Single(r => r.Name == "_02_Works");
            Assert.Equal(group.Depth + 1, row.Depth);
            Assert.True(tree.IndexOf(row) > tree.IndexOf(group));
            // Redo: forward again.
            Assert.Equal("_02_Main_Works", Assert.Single(PlannerAddedLayerStore.Present(renamed, new PlannerSceneModel(cleaned))).ParentLayer);
        }

        [Fact]
        public void TheRecordsAnEarlierCleanUpRenamedAreRenamedFromTheScenesOwnCopy()
        {
            // Clean up from MAXScript renamed a layer added by the released tool, and its record with it; the Schedule tab's
            // Clean up then answers what MAXScript left open. Starting from the records the scene keeps (as the scene names the
            // layers now), the record keeps following its layer; the copy read before MAXScript ran would lose it.
            const string dated = "02_31_Old_Added_05-10-26";
            var scene = Phasing();
            Layer(scene, "_02_Works", dated);
            Layer(scene, "_02_Works", "02_01_Kerbs_17-10-26", "Ph2_St01_IN_Kerb", "Ph2_St01_IN_Gully_RM");
            var stale = new List<PlannerAddedLayer> { new PlannerAddedLayer { Layer = dated, ParentLayer = "_02_Works", Kind = "Layer", Id = "02_31", Name = "Old_Added" } };
            var first = Plan(scene.Snapshot).Resolve(null);
            var afterFirst = first.ApplyTo(scene.Snapshot);
            var kept = first.RenameAddedLayers(PlannerAddedLayerStore.ForScene(stale, new PlannerSceneModel(scene.Snapshot)), new PlannerSceneModel(afterFirst));
            Assert.Equal("02_31_Old_Added", Assert.Single(kept).Layer);

            var second = Plan(afterFirst);
            var question = Assert.Single(second.Questions);
            var answered = second.Resolve(new Dictionary<string, string> { { question.Id, "end" } });
            Assert.True(answered.HasChanges);
            var afterSecond = new PlannerSceneModel(answered.ApplyTo(afterFirst));
            var fromScene = answered.RenameAddedLayers(PlannerAddedLayerStore.ForScene(kept, new PlannerSceneModel(afterFirst)), afterSecond);
            var record = Assert.Single(PlannerAddedLayerStore.Present(fromScene, afterSecond));
            Assert.Equal("02_31_Old_Added", record.Layer);
            Assert.Equal(dated, record.WasLayer);
            // The copy from before MAXScript ran, renamed again, names a layer the scene no longer has: the record would go.
            Assert.Empty(PlannerAddedLayerStore.Present(answered.RenameAddedLayers(stale, afterSecond), afterSecond));
        }

        // -------------------------------------------------------------------------------------------------------------
        // Questions

        [Fact]
        public void ASecondTypeWordThatIsNotTheSameWordIsNotCalledDoubled()
        {
            var scene = Phasing();
            var layer = Layer(scene, "_02_Works", "02_01_Kerbs_01-02-26");
            var mixed = scene.Object("Ph2_St01_IN_RM_Barrier", layer, "02_01_Kerbs_01-02-26");
            var doubled = scene.Object("Ph2_St01_RM_RM_Marwood_Barrier3", layer, "02_01_Kerbs_01-02-26");
            var plan = Plan(scene.Snapshot);

            var q = plan.Questions.Single(x => x.Key == mixed.Key);
            Assert.Equal(PlannerCleanUpCase.DoubledType, q.Case);
            Assert.DoesNotContain("doubled", q.Why);
            Assert.Contains("leading IN_ is followed by a second type word, RM_", q.Why);
            var option = Assert.Single(q.Options);
            Assert.Equal("Take out the RM_ after IN_", option.Text);
            Assert.Equal("IN_Barrier", option.Name);

            var same = plan.Questions.Single(x => x.Key == doubled.Key);
            Assert.Equal("the type word is doubled (RM_RM_)", same.Why);
            Assert.Equal("Take out the second RM_", Assert.Single(same.Options).Text);
        }

        [Fact]
        public void AStrayObjectIsLinkedAloneOnlyWhenNothingElseMovesWithItOrStaysBehind()
        {
            const string hoarding = "02_01_Hoarding_01-10-26";
            const string barriers = "02_03_Barriers_05-10-26";
            var scene = Phasing();
            var layer = Layer(scene, "_02_Works", hoarding);
            Layer(scene, "_02_Works", barriers);
            // A group holding a stray member and one of its own layer: the member alone would be taken out of its group.
            var group = scene.Helper("Group001", layer, hoarding);
            group.IsGroupHead = true;
            var member = scene.Object("Ph2_St03_IN_Barrier", group, hoarding);
            member.IsGroupMember = true;
            scene.Object("Ph2_St01_IN_Fence", group, hoarding).IsGroupMember = true;
            // A stray object holding another tagged object: that one would be read against another layer.
            var post = scene.Object("Ph2_St03_IN_Post", layer, hoarding);
            scene.Object("Ph2_St01_IN_Post_Cap", post, hoarding);
            // A stray group head whose members carry no tags moves whole, onto the Max layer of the helper it is linked under.
            var head = scene.Helper("Ph2_St03_IN_Gate_Group", layer, hoarding);
            head.IsGroupHead = true;
            var leaf = scene.Object("Gate_Leaf_A", head, hoarding);
            leaf.IsGroupMember = true;
            var plan = Plan(scene.Snapshot);

            var inGroup = plan.Questions.Single(q => q.Key == member.Key);
            Assert.DoesNotContain(inGroup.Options, o => o.Id.StartsWith("link:", StringComparison.Ordinal));
            Assert.Contains("member of a 3ds Max group", inGroup.Why);
            var holding = plan.Questions.Single(q => q.Key == post.Key);
            Assert.DoesNotContain(holding.Options, o => o.Id.StartsWith("link:", StringComparison.Ordinal));
            Assert.Contains("tagged objects sit under it", holding.Why);

            var headQuestion = plan.Questions.Single(q => q.Key == head.Key);
            var link = headQuestion.Options.Single(o => o.Id.StartsWith("link:", StringComparison.Ordinal));
            var result = plan.Resolve(new Dictionary<string, string> { { headQuestion.Id, link.Id } });
            Assert.Equal(head.Key, Assert.Single(result.Links).Key);
            Assert.Equal(new[] { head.Key, leaf.Key }, result.LayerMoves.Select(m => m.Key).OrderBy(k => k).ToArray());
            Assert.All(result.LayerMoves, m => Assert.Equal(barriers, m.ToLayer));
            var after = result.ApplyTo(scene.Snapshot);
            Assert.Equal(head.Key, after.Nodes.Single(n => n.Key == leaf.Key).ParentKey);
            Assert.Equal("02_03_Barriers", after.Nodes.Single(n => n.Key == leaf.Key).LayerName);

            // The batch links the head only: no member is taken out of a group.
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            Assert.Equal(new[] { head.Key }, batch.Links.Select(l => l.Key).ToArray());
        }
    }
}
