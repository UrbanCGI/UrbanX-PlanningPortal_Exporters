using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// Naming v2 (spec sections 2.1 to 2.3): mirrors the naming v2 cases of packages/domain/src/phasingNaming.test.ts in the
    /// Planner repository case for case (commit 6509458), and the name trap of apps/web/src/features/panel/folderName.test.ts.
    /// If one of these fails after a Planner change, port the change here too: the Max tool must read new names exactly as
    /// the Planner does. The old-name suites (PlannerNamingTests and the rest) stay as they are.
    /// </summary>
    public class PlannerNamingV2Tests
    {
        private readonly ITestOutputHelper output;

        public PlannerNamingV2Tests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Layer and helper names (spec 2.1)

        [Fact]
        public void LayerV2_ReadsIdAndNameAsWritten()
        {
            Assert.Equal(LayerSaid(Id(6, 5, 2), "Site_Entrance-Hoarding_Install", "06_05_2_Site_Entrance-Hoarding_Install"), Layer("06_05_2_Site_Entrance-Hoarding_Install"));
            Assert.Equal(LayerSaid(Id(6, 4), "Temporary_Road-Road_Marking", "06_04_Temporary_Road-Road_Marking"), Layer("06_04_Temporary_Road-Road_Marking"));
        }

        [Fact]
        public void LayerV2_TakesOnlyTwoOrThreeNumbersOfOneOrTwoDigitsApartBySingleUnderscores()
        {
            foreach (var n in new[]
            {
                "05_Retainment", "05", "1_Kerbs", "002_01_Kerbs", "02_100_Kerbs", "02__05_Kerbs", "02_05-2_Kerbs", "02_05.2_Kerbs", "02_05 Kerbs",
                "2.4 High Hoarding", "_03_Cardington_St_Works", "Kerbs_02_05"
            })
            {
                Assert.True(PlannerNaming.ParseLayerNameV2(n) == null, n);
            }
            // a third number of three digits is no sub-stage: it starts the name
            var p = PlannerNaming.ParseLayerNameV2("02_05_123_Frame");
            Assert.Equal(Id(2, 5), p.Id);
            Assert.Equal("123_Frame", p.Name);
        }

        [Fact]
        public void LayerV2_PassesOverASpaceAfterTheIdAndSaysHowToWriteIt()
        {
            foreach (var pair in new[]
            {
                new[] { "03_03 Kerbs", "03_03_Kerbs" },
                new[] { "3_3  Kerbs North", "03_03_Kerbs_North" },
                new[] { "03_03_2 Kerbs", "03_03_2_Kerbs" },
                new[] { "03_09_24 Hour Closure", "03_09_24_Hour_Closure" },
                new[] { "03_03_2 Kerbs (1)", "03_03_2_Kerbs" }
            })
            {
                Assert.True(PlannerNaming.ParseLayerNameV2(pair[0]) == null, pair[0]);
                Assert.Equal(pair[1], PlannerNaming.LayerNameSpacedAfterId(pair[0]));
            }
            // A space later in the name is a layer, with a warning, as before; nothing else is this case.
            var kerbs = PlannerNaming.ParseLayerNameV2("02_01_Kerbs North");
            Assert.Equal(Id(2, 1), kerbs.Id);
            Assert.Equal("Kerbs North", kerbs.Name);
            Assert.Equal(new[] { "space" }, Codes(kerbs.Issues));
            var frame = PlannerNaming.ParseLayerNameV2("02_05_123 Frame");
            Assert.Equal(Id(2, 5), frame.Id);
            Assert.Equal("123 Frame", frame.Name);
            Assert.Equal(new[] { "space" }, Codes(frame.Issues));
            foreach (var n in new[] { "02_01_Kerbs North", "02_05_123 Frame", "05 Retainment", "02_05-2 Kerbs", "Kerbs 02_05", "02_05_Kerbs" })
            {
                Assert.True(PlannerNaming.LayerNameSpacedAfterId(n) == null, n);
            }
        }

        [Fact]
        public void LayerV2_DigitsAndAnUnderscoreAfterATwoNumberIdAreTheThirdNumber()
        {
            var storey = PlannerNaming.ParseLayerNameV2("02_05_3_Storey_Frame");
            Assert.Equal(Id(2, 5, 3), storey.Id);
            Assert.Equal("Storey_Frame", storey.Name);
            var closure = PlannerNaming.ParseLayerNameV2("03_09_24_Hour_Closure");
            Assert.Equal(Id(3, 9, 24), closure.Id);
            Assert.Equal("Hour_Closure", closure.Name);
            var folder = PlannerNaming.ParseLayerName("03_09_24_Hour_Closure");
            Assert.Equal(PlannerNaming.LayerIdKey(Id(3, 9, 24)), PlannerNaming.LayerIdKey(new PlannerLayerIdKey(folder.Phase, folder.Stage, folder.SubStage)));
        }

        [Fact]
        public void LayerV2_AHelperCopiedInMaxIsTheSameLayer()
        {
            Assert.Equal(Layer("02_01_4_Construction_of_Phase1_TM"), Layer("02_01_4_Construction_of_Phase1_TM (1)"));
        }

        [Fact]
        public void LayerV2_ComparesIdsAsNumbers()
        {
            Assert.Equal(PlannerNaming.LayerIdKey(PlannerNaming.ParseLayerNameV2("06_05_2_Y").Id), PlannerNaming.LayerIdKey(PlannerNaming.ParseLayerNameV2("6_5_2_X").Id));
            Assert.Equal(PlannerNaming.LayerIdKey(Id(6, 5, 2)), PlannerNaming.LayerIdKey(PlannerNaming.ParseLayerNameV2("06_05_02_X").Id));
            Assert.NotEqual(PlannerNaming.LayerIdKey(Id(6, 5, 2)), PlannerNaming.LayerIdKey(Id(6, 5)));
            Assert.NotEqual(PlannerNaming.LayerIdKey(Id(6, 5, 0)), PlannerNaming.LayerIdKey(Id(6, 5)));
            Assert.Equal(PlannerNaming.LayerIdKey(Id(6, 5, 0)), PlannerNaming.LayerIdKey(PlannerNaming.ParseLayerNameV2("06_05_00_X").Id));
        }

        [Fact]
        public void LayerV2_WritesTheIdTheCurrentWay()
        {
            var gates = PlannerNaming.ParseLayerNameV2("6_5_2_Gates");
            Assert.Equal(Id(6, 5, 2), gates.Id);
            Assert.Equal("06_05_2_Gates", gates.Label);
            Assert.Equal("06_05_2_Gates", PlannerNaming.ParseLayerNameV2("6_5_02_Gates").Label);
            Assert.Equal(new[] { "03_09", "02_02_13", "01_00" }, new[] { Id(3, 9), Id(2, 2, 13), Id(1, 0) }.Select(PlannerNaming.LayerIdText).ToArray());
        }

        [Fact]
        public void LayerV2_NotesNoNameASpaceAndDatesButReadsTheLayerAllTheSame()
        {
            Assert.Equal(LayerSaid(Id(2, 1), string.Empty, "02_01", "no-name"), Layer("02_01"));
            Assert.Equal(LayerSaid(Id(2, 1), string.Empty, "02_01", "no-name"), Layer("02_01_"));
            var line = PlannerNaming.ParseLayerNameV2("02_01_Kerb Line");
            Assert.Equal("Kerb Line", line.Name);
            Assert.Equal(new[] { "space" }, Codes(line.Issues));
            var dated = PlannerNaming.ParseLayerNameV2("02_01_17-08-26_Kerbs");
            Assert.Equal(Id(2, 1), dated.Id);
            Assert.Equal("17-08-26_Kerbs", dated.Name);
            Assert.Equal(new[] { "dates" }, Codes(dated.Issues));
        }

        [Fact]
        public void LayerV2_RealLayerNames()
        {
            var table = new[]
            {
                new { Old = "02_01_4_Construction_of_Phase1_TM_TBC_07-09-26_05-02-27", Converted = "02_01_4_Construction_of_Phase1_TM", Layer = Id(2, 1, 4), Name = "Construction_of_Phase1_TM" },
                new
                {
                    Old = "02_02_8_MainRd_Hoardings_inst_TBC_!!!FULL_CLOSURE!!!_17-10-26_18-10-26", Converted = "02_02_8_MainRd_Hoardings_inst_!!!FULL_CLOSURE!!!",
                    Layer = Id(2, 2, 8), Name = "MainRd_Hoardings_inst_!!!FULL_CLOSURE!!!"
                },
                new { Old = "06_04_Temporary_Road-Road_Marking_06-02-27", Converted = "06_04_Temporary_Road-Road_Marking", Layer = Id(6, 4), Name = "Temporary_Road-Road_Marking" },
                new { Old = "01_00_START_DATE_01-06-26", Converted = "01_00_START_DATE", Layer = Id(1, 0), Name = "START_DATE" }, // an empty layer; it no longer sets the start
                new { Old = "01_01_Drainage_TBC_08-06-26_03-07-26", Converted = "01_01_Drainage", Layer = Id(1, 1), Name = "Drainage" } // HS2
            };
            foreach (var row in table)
            {
                Assert.Equal(LayerSaid(row.Layer, row.Name, row.Converted), Layer(row.Converted));
                var before = PlannerNaming.ParseLayerNameV2(row.Old);
                Assert.Equal(PlannerNaming.LayerIdKey(row.Layer), PlannerNaming.LayerIdKey(before.Id));
                Assert.Equal(row.Old.Substring(PlannerNaming.LayerIdText(row.Layer).Length + 1), before.Name);
                Assert.Equal(new[] { "dates" }, Codes(before.Issues));
                Assert.True(PlannerNaming.LayerNameCarriesDates(row.Old), row.Old);
                Assert.False(PlannerNaming.LayerNameCarriesDates(row.Converted), row.Converted);
            }
            Assert.False(PlannerNaming.LayerNameCarriesDates("03_01-1_Temporary_Hoarding")); // an older sub-stage is no date
            Assert.True(PlannerNaming.LayerNameCarriesDates("02_01_Drainage_TBC (1)"));
            // A date, not the TBC word, is what decides a model's naming (NamingOf).
            foreach (var n in new[] { "02_01_Drainage_TBC (1)", "02_02_12_Hoarding_Install2_TBC", "03_01-1_Temporary_Hoarding" })
            {
                Assert.False(PlannerNaming.LayerNameHasDate(n), n);
            }
            foreach (var n in new[] { "06_04_Temporary_Road-Road_Marking_06-02-27", "01_01_Drainage_TBC_08-06-26_03-07-26 (1)" })
            {
                Assert.True(PlannerNaming.LayerNameHasDate(n), n);
            }
        }

        [Fact]
        public void LayerV2_LeavesPhaseGroupsAndOneNumberLayersAsTheyAre()
        {
            // The Planner's parsePhaseGroupName also gives the group's number (3); the port gives the label.
            Assert.Equal("_03_Cardington_St_Works", PlannerSchedule.ParsePhaseGroup("_03_Cardington_St_Works"));
            Assert.Null(PlannerNaming.ParseLayerNameV2("_03_Cardington_St_Works"));
            var old = PlannerNaming.ParseLayerName("05_Retainment");
            Assert.Equal("05", old.Order);
            Assert.False(old.Phase.HasValue);
            Assert.Equal("Retainment", old.Name);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Object names: the grammar and what each form means (spec 2.2)

        [Fact]
        public void ObjectV2_LeadingTagAtTheVeryStartInCapitals()
        {
            Assert.Equal(Said("new", "Install", null, null, "Kerb"), Reads("IN_Kerb"));
            Assert.Equal(Said("new", "Dismantle", null, null, "Kerb"), Reads("RM_Kerb"));
        }

        [Fact]
        public void ObjectV2_TrailingTagAtTheEndAndTheReferenceAfterItsTypeWord()
        {
            Assert.Equal(Said("new", null, "Dismantle", null, "Kerb"), Reads("Kerb_RM"));
            Assert.Equal(Said("new", "Install", "Dismantle", "06_05_2", "Hoarding"), Reads("IN_Hoarding_RM_06_05_2"));
            Assert.Equal(Said("new", "Dismantle", "Install", "03_01", "Bench"), Reads("RM_Bench_IN_03_01"));
        }

        [Fact]
        public void ObjectV2_ScheduledOnlyWithATagInCapitals()
        {
            Assert.Equal(Said(null, null, null, null, "Kerb"), Reads("Kerb"));
            Assert.Equal(Said(null, null, null, null, "In_Situ_Wall"), Reads("In_Situ_Wall"));
        }

        [Fact]
        public void ObjectV2_MeansWhatTheTableSays()
        {
            var table = new[]
            {
                new { Name = "IN_Kerb", Means = new[] { "IN during" } }, // installed while L runs
                new { Name = "RM_Kerb", Means = new[] { "RM during" } }, // there from the start, removed while L runs
                new { Name = "IN_Hoarding_RM", Means = new[] { "IN during", "RM end" } }, // installed while L runs, removed at the end of L
                new { Name = "RM_Bench_IN", Means = new[] { "RM during", "IN end" } }, // taken away while L runs, put back at the end of L
                new { Name = "IN_Hoarding_RM_06_05_2", Means = new[] { "IN during", "RM in 06_05_2" } }, // installed while L runs, removed in 06_05_2
                new { Name = "RM_Bench_IN_03_01", Means = new[] { "RM during", "IN in 03_01" } }, // taken away while L runs, put back in 03_01
                new { Name = "Kerb_RM", Means = new[] { "RM end" } }, // there from the start, removed at the end of L
                new { Name = "Kerb_RM_06_05_2", Means = new[] { "RM in 06_05_2" } }, // there from the start, removed in 06_05_2
                new { Name = "Kerb_IN", Means = new[] { "IN end" } }, // installed at the end of L, not during it
                new { Name = "Kerb_IN_03_01", Means = new[] { "IN in 03_01" } }, // installed in 03_01, while filed under L's helper
                new { Name = "Kerb", Means = new string[0] } // not scheduled, so shown for the whole programme
            };
            foreach (var row in table)
            {
                Assert.Equal(row.Name + ": " + string.Join(", ", row.Means), row.Name + ": " + string.Join(", ", Means(row.Name)));
            }
        }

        [Fact]
        public void ObjectV2_TheLayerNumberGoesAfterTheTypeWord()
        {
            Assert.Equal(Said("new", "Install", "Dismantle", null, "Barrier_01"), Reads("IN_Barrier_01_RM"));
            Assert.Equal(Said("new", "Install", null, null, "Barrier_RM_01", "not-an-id"), Reads("IN_Barrier_RM_01"));
            // An error Clean up must list, never number: numbered at the end, it would read as RM in 06_02.
            Assert.Equal(Said("new", "Install", null, null, "X_RM_06", "not-an-id"), Reads("IN_X_RM_06"));
            var x = PlannerNaming.ParseObjectNameV2("IN_X_RM_06_02");
            Assert.Equal(PlannerTaskType.Dismantle, x.Trail);
            Assert.Equal("06_02", PlannerNaming.LayerIdText(x.Ref));
            Assert.Equal("X", x.Own);
            Assert.Empty(x.Issues);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Object names: the parsing rule (spec 2.2)

        [Fact]
        public void ObjectV2_WorkedCases()
        {
            Assert.Equal(Said("new", "Install", "Dismantle", "06_05_2", "X"), Reads("IN_X_RM_06_05_2"));
            Assert.Equal(Said(null, null, null, null, "X_RM_1", "not-an-id"), Reads("X_RM_1"));
            Assert.Equal(Said("new", null, "Dismantle", null, "X_IN_06_05_2", "two-trailing"), Reads("X_IN_06_05_2_RM"));
            Assert.Equal(Said("new", "Install", null, null, "Road", "note-tag"), Reads("IN_Road Plates_RM_03_09"));
            Assert.Equal(Said("new", "Install", "Dismantle", "06_05_2", "Barrier_02"), Reads("IN_Barrier_02_RM_06_05_2"));
        }

        [Fact]
        public void ObjectV2_OneDuplicateSuffixOffThenOnlyTheTextBeforeTheFirstWhitespace()
        {
            Assert.Equal(Reads("IN_Sheet_Pile_4"), Reads("IN_Sheet_Pile_4 (1)"));
            Assert.Equal(Reads("IN_Kerb"), Reads("IN_Kerb (1) (2)"));
            Assert.Equal(Said("new", "Install", "Dismantle", null, "Kerb"), Reads("IN_Kerb_RM not proved (based on CAD)"));
            Assert.Equal(Said(null, null, null, null, "Kerb", "note-tag"), Reads("Kerb RM_later"));
        }

        [Fact]
        public void ObjectV2_NeedsSomethingAfterALeadingTagAndListsANameWithNothingBetweenItsTags()
        {
            foreach (var n in new[] { "IN_", "_RM_06_05" })
            {
                var p = PlannerNaming.ParseObjectNameV2(n);
                Assert.True(p.Kind == null && p.Lead == null && p.Trail == null, n);
            }
            Assert.Equal(Said(null, null, null, null, string.Empty, "no-own-name"), Reads("IN___"));
            // after a leading tag, a would-be trailing tag at the very start of what is left is no trailing tag (step 4)
            var rmIn = PlannerNaming.ParseObjectNameV2("RM__IN");
            Assert.Equal(PlannerNamingKind.New, rmIn.Kind);
            Assert.Equal(PlannerTaskType.Dismantle, rmIn.Lead);
            Assert.Null(rmIn.Trail);
            Assert.Null(rmIn.Ref);
            Assert.Equal("IN", rmIn.Own);
            var inRm = PlannerNaming.ParseObjectNameV2("IN__RM_06_05");
            Assert.Equal(PlannerNamingKind.New, inRm.Kind);
            Assert.Equal(PlannerTaskType.Install, inRm.Lead);
            Assert.Null(inRm.Trail);
            Assert.Null(inRm.Ref);
            Assert.Equal("RM_06_05", inRm.Own);
        }

        [Fact]
        public void ObjectV2_TrimsUnderscoresFromBothEndsOfTheOwnNameAndNotesIt()
        {
            Assert.Equal(Said("new", "Install", null, null, "TR_Subbase_280mm_C", "trimmed"), Reads("IN_TR_Subbase_280mm_C_"));
            Assert.Equal(Said("new", "Install", "Dismantle", null, "Kerb", "trimmed"), Reads("IN__Kerb__RM"));
        }

        [Fact]
        public void ObjectV2_AReferenceToTheOwnLayerReadsAsThePlainTrailingTag()
        {
            Assert.Equal(Said("new", "Install", "Dismantle", null, "X", "own-layer"), Reads("IN_X_RM_06_05_2", Id(6, 5, 2)));
            Assert.Equal(new[] { "IN during", "RM end" }, Means("IN_X_RM_6_5_2", Id(6, 5, 2)));
            var other = PlannerNaming.ParseObjectNameV2("IN_X_RM_06_05_2", Id(6, 5));
            Assert.Equal("06_05_2", PlannerNaming.LayerIdText(other.Ref));
            Assert.Empty(other.Issues);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Illegal names: Generate lists each one (spec 2.2)

        [Fact]
        public void IllegalV2_SameTypeAtBothEndsSchedulesTheLeadingTagOnly()
        {
            Assert.Equal(Said("new", "Install", null, null, "Kerb", "same-type"), Reads("IN_Kerb_IN"));
            Assert.Equal(Said("new", "Dismantle", null, null, "Kerb", "same-type"), Reads("RM_Kerb_RM_03_01"));
        }

        [Fact]
        public void IllegalV2_ADoubledTypeWordReadsTheFirst()
        {
            Assert.Equal(Said("new", "Dismantle", null, null, "RM_Marwood_Barrier3", "doubled-type"), Reads("RM_RM_Marwood_Barrier3"));
        }

        [Fact]
        public void IllegalV2_TwoTrailingTagsReadTheLastOnly()
        {
            var x = PlannerNaming.ParseObjectNameV2("X_IN_06_05_2_RM");
            Assert.Equal(PlannerTaskType.Dismantle, x.Trail);
            Assert.Null(x.Ref);
            Assert.Equal("X_IN_06_05_2", x.Own);
            Assert.Equal(new[] { "two-trailing" }, Codes(x.Issues));
            Assert.Equal(Said("new", "Install", "Dismantle", null, "X_RM", "two-trailing"), Reads("IN_X_RM_RM"));
        }

        [Fact]
        public void IllegalV2_ATypeWordFollowedByDigitsThatAreNotALayerId()
        {
            foreach (var n in new[] { "X_RM_1", "X_RM001", "X_RM_06_05_002", "X_RM_06_05_2_3" })
            {
                Assert.Equal(Said(null, null, null, null, n, "not-an-id"), Reads(n));
            }
        }

        [Fact]
        public void IllegalV2_NothingIsReadFromANameHoldingAnOldTag()
        {
            foreach (var n in new[] { "Ph2_St01_4_IN_Kerbs2", "IN_Kerb_Ph6_St05_RM" })
            {
                var p = PlannerNaming.ParseObjectNameV2(n);
                Assert.Equal(PlannerNamingKind.Old, p.Kind);
                Assert.True(p.Lead == null && p.Trail == null && p.Ref == null, n);
                Assert.Equal(new[] { "old-tag" }, Codes(p.Issues));
            }
        }

        [Fact]
        public void IllegalV2_NoTagAfterALayerId()
        {
            Assert.Equal(Said(null, null, null, null, "02_01_4_IN_Kerb", "id-then-tag"), Reads("02_01_4_IN_Kerb"));
        }

        [Fact]
        public void IllegalV2_NoTagInsideTheTextAfterASpace()
        {
            Assert.Equal(new[] { "IN during" }, Means("IN_Road Plates_RM_03_09"));
        }

        [Fact]
        public void IllegalV2_AOneNumberLayerIsNoLayerNode()
        {
            Assert.Null(PlannerNaming.ParseLayerNameV2("05_Retainment"));
        }

        [Fact]
        public void IllegalV2_TwoExportedObjectsWithOneName()
        {
            var issues = PlannerNaming.NamingIssuesV2(
                new[] { Obj("IN_Sheet_Pile_4", Id(1, 4, 1)), Obj("IN_Sheet_Pile_4 (1)", Id(1, 4, 2)) },
                new[] { Id(1, 4, 1), Id(1, 4, 2) });
            Assert.Equal(new[] { "duplicate-export: IN_Sheet_Pile_4 | IN_Sheet_Pile_4 (1)" }, Said(issues));
            Assert.Equal(PlannerNaming.ParseObjectNameV2("IN_Sheet_Pile_4").Own, PlannerNaming.ParseObjectNameV2("IN_Sheet_Pile_4 (1)").Own);
        }

        [Fact]
        public void IllegalV2_TwoObjectsWithOneNameAreBothReadTheirActivitiesAlikeOnlyWithinOneLayer()
        {
            // In two layers their activities differ by the layer ID (Sheet_Pile_4_IN_01_04_1, Sheet_Pile_4_IN_01_04_2), so the
            // text must not say both activities get the same name; in one layer they do.
            var said = PlannerNaming.NamingIssueV2[PlannerNamingCode.DuplicateExport];
            Assert.Equal(NamingSeverity.Error, said.Severity);
            Assert.Equal(
                "Two objects have this name, so the export renames one with (1). Both are read as the name says, and their activities share a name where they fall in one layer: give each object its own name.",
                said.Text);
            Assert.DoesNotContain("both activities get the same name", said.Text);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Warned, not illegal (spec 2.2)

        [Fact]
        public void WarnedV2_ObjectsSharingAnOwnName()
        {
            var objects = new[] { Obj("IN_Cycling_Bollards", Id(2, 2, 9)), Obj("IN_Cycling_Bollards_RM_04_02_3", Id(2, 2, 13)) };
            Assert.Equal(new[] { "shared-own-name: IN_Cycling_Bollards | IN_Cycling_Bollards_RM_04_02_3" },
                Said(PlannerNaming.NamingIssuesV2(objects, new[] { Id(2, 2, 9), Id(2, 2, 13), Id(4, 2, 3) })));
        }

        [Fact]
        public void WarnedV2_NearOwnNamesInOneLayerNamingDifferentLayers()
        {
            var layers = new[] { Id(2, 2, 1), Id(6, 5, 2), Id(6, 5, 3) };
            var near = new[] { Obj("IN_Hoarding_RM_06_05_2", Id(2, 2, 1)), Obj("IN_Hoarding_01_RM_06_05_3", Id(2, 2, 1)) };
            Assert.Equal(new[] { "near-own-names: IN_Hoarding_RM_06_05_2 | IN_Hoarding_01_RM_06_05_3" }, Said(PlannerNaming.NamingIssuesV2(near, layers)));
            // not across layers, nor when they name one layer
            Assert.Empty(PlannerNaming.NamingIssuesV2(new[] { near[0], Obj(near[1].Name, Id(6, 5, 2)) }, layers));
            Assert.Empty(PlannerNaming.NamingIssuesV2(new[] { near[0], Obj("IN_Hoarding_01_RM_06_05_2", Id(2, 2, 1)) }, layers));
        }

        [Fact]
        public void WarnedV2_TheOldOrderWrittenWithoutTheLettersWhenItsNumberNamesALayer()
        {
            var kerb = new[] { Obj("IN_Kerb_06_05_2_RM", Id(2, 1)) };
            Assert.Equal(new[] { "old-order: IN_Kerb_06_05_2_RM" }, Said(PlannerNaming.NamingIssuesV2(kerb, new[] { Id(2, 1), Id(6, 5, 2) })));
            Assert.Equal(new[] { "old-order: IN_Kerb_06_05_2_RM" }, Said(PlannerNaming.NamingIssuesV2(kerb, new[] { Id(2, 1), Id(5, 2) })));
            Assert.Empty(PlannerNaming.NamingIssuesV2(kerb, new[] { Id(2, 1) }));
        }

        [Fact]
        public void WarnedV2_AReferenceToTheOwnLayerAndOneToALayerNobodyHas()
        {
            Assert.Equal(new[] { "own-layer: IN_X_RM_06_05_2" }, Said(PlannerNaming.NamingIssuesV2(new[] { Obj("IN_X_RM_06_05_2", Id(6, 5, 2)) }, new[] { Id(6, 5, 2) })));
            var hrd2 = new[] { Obj("IN_RC_Hrd2_RM_01_23", Id(2, 2, 12)) };
            Assert.Equal(new[] { "unknown-layer: IN_RC_Hrd2_RM_01_23" }, Said(PlannerNaming.NamingIssuesV2(hrd2, new[] { Id(2, 2, 12) })));
            Assert.Empty(PlannerNaming.NamingIssuesV2(hrd2, new[] { Id(2, 2, 12), Id(1, 23) })); // an ID only a Planner folder has
            Assert.Equal(new[] { "unknown-layer: X_RM_99_99" }, Said(PlannerNaming.NamingIssuesV2(new[] { Obj("X_RM_99_99", Id(2, 1)) }, new[] { Id(2, 1) })));
            // 99_99 is how work whose date is not known yet is written (generateNewNames.ts DATE_UNKNOWN): a layer nobody has.
            Assert.Equal(PlannerNaming.DateUnknownLayer, PlannerNaming.LayerIdText(PlannerNaming.ParseObjectNameV2("X_RM_99_99").Ref));
        }

        [Fact]
        public void WarnedV2_ASplitPieceWhoseNameReadAloneNamesALayer()
        {
            var plates = new[] { Obj("IN_Road_Plates_RM_03_09", Id(3, 11)) };
            var known = new[] { Id(3, 11), Id(3, 9) };
            // the dry run's case: a piece _1 added to one of the three names ending in a two-number reference
            Assert.Equal(new[] { "piece-names-layer: IN_Road_Plates_RM_03_09 | IN_Road_Plates_RM_03_09_1" },
                Said(PlannerNaming.NamingIssuesV2(plates, known, Pieces("IN_Road_Plates_RM_03_09_1", "IN_Road_Plates_RM_03_09"))));
            // a piece that turns a number that is no layer ID into one
            Assert.Equal(new[] { "not-an-id: IN_Pipe_RM_03", "piece-names-layer: IN_Pipe_RM_03 | IN_Pipe_RM_03_09" },
                Said(PlannerNaming.NamingIssuesV2(new[] { Obj("IN_Pipe_RM_03", Id(3, 11)) }, known, Pieces("IN_Pipe_RM_03_09", "IN_Pipe_RM_03"))));
            // pieces that name no layer read alone: a copy number after a tag, a fourth number, no tag, an old name
            var quiet = Pieces(
                "IN_Kerb_RM_2", "IN_Kerb_RM",
                "IN_Gate_RM_06_05_2_1", "IN_Gate_RM_06_05_2",
                "Lamp_1", "Lamp",
                "Ph1_St01_IN_X_RM_03_09_1", "Ph1_St01_IN_X_RM_03_09");
            Assert.Empty(PlannerNaming.NamingIssuesV2(new PlannerObjectLayer[0], known, quiet));
        }

        [Fact]
        public void WarnedV2_LowerCaseNearTagsATypeWordInTheMiddleAndHyphens()
        {
            Assert.Equal(Said(null, null, null, null, "in_Kerb", "lower-case"), Reads("in_Kerb"));
            var rm = PlannerNaming.ParseObjectNameV2("Kerb_rm");
            Assert.Null(rm.Kind);
            Assert.Equal(new[] { "lower-case" }, Codes(rm.Issues));
            var kerb = PlannerNaming.ParseObjectNameV2("IN_Kerb_in_03_01");
            Assert.Equal(PlannerNamingKind.New, kerb.Kind);
            Assert.Equal(PlannerTaskType.Install, kerb.Lead);
            Assert.Null(kerb.Trail);
            Assert.Equal("Kerb_in_03_01", kerb.Own);
            Assert.Equal(new[] { "lower-case" }, Codes(kerb.Issues));
            Assert.Equal(Said(null, null, null, null, "Pipe_RM_Trench", "type-in-middle"), Reads("Pipe_RM_Trench"));
            var hoarding = PlannerNaming.ParseObjectNameV2("IN_Site_Entrance-Hoarding");
            Assert.Equal(PlannerNamingKind.New, hoarding.Kind);
            Assert.Equal("Site_Entrance-Hoarding", hoarding.Own);
            Assert.Equal(new[] { "hyphen" }, Codes(hoarding.Issues));
        }

        [Fact]
        public void WarnedV2_AnInstallTagAtTheEndAndTbcOrADateInAnObjectName()
        {
            Assert.Equal(new[] { "IN end" }, Means("Kerb_IN"));
            Assert.Equal(Said("new", "Install", null, null, "Kerb_TBC"), Reads("IN_Kerb_TBC"));
            Assert.Equal(Said("new", "Install", null, null, "Kerb_17_08_26"), Reads("IN_Kerb_17_08_26"));
        }

        [Fact]
        public void WarnedV2_EachObjectsOwnIssuesAndOnlyNewKindFiledObjectsInTheGroups()
        {
            var issues = PlannerNaming.NamingIssuesV2(
                new[]
                {
                    Obj("X_RM_1", Id(2, 1)),
                    Obj("Ph1_St01_IN_Kerb", Id(2, 1)),
                    Obj("Ph1_St02_IN_Kerb", Id(2, 1)),
                    Obj("IN_A_RM", null),
                    Obj("IN_A_02_RM_02_01", null)
                },
                new[] { Id(2, 1) });
            Assert.Equal(new[] { "not-an-id: X_RM_1", "old-tag: Ph1_St01_IN_Kerb", "old-tag: Ph1_St02_IN_Kerb" }, Said(issues));
        }

        [Fact]
        public void WarnedV2_EveryIssueHasASeverityAndPlainWording()
        {
            foreach (var code in new[]
            {
                PlannerNamingCode.OldTag, PlannerNamingCode.NoteTag, PlannerNamingCode.NoOwnName, PlannerNamingCode.SameType, PlannerNamingCode.DoubledType,
                PlannerNamingCode.TwoTrailing, PlannerNamingCode.NotAnId, PlannerNamingCode.IdThenTag, PlannerNamingCode.DuplicateExport
            })
            {
                Assert.True(PlannerNaming.NamingIssueV2[code].Severity == NamingSeverity.Error, code.ToString());
            }
            foreach (var code in new[]
            {
                PlannerNamingCode.SharedOwnName, PlannerNamingCode.NearOwnNames, PlannerNamingCode.OldOrder, PlannerNamingCode.OwnLayer, PlannerNamingCode.UnknownLayer,
                PlannerNamingCode.PieceNamesLayer, PlannerNamingCode.TypeInMiddle, PlannerNamingCode.LowerCase, PlannerNamingCode.Hyphen
            })
            {
                Assert.True(PlannerNaming.NamingIssueV2[code].Severity == NamingSeverity.Warning, code.ToString());
            }
            foreach (var info in PlannerNaming.NamingIssueV2.Values)
            {
                Assert.Matches(new Regex(@"^[A-Z][^\n]*\.\z"), info.Text);
            }
            // Every code has its entry, under the Planner's code (NAMING_ISSUE_V2's keys), each once.
            var all = (PlannerNamingCode[])System.Enum.GetValues(typeof(PlannerNamingCode));
            Assert.Equal(all.Length, PlannerNaming.NamingIssueV2.Count);
            Assert.Equal(
                new[]
                {
                    "dates", "doubled-type", "duplicate-export", "hyphen", "id-then-tag", "lower-case", "near-own-names", "no-name", "no-own-name", "not-an-id",
                    "note-tag", "old-order", "old-tag", "own-layer", "piece-names-layer", "same-type", "shared-own-name", "space", "trimmed", "two-trailing",
                    "type-in-middle", "unknown-layer"
                },
                all.Select(PlannerNaming.CodeText).OrderBy(c => c, System.StringComparer.Ordinal).ToArray());
            Assert.Equal(NamingSeverity.Note, PlannerNaming.NamingIssueV2[PlannerNamingCode.Trimmed].Severity);
            Assert.Equal("The tag names the object" + (char)0x2019 + "s own layer: write the plain tag instead.", PlannerNaming.NamingIssueV2[PlannerNamingCode.OwnLayer].Text);
        }

        // ---------------------------------------------------------------------------------------------------------
        // The real names converted (spec 2.2)

        [Fact]
        public void RealV2_EachConvertedNameReadsAsCleanUpMeansItAndEachOldNameAsOldKind()
        {
            // old name, its layer, the converted name, what that means, its own name
            var table = new[]
            {
                new { Old = "Ph1_St04_1_IN_Sheet_Pile_4", Layer = Id(1, 4, 1), Converted = "IN_Sheet_Pile_4", Means = new[] { "IN during" }, Own = "Sheet_Pile_4" },
                new { Old = "Ph1_St04_2_IN_Sheet_Pile_4", Layer = Id(1, 4, 2), Converted = "IN_Sheet_Pile_4_02", Means = new[] { "IN during" }, Own = "Sheet_Pile_4_02" },
                new
                {
                    Old = "Ph3_St04_2_IN_RC_HF_Outer_Full_Closure_Ph3_St09_RM", Layer = Id(3, 4, 2), Converted = "IN_RC_HF_Outer_Full_Closure_02_RM_03_09",
                    Means = new[] { "IN during", "RM in 03_09" }, Own = "RC_HF_Outer_Full_Closure_02"
                },
                new { Old = "Ph2_St02_13_IN_Cycling_Bollards", Layer = Id(2, 2, 13), Converted = "IN_Cycling_Bollards_03", Means = new[] { "IN during" }, Own = "Cycling_Bollards_03" },
                new
                {
                    Old = "Ph2_St02_1_IN_RB22_Barrier01_Ph6_St05_2_RM", Layer = Id(2, 2, 1), Converted = "IN_RB22_Barrier01_RM_06_05_2",
                    Means = new[] { "IN during", "RM in 06_05_2" }, Own = "RB22_Barrier01"
                },
                new
                {
                    Old = "Ph3_St04_1_IN_RC_HF_Outer_Full_Closure_Ph3_St04_1_RM", Layer = Id(3, 4, 1), Converted = "IN_RC_HF_Outer_Full_Closure_RM",
                    Means = new[] { "IN during", "RM end" }, Own = "RC_HF_Outer_Full_Closure"
                },
                new { Old = "Ph3_St11_IN_Road_Plates_Ph3_St09_RM", Layer = Id(3, 11), Converted = "IN_Road_Plates_RM_03_09", Means = new[] { "IN during", "RM in 03_09" }, Own = "Road_Plates" },
                new { Old = "Ph2_St05_1_IN_TR_Subbase_280mm_C_", Layer = Id(2, 5, 1), Converted = "IN_TR_Subbase_280mm_C", Means = new[] { "IN during" }, Own = "TR_Subbase_280mm_C" },
                new { Old = "Ph2_St01_5_RM_RM_Marwood_Barrier3", Layer = Id(2, 1, 5), Converted = "RM_Marwood_Barrier3", Means = new[] { "RM during" }, Own = "Marwood_Barrier3" }, // the proposal
                new { Old = "Ph2_St02_12_IN_RC_Hrd2_Ph1_St23_RM", Layer = Id(2, 2, 12), Converted = "IN_RC_Hrd2_RM_01_23", Means = new[] { "IN during", "RM in 01_23" }, Own = "RC_Hrd2" } // HS2
            };
            foreach (var row in table)
            {
                Assert.True(PlannerNaming.IsOldKindName(row.Old), row.Old);
                Assert.Equal(PlannerNamingKind.Old, PlannerNaming.ParseObjectNameV2(row.Old).Kind);
                var p = PlannerNaming.ParseObjectNameV2(row.Converted, row.Layer);
                Assert.Equal(
                    row.Converted + ": new " + row.Own + " [" + string.Join(", ", row.Means) + "] []",
                    row.Converted + ": " + KindText(p.Kind) + " " + p.Own + " [" + string.Join(", ", Means(row.Converted, row.Layer)) + "] [" + string.Join(", ", Codes(p.Issues)) + "]");
            }
            // The listed ones are old-kind, whatever Clean up proposes for them.
            foreach (var old in new[] { "Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM", "Ph2_St02_10_IN_RC_Hrd_004_from_PDF_Ph6_St05_2_RM" })
            {
                Assert.True(PlannerNaming.IsOldKindName(old), old);
            }
            // The HS2 reference no layer has is flagged: the Planner parks it TBC.
            Assert.Equal(new[] { "unknown-layer: IN_RC_Hrd2_RM_01_23" }, Said(PlannerNaming.NamingIssuesV2(new[] { Obj("IN_RC_Hrd2_RM_01_23", Id(2, 2, 12)) }, new[] { Id(2, 2, 12) })));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Which naming a model, a scene and a project uses (spec 2.3)

        [Fact]
        public void NamingV2_ANameIsOldKindWhenItHoldsAPhStTagAnywhereInAnyCapitals()
        {
            foreach (var n in new[]
            {
                "Ph2_St01_4_IN_Kerbs2",
                "IN_Kerb_Ph6_St05_RM",
                "ph1_st01_in_Kerb",
                "Ph1_St04-2_IN_Pile",
                "Ph1_St04.2_IN_Pile",
                "Kerb_Ph1_St01_IN", // no leading segment, so the old reader skips it; old-kind all the same
                "Kerb_Ph1_St02_RM not proved",
                "Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM" // the draft read its closing _RM as new
            })
            {
                Assert.True(PlannerNaming.IsOldKindName(n), n);
            }
            foreach (var n in new[] { "IN_Kerb", "Photo_St01_IN", "Ph1_St01_INX", "XPh1_St01_IN_Kerb", "Ph1_St18-09-26_IN" })
            {
                Assert.False(PlannerNaming.IsOldKindName(n), n);
            }
        }

        [Fact]
        public void NamingV2_TheLargerCountWinsATieGoesToTheNewNamesUntaggedAndRepeatsCountForNothing()
        {
            var none = new string[0];
            Assert.Equal(PlannerNamingKind.Old, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_A", "Ph1_St01_IN_B", "IN_C" }, none));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_A", "IN_B", "RM_C" }, none));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_A", "IN_B" }, none));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_A", "Ph1_St01_IN_A", "IN_B", "Kerb", "EUS_Kerb_001" }, none));
            Assert.Equal(PlannerNamingKind.Old, PlannerNaming.NamingOf(new[] { "Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM" }, none));
        }

        [Fact]
        public void NamingV2_OnATieADateInALayerNameMakesItOldTheTbcWordAloneDoesNot()
        {
            var dated = new[] { "01_01_Drainage_08-06-26_10-06-26" };
            // an old model's Ph/St objects matched one for one by names the old reader never read as tags (Kerb_RM)
            Assert.Equal(PlannerNamingKind.Old, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_Pipe", "Kerb_RM" }, dated));
            Assert.Equal(PlannerNamingKind.Old, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_Pipe", "Ph1_St01_IN_Kerb", "Kerb_RM", "Pipe_RM" }, dated.Concat(new[] { "01_02_Kerbs" })));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_Pipe", "Kerb_RM" }, new[] { "02_01_Drainage_TBC" }));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_Pipe", "Kerb_RM" }, new[] { "01_01_Drainage" }));
        }

        [Fact]
        public void NamingV2_TheCountsDecideWheneverTheyDiffer()
        {
            var dated = new[] { "01_01_Drainage_08-06-26_10-06-26" };
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_Pipe", "Kerb_RM", "Pipe_RM" }, dated));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "IN_A", "IN_B" }, dated));
            Assert.Equal(PlannerNamingKind.Old, PlannerNaming.NamingOf(new[] { "Ph1_St01_IN_A", "Ph1_St01_IN_B", "IN_C" }, new[] { "02_01_Kerbs" }));
            // a new-name model with one layer still noting TBC and one object left on an old tag: 4 new to 1 old
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new[] { "IN_A", "IN_B", "IN_C_RM", "IN_D", "Ph2_St02_IN_Old" }, new[] { "02_01_Kerbs_TBC", "02_02_Fence" }));
        }

        [Fact]
        public void NamingV2_WithNoTaggedObjectTheLayerNodesDecide()
        {
            Assert.Equal(PlannerNamingKind.Old, PlannerNaming.NamingOf(new[] { "Kerb" }, new[] { "01_00_START_DATE_01-06-26", "01_01_Drainage" }));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new string[0], new[] { "02_01_Drainage_TBC" }));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new string[0], new[] { "01_00_START_DATE", "01_01_Drainage" }));
            Assert.Equal(PlannerNamingKind.New, PlannerNaming.NamingOf(new string[0], new string[0]));
        }

        [Fact]
        public void NamingV2_AModelReadsFromBothReadingsOfItsLayers()
        {
            // the HRB test model: older hyphen sub-stages (03_01-1_...), which are no layer nodes to the new rule
            const string hrb = "03_01-1_Temporary_Hoarding_TBC_19-10-26_09-11-26";
            Assert.Equal("Old from Objects", ModelSaid(PlannerNaming.NamingOfModel(null, null, Map("Ph3_St01-1_IN_Works", hrb), Nodes(skipped: new[] { hrb }))));
            // a one-number dated layer holding nothing tagged
            Assert.Equal("Old from Layers", ModelSaid(PlannerNaming.NamingOfModel(null, null, Map(), Nodes(skipped: new[] { "01_Sheet_Piling_17-08-26_18-09-26" }))));
            // a new project's empty layers, then one with an object
            Assert.Equal("New from Layers", ModelSaid(PlannerNaming.NamingOfModel(null, null, Map(), Nodes(layers: new[] { "01_00_START_DATE", "02_01_Drainage" }))));
            Assert.Equal("New from Objects", ModelSaid(PlannerNaming.NamingOfModel(
                null, null, Map("IN_Kerb", "02_01_Drainage"), Nodes(layers: new[] { "02_01_Drainage" }, objects: Map("IN_Kerb", "02_01_Drainage")))));
        }

        [Fact]
        public void NamingV2_WithNoTaggedObjectUnderALayerEveryObjectOfTheModelElseItIsLeftToTheTool()
        {
            // an older segment-mode model, with no layers at all, and its material shards and split pieces folded first
            Assert.Equal("Old from Objects", ModelSaid(PlannerNaming.NamingOfModel(new[] { "Ph1_St01_IN_Kerb", "Ph1_St02_IN_Wall" }, null, null, null)));
            Assert.Equal("New from Objects", ModelSaid(PlannerNaming.NamingOfModel(new[] { "Kerb_RM", "Kerb_RM_2", "Kerb_RM_2_primitive0" }, Map("Kerb_RM_2", "Kerb_RM"), null, null)));
            Assert.Equal(new[] { "Kerb_RM", "Lamp_1" }, PlannerNaming.ModelObjectNames(new[] { "Kerb_RM", "Kerb_RM_2", "Kerb_RM_2_primitive0", "Lamp_1" }, Map("Kerb_RM_2", "Kerb_RM")));
            // nothing tagged, and no layer node: a number-led node that is no layer ID (1st Floor) is no layer either
            Assert.Null(PlannerNaming.NamingOfModel(null, null, null, null));
            Assert.Null(PlannerNaming.NamingOfModel(new[] { "1st Floor", "Lamp" }, null, null, Nodes(skipped: new[] { "1st Floor" })));
            Assert.Null(PlannerNaming.NamingOfModel(new[] { "Lamp" }, null, Map("Lamp", "05_Retainment"), null)); // an undated layer only the old reader has
            Assert.Null(PlannerNaming.NamingOfModel(new[] { "Lamp" }, null, Map("Lamp", "35_Service_Road_Setback_TBC"), null)); // TBC is no date
        }

        // ---------------------------------------------------------------------------------------------------------
        // Activity names and event layers (spec 3.2, 3.9)

        [Fact]
        public void ActivityV2_NamesAnEventsActivityOwnNameTypeAndId()
        {
            Assert.Equal("Road_Plates_IN_03_11", PlannerNaming.ActivityNameV2("Road_Plates", PlannerTaskType.Install, Id(3, 11)));
            Assert.Equal("Road_Plates_RM_03_09", PlannerNaming.ActivityNameV2("Road_Plates", PlannerTaskType.Dismantle, Id(3, 9)));
            Assert.Equal("RC_Hrd2_RM_01_23", PlannerNaming.ActivityNameV2("RC_Hrd2", PlannerTaskType.Dismantle, Id(1, 23)));
            Assert.Equal("Gates_IN_06_05_2", PlannerNaming.ActivityNameV2("Gates", PlannerTaskType.Install, Id(6, 5, 2)));
        }

        [Fact]
        public void ActivityV2_TheEventLayerACopyItsFoldersAnyOtherTheIdClosingItsNameElseItsFolders()
        {
            Assert.Equal("03_09", At("Road_Plates_RM_03_09", "03_11_Road_Plates"));
            Assert.Equal("03_11", At("Road_Plates_IN_03_11", "03_11_Road_Plates"));
            Assert.Equal("01_23", At("RC_Hrd2_RM_01_23", "02_02_12_Hoardings"));
            Assert.Equal("03_09", At("Road_Plates_RM_03_09 night shift", "03_11_Road_Plates"));
            Assert.Equal("03_11", At("Road_Plates_RM_03_09 (copy)", "03_11_Road_Plates"));
            Assert.Equal("04_01", At("Road_Plates_RM_03_09 (copy) (copy)", "04_01_Resurfacing"));
            Assert.Equal("06_05_2", At("Layer dates", "06_05_2_Site_Entrance"));
            Assert.Equal("02_01", At("Night closure 03_09", "02_01_Works")); // an ID without its type word is just text
            Assert.Equal("06_05_2", At("Gates_IN_06_05_2", null));
            Assert.Null(At("Layer dates", null));
            Assert.Null(At("Layer dates", "05_Retainment")); // one number is no layer ID
            Assert.Null(At("Layer dates", "_03_Cardington_St_Works")); // a phase group
            Assert.Equal("03_01", At("Kerb_in_02_01", "03_01_X")); // capitals only
        }

        [Fact]
        public void ActivityV2_AFoldersIdIsReadWithParseLayerName()
        {
            Assert.Equal(Id(6, 5, 2), PlannerNaming.FolderLayerId("06_05_2_Site_Entrance"));
            Assert.Equal(Id(6, 5, 2), PlannerNaming.FolderLayerId("6_5_2_Gates"));
            Assert.Equal(Id(6, 5), PlannerNaming.FolderLayerId("06_05_Gates"));
            Assert.Equal(Id(3, 9, 24), PlannerNaming.FolderLayerId("03_09_24_Hour_Closure")); // the trap the folder editor warns about
            Assert.Null(PlannerNaming.FolderLayerId("05_Retainment"));
            Assert.Null(PlannerNaming.FolderLayerId("Key dates"));
        }

        [Fact]
        public void ActivityV2_LayerIdsInTheOrderOfTheirNumbersAMissingSubStageFirst()
        {
            var ids = new[] { Id(2, 3), Id(2, 2, 10), Id(2, 2, 1), Id(2, 2), Id(2, 2, 3), Id(1, 23), Id(2, 2, 0) };
            Assert.Equal(new[] { "01_23", "02_02", "02_02_0", "02_02_1", "02_02_3", "02_02_10", "02_03" },
                ids.OrderBy(i => i, Comparer<PlannerLayerIdKey>.Create(PlannerNaming.CompareLayerIds)).Select(PlannerNaming.LayerIdText).ToArray());
            // a missing sub-stage before sub-stage 0, whichever comes first
            Assert.Equal(new[] { "02_02", "02_02_0" }, new[] { Id(2, 2, 0), Id(2, 2) }.OrderBy(i => i, Comparer<PlannerLayerIdKey>.Create(PlannerNaming.CompareLayerIds)).Select(PlannerNaming.LayerIdText).ToArray());
            Assert.True(PlannerNaming.CompareLayerIds(Id(2, 2), Id(2, 2, 0)) < 0);
            Assert.True(PlannerNaming.CompareLayerIds(Id(2, 2, 0), Id(2, 2)) > 0);
        }

        // ---------------------------------------------------------------------------------------------------------
        // The name trap (spec 2.1; folderName.test.ts, "knows the name trap" and its wording)

        [Fact]
        public void TrapV2_ATwoNumberIdThenANameStartingWithDigitsReadsAsThreeNumbers()
        {
            Assert.True(Trap("03_09_Closure", "03_09_24_Hour_Closure"));
            Assert.True(Trap("03_09_Closure", "03_09_24 Hour Closure"));
            Assert.False(Trap("03_09_Closure", "03_09_Closure_24_Hour"));
            Assert.False(Trap("03_09_Closure", "03_10_2_Closure")); // another stage: an ID change, not the trap
            Assert.False(Trap("03_09_2_Closure", "03_09_24_Closure"));
            Assert.False(Trap("New folder", "04_01_24_Hour_Closure"));
            Assert.Equal(
                "The name after 03_09 starts with a number and an underscore, so the number reads as a sub-stage: layer ID 03_09_24. " +
                "To keep 03_09, start the name after the ID with a letter.",
                PlannerNaming.NameTrapText(PlannerNaming.FolderLayerId("03_09_Closure"), PlannerNaming.FolderLayerId("03_09_24_Hour_Closure")));
            // A layer named in Max falls into it the same way: the ID typed, then the name.
            Assert.True(PlannerNaming.IsNameTrap(Id(3, 9), PlannerNaming.ParseLayerNameV2("03_09_24_Hour_Closure").Id));
            Assert.False(PlannerNaming.IsNameTrap(Id(3, 9), PlannerNaming.ParseLayerNameV2("03_09_Hour_Closure_24").Id));
        }

        // ---------------------------------------------------------------------------------------------------------
        // The port's character sets: JavaScript's \s, \S, digits and $ (the Planner's), never .NET's

        [Fact]
        public void PortV2_ReadsSpacesDigitsAndLineEndsAsThePlannerDoes()
        {
            var nel = ((char)0x85).ToString();  // .NET's \s takes it, JavaScript's does not
            var bom = ((char)0xFEFF).ToString(); // JavaScript's \s takes it, .NET's does not
            var arabic = new string(new[] { (char)0x0660, (char)0x0663 }); // digits to .NET's \d, not to JavaScript's

            Assert.Equal(Said("new", "Install", "Dismantle", null, "Kerb" + nel), Reads("IN_Kerb" + nel + "_RM"));
            Assert.Equal(Said("new", "Install", null, null, "Kerb", "note-tag"), Reads("IN_Kerb" + bom + "RM_X"));
            Assert.Equal(Said("new", "Install", null, null, "X_RM_" + arabic + "_09", "type-in-middle"), Reads("IN_X_RM_" + arabic + "_09"));
            Assert.Null(PlannerNaming.ParseLayerNameV2(arabic + "_01_Kerbs"));
            Assert.Empty(PlannerNaming.ParseLayerNameV2("02_01_Kerbs" + nel + "North").Issues);
            Assert.Null(PlannerNaming.ParseLayerNameV2("03_03" + bom + "Kerbs"));
            Assert.Equal("03_03_Kerbs", PlannerNaming.LayerNameSpacedAfterId("03_03" + bom + "Kerbs"));
            Assert.Equal("06_05_2", At("Gates_IN_06_05_2" + bom + "night works", null));
            Assert.False(PlannerNaming.IsOldKindName("Ph1_St0" + (char)0x0661 + "_IN_Kerb"));
        }

        // ---------------------------------------------------------------------------------------------------------
        // The Planner's own answers

        /// <summary>
        /// The Planner's answers for a large set of names and groups, made by running its phasingNaming.ts on them
        /// (PLANNER_NAMING_V2_CASES, written by a script outside the repo: the converted v30 GLB's nodes, the HS2 scene and
        /// layer list, the v30 rename list, the parity fixtures' scenes, every string of phasingNaming.test.ts, seeded random
        /// names, and random groups of them). Every answer of the port must be the Planner's. Skipped, and reported so, when the
        /// variable is not set; a file it names that is not there fails.
        /// </summary>
        [EnvironmentFact("PLANNER_NAMING_V2_CASES")]
        public void PlannerParity()
        {
            var path = EnvironmentFactAttribute.InputFile("PLANNER_NAMING_V2_CASES");
            JObject cases;
            using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            {
                cases = JObject.Load(reader); // names such as "2026-08-17" stay text
            }
            var differences = new List<string>();
            int compared = 0;
            int differing = 0;
            Action<string, string, string> check = (what, planner, port) =>
            {
                compared++;
                if (planner == port)
                {
                    return;
                }
                differing++;
                if (differences.Count < 30)
                {
                    differences.Add(what + "\n  Planner: " + planner.Replace("\n", "\n           ") + "\n  port:    " + port.Replace("\n", "\n           "));
                }
            };

            var names = (JArray)cases["names"];
            foreach (JObject c in names)
            {
                var name = (string)c["name"];
                check("name " + Quoted(name), (string)c["planner"], PortPerName(name));
            }
            foreach (JObject c in (JArray)cases["namingOf"])
            {
                var naming = PlannerNaming.NamingOf(Strings(c["objects"]), Strings(c["layers"]));
                check("namingOf " + c.ToString(Newtonsoft.Json.Formatting.None), (string)c["planner"], naming == PlannerNamingKind.Old ? "old" : "new");
            }
            foreach (JObject c in (JArray)cases["namingIssues"])
            {
                var objects = ((JArray)c["objects"]).Select(o => Obj((string)o["name"], IdOf(o["layer"]))).ToList();
                var known = ((JArray)c["known"]).Select(IdOf).ToList();
                var pieces = ((JArray)c["pieces"]).Select(p => new KeyValuePair<string, string>((string)p[0], (string)p[1])).ToList();
                var issues = PlannerNaming.NamingIssuesV2(objects, known, pieces);
                check("namingIssuesV2 " + c.ToString(Newtonsoft.Json.Formatting.None), (string)c["planner"],
                    string.Join("\n", issues.Select(i => PlannerNaming.CodeText(i.Code) + ":" + string.Join("|", i.Names))));
            }
            foreach (JObject c in (JArray)cases["namingOfModel"])
            {
                PlannerLayerNodeIndex nodes = null;
                if (c["nodes"].Type == JTokenType.Object)
                {
                    nodes = Nodes(Strings(c["nodes"]["layers"]).ToArray(), Pairs(c["nodes"]["objects"]), Strings(c["nodes"]["skipped"]).ToArray());
                }
                var meshNames = c["meshNames"].Type == JTokenType.Array ? Strings(c["meshNames"]) : null;
                var naming = PlannerNaming.NamingOfModel(meshNames, Pairs(c["meshSplits"]), Pairs(c["meshLayers"]), nodes);
                check("namingOfModel " + c.ToString(Newtonsoft.Json.Formatting.None), (string)c["planner"],
                    naming == null ? "null" : (naming.Naming == PlannerNamingKind.Old ? "old" : "new") + "|" + (naming.From == PlannerNamingSource.Objects ? "objects" : "layers"));
            }
            foreach (JObject c in (JArray)cases["compare"])
            {
                var sign = Math.Sign(PlannerNaming.CompareLayerIds(IdOf(c["a"]), IdOf(c["b"])));
                check("compareLayerIds " + c.ToString(Newtonsoft.Json.Formatting.None), ((int)c["planner"]).ToString(), sign.ToString());
            }

            output.WriteLine("Naming v2 parity with the Planner (" + (string)cases["commit"] + "): " + compared + " answers compared (" + names.Count + " names), "
                + differing + " differ.");
            foreach (var d in differences)
            {
                output.WriteLine(d);
            }
            Assert.True(differing == 0, differing + " of " + compared + " answers differ from the Planner's; the first:\n" + string.Join("\n", differences.Take(5)));
        }

        // One name's answers, as the case script writes the Planner's: one line each.
        private static string PortPerName(string n)
        {
            var p0 = PlannerNaming.ParseObjectNameV2(n);
            var p1 = PlannerNaming.ParseObjectNameV2(n, Id(6, 5, 2));
            var p2 = PlannerNaming.ParseObjectNameV2(n, Id(3, 9));
            var folder = PlannerNaming.FolderLayerId(n);
            var layer = PlannerNaming.ParseLayerNameV2(n);
            return string.Join("\n", new[]
            {
                ObjText(p0),
                ObjText(p1),
                ObjText(p2),
                EventsText(p0),
                EventsText(p1),
                layer == null ? "null" : IdText(layer.Id) + "|" + layer.Name + "|" + layer.Label + "|" + string.Join(",", Codes(layer.Issues)),
                PlannerNaming.LayerNameSpacedAfterId(n) ?? "null",
                Bool(PlannerNaming.LayerNameCarriesDates(n)),
                Bool(PlannerNaming.LayerNameHasDate(n)),
                Bool(PlannerNaming.IsOldKindName(n)),
                IdText(folder),
                IdText(PlannerNaming.ActivityEventLayer(n, null)),
                IdText(PlannerNaming.ActivityEventLayer(n, "03_11_Road_Plates")),
                IdText(PlannerNaming.ActivityEventLayer(n, n)),
                Bool(folder != null && PlannerNaming.LayerIdKey(folder) == "3_9_24"),
                PlannerNaming.ActivityNameV2(n, PlannerTaskType.Dismantle, Id(2, 2, 10))
            });
        }

        private static string ObjText(PlannerObjectNameV2 p)
        {
            return (KindText(p.Kind) ?? "-") + "|" + (TypeText(p.Lead) ?? "-") + "|" + (TypeText(p.Trail) ?? "-") + "|"
                + (p.Ref == null ? "-" : PlannerNaming.LayerIdKey(p.Ref)) + "|" + p.Own + "|" + string.Join(",", Codes(p.Issues));
        }

        private static string EventsText(PlannerObjectNameV2 p)
        {
            return string.Join(",", PlannerNaming.ObjectEventsV2(p).Select(e =>
                e.Type + ":" + (e.When == PlannerEventTime.During ? "during" : e.When == PlannerEventTime.End ? "end" : "in") + ":"
                + (e.Ref == null ? "-" : PlannerNaming.LayerIdKey(e.Ref))));
        }

        private static string IdText(PlannerLayerIdKey id)
        {
            return id == null ? "-" : PlannerNaming.LayerIdKey(id) + "/" + PlannerNaming.LayerIdText(id);
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Quoted(string name)
        {
            return Newtonsoft.Json.JsonConvert.ToString(name);
        }

        private static List<string> Strings(JToken array)
        {
            return ((JArray)array).Select(t => (string)t).ToList();
        }

        private static Dictionary<string, string> Pairs(JToken array)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in (JArray)array)
            {
                result[(string)pair[0]] = (string)pair[1];
            }
            return result;
        }

        private static PlannerLayerIdKey IdOf(JToken id)
        {
            if (id == null || id.Type == JTokenType.Null)
            {
                return null;
            }
            return new PlannerLayerIdKey((int)id[0], (int)id[1], id[2].Type == JTokenType.Null ? (int?)null : (int)id[2]);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers

        /// <summary>A layer ID from its numbers: Id(6, 5, 2) is 06_05_2.</summary>
        private static PlannerLayerIdKey Id(int phase, int stage, int? sub = null)
        {
            return new PlannerLayerIdKey(phase, stage, sub);
        }

        private static string[] Codes(IEnumerable<PlannerNamingCode> codes)
        {
            return codes.Select(PlannerNaming.CodeText).ToArray();
        }

        private static string TypeText(PlannerTaskType? type)
        {
            return type.HasValue ? type.Value.ToString() : null;
        }

        private static string KindText(PlannerNamingKind? kind)
        {
            return kind == PlannerNamingKind.New ? "new" : kind == PlannerNamingKind.Old ? "old" : null;
        }

        /// <summary>What a name reads as, as the Planner's test helper reads gives it: the reference written the current way.</summary>
        private static string Reads(string name, PlannerLayerIdKey ownLayer = null)
        {
            var p = PlannerNaming.ParseObjectNameV2(name, ownLayer);
            return Said(KindText(p.Kind), TypeText(p.Lead), TypeText(p.Trail), p.Ref == null ? null : PlannerNaming.LayerIdText(p.Ref), p.Own, Codes(p.Issues));
        }

        private static string Said(string kind, string lead, string trail, string reference, string own, params string[] issues)
        {
            return "kind=" + (kind ?? "null") + " lead=" + (lead ?? "null") + " trail=" + (trail ?? "null") + " ref=" + (reference ?? "null")
                + " own='" + own + "' issues=[" + string.Join(", ", issues) + "]";
        }

        /// <summary>What a name means, as the spec's table puts it: each event's type word and when ("IN during", "RM end", "RM in 06_05_2").</summary>
        private static string[] Means(string name, PlannerLayerIdKey ownLayer = null)
        {
            return PlannerNaming.ObjectEventsV2(PlannerNaming.ParseObjectNameV2(name, ownLayer))
                .Select(e => (e.Type == PlannerTaskType.Install ? "IN" : "RM") + " "
                    + (e.When == PlannerEventTime.In ? "in " + PlannerNaming.LayerIdText(e.Ref) : e.When == PlannerEventTime.During ? "during" : "end"))
                .ToArray();
        }

        private static string Layer(string name)
        {
            var p = PlannerNaming.ParseLayerNameV2(name);
            return p == null ? "null" : LayerSaid(p.Id, p.Name, p.Label, Codes(p.Issues));
        }

        private static string LayerSaid(PlannerLayerIdKey id, string name, string label, params string[] issues)
        {
            return "id=" + PlannerNaming.LayerIdKey(id) + " name='" + name + "' label='" + label + "' issues=[" + string.Join(", ", issues) + "]";
        }

        private static PlannerObjectLayer Obj(string name, PlannerLayerIdKey layer)
        {
            return new PlannerObjectLayer { Name = name, Layer = layer };
        }

        private static string[] Said(IEnumerable<PlannerNamingIssueV2> issues)
        {
            return issues.Select(i => PlannerNaming.CodeText(i.Code) + ": " + string.Join(" | ", i.Names)).ToArray();
        }

        /// <summary>Split pieces as pairs: a piece, the object it folds into, the next piece, ...</summary>
        private static List<KeyValuePair<string, string>> Pieces(params string[] pairs)
        {
            var result = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                result.Add(new KeyValuePair<string, string>(pairs[i], pairs[i + 1]));
            }
            return result;
        }

        private static Dictionary<string, string> Map(params string[] pairs)
        {
            var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                result[pairs[i]] = pairs[i + 1];
            }
            return result;
        }

        private static PlannerLayerNodeIndex Nodes(string[] layers = null, Dictionary<string, string> objects = null, string[] skipped = null)
        {
            var index = new PlannerLayerNodeIndex();
            index.Layers.AddRange(layers ?? new string[0]);
            foreach (var pair in objects ?? new Dictionary<string, string>())
            {
                index.Objects[pair.Key] = pair.Value;
            }
            index.Skipped.AddRange(skipped ?? new string[0]);
            return index;
        }

        private static string ModelSaid(PlannerModelNaming naming)
        {
            return naming == null ? "null" : naming.Naming + " from " + naming.From;
        }

        /// <summary>An activity's event layer, written the current way, or null.</summary>
        private static string At(string name, string folder)
        {
            var layer = PlannerNaming.ActivityEventLayer(name, folder);
            return layer == null ? null : PlannerNaming.LayerIdText(layer);
        }

        /// <summary>Does renaming a folder from one name to another fall into the name trap (layerFolderEdit's trap)?</summary>
        private static bool Trap(string from, string to)
        {
            return PlannerNaming.IsNameTrap(PlannerNaming.FolderLayerId(from), PlannerNaming.FolderLayerId(to));
        }
    }
}
