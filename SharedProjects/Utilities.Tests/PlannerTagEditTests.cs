using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// IN / RM edits on the new names (naming v2, spec 5.2): IN_ / RM_ tags written in object names, each read back as the
    /// Planner reads it and checked by reading the scene again as the new names' preview does.
    /// </summary>
    public class PlannerTagEditTests
    {
        private const PlannerTaskType IN = PlannerTaskType.Install;
        private const PlannerTaskType RM = PlannerTaskType.Dismantle;

        // The object's own layer (Lo) and another layer (M), as in spec 5.2's table.
        private static readonly PlannerLayerIdKey Own = new PlannerLayerIdKey(2, 1, null);
        private static readonly PlannerLayerIdKey M = new PlannerLayerIdKey(6, 5, 2);

        private static string Set(string name, PlannerTaskType type, PlannerLayerIdKey layer)
        {
            string result, problem;
            Assert.True(PlannerTagEdit.TrySetTag(name, type, Own, layer, out result, out problem), name + ": " + problem);
            return result;
        }

        private static string Refused(string name, PlannerTaskType type, PlannerLayerIdKey layer)
        {
            string result, problem;
            Assert.False(PlannerTagEdit.TrySetTag(name, type, Own, layer, out result, out problem), name + " gave " + result);
            return problem;
        }

        private static string Remove(string name, PlannerTaskType type)
        {
            string result, problem;
            Assert.True(PlannerTagEdit.TryRemoveTag(name, type, Own, out result, out problem), name + ": " + problem);
            return result;
        }

        private static string RefusedRemove(string name, PlannerTaskType type)
        {
            string result, problem;
            Assert.False(PlannerTagEdit.TryRemoveTag(name, type, Own, out result, out problem), name + " gave " + result);
            return problem;
        }

        // A name as the Planner reads it in its own layer 02_01: where each event is, and its own name.
        private static string Read(string name)
        {
            var p = PlannerNaming.ParseObjectNameV2(name, Own);
            if (p.Kind != PlannerNamingKind.New)
            {
                return "not scheduled [" + p.Own + "]";
            }
            Func<PlannerTaskType, string> at = t => p.Lead == t ? "during" : p.Trail == t ? (p.Ref == null ? "end" : "in " + PlannerNaming.LayerIdText(p.Ref)) : "-";
            return "IN " + at(IN) + ", RM " + at(RM) + " [" + p.Own + "]";
        }

        // ---------------------------------------------------------------------------------------------------------
        // The tag writer

        [Fact]
        public void AddWritesTheTagWhereSpec52PutsIt()
        {
            // Spec 5.2's table, row by row: X, IN_X, RM_X and IN_X_RM, each with Add IN and Add RM for Lo and for M.
            Assert.Equal("IN_X", Set("X", IN, Own));
            Assert.Equal("X_IN_06_05_2", Set("X", IN, M));
            Assert.Equal("RM_X", Set("X", RM, Own));
            Assert.Equal("X_RM_06_05_2", Set("X", RM, M));

            Assert.Equal("IN_X", Set("IN_X", IN, Own));
            Assert.Equal("X_IN_06_05_2", Set("IN_X", IN, M));
            Assert.Equal("IN_X_RM", Set("IN_X", RM, Own));
            Assert.Equal("IN_X_RM_06_05_2", Set("IN_X", RM, M));

            Assert.Equal("RM_X_IN", Set("RM_X", IN, Own));
            Assert.Equal("RM_X_IN_06_05_2", Set("RM_X", IN, M));
            Assert.Equal("RM_X", Set("RM_X", RM, Own));
            Assert.Equal("X_RM_06_05_2", Set("RM_X", RM, M));

            Assert.Equal("IN_X_RM", Set("IN_X_RM", IN, Own));
            Assert.Equal("the end of its name holds its removal tag (RM), and only a tag at the end can name another layer: an object has one leading and one trailing tag, "
                + "so take that one out first", Refused("IN_X_RM", IN, M));
            Assert.Equal("IN_X_RM", Set("IN_X_RM", RM, Own));
            Assert.Equal("IN_X_RM_06_05_2", Set("IN_X_RM", RM, M));

            // Each reads back as the table means it.
            Assert.Equal("IN in 06_05_2, RM - [X]", Read("X_IN_06_05_2"));
            Assert.Equal("IN during, RM end [X]", Read("IN_X_RM"));
            Assert.Equal("IN end, RM during [X]", Read("RM_X_IN"));
            Assert.Equal("IN during, RM in 06_05_2 [X]", Read("IN_X_RM_06_05_2"));
        }

        [Fact]
        public void TheTagOfThatTypeIsReplacedAndTheRestStaysAsWritten()
        {
            var other = new PlannerLayerIdKey(3, 1, null);
            // A tag naming another layer is replaced where it sits.
            Assert.Equal("IN_X_RM_03_01", Set("IN_X_RM_06_05_2", RM, other));
            Assert.Equal("RM_Bench_IN_03_01", Set("RM_Bench_IN_06_05_2", IN, other));
            Assert.Equal("IN_Barrier_RM_06_05_2", Set("IN_Barrier_RM_01_23", RM, M));
            // In its own layer already: as it is, at the end too; a tag naming its own layer becomes the plain one it means.
            Assert.Equal("Kerb_IN", Set("Kerb_IN", IN, Own));
            Assert.Equal("IN_X_RM", Set("IN_X_RM_02_01", RM, Own));
            Assert.Equal("IN during, RM end [X]", Read("IN_X_RM_02_01"));
            // From another layer to its own: the tag comes out, then goes at the start, as that place is free.
            Assert.Equal("RM_X", Set("X_RM_06_05_2", RM, Own));
            Assert.Equal("IN_X_RM_06_05_2", Set("X_RM_06_05_2", IN, Own));
            // The own name as written: a number closing it (the type word marks where the layer ID starts), underscores the
            // reader trims, a hyphen.
            Assert.Equal("IN_Barrier_01_RM_06_05_2", Set("IN_Barrier_01", RM, M));
            Assert.Equal("IN during, RM in 06_05_2 [Barrier_01]", Read("IN_Barrier_01_RM_06_05_2"));
            Assert.Equal("Barrier_02_RM_06_05_2", Set("RM_Barrier_02", RM, M));
            Assert.Equal("IN__Kerb__RM_06_05_2", Set("IN__Kerb_", RM, M));
            Assert.Equal("IN_Kerb-A", Set("Kerb-A", IN, Own));
            // A tag at the end goes before a note after a space, and before a " (n)".
            Assert.Equal("IN_Road_RM_06_05_2 Plates", Set("IN_Road Plates", RM, M));
            Assert.Equal("IN_Kerb_RM (1)", Set("IN_Kerb (1)", RM, Own));
            Assert.Equal("IN_Kerb Stone", Set("Kerb Stone", IN, Own));
            Assert.Equal("IN during, RM end [Kerb]", Read("IN_Kerb_RM (1)"));
            // Capitals only are tags: "In_Situ" is part of the name.
            Assert.Equal("RM_In_Situ_Wall", Set("In_Situ_Wall", RM, Own));
        }

        [Fact]
        public void RemoveTakesTheTagOutWhereItIs()
        {
            // The other tag stays where it is: its place carries its meaning.
            Assert.Equal("X_RM", Remove("IN_X_RM", IN));
            Assert.Equal("IN -, RM end [X]", Read("X_RM"));
            Assert.Equal("IN_X", Remove("IN_X_RM", RM));
            Assert.Equal("X", Remove("IN_X", IN));
            Assert.Equal("not scheduled [X]", Read("X"));
            Assert.Equal("X_IN_03_01", Remove("RM_X_IN_03_01", RM));
            Assert.Equal("RM_X", Remove("RM_X_IN_03_01", IN));
            Assert.Equal("Road Plates", Remove("IN_Road Plates", IN));
            Assert.Equal("IN_Kerb (1)", Remove("IN_Kerb_RM_06_05_2 (1)", RM));
            Assert.Equal("it has no IN tag", RefusedRemove("RM_X", IN));
        }

        [Fact]
        public void ANameThatWouldNotReadAsMeantIsRefused()
        {
            // An old Ph/St tag anywhere: Clean up scene graph converts it first.
            Assert.Equal(PlannerTagEdit.OldTagProblem, Refused("Ph2_St01_IN_Kerb", IN, Own));
            Assert.Equal(PlannerTagEdit.OldTagProblem, RefusedRemove("IN_Kerb_Ph6_St05_RM", IN));
            // The same type at both ends (D3), which the Planner reads as the one at the start only.
            Assert.Equal("its name has IN at both ends (IN_\u2026_IN), which the Planner reads as the one at the start only: edit its name by hand", Refused("IN_Kerb_IN", IN, M));
            Assert.Contains("IN at both ends", RefusedRemove("IN_Kerb_IN", IN));
            Assert.Contains("both ends of its name hold a tag already (IN_\u2026_IN), so there is no place for RM", Refused("IN_Kerb_IN", RM, Own));
            Assert.Contains("only a tag at the end can name another layer", Refused("RM_Kerb_RM_03_01", IN, M));
            // A type word the new name would read where none was meant: taking out IN_ leaves RM_ at the start.
            Assert.Equal("the new name RM_Kerb would read as removed while its own layer runs (it is not removed now), which is not what was asked: edit its name by hand",
                RefusedRemove("IN_RM_Kerb", IN));
            // A new naming error: a name that is only a type word, given a tag.
            Assert.Contains("would be a naming error (The name after the leading tag starts with IN or RM again)", Refused("RM", IN, Own));
            // Another own name: taking out the last tag of "IN_X_IN_06_05_2_RM" leaves its other IN at the end.
            Assert.Contains("own name X (it is X_IN_06_05_2 now)", RefusedRemove("IN_X_IN_06_05_2_RM", RM));
            // A name refused on reading back is given, for the review.
            string result, problem;
            Assert.False(PlannerTagEdit.TryRemoveTag("IN_RM_Kerb", IN, Own, out result, out problem));
            Assert.Equal("RM_Kerb", result);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Planning on a scene

        private const string ListHeader = "Planner ID,Parent ID,Layer,Kind,ID,Phase,Stage,Sub-stage,Name,Start,Finish,TBC,Order,Project start";

        // The Planner's list for PlannerScheduleV2Tests.Scene: 01_01 and 02_01 dated, 01_02 TBC, 02_02 only in the list.
        private static PlannerLayerList SceneList()
        {
            return PlannerLayerList.Parse("\ufeff" + ListHeader + "\r\n" + string.Join("\r\n", new[]
            {
                "g1,,_01_Zone,Group,,,,,Zone,,,,1,2026-08-31",
                "l1,g1,01_01_Strip_Out,Layer,01_01,1,1,,Strip_Out,2026-09-01,2026-09-02,,1,2026-08-31",
                "l2,g1,01_02_Kerbs,Layer,01_02,1,2,,Kerbs,,,Yes,2,2026-08-31",
                "g2,,_02_Zone,Group,,,,,Zone,,,,2,2026-08-31",
                "l3,g2,02_01_Reinstate,Layer,02_01,2,1,,Reinstate,2026-09-10,2026-09-10,,1,2026-08-31",
                "l4,g2,02_02_Planned,Layer,02_02,2,2,,Planned,2026-09-15,2026-09-15,,2,2026-08-31"
            }) + "\r\n");
        }

        // The tab's options: the list's folders and project start (none: every layer parked from 1 Sep), in UTC.
        private static PlannerScheduleOptions Options(PlannerLayerList list, PlannerSceneSnapshot snapshot)
        {
            var plan = list != null ? PlannerLayerPlanner.Plan(list, new PlannerSceneModel(snapshot)) : null;
            return new PlannerScheduleOptions
            {
                Zone = TimeZoneInfo.Utc,
                Today = new DateTime(2026, 10, 15),
                ProjectStart = list != null ? list.ProjectStart : new DateTime(2026, 9, 1),
                Folders = PlannerScheduleRows.PlannerFolders(list, plan)
            };
        }

        private static PlannerTagTarget Target(PlannerSchedule schedule, string id)
        {
            return PlannerTagTarget.Of(schedule.Layers.Single(l => PlannerNaming.LayerIdText(l.Id) == id));
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
        public void EditsRenameTagsAndThePreviewReadsBackTheEditedLists()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            var list = SceneList();
            var options = Options(list, scene.Snapshot);
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);
            var strip = Target(before, "01_01");
            var planned = Target(before, "02_02");
            Assert.Equal("01_01_Strip_Out", strip.Label);
            Assert.True(strip.InScene);
            // A layer only the Planner's list has: a tag at the end names it.
            Assert.Equal("02_02_Planned", planned.Label);
            Assert.False(planned.InScene);

            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[]
            {
                // An untagged object in 01_01 into its IN: IN_ at the start.
                Request(scene, "Plain_Box", IN, PlannerTagEditKind.Add, strip),
                // An object installed in 02_01, removed in the list's 02_02.
                Request(scene, "IN_New_Kerb", RM, PlannerTagEditKind.Add, planned),
                // A removal naming a layer nobody has, now naming 02_02: replaced where it sits.
                Request(scene, "IN_Barrier_RM_01_23", RM, PlannerTagEditKind.Add, planned)
            });
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "IN_Plain_Box", "IN_New_Kerb_RM_02_02", "IN_Barrier_RM_02_02" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.Equal("IN of 01_01_Strip_Out: IN_ at the start (installed while 01_01_Strip_Out runs)", plan.Renames[0].Why);
            Assert.Equal("RM of 02_02_Planned: _RM_02_02 at the end (removed in 02_02_Planned, a layer only the Planner's list has so far)", plan.Renames[1].Why);
            Assert.DoesNotContain(plan.Review, r => r.Status == "Also");

            // The edit never moves or links anything: only names change.
            var after = Apply(scene.Snapshot, plan);
            Assert.Equal(scene.Snapshot.Nodes.Select(n => n.ParentKey + "/" + n.LayerName), after.Nodes.Select(n => n.ParentKey + "/" + n.LayerName));
            // The preview shows the change: each in the column asked for, on the layer's days.
            var filled = PlannerSchedule.BuildV2(after, Options(list, after));
            Assert.Equal(new[] { "IN 01_01_Strip_Out" }, filled.PlacesOf("IN_Plain_Box").ToArray());
            Assert.Equal(new[] { "IN 02_01_Reinstate", "RM 02_02_Planned" }, filled.PlacesOf("IN_New_Kerb_RM_02_02").ToArray());
            Assert.Equal(new[] { "IN 01_02_Kerbs", "RM 02_02_Planned" }, filled.PlacesOf("IN_Barrier_RM_02_02").ToArray());
            var kerb = scene.Find("IN_New_Kerb").Key;
            Assert.DoesNotContain(kerb, filled.HiddenAt(filled.Midnight(new DateTime(2026, 9, 14))));
            Assert.Contains(kerb, filled.HiddenAt(filled.Midnight(new DateTime(2026, 9, 16))));
            Assert.Equal(new[] { "IN 01_01_Strip_Out", "IN 02_01_Reinstate", "RM 02_02_Planned" },
                plan.After.Activities.Where(a => a.Members.Any(m => m == "IN_Plain_Box" || m == "IN_New_Kerb_RM_02_02")).Select(PlannerSchedule.PlaceOf).OrderBy(p => p).ToArray());
            // Every other object reads as before.
            foreach (var name in before.Objects.Keys.Where(n => plan.Renames.All(r => r.OldName != n)))
            {
                Assert.Equal(before.PlacesOf(name), filled.PlacesOf(name));
            }
            // The list-only layer's row holds them in its RM column, and can be edited.
            var model = new PlannerSceneModel(after);
            var rows = PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, model), model, null, filled, PlannerNamingKind.New);
            var row = rows.Single(r => r.Name == "02_02_Planned");
            Assert.True(row.Editable);
            Assert.Equal(new[] { "IN_Barrier_RM_02_02", "IN_New_Kerb_RM_02_02" }, row.Rm.SelectMany(a => a.Members).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal("02_02_Planned", PlannerTagTarget.For(row, filled).Label);
            Assert.False(rows.Single(r => r.Name == "_02_Zone").Editable);

            // Remove from 02_02's RM: the removal comes out where it is.
            var renamed = new TestScene(after);
            var removal = PlannerTagEdit.Plan(after, Options(list, after), new[]
            {
                Request(renamed, "IN_New_Kerb_RM_02_02", RM, PlannerTagEditKind.Remove, PlannerTagTarget.For(row, filled))
            });
            Assert.Equal(0, removal.ProblemCount);
            Assert.Equal("IN_New_Kerb", removal.Renames.Single().NewName);
            Assert.Equal("out of the RM of 02_02_Planned: its RM tag taken out", removal.Renames.Single().Why);
            // Remove from 01_01's IN, leaving the removal 01_01 names for 02_01 where it is: there from the start.
            var hoarding = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_Hoarding_RM_02_01", IN, PlannerTagEditKind.Remove, strip) });
            Assert.Equal("Hoarding_RM_02_01", hoarding.Renames.Single().NewName);
            Assert.Equal(new[] { "RM 02_01_Reinstate" }, PlannerSchedule.BuildV2(Apply(scene.Snapshot, hoarding), options).PlacesOf("Hoarding_RM_02_01").ToArray());
        }

        [Fact]
        public void AnEditThePreviewWouldReadOtherwiseIsAProblemAndKeepsItsName()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            var list = SceneList();
            var options = Options(list, scene.Snapshot);
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Func<string, PlannerTaskType, PlannerTagEditKind, string, PlannerTagEditPlan> plan = (name, type, kind, id) =>
                PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, name, type, kind, Target(before, id)) });
            Action<PlannerTagEditPlan, string> problem = (p, why) =>
            {
                Assert.Empty(p.Renames);
                Assert.Equal(1, p.ProblemCount);
                Assert.Equal("Problem", p.Review.Single().Status);
                Assert.Contains(why, p.Review.Single().Why);
            };

            // A name on the old Ph/St tags: Clean up scene graph converts it first.
            problem(plan("Ph1_St02_IN_Old", IN, PlannerTagEditKind.Add, "01_02"), PlannerTagEdit.OldTagProblem);
            // An object under no layer's helper: link it first.
            problem(plan("IN_Loose", RM, PlannerTagEditKind.Add, "02_02"), PlannerTagEdit.NoLayerProblem);
            // A layer's helper, and a helper named as a layer on another's Max layer: their names are the layers'. An object named
            // as a phase group is a folder to the Planner.
            problem(plan("01_01_Strip_Out", IN, PlannerTagEditKind.Add, "01_01"), "it is the helper of the layer 01_01_Strip_Out");
            scene.Helper("01_05_Spare", scene.Find("01_01_Strip_Out"), "01_01_Strip_Out");
            scene.Object("_01_Kerb", scene.Find("01_01_Strip_Out"), "01_01_Strip_Out");
            var spare = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "01_05_Spare", IN, PlannerTagEditKind.Add, Target(before, "01_01")) });
            problem(spare, "it is the helper of the layer 01_05_Spare");
            var group = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "_01_Kerb", IN, PlannerTagEditKind.Add, Target(before, "01_01")) });
            problem(group, "it is named as a phase group (_<n>_<Name>), which the Planner reads as a folder, not an object");
            // The place at the end holds the other tag, and only it can name another layer: refused, with the reason.
            var hoarding = plan("IN_Hoarding_RM_02_01", IN, PlannerTagEditKind.Add, "02_02");
            problem(hoarding, "only a tag at the end can name another layer");
            // Remove from a list the object is not in (the scene changed since the list was filled).
            problem(plan("IN_Kerb_A", RM, PlannerTagEditKind.Remove, "02_02"), "it is not in the RM of 02_02_Planned: the Planner reads it as IN 01_02_Kerbs");
            // Read back otherwise: the name it would have given is shown.
            scene.Object("IN_RM_Kerb", scene.Find("01_02_Kerbs"), "01_02_Kerbs");
            var doubled = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_RM_Kerb", IN, PlannerTagEditKind.Remove, Target(before, "01_02")) });
            problem(doubled, "would read as removed while its own layer runs");
            Assert.Equal("RM_Kerb", doubled.Review.Single().To);
            // A name another node has.
            scene.Object("IN_Plain_Box", null, "0");
            problem(plan("Plain_Box", IN, PlannerTagEditKind.Add, "01_01"), "which another node is called already");
            // A node the export leaves out.
            scene.Find("RM_Old_Fence").Hidden = true;
            problem(plan("RM_Old_Fence", IN, PlannerTagEditKind.Add, "01_01"), "the export leaves it out (" + PlannerScheduleModel.HiddenReason + ")");
        }

        [Fact]
        public void ObjectsTheRenamesMakeThePlannerReadAnewAreListed()
        {
            // An untagged Wall holding a tagged IN_Wall_1 of its own: once Wall is IN_Wall, the Planner folds IN_Wall_1 into it
            // as a split piece (by name and ancestor), so it is listed as read anew, with why.
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone = scene.Unit("_01_Zone", "Work_Phasing", root);
            var strip = scene.Unit("01_01_Strip_Out", "_01_Zone", zone);
            var wall = scene.Object("Wall", strip, "01_01_Strip_Out");
            scene.Object("IN_Wall_1", wall, "01_01_Strip_Out");
            var options = Options(null, scene.Snapshot);
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Assert.Equal(new[] { "IN 01_01_Strip_Out" }, before.PlacesOf("IN_Wall_1").ToArray());
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Wall", IN, PlannerTagEditKind.Add, Target(before, "01_01")) });
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal("IN_Wall", plan.Renames.Single().NewName);
            var also = plan.Review.Single(r => r.Status == "Also");
            Assert.Equal("IN_Wall_1", also.Name);
            Assert.Equal("IN 01_01_Strip_Out", also.From);
            Assert.Equal("part of IN_Wall", also.To);
            Assert.Equal("not renamed, but the Planner reads it as a split piece of IN_Wall once that is renamed", also.Why);
            Assert.Contains(plan.Notes, n => n.StartsWith("Objects marked Also keep their names", StringComparison.Ordinal));
        }

        [Fact]
        public void TheReviewNotesWhatTheNewNamesBring()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            scene.Object("Kerb-Stone", scene.Find("01_01_Strip_Out"), "01_01_Strip_Out");
            scene.Object("RM_New_Kerb", scene.Find("01_02_Kerbs"), "01_02_Kerbs");
            var list = SceneList();
            var options = Options(list, scene.Snapshot);
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);

            // Removed in 01_01 (1 to 2 Sep), installed in 02_01 (10 Sep): the preview runs them the other way round from the name.
            var early = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_New_Kerb", RM, PlannerTagEditKind.Add, Target(before, "01_01")) });
            Assert.Equal("IN_New_Kerb_RM_01_01", early.Renames.Single().NewName);
            Assert.Contains("After the renames, the preview runs the two events of IN_New_Kerb_RM_01_01 the other way round from its name (the layers' dates put them in the other order): "
                + "check the layer it names.", early.Notes);
            // Its own name New_Kerb is RM_New_Kerb's too: the Planner cannot follow the tag change.
            Assert.Contains("IN_New_Kerb_RM_01_01 shares its own name New_Kerb with RM_New_Kerb: the Planner cannot follow a tag change on objects sharing an own name, "
                + "so it reads this one's earlier activities as gone and its work as new.", early.Notes);
            // A naming warning the new name brings: a hyphen, which Unreal turns into an underscore.
            var hyphen = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Kerb-Stone", IN, PlannerTagEditKind.Add, Target(before, "01_01")) });
            Assert.Contains("After the renames, IN_Kerb-Stone: Unreal turns hyphens into underscores.", hyphen.Notes);
            // Given work, a layer the list has not dated is parked: said.
            var parked = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Plain_Box", RM, PlannerTagEditKind.Add, Target(before, "01_02")) });
            Assert.Equal("Plain_Box_RM_01_02", parked.Renames.Single().NewName);
            Assert.Contains(parked.Notes, n => n.StartsWith("01_02_Kerbs has no dates in the Planner's list, so the preview parks its work on ", StringComparison.Ordinal));
            // With no list imported, the note does not speak of a list.
            var noList = Options(null, scene.Snapshot);
            var unlisted = PlannerTagEdit.Plan(scene.Snapshot, noList, new[] { Request(scene, "Plain_Box", RM, PlannerTagEditKind.Add, Target(PlannerSchedule.BuildV2(scene.Snapshot, noList), "01_02")) });
            Assert.Contains(unlisted.Notes, n => n.StartsWith("01_02_Kerbs has no dates here, as no layer list is imported, so the preview parks its work on ", StringComparison.Ordinal));
            Assert.DoesNotContain(unlisted.Notes, n => n.Contains("Planner's list"));
            // An object left with no tag is no longer scheduled: said in its row and in a note.
            var untagged = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_Kerb_A", IN, PlannerTagEditKind.Remove, Target(before, "01_02")) });
            Assert.Equal("Kerb_A", untagged.Renames.Single().NewName);
            Assert.EndsWith("; no tag is left, so the Planner no longer schedules it (it shows for the whole programme)", untagged.Renames.Single().Why);
            Assert.Contains("1 object is left with no IN or RM tag, so the Planner no longer schedules it and it shows for the whole programme: Kerb_A.", untagged.Notes);
        }

        // A layer with a closed group, a hierarchy and an object split into pieces, for the selection tests: no list, so 02_01
        // is parked on 1 Sep and 02_02 on 2 Sep.
        private static TestScene SelectionScene()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone = scene.Unit("_02_Zone", "Work_Phasing", root);
            var kerbs = scene.Unit("02_01_Kerbs", "_02_Zone", zone);
            scene.Object("IN_KerbA", kerbs, "02_01_Kerbs");
            var strip = scene.Unit("02_02_Strip", "_02_Zone", zone);
            scene.Object("RM_Old", strip, "02_02_Strip");
            // A closed group: its head (a helper) and its members.
            var head = scene.Helper("Scaffold_Group", kerbs, "02_01_Kerbs");
            head.IsGroupHead = true;
            foreach (var name in new[] { "Box001", "Box002" })
            {
                scene.Object(name, head, "02_01_Kerbs").IsGroupMember = true;
            }
            // An object split into pieces (Name_N under Name, read as part of Name).
            var wall = scene.Object("IN_Wall", kerbs, "02_01_Kerbs");
            scene.Object("IN_Wall_1", wall, "02_01_Kerbs");
            scene.Object("IN_Wall_2", wall, "02_01_Kerbs");
            // A hierarchy that is not a group: a tagged crane with an untagged hook and a tagged jib under it.
            var crane = scene.Object("IN_Crane", kerbs, "02_01_Kerbs");
            scene.Object("Hook", crane, "02_01_Kerbs");
            scene.Object("IN_Jib", crane, "02_01_Kerbs");
            return scene;
        }

        private static PlannerTagEditPlan AddTo(TestScene scene, PlannerTaskType type, string id, params string[] names)
        {
            var options = Options(null, scene.Snapshot);
            var target = Target(PlannerSchedule.BuildV2(scene.Snapshot, options), id);
            return PlannerTagEdit.Plan(scene.Snapshot, options, names.Select(n => Request(scene, n, type, PlannerTagEditKind.Add, target)).ToList());
        }

        [Fact]
        public void AClosedGroupSelectedWholeIsTaggedOnItsHead()
        {
            // Clicking a closed group in 3ds Max selects its head and every member: the head takes the tag, the untagged
            // members are part of it (the Planner shows and hides them with it) and are left out, with a note.
            var scene = SelectionScene();
            var plan = AddTo(scene, RM, "02_02", "Scaffold_Group", "Box001", "Box002");
            Assert.Equal(0, plan.ProblemCount);
            var rename = plan.Renames.Single();
            Assert.Equal("Scaffold_Group", rename.OldName);
            Assert.Equal("Scaffold_Group_RM_02_02", rename.NewName);
            Assert.Contains(plan.Notes, n => n.Contains("2 objects under Scaffold_Group (Box001, Box002) left out"));
            var after = PlannerSchedule.BuildV2(Apply(scene.Snapshot, plan), Options(null, scene.Snapshot));
            Assert.Equal(new[] { "RM 02_02_Strip" }, after.PlacesOf("Scaffold_Group_RM_02_02").ToArray());
            Assert.Empty(after.PlacesOfNode(scene.Find("Box001").Key));
            Assert.DoesNotContain(scene.Find("Box001").Key, after.HiddenAt(after.Midnight(new DateTime(2026, 9, 2))));
            Assert.Contains(scene.Find("Box001").Key, after.HiddenAt(after.Midnight(new DateTime(2026, 9, 3))));

            // A tagged head: the same, its removal added at the end.
            scene.Find("Scaffold_Group").Name = "IN_Scaffold";
            var tagged = AddTo(scene, RM, "02_02", "IN_Scaffold", "Box001", "Box002");
            Assert.Equal("IN_Scaffold_RM_02_02", tagged.Renames.Single().NewName);

            // A member with a tag of its own, selected with its group: a Problem, as only the modeller knows which is meant.
            scene.Find("Box002").Name = "IN_Panel";
            var mixed = AddTo(scene, RM, "02_02", "IN_Scaffold", "Box001", "IN_Panel");
            Assert.Equal(1, mixed.ProblemCount);
            Assert.Contains("inside the closed group IN_Scaffold", mixed.Review.Single(r => r.Status == "Problem").Why);
            Assert.Contains("Group > Open", mixed.Review.Single(r => r.Status == "Problem").Why);
            Assert.Equal("IN_Scaffold_RM_02_02", mixed.Renames.Single().NewName);

            // Chosen alone (an open group), a member is edited like any object.
            var alone = AddTo(scene, RM, "02_02", "IN_Panel");
            Assert.Equal("IN_Panel_RM_02_02", alone.Renames.Single().NewName);
        }

        [Fact]
        public void AHierarchySelectedWholeKeepsItsUntaggedPartsAndItsTaggedObjects()
        {
            var scene = SelectionScene();
            var plan = AddTo(scene, RM, "02_02", "IN_Crane", "Hook", "IN_Jib");
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "IN_Crane_RM_02_02", "IN_Jib_RM_02_02" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.Contains(plan.Notes, n => n.Contains("1 object under IN_Crane (Hook) left out"));
            // A layer's helper chosen with its objects is no object, and what is under it is not left out for it.
            var withHelper = AddTo(scene, RM, "02_02", "02_01_Kerbs", "IN_KerbA");
            Assert.Contains("it is the helper of the layer 02_01_Kerbs", withHelper.Review.Single(r => r.Status == "Problem").Why);
            Assert.Equal("IN_KerbA_RM_02_02", withHelper.Renames.Single().NewName);
        }

        [Fact]
        public void AddToInAndRmRefuseEveryPhasingRootsHelper()
        {
            // One root: its helper is no object.
            var one = SelectionScene();
            var refused = AddTo(one, IN, "02_01", "Work_Phasing");
            Assert.Empty(refused.Renames);
            Assert.Equal("it is the helper of the phasing root Work_Phasing: its name is the root's, not an object's", refused.Review.Single(r => r.Status == "Problem").Why);

            // Two roots: each root's helper is refused, and a tag may name a layer in either root (R3, R19).
            var scene = new TestScene();
            scene.Layer("0");
            var south = scene.Unit("Work_Phasing_South", null, null);
            var north = scene.Unit("Work_Phasing_North", null, null);
            var enabling = scene.Unit("_01_North_Enabling", "Work_Phasing_North", north);
            var drainage = scene.Unit("01_01_Drainage", "_01_North_Enabling", enabling);
            scene.Object("Pipe", drainage, "01_01_Drainage");
            var area = scene.Unit("_07_South_Enabling", "Work_Phasing_South", south);
            var hoarding = scene.Unit("07_01_Hoarding", "_07_South_Enabling", area);
            scene.Object("Panel", hoarding, "07_01_Hoarding");

            foreach (var root in new[] { "Work_Phasing_South", "Work_Phasing_North" })
            {
                foreach (var type in new[] { IN, RM })
                {
                    var plan = AddTo(scene, type, "07_01", root);
                    Assert.Empty(plan.Renames);
                    Assert.Equal(1, plan.ProblemCount);
                    Assert.Equal("it is the helper of the phasing root " + root + ": its name is the root's, not an object's", plan.Review.Single().Why);
                }
            }
            // The second root chosen with its object: the root is refused, and its object is not taken as part of it.
            var withObject = AddTo(scene, IN, "07_01", "Work_Phasing_South", "Panel");
            Assert.Equal(1, withObject.ProblemCount);
            Assert.Equal("IN_Panel", withObject.Renames.Single().NewName);
            // South's object removed in North's layer, North's installed in South's.
            Assert.Equal("Panel_RM_01_01", AddTo(scene, RM, "01_01", "Panel").Renames.Single().NewName);
            Assert.Equal("Pipe_IN_07_01", AddTo(scene, IN, "07_01", "Pipe").Renames.Single().NewName);
        }

        [Fact]
        public void SplitPiecesAreRenamedWithTheirObject()
        {
            var scene = SelectionScene();
            var plan = AddTo(scene, RM, "02_02", "IN_Wall");
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "IN_Wall_RM_02_02", "IN_Wall_RM_02_02_1", "IN_Wall_RM_02_02_2" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.All(plan.Renames.Skip(1), r => Assert.StartsWith("a split piece of IN_Wall: renamed with it", r.Why));
            Assert.DoesNotContain(plan.Review, r => r.Status == "Also");
            // Read alone, each piece now names a layer (02_02_1, 02_02_2), which the naming check warns about; it folds all the same.
            Assert.Contains(plan.Notes, n => n.StartsWith("After the renames, IN_Wall_RM_02_02: A piece of this object has a name that, read alone, names a layer.", StringComparison.Ordinal));
            var after = PlannerSchedule.BuildV2(Apply(scene.Snapshot, plan), Options(null, scene.Snapshot));
            var wall = after.ObjectOf(scene.Find("IN_Wall").Key);
            Assert.Equal(wall, after.PieceOf(scene.Find("IN_Wall_1").Key));
            Assert.Equal(new[] { "IN 02_01_Kerbs", "RM 02_02_Strip" }, after.PlacesOf(wall.Name).ToArray());
            // The pieces go and come with it.
            var removed = after.HiddenAt(after.Midnight(new DateTime(2026, 9, 3)));
            Assert.Contains(scene.Find("IN_Wall_1").Key, removed);
            Assert.Contains(scene.Find("IN_Wall_2").Key, removed);

            // Chosen with its object, a piece is part of it; chosen alone, it is a Problem.
            var together = AddTo(scene, RM, "02_02", "IN_Wall", "IN_Wall_1", "IN_Wall_2");
            Assert.Equal(0, together.ProblemCount);
            Assert.Equal(3, together.Renames.Count);
            var alone = AddTo(scene, RM, "02_02", "IN_Wall_1");
            Assert.Contains("it is a split piece of IN_Wall", alone.Review.Single().Why);
            Assert.Empty(alone.Renames);

            // A piece that cannot follow (its new name is taken) keeps its object from being renamed.
            scene.Object("IN_Wall_RM_02_02_2", null, "0");
            var blocked = AddTo(scene, RM, "02_02", "IN_Wall");
            Assert.Empty(blocked.Renames);
            Assert.Contains("its split piece IN_Wall_2 would be called IN_Wall_RM_02_02_2, which another node is called already", blocked.Review.Single().Why);
        }

        [Fact]
        public void SplitPiecesGoWithAnObjectThatLosesOrGainsItsOnlyTag()
        {
            // The Planner folds a piece into its object by name and ancestor alone, whether the object is tagged or not:
            // taking out an object's only tag takes its pieces' tags out with it, and an untagged object given a tag gives
            // it to its pieces.
            var scene = SelectionScene();
            var kerb = scene.Find("IN_KerbA");
            var piece = scene.Object("IN_KerbA_2", kerb, "02_01_Kerbs");
            var options = Options(null, scene.Snapshot);
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Assert.Equal(kerb.Key, before.PieceOf(piece.Key).Key);
            var removal = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_KerbA", IN, PlannerTagEditKind.Remove, Target(before, "02_01")) });
            Assert.Equal(0, removal.ProblemCount);
            Assert.Equal(new[] { "KerbA", "KerbA_2" }, removal.Renames.Select(r => r.NewName).ToArray());
            Assert.DoesNotContain(removal.Review, r => r.Status == "Also");
            var untagged = new TestScene(Apply(scene.Snapshot, removal));
            var unscheduled = PlannerSchedule.BuildV2(untagged.Snapshot, options);
            Assert.Null(unscheduled.ObjectOf(kerb.Key));
            Assert.Null(unscheduled.PieceOf(piece.Key));
            Assert.Equal(kerb.Key, unscheduled.MergedOwnerOf(piece.Key).Key);
            Assert.DoesNotContain(unscheduled.NotScheduled, n => n.Key == piece.Key);

            // Back into the IN: the piece goes with it, and is read as part of it again.
            var addition = PlannerTagEdit.Plan(untagged.Snapshot, options, new[] { Request(untagged, "KerbA", IN, PlannerTagEditKind.Add, Target(unscheduled, "02_01")) });
            Assert.Equal(0, addition.ProblemCount);
            Assert.Equal(new[] { "IN_KerbA", "IN_KerbA_2" }, addition.Renames.Select(r => r.NewName).ToArray());
            Assert.StartsWith("a split piece of KerbA: renamed with it", addition.Renames[1].Why);
            var again = PlannerSchedule.BuildV2(Apply(untagged.Snapshot, addition), options);
            Assert.Equal(kerb.Key, again.PieceOf(piece.Key).Key);
            Assert.Equal(new[] { "IN 02_01_Kerbs" }, again.PlacesOfNode(kerb.Key).ToArray());

            // A piece of an untagged object chosen alone is a Problem, as for a tagged one: choose its object.
            var alone = PlannerTagEdit.Plan(untagged.Snapshot, options, new[] { Request(untagged, "KerbA_2", IN, PlannerTagEditKind.Add, Target(unscheduled, "02_01")) });
            Assert.Contains("it is a split piece of KerbA", alone.Review.Single().Why);
            Assert.Empty(alone.Renames);
            var removePiece = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_KerbA_2", IN, PlannerTagEditKind.Remove, Target(before, "02_01")) });
            Assert.Contains("choose IN_KerbA instead", removePiece.Review.Single().Why);
        }

        [Fact]
        public void EditsTargetTheLayerAsThePreviewReadsItsHelper()
        {
            // A scene built by hand: the Max layer renamed, its helper (the name the GLB carries) not. The row is the Max
            // layer's; the edit is the layer the preview reads.
            const string layer = "02_01_Kerbing";
            const string helper = "02_01_Kerbs";
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            scene.Layer(layer, "Work_Phasing");
            var kerbs = scene.Helper(helper, root, layer);
            scene.Object("IN_KerbA", kerbs, layer);
            scene.Object("Box001", kerbs, layer);
            var options = Options(null, scene.Snapshot);
            var schedule = PlannerSchedule.BuildV2(scene.Snapshot, options);
            var model = new PlannerSceneModel(scene.Snapshot);
            var row = PlannerScheduleRows.Tree(null, null, model, null, schedule, PlannerNamingKind.New).Single(r => r.LayerName == layer);
            Assert.Equal(helper, row.Layer.Label);
            Assert.True(row.Editable);
            var target = PlannerTagTarget.For(row, schedule);
            Assert.Equal(helper, target.LayerName);
            Assert.Equal(helper, target.Label);
            Assert.Equal("02_01", target.IdText);
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "Box001", IN, PlannerTagEditKind.Add, target) });
            Assert.Equal(0, plan.ProblemCount);
            var after = PlannerSchedule.BuildV2(Apply(scene.Snapshot, plan), options);
            Assert.Equal(new[] { "IN_Box001", "IN_KerbA" }, after.ColumnOf(after.Layers.Single(l => l.Label == helper), IN).SelectMany(a => a.Members).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            var remove = PlannerTagEdit.Plan(scene.Snapshot, options, new[] { Request(scene, "IN_KerbA", IN, PlannerTagEditKind.Remove, target) });
            Assert.Equal(0, remove.ProblemCount);
            Assert.Equal("KerbA", remove.Renames.Single().NewName);
            // Before Fill, a row of the scene's layers still names its layer by its Max layer's ID.
            var unfilled = PlannerScheduleRows.Tree(null, null, model, null, null, PlannerNamingKind.New).Single(r => r.LayerName == layer);
            Assert.True(unfilled.Editable);
            Assert.Equal("02_01", PlannerTagTarget.For(unfilled, null).IdText);
        }

        [Fact]
        public void ANodeTheExportRenamesIsEditedByItsOwnNode()
        {
            // Two nodes called IN_New_Kerb: the one in 02_01, written after 01_01's, reaches the Planner as "IN_New_Kerb (1)".
            // Editing it is about that node, by its handle, not about the other of its name.
            var scene = PlannerScheduleV2Tests.Scene();
            var list = SceneList();
            scene.Object("IN_New_Kerb", scene.Find("01_01_Strip_Out"), "01_01_Strip_Out");
            var options = Options(list, scene.Snapshot);
            var second = scene.Snapshot.Nodes.Single(n => n.Name == "IN_New_Kerb" && n.ParentKey == scene.Find("02_01_Reinstate").Key);
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Assert.Equal("IN_New_Kerb (1)", before.NameOf(second.Key));
            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, new[]
            {
                new PlannerTagEditRequest { Key = second.Key, Type = RM, Kind = PlannerTagEditKind.Add, Target = Target(before, "02_02") }
            });
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal("IN_New_Kerb_RM_02_02", plan.Renames.Single().NewName);
            var after = PlannerSchedule.BuildV2(Apply(scene.Snapshot, plan), options);
            Assert.Equal(new[] { "IN 02_01_Reinstate", "RM 02_02_Planned" }, after.PlacesOfNode(second.Key).ToArray());
            Assert.Equal(new[] { "IN 01_01_Strip_Out" }, after.PlacesOf("IN_New_Kerb").ToArray());
        }

        [Fact]
        public void TheBatchRenamesByHandleInOneUndoRecord()
        {
            var plan = new PlannerTagEditPlan();
            plan.Renames.Add(new PlannerTagRename { Key = 12, OldName = "Kerb \"A\"", NewName = "IN_Kerb \"A\"" });
            plan.Renames.Add(new PlannerTagRename { Key = 13, OldName = "Bad\u0001", NewName = "IN_Bad" });
            List<string> refused;
            List<PlannerTagRename> sent;
            var script = PlannerTagEdit.Script(plan, out refused, out sent);
            Assert.Single(sent);
            Assert.Single(refused);
            Assert.Contains("undo \"Planner IN / RM\" on", script);
            Assert.Contains("local bHandles = #(12)", script);
            Assert.Contains("\"Kerb \\\"A\\\"\"", script);
            Assert.DoesNotContain("\r\r", script);
            MaxScriptText.AssertBalanced(script);
            int renamed;
            List<string> failures;
            bool completed;
            string aborted;
            PlannerTagEdit.ReadResult(sent, "FAIL\trename\t1\tchanged\nDONE\t0\n" + PlannerTagEdit.EndMarker + "\n", out renamed, out failures, out completed, out aborted);
            Assert.True(completed);
            Assert.Equal(0, renamed);
            Assert.Equal("Kerb \"A\" has been renamed since the list was made, so it was not renamed", failures.Single());
        }

        // ---------------------------------------------------------------------------------------------------------
        // v30 2, cleaned with its list (kept out of the repo: see PlannerCleanUpAcceptanceTests)

        [EnvironmentFact("PLANNER_CLEANUP_V30_TREE", "PLANNER_CLEANUP_V30_CSV", "PLANNER_CLEANUP_V30_LIST")]
        public void V30TheBollardsRemovalRetaggedTo06_05_2MovesInThePreview()
        {
            // v30 2 cleaned with its list, as the rename list answers its questions; then the cycling bollards of 02_02_9,
            // removed in 04_02_3 (25 Jan 2027), retagged for 06_05_2 (5 Feb 2027), the day the corrected schedule has (v30
            // issue rows 24 and 25).
            var tree = PlannerCleanUpAcceptanceTests.LoadTree(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_TREE"));
            var expected = PlannerCleanUpAcceptanceTests.Renames.Load(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_CSV"));
            var list = PlannerLayerList.Parse(File.ReadAllBytes(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_LIST")));
            var cleanUp = PlannerCleanUp.Plan(tree, PhasingNameFixer.DefaultWordFixes(), list);
            var scene = new TestScene(cleanUp.Resolve(PlannerCleanUpAcceptanceTests.AnswersFrom(cleanUp, expected)).ApplyTo(tree));
            var options = new PlannerScheduleOptions
            {
                Zone = TimeZoneInfo.Utc,
                Today = new DateTime(2026, 10, 18),
                ProjectStart = list.ProjectStart,
                Folders = PlannerScheduleRows.PlannerFolders(list, PlannerLayerPlanner.Plan(list, new PlannerSceneModel(scene.Snapshot)))
            };
            var before = PlannerSchedule.BuildV2(scene.Snapshot, options);
            Assert.Equal(PlannerNamingKind.New, PlannerSchedule.NamingOfScene(before.Model).Naming);
            var bollards = new[] { "IN_Cycling_Bollards_RM_04_02_3", "IN_Cycling_Bollards_02_RM_04_02_3" };
            foreach (var name in bollards)
            {
                Assert.Equal(new[] { "IN 02_02_9_TM_Removal", "RM 04_02_3_Binder_Course" }, before.PlacesOf(name).ToArray());
            }
            var target = Target(before, "06_05_2");
            Assert.Equal("06_05_2_Site_Entrance-Hoarding_Install", target.Label);

            var plan = PlannerTagEdit.Plan(scene.Snapshot, options, bollards.Select(n => Request(scene, n, RM, PlannerTagEditKind.Add, target)).ToList());
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(new[] { "IN_Cycling_Bollards_RM_06_05_2", "IN_Cycling_Bollards_02_RM_06_05_2" }, plan.Renames.Select(r => r.NewName).ToArray());
            Assert.DoesNotContain(plan.Review, r => r.Status != "Change");
            Assert.Empty(plan.Notes);

            // The preview moves their removal from 25 Jan to 5 Feb 2027: in view on 1 Feb, gone on 6 Feb.
            var after = PlannerSchedule.BuildV2(Apply(scene.Snapshot, plan), options);
            var keys = bollards.Select(n => scene.Find(n).Key).ToList();
            foreach (var name in plan.Renames.Select(r => r.NewName))
            {
                Assert.Equal(new[] { "IN 02_02_9_TM_Removal", "RM 06_05_2_Site_Entrance-Hoarding_Install" }, after.PlacesOf(name).ToArray());
            }
            Assert.True(keys.All(k => before.HiddenAt(before.Midnight(new DateTime(2027, 2, 1))).Contains(k)));
            Assert.True(keys.All(k => !after.HiddenAt(after.Midnight(new DateTime(2027, 2, 1))).Contains(k)));
            Assert.True(keys.All(k => after.HiddenAt(after.Midnight(new DateTime(2027, 2, 6))).Contains(k)));
            // Every other object reads as before, and the scene still reads clean.
            foreach (var name in before.Objects.Keys.Where(n => !bollards.Contains(n)))
            {
                Assert.Equal(before.PlacesOf(name), after.PlacesOf(name));
            }
            Assert.Equal(166, after.Objects.Count);
            Assert.DoesNotContain(after.V2.NamingIssues, i => PlannerNaming.NamingIssueV2[i.Code].Severity == NamingSeverity.Error);
            // The rows: 06_05_2's RM holds them, 04_02_3's no longer does.
            var model = new PlannerSceneModel(Apply(scene.Snapshot, plan));
            var rows = PlannerScheduleRows.Tree(list, PlannerLayerPlanner.Plan(list, model), model, null, after, PlannerNamingKind.New);
            Assert.Equal(plan.Renames.Select(r => r.NewName).OrderBy(n => n, StringComparer.Ordinal),
                rows.Single(r => r.Name == target.Label).Rm.SelectMany(a => a.Members).Where(m => m.StartsWith("IN_Cycling", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal));
            Assert.DoesNotContain(rows.Single(r => r.Name == "04_02_3_Binder_Course").Rm.SelectMany(a => a.Members), m => m.StartsWith("IN_Cycling", StringComparison.Ordinal));
        }
    }
}
