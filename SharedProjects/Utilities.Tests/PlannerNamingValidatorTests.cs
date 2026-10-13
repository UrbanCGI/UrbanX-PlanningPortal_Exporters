using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    public class PlannerNamingValidatorTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 9);
        private readonly ITestOutputHelper output;

        public PlannerNamingValidatorTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const string Piling = "01_Sheet_Piling_SA&DS3_17-08-26_18-09-26";
        private const string HoardingRemoval = "06-2 Hoarding Removal_20-10-26_23-10-26";
        private const string Undated = "01 Hoardings Removal";
        private const string FinishBeforeStart = "30_CycleLane_Splitter_Island_Install_17-02-27_23-02-17";
        private const string TwinA = "05_MainRd_Hoardings_Inst_TBC_18-10-26_25-02-27";
        private const string TwinB = "05_MainRd_Hoardings_inst_TBC_17-10-26_18-10-26";
        private const string OldYear = "00_Hrds_TBC_01-08-22_23-10-26";
        private const string PlainGroup = "Context";

        private static SceneNodeInfo Group(string name)
        {
            return new SceneNodeInfo { Name = name, IsMesh = false };
        }

        private static SceneNodeInfo Mesh(string name, string parent = null, string layer = null)
        {
            return new SceneNodeInfo
            {
                Name = name,
                IsMesh = true,
                Ancestors = parent == null ? new List<string>() : new List<string> { parent },
                LayerName = layer
            };
        }

        private static List<SceneNodeInfo> RepresentativeScene()
        {
            return new List<SceneNodeInfo>
            {
                Group(Piling), Group(HoardingRemoval), Group(Undated), Group(FinishBeforeStart), Group(TwinA), Group(TwinB), Group(OldYear), Group(PlainGroup),
                Mesh("Ph1_St01_IN_Sheet_Pile_1", Piling),                      // clean
                Mesh("Ph1_St01_IN_Sheet_Pile_1", Piling),                      // duplicate name -> error
                Mesh("Ph0_St00_IN_Hoarding_A_Ph1_St06_RM", HoardingRemoval),  // trail owned by the group -> clean
                Mesh("Ph0_St00_IN_Hoarding_B", HoardingRemoval),              // neither tag matches stage 6 -> warning
                Mesh("Ph1_S03_RM_Thing", Piling),                             // malformed lead -> error
                Mesh("Ph1_St01_IN_RC_Hrd_Ph1_S03_RM", Piling),                // malformed trail -> error
                Mesh("Ph2_St34_IN_TR_Sidewalk_B"),                            // root level -> unfiled warning
                Mesh("Ph1_St00_IN_Fway", PlainGroup),                         // undated group -> unfiled warning with hint
                Mesh("Ph1_St01_RM_RC_Hrd_", Piling),                          // trailing underscore: trimmed by the Planner, not reported
                Mesh("Ph1_St1_IN_Kerb", Piling),                              // zero padding -> note
                Mesh("EUS_Con_HRB_Kerb_001"),                                 // untagged context at root -> nothing
                Mesh("St1_Kerb_003", Piling),                                 // untagged inside dated group -> note
                Mesh("Ph1_St01_IN", Piling),                                  // empty description -> warning
                Mesh("Ph1_St01_IN_Sheet Pile 2", Piling),                     // spaces in the name -> warning
                Mesh("Ph3_St03_IN_Pile", null, "03_Sheet_Piling_DS2_18-09-26_23-10-26") // unfiled, layer hint
            };
        }

        [Fact]
        public void CleanSceneHasNoIssues()
        {
            var report = PlannerNamingValidator.Validate(new[] { Group(Piling), Mesh("Ph1_St01_IN_Sheet_Pile_1", Piling), Mesh("EUS_Con_HRB_Kerb_001") }, Today);
            Assert.Empty(report.Issues);
            Assert.Equal(1, report.TaggedObjects);
            Assert.Equal(1, report.DatedGroups);
            Assert.Equal(0, report.UnfiledObjects);
            Assert.Contains("No naming problems found.", report.Summary());
        }

        [Fact]
        public void RepresentativeSceneCountsAndOrdering()
        {
            var report = PlannerNamingValidator.Validate(RepresentativeScene(), Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            output.WriteLine(report.Summary());

            Assert.Equal(12, report.TaggedObjects);
            Assert.Equal(7, report.DatedGroups);
            Assert.Equal(3, report.UnfiledObjects);
            Assert.Equal(4, report.Errors);
            Assert.Equal(10, report.Warnings); // includes the spaces in two group names and one object name
            Assert.Equal(3, report.Notes);
            Assert.Equal(NamingSeverity.Error, report.Issues.First().Severity);
            Assert.Equal(NamingSeverity.Note, report.Issues.Last().Severity);
            Assert.Contains("4 error(s), 10 warning(s), 3 note(s)", report.Summary());
        }

        [Fact]
        public void ErrorsAreTheUnreadableAndAmbiguousCases()
        {
            var errors = PlannerNamingValidator.Validate(RepresentativeScene(), Today).Issues.Where(i => i.Severity == NamingSeverity.Error).ToList();
            Assert.Contains(errors, e => e.Subject == FinishBeforeStart && e.Message.Contains("finish 23-02-17 is before start 17-02-27"));
            Assert.Contains(errors, e => e.Subject == "Ph1_St01_IN_Sheet_Pile_1" && e.Message.Contains("is the name of 2 objects"));
            Assert.Contains(errors, e => e.Subject == "Ph1_S03_RM_Thing" && e.Message.Contains("leading tag is malformed"));
            Assert.Contains(errors, e => e.Subject == "Ph1_St01_IN_RC_Hrd_Ph1_S03_RM" && e.Message.Contains("looks like a second Ph/St tag"));
        }

        [Fact]
        public void WarningsExplainWhatThePlannerWillDo()
        {
            var warnings = PlannerNamingValidator.Validate(RepresentativeScene(), Today).Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
            Assert.Contains(warnings, w => w.Subject == Undated && w.Message.Contains("neither dates nor TBC"));
            Assert.Contains(warnings, w => w.Message.Contains("differ only by letter case") && w.Message.Contains("'05_MainRd_Hoardings_Inst'") && w.Message.Contains("'05_MainRd_Hoardings_inst'"));
            Assert.Contains(warnings, w => w.Subject == "Ph0_St00_IN_Hoarding_B" && w.Message.Contains("(St06-2)")); // the group's sub-number is its sub-stage
            Assert.Contains(warnings, w => w.Subject == "Ph2_St34_IN_TR_Sidewalk_B" && w.Message.Contains("unfiled") && w.Message.Contains("Group it under a node named"));
            Assert.Contains(warnings, w => w.Subject == "Ph1_St00_IN_Fway" && w.Message.Contains("Its group 'Context' needs an order number and dates"));
            Assert.DoesNotContain(warnings, w => w.Subject == "Ph1_St01_RM_RC_Hrd_"); // edge underscores are the Planner's to trim
            Assert.Contains(warnings, w => w.Subject == "Ph1_St01_IN_Sheet Pile 2" && w.Message.Contains("contains a space"));
            Assert.Contains(warnings, w => w.Subject == HoardingRemoval && w.Message.Contains("Group") && w.Message.Contains("contains a space"));
            Assert.Contains(warnings, w => w.Subject == Undated && w.Message.Contains("contains a space"));
            Assert.DoesNotContain(warnings, w => w.Subject == Piling && w.Message.Contains("contains a space"));
            Assert.Contains(warnings, w => w.Subject == "Ph1_St01_IN" && w.Message.Contains("no description"));
            Assert.Contains(warnings, w => w.Subject == "Ph3_St03_IN_Pile" && w.Message.Contains("3ds Max layer '03_Sheet_Piling_DS2_18-09-26_23-10-26'"));
        }

        [Fact]
        public void NotesAreAdvisory()
        {
            var notes = PlannerNamingValidator.Validate(RepresentativeScene(), Today).Issues.Where(i => i.Severity == NamingSeverity.Note).ToList();
            Assert.Contains(notes, n => n.Subject == OldYear && n.Message.Contains("01-08-22 falls in 2022"));
            Assert.Contains(notes, n => n.Subject == "Ph1_St1_IN_Kerb" && n.Message.Contains("two digits"));
            Assert.Contains(notes, n => n.Subject == "St1_Kerb_003" && n.Message.Contains("carries no Ph/St tag"));
        }

        [Fact]
        public void NearestDatedAncestorWins()
        {
            var nested = new SceneNodeInfo
            {
                Name = "Ph0_St00_IN_Wall_Ph1_St06_RM",
                IsMesh = true,
                Ancestors = new List<string> { "Sub assembly", HoardingRemoval, Piling }
            };
            var report = PlannerNamingValidator.Validate(new[] { nested }, Today);
            Assert.DoesNotContain(report.Issues, i => i.Subject == nested.Name); // owned by the 06 group, not confused by the 01 group further up
            Assert.All(report.Issues, i => Assert.Contains("contains a space", i.Message)); // only the spaces in the 06 group's own name are reported
            Assert.Equal(2, report.DatedGroups);
        }

        [Fact]
        public void UnreadableDateIsAnError()
        {
            var report = PlannerNamingValidator.Validate(new[] { Group("03_Piling_31-02-26_05-03-26") }, Today);
            var error = Assert.Single(report.Issues);
            Assert.Equal(NamingSeverity.Error, error.Severity);
            Assert.Contains("unreadable date \"31-02-26\"", error.Message);
        }

        [Fact]
        public void DuplicateGroupNamesAreWarnings()
        {
            var report = PlannerNamingValidator.Validate(new[] { Group(Piling), Group(Piling) }, Today);
            var warning = Assert.Single(report.Issues);
            Assert.Equal(NamingSeverity.Warning, warning.Severity);
            Assert.Contains("is the name of 2 groups", warning.Message);
        }

        [Fact]
        public void NullsAreTolerated()
        {
            var report = PlannerNamingValidator.Validate(new SceneNodeInfo[] { null, new SceneNodeInfo { Name = null, IsMesh = true }, new SceneNodeInfo { Name = "Ph1_St01_IN_X", IsMesh = true, Ancestors = null } }, Today);
            Assert.Equal(1, report.TaggedObjects);
            Assert.Single(report.Issues); // unfiled
        }

        /// <summary>
        /// Runs the rules over a real exported hierarchy when PLANNER_NODES_FIXTURE points at a JSON file of the form
        /// { "nodes": [ { "name": "...", "parent": "..." | null, "isMesh": true|false } ] } and prints the report.
        /// </summary>
        [Fact]
        public void SubStagesMatchTheirOwnGroupOrTheWholeStage()
        {
            const string subA = "05-1_Subbase_A_01-11-26_02-11-26";
            const string subB = "05-2_Subbase_B_03-11-26_04-11-26";
            const string whole = "05_Subbase_TBC";
            var scene = new List<SceneNodeInfo>
            {
                Group(subA), Group(subB), Group(whole),
                Mesh("Ph2_St05-1_IN_TR_Subbase_A", subA),  // the sub-stage in its own group -> clean
                Mesh("Ph2_St05-2_IN_TR_Subbase_B", subB),  // its own sub-numbered group -> clean
                Mesh("Ph2_St05-2_IN_TR_Subbase_C", whole), // a plain group number covers its sub-stages -> clean
                Mesh("Ph2_St05_IN_TR_Subbase_D", subA),    // a plain St under a sub-numbered group, as before -> clean
                Mesh("Ph2_St05-2_IN_TR_Subbase_E", subA),  // the wrong sub-stage -> warning naming St05-1
                Mesh("Ph2_St5-2_IN_TR_Subbase_F", subB)    // unpadded stage digits -> note
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            Assert.Equal(0, report.Errors);
            Assert.Equal(6, report.TaggedObjects);
            Assert.Equal(3, report.DatedGroups);
            Assert.Equal(0, report.UnfiledObjects);
            var warnings = report.Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
            Assert.Single(warnings);
            Assert.Equal("Ph2_St05-2_IN_TR_Subbase_E", warnings[0].Subject);
            Assert.Contains("(St05-1)", warnings[0].Message);
            var notes = report.Issues.Where(i => i.Severity == NamingSeverity.Note).ToList();
            Assert.Single(notes);
            Assert.Equal("Ph2_St5-2_IN_TR_Subbase_F", notes[0].Subject);
            Assert.Contains("St06-1", notes[0].Message);
        }

        [Fact]
        public void FullStopSubStagesAreOneWarningForObjectsAndOneForGroups()
        {
            const string dotted = "05.2_Subbase_B_03-11-26_04-11-26";
            const string hyphened = "05-1_Subbase_A_01-11-26_02-11-26";
            var scene = new List<SceneNodeInfo> { Group(dotted), Group(hyphened), Group("2.4 High Hoarding") };
            for (int i = 1; i <= 12; i++)
            {
                scene.Add(Mesh("Ph2_St05.2_IN_Kerb_" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), dotted));
            }
            scene.Add(Mesh("Ph2_St05-1_IN_Kerb_A_Ph3_St01.1_RM", hyphened)); // the trailing tag only
            scene.Add(Mesh("Ph2_St05-1_IN_1.8_x_2.4_Heras_Panel", hyphened));  // full stops in a description are fine
            scene.Add(Mesh("Ph2_St05.1_IN_Fence", "2.4 High Hoarding"));      // dotted, and unfiled

            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            var legacy = report.Issues.Where(i => i.Message.Contains("with a full stop, the old form")).ToList();
            Assert.Equal(2, legacy.Count);
            Assert.All(legacy, w => Assert.Equal(NamingSeverity.Warning, w.Severity));
            var objects = legacy.Single(w => w.Message.StartsWith("14 object name(s)", System.StringComparison.Ordinal));
            Assert.Equal("Ph2_St05.2_IN_Kerb_01", objects.Subject);
            Assert.Contains("Fix phasing names", objects.Message);
            Assert.Contains("Ph2_St05.2_IN_Kerb_10 and 4 more.", objects.Message);
            Assert.DoesNotContain("Heras_Panel", objects.Message);
            var groups = legacy.Single(w => w.Message.StartsWith("1 group name(s)", System.StringComparison.Ordinal));
            Assert.Equal(dotted, groups.Subject);
            Assert.EndsWith(": " + dotted + ".", groups.Message);
            Assert.Equal(1, report.UnfiledObjects);
            Assert.Equal(0, report.Errors);
            // Converted, nothing is left to say about the full stops.
            var converted = PlannerNamingValidator.Validate(new[] { Group(hyphened), Mesh("Ph2_St05-1_IN_Kerb", hyphened) }, Today);
            Assert.Empty(converted.Issues);
        }

        // The phasing hierarchy as Fix phasing names leaves it: top node, phase groups, layers <PP>_<SS>[-<split>]_<name>.
        private static SceneNodeInfo InLayer(string name, string layer, string group, string top = "Road_Phasing_v.02")
        {
            return new SceneNodeInfo { Name = name, IsMesh = true, Ancestors = new List<string> { layer, group, top } };
        }

        [Fact]
        public void PhasingLayersAreReadByTheirIdAsFixPhasingNamesWritesThem()
        {
            const string group = "_02_Phase1_TM";
            const string subbase = "02_05-1_SubBase_Installation1_29-10-26_03-11-26";
            const string hoarding = "02_02-7_Hoarding_Install_01-10-26_05-02-27";
            const string legacy = "02_02.8_Hoarding_Install_02-10-26_05-02-27";
            const string stray = "06_06_Full_Closure_TBC_07-02-27";
            const string gates = "06_05-2_Site_Entrance_05-02-27";
            const string piling = "01_04_Piling_17-08-26";
            var scene = new List<SceneNodeInfo>
            {
                InLayer("Ph2_St05-1_IN_TR_Subbase_280mm_C_", subbase, group),       // clean, though the order number is 02
                InLayer("Ph2_St02-7_IN_RC_Hrd_001_Ph6_St05-2_RM", hoarding, group), // clean
                InLayer("Ph2_St02-3_IN_RC_Hrd_002", hoarding, group),               // the wrong sub-stage -> warning
                InLayer("Ph3_St05-1_IN_TR_Kerb", subbase, group),                   // the wrong phase -> warning
                InLayer("Ph2_St02-8_IN_RC_Hrd_003", legacy, group),                 // clean apart from the group's full stop
                InLayer("Ph6_St05-2_IN_RC_Hrd_Gates", gates, "_06_Weekend_Closure"),
                new SceneNodeInfo { Name = "Ph6_St06_IN_Barrier", IsMesh = true, Ancestors = new List<string> { stray, "Road_Phasing_v.02" } }, // under the top
                // Not in the hierarchy, read as the Planner reads it: two numbers are phase then stage.
                new SceneNodeInfo { Name = "Ph1_St04_IN_Sheet_Pile_1", IsMesh = true, Ancestors = new List<string> { piling } },   // clean
                new SceneNodeInfo { Name = "Ph2_St04_IN_Sheet_Pile_2", IsMesh = true, Ancestors = new List<string> { piling } },   // the wrong phase -> warning
                new SceneNodeInfo { Name = "Ph1_St01_IN_Sheet_Pile_3", IsMesh = true, Ancestors = new List<string> { piling } }    // the phase as the stage -> warning
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(0, report.Errors);
            var warnings = report.Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
            Assert.Equal(new[] { "Ph2_St02-3_IN_RC_Hrd_002", "Ph3_St05-1_IN_TR_Kerb", "Ph2_St04_IN_Sheet_Pile_2", "Ph1_St01_IN_Sheet_Pile_3", legacy }.OrderBy(s => s, System.StringComparer.Ordinal),
                warnings.Select(w => w.Subject).OrderBy(s => s, System.StringComparer.Ordinal));
            Assert.Contains("neither tag matches its layer '" + hoarding + "' (Ph2_St02-7)", warnings.Single(w => w.Subject == "Ph2_St02-3_IN_RC_Hrd_002").Message);
            Assert.Contains("(Ph2_St05-1)", warnings.Single(w => w.Subject == "Ph3_St05-1_IN_TR_Kerb").Message);
            Assert.Contains("neither tag matches the phase and stage of its group '" + piling + "' (Ph1_St04)", warnings.Single(w => w.Subject == "Ph2_St04_IN_Sheet_Pile_2").Message);
            Assert.Contains("(Ph1_St04)", warnings.Single(w => w.Subject == "Ph1_St01_IN_Sheet_Pile_3").Message);
            Assert.Contains("1 group name(s) write the sub-stage with a full stop", warnings.Single(w => w.Subject == legacy).Message);

            // A full stop after a layer's phase is not a sub-stage: Fix phasing names corrects it with the rest of the ID.
            var phaseDot = PlannerNamingValidator.Validate(new[] { InLayer("Ph6_St06_IN_Barrier", "6.06_Closure_07-02-27", "_06_Weekend") }, Today);
            Assert.Empty(phaseDot.Issues);
        }

        [Fact]
        public void AMeasurementInAGroupNameIsNotAFullStopSubStage()
        {
            const string inLayer = "01_04-2_Hoarding_18-09-26";
            var scene = new List<SceneNodeInfo>
            {
                Mesh("Ph2_St02_IN_Panel_A", "2.4_High_Hoarding_TBC"),            // read as order 2.4, but one digit: a measurement
                Mesh("Ph1_St01_IN_Panel_B", "01_2.4_High_Hoarding_17-08-26"),
                Mesh("Ph3_St03_IN_Panel_C", "03_1.8_x_2.4_Heras_Panel_17-08-26"),
                Mesh("Ph1_St01_IN_Panel_D", "01_10.8_x_2.4_Panel_TBC"),           // two digits, but "_x_": a measurement
                Mesh("Ph1_St01_IN_Panel_E", "1.8_x_2.4_Heras_Panel"),
                new SceneNodeInfo { Name = "Ph1_St04-2_IN_Panel_F", IsMesh = true, Ancestors = new List<string> { "2.4_High_Hoarding", inLayer, "_01_Zone5", "Road_Phasing_v.02" } },
                Mesh("Ph1_St01_IN_Kerb_A", "01__04.2_Kerbs_17-08-26"),           // a stray underscore: still a sub-stage
                Mesh("Ph1_St01_IN_Kerb_B", "01_04.2_Kerbs_B_17-08-26")
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            var groups = Assert.Single(report.Issues, i => i.Message.Contains("group name(s) write the sub-stage with a full stop"));
            Assert.StartsWith("2 group name(s)", groups.Message);
            Assert.EndsWith("Rename them by hand (Fix phasing names\u2026 converts only the phasing layers under 'Road_Phasing_v.02' and the objects directly in them): 01_04.2_Kerbs_B_17-08-26, 01__04.2_Kerbs_17-08-26.", groups.Message);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("with a full stop") && i.Message.Contains("2.4"));
        }

        [Fact]
        public void ObjectsLinkedUnderAnotherNodeInALayerAreLeftToBeRenamedByHand()
        {
            const string layer = "01_04-2_Piles_18-09-26";
            const string group = "_01_Zone5";
            const string top = "Road_Phasing_v.02";
            Func<string, SceneNodeInfo> nested = name => new SceneNodeInfo { Name = name, IsMesh = true, Ancestors = new List<string> { "Piles_Group", layer, group, top } };
            var scene = new List<SceneNodeInfo>
            {
                nested("Ph1_St04.2_IN_Sheet_Pile_1"),
                nested("Ph1_St04.2_IN_Sheet_Pile_2"),
                InLayer("Ph1_St04.2_IN_Sheet_Pile_3", layer, group),
                nested("Ph1_St05_IN_Sheet_Pile_4"),                 // the wrong stage, linked under a group in the layer
                InLayer("Ph1_St05_IN_Sheet_Pile_5", layer, group)   // the wrong stage, directly in the layer
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            var objects = Assert.Single(report.Issues, i => i.Message.Contains("object name(s) write the sub-stage with a full stop"));
            Assert.Equal("Ph1_St04.2_IN_Sheet_Pile_1", objects.Subject);
            Assert.EndsWith("Fix phasing names\u2026 on the exporter window converts 1 of them: Ph1_St04.2_IN_Sheet_Pile_3; rename the other 2 by hand (Fix phasing names\u2026 converts only the phasing layers under 'Road_Phasing_v.02' and the objects directly in them): Ph1_St04.2_IN_Sheet_Pile_1, Ph1_St04.2_IN_Sheet_Pile_2.", objects.Message);
            Assert.Contains("It is linked under 'Piles_Group' inside the layer, and Fix phasing names\u2026 leaves such objects as they are", report.Issues.Single(i => i.Subject == "Ph1_St05_IN_Sheet_Pile_4").Message);
            Assert.Contains("Fix phasing names\u2026 gives the object its layer's tag", report.Issues.Single(i => i.Subject == "Ph1_St05_IN_Sheet_Pile_5").Message);

            // All of them directly in phasing layers: the fixer converts them all.
            var direct = PlannerNamingValidator.Validate(new[] { InLayer("Ph1_St04.2_IN_Sheet_Pile_3", layer, group) }, Today);
            Assert.EndsWith("Fix phasing names\u2026 on the exporter window converts them: Ph1_St04.2_IN_Sheet_Pile_3.", Assert.Single(direct.Issues).Message);
        }

        [Fact]
        public void LayersOfASecondPhasingTopNodeAreRenamedByHand()
        {
            // Fix phasing names corrects only the first top node with "phasing" in its name.
            const string first = "Road_Phasing_v.02";
            const string second = "Old_Phasing_Copy";
            const string layer = "01_04.2_Piles_18-09-26";
            const string copy = "01_05.2_Piles_Copy_19-09-26";
            var scene = new List<SceneNodeInfo>
            {
                InLayer("Ph1_St04.2_IN_Sheet_Pile_1", layer, "_01_Zone5", first),
                InLayer("Ph1_St05.2_IN_Sheet_Pile_2", copy, "_01_Zone5_Copy", second),
                InLayer("Ph1_St06_IN_Sheet_Pile_3", copy, "_01_Zone5_Copy", second),   // the wrong stage, under the second top node
                InLayer("Ph1_St06_IN_Sheet_Pile_4", layer, "_01_Zone5", first)         // the wrong stage, under the first
            };
            foreach (var top in new[] { null, first })
            {
                var report = PlannerNamingValidator.Validate(scene, Today, top);
                foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
                Assert.EndsWith("Fix phasing names\u2026 on the exporter window converts 1 of them: " + layer
                    + "; rename the other 1 by hand (Fix phasing names\u2026 converts only the phasing layers under '" + first + "' and the objects directly in them): " + copy + ".",
                    Assert.Single(report.Issues, i => i.Message.Contains("group name(s) write the sub-stage with a full stop")).Message);
                Assert.EndsWith("converts 1 of them: Ph1_St04.2_IN_Sheet_Pile_1; rename the other 1 by hand (Fix phasing names\u2026 converts only the phasing layers under '" + first
                    + "' and the objects directly in them): Ph1_St05.2_IN_Sheet_Pile_2.",
                    Assert.Single(report.Issues, i => i.Message.Contains("object name(s) write the sub-stage with a full stop")).Message);
                var byHand = report.Issues.Single(i => i.Subject == "Ph1_St06_IN_Sheet_Pile_3").Message;
                Assert.Contains("The layer is under '" + second + "', and Fix phasing names\u2026 corrects only '" + first + "': correct the Ph and St numbers by hand", byHand);
                Assert.DoesNotContain("gives the object its layer's tag", byHand);
                Assert.Contains("Fix phasing names\u2026 gives the object its layer's tag", report.Issues.Single(i => i.Subject == "Ph1_St06_IN_Sheet_Pile_4").Message);
            }

            // The fixer's own choice wins (here the second), and with no top node to correct, everything is by hand.
            var chosen = PlannerNamingValidator.Validate(scene, Today, second);
            Assert.Contains("converts 1 of them: " + copy + "; rename the other 1 by hand (Fix phasing names\u2026 converts only the phasing layers under '" + second + "'",
                chosen.Issues.Single(i => i.Message.Contains("group name(s) write the sub-stage with a full stop")).Message);
            var none = PlannerNamingValidator.Validate(scene, Today, string.Empty);
            Assert.Contains("Rename them by hand (Fix phasing names\u2026 converts only the phasing layers and the objects directly in them)",
                none.Issues.Single(i => i.Message.Contains("group name(s) write the sub-stage with a full stop")).Message);
            Assert.DoesNotContain(none.Issues, i => i.Message.Contains("gives the object its layer's tag"));
            Assert.Contains("The layer is under '" + first + "', which Fix phasing names\u2026 does not correct: correct the Ph and St numbers by hand",
                none.Issues.Single(i => i.Subject == "Ph1_St06_IN_Sheet_Pile_4").Message);
        }

        [Fact]
        public void StagesWithoutALayerAreOneWarningForRemovalsAndOneForInstalls()
        {
            const string top = "Road_Phasing_v.02";
            const string hoarding = "02_02-7_Hoarding_Install_01-10-26_05-02-27";
            const string kerbs = "02_05_Kerbs_01-11-26";
            const string removal = "02_06-1_Removal_10-11-26";
            const string loose = "08_Loose_Group_12-11-26";      // outside the hierarchy, one number: its phase from its objects
            var scene = new List<SceneNodeInfo>
            {
                InLayer("Ph2_St02-7_IN_Hrd_A_Ph2_St05-2_RM", hoarding, "_02_TM", top),  // a whole-stage layer 02_05 dates St05-2: clean
                InLayer("Ph2_St02-7_IN_Hrd_B_Ph2_St06-1_RM", hoarding, "_02_TM", top),  // clean
                InLayer("Ph2_St02-7_IN_Hrd_C_Ph2_St06-2_RM", hoarding, "_02_TM", top),  // 02_06-1 is not 06-2 -> removal warning
                InLayer("Ph2_St02-7_IN_Hrd_D_Ph1_St23_RM", hoarding, "_02_TM", top),    // no phase 1 layer at all -> removal warning
                InLayer("Ph2_St02-7_IN_Hrd_E_Ph1_St23_RM", hoarding, "_02_TM", top),    // the same tag again: listed once
                InLayer("Ph2_St02-7_IN_Hrd_F_Ph8_St08_RM", hoarding, "_02_TM", top),    // 08_Loose_Group is phase 8 by its objects: clean
                InLayer("Ph2_St05_IN_Kerb_A", kerbs, "_02_TM", top),                    // clean
                InLayer("Ph2_St06-1_RM_Old_Kerb_Ph3_St02_IN", removal, "_02_TM", top),  // an install tag with no layer -> install warning
                InLayer("Ph2_St06-1_RM_Old_Kerb_Ph0_St00_IN", removal, "_02_TM", top),  // phase 0 is there before the works: nothing to say
                InLayer("Ph2_St09_IN_Wrong_Ph1_St40_RM", kerbs, "_02_TM", top),         // neither tag is the layer's: the mismatch, and the removal with no layer
                new SceneNodeInfo { Name = "Ph8_St08_IN_Loose_A", IsMesh = true, Ancestors = new List<string> { loose } },
                new SceneNodeInfo { Name = "Ph8_St08_IN_Loose_B", IsMesh = true, Ancestors = new List<string> { loose } },
                new SceneNodeInfo { Name = "Ph9_St08_IN_Loose_C_Ph1_St50_RM", IsMesh = true, Ancestors = new List<string> { loose } }, // most leading tags with stage 8 carry phase 8
                new SceneNodeInfo { Name = "Ph1_St60_IN_Unfiled_Ph1_St61_RM", IsMesh = true }   // unfiled: reported as such only
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(0, report.Errors);
            var removals = Assert.Single(report.Issues, i => i.Message.Contains("have a removal tag whose phase and stage have no layer in this file"));
            Assert.Equal(NamingSeverity.Warning, removals.Severity);
            Assert.Equal("Ph2_St02-7_IN_Hrd_C_Ph2_St06-2_RM", removals.Subject);
            Assert.Equal("5 object(s) have a removal tag whose phase and stage have no layer in this file (Ph2_St06-2, Ph1_St23, Ph1_St40, Ph1_St50), so the Planner cannot date that work: it puts it after the end of the works, marked TBC. "
                + "Give each tag the stage the object comes out at, or add the layer for that stage: Ph2_St02-7_IN_Hrd_C_Ph2_St06-2_RM, Ph2_St02-7_IN_Hrd_D_Ph1_St23_RM, Ph2_St02-7_IN_Hrd_E_Ph1_St23_RM, Ph2_St09_IN_Wrong_Ph1_St40_RM, Ph9_St08_IN_Loose_C_Ph1_St50_RM.",
                removals.Message);
            var installs = Assert.Single(report.Issues, i => i.Message.Contains("have an install tag whose phase and stage have no layer in this file"));
            Assert.Equal("1 object(s) have an install tag whose phase and stage have no layer in this file (Ph3_St02), so the Planner cannot date that work: it puts it at the start of the project, marked TBC. "
                + "Give each tag the stage the object goes in at, or add the layer for that stage: Ph2_St06-1_RM_Old_Kerb_Ph3_St02_IN.", installs.Message);
            Assert.Contains(report.Issues, i => i.Subject == "Ph2_St09_IN_Wrong_Ph1_St40_RM" && i.Message.Contains("neither tag matches its layer"));
            Assert.Contains(report.Issues, i => i.Subject == "Ph1_St60_IN_Unfiled_Ph1_St61_RM" && i.Message.Contains("unfiled"));
            Assert.Equal(4, report.Warnings); // the two above, the mismatch and the unfiled object

            // Ten names at most, then how many more.
            var many = new List<SceneNodeInfo>();
            for (int i = 1; i <= 12; i++)
            {
                many.Add(InLayer("Ph2_St02-7_IN_Hrd_" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "_Ph1_St23_RM", hoarding, "_02_TM", top));
            }
            Assert.EndsWith("Ph2_St02-7_IN_Hrd_10_Ph1_St23_RM and 2 more.", Assert.Single(PlannerNamingValidator.Validate(many, Today).Issues).Message);
        }

        /// <summary>The start of how a warning says a group named with one number got its phase.</summary>
        private const string OneNumberRule = "named with one number, the group takes the phase most of its objects' leading tags with its stage carry, "
            + "and looks at the trailing tags only when no leading tag has its stage; here ";

        /// <summary>
        /// A group named with one number takes the phase most of its objects' leading tags with its stage carry. An object
        /// in it whose tag has its stage in another phase is not its work: when another dated layer has that phase and
        /// stage, the Planner dates the object from that layer (it stays in the group's folder), which is warned. With no
        /// such layer the Planner keeps it on the group's dates, as meant, and nothing is said.
        /// </summary>
        [Fact]
        public void AnObjectOfAnotherPhaseInAOneNumberGroupIsWarnedWhenAnotherLayerDatesIt()
        {
            const string kerbs = "07_Kerbs_01-12-26_10-12-26";
            var scene = new List<SceneNodeInfo>
            {
                Mesh("Ph1_St07_IN_Kerb_A", kerbs),
                Mesh("Ph1_St07_IN_Kerb_B", kerbs),
                Mesh("Ph2_St07_IN_Kerb_C", kerbs),                     // phase 2 in a phase 1 group
                Mesh("Ph2_St07_IN_Other_D", "07_Other_01-03-27"),      // a one-number group of phase 2, stage 7
                Mesh("Ph2_St07_IN_Late_E", "02_07_Later_05-03-27")     // a later layer of the same phase and stage
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            var warning = Assert.Single(report.Issues);
            Assert.Equal(NamingSeverity.Warning, warning.Severity);
            Assert.Equal("Ph2_St07_IN_Kerb_C", warning.Subject);
            Assert.Equal("'Ph2_St07_IN_Kerb_C': neither tag matches the phase and stage of its group '" + kerbs + "' (Ph1_St07: " + OneNumberRule
                + "it comes from 'Ph1_St07_IN_Kerb_A' and 'Ph1_St07_IN_Kerb_B', 2 of the 3 objects whose leading tag has its stage), "
                + "so the group cannot own this object's work: the Planner dates it from '07_Other_01-03-27' (Ph2_St07), the layer for its leading tag, though it stays in the group's folder. "
                + "Check the Ph numbers, or move the object to the group for its phase and stage.", warning.Message);

            // A two-number layer dates it likewise.
            var twoNumber = PlannerNamingValidator.Validate(new[] { scene[0], scene[1], scene[2], Mesh("Ph2_St07_IN_Other_D", "02_07_Other_01-03-27") }, Today);
            Assert.Contains("the Planner dates it from '02_07_Other_01-03-27' (Ph2_St07)", Assert.Single(twoNumber.Issues).Message);

            // A tie goes to the lower phase, so the phase 2 object is the one dated elsewhere, and the message says so.
            var tie = PlannerNamingValidator.Validate(new[] { Mesh("Ph2_St05_IN_a", "05_X_18-09-26"), Mesh("Ph1_St05_IN_b", "05_X_18-09-26"), Mesh("Ph2_St05_IN_c", "02_05_Y_01-12-26") }, Today);
            Assert.Equal("Ph2_St05_IN_a", Assert.Single(tie.Issues).Subject);
            Assert.Contains("(Ph1_St05: " + OneNumberRule + "it comes from 'Ph1_St05_IN_b', 1 of the 2 objects whose leading tag has its stage, the lower phase on a tie), ",
                tie.Issues[0].Message);
            Assert.EndsWith(" Check the Ph numbers, or move the object to the group for its phase and stage.", tie.Issues[0].Message);

            // No other layer for phase 2, stage 7, or only an undated one: the Planner keeps the object on its group's dates.
            Assert.Empty(PlannerNamingValidator.Validate(new[] { scene[0], scene[1], scene[2] }, Today).Issues);
            Assert.Empty(PlannerNamingValidator.Validate(new[] { scene[0], scene[1], scene[2], Mesh("Ph2_St07_IN_Other_D", "07_Other_TBC") }, Today).Issues);
        }

        /// <summary>
        /// Only the trailing tag has the stage of a group named with one number, in another phase than the group's: the
        /// group does not own that work (the removal), and the Planner reads the object as one whose work is not the
        /// group's. It dates the removal from the layer with its phase and stage, and the leading tag's work from its own
        /// layer; with no dated layer for the leading tag, it spreads that work over the group's dates as the group's own,
        /// which nothing else reports (the review's R11 and R12). Always warned, worded around the removal tag.
        /// </summary>
        [Fact]
        public void OnlyTheTrailingTagHavingTheStageOfAOneNumberGroupInAnotherPhaseIsWarned()
        {
            const string kerbs = "07_Kerbs_01-12-26_10-12-26";
            const string other = "02_07_Other_01-03-27";
            const string advice = " Check the Ph numbers, or move the object to the group for its phase and stage.";
            const string kerbsText = "' but not its phase (Ph1_St07: " + OneNumberRule
                + "it comes from 'Ph1_St07_IN_Kerb_A' and 'Ph1_St07_IN_Kerb_B', every object whose leading tag has its stage), so the group does not own the removal: ";

            // R11: the removal is dated from 02_07_Other, and with no layer for Ph2_St03 the install takes the group's dates.
            var r11 = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St07_IN_Kerb_A", kerbs),
                Mesh("Ph1_St07_IN_Kerb_B", kerbs),
                Mesh("Ph2_St03_IN_Kerb_X_Ph2_St07_RM", kerbs),
                Mesh("Ph2_St07_IN_Other_D", other)
            }, Today);
            foreach (var issue in r11.Issues) output.WriteLine(issue.ToString());
            var warning = Assert.Single(r11.Issues);
            Assert.Equal(NamingSeverity.Warning, warning.Severity);
            Assert.Equal("Ph2_St03_IN_Kerb_X_Ph2_St07_RM", warning.Subject);
            Assert.Equal("'Ph2_St03_IN_Kerb_X_Ph2_St07_RM': its removal tag Ph2_St07 has the stage of its group '" + kerbs + kerbsText
                + "the Planner dates the removal from '" + other + "' (Ph2_St07); as no dated layer has Ph2_St03, the Planner spreads the leading tag's work over the group's dates as the group's own work, "
                + "rather than putting it at the start of the project, marked TBC." + advice, warning.Message);

            // R12: a phase 0 install, which the Planner would put at the start of the project, takes the group's dates.
            const string hoarding = "06-2_Hoarding_Removal_20-10-26_23-10-26";
            const string phase2 = "02_06-2_Phase2_Hoarding_Removal_01-03-27";
            var r12 = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph0_St00_IN_Hrd_A_Ph1_St06-2_RM", hoarding),
                Mesh("Ph0_St00_IN_Hrd_B_Ph1_St06-2_RM", hoarding),
                Mesh("Ph0_St00_IN_Hrd_C_Ph2_St06-2_RM", hoarding),
                Mesh("Ph2_St06-2_RM_Other_Hrd", phase2)
            }, Today);
            foreach (var issue in r12.Issues) output.WriteLine(issue.ToString());
            warning = Assert.Single(r12.Issues);
            Assert.Equal("Ph0_St00_IN_Hrd_C_Ph2_St06-2_RM", warning.Subject);
            Assert.Equal("'Ph0_St00_IN_Hrd_C_Ph2_St06-2_RM': its removal tag Ph2_St06-2 has the stage of its group '" + hoarding
                + "' but not its phase (Ph1_St06-2: " + OneNumberRule + "no leading tag has its stage, and it comes from 2 of the 3 objects whose trailing tag has it), so the group does not own the removal: "
                + "the Planner dates the removal from '" + phase2 + "' (Ph2_St06-2); as no dated layer has Ph0_St00, the Planner spreads the leading tag's work over the group's dates as the group's own work, "
                + "rather than putting it at the start of the project." + advice, warning.Message);

            // The leading tag dated by its own layer: the warning is about the removal only.
            var leadDated = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St07_IN_Kerb_A", kerbs),
                Mesh("Ph1_St07_IN_Kerb_B", kerbs),
                Mesh("Ph2_St03_IN_Kerb_Z_Ph2_St07_RM", kerbs),
                Mesh("Ph2_St07_IN_Other_D", other),
                Mesh("Ph2_St03_IN_Base_E", "02_03_Base_01-11-26")
            }, Today);
            foreach (var issue in leadDated.Issues) output.WriteLine(issue.ToString());
            Assert.Equal("'Ph2_St03_IN_Kerb_Z_Ph2_St07_RM': its removal tag Ph2_St07 has the stage of its group '" + kerbs + kerbsText
                + "the Planner dates the removal from '" + other + "' (Ph2_St07); the leading tag's work (Ph2_St03) is dated from '02_03_Base_01-11-26', its own layer, as usual." + advice,
                Assert.Single(leadDated.Issues).Message);

            // No layer for the removal either: it is also in the warning on removal tags with no layer, as the Planner
            // lists it as unresolved.
            var noLayer = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St07_IN_Kerb_A", kerbs),
                Mesh("Ph1_St07_IN_Kerb_B", kerbs),
                Mesh("Ph2_St03_IN_Kerb_Y_Ph3_St07_RM", kerbs)
            }, Today);
            foreach (var issue in noLayer.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(2, noLayer.Warnings);
            Assert.Equal("'Ph2_St03_IN_Kerb_Y_Ph3_St07_RM': its removal tag Ph3_St07 has the stage of its group '" + kerbs + kerbsText
                + "no dated layer has Ph3_St07 either, so the Planner puts the removal after the end of the works, marked TBC; as no dated layer has Ph2_St03, the Planner spreads the leading tag's work over the group's dates as the group's own work, "
                + "rather than putting it at the start of the project, marked TBC." + advice,
                noLayer.Issues.Single(i => i.Message.StartsWith("'Ph2_St03_IN_Kerb_Y_Ph3_St07_RM'", StringComparison.Ordinal)).Message);
            Assert.Contains(noLayer.Issues, i => i.Message.StartsWith("1 object(s) have a removal tag whose phase and stage have no layer in this file (Ph3_St07)", StringComparison.Ordinal));
            Assert.DoesNotContain(noLayer.Issues, i => i.Message.Contains("have an install tag")); // the install takes the group's dates

            // Only an undated layer for the removal: the same warning, and the note on undated layers.
            var undated = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St07_IN_Kerb_A", kerbs),
                Mesh("Ph1_St07_IN_Kerb_B", kerbs),
                Mesh("Ph2_St03_IN_Kerb_V_Ph2_St07_RM", kerbs),
                Mesh("Ph2_St07_IN_Other_D", "02_07_Other_TBC")
            }, Today);
            foreach (var issue in undated.Issues) output.WriteLine(issue.ToString());
            Assert.Contains("so the group does not own the removal: no dated layer has Ph2_St07 either, so the Planner puts the removal after the end of the works, marked TBC; ",
                undated.Issues.Single(i => i.Severity == NamingSeverity.Warning).Message);
            Assert.Contains("only an undated layer has (Ph2_St07; 02_07_Other_TBC)", undated.Issues.Single(i => i.Severity == NamingSeverity.Note).Message);

            // An install trailing a removal: worded around the install, and the removal the group dates is said so.
            const string build = "05_Build_01-11-26_10-11-26";
            var install = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St05_IN_Wall_A", build),
                Mesh("Ph1_St05_IN_Wall_B", build),
                Mesh("Ph1_St03_RM_Old_Fence_Ph2_St05_IN", build)
            }, Today);
            foreach (var issue in install.Issues) output.WriteLine(issue.ToString());
            Assert.Equal("'Ph1_St03_RM_Old_Fence_Ph2_St05_IN': its install tag Ph2_St05 has the stage of its group '" + build
                + "' but not its phase (Ph1_St05: " + OneNumberRule
                + "it comes from 'Ph1_St05_IN_Wall_A' and 'Ph1_St05_IN_Wall_B', every object whose leading tag has its stage), so the group does not own the install: "
                + "no dated layer has Ph2_St05 either, so the Planner puts the install at the start of the project, marked TBC; as no dated layer has Ph1_St03, the Planner spreads the leading tag's work over the group's dates as the group's own work, "
                + "rather than putting it after the end of the works, marked TBC." + advice,
                install.Issues.Single(i => i.Message.StartsWith("'Ph1_St03_RM_Old_Fence_Ph2_St05_IN'", StringComparison.Ordinal)).Message);
            Assert.Contains(install.Issues, i => i.Message.StartsWith("1 object(s) have an install tag whose phase and stage have no layer in this file (Ph2_St05)", StringComparison.Ordinal));

            // The control (R11b): the trailing tag in the group's phase is the group's, and only the install with no layer is said.
            var control = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St07_IN_Kerb_A", kerbs),
                Mesh("Ph1_St07_IN_Kerb_B", kerbs),
                Mesh("Ph2_St03_IN_Kerb_X_Ph1_St07_RM", kerbs),
                Mesh("Ph2_St07_IN_Other_D", other)
            }, Today);
            Assert.StartsWith("1 object(s) have an install tag whose phase and stage have no layer in this file (Ph2_St03)", Assert.Single(control.Issues).Message);
        }

        /// <summary>
        /// A group named with one number takes its phase, as the Planner does, from its objects' leading tags with its
        /// stage, and from their trailing tags only when no leading tag has its stage: one object whose leading tag has the
        /// stage sets the phase on its own, whatever the others' trailing tags carry. The warnings say how the phase was
        /// read and, when leading tags set it, name the objects that did; when those are fewer than the objects warned in
        /// the group for another phase, the advice points at them, the likely slip (the review's M1 and M2: a removal-only
        /// object with a phase slip made three correctly tagged hoardings look wrong and was never named).
        /// </summary>
        [Fact]
        public void TheObjectsThatSetAOneNumberGroupsPhaseAreNamedAndSuspectedWhenFewer()
        {
            const string hoarding = "06-2_Hoarding_Removal_20-10-26_23-10-26";
            const string existing = "00_Existing_01-06-26";
            var hoardings = new[]
            {
                Mesh("Ph0_St00_IN_Hrd_A_Ph1_St06-2_RM", hoarding),
                Mesh("Ph0_St00_IN_Hrd_B_Ph1_St06-2_RM", hoarding),
                Mesh("Ph0_St00_IN_Hrd_C_Ph1_St06-2_RM", hoarding)
            };
            var slip = Mesh("Ph2_St06-2_RM_Old_Barrier", hoarding);
            const string phaseText = "' but not its phase (Ph2_St06-2: " + OneNumberRule
                + "it comes from 'Ph2_St06-2_RM_Old_Barrier', the only object whose leading tag has its stage), so the group does not own the removal: "
                + "no dated layer has Ph1_St06-2 either, so the Planner puts the removal after the end of the works, marked TBC; ";
            const string advice = " Check the Ph numbers: fewer objects set the group's phase (1) than are warned in it for another (3), so the slip may be in "
                + "'Ph2_St06-2_RM_Old_Barrier' rather than in this object; otherwise move the object to the group for its phase and stage.";

            // M1: a dated 00 layer dates the leading tags' work.
            var m1 = PlannerNamingValidator.Validate(hoardings.Concat(new[] { slip, Mesh("Ph0_St00_IN_Base", existing) }), Today);
            foreach (var issue in m1.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(0, m1.Errors);
            Assert.Equal(4, m1.Warnings);
            Assert.Equal(0, m1.Notes);
            foreach (var hrd in hoardings)
            {
                Assert.Equal("'" + hrd.Name + "': its removal tag Ph1_St06-2 has the stage of its group '" + hoarding + phaseText
                    + "the leading tag's work (Ph0_St00) is dated from '" + existing + "', its own layer, as usual." + advice,
                    m1.Issues.Single(i => i.Message.StartsWith("'" + hrd.Name + "'", StringComparison.Ordinal)).Message);
            }
            Assert.Contains(m1.Issues, i => i.Message.StartsWith("3 object(s) have a removal tag whose phase and stage have no layer in this file (Ph1_St06-2)", StringComparison.Ordinal));

            // M2: no 00 layer, so the Planner takes the leading tags' work as the group's own.
            var m2 = PlannerNamingValidator.Validate(hoardings.Concat(new[] { slip }), Today);
            foreach (var issue in m2.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(0, m2.Errors);
            Assert.Equal(4, m2.Warnings);
            foreach (var hrd in hoardings)
            {
                Assert.Equal("'" + hrd.Name + "': its removal tag Ph1_St06-2 has the stage of its group '" + hoarding + phaseText
                    + "as no dated layer has Ph0_St00, the Planner spreads the leading tag's work over the group's dates as the group's own work, rather than putting it at the start of the project." + advice,
                    m2.Issues.Single(i => i.Message.StartsWith("'" + hrd.Name + "'", StringComparison.Ordinal)).Message);
            }

            // M3, the control: the removal-only object in phase 1 gives the group the phase the hoardings carry.
            Assert.Empty(PlannerNamingValidator.Validate(hoardings.Concat(new[] { Mesh("Ph1_St06-2_RM_Old_Barrier", hoarding) }), Today).Issues);

            // The leading-tag message is weighed and worded likewise: two objects set phase 2, and three are warned for
            // phase 1, one by its leading tag and two by their trailing tags.
            const string kerbs = "07_Kerbs_01-12-26_10-12-26";
            const string phase1 = "01_07_Kerbs_Phase1_01-11-26";
            var mixed = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph2_St07_IN_Kerb_X1", kerbs),
                Mesh("Ph2_St07_IN_Kerb_X2", kerbs),
                Mesh("Ph1_St07_IN_Kerb_W", kerbs),
                Mesh("Ph0_St00_IN_Kerb_Y_Ph1_St07_RM", kerbs),
                Mesh("Ph0_St00_IN_Kerb_Z_Ph1_St07_RM", kerbs),
                Mesh("Ph1_St07_IN_Other", phase1)
            }, Today);
            foreach (var issue in mixed.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(3, mixed.Warnings);
            const string setBy = "(Ph2_St07: " + OneNumberRule + "it comes from 'Ph2_St07_IN_Kerb_X1' and 'Ph2_St07_IN_Kerb_X2', 2 of the 3 objects whose leading tag has its stage)";
            const string mixedAdvice = " Check the Ph numbers: fewer objects set the group's phase (2) than are warned in it for another (3), so the slip may be in "
                + "'Ph2_St07_IN_Kerb_X1' and 'Ph2_St07_IN_Kerb_X2' rather than in this object; otherwise move the object to the group for its phase and stage.";
            Assert.Equal("'Ph1_St07_IN_Kerb_W': neither tag matches the phase and stage of its group '" + kerbs + "' " + setBy
                + ", so the group cannot own this object's work: the Planner dates it from '" + phase1 + "' (Ph1_St07), the layer for its leading tag, though it stays in the group's folder." + mixedAdvice,
                mixed.Issues.Single(i => i.Subject == "Ph1_St07_IN_Kerb_W").Message);
            Assert.Equal("'Ph0_St00_IN_Kerb_Y_Ph1_St07_RM': its removal tag Ph1_St07 has the stage of its group '" + kerbs + "' but not its phase " + setBy
                + ", so the group does not own the removal: the Planner dates the removal from '" + phase1 + "' (Ph1_St07); as no dated layer has Ph0_St00, the Planner spreads the leading tag's work "
                + "over the group's dates as the group's own work, rather than putting it at the start of the project." + mixedAdvice,
                mixed.Issues.Single(i => i.Subject == "Ph0_St00_IN_Kerb_Y_Ph1_St07_RM").Message);

            // Five names at most, then how many more.
            var many = new List<SceneNodeInfo>();
            for (int i = 1; i <= 6; i++)
            {
                many.Add(Mesh("Ph1_St07_IN_K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), kerbs));
            }
            many.Add(Mesh("Ph1_St03_IN_R1_Ph2_St07_RM", kerbs));
            many.Add(Mesh("Ph2_St07_IN_Other", "02_07_Other_01-03-27"));
            many.Add(Mesh("Ph1_St03_IN_Base", "01_03_Base_01-11-26"));
            var capped = Assert.Single(PlannerNamingValidator.Validate(many, Today).Issues).Message;
            Assert.Contains("it comes from 'Ph1_St07_IN_K1', 'Ph1_St07_IN_K2', 'Ph1_St07_IN_K3', 'Ph1_St07_IN_K4', 'Ph1_St07_IN_K5' and 1 more, every object whose leading tag has its stage)", capped);
            Assert.EndsWith(" Check the Ph numbers, or move the object to the group for its phase and stage.", capped); // six set it, one is warned

            // With no leading tag of its stage, the trailing tags give the phase (here a tie, to the lower phase), and the
            // objects are only counted: the one warned is among the fewer.
            var trailing = Assert.Single(PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St03_IN_Kerb_A_Ph1_St07_RM", "07_Kerb_Removal_01-12-26_10-12-26"),
                Mesh("Ph1_St03_IN_Kerb_B_Ph2_St07_RM", "07_Kerb_Removal_01-12-26_10-12-26"),
                Mesh("Ph1_St03_IN_Kerb_C", "01_03_Kerbs_01-11-26"),
                Mesh("Ph2_St07_IN_Other_D", "02_07_Other_01-03-27")
            }, Today).Issues).Message;
            Assert.Contains("(Ph1_St07: " + OneNumberRule + "no leading tag has its stage, and it comes from 1 of the 2 objects whose trailing tag has it, the lower phase on a tie), ", trailing);
            Assert.EndsWith(" Check the Ph numbers, or move the object to the group for its phase and stage.", trailing);
        }

        /// <summary>
        /// Everything in a TBC group is TBC, the group's own work included: when the Planner takes the leading tag's work as
        /// the group's own, a group with dates spreads it over them, TBC, and a group with no dates puts it at the start of
        /// the project, TBC (the review's A1).
        /// </summary>
        [Fact]
        public void InATbcGroupTheLeadingTagsWorkTakenAsTheGroupsOwnIsTbc()
        {
            Func<string, string> warn = group => Assert.Single(PlannerNamingValidator.Validate(new[]
            {
                Mesh("Ph1_St07_IN_Kerb_A", group),
                Mesh("Ph1_St07_IN_Kerb_B", group),
                Mesh("Ph2_St03_IN_Kerb_X_Ph2_St07_RM", group),
                Mesh("Ph2_St07_IN_Other_D", "02_07_Other_01-03-27")
            }, Today).Issues).Message;
            const string removal = "so the group does not own the removal: the Planner dates the removal from '02_07_Other_01-03-27' (Ph2_St07); ";
            Assert.Contains(removal + "as no dated layer has Ph2_St03, the Planner takes the leading tag's work as the group's own work, and as the group has no dates yet, "
                + "puts it at the start of the project, marked TBC like the rest of the group. Check the Ph numbers", warn("07_Kerbs_TBC"));
            Assert.Contains(removal + "as no dated layer has Ph2_St03, the Planner spreads the leading tag's work over the group's dates as the group's own work, "
                + "marked TBC like the rest of the group. Check the Ph numbers", warn("07_Kerbs_TBC_01-12-26_10-12-26"));
            Assert.Contains(removal + "as no dated layer has Ph2_St03, the Planner spreads the leading tag's work over the group's dates as the group's own work, "
                + "rather than putting it at the start of the project, marked TBC. Check the Ph numbers", warn("07_Kerbs_01-12-26_10-12-26"));
        }

        /// <summary>
        /// The Planner dates another stage's work only from a layer with a start date, and reads a group as a layer only
        /// when it holds a tagged object. A tag whose only layer has no dates yet gets a note of its own (the TBC is the
        /// layer's); a group with the tag's phase and stage that holds no tagged object does not count, and is named.
        /// </summary>
        [Fact]
        public void UndatedAndEmptyLayersDoNotDateAnotherStagesWork()
        {
            const string top = "Work_Phasing";
            const string piles = "01_04-1_Piles_18-09-26_23-10-26";
            var undated = PlannerNamingValidator.Validate(new[]
            {
                InLayer("Ph1_St04-1_IN_Pile_1_Ph1_St05_RM", piles, "_01_Zone5", top),
                InLayer("Ph1_St05_IN_Hoarding", "01_05_Hoarding_TBC", "_01_Zone5", top),
                InLayer("Ph1_St04-1_RM_Old_Ph1_St05_IN", piles, "_01_Zone5", top)
            }, Today);
            foreach (var issue in undated.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(0, undated.Warnings);
            Assert.Equal(2, undated.Notes);
            Assert.Equal("1 object(s) have a removal tag whose phase and stage only an undated layer has (Ph1_St05; 01_05_Hoarding_TBC), so the Planner cannot date that work until the layer has dates: "
                + "it puts it after the end of the works, marked TBC: Ph1_St04-1_IN_Pile_1_Ph1_St05_RM.", undated.Issues.Single(i => i.Subject == "Ph1_St04-1_IN_Pile_1_Ph1_St05_RM").Message);
            Assert.Contains("have an install tag whose phase and stage only an undated layer has (Ph1_St05; 01_05_Hoarding_TBC)", undated.Issues.Single(i => i.Subject == "Ph1_St04-1_RM_Old_Ph1_St05_IN").Message);

            var empty = PlannerNamingValidator.Validate(new[]
            {
                Mesh("Untagged_Box", "03_09_Context_01-02-27"),
                Mesh("Ph3_St08_IN_Wall_Ph3_St09_RM", "03_08_Build_01-01-27")
            }, Today);
            foreach (var issue in empty.Issues) output.WriteLine(issue.ToString());
            var warning = Assert.Single(empty.Issues, i => i.Severity == NamingSeverity.Warning);
            Assert.Equal("1 object(s) have a removal tag whose phase and stage have no layer in this file (Ph3_St09), so the Planner cannot date that work: it puts it after the end of the works, marked TBC. "
                + "Give each tag the stage the object comes out at, or add the layer for that stage: Ph3_St08_IN_Wall_Ph3_St09_RM. "
                + "The group(s) 03_09_Context_01-02-27 have such a phase and stage but hold no tagged object, so the Planner does not read them as layers.", warning.Message);
        }

        /// <summary>
        /// An object in a phasing layer whose ID only Fix phasing names reads (a full stop after the phase) is unfiled for
        /// the Planner; the hint is about the layer's ID, which the fixer corrects (or, under a second top node, is renamed
        /// by hand), not the generic "needs an order number and dates".
        /// </summary>
        [Fact]
        public void AnObjectInALayerOnlyTheFixerReadsIsToldAboutTheLayersId()
        {
            const string layer = "06.01.2_Traffic_Islands_Install_05-02-27";
            const string copy = "06.02.1_Traffic_Islands_Copy_06-02-27";
            var scene = new List<SceneNodeInfo>
            {
                InLayer("Ph6_St01.2_IN_Kerbs", layer, "_06_Weekend", "Road_Phasing_v.02"),
                InLayer("Ph6_St02.1_IN_Infill", copy, "_06_Weekend_Copy", "Old_Phasing_Copy")
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            Assert.Equal(2, report.UnfiledObjects);
            Assert.Equal("'Ph6_St01.2_IN_Kerbs' is not inside a dated group, so the Planner lists it as unfiled and cannot schedule it. Its layer '" + layer
                + "' has a phase and stage ID the Planner cannot read (a full stop after the phase number): Fix phasing names\u2026 on the exporter window corrects it.",
                report.Issues.Single(i => i.Subject == "Ph6_St01.2_IN_Kerbs" && i.Message.Contains("unfiled")).Message);
            Assert.EndsWith("Its layer '" + copy + "' has a phase and stage ID the Planner cannot read (a full stop after the phase number): rename it by hand to 06_02-1_<name>_DD-MM-YY[_DD-MM-YY] "
                + "(Fix phasing names\u2026 corrects only the layers under 'Road_Phasing_v.02').", report.Issues.Single(i => i.Subject == "Ph6_St02.1_IN_Infill" && i.Message.Contains("unfiled")).Message);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("needs an order number and dates"));
        }

        // A node tree { "roots", "nodes": [ { "name", "children" } ] } as the naming check sees it: every node with its
        // ancestors. The GLB does not say which nodes are geometry: a node without children is taken as one.
        private static List<SceneNodeInfo> TreeNodes(string path, Func<string, string> rename)
        {
            var json = JObject.Parse(File.ReadAllText(path));
            var names = json["nodes"].Select(n => rename(PhasingNameFixerAcceptanceTests.StripDuplicateSuffix((string)n["name"]))).ToList();
            var children = json["nodes"].Select(n => n["children"].Select(c => (int)c).ToList()).ToList();
            var parents = new int?[names.Count];
            for (int i = 0; i < children.Count; i++) foreach (var c in children[i]) parents[c] = i;
            var nodes = new List<SceneNodeInfo>();
            for (int i = 0; i < names.Count; i++)
            {
                var ancestors = new List<string>();
                for (var p = parents[i]; p.HasValue; p = parents[p.Value]) ancestors.Add(names[p.Value]);
                nodes.Add(new SceneNodeInfo { Name = names[i], IsMesh = children[i].Count == 0 && PlannerNaming.ParseLayerName(names[i]) == null, Ancestors = ancestors });
            }
            return nodes;
        }

        private NamingReport ReportOn(string title, List<SceneNodeInfo> nodes)
        {
            var report = PlannerNamingValidator.Validate(nodes, Today);
            output.WriteLine("--- " + title);
            foreach (var issue in report.Issues.Where(i => i.Severity != NamingSeverity.Note)) output.WriteLine(issue.ToString());
            output.WriteLine(report.Summary());
            return report;
        }

        // The objects whose removal tag has no layer in the reference model: the clean-up's Check lines "removal tag ...
        // names a stage that has no layer in this file".
        private const string ReferenceRemovalsWithoutLayer =
            "8 object(s) have a removal tag whose phase and stage have no layer in this file (Ph1_St23, Ph1_St06, Ph1_St26, Ph1_St34, Ph1_St14, Ph99_St99)";

        /// <summary>
        /// The reference model as Fix phasing names leaves it (PLANNER_FIX_V306 with its sub-stages hyphenated, v3_06h)
        /// reads with no errors and one warning: the eight removal tags that name a stage with no layer in the file, which
        /// the fixer lists to check and cannot correct. Every object's tags match its layer's phase and stage. As the
        /// clean-up left it (full stops), the two warnings about them come on top. Before the clean-up (PLANNER_FIX_V305,
        /// when set), the report is printed for comparison.
        /// </summary>
        [Fact]
        public void ReferenceModelAfterTheFixReadsClean()
        {
            var path = System.Environment.GetEnvironmentVariable("PLANNER_FIX_V306");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                output.WriteLine("PLANNER_FIX_V306 not set; skipped.");
                return;
            }
            foreach (var hyphenated in new[] { true, false })
            {
                var report = ReportOn(hyphenated ? "v3_06h (v3_06 hyphenated, as Fix phasing names leaves it)" : "v3_06 as cleaned (full stops)",
                    TreeNodes(path, hyphenated ? (Func<string, string>)PhasingNameFixerAcceptanceTests.Hyphenate : n => n));
                Assert.Equal(173, report.TaggedObjects);
                Assert.Equal(0, report.UnfiledObjects);
                Assert.Equal(0, report.Errors);
                var warnings = report.Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
                var noLayer = Assert.Single(warnings, w => w.Message.Contains("have no layer in this file"));
                Assert.StartsWith(ReferenceRemovalsWithoutLayer, noLayer.Message);
                Assert.DoesNotContain(warnings, w => w.Message.Contains("neither tag matches"));
                Assert.DoesNotContain(warnings, w => w.Message.Contains("an install tag"));
                if (hyphenated)
                {
                    Assert.Single(warnings);
                }
                else
                {
                    Assert.Equal(3, warnings.Count);
                    Assert.Equal(2, warnings.Count(w => w.Message.Contains("with a full stop, the old form")));
                }
            }

            var before = System.Environment.GetEnvironmentVariable("PLANNER_FIX_V305");
            if (!string.IsNullOrEmpty(before) && File.Exists(before))
            {
                var report = ReportOn("v3_05, before the clean-up", TreeNodes(before, n => n));
                Assert.True(report.TaggedObjects > 100);
            }
        }

        [Fact]
        public void RealFixtureReport()
        {
            var fixture = Environment.GetEnvironmentVariable("PLANNER_NODES_FIXTURE");
            if (string.IsNullOrEmpty(fixture) || !File.Exists(fixture))
            {
                output.WriteLine("PLANNER_NODES_FIXTURE not set; skipped.");
                return;
            }
            var json = JObject.Parse(File.ReadAllText(fixture));
            var nodes = json["nodes"].Select(n => new SceneNodeInfo
            {
                Name = (string)n["name"],
                IsMesh = (bool)n["isMesh"],
                Ancestors = n["parent"].Type == JTokenType.Null ? new List<string>() : new List<string> { (string)n["parent"] }
            }).ToList();
            var report = PlannerNamingValidator.Validate(nodes, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            output.WriteLine(report.Summary());
            Assert.True(report.TaggedObjects > 0);
        }
    }
}
