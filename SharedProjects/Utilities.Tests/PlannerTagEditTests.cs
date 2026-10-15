using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>IN / RM edits: tags rewritten in object names, each checked by reading the scene again as the Planner would.</summary>
    public class PlannerTagEditTests
    {
        private static string Set(string name, PlannerTaskType type, int phase, int stage, int? sub = null)
        {
            string result, problem;
            Assert.True(PlannerTagEdit.TrySetTag(name, type, phase, stage, sub, out result, out problem), problem);
            return result;
        }

        private static string Remove(string name, PlannerTaskType type, int? phase, int stage, int? sub = null)
        {
            string result, problem;
            Assert.True(PlannerTagEdit.TryRemoveTag(name, type, phase, stage, sub, out result, out problem), problem);
            return result;
        }

        private static string Read(string name)
        {
            var parsed = PlannerNaming.ParseMeshName(name);
            if (parsed == null)
            {
                return "untagged";
            }
            Func<PlannerMeshSegment, string> text = s => s == null ? "-" : (s.Type == PlannerTaskType.Install ? "IN " : "RM ") + s.Phase + "/" + s.Stage + (s.SubStage.HasValue ? "_" + s.SubStage : "");
            return text(parsed.Lead) + " " + text(parsed.Trail) + " [" + parsed.Description + "]";
        }

        [Fact]
        public void TheTagOfTheSameTypeIsReplacedWhereItSitsAndTheRestStaysAsWritten()
        {
            const PlannerTaskType IN = PlannerTaskType.Install;
            const PlannerTaskType RM = PlannerTaskType.Dismantle;
            // No tag yet: it leads, as a name reads only with one.
            Assert.Equal("Ph2_St01_IN_Kerb_A", Set("Kerb_A", IN, 2, 1));
            // The leading install replaced, the trailing removal as written.
            Assert.Equal("Ph1_St05_IN_Kerb_ph2_st01_RM", Set("Ph1_St04_IN_Kerb_ph2_st01_RM", IN, 1, 5));
            // The trailing install replaced, the lower-case leading removal untouched (the amendment's example).
            Assert.Equal("ph1_st02_RM_Kerb_Ph3_St02_1_IN", Set("ph1_st02_RM_Kerb_ph2_st01_IN", IN, 3, 2, 1));
            Assert.Equal("RM 1/2 IN 3/2_1 [Kerb]", Read("ph1_st02_RM_Kerb_Ph3_St02_1_IN"));
            // A missing type goes on the end.
            Assert.Equal("ph1_st02_RM_Obj_Ph2_St01_IN", Set("ph1_st02_RM_Obj", IN, 2, 1));
            Assert.Equal("Ph1_St04-2_IN_Pile_Ph1_St06_RM", Set("Ph1_St04-2_IN_Pile", RM, 1, 6));
            Assert.Equal("Ph1_St01_IN_Kerb Stone_Ph2_St01_RM", Set("Ph1_St01_IN_Kerb Stone", RM, 2, 1));
            Assert.Equal("IN 1/1 RM 2/1 [Kerb Stone]", Read("Ph1_St01_IN_Kerb Stone_Ph2_St01_RM"));
            // The same numbers, written another way: nothing to do.
            Assert.Equal("ph1_st04.2_IN_Pile", Set("ph1_st04.2_IN_Pile", IN, 1, 4, 2));
            string result, problem;
            Assert.False(PlannerTagEdit.TrySetTag("Ph1_St01_IN_X_Ph2_St01_IN", IN, 3, 1, null, out result, out problem));
            Assert.Contains("two install tags", problem);

            // Removing: the trailing tag goes; the leading one goes and the trailing one, as written, leads.
            Assert.Equal("Ph1_St01_IN_X", Remove("Ph1_St01_IN_X_Ph1_St05_RM", RM, 1, 5));
            Assert.Equal("ph1_st05_RM_X", Remove("Ph1_St01_IN_X_ph1_st05_RM", IN, 1, 1));
            Assert.Equal("Ph2_St01_RM", Remove("Ph1_St01_IN_Ph2_St01_RM", IN, 1, 1));
            Assert.Equal("Kerb_2", Remove("Ph1_St01_IN_Kerb_2", IN, 1, 1));
            Assert.False(PlannerTagEdit.TryRemoveTag("Ph1_St01_IN", IN, 1, 1, null, out result, out problem));
            Assert.Contains("no name left", problem);
            Assert.False(PlannerTagEdit.TryRemoveTag("Kerb", IN, 1, 1, null, out result, out problem));
            // Of two installs, the one of the layer's stage.
            Assert.Equal("Ph1_St01_IN_X", Remove("Ph1_St01_IN_X_Ph2_St03_IN", IN, 2, 3));
        }

        private static PlannerTagTarget Target(PlannerSchedule schedule, string layerName)
        {
            return PlannerTagTarget.Of(layerName, schedule);
        }

        private static PlannerTagEditRequest Request(TestScene scene, string name, PlannerTaskType type, PlannerTagEditKind kind, PlannerTagTarget target)
        {
            return new PlannerTagEditRequest { Key = scene.Find(name).Key, Type = type, Kind = kind, Target = target };
        }

        // Applies the renames to the scene, as the MAXScript batch does.
        private static PlannerSceneSnapshot Apply(PlannerSceneSnapshot snapshot, PlannerTagEditPlan plan)
        {
            return PlannerTagEdit.Renamed(snapshot, plan.Renames.ToDictionary(r => r.Key, r => r.NewName));
        }

        [Fact]
        public void EditsRenameTagsAndFillReadsBackTheEditedLists()
        {
            var scene = PlannerScheduleTests.Scene();
            var options = PlannerScheduleTests.Utc;
            var before = PlannerSchedule.Build(scene.Snapshot, options);
            var reinstate = Target(before, PlannerScheduleTests.Reinstate);
            var strip = Target(before, PlannerScheduleTests.Strip);
            Assert.Equal("Ph2_St01", reinstate.TagText);

            var requests = new List<PlannerTagEditRequest>
            {
                // An untagged object in 02_01 into its IN.
                Request(scene, "Plain_Box", PlannerTaskType.Install, PlannerTagEditKind.Add, reinstate),
                // An object installed in 02_01 also removed by 01_02 (the removal before the install).
                Request(scene, "Ph2_St01_IN_New_Kerb", PlannerTaskType.Dismantle, PlannerTagEditKind.Add, strip)
            };
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, requests);
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "Ph2_St01_IN_Plain_Box", "Ph2_St01_IN_New_Kerb_Ph1_St02_RM" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.Equal("IN of " + PlannerScheduleTests.Reinstate + ": install tag Ph2_St01", plan.Renames[0].Why);

            // The edit never moves or links anything: only names change.
            var after = Apply(scene.Snapshot, plan);
            Assert.Equal(scene.Snapshot.Nodes.Select(n => n.ParentKey + "/" + n.LayerName), after.Nodes.Select(n => n.ParentKey + "/" + n.LayerName));
            var filled = PlannerSchedule.Build(after, options);
            Assert.Contains("Ph2_St01_IN_Plain_Box", PlannerScheduleTests.Column(filled, "02_01_Reinstate", PlannerTaskType.Install));
            Assert.Contains("Ph2_St01_IN_New_Kerb_Ph1_St02_RM", PlannerScheduleTests.Column(filled, "01_02_Strip_Out", PlannerTaskType.Dismantle));
            Assert.Contains("Ph2_St01_IN_New_Kerb_Ph1_St02_RM", PlannerScheduleTests.Column(filled, "02_01_Reinstate", PlannerTaskType.Install));
            // Every other object reads as before.
            foreach (var name in before.Objects.Keys.Where(n => plan.Renames.All(r => r.OldName != n)))
            {
                Assert.Equal(before.PlacesOf(name), filled.PlacesOf(name));
            }
            Assert.DoesNotContain(plan.Review, r => r.Status == "Also");

            // Taking the install out of 02_01 for an object removed by 01_02 first: its removal, as written, now leads, and
            // it is still removed by 01_02.
            var removal = PlannerTagEdit.Plan(scene.Snapshot, options, new[]
            {
                Request(scene, "Ph2_St01_IN_Fence_Ph1_St02_RM", PlannerTaskType.Install, PlannerTagEditKind.Remove, reinstate)
            });
            Assert.Equal(0, removal.ProblemCount);
            Assert.Equal("Ph1_St02_RM_Fence", removal.Renames.Single().NewName);
            Assert.Equal(new[] { "RM 01_02_Strip_Out" }, PlannerSchedule.Build(Apply(scene.Snapshot, removal), options).PlacesOf("Ph1_St02_RM_Fence").ToArray());
        }

        [Fact]
        public void AnEditThePlannerWouldReadOtherwiseIsAProblemAndKeepsItsName()
        {
            var scene = PlannerScheduleTests.Scene();
            var options = PlannerScheduleTests.Utc;
            var before = PlannerSchedule.Build(scene.Snapshot, options);
            Func<string, PlannerTaskType, PlannerTagEditKind, string, PlannerTagEditPlan> plan = (name, type, kind, layer) =>
                PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, name, type, kind, Target(before, layer)) });

            // Into the IN of an undated layer while sitting in another: the Planner cannot date it from there.
            var undated = plan("Ph2_St01_IN_New_Kerb", PlannerTaskType.Install, PlannerTagEditKind.Add, PlannerScheduleTests.Undated);
            Assert.Equal(1, undated.ProblemCount);
            Assert.Empty(undated.Renames);
            var why = undated.Review.Single().Why;
            Assert.Contains("sits in " + PlannerScheduleTests.Reinstate, why);
            Assert.Contains("has no dates", why);
            Assert.Equal("Ph1_St03_IN_New_Kerb", undated.Review.Single().To);

            // Into a layer with no tagged object yet.
            var empty = plan("Ph2_St01_IN_New_Kerb", PlannerTaskType.Install, PlannerTagEditKind.Add, PlannerScheduleTests.Empty);
            Assert.Contains("holds no tagged object yet", empty.Review.Single().Why);

            // An object not linked under a dated layer's helper.
            var loose = plan("Ph2_St01_IN_Loose", PlannerTaskType.Dismantle, PlannerTagEditKind.Add, PlannerScheduleTests.Strip);
            Assert.Contains("not linked under a dated layer's helper", loose.Review.Single().Why);

            // A name another node has.
            scene.Object("Ph2_St01_IN_Plain_Box", null, "0");
            var clash = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Plain_Box", PlannerTaskType.Install, PlannerTagEditKind.Add, Target(before, PlannerScheduleTests.Reinstate)) });
            Assert.Contains("another node is called already", clash.Review.Single().Why);

            // A layer's helper is never retagged: its name is its layer's.
            var helper = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, PlannerScheduleTests.Reinstate, PlannerTaskType.Install, PlannerTagEditKind.Add, Target(before, PlannerScheduleTests.Strip)) });
            Assert.Contains("it is the helper of the layer " + PlannerScheduleTests.Reinstate, helper.Review.Single().Why);
            Assert.Empty(helper.Renames);

            // A one-number layer with no phase from its objects has no tag to write.
            var target = PlannerTagTarget.Of("07_Hoarding_01-10-26", before);
            Assert.Null(target.Phase);
            var noPhase = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Plain_Box", PlannerTaskType.Install, PlannerTagEditKind.Add, target) });
            Assert.Contains("no phase to write", noPhase.Review.Single().Why);
        }

        [Fact]
        public void ObjectsTheRenamesMakeThePlannerReadAnewAreListed()
        {
            // A one-number layer takes its phase from most of its objects' leading tags of its stage (the lower on a
            // tie): with three of phase 3 and two of phase 2, it is phase 3. Taking one of phase 3 into another stage's
            // layer leaves a tie, so the layer becomes phase 2, and its phase 3 objects are then dated by 03_05.
            const string hoarding = "05_Hoarding_01-10-26_02-10-26";
            var scene = new TestScene();
            var root = scene.Unit("Phasing", null, null);
            var layer = scene.Unit(hoarding, "Phasing", root);
            foreach (var name in new[] { "Ph2_St05_IN_A", "Ph2_St05_IN_B", "Ph3_St05_IN_C", "Ph3_St05_IN_D", "Ph3_St05_IN_E" })
            {
                scene.Object(name, layer, hoarding);
            }
            scene.Object("Ph3_St05_IN_F", scene.Unit("03_05_Other_05-10-26", "Phasing", root), "03_05_Other_05-10-26");
            scene.Object("Ph1_St07_IN_G", scene.Unit("01_07_Gate_10-10-26", "Phasing", root), "01_07_Gate_10-10-26");
            var options = PlannerScheduleTests.Utc;
            var before = PlannerSchedule.Build(scene.Snapshot, options);
            Assert.Equal(3, before.Layers.Single(l => l.Label == "05_Hoarding").Phase);
            Assert.Equal(new[] { "IN 05_Hoarding" }, before.PlacesOf("Ph3_St05_IN_D").ToArray());
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[]
            {
                Request(scene, "Ph3_St05_IN_C", PlannerTaskType.Install, PlannerTagEditKind.Add, PlannerTagTarget.Of("01_07_Gate_10-10-26", before))
            });
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal("Ph1_St07_IN_C", plan.Renames.Single().NewName);
            var also = plan.Review.Where(r => r.Status == "Also").ToList();
            Assert.Equal(new[] { "Ph3_St05_IN_D", "Ph3_St05_IN_E" }, also.Select(r => r.Name).ToArray());
            Assert.All(also, r => Assert.Equal("IN 05_Hoarding", r.From));
            Assert.All(also, r => Assert.Equal("IN 03_05_Other", r.To));
            Assert.All(also, r => Assert.Contains("its layer " + hoarding + " has one number in its ID and takes its phase from its objects' tags: with the renames it is phase 2 (it was phase 3)", r.Why));
            Assert.NotEmpty(plan.Notes);
            var after = PlannerSchedule.Build(Apply(scene.Snapshot, plan), options);
            Assert.Equal(new[] { "IN 01_07_Gate" }, after.PlacesOf("Ph1_St07_IN_C").ToArray());
            Assert.Equal(new[] { "IN 03_05_Other" }, after.PlacesOf("Ph3_St05_IN_D").ToArray());
        }

        // A layer with a closed group, a hierarchy and an object split into pieces, for the selection tests.
        private const string Kerbs = "02_01_Kerbs_01-09-26_05-09-26";
        private const string Strip = "02_02_Strip_08-09-26_09-09-26";

        private static TestScene SelectionScene()
        {
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var zone = scene.Unit("_02_Zone", "Work_Phasing", root);
            var kerbs = scene.Unit(Kerbs, "_02_Zone", zone);
            scene.Object("Ph2_St01_IN_KerbA", kerbs, Kerbs);
            var strip = scene.Unit(Strip, "_02_Zone", zone);
            scene.Object("Ph2_St02_RM_Old", strip, Strip);
            // A closed group: its head (a helper) and its members.
            var head = scene.Helper("Scaffold_Group", kerbs, Kerbs);
            head.IsGroupHead = true;
            foreach (var name in new[] { "Box001", "Box002" })
            {
                scene.Object(name, head, Kerbs).IsGroupMember = true;
            }
            // An object split into pieces (Name_N under Name, read as part of Name).
            var wall = scene.Object("Ph2_St01_IN_Wall", kerbs, Kerbs);
            scene.Object("Ph2_St01_IN_Wall_1", wall, Kerbs);
            scene.Object("Ph2_St01_IN_Wall_2", wall, Kerbs);
            // A hierarchy that is not a group: a tagged crane with an untagged hook and a tagged jib under it.
            var crane = scene.Object("Ph2_St01_IN_Crane", kerbs, Kerbs);
            scene.Object("Hook", crane, Kerbs);
            scene.Object("Ph2_St01_IN_Jib", crane, Kerbs);
            return scene;
        }

        private static PlannerTagEditPlan AddTo(TestScene scene, PlannerTaskType type, string layer, params string[] names)
        {
            var options = PlannerScheduleTests.Utc;
            var target = Target(PlannerSchedule.Build(scene.Snapshot, options), layer);
            return PlannerTagEdit.Plan(scene.Snapshot, options, names.Select(n => Request(scene, n, type, PlannerTagEditKind.Add, target)).ToList());
        }

        [Fact]
        public void AClosedGroupSelectedWholeIsEditedByItsHead()
        {
            // Clicking a closed group in 3ds Max selects its head and every member: the head takes the tag, the untagged
            // members are part of it (the Planner shows and hides them with it) and are left out, with a note.
            var scene = SelectionScene();
            var options = PlannerScheduleTests.Utc;
            var plan = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Scaffold_Group", "Box001", "Box002");
            Assert.Equal(0, plan.ProblemCount);
            var rename = plan.Renames.Single();
            Assert.Equal("Scaffold_Group", rename.OldName);
            Assert.Equal("Ph2_St02_RM_Scaffold_Group", rename.NewName);
            Assert.Contains(plan.Notes, n => n.Contains("2 objects under Scaffold_Group (Box001, Box002) left out"));
            var after = PlannerSchedule.Build(Apply(scene.Snapshot, plan), options);
            Assert.Equal(new[] { "RM 02_02_Strip" }, after.PlacesOf("Ph2_St02_RM_Scaffold_Group").ToArray());
            Assert.Empty(after.PlacesOfNode(scene.Find("Box001").Key));
            Assert.Contains(scene.Find("Box001").Key, after.HiddenAt(after.Midnight(new DateTime(2026, 9, 10))));

            // A tagged head: the same, its removal added at the end.
            scene.Find("Scaffold_Group").Name = "Ph2_St01_IN_Scaffold";
            var tagged = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Scaffold", "Box001", "Box002");
            Assert.Equal("Ph2_St01_IN_Scaffold_Ph2_St02_RM", tagged.Renames.Single().NewName);

            // A member with a tag of its own, selected with its group: a Problem, as only the modeller knows which is meant.
            scene.Find("Box002").Name = "Ph2_St01_IN_Panel";
            var mixed = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Scaffold", "Box001", "Ph2_St01_IN_Panel");
            Assert.Equal(1, mixed.ProblemCount);
            Assert.Contains("inside the closed group Ph2_St01_IN_Scaffold", mixed.Review.Single(r => r.Status == "Problem").Why);
            Assert.Contains("Group > Open", mixed.Review.Single(r => r.Status == "Problem").Why);
            Assert.Equal("Ph2_St01_IN_Scaffold_Ph2_St02_RM", mixed.Renames.Single().NewName);

            // Chosen alone (an open group), a member is edited like any object.
            var alone = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Panel");
            Assert.Equal("Ph2_St01_IN_Panel_Ph2_St02_RM", alone.Renames.Single().NewName);
        }

        [Fact]
        public void AHierarchySelectedWholeKeepsItsUntaggedPartsAndItsTaggedObjects()
        {
            var scene = SelectionScene();
            var plan = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Crane", "Hook", "Ph2_St01_IN_Jib");
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "Ph2_St01_IN_Crane_Ph2_St02_RM", "Ph2_St01_IN_Jib_Ph2_St02_RM" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.Contains(plan.Notes, n => n.Contains("1 object under Ph2_St01_IN_Crane (Hook) left out"));
            // A layer's helper chosen with its objects is no object, and what is under it is not left out for it.
            var withHelper = AddTo(scene, PlannerTaskType.Dismantle, Strip, Kerbs, "Ph2_St01_IN_KerbA");
            Assert.Contains("it is the helper of the layer " + Kerbs, withHelper.Review.Single(r => r.Status == "Problem").Why);
            Assert.Equal("Ph2_St01_IN_KerbA_Ph2_St02_RM", withHelper.Renames.Single().NewName);
        }

        [Fact]
        public void SplitPiecesAreRenamedWithTheirObject()
        {
            var scene = SelectionScene();
            var options = PlannerScheduleTests.Utc;
            var plan = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Wall");
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "Ph2_St01_IN_Wall_Ph2_St02_RM", "Ph2_St01_IN_Wall_Ph2_St02_RM_1", "Ph2_St01_IN_Wall_Ph2_St02_RM_2" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.All(plan.Renames.Skip(1), r => Assert.StartsWith("a split piece of Ph2_St01_IN_Wall: renamed with it", r.Why));
            Assert.DoesNotContain(plan.Review, r => r.Status == "Also");
            var after = PlannerSchedule.Build(Apply(scene.Snapshot, plan), options);
            var wall = after.ObjectOf(scene.Find("Ph2_St01_IN_Wall").Key);
            Assert.Equal(wall, after.PieceOf(scene.Find("Ph2_St01_IN_Wall_1").Key));
            Assert.Equal(new[] { "IN 02_01_Kerbs", "RM 02_02_Strip" }, after.PlacesOf(wall.Name).ToArray());
            // The pieces go and come with it.
            var removed = after.HiddenAt(after.Midnight(new DateTime(2026, 9, 10)));
            Assert.Contains(scene.Find("Ph2_St01_IN_Wall_1").Key, removed);
            Assert.Contains(scene.Find("Ph2_St01_IN_Wall_2").Key, removed);

            // Chosen with its object, a piece is part of it; chosen alone, it is a Problem.
            var together = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Wall", "Ph2_St01_IN_Wall_1", "Ph2_St01_IN_Wall_2");
            Assert.Equal(0, together.ProblemCount);
            Assert.Equal(3, together.Renames.Count);
            var alone = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Wall_1");
            Assert.Contains("it is a split piece of Ph2_St01_IN_Wall", alone.Review.Single().Why);
            Assert.Empty(alone.Renames);

            // A piece that cannot follow (its new name is taken) keeps its object from being renamed.
            scene.Object("Ph2_St01_IN_Wall_Ph2_St02_RM_2", null, "0");
            var blocked = AddTo(scene, PlannerTaskType.Dismantle, Strip, "Ph2_St01_IN_Wall");
            Assert.Empty(blocked.Renames);
            Assert.Contains("its split piece Ph2_St01_IN_Wall_2 would be called Ph2_St01_IN_Wall_Ph2_St02_RM_2, which another node is called already", blocked.Review.Single().Why);
        }

        [Fact]
        public void EditsTargetTheLayerAsThePlannerReadsItsHelper()
        {
            // A scene built by hand: the Max layer re-dated and renamed, its helper (the name the GLB carries) not. The row
            // is the Max layer's; the edit is the layer the Planner reads.
            const string layer = "02_01_Kerbing_01-09-26_05-09-26";
            const string helper = "02_01_Kerbs_01-09-26_05-09-26";
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            scene.Layer(layer, "Work_Phasing");
            var kerbs = scene.Helper(helper, root, layer);
            scene.Object("Ph2_St01_IN_KerbA", kerbs, layer);
            scene.Object("Box001", kerbs, layer);
            var options = PlannerScheduleTests.Utc;
            var schedule = PlannerSchedule.Build(scene.Snapshot, options);
            var model = new PlannerSceneModel(scene.Snapshot);
            var row = PlannerScheduleRows.Tree(null, null, model, null, schedule).Single(r => r.LayerName == layer);
            Assert.Equal("02_01_Kerbs", row.Layer.Label);
            Assert.True(row.Editable);
            var target = PlannerTagTarget.For(row, schedule);
            Assert.Equal(helper, target.LayerName);
            Assert.Equal("02_01_Kerbs", target.Label);
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Box001", PlannerTaskType.Install, PlannerTagEditKind.Add, target) });
            Assert.Equal(0, plan.ProblemCount);
            var after = PlannerSchedule.Build(Apply(scene.Snapshot, plan), options);
            Assert.Equal(new[] { "Ph2_St01_IN_Box001", "Ph2_St01_IN_KerbA" }, PlannerScheduleTests.Column(after, "02_01_Kerbs", PlannerTaskType.Install).ToArray());
            var remove = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Ph2_St01_IN_KerbA", PlannerTaskType.Install, PlannerTagEditKind.Remove, target) });
            Assert.Equal(0, remove.ProblemCount);
            Assert.Equal("KerbA", remove.Renames.Single().NewName);
        }

        [Fact]
        public void ANodeTheExportRenamesIsEditedByItsOwnNode()
        {
            // Two nodes called Ph2_St01_IN_New_Kerb: the second (in 02_01, written after 01_02's) reaches the Planner as
            // "Ph2_St01_IN_New_Kerb (1)". Editing it is about that node, by its handle, not about the other of its name.
            var scene = PlannerScheduleTests.Scene();
            var options = PlannerScheduleTests.Utc;
            scene.Object("Ph2_St01_IN_New_Kerb", scene.Find(PlannerScheduleTests.Strip), PlannerScheduleTests.Strip);
            var second = scene.Snapshot.Nodes.Single(n => n.Name == "Ph2_St01_IN_New_Kerb" && n.ParentKey == scene.Find(PlannerScheduleTests.Reinstate).Key);
            var before = PlannerSchedule.Build(scene.Snapshot, options);
            Assert.Equal("Ph2_St01_IN_New_Kerb (1)", before.NameOf(second.Key));
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[]
            {
                new PlannerTagEditRequest { Key = second.Key, Type = PlannerTaskType.Dismantle, Kind = PlannerTagEditKind.Add, Target = Target(before, PlannerScheduleTests.Strip) }
            });
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal("Ph2_St01_IN_New_Kerb_Ph1_St02_RM", plan.Renames.Single().NewName);
            var after = PlannerSchedule.Build(Apply(scene.Snapshot, plan), options);
            Assert.Equal(new[] { "IN 02_01_Reinstate", "RM 01_02_Strip_Out" }, after.PlacesOfNode(second.Key).ToArray());
        }

        [Fact]
        public void TheBatchRenamesByHandleInOneUndoRecord()
        {
            var plan = new PlannerTagEditPlan();
            plan.Renames.Add(new PlannerTagRename { Key = 12, OldName = "Kerb \"A\"", NewName = "Ph2_St01_IN_Kerb \"A\"" });
            plan.Renames.Add(new PlannerTagRename { Key = 13, OldName = "Bad\u0001", NewName = "Ph2_St01_IN_Bad" });
            List<string> refused;
            List<PlannerTagRename> sent;
            var script = PlannerTagEdit.Script(plan, out refused, out sent);
            Assert.Single(sent);
            Assert.Single(refused);
            Assert.Contains("undo \"Planner IN / RM\" on", script);
            Assert.Contains("local bHandles = #(12)", script);
            Assert.Contains("\"Kerb \\\"A\\\"\"", script);
            Assert.DoesNotContain("\r\r", script);
            int renamed;
            List<string> failures;
            bool completed;
            string aborted;
            PlannerTagEdit.ReadResult(sent, "FAIL\trename\t1\tchanged\nDONE\t0\n" + PlannerTagEdit.EndMarker + "\n", out renamed, out failures, out completed, out aborted);
            Assert.True(completed);
            Assert.Equal(0, renamed);
            Assert.Equal("Kerb \"A\" has been renamed since the list was made, so it was not renamed", failures.Single());
        }
    }
}
