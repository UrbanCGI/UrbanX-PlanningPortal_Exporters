using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// The export naming check on the new names (<see cref="PlannerNamingValidator.ValidateV2"/>, spec 5.10): every rule on
    /// small scenes, then parity with the Planner's own code. What the Planner lists for a model (its naming issues, old
    /// names, layer issues, shared IDs, unfiled objects) must be what the check reads, and the check must report each of
    /// them, with the Planner's code and severity, and nothing more under those codes:
    ///   PlannerScheduleScenesV2.json / .utc.json   the 41 synthetic Generate scenes and the Planner's results (in the repo)
    ///   PLANNER_NAMING_V2_CHECK                    real models (kept out of the repo): the converted v30 2 GLB with no list,
    ///                                              its list and an edited list; the same GLB with a modeller's slips in it
    ///                                              (every rule), with no list and with the edited list; v30 and HS2 v3_06u
    ///                                              converted (the parity fixtures' real scenes), each run through the
    ///                                              Planner's committed code (6509458) by a harness
    ///   PLANNER_NAMING_V2_CHECK_REPORT             optional: a file the real models' reports are written to
    /// </summary>
    public class PlannerNamingValidatorV2Tests
    {
        private readonly ITestOutputHelper output;

        public PlannerNamingValidatorV2Tests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static string Here([CallerFilePath] string path = null)
        {
            return Path.GetDirectoryName(path);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scenes

        /// <summary>
        /// A new-name scene with every Max layer paired with its helper: a phasing root, two phase groups, three layers, and
        /// clean objects in each. Tests add their slips to it.
        /// </summary>
        private sealed class V2Scene
        {
            public readonly TestScene Scene = new TestScene();
            public readonly PlannerSceneNode Root;
            public readonly PlannerSceneNode Zone1;
            public readonly PlannerSceneNode Zone2;
            public readonly PlannerSceneNode Strip;
            public readonly PlannerSceneNode Kerbs;
            public readonly PlannerSceneNode Reinstate;

            public V2Scene()
            {
                Scene.Layer("0");
                Root = Scene.Unit("Work_Phasing", null, null);
                Zone1 = Scene.Unit("_01_Zone", "Work_Phasing", Root);
                Strip = Scene.Unit("01_01_Strip_Out", "_01_Zone", Zone1);
                Kerbs = Scene.Unit("01_02_Kerbs", "_01_Zone", Zone1);
                Zone2 = Scene.Unit("_02_Zone", "Work_Phasing", Root);
                Reinstate = Scene.Unit("02_01_Reinstate", "_02_Zone", Zone2);
                Object("RM_Old_Fence", Strip);
                Object("IN_Hoarding_RM_02_01", Strip);
                Object("IN_Kerb_A", Kerbs);
                Object("RM_Bench_IN", Kerbs);
                Object("IN_New_Kerb", Reinstate);
            }

            public PlannerSceneNode Object(string name, PlannerSceneNode layer)
            {
                return Scene.Object(name, layer, layer != null ? layer.Name : "0");
            }

            public NamingReport Check(params string[] list)
            {
                return PlannerNamingValidator.ValidateV2(new PlannerScheduleModel(Scene.Snapshot), list.Length > 0 ? list : null);
            }
        }

        private static NamingIssue Single(NamingReport report, string code, string name)
        {
            var found = report.Issues.Where(i => i.Code == code && i.Names.Contains(name)).ToList();
            Assert.True(found.Count == 1, "expected one " + code + " issue for " + name + ", found " + found.Count + ":\n" + string.Join("\n", report.Issues.Select(i => i.ToString()).ToArray()));
            return found[0];
        }

        private static NamingIssue About(NamingReport report, string name)
        {
            var found = report.Issues.Where(i => i.Names.Contains(name) || i.Subject == name).ToList();
            Assert.True(found.Count == 1, "expected one issue about " + name + ", found " + found.Count + ":\n" + string.Join("\n", report.Issues.Select(i => i.ToString()).ToArray()));
            return found[0];
        }

        private static void Clean(NamingReport report)
        {
            Assert.True(report.Errors == 0 && report.Warnings == 0, string.Join("\n", report.Issues.Select(i => i.ToString()).ToArray()));
        }

        // ---------------------------------------------------------------------------------------------------------
        // The rules

        [Fact]
        public void ACleanSceneReadsCleanAndTheLogSaysWhatThePlannerReads()
        {
            var v2 = new V2Scene();
            var report = v2.Check();
            Assert.Equal(PlannerNamingKind.New, report.Naming);
            Assert.Empty(report.Issues);
            Assert.Equal("Planner naming check (new names): 5 tagged objects in 3 layers; 0 error(s), 0 warning(s), 0 note(s). No naming problems found.", report.Summary());
            Assert.Equal("What the Planner reads: 3 layers by ID, 5 objects scheduled with 4 installs and 3 removals, 1 layer named by references to another layer.", report.Reads());
            Assert.Null(report.CleanUpPointer());
            // The phasing counts, as the export log has always shown them.
            Assert.Equal("Phasing counts: 5 layers in the Max layer Work_Phasing, 5 helper nodes under Work_Phasing.", report.Counts.Describe(false));
        }

        [Fact]
        public void IllegalNamesAreErrorsThatSayHowThePlannerReadsThem()
        {
            var v2 = new V2Scene();
            v2.Object("IN_Kerb_IN", v2.Kerbs);
            v2.Object("RM_RM_Marwood_Barrier3", v2.Kerbs);
            v2.Object("X_RM_1", v2.Kerbs);
            v2.Object("IN_Gully_RM001", v2.Kerbs);
            v2.Object("IN_Pipe_RM_06_05_2_3", v2.Kerbs);
            v2.Object("Gate_IN_02_01_RM", v2.Kerbs);
            v2.Object("01_02_IN_Kerb", v2.Kerbs);
            v2.Object("IN___", v2.Kerbs);
            var report = v2.Check();
            Assert.Equal("'IN_Kerb_IN': The same type at both ends: only the leading tag is scheduled. The Planner reads it as 'Kerb', installed while its layer runs.",
                Single(report, "same-type", "IN_Kerb_IN").Message);
            Assert.Equal("'RM_RM_Marwood_Barrier3': The name after the leading tag starts with IN or RM again. The Planner reads it as 'RM_Marwood_Barrier3', removed while its layer runs.",
                Single(report, "doubled-type", "RM_RM_Marwood_Barrier3").Message);
            Assert.Equal("'X_RM_1': IN or RM is followed by numbers that are not a layer ID, so it is not read as a tag. The Planner reads no tag in it, so it shows for the whole programme.",
                Single(report, "not-an-id", "X_RM_1").Message);
            Assert.Equal(NamingSeverity.Error, Single(report, "not-an-id", "IN_Gully_RM001").Severity);
            Assert.Equal(NamingSeverity.Error, Single(report, "not-an-id", "IN_Pipe_RM_06_05_2_3").Severity);
            Assert.Equal("'Gate_IN_02_01_RM': Two tags at the end: only the last one is read. The Planner reads it as 'Gate_IN_02_01', removed at the end of its layer.",
                Single(report, "two-trailing", "Gate_IN_02_01_RM").Message);
            Assert.Equal(NamingSeverity.Error, Single(report, "id-then-tag", "01_02_IN_Kerb").Severity);
            Assert.Equal("'IN___': Nothing is left between the tags, so it is not an object. The Planner reads no tag in it, so it shows for the whole programme.",
                Single(report, "no-own-name", "IN___").Message);
            // Errors lead the log.
            Assert.True(report.Issues.TakeWhile(i => i.Severity == NamingSeverity.Error).Count() == report.Errors);
        }

        [Fact]
        public void OldTagsInANewNameSceneAreOneErrorPointingToCleanUp()
        {
            var v2 = new V2Scene();
            v2.Object("Ph1_St02_IN_Old_Kerb", v2.Kerbs);
            v2.Object("IN_Kerb_Ph6_St05_RM", v2.Kerbs);
            var report = v2.Check();
            Assert.Equal(PlannerNamingKind.New, PlannerSchedule.NamingOfScene(new PlannerScheduleModel(v2.Scene.Snapshot)).Naming);
            var issue = Single(report, "old-tag", "Ph1_St02_IN_Old_Kerb");
            Assert.Equal(NamingSeverity.Error, issue.Severity);
            Assert.Equal(new[] { "Ph1_St02_IN_Old_Kerb", "IN_Kerb_Ph6_St05_RM" }, issue.Names);
            Assert.Equal("2 objects still carry an old Ph/St tag, which the new names do not read, so the Planner will not schedule them: Ph1_St02_IN_Old_Kerb, IN_Kerb_Ph6_St05_RM. "
                + "Clean up scene graph\u2026 on the Schedule tab converts them.", issue.Message);
            Assert.Equal("2 names can be converted by Clean up scene graph\u2026 on the Schedule tab.", report.CleanUpPointer());
        }

        [Fact]
        public void AReferenceToALayerNobodyHasIsWarnedAndLayer9999IsWorkWithNoDateYet()
        {
            var v2 = new V2Scene();
            v2.Object("IN_RC_Hrd2_RM_01_23", v2.Kerbs);
            v2.Object("IN_Plates_RM_99_99", v2.Kerbs);
            v2.Object("Kerb_IN_03_01", v2.Kerbs);
            var report = v2.Check();
            Assert.Equal("'IN_RC_Hrd2_RM_01_23': its removal names layer 01_23, which the scene does not have (no layer list is imported to say whether the Planner has it): "
                + "the Planner parks it TBC the day after the last dated work (with none dated, the day after the last parked day). Add the layer, or correct the tag.",
                Single(report, "unknown-layer", "IN_RC_Hrd2_RM_01_23").Message);
            Assert.Equal("'IN_Plates_RM_99_99': its removal is for layer 99_99, work whose date is not known yet: the Planner parks it TBC the day after the last dated work "
                + "(with none dated, the day after the last parked day).", Single(report, "unknown-layer", "IN_Plates_RM_99_99").Message);
            Assert.Equal(NamingSeverity.Warning, Single(report, "unknown-layer", "IN_Plates_RM_99_99").Severity);
            Assert.Contains("its install names layer 03_01, which", Single(report, "unknown-layer", "Kerb_IN_03_01").Message);
            Assert.Contains("on the project start", Single(report, "unknown-layer", "Kerb_IN_03_01").Message);

            // With a list: a layer only the list has is noted (planned in the Planner), one neither has is still warned.
            report = v2.Check("_01_Zone", "01_01_Strip_Out", "01_02_Kerbs", "01_23_Planned_Removal", "_02_Zone", "02_01_Reinstate");
            Assert.DoesNotContain(report.Issues, i => i.Code == "unknown-layer" && i.Names.Contains("IN_RC_Hrd2_RM_01_23"));
            var planned = About(report, "IN_RC_Hrd2_RM_01_23");
            Assert.Equal(NamingSeverity.Note, planned.Severity);
            Assert.Equal("1 object names a layer only the imported layer list has (planned in the Planner, not built in 3ds Max yet), where the Planner files that work: 'IN_RC_Hrd2_RM_01_23' (01_23).",
                planned.Message);
            Assert.Contains("which neither the scene nor the imported layer list has", Single(report, "unknown-layer", "Kerb_IN_03_01").Message);
            Assert.Equal(NamingSeverity.Warning, Single(report, "unknown-layer", "IN_Plates_RM_99_99").Severity);
        }

        [Fact]
        public void DatesOrTbcInLayerAndHelperNamesAreWarnedAndCleanUpTakesThemOut()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone = scene.Unit("_01_Zone", "Work_Phasing", root);
            var dated = scene.Unit("01_01_Strip_Out_TBC_01-09-26", "_01_Zone", zone);
            scene.Object("IN_Fence", dated, dated.Name);
            var kerbs = scene.Unit("01_02_Kerbs", "_01_Zone", zone);
            scene.Object("IN_Kerb", kerbs, kerbs.Name);
            // A Max layer renamed apart from its helper (the helper sits on it): only the layer carries a date.
            scene.Layer("01_03_Gates_05-10-26", "_01_Zone");
            var gates = scene.Helper("01_03_Gates", zone, "01_03_Gates_05-10-26");
            scene.Object("IN_Gate", gates, "01_03_Gates_05-10-26");
            // Outside the phasing tree, a dated Max layer is never checked.
            scene.Layer("Old_Stuff_TBC_01-01-25");
            // A phase group's name is the Planner's folder name, kept as written: Clean up never takes a date or TBC out of it,
            // so it is not pointed to Clean up (with or without the Planner's list).
            var future = scene.Unit("_03_Future_Works_TBC", "Work_Phasing", root);
            var later = scene.Unit("_04_Works_17-10-26", "Work_Phasing", root);
            scene.Object("IN_Later", scene.Unit("04_01_Later", "_04_Works_17-10-26", later), "04_01_Later");
            var report = PlannerNamingValidator.ValidateV2(new PlannerScheduleModel(scene.Snapshot), null);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("_03_Future_Works_TBC") || i.Message.Contains("_04_Works_17-10-26"));
            Assert.DoesNotContain(future.Name, report.CleanUp);
            Assert.DoesNotContain(later.Name, report.CleanUp);
            var helpers = Single(report, "dates", "01_01_Strip_Out_TBC_01-09-26");
            Assert.Equal(NamingSeverity.Warning, helpers.Severity);
            Assert.Equal("1 helper name still carries dates or TBC, which the Planner does not read: it holds every date. Clean up scene graph\u2026 on the Schedule tab takes them out: "
                + "01_01_Strip_Out_TBC_01-09-26.", helpers.Message);
            var maxLayers = report.Issues.Single(i => i.Severity == NamingSeverity.Warning && i.Subject == "01_03_Gates_05-10-26");
            Assert.Null(maxLayers.Code);
            Assert.Equal(NamingSeverity.Warning, maxLayers.Severity);
            Assert.Equal("1 Max layer name in the phasing tree still carries dates or TBC: 01_03_Gates_05-10-26. Clean up scene graph\u2026 on the Schedule tab takes them out, with their helpers'.",
                maxLayers.Message);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("Old_Stuff"));
            Assert.Equal("2 names can be converted by Clean up scene graph\u2026 on the Schedule tab.", report.CleanUpPointer());
            // The layer and its helper named apart are noted, and not again as unpaired.
            var differ = report.Issues.Single(i => i.Message.Contains("named differently"));
            Assert.Equal(NamingSeverity.Note, differ.Severity);
            Assert.Equal("1 Max layer and its helper are named differently: '01_03_Gates_05-10-26' and '01_03_Gates'. On the new names a layer and its helper share one name, <ID>_<Name>, "
                + "and the Planner reads only the helper's.", differ.Message);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("do not pair up"));
        }

        [Fact]
        public void ASpaceInALayerNameAndNothingAfterTheIdAreWarned()
        {
            var v2 = new V2Scene();
            var spaced = v2.Scene.Unit("01_03_Gate Works", "_01_Zone", v2.Zone1);
            v2.Object("IN_Gate", spaced);
            v2.Scene.Unit("01_04", "_01_Zone", v2.Zone1);
            var report = v2.Check();
            Assert.Equal("Helper '01_03_Gate Works': A layer name has no spaces: use underscores.", Single(report, "space", "01_03_Gate Works").Message);
            Assert.Equal("Helper '01_04': Nothing follows the layer ID.", Single(report, "no-name", "01_04").Message);
            Assert.Equal(NamingSeverity.Warning, Single(report, "no-name", "01_04").Severity);
            // The empty 01_04 is a layer all the same: an empty folder.
            Assert.Equal(5, report.Layers);
            Assert.Equal(1, report.EmptyLayers);
            var empty = report.Issues.Single(i => i.Code == null && i.Names.Contains("01_04"));
            Assert.Equal(NamingSeverity.Note, empty.Severity);
            Assert.Equal("1 layer holds no object yet: the Planner makes a folder for it all the same (a new folder is flagged TBC): 01_04.", empty.Message);
            Assert.StartsWith("What the Planner reads: 5 layers by ID (1 empty, an empty folder), ", report.Reads());
        }

        [Fact]
        public void ANumberLedNodeThatIsNoLayerIsAnErrorWhenItHoldsTaggedObjectsAndAWarningWhereLayersGo()
        {
            var v2 = new V2Scene();
            // One number, inside a phase group: its tagged objects are unfiled.
            var retainment = v2.Scene.Unit("05_Retainment", "_01_Zone", v2.Zone1);
            v2.Object("IN_Sheet_Pile_1", retainment);
            v2.Object("IN_Sheet_Pile_2", retainment);
            // A space after the ID, inside another layer's helper.
            var spaced = v2.Scene.Helper("01_02 Kerbs", v2.Kerbs, "01_02_Kerbs");
            v2.Object("IN_Kerb_B", spaced);
            // Empty, where layers go: no folder.
            v2.Scene.Unit("07_Future_Works", "_02_Zone", v2.Zone2);
            // A Max group of objects led by a number, inside a layer: not a layer, and no slip.
            var group = v2.Scene.Node("2.4 High Hoarding", v2.Reinstate, "02_01_Reinstate", true);
            group.IsGroupHead = true;
            var member = v2.Scene.Object("Hoarding_Panel", group, "02_01_Reinstate");
            member.IsGroupMember = true;
            var report = v2.Check();

            var one = Single(report, PlannerNamingValidator.NotALayerCode, "05_Retainment");
            Assert.Equal(NamingSeverity.Error, one.Severity);
            Assert.Equal("Helper '05_Retainment': Not a layer with the new names: its ID needs two or three numbers. Its 2 tagged objects are not scheduled.", one.Message);
            Assert.Equal("Helper '01_02 Kerbs': Not a layer with the new names: a space follows its ID, where an underscore belongs: name it 01_02_Kerbs. Its 1 tagged object is not scheduled.",
                Single(report, PlannerNamingValidator.NotALayerCode, "01_02 Kerbs").Message);
            Assert.Equal("'IN_Sheet_Pile_1' is not linked under a layer's helper (a helper named <ID>_<Name>, the ID two or three numbers), so the Planner lists it as unfiled and cannot schedule it. "
                + "It sits under '05_Retainment', which is not a layer with the new names: its ID needs two or three numbers.", Single(report, PlannerNamingValidator.UnfiledCode, "IN_Sheet_Pile_1").Message);
            var empty = About(report, "07_Future_Works");
            Assert.Null(empty.Code);
            Assert.Equal(NamingSeverity.Warning, empty.Severity);
            Assert.Equal("Helper '07_Future_Works' is not a layer with the new names (its ID needs two or three numbers), so the Planner makes no folder for it.", empty.Message);
            // Only the pairing note names it, as today: a helper with no layer of its name.
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("2.4 High Hoarding") && !i.Message.StartsWith("The phasing layers and helper nodes do not pair up", StringComparison.Ordinal));
        }

        [Fact]
        public void ASpaceAfterAnIdSaysHowToNameTheHelperWhereverTheCheckMeetsIt()
        {
            var v2 = new V2Scene();
            // Empty, where layers go: no folder, and how to name it.
            v2.Scene.Unit("03_03 Walls", "_02_Zone", v2.Zone2);
            // Holding a tagged object, inside another layer's helper: the object is unfiled, and why.
            var spaced = v2.Scene.Helper("01_02 Kerbs", v2.Kerbs, "01_02_Kerbs");
            v2.Object("IN_Kerb_B", spaced);
            var report = v2.Check();
            Assert.Equal("Helper '03_03 Walls' is not a layer with the new names (a space follows its ID, where an underscore belongs: name it 03_03_Walls), so the Planner makes no folder for it.",
                About(report, "03_03 Walls").Message);
            Assert.EndsWith("It sits under '01_02 Kerbs', which is not a layer with the new names: a space follows its ID, where an underscore belongs: name it 01_02_Kerbs.",
                Single(report, PlannerNamingValidator.UnfiledCode, "IN_Kerb_B").Message);
        }

        [Fact]
        public void LayerNodesSharingAnIdWithDifferentNamesAreAnErrorButACopiedHelperIsNot()
        {
            var v2 = new V2Scene();
            var other = v2.Scene.Unit("01_02_Kerb_Install", "_01_Zone", v2.Zone1);
            v2.Object("IN_Kerb_C", other);
            // A helper copied in 3ds Max: the same layer, not a clash, and no duplicate-name warning either.
            var copy = v2.Scene.Helper("02_01_Reinstate", v2.Zone2, "02_01_Reinstate");
            v2.Object("IN_Copy_Kerb", copy);
            var report = v2.Check();
            var shared = Single(report, PlannerNamingValidator.SharedIdCode, "01_02_Kerbs");
            Assert.Equal(NamingSeverity.Error, shared.Severity);
            Assert.Equal(new[] { "01_02_Kerbs", "01_02_Kerb_Install" }, shared.Names);
            Assert.Equal("Helpers '01_02_Kerbs' and '01_02_Kerb_Install' share the layer ID 01_02 but not the name after it: the Planner reads them as one layer, with one folder named after the first. "
                + "Give each layer its own ID, or one name.", shared.Message);
            Assert.DoesNotContain(report.Issues, i => i.Names.Contains("02_01_Reinstate") || i.Names.Contains("02_01_Reinstate (1)"));
        }

        [Fact]
        public void TwoExportedObjectsWithOneNameAreErrorsAndTwoGroupsAWarning()
        {
            var v2 = new V2Scene();
            v2.Object("IN_Sheet_Pile_4", v2.Kerbs);
            v2.Object("IN_Sheet_Pile_4", v2.Kerbs);
            v2.Object("Plain_Box", v2.Kerbs);
            v2.Object("Plain_Box", v2.Reinstate);
            v2.Scene.Helper("Pivots", v2.Reinstate, "02_01_Reinstate");
            v2.Scene.Helper("Pivots", v2.Kerbs, "01_02_Kerbs");
            var report = v2.Check();
            var tagged = Single(report, "duplicate-export", "IN_Sheet_Pile_4");
            Assert.Equal(NamingSeverity.Error, tagged.Severity);
            Assert.Equal(new[] { "IN_Sheet_Pile_4", "IN_Sheet_Pile_4 (1)" }, tagged.Names);
            Assert.Equal("'IN_Sheet_Pile_4' is the name of 2 exported objects, so the export renames the second 'IN_Sheet_Pile_4 (1)'. The Planner reads each as the name says, "
                + "and their activities share a name where they fall in one layer: give each object its own name. Clean up scene graph\u2026 on the Schedule tab numbers the later ones.", tagged.Message);
            Assert.Equal("1 name can be converted by Clean up scene graph\u2026 on the Schedule tab.", report.CleanUpPointer());
            var plain = report.Issues.Single(i => i.Severity == NamingSeverity.Error && i.Subject == "Plain_Box");
            Assert.Null(plain.Code);
            Assert.Equal(new[] { "Plain_Box", "Plain_Box (1)" }, plain.Names);
            Assert.Equal("'Plain_Box' is the name of 2 objects. Every exported object needs a unique name: the Planner binds activities to objects by name.", plain.Message);
            // (A helper that is no layer is also counted as unpaired in the pairing note, as today.)
            var groups = report.Issues.Single(i => i.Severity == NamingSeverity.Warning && i.Subject == "Pivots");
            Assert.Equal("'Pivots' is the name of 2 groups; give each group its own name.", groups.Message);
        }

        [Fact]
        public void TextAfterASpaceThatMakesTwoOwnNamesOneIsAnError()
        {
            var v2 = new V2Scene();
            v2.Object("IN_Road Plates", v2.Kerbs);
            v2.Object("IN_Road Barrier", v2.Reinstate);
            v2.Object("IN_Gully Pot", v2.Kerbs);
            v2.Object("IN_Fence Panel_RM_02_01", v2.Kerbs);
            var report = v2.Check();
            var equal = report.Issues.Single(i => i.Code == null && i.Severity == NamingSeverity.Error);
            Assert.Equal(new[] { "IN_Road Plates", "IN_Road Barrier" }, equal.Names);
            Assert.Equal("'IN_Road Plates' and 'IN_Road Barrier': the Planner reads only the text before a space, so these read as one name between their tags, 'Road', "
                + "and it cannot tell them apart. Give each a name without spaces.", equal.Message);
            // The Planner's own warning for the shared own name stands beside it.
            Assert.Equal(NamingSeverity.Warning, Single(report, "shared-own-name", "IN_Road Plates").Severity);
            // A space alone is warned, as today; a tag after a space is the Planner's error, and not warned again.
            Assert.Equal("'IN_Gully Pot' contains a space. Names must not contain spaces: the Planner reads only 'IN_Gully', and the rest is a note.", About(report, "IN_Gully Pot").Message);
            Assert.Equal(NamingSeverity.Error, About(report, "IN_Fence Panel_RM_02_01").Severity);
            Assert.Equal("note-tag", About(report, "IN_Fence Panel_RM_02_01").Code);
        }

        [Fact]
        public void CopiesRenumberedSharedOwnNamesAndTheOldOrderAreWarned()
        {
            var v2 = new V2Scene();
            v2.Object("IN_Barrier_01_RM_02_01", v2.Strip);
            v2.Object("IN_Barrier_02_RM_02_02", v2.Strip);
            v2.Object("IN_Kerb_A_RM_02_01", v2.Reinstate);
            v2.Object("IN_Fence_02_01_RM", v2.Kerbs);
            v2.Object("IN_Gate_RM_01_02", v2.Kerbs);
            var report = v2.Check();
            var near = Single(report, "near-own-names", "IN_Barrier_01_RM_02_01");
            Assert.Equal(NamingSeverity.Warning, near.Severity);
            Assert.Equal("'IN_Barrier_01_RM_02_01' and 'IN_Barrier_02_RM_02_02': These objects in one layer have the same or nearly the same name but name different layers: "
                + "3ds Max may have renumbered a copy.", near.Message);
            var shared = Single(report, "shared-own-name", "IN_Kerb_A");
            Assert.Equal(new[] { "IN_Kerb_A", "IN_Kerb_A_RM_02_01" }, shared.Names);
            // With no space in either name, sharing an own name is the Planner's warning alone, not the check's error.
            Assert.DoesNotContain(report.Issues, i => i.Code == null && i.Severity == NamingSeverity.Error);
            Assert.Equal("'IN_Kerb_A' and 'IN_Kerb_A_RM_02_01': These objects share a name between their tags, so the Planner cannot follow a tag change on them. "
                + "Clean up scene graph\u2026 on the Schedule tab numbers the later ones.", shared.Message);
            Assert.Equal("'IN_Fence_02_01_RM': A layer ID comes before the tag at the end: the layer goes after the tag, as in IN_Kerb_RM_06_05_2.", Single(report, "old-order", "IN_Fence_02_01_RM").Message);
            Assert.Equal(NamingSeverity.Warning, Single(report, "own-layer", "IN_Gate_RM_01_02").Severity);
            // 02_02 is a layer nobody has.
            Assert.Equal(NamingSeverity.Warning, Single(report, "unknown-layer", "IN_Barrier_02_RM_02_02").Severity);
        }

        [Fact]
        public void ASplitPieceWhoseNameNamesALayerIsWarned()
        {
            var v2 = new V2Scene();
            var plates = v2.Object("IN_Plates_RM_02_01", v2.Kerbs);
            v2.Scene.Object("IN_Plates_RM_02_01_1", plates, "01_02_Kerbs");
            var report = v2.Check();
            var piece = Single(report, "piece-names-layer", "IN_Plates_RM_02_01");
            Assert.Equal(new[] { "IN_Plates_RM_02_01", "IN_Plates_RM_02_01_1" }, piece.Names);
            Assert.Equal(NamingSeverity.Warning, piece.Severity);
            Assert.Equal("'IN_Plates_RM_02_01' (its piece 'IN_Plates_RM_02_01_1'): A piece of this object has a name that, read alone, names a layer. It is scheduled as its object\u2019s name says.",
                piece.Message);
            // The piece folds into its object: one object, one unknown layer, nothing else.
            Assert.DoesNotContain(report.Issues, i => i.Code == "unknown-layer");
        }

        [Fact]
        public void LowerCaseNearTagsTypeWordsHyphensAndEdgeUnderscores()
        {
            var v2 = new V2Scene();
            v2.Object("in_Kerb", v2.Kerbs);
            v2.Object("Kerb_rm", v2.Kerbs);
            v2.Object("IN_Pipe_RM_Trench", v2.Kerbs);
            v2.Object("IN_Road-Marking", v2.Kerbs);
            v2.Object("IN_Sub-Base_RM", v2.Kerbs);
            v2.Object("IN_TR_Subbase_C_", v2.Kerbs);
            var report = v2.Check();
            Assert.Equal(NamingSeverity.Warning, Single(report, "lower-case", "in_Kerb").Severity);
            Assert.Equal(NamingSeverity.Warning, Single(report, "lower-case", "Kerb_rm").Severity);
            Assert.Equal("'IN_Pipe_RM_Trench': IN or RM in the middle of the name is not read as a tag.", Single(report, "type-in-middle", "IN_Pipe_RM_Trench").Message);
            var hyphens = Single(report, "hyphen", "IN_Road-Marking");
            Assert.Equal(new[] { "IN_Road-Marking", "IN_Sub-Base_RM" }, hyphens.Names);
            Assert.StartsWith("2 object names contain a hyphen. Unreal's Datasmith import turns every hyphen", hyphens.Message);
            var trimmed = Single(report, "trimmed", "IN_TR_Subbase_C_");
            Assert.Equal(NamingSeverity.Note, trimmed.Severity);
            Assert.Equal("1 object name has underscores at the ends of the name between the tags, which are not part of it: IN_TR_Subbase_C_. Clean up scene graph\u2026 on the Schedule tab trims them.",
                trimmed.Message);
            Assert.Equal("3 names can be converted by Clean up scene graph\u2026 on the Schedule tab.", report.CleanUpPointer());
            // Near-tags read no tag: the untagged note does not repeat them.
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("has no IN or RM tag"));
        }

        [Fact]
        public void ANameCleanUpConvertsForTwoReasonsCountsOnceAndErrorsLeadTheSummary()
        {
            var v2 = new V2Scene();
            v2.Object("IN_Road-Marking_", v2.Kerbs);
            v2.Object("IN_Kerb_IN", v2.Kerbs);
            var report = v2.Check();
            Assert.Equal(new[] { "IN_Road-Marking_" }, Single(report, "hyphen", "IN_Road-Marking_").Names);
            Assert.Equal(new[] { "IN_Road-Marking_" }, Single(report, "trimmed", "IN_Road-Marking_").Names);
            Assert.Equal(new[] { "IN_Road-Marking_" }, report.CleanUp);
            Assert.Equal("1 name can be converted by Clean up scene graph\u2026 on the Schedule tab.", report.CleanUpPointer());
            Assert.Equal("1 object name contains a hyphen. Unreal's Datasmith import turns every hyphen in an object's name into an underscore, so the name would not match between the Planner "
                + "and Unreal: use underscores instead. Clean up scene graph\u2026 on the Schedule tab converts it: IN_Road-Marking_.", Single(report, "hyphen", "IN_Road-Marking_").Message);
            Assert.Equal("Planner naming check (new names): 7 tagged objects in 3 layers; 1 error(s), 1 warning(s), 1 note(s).", report.Summary());
            Assert.Equal(new[] { NamingSeverity.Error, NamingSeverity.Warning, NamingSeverity.Note }, report.Issues.Select(i => i.Severity));
        }

        [Fact]
        public void AnEmptyLayerWhoseHelperDoesNotReachThePlannerIsWarned()
        {
            var v2 = new V2Scene();
            var hidden = v2.Scene.Unit("01_03_Gates", "_01_Zone", v2.Zone1);
            hidden.Hidden = true;
            var noExport = v2.Scene.Unit("02_02_Barrier_Removal", "_02_Zone", v2.Zone2);
            noExport.NoExport = true;
            // Hidden, but holding an exported object: the export writes it as an empty node, and the Planner reads it.
            var holding = v2.Scene.Unit("02_03_Kerb_Removal", "_02_Zone", v2.Zone2);
            holding.Hidden = true;
            v2.Object("RM_Kerb", holding);
            // Not selected while the export takes only the selection.
            v2.Scene.Unit("02_04_Gully_Removal", "_02_Zone", v2.Zone2).NotInThisExport = true;
            // Outside the phasing tree: not checked.
            v2.Scene.Helper("09_01_Elsewhere", null, "0").Hidden = true;
            var report = v2.Check();
            var left = report.Issues.Single(i => i.Message.Contains("will not reach the Planner"));
            Assert.Equal(NamingSeverity.Warning, left.Severity);
            Assert.Equal(new[] { "01_03_Gates", "02_02_Barrier_Removal", "02_04_Gully_Removal" }, left.Names);
            Assert.Equal("3 layer helpers will not reach the Planner, as they are left out of the export with nothing exported under them: the Planner makes no folder for them, "
                + "and parks TBC any work named for them that its layer list does not place: '01_03_Gates' (" + PlannerScheduleModel.HiddenReason + "), '02_02_Barrier_Removal' ("
                + PlannerScheduleModel.NoExportReason + "), '02_04_Gully_Removal' (" + PlannerScheduleModel.NotInThisExportReason + "). "
                + "Show the helper or tick Export hidden objects, untick Do not export, or take it into the export.", left.Message);
            // With Export hidden objects on, the hidden one reaches the Planner.
            v2.Scene.Snapshot.ExportHidden = true;
            report = v2.Check();
            Assert.Equal(new[] { "02_02_Barrier_Removal", "02_04_Gully_Removal" }, report.Issues.Single(i => i.Message.Contains("will not reach the Planner")).Names);
        }

        [Fact]
        public void ATaggedObjectUnderNoLayersHelperIsWarnedWithWhereItSits()
        {
            var v2 = new V2Scene();
            v2.Scene.Object("IN_Loose_Kerb", null, "01_02_Kerbs");
            v2.Scene.Object("IN_Far_Away", null, "0");
            var report = v2.Check();
            Assert.Equal("'IN_Loose_Kerb' is not linked under a layer's helper (a helper named <ID>_<Name>, the ID two or three numbers), so the Planner lists it as unfiled and cannot schedule it. "
                + "It sits on the Max layer '01_02_Kerbs': Link objects to their helpers on the Schedule tab puts it under the layer's helper.",
                Single(report, PlannerNamingValidator.UnfiledCode, "IN_Loose_Kerb").Message);
            Assert.EndsWith("so the Planner lists it as unfiled and cannot schedule it.", Single(report, PlannerNamingValidator.UnfiledCode, "IN_Far_Away").Message);
            Assert.Equal(NamingSeverity.Warning, Single(report, PlannerNamingValidator.UnfiledCode, "IN_Far_Away").Severity);
            Assert.Equal("Planner naming check (new names): 7 tagged objects in 3 layers, 2 unfiled; 0 error(s), 2 warning(s), 0 note(s).", report.Summary());
        }

        [Fact]
        public void InstallsAtTheEndOnlyAndUntaggedObjectsInALayerAreNoted()
        {
            var v2 = new V2Scene();
            v2.Object("Kerb_IN", v2.Kerbs);
            v2.Object("Edge_IN", v2.Reinstate);
            v2.Object("Site_Cabin", v2.Kerbs);
            // Linked under a scheduled object: shown and hidden with it.
            var kerb = v2.Scene.Find("IN_Kerb_A");
            v2.Scene.Object("Kerb_Bolt", kerb, "01_02_Kerbs");
            // Outside every layer: not noted.
            v2.Scene.Object("Tree_01", null, "0");
            var report = v2.Check();
            var ends = About(report, "Kerb_IN");
            Assert.Equal(NamingSeverity.Note, ends.Severity);
            Assert.Equal(new[] { "Kerb_IN", "Edge_IN" }, ends.Names);
            Assert.Equal("2 objects are installed at the end of their layer, as a closing _IN with no leading tag says (Kerb_IN); to install one while its layer runs, start the name with IN_ instead: "
                + "Kerb_IN, Edge_IN.", ends.Message);
            Assert.Equal("'Site_Cabin' sits in the layer '01_02_Kerbs' but has no IN or RM tag, so the Planner shows it for the whole programme.", About(report, "Site_Cabin").Message);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("Kerb_Bolt") || i.Message.Contains("Tree_01"));
            Clean(report);
            Assert.EndsWith("; 3 objects have no IN or RM tag and show for the whole programme.", report.Reads());
        }

        [Fact]
        public void LayersAndHelpersThatDoNotPairAreNotedAsToday()
        {
            var v2 = new V2Scene();
            v2.Scene.Layer("01_03_Gates", "_01_Zone");
            v2.Scene.Helper("01_04_Posts", v2.Zone1, "_01_Zone");
            var report = v2.Check();
            var pairing = report.Issues.Single(i => i.Message.StartsWith("The phasing layers and helper nodes do not pair up", StringComparison.Ordinal));
            Assert.Equal(NamingSeverity.Note, pairing.Severity);
            Assert.Equal("The phasing layers and helper nodes do not pair up: 1 layer has no helper node of the same name (01_03_Gates); 1 helper node has no layer of the same name (01_04_Posts).",
                pairing.Message);
            Assert.Equal("Phasing counts: 6 layers in the Max layer Work_Phasing, 6 helper nodes under Work_Phasing.", report.Counts.Describe(false));
        }

        [Fact]
        public void ALayerIdWrittenAnotherWayIsNotedForCleanUp()
        {
            var v2 = new V2Scene();
            var gates = v2.Scene.Unit("1_3_Gates", "_01_Zone", v2.Zone1);
            v2.Object("IN_Gate", gates);
            var report = v2.Check();
            var note = About(report, "1_3_Gates");
            Assert.Equal(NamingSeverity.Note, note.Severity);
            Assert.Equal("1 helper name writes the layer ID another way than the current one (two-digit phase and stage, then the sub-stage): 1_3_Gates (01_03_Gates). "
                + "The Planner reads it the same; Clean up scene graph\u2026 on the Schedule tab writes it the current way.", note.Message);
            Assert.Equal("1 name can be converted by Clean up scene graph\u2026 on the Schedule tab.", report.CleanUpPointer());
        }

        [Fact]
        public void AnOldNameSceneKeepsTodaysRulesAndGetsOneNote()
        {
            // The export picks the rules by the scene's naming (spec 2.3): the old names keep Validate, unchanged.
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone = scene.Unit("_01_Zone", "Work_Phasing", root);
            var layer = scene.Unit("01_01_Strip_Out_01-09-26", "_01_Zone", zone);
            scene.Object("Ph1_St01_RM_Old_Fence", layer, layer.Name);
            scene.Object("Ph1_St01_IN_Hoarding_Ph2_St01_RM", layer, layer.Name);
            var naming = PlannerSchedule.NamingOfScene(new PlannerScheduleModel(scene.Snapshot));
            Assert.Equal(PlannerNamingKind.Old, naming.Naming);
            // The note points to Clean up (D23) and says a live project waits for the planning team (D25), with no "new workspace only".
            Assert.Equal("This scene uses the old Ph/St names. The Schedule tab needs the new names: Clean up scene graph\u2026 there converts the scene. "
                + "On a live project, wait for the planning team's go-ahead first, as the Planner reads the new names only from its next release.", PlannerNamingValidator.OldNamesNote);
            Assert.DoesNotContain("workspace", PlannerNamingValidator.OldNamesNote);
            Assert.DoesNotContain("workspace", PlannerCleanUp.OldNamesWarning);
            Assert.Contains("planning team says to go ahead", PlannerCleanUp.OldNamesWarning);
            // Today's report knows nothing of the new names.
            var report = PlannerNamingValidator.Validate(new[] { new SceneNodeInfo { Name = "Ph1_St01_RM_Old_Fence", IsMesh = true, Ancestors = { layer.Name, "_01_Zone", "Work_Phasing" } } },
                new DateTime(2026, 10, 18));
            Assert.Equal(PlannerNamingKind.Old, report.Naming);
            Assert.Null(report.Reads());
            Assert.Null(report.CleanUpPointer());
            Assert.All(report.Issues, i => Assert.Null(i.Code));
            Assert.StartsWith("Planner naming check: 1 tagged object(s) in 1 dated group(s)", report.Summary());
            // A scene with no tagged object and no layer has no naming: today's rules, and no note.
            var plain = new TestScene();
            plain.Object("Tree_01", null, "0");
            Assert.Null(PlannerSchedule.NamingOfScene(new PlannerScheduleModel(plain.Snapshot)));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Parity with the Planner

        private static List<string> Strings(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? new List<string>() : token.Select(t => (string)t).ToList();
        }

        private static readonly Dictionary<string, PlannerNamingCodeInfo> ByCode =
            PlannerNaming.NamingIssueV2.Values.ToDictionary(i => i.Code, StringComparer.Ordinal);

        /// <summary>
        /// Every way the check differs from the Planner's result for a model: what it reads (naming issues, old names, shared
        /// IDs, layer issues, unfiled, scheduled and untagged objects) against the Planner's lists, then the report: each of the
        /// Planner's items reported, with its code and severity, once, and nothing more under the Planner's codes.
        /// </summary>
        internal static List<string> Compare(string id, NamingReport report, JObject planner)
        {
            var differences = new List<string>();
            Action<string, IEnumerable<string>, IEnumerable<string>> list = (what, mine, theirs) =>
            {
                var a = mine.ToList();
                var b = theirs.ToList();
                if (!a.SequenceEqual(b, StringComparer.Ordinal))
                {
                    var onlyMine = a.Except(b, StringComparer.Ordinal).ToList();
                    var onlyTheirs = b.Except(a, StringComparer.Ordinal).ToList();
                    differences.Add(id + ": " + what + (onlyMine.Count == 0 && onlyTheirs.Count == 0 && a.Count == b.Count
                        ? ": the same " + a.Count + " in another order\n    check:   " + string.Join(" | ", a.ToArray()) + "\n    Planner: " + string.Join(" | ", b.ToArray())
                        : "\n    only the check's:   " + string.Join(" | ", onlyMine.ToArray()) + "\n    only the Planner's: " + string.Join(" | ", onlyTheirs.ToArray())));
                }
            };
            var v2 = (JObject)planner["v2"];
            var read = report.V2;
            list("naming issues", read.NamingIssues.Select(i => PlannerNaming.CodeText(i.Code) + ": " + string.Join(",", i.Names.ToArray())),
                v2["namingIssues"].Select(i => (string)i["code"] + ": " + string.Join(",", Strings(i["names"]).ToArray())));
            list("old names", read.OtherNaming, Strings(v2["otherNaming"]));
            list("shared IDs", read.SharedIds.Select(s => s.Id + ": " + string.Join(",", s.Names.ToArray())),
                v2["sharedIds"].Where(s => (string)s["kind"] == "layers").Select(s => (string)s["id"] + ": " + string.Join(",", Strings(s["names"]).ToArray())));
            list("layer issues", read.LayerIssues, Strings(planner["layerIssues"]));
            list("unfiled", read.Unfiled, Strings(planner["unfiled"]));
            list("scheduled", read.Matched, Strings(planner["matched"]));
            // Untagged objects as a set: a GLB mesh of several materials reaches the Planner's index as a node of its pieces,
            // after the meshes, where the scene (one mesh per 3ds Max object) has it among them; only this list's order shows it.
            list("untagged", read.Skipped.OrderBy(s => s, StringComparer.Ordinal), Strings(planner["skipped"]).OrderBy(s => s, StringComparer.Ordinal));

            // The report: each of the Planner's items once, as code, severity and name.
            var expected = new List<string>();
            foreach (JObject issue in v2["namingIssues"])
            {
                var code = (string)issue["code"];
                expected.AddRange(Strings(issue["names"]).Select(n => code + "|" + ByCode[code].Severity + "|" + n));
            }
            expected.AddRange(Strings(v2["otherNaming"]).Select(n => "old-tag|Error|" + n));
            foreach (var line in Strings(planner["layerIssues"]))
            {
                // "<node>: <text>": the node is the one whose name the Planner's text follows.
                var pair = read.LayerNameIssues.Select(p => new { Line = p.Key + ": " + PlannerNaming.NamingIssueV2[p.Value].Text, Code = PlannerNaming.NamingIssueV2[p.Value].Code, Node = p.Key })
                    .FirstOrDefault(p => p.Line == line);
                if (pair != null)
                {
                    expected.Add(pair.Code + "|Warning|" + pair.Node);
                    continue;
                }
                var not = read.NotLayers.FirstOrDefault(p => line.StartsWith(p.Key + ": Not a layer with the new names", StringComparison.Ordinal));
                expected.Add(not.Key != null ? PlannerNamingValidator.NotALayerCode + "|Error|" + not.Key : "unread layer issue|" + line);
            }
            foreach (var shared in v2["sharedIds"].Where(s => (string)s["kind"] == "layers"))
            {
                expected.AddRange(Strings(shared["names"]).Select(n => PlannerNamingValidator.SharedIdCode + "|Error|" + n));
            }
            expected.AddRange(Strings(planner["unfiled"]).Select(n => PlannerNamingValidator.UnfiledCode + "|Warning|" + n));
            var reported = report.Issues.Where(i => i.Code != null).SelectMany(i => i.Names.Select(n => i.Code + "|" + i.Severity + "|" + n)).ToList();
            list("reported", reported.OrderBy(s => s, StringComparer.Ordinal), expected.OrderBy(s => s, StringComparer.Ordinal));
            return differences;
        }

        private static string Describe(NamingReport report)
        {
            var lines = report.Issues.Select(i => i.Severity.ToString().ToUpperInvariant() + (i.Code != null ? " [" + i.Code + "]" : string.Empty) + ": " + i.Message).ToList();
            var pointer = report.CleanUpPointer();
            if (pointer != null)
            {
                lines.Add("WARNING: " + pointer);
            }
            lines.Add(report.Summary());
            lines.Add(report.Reads());
            if (report.Counts != null)
            {
                lines.Add(report.Counts.Describe(false));
            }
            return string.Join("\n", lines.ToArray());
        }

        [Fact]
        public void SyntheticScenesMatchThePlanner()
        {
            var scenes = JArray.Parse(File.ReadAllText(Path.Combine(Here(), "PlannerScheduleScenesV2.json")));
            var results = JArray.Parse(File.ReadAllText(Path.Combine(Here(), "PlannerScheduleScenesV2.utc.json")));
            var byId = results.Cast<JObject>().ToDictionary(r => (string)r["id"], StringComparer.Ordinal);
            var differences = new List<string>();
            int checkedScenes = 0, old = 0, items = 0;
            foreach (JObject scene in scenes)
            {
                var id = (string)scene["id"];
                var planner = byId[id];
                var model = new PlannerScheduleModel(PlannerSceneFiles.SnapshotOf((JArray)scene["nodes"]));
                var naming = PlannerSchedule.NamingOfScene(model);
                var mine = naming != null && naming.Naming == PlannerNamingKind.New ? "new" : "old";
                if (mine != (string)planner["naming"])
                {
                    differences.Add(id + ": naming " + mine + " against " + planner["naming"]);
                }
                if (planner["v2"] == null || planner["v2"].Type == JTokenType.Null)
                {
                    // Read as the old names: the export runs today's check.
                    old++;
                    continue;
                }
                checkedScenes++;
                var listLayers = PlannerScheduleV2ParityTests.OptionsOf(scene, TimeZoneInfo.Utc).Folders.Select(f => f.Name).ToList();
                var report = PlannerNamingValidator.ValidateV2(model, listLayers);
                items += report.Issues.Count(i => i.Code != null);
                differences.AddRange(Compare(id, report, planner));
            }
            output.WriteLine("{0} new-name scenes checked ({1} issues with the Planner's codes), {2} read as old, {3} differences", checkedScenes, items, old, differences.Count);
            Assert.True(differences.Count == 0, string.Join("\n", differences.ToArray()));
            Assert.Equal(40, checkedScenes);
            Assert.Equal(1, old);
        }

        [EnvironmentFact("PLANNER_NAMING_V2_CHECK")]
        public void RealModelsMatchThePlanner()
        {
            var path = EnvironmentFactAttribute.InputFile("PLANNER_NAMING_V2_CHECK");
            var models = JArray.Parse(File.ReadAllText(path));
            var differences = new List<string>();
            var log = new List<string>();
            foreach (JObject planner in models)
            {
                var id = (string)planner["id"];
                var model = new PlannerScheduleModel(PlannerSceneFiles.SnapshotOf((JArray)planner["nodes"]));
                var naming = PlannerSchedule.NamingOfScene(model);
                Assert.True(naming != null && naming.Naming == PlannerNamingKind.New && (string)planner["naming"] == "new", id + " reads " + (naming == null ? "nothing" : naming.Naming.ToString()));
                var listLayers = planner["listLayers"].Type == JTokenType.Null ? null : Strings(planner["listLayers"]);
                var report = PlannerNamingValidator.ValidateV2(model, listLayers);
                var found = Compare(id, report, planner);
                differences.AddRange(found);
                log.Add("== " + id + (listLayers != null ? " (list of " + listLayers.Count + " rows)" : " (no list)") + ": " + found.Count + " differences");
                log.Add(Describe(report));
                output.WriteLine("{0}: {1} errors, {2} warnings, {3} notes, {4} with the Planner's codes; {5} differences",
                    id, report.Errors, report.Warnings, report.Notes, report.Issues.Count(i => i.Code != null), found.Count);
                if (id.StartsWith("v30-2", StringComparison.Ordinal) && !id.Contains("slips"))
                {
                    // The converted v30 2 is clean in the Planner: no issue of the Planner's, and nothing worse than a note.
                    Assert.DoesNotContain(report.Issues, i => i.Code != null);
                    Assert.True(report.Errors == 0 && report.Warnings == 0, Describe(report));
                }
            }
            var file = Environment.GetEnvironmentVariable("PLANNER_NAMING_V2_CHECK_REPORT");
            if (!string.IsNullOrEmpty(file))
            {
                File.WriteAllText(file, string.Join("\r\n", log.ToArray()) + "\r\n");
            }
            Assert.True(differences.Count == 0, string.Join("\n", differences.ToArray()));
            Assert.Equal(9, models.Count);
        }
    }
}
