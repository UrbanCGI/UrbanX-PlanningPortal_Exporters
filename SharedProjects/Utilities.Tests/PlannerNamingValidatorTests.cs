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
                Mesh("Ph1_St01_RM_RC_Hrd_", Piling),                          // trailing underscore -> warning
                Mesh("Ph1_St1_IN_Kerb", Piling),                              // zero padding -> note
                Mesh("EUS_Con_HRB_Kerb_001"),                                 // untagged context at root -> nothing
                Mesh("St1_Kerb_003", Piling),                                 // untagged inside dated group -> note
                Mesh("Ph1_St01_IN", Piling),                                  // empty description -> warning
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

            Assert.Equal(11, report.TaggedObjects);
            Assert.Equal(7, report.DatedGroups);
            Assert.Equal(3, report.UnfiledObjects);
            Assert.Equal(4, report.Errors);
            Assert.Equal(8, report.Warnings);
            Assert.Equal(3, report.Notes);
            Assert.Equal(NamingSeverity.Error, report.Issues.First().Severity);
            Assert.Equal(NamingSeverity.Note, report.Issues.Last().Severity);
            Assert.Contains("4 error(s), 8 warning(s), 3 note(s)", report.Summary());
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
            Assert.Contains(warnings, w => w.Subject == "Ph0_St00_IN_Hoarding_B" && w.Message.Contains("(St06)"));
            Assert.Contains(warnings, w => w.Subject == "Ph2_St34_IN_TR_Sidewalk_B" && w.Message.Contains("unfiled") && w.Message.Contains("Group it under a node named"));
            Assert.Contains(warnings, w => w.Subject == "Ph1_St00_IN_Fway" && w.Message.Contains("Its group 'Context' needs an order number and dates"));
            Assert.Contains(warnings, w => w.Subject == "Ph1_St01_RM_RC_Hrd_" && w.Message.Contains("stray underscore"));
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
            Assert.Empty(report.Issues); // owned by the 06 group, not confused by the 01 group further up
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
