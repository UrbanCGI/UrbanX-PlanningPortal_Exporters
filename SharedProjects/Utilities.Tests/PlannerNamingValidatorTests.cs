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
            Assert.Contains(warnings, w => w.Subject == "Ph0_St00_IN_Hoarding_B" && w.Message.Contains("(St06.2)")); // the group's sub-number is its sub-stage
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

        // ---- coded scheme (the scene has a Work_Phasing root) ------------------------------------------------------

        /// <summary>The export's nodes as the 3ds Max side collects them: every non-helper with its id, layer and ancestor names.</summary>
        private static List<SceneNodeInfo> ExportNodes(PlannerScene scene, bool withIds = true)
        {
            var result = new List<SceneNodeInfo>();
            foreach (var node in scene.Nodes.Where(n => !n.IsHelper))
            {
                var ancestors = new List<string>();
                for (var parent = scene.FindNode(node.ParentId); parent != null; parent = scene.FindNode(parent.ParentId))
                {
                    ancestors.Add(parent.Name);
                }
                result.Add(new SceneNodeInfo { Id = withIds ? node.Id : null, Name = node.Name, IsMesh = true, Ancestors = ancestors, LayerName = node.LayerName });
            }
            return result;
        }

        private static PlannerScene Adopted()
        {
            return PlannerLayerPlans.AdoptPlan(PlannerLayerPlansTests.BuildFixture(), null).Result;
        }

        [Fact]
        public void CodedSceneIsReadByItsLayers()
        {
            var scene = Adopted();
            var report = PlannerNamingValidator.Validate(ExportNodes(scene), scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());
            output.WriteLine(report.Summary());

            Assert.Equal("Work_Phasing", report.RootLayerName);
            Assert.Equal(20, report.PlannerLayers);
            Assert.Equal(23, report.PhasedObjects);   // the two context objects on layer 0 are not phased
            Assert.Equal(22, report.TaggedObjects);   // Bollard_Cap carries no tag, and needs none
            Assert.Equal(0, report.UnfiledObjects);
            Assert.Equal(0, report.Errors);
            Assert.Contains("23 object(s) on 20 Planner layer(s) under 'Work_Phasing', 22 tagged;", report.Summary());

            var warnings = report.Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
            Assert.Contains(warnings, w => w.Subject == "Ph1_St01_IN_Sheet_Pile_1" && w.Message.Contains("its tag reads 01-01 but it sits on '01-04.0_Zone5_Working_Phase1_Retainment_Installation' (01-04.0)"));
            Assert.Contains(warnings, w => w.Subject == "Ph2_St01.3_RM_RC_Road_Barriers2" && w.Message.Contains("its tag reads 02-01.3"));
            Assert.DoesNotContain(warnings, w => w.Subject == "Ph1_St04_IN_Sheet_Pile_1");
            Assert.DoesNotContain(warnings, w => w.Subject == "Ph6_St05_IN_RC_PlasticRoadBarrier_Full_Closure_Ph99_St99_RM" && w.Message.Contains("its tag")); // 06-05 covers 06-05.2
            Assert.Contains(warnings, w => w.Message.Contains("Code 02-01.5 is used by 2 Planner layers"));
            Assert.Contains(warnings, w => w.Message.Contains("no Planner layer has the code 99-99"));
            Assert.Contains(warnings, w => w.Message.Contains("no Planner layer has the code 01-26"));
            Assert.Contains(warnings, w => w.Subject.StartsWith("Ph2_St02_IN_LM_based on", StringComparison.Ordinal) && w.Message.Contains("contains a space"));

            // The legacy rules are not applied: no dated groups, no "unfiled" and no undated-group warnings.
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("dated group"));
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("neither dates nor TBC"));
            Assert.DoesNotContain(report.Issues, i => i.Subject == "St1_Kerb_003" || i.Subject == "EUS_Con_HRB_Bridge_Slab");
        }

        [Fact]
        public void CodedSceneFlagsObjectsNotUnderTheirLayersHelper()
        {
            var scene = Adopted();
            scene.Nodes.Single(n => n.Name == "Ph1_St04_IN_Sheet_Pile_3").ParentId = null;
            var zoneHelper = scene.Nodes.Single(n => n.IsHelper && n.Name == "01-04.1_Phase1_Retainment_Installation");
            scene.Nodes.Add(new PlannerSceneNode { Id = "x1", Name = "IN_Zone_Fence", LayerName = "01_Zone5_Working", ParentId = zoneHelper.Id }); // a sub-folder's helper
            scene.Nodes.Add(new PlannerSceneNode { Id = "x2", Name = "Ph3_St02_IN_TR_Kerb", LayerName = "0" });                                // tagged, outside the root

            foreach (var withIds in new[] { true, false })
            {
                var report = PlannerNamingValidator.Validate(ExportNodes(scene, withIds), scene, Today);
                Assert.Equal(3, report.UnfiledObjects);
                Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Warning && i.Subject == "01-04.1_Phase1_Retainment_Installation"
                                                   && i.Message.StartsWith("1 object(s) on the Planner layer '01-04.1_Phase1_Retainment_Installation' are not linked under its helper", StringComparison.Ordinal)
                                                   && i.Message.Contains("'Ph1_St04_IN_Sheet_Pile_3'") && i.Message.Contains("Run Update layers"));
                Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Warning && i.Subject == "01_Zone5_Working" && i.Message.Contains("'IN_Zone_Fence'"));
                Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Warning && i.Message.Contains("1 tagged object(s) on the layer '0' sit outside 'Work_Phasing'"));
                Assert.Contains("3 not filed under their layer", report.Summary());
            }
        }

        [Fact]
        public void CodedSceneFlagsPhasingLayersOutsideTheRootAndLayersWithoutHelpers()
        {
            var scene = PlannerLayerPlansTests.BuildFixture();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01_02_Old_Dated_12-01-27", ParentName = "Work_Phasing" });

            var report = PlannerNamingValidator.Validate(ExportNodes(scene), scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());

            var warnings = report.Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
            Assert.Contains(warnings, w => w.Message.Contains("The phasing root 'Work_Phasing' has no helper yet"));
            Assert.Contains(warnings, w => w.Subject == "01_02_Old_Dated_12-01-27" && w.Message.Contains("has no helper yet"));
            Assert.Contains(warnings, w => w.Subject == "01_02_Old_Dated_12-01-27" && w.Message.Contains("still has an old dated name: it reads as code 01-02"));
            Assert.Contains(warnings, w => w.Subject == "_06_Weekend_Closure" && w.Message.Contains("looks like a phasing layer but sits outside 'Work_Phasing' (with 3 more inside it)"));
            Assert.Contains(warnings, w => w.Subject == "_01_Zone5_Working" && w.Message.Contains("(with 4 more inside it)"));
            Assert.Contains(warnings, w => w.Subject == "06_06_Full_Closure_of_Main_Road_TBC_07-02-27" && w.Message.Contains("looks like a phasing layer"));
            Assert.DoesNotContain(warnings, w => w.Subject == "01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26"); // inside a reported layer
            Assert.DoesNotContain(warnings, w => w.Subject == OldRootName);                                               // not a phasing layer itself
            Assert.DoesNotContain(warnings, w => w.Message.Contains("tagged object(s)"));                                // covered by the layer warnings
            Assert.Equal(0, report.PhasedObjects);
        }

        private const string OldRootName = "HS2_EUS_HRB_Phasing_RoadConstraction_Main_Section_v.02";

        [Fact]
        public void CodedSceneKeepsErrorsForBrokenNames()
        {
            var scene = new PlannerScene();
            scene.Layers.Add(new PlannerSceneLayer { Name = "Work_Phasing" });
            scene.Layers.Add(new PlannerSceneLayer { Name = "01-04_Retainment", ParentName = "Work_Phasing" });
            scene.Nodes.Add(new PlannerSceneNode { Id = "r", Name = "Work_Phasing", LayerName = "Work_Phasing", IsHelper = true, Props = { { PlannerProps.Root, "true" } } });
            scene.Nodes.Add(new PlannerSceneNode { Id = "h", Name = "01-04_Retainment", LayerName = "01-04_Retainment", ParentId = "r", IsHelper = true, Props = { { PlannerProps.Code, "01-04" } } });
            var names = new[]
            {
                "Ph1_S03_RM_Thing",              // malformed lead -> error
                "IN_Kerb_Ph2_S06_RM",            // malformed removal -> error
                "Ph1_St04_IN_Kerb_Ph2_St06_IN",  // a trailing install tag means nothing now -> warning
                "Ph1_St04_RM_Barrier_Ph2_St06_IN", // re-install after a removal -> note
                "Ph1_St04_IN",                   // empty description -> warning
                "Ph1_St4_IN_Pile",               // padding -> note
                "Sheet_Pile_7",                  // untagged: an activity like any other -> nothing
                "IN_Pile_RM_01-04",              // removal by code, resolved -> nothing
                "Sheet_Pile_7"                   // duplicate name -> error
            };
            var id = 0;
            foreach (var name in names)
            {
                scene.Nodes.Add(new PlannerSceneNode { Id = "o" + (++id), Name = name, LayerName = "01-04_Retainment", ParentId = "h" });
            }

            var report = PlannerNamingValidator.Validate(ExportNodes(scene), scene, Today);
            foreach (var issue in report.Issues) output.WriteLine(issue.ToString());

            Assert.Equal(3, report.Errors);
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Error && i.Subject == "Ph1_S03_RM_Thing" && i.Message.Contains("leading tag is malformed"));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Error && i.Subject == "IN_Kerb_Ph2_S06_RM" && i.Message.Contains("looks like a removal tag"));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Error && i.Subject == "Sheet_Pile_7" && i.Message.Contains("is the name of 2 objects"));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Warning && i.Subject == "Ph1_St04_IN_Kerb_Ph2_St06_IN" && i.Message.Contains("ends in an install tag"));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Note && i.Subject == "Ph1_St04_RM_Barrier_Ph2_St06_IN" && i.Message.Contains("re-install is ignored"));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Warning && i.Subject == "Ph1_St04_IN" && i.Message.Contains("no description"));
            Assert.Contains(report.Issues, i => i.Severity == NamingSeverity.Note && i.Subject == "Ph1_St4_IN_Pile" && i.Message.Contains("two digits"));
            Assert.DoesNotContain(report.Issues, i => i.Subject == "IN_Pile_RM_01-04");
            Assert.Equal(NamingSeverity.Error, report.Issues.First().Severity);
        }

        [Fact]
        public void SceneWithoutARootKeepsTheLegacyRules()
        {
            var legacy = PlannerNamingValidator.Validate(RepresentativeScene(), Today);
            var withScene = PlannerNamingValidator.Validate(RepresentativeScene(), PlannerLayerPlansTests.BuildFixture(), Today);
            var withoutScene = PlannerNamingValidator.Validate(RepresentativeScene(), null, Today);
            foreach (var report in new[] { withScene, withoutScene })
            {
                Assert.Null(report.RootLayerName);
                Assert.Equal(legacy.Summary(), report.Summary());
                Assert.Equal(legacy.Issues.Select(i => i.ToString()).ToArray(), report.Issues.Select(i => i.ToString()).ToArray());
            }
        }

        /// <summary>
        /// Runs the rules over a real exported hierarchy when PLANNER_NODES_FIXTURE points at a JSON file of the form
        /// { "nodes": [ { "name": "...", "parent": "..." | null, "isMesh": true|false } ] } and prints the report.
        /// </summary>
        [Fact]
        public void SubStagesMatchTheirOwnGroupOrTheWholeStage()
        {
            const string subA = "05-1_Subbase_A_01-11-26_02-11-26";
            const string subB = "05.2_Subbase_B_03-11-26_04-11-26";
            const string whole = "05_Subbase_TBC";
            var scene = new List<SceneNodeInfo>
            {
                Group(subA), Group(subB), Group(whole),
                Mesh("Ph2_St05.1_IN_TR_Subbase_A", subA),  // the sub-stage in its own group -> clean
                Mesh("Ph2_St05.2_IN_TR_Subbase_B", subB),  // a dotted group number reads the same -> clean
                Mesh("Ph2_St05.2_IN_TR_Subbase_C", whole), // a plain group number covers its sub-stages -> clean
                Mesh("Ph2_St05_IN_TR_Subbase_D", subA),    // a plain St under a sub-numbered group, as before -> clean
                Mesh("Ph2_St05.2_IN_TR_Subbase_E", subA),  // the wrong sub-stage -> warning naming St05.1
                Mesh("Ph2_St5.2_IN_TR_Subbase_F", subB)    // unpadded stage digits -> note
            };
            var report = PlannerNamingValidator.Validate(scene, Today);
            Assert.Equal(0, report.Errors);
            Assert.Equal(6, report.TaggedObjects);
            Assert.Equal(3, report.DatedGroups);
            Assert.Equal(0, report.UnfiledObjects);
            var warnings = report.Issues.Where(i => i.Severity == NamingSeverity.Warning).ToList();
            Assert.Single(warnings);
            Assert.Equal("Ph2_St05.2_IN_TR_Subbase_E", warnings[0].Subject);
            Assert.Contains("(St05.1)", warnings[0].Message);
            var notes = report.Issues.Where(i => i.Severity == NamingSeverity.Note).ToList();
            Assert.Single(notes);
            Assert.Equal("Ph2_St5.2_IN_TR_Subbase_F", notes[0].Subject);
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
