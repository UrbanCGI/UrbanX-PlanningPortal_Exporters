using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// Mirrors packages/domain/src/phasingNaming.test.ts in the Planner repository case for case. If one of these
    /// fails after a Planner change, port the change here too: the exporter must predict what the Planner reads.
    /// </summary>
    public class PlannerNamingTests
    {
        [Fact]
        public void MeshName_SingleLeadingInstallSegment()
        {
            var p = PlannerNaming.ParseMeshName("Ph1_St00_IN_RC_Hoardings");
            AssertSegment(p.Lead, "PH1", "St00", PlannerTaskType.Install);
            Assert.Null(p.Trail);
            Assert.Equal("RC_Hoardings", p.Description);
        }

        [Fact]
        public void MeshName_LeadIsIndependentOfDescription()
        {
            AssertSegment(PlannerNaming.ParseMeshName("Ph1_St00_IN_RC_Hoardings02").Lead, "PH1", "St00", PlannerTaskType.Install);
            AssertSegment(PlannerNaming.ParseMeshName("Ph1_St00_IN_RC_HoardingsSupports").Lead, "PH1", "St00", PlannerTaskType.Install);
        }

        [Fact]
        public void MeshName_BothSegmentsAcrossAnUnderscoredDescription()
        {
            var p = PlannerNaming.ParseMeshName("Ph1_St00_IN_RC_Hoardings_CardingtonSt_S_Ph2_St00_RM");
            AssertSegment(p.Lead, "PH1", "St00", PlannerTaskType.Install);
            AssertSegment(p.Trail, "PH2", "St00", PlannerTaskType.Dismantle);
            Assert.Equal("RC_Hoardings_CardingtonSt_S", p.Description);
            AssertSegment(PlannerNaming.ParseMeshName("Ph1_St00_IN_RC_Hoardings_Ph3_St00_RM").Trail, "PH3", "St00", PlannerTaskType.Dismantle);
        }

        [Fact]
        public void MeshName_DescriptionKeepsPartIndex()
        {
            Assert.Equal("Name_01", PlannerNaming.ParseMeshName("Ph1_St03_IN_Name_01_Ph1_St03_RM").Description);
            Assert.Equal("Name01", PlannerNaming.ParseMeshName("Ph1_St03_IN_Name01_Ph1_St03_RM").Description);
        }

        [Fact]
        public void MeshName_BothSegmentsWithNoDescription()
        {
            var p = PlannerNaming.ParseMeshName("Ph1_St00_IN_Ph2_St01_RM");
            AssertSegment(p.Lead, "PH1", "St00", PlannerTaskType.Install);
            AssertSegment(p.Trail, "PH2", "St01", PlannerTaskType.Dismantle);
            Assert.Equal(string.Empty, p.Description);
        }

        [Fact]
        public void MeshName_BareLeadingSegment()
        {
            var p = PlannerNaming.ParseMeshName("Ph2_St03_RM");
            AssertSegment(p.Lead, "PH2", "St03", PlannerTaskType.Dismantle);
            Assert.Null(p.Trail);
            Assert.Equal(string.Empty, p.Description);
        }

        [Fact]
        public void MeshName_TokensAreCaseInsensitive()
        {
            Assert.Equal(PlannerTaskType.Install, PlannerNaming.ParseMeshName("ph1_st00_in_Wall").Lead.Type);
        }

        [Fact]
        public void MeshName_NonMatchingNamesReturnNull()
        {
            Assert.Null(PlannerNaming.ParseMeshName("HRB_Construction_LOGISTICS"));
            Assert.Null(PlannerNaming.ParseMeshName("Cube"));
            Assert.Null(PlannerNaming.ParseMeshName("Phase1_Stage0_Install"));
            Assert.Null(PlannerNaming.ParseMeshName(null));
        }

        [Fact]
        public void MeshName_NoteAfterTheTrailingSegment()
        {
            var p = PlannerNaming.ParseMeshName("Ph1_St22_IN_RC_Hrd_Bridge_N_Ph2_St00_RM not proved (assumed based on 120lm)");
            AssertSegment(p.Trail, "PH2", "St00", PlannerTaskType.Dismantle);
            Assert.Equal("RC_Hrd_Bridge_N", p.Description);
            Assert.Equal("St00", PlannerNaming.ParseMeshName("Ph1_St10_IN_X_Ph2_St00_RM (1)").Trail.ActivityName);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St10_IN_X_Ph2_St00_RM_extra").Trail);
        }

        [Fact]
        public void LayerName_DateRange()
        {
            var p = PlannerNaming.ParseLayerName("01_Sheet_Piling_SA&DS3_17-08-26_18-09-26");
            Assert.Equal("01", p.Order);
            Assert.Equal(1, p.Stage);
            Assert.Equal("Sheet_Piling_SA&DS3", p.Name);
            Assert.Equal("01_Sheet_Piling_SA&DS3", p.Label);
            Assert.False(p.Tbc);
            Assert.Equal("2026-08-17", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2026-09-18", PlannerNaming.ToIsoDate(p.End));
            Assert.Empty(p.Issues);
        }

        [Fact]
        public void LayerName_SingleDay()
        {
            var p = PlannerNaming.ParseLayerName("09_Cardington_Subgrade_Install_30-10-26");
            Assert.Equal("2026-10-30", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2026-10-30", PlannerNaming.ToIsoDate(p.End));
            Assert.Equal("09_Cardington_Subgrade_Install", p.Label);
            Assert.False(p.Tbc);
        }

        [Fact]
        public void LayerName_TbcWithoutDatesIsNotAnIssue()
        {
            var p = PlannerNaming.ParseLayerName("35_Service_Road_Setback_TBC");
            Assert.True(p.Tbc);
            Assert.Null(p.Start);
            Assert.Null(p.End);
            Assert.Equal("35_Service_Road_Setback", p.Label);
            Assert.Empty(p.Issues);
        }

        [Fact]
        public void LayerName_TbcKeepsProvisionalDates()
        {
            var p = PlannerNaming.ParseLayerName("12_Kerbing_TBC_14-11-26_16-11-26");
            Assert.True(p.Tbc);
            Assert.Equal("2026-11-14", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2026-11-16", PlannerNaming.ToIsoDate(p.End));
            Assert.Equal("12_Kerbing", p.Label);
        }

        [Fact]
        public void LayerName_DuplicateSuffixIsStripped()
        {
            Assert.Equal("07_Cardington_Kerb_Removal", PlannerNaming.ParseLayerName("07_Cardington_Kerb_Removal_26-10-26_27-10-26 (1)").Label);
        }

        [Fact]
        public void LayerName_LenientAboutSpacesSubNumbersAndSingleDigitDays()
        {
            var p = PlannerNaming.ParseLayerName("06-1 Temporary Hoarding - Erection_19-10-26_19-10-26");
            Assert.Equal("06-1", p.Order);
            Assert.Equal(6, p.Stage);
            Assert.Equal("Temporary Hoarding - Erection", p.Name);
            Assert.Equal("2026-10-19", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2026-10-19", PlannerNaming.ToIsoDate(p.End));

            var q = PlannerNaming.ParseLayerName("10 Hoarding Install - 63. No posts 6-11-26_12-11-26");
            Assert.Equal("2026-11-06", PlannerNaming.ToIsoDate(q.Start));
            Assert.Equal("2026-11-12", PlannerNaming.ToIsoDate(q.End));
            Assert.Equal("Hoarding Install - 63. No posts", q.Name);
        }

        [Fact]
        public void LayerName_TextAfterTheDatesStaysInTheName()
        {
            var p = PlannerNaming.ParseLayerName("05 MainRd_Hoardings install_17-10-26_18-10-26_Not proved");
            Assert.Equal("2026-10-17", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2026-10-18", PlannerNaming.ToIsoDate(p.End));
            Assert.Equal("MainRd_Hoardings install_Not proved", p.Name);
        }

        [Fact]
        public void LayerName_UndatedIsTbcAndReported()
        {
            var p = PlannerNaming.ParseLayerName("01 Hoardings Removal");
            Assert.True(p.Tbc);
            Assert.Null(p.Start);
            Assert.Equal(new[] { "no dates" }, p.Issues);
            Assert.Equal("01_Hoardings Removal", p.Label);
        }

        [Fact]
        public void LayerName_FinishBeforeStartCollapsesAndFlags()
        {
            var p = PlannerNaming.ParseLayerName("30_CycleLane_Splitter_Island_Install_17-02-27_23-02-17");
            Assert.Equal("2027-02-17", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2027-02-17", PlannerNaming.ToIsoDate(p.End));
            Assert.True(p.Tbc);
            Assert.Single(p.Issues);
            Assert.Contains("before start", p.Issues[0]);
        }

        [Fact]
        public void LayerName_ImpossibleDateIsRejectedButTheOtherKept()
        {
            var p = PlannerNaming.ParseLayerName("03_Piling_31-02-26_05-03-26");
            Assert.Equal(new[] { "unreadable date \"31-02-26\"" }, p.Issues);
            Assert.Equal("2026-03-05", PlannerNaming.ToIsoDate(p.Start));
            Assert.True(p.Tbc);
        }

        [Fact]
        public void LayerName_BareOrder()
        {
            var p = PlannerNaming.ParseLayerName("00");
            Assert.Equal("00", p.Label);
            Assert.Equal(0, p.Stage);
            Assert.Contains("no activity name", p.Issues);
        }

        [Fact]
        public void LayerName_WithoutLeadingNumberReturnsNull()
        {
            Assert.Null(PlannerNaming.ParseLayerName("St1_Shape1001"));
            Assert.Null(PlannerNaming.ParseLayerName("EUS_Con_HRB_RC_Road_Barriers"));
            Assert.Null(PlannerNaming.ParseLayerName("Ph1_St01_IN_Sheet_Pile_1"));
            Assert.Null(PlannerNaming.ParseLayerName(null));
        }

        [Fact]
        public void LayerName_SizesAndOrderTokensAreNotDates()
        {
            var p = PlannerNaming.ParseLayerName("23_Road_Course_100mm_09-02-27");
            Assert.Equal("Road_Course_100mm", p.Name);
            Assert.Equal("2027-02-09", PlannerNaming.ToIsoDate(p.Start));
            Assert.Empty(p.Issues);
        }

        [Fact]
        public void MeshName_SubStage()
        {
            var p = PlannerNaming.ParseMeshName("Ph2_St05.2_IN_TR_Subbase_280mm_C_");
            AssertSegment(p.Lead, "PH2", "St05.2", PlannerTaskType.Install);
            Assert.Equal(5, p.Lead.Stage);
            Assert.Equal(2, p.Lead.SubStage.Value);
            Assert.Equal("TR_Subbase_280mm_C_", p.Description);
            Assert.Null(p.Trail);

            var q = PlannerNaming.ParseMeshName("Ph2_St03.2_IN_Utility_Crossing_SB_H_Ph1_St14_RM");
            Assert.Equal(3, q.Lead.Stage);
            Assert.Equal(2, q.Lead.SubStage.Value);
            Assert.Equal(14, q.Trail.Stage);
            Assert.False(q.Trail.SubStage.HasValue);
            Assert.Equal("Utility_Crossing_SB_H", q.Description);

            var r = PlannerNaming.ParseMeshName("Ph3_St01.1_RM_RC_Hrd_CardingtonSt_N_Ph3_St07.2_IN");
            AssertSegment(r.Trail, "PH3", "St07.2", PlannerTaskType.Install);
            Assert.Equal(2, r.Trail.SubStage.Value);

            Assert.Equal(10, PlannerNaming.ParseMeshName("Ph1_St03.10_IN_Kerb").Lead.SubStage.Value);
            Assert.Equal(2, PlannerNaming.ParseMeshName("Ph1_St3.2_IN_Kerb").Lead.SubStage.Value);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03._IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03.x_IN_Kerb"));
            Assert.Equal(PlannerLegacyForm.Dot, p.Lead.LegacyForm);
        }

        [Fact]
        public void MeshName_UnderscoreSubStage_TheExamplesBothParsersReadAlike()
        {
            // phasingNaming.test.ts: "reads the underscore sub-stage, and the older hyphen and dot as the same stage, flagged by kind".
            var current = PlannerNaming.ParseMeshName("Ph1_St04_2_IN_Hoarding");
            AssertSegment(current.Lead, "PH1", "St04_2", PlannerTaskType.Install);
            Assert.Equal(new object[] { 1, 4, 2, PlannerLegacyForm.None }, new object[] { current.Lead.Phase, current.Lead.Stage, current.Lead.SubStage, current.Lead.LegacyForm });
            Assert.Null(current.Trail);
            Assert.Equal("Hoarding", current.Description);
            Assert.False(current.Legacy);
            var hyphen = PlannerNaming.ParseMeshName("Ph1_St04-2_IN_Hoarding");
            Assert.Equal(new object[] { 1, 4, 2, PlannerLegacyForm.Hyphen, "Hoarding" }, new object[] { hyphen.Lead.Phase, hyphen.Lead.Stage, hyphen.Lead.SubStage, hyphen.Lead.LegacyForm, hyphen.Description });
            var dot = PlannerNaming.ParseMeshName("Ph1_St04.2_IN_Hoarding");
            Assert.Equal(new object[] { 1, 4, 2, PlannerLegacyForm.Dot, "Hoarding" }, new object[] { dot.Lead.Phase, dot.Lead.Stage, dot.Lead.SubStage, dot.Lead.LegacyForm, dot.Description });
            // The activity token keeps what the model wrote.
            Assert.Equal(new[] { "St04_2", "St04-2", "St04.2" }, new[] { current.Lead.ActivityName, hyphen.Lead.ActivityName, dot.Lead.ActivityName });

            // The trailing tag.
            var trail = PlannerNaming.ParseMeshName("Ph0_St00_IN_RC_Hrd_Ph1_St06_2_RM");
            AssertSegment(trail.Lead, "PH0", "St00", PlannerTaskType.Install);
            AssertSegment(trail.Trail, "PH1", "St06_2", PlannerTaskType.Dismantle);
            Assert.Equal(new object[] { 1, 6, 2, PlannerLegacyForm.None }, new object[] { trail.Trail.Phase, trail.Trail.Stage, trail.Trail.SubStage, trail.Trail.LegacyForm });
            Assert.Equal("RC_Hrd", trail.Description);

            // A part number after the type is the description, never a sub-stage or a trailing tag (no "_Ph" before it).
            var part = PlannerNaming.ParseMeshName("Ph1_St04_IN_Kerb_2_RM");
            AssertSegment(part.Lead, "PH1", "St04", PlannerTaskType.Install);
            Assert.Null(part.Trail);
            Assert.Equal("Kerb_2_RM", part.Description);

            // IN/RM must come straight after the sub-stage, so a date in the stage's place is no stage.
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St18_09_26_IN_x"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03_IN_Kerb_Ph2_St18_09_26_RM").Trail);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03__2_IN_Kerb")); // two underscores
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03_x_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03_2x_IN_Kerb"));
        }

        [Fact]
        public void MeshName_UnderscoreSubStageInBothTagsAndUnpadded()
        {
            var q = PlannerNaming.ParseMeshName("Ph2_St01_2_IN_Water_Barrier3_Ph2_St02_7_RM");
            Assert.Equal(new object[] { 1, 2 }, new object[] { q.Lead.Stage, q.Lead.SubStage });
            Assert.Equal(new object[] { 2, 7 }, new object[] { q.Trail.Stage, q.Trail.SubStage });
            Assert.Equal("St02_7", q.Trail.ActivityName);
            Assert.Equal("Water_Barrier3", q.Description);
            var note = PlannerNaming.ParseMeshName("Ph1_St22_IN_Hrd_Ph2_St00_1_RM not proved").Trail;
            Assert.Equal(new object[] { 0, 1 }, new object[] { note.Stage, note.SubStage });
            var bare = PlannerNaming.ParseMeshName("Ph1_St03_2_RM");
            AssertSegment(bare.Lead, "PH1", "St03_2", PlannerTaskType.Dismantle);
            Assert.Equal(string.Empty, bare.Description);
            Assert.Equal(new object[] { 3, 2 }, new object[] { PlannerNaming.ParseMeshName("Ph1_St3_2_IN_Kerb").Lead.Stage, PlannerNaming.ParseMeshName("Ph1_St3_2_IN_Kerb").Lead.SubStage });
            Assert.Equal(10, PlannerNaming.ParseMeshName("Ph1_St03_10_IN_Kerb").Lead.SubStage.Value);
            Assert.Equal(1, PlannerNaming.ParseMeshName("Ph1_St03_1_IN_Kerb").Lead.SubStage.Value);
            // Hyphens and full stops outside the sub-stage's place are the description's.
            Assert.Equal("1.8_x_2.4_Heras_Panel", PlannerNaming.ParseMeshName("Ph1_St04_2_IN_1.8_x_2.4_Heras_Panel").Description);
            Assert.Equal("TR_Sub-base_280mm", PlannerNaming.ParseMeshName("Ph2_St05_2_IN_TR_Sub-base_280mm").Description);
            Assert.False(PlannerNaming.ParseMeshName("Ph2_St05_IN_TR_Sub-base_280mm").Lead.SubStage.HasValue);
        }

        [Fact]
        public void LayerName_SubNumberIsTheSubStage()
        {
            var p = PlannerNaming.ParseLayerName("06-1 Temporary Hoarding - Erection_19-10-26_19-10-26");
            Assert.Equal(6, p.Stage);
            Assert.Equal(1, p.SubStage.Value);

            var q = PlannerNaming.ParseLayerName("05.2_Subbase_B_03-11-26_04-11-26");
            Assert.Equal("05.2", q.Order);
            Assert.Equal(5, q.Stage);
            Assert.Equal(2, q.SubStage.Value);
            Assert.Equal("05.2_Subbase_B", q.Label);
            Assert.Equal("2026-11-03", PlannerNaming.ToIsoDate(q.Start));
            Assert.Empty(q.Issues);

            Assert.False(PlannerNaming.ParseLayerName("01_Sheet_Piling_SA&DS3_17-08-26_18-09-26").SubStage.HasValue);
            Assert.Null(PlannerNaming.ParseLayerName("3.5m_Barrier_17-10-26")); // a size, not an order
            // a dotted number needs an underscore after it: dimension-led group names are not layers
            Assert.Null(PlannerNaming.ParseLayerName("2.4 High Hoarding"));
            Assert.Null(PlannerNaming.ParseLayerName("1.8 x 2.4 Heras Panel"));
            Assert.Null(PlannerNaming.ParseLayerName("05.2 Subbase_03-11-26"));
            Assert.Equal(1, PlannerNaming.ParseLayerName("06-1 Temporary Hoarding").SubStage.Value); // the dashed form stays lenient
        }

        [Fact]
        public void OnlyAsciiDigitsAreDigits()
        {
            // The Planner's JavaScript \d is ASCII-only; .NET's \d is not — the port must agree with the Planner.
            Assert.Null(PlannerNaming.ParseMeshName("Ph١_St٠٣_IN_x"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03.٢_IN_x"));
            Assert.Null(PlannerNaming.ParseLayerName("٠١_Name_17-08-26"));
            Assert.Null(PlannerNaming.ParseLayerName("01_Name_1٧-08-26").Start); // not a date at all
        }

        [Fact]
        public void SameStage_AWholeStageCoversItsSubStages()
        {
            Assert.True(PlannerNaming.SameStage(5, null, 5, null));
            Assert.True(PlannerNaming.SameStage(5, null, 5, 1)); // group 05 dates St05_1 work
            Assert.True(PlannerNaming.SameStage(5, 1, 5, null)); // object St05 under group 01_05_1, as before
            Assert.True(PlannerNaming.SameStage(5, 1, 5, 1));
            Assert.False(PlannerNaming.SameStage(5, 1, 5, 2));
            Assert.False(PlannerNaming.SameStage(5, null, 6, null));
            Assert.False(PlannerNaming.SameStage(5, 1, 6, 1));
            Assert.Equal("St05", PlannerNaming.StageText(5, null));
            Assert.Equal("St05_1", PlannerNaming.StageText(5, 1));
            Assert.Equal("St12_10", PlannerNaming.StageText(12, 10));
        }

        [Fact]
        public void StageNumber_AfterAnUnderscoreAHyphenOrAFullStop()
        {
            // phasingNaming.test.ts: "parses a stage token with or without a sub-stage, after an underscore, a hyphen or a dot".
            foreach (var token in new[] { "03_2", "03-2", "03.2", "01 2", "01__2", "01_ 2" })
            {
                int stage;
                int? subStage;
                PlannerNaming.ParseStageNumber(token, out stage, out subStage);
                Assert.True(stage == (token.StartsWith("03", System.StringComparison.Ordinal) ? 3 : 1) && subStage == 2, token);
            }
        }

        [Fact]
        public void MeshName_HyphenSubStageIsTheOldFormStillRead()
        {
            var p = PlannerNaming.ParseMeshName("Ph1_St04-2_IN_Sheet_Pile_6");
            AssertSegment(p.Lead, "PH1", "St04-2", PlannerTaskType.Install);
            Assert.Equal(4, p.Lead.Stage);
            Assert.Equal(2, p.Lead.SubStage.Value);
            Assert.Equal(PlannerLegacyForm.Hyphen, p.Lead.LegacyForm);
            Assert.True(p.Lead.Legacy);
            Assert.True(p.Legacy);
            Assert.Equal("Sheet_Pile_6", p.Description);

            var q = PlannerNaming.ParseMeshName("Ph2_St01-8_IN_RC_Hrd_from_PDF_04_Ph6_St05-2_RM");
            AssertSegment(q.Trail, "PH6", "St05-2", PlannerTaskType.Dismantle);
            Assert.Equal(2, q.Trail.SubStage.Value);
            Assert.Equal(PlannerLegacyForm.Hyphen, q.Trail.LegacyForm);
            Assert.Equal("RC_Hrd_from_PDF_04", q.Description);
            Assert.Equal(PlannerLegacyForm.Hyphen, PlannerNaming.ParseMeshName("Ph1_St04_2_IN_Pile_Ph2_St05-2_RM").Trail.LegacyForm);
            Assert.Equal(PlannerLegacyForm.None, PlannerNaming.ParseMeshName("Ph1_St04_2_IN_Pile_Ph2_St05-2_RM").Lead.LegacyForm);
            Assert.Equal(10, PlannerNaming.ParseMeshName("Ph1_St03-10_IN_Kerb").Lead.SubStage.Value);
            Assert.Equal(3, PlannerNaming.ParseMeshName("Ph1_St3-2_IN_Kerb").Lead.Stage);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03-_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03-x_IN_Kerb"));
        }

        [Fact]
        public void MeshName_AllThreeSpellingsReadTheSameAndTheOldOnesAreMarked()
        {
            var underscore = PlannerNaming.ParseMeshName("Ph1_St04_2_IN_Sheet_Pile_6");
            var dot = PlannerNaming.ParseMeshName("Ph1_St04.2_IN_Sheet_Pile_6");
            var hyphen = PlannerNaming.ParseMeshName("Ph1_St04-2_IN_Sheet_Pile_6");
            foreach (var old in new[] { dot, hyphen })
            {
                Assert.Equal(underscore.Lead.Stage, old.Lead.Stage);
                Assert.Equal(underscore.Lead.SubStage, old.Lead.SubStage);
                Assert.Equal(underscore.Description, old.Description);
                Assert.True(PlannerNaming.SameStage(old.Lead.Stage, old.Lead.SubStage, underscore.Lead.Stage, underscore.Lead.SubStage));
            }
            Assert.Equal(new[] { PlannerLegacyForm.None, PlannerLegacyForm.Hyphen, PlannerLegacyForm.Dot }, new[] { underscore.Lead.LegacyForm, hyphen.Lead.LegacyForm, dot.Lead.LegacyForm });
            Assert.Equal("St04.2", dot.Lead.ActivityName); // the activity keeps the spelling of the model
            Assert.True(dot.Legacy);
            Assert.False(underscore.Legacy);
            var mixed = PlannerNaming.ParseMeshName("Ph2_St01-8_IN_X_Ph6_St05.2_RM"); // either tag may be an old one
            Assert.Equal(new[] { PlannerLegacyForm.Hyphen, PlannerLegacyForm.Dot }, new[] { mixed.Lead.LegacyForm, mixed.Trail.LegacyForm });
            Assert.True(PlannerNaming.ParseMeshName("Ph2_St01_8_IN_X_Ph6_St05.2_RM").Legacy);
            Assert.False(PlannerNaming.ParseMeshName("Ph2_St01_8_IN_X_Ph6_St05.2_RM").Lead.Legacy);
            Assert.False(PlannerNaming.ParseMeshName("Ph1_St04_IN_1.8_x_2.4_Heras_Panel").Legacy); // full stops in the description are not sub-stages
            Assert.False(PlannerNaming.ParseMeshName("Ph2_St05_IN_TR_Sub-base_280mm").Legacy);     // nor hyphens
            Assert.False(PlannerNaming.ParseMeshName("Ph1_St04_IN_Pile").Legacy);
        }

        [Fact]
        public void MeshName_ADateIsNeverAStage()
        {
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St17-08-26_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St17_08_26_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St04-2-1_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St04_2_1_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St01_IN_X_Ph2_St17-08-26_RM").Trail);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St01_IN_X_Ph2_St17_08_26_RM").Trail);
        }

        [Fact]
        public void LayerName_OneTwoOrThreeNumbers_TheExamplesBothParsersReadAlike()
        {
            // One number: the stage, the phase from its objects.
            var one = PlannerNaming.ParseLayerName("05_Hoardings_17-08-26");
            Assert.Equal(new object[] { "05", null, 5, null, PlannerLegacyForm.None, "Hoardings", "05_Hoardings", "2026-08-17" },
                new object[] { one.Order, one.Phase, one.Stage, one.SubStage, one.LegacyForm, one.Name, one.Label, PlannerNaming.ToIsoDate(one.Start) });
            // Two numbers: the phase, then the stage.
            var two = PlannerNaming.ParseLayerName("02_01_Sheet_Piling_17-08-26");
            Assert.Equal(new object[] { "02", 2, 1, null, PlannerLegacyForm.None, "Sheet_Piling", "02_01_Sheet_Piling", "2026-08-17" },
                new object[] { two.Order, two.Phase, two.Stage, two.SubStage, two.LegacyForm, two.Name, two.Label, PlannerNaming.ToIsoDate(two.Start) });
            // Three numbers: the phase, the stage and its sub-stage.
            var three = PlannerNaming.ParseLayerName("02_01_2_Retainment_17-08-26");
            Assert.Equal(new object[] { "02", 2, 1, 2, PlannerLegacyForm.None, "Retainment", "02_01_2_Retainment", false, "2026-08-17", "2026-08-17" },
                new object[] { three.Order, three.Phase, three.Stage, three.SubStage, three.LegacyForm, three.Name, three.Label, three.Tbc, PlannerNaming.ToIsoDate(three.Start), PlannerNaming.ToIsoDate(three.End) });
            Assert.Empty(three.Issues);
        }

        [Fact]
        public void LayerName_ANumberFollowedByAHyphenEndsNoId()
        {
            // A date after the ID stays a date.
            AssertLayer("02_01_17-08-26_Kerbs", 2, 1, null, "Kerbs", "2026-08-17");
            Assert.Equal("02_01_Kerbs", PlannerNaming.ParseLayerName("02_01_17-08-26_Kerbs").Label);
            AssertLayer("01_18-09-26_Kerbs", null, 1, null, "Kerbs", "2026-09-18");
            AssertLayer("02_01_2-08-26_Kerbs", 2, 1, null, "Kerbs", "2026-08-02"); // a single-digit day straight after the stage
            var s = PlannerNaming.ParseLayerName("02_01_2_17-08-26_18-08-26");   // the full ID, then the dates, and no name
            AssertLayer("02_01_2_17-08-26_18-08-26", 2, 1, 2, string.Empty, "2026-08-17");
            Assert.Equal("02_01_2", s.Label);
            Assert.Equal("2026-08-18", PlannerNaming.ToIsoDate(s.End));
            Assert.Equal(new[] { "no activity name" }, s.Issues);
            AssertLayer("02_01_2-3_X", 2, 1, null, "2-3_X", null);              // a hyphen after the third number ends no ID
        }

        [Fact]
        public void LayerName_ThreeNumbersWithTbcProvisionalDatesAndTheDuplicateSuffix()
        {
            var tbc = PlannerNaming.ParseLayerName("03_06_3_Hoarding_installation_TBC_06-01-27");
            AssertLayer("03_06_3_Hoarding_installation_TBC_06-01-27", 3, 6, 3, "Hoarding_installation", "2027-01-06");
            Assert.True(tbc.Tbc);
            Assert.Empty(tbc.Issues);
            var undated = PlannerNaming.ParseLayerName("02_02_12_Hoarding_Install2_TBC");
            AssertLayer("02_02_12_Hoarding_Install2_TBC", 2, 2, 12, "Hoarding_Install2", null);
            Assert.True(undated.Tbc);
            Assert.Empty(undated.Issues);
            Assert.Equal(PlannerNaming.ParseLayerName("02_01_2_Construction_31-08-26_17-10-26").Label,
                PlannerNaming.ParseLayerName("02_01_2_Construction_31-08-26_17-10-26 (1)").Label);
            var bare = PlannerNaming.ParseLayerName("02_01_2");
            AssertLayer("02_01_2", 2, 1, 2, string.Empty, null);
            Assert.Equal("02_01_2", bare.Label);
            Assert.Equal(new[] { "no activity name", "no dates" }, bare.Issues);
        }

        [Fact]
        public void LayerName_UnderscoresOrSpacesAnyRunOfThemBetweenTheNumbers()
        {
            var spaced = PlannerNaming.ParseLayerName("02 01 2 Construction of Phase1 TM_31-08-26");
            AssertLayer("02 01 2 Construction of Phase1 TM_31-08-26", 2, 1, 2, "Construction of Phase1 TM", "2026-08-31");
            Assert.Equal("02_01 2 Construction of Phase1 TM", spaced.Label);
            var runs = PlannerNaming.ParseLayerName("02__01__2_Kerbs_17-08-26");
            AssertLayer("02__01__2_Kerbs_17-08-26", 2, 1, 2, "Kerbs", "2026-08-17");
            Assert.Equal("02_01_2_Kerbs", runs.Label);
            AssertLayer("02_01_ 2_Kerbs", 2, 1, 2, "Kerbs", null);
            AssertLayer("2_1_2_Barrier_31-08-26", 2, 1, 2, "Barrier", "2026-08-31"); // single digits
        }

        [Fact]
        public void LayerName_ASubStageIsOneOrTwoDigitsFollowedByAnUnderscoreASpaceOrTheEnd()
        {
            AssertLayer("02_01_12_X", 2, 1, 12, "X", null);
            AssertLayer("02_01_123_X", 2, 1, null, "123_X", null);                     // three digits are no sub-stage
            AssertLayer("02_01_100mm_Kerb_09-02-27", 2, 1, null, "100mm_Kerb", "2027-02-09");
            AssertLayer("02_01_2.4 High Hoarding_18-09-26", 2, 1, null, "2.4 High Hoarding", "2026-09-18"); // a dimension
            Assert.False(PlannerNaming.ParseLayerName("02_01_2.4 High Hoarding_18-09-26").Legacy);
            AssertLayer("02_01_2.4_High_Hoarding_18-09-26", 2, 1, null, "2.4_High_Hoarding", "2026-09-18");
            AssertLayer("02_02_10_18-09-26_Integration", 2, 2, 10, "Integration", "2026-09-18");
        }

        [Fact]
        public void LayerName_TheAcceptedConsequences()
        {
            // A bare number after a two-number ID is now the sub-stage (once phase 2, stage 1, named "2_Name"); one after a
            // one-number ID is the stage (phase 6, stage 2, as since 2026-10-13).
            AssertLayer("02_01_2_Name_17-08-26", 2, 1, 2, "Name", "2026-08-17");
            AssertLayer("06_2_Name_17-08-26", 6, 2, null, "Name", "2026-08-17");
            // A first number of three digits, or an old sub-numbered one, is no phase.
            AssertLayer("100_01_2_Kerb_09-02-27", null, 100, null, "01_2_Kerb", "2027-02-09");
            var q = PlannerNaming.ParseLayerName("06-2_01_2_Kerb_09-02-27");
            AssertLayer("06-2_01_2_Kerb_09-02-27", null, 6, 2, "01_2_Kerb", "2027-02-09");
            Assert.Equal(PlannerLegacyForm.Hyphen, q.LegacyForm);
        }

        [Fact]
        public void LayerName_TheOldFormsReadAsTheThreeNumbersDo()
        {
            var underscore = PlannerNaming.ParseLayerName("01_04_2_Phase1_Retainment_Installation_18-09-26_23-10-26");
            var hyphen = PlannerNaming.ParseLayerName("01_04-2_Phase1_Retainment_Installation_18-09-26_23-10-26");
            var dot = PlannerNaming.ParseLayerName("01_04.2_Phase1_Retainment_Installation_18-09-26_23-10-26");
            Assert.Equal("01", underscore.Order);
            Assert.Equal(1, underscore.Phase.Value); // phase then stage
            Assert.Equal(4, underscore.Stage);
            Assert.Equal(2, underscore.SubStage.Value);
            Assert.Equal("Phase1_Retainment_Installation", underscore.Name); // the name after the ID
            Assert.Equal("01_04_2_Phase1_Retainment_Installation", underscore.Label);
            Assert.Equal("2026-09-18", PlannerNaming.ToIsoDate(underscore.Start));
            Assert.Equal("2026-10-23", PlannerNaming.ToIsoDate(underscore.End));
            Assert.False(underscore.Tbc);
            Assert.Empty(underscore.Issues);
            foreach (var old in new[] { hyphen, dot })
            {
                Assert.Equal(underscore.Order, old.Order);
                Assert.Equal(underscore.Phase, old.Phase);
                Assert.Equal(underscore.Stage, old.Stage);
                Assert.Equal(underscore.SubStage, old.SubStage);
                Assert.Equal(underscore.Name, old.Name);
                Assert.Equal(PlannerNaming.ToIsoDate(underscore.Start), PlannerNaming.ToIsoDate(old.Start));
                Assert.Equal(PlannerNaming.ToIsoDate(underscore.End), PlannerNaming.ToIsoDate(old.End));
                Assert.Equal(underscore.Tbc, old.Tbc);
                Assert.Empty(old.Issues);
            }
            Assert.Equal("01_04-2_Phase1_Retainment_Installation", hyphen.Label); // the label keeps the spelling
            Assert.Equal(new[] { PlannerLegacyForm.None, PlannerLegacyForm.Hyphen, PlannerLegacyForm.Dot }, new[] { underscore.LegacyForm, hyphen.LegacyForm, dot.LegacyForm });
            Assert.False(underscore.Legacy);
            Assert.True(hyphen.Legacy);
            Assert.True(dot.Legacy);

            // One number with an old sub-stage: the stage, its phase its objects'.
            var p = PlannerNaming.ParseLayerName("06-2_X_17-08-26");
            Assert.Equal(new object[] { "06-2", null, 6, 2, PlannerLegacyForm.Hyphen, "X", "06-2_X" }, new object[] { p.Order, p.Phase, p.Stage, p.SubStage, p.LegacyForm, p.Name, p.Label });
            var q = PlannerNaming.ParseLayerName("06.2_X_17-08-26");
            Assert.Equal(new object[] { "06.2", null, 6, 2, PlannerLegacyForm.Dot, "X", "06.2_X" }, new object[] { q.Order, q.Phase, q.Stage, q.SubStage, q.LegacyForm, q.Name, q.Label });
            var r = PlannerNaming.ParseLayerName("02_01-2_X_17-08-26");
            Assert.Equal(new object[] { "02", 2, 1, 2, PlannerLegacyForm.Hyphen, "X" }, new object[] { r.Order, r.Phase, r.Stage, r.SubStage, r.LegacyForm, r.Name });
        }

        private static void AssertLayer(string name, int? phase, int stage, int? subStage, string activity, string start)
        {
            var p = PlannerNaming.ParseLayerName(name);
            Assert.True(p != null, name);
            Assert.True(p.Phase == phase, name + ": phase " + p.Phase);
            Assert.True(p.Stage == stage, name + ": stage " + p.Stage);
            Assert.True(p.SubStage == subStage, name + ": sub-stage " + p.SubStage);
            Assert.True(p.Name == activity, name + ": name " + p.Name);
            Assert.True(PlannerNaming.ToIsoDate(p.Start) == start, name + ": start " + PlannerNaming.ToIsoDate(p.Start));
        }

        [Fact]
        public void LayerName_TwoNumbersArePhaseThenStage()
        {
            // The layer ID of the hyphen release, <PP>_<SS>[-<m>]_<name>_<dates>: phase PP, stage SS (sub-stage m), as the
            // convention's <PP>_<SS>[_<m>] reads.
            var p = PlannerNaming.ParseLayerName("02_01-2_Construction_of_Phase1_TM_31-08-26_17-10-26");
            Assert.Equal(2, p.Phase.Value);
            Assert.Equal(1, p.Stage);
            Assert.Equal(2, p.SubStage.Value);
            Assert.Equal("2026-08-31", PlannerNaming.ToIsoDate(p.Start));
            Assert.Equal("2026-10-17", PlannerNaming.ToIsoDate(p.End));
            // The order is the first number, the name what follows the ID, and the label reads as before.
            Assert.Equal("02", p.Order);
            Assert.Equal("Construction_of_Phase1_TM", p.Name);
            Assert.Equal("02_01-2_Construction_of_Phase1_TM", p.Label);
            Assert.Equal(PlannerLegacyForm.Hyphen, p.LegacyForm);
            Assert.Empty(p.Issues);

            var dot = PlannerNaming.ParseLayerName("01_04.2_Retainment_18-09-26"); // the old full stop: phase 1, stage 4-2
            Assert.Equal(1, dot.Phase.Value);
            Assert.Equal(4, dot.Stage);
            Assert.Equal(2, dot.SubStage.Value);
            Assert.True(dot.Legacy);

            var plain = PlannerNaming.ParseLayerName("02_04_SubGrade_Installation_26-10-26_30-10-26");
            Assert.Equal(2, plain.Phase.Value);
            Assert.Equal(4, plain.Stage);
            Assert.False(plain.SubStage.HasValue);

            // One or two digits each; the separators the order token takes; the stage followed by "_", a space or the end.
            AssertPhaseStage("1_4_Kerbs_17-08-26", 1, 4, null);
            AssertPhaseStage("01 04-2 Kerbs_17-08-26", 1, 4, 2);
            AssertPhaseStage("01__04-2_Kerbs", 1, 4, 2);
            AssertPhaseStage("01 _04-2_Kerbs", 1, 4, 2);
            AssertPhaseStage("06_06_Full_Closure_of_Main_Road_TBC_07-02-27", 6, 6, null);
            AssertPhaseStage("01_00_START_DATE_01-06-26", 1, 0, null);
            AssertPhaseStage("02_02-10_Integration_17-10-26", 2, 2, 10);
            AssertPhaseStage("01_04", 1, 4, null);
            AssertPhaseStage("01_04-2", 1, 4, 2);
            AssertPhaseStage("01_04.2", 1, 4, 2);
            AssertPhaseStage("01_04-2_Kerbs (1)", 1, 4, 2);
            AssertPhaseStage("01_04-2 Kerbs", 1, 4, 2);                  // a hyphen sub-stage may be followed by a space
            AssertPhaseStage("01_1.8_x_2.4_Heras_18-09-26", 1, 1, 8);    // a measurement in the stage's place reads as one
            Assert.Equal("Kerbs", PlannerNaming.ParseLayerName("01 04-2 Kerbs_17-08-26").Name);
            Assert.Equal("01_04-2 Kerbs", PlannerNaming.ParseLayerName("01 04-2 Kerbs_17-08-26").Label);
            Assert.Equal(new[] { "no activity name" }, PlannerNaming.ParseLayerName("01_04-2_17-08-26").Issues); // nothing after the ID
            Assert.Equal("01_04-2", PlannerNaming.ParseLayerName("01_04-2_17-08-26").Label);
        }

        [Fact]
        public void LayerName_OneNumberKeepsItsMeaningAndADateIsNeverAPhaseStage()
        {
            // One number: the stage, the phase from the objects.
            AssertStageOnly("01_Sheet_Piling_SA&DS3_17-08-26_18-09-26", 1, null);
            AssertStageOnly("06-1 Temporary Hoarding - Erection_19-10-26_19-10-26", 6, 1);
            AssertStageOnly("05.2_Subbase_B_03-11-26_04-11-26", 5, 2);
            AssertStageOnly("05-2_04_Subbase", 5, 2);        // the first number has a sub-stage: not a phase
            AssertStageOnly("05.2_04_Subbase", 5, 2);
            AssertStageOnly("23_Road_Course_100mm_09-02-27", 23, null);
            AssertStageOnly("10 Hoarding Install - 63. No posts 6-11-26_12-11-26", 10, null);
            AssertStageOnly("00", 0, null);
            // A date is never the stage: three hyphenated numbers.
            AssertStageOnly("01_17-08-26", 1, null);
            AssertStageOnly("01_18-09-26_Kerbs", 1, null);
            AssertStageOnly("01_04-2-3_Kerbs", 1, null);
            // More than two digits on either side is not a phase and a stage.
            AssertStageOnly("001_04_Kerbs", 1, null);
            AssertStageOnly("01_004_Kerbs", 1, null);
            AssertStageOnly("01_123_Kerbs", 1, null);
            AssertStageOnly("2026_01_Kerbs", 2026, null);
            // The second number must stand alone.
            AssertStageOnly("01_04x_Kerbs", 1, null);
            AssertStageOnly("01_04-_Kerbs", 1, null);
            AssertStageOnly("01_04.x_Kerbs", 1, null);
            // A stage with a full stop only before an underscore or the end, as a dotted first number: a measurement
            // followed by a space is a name.
            AssertStageOnly("01_2.4 High Hoarding_18-09-26", 1, null);
            Assert.Equal("2.4 High Hoarding", PlannerNaming.ParseLayerName("01_2.4 High Hoarding_18-09-26").Name);
            AssertStageOnly("01_04.2 Kerbs", 1, null);
            AssertStageOnly("01-04_Kerbs", 1, 4);            // a hyphen after the first number is its sub-stage, as before
            Assert.Equal(1, PlannerNaming.ParseLayerName("01_17-08-26").Stage);
            Assert.Equal("2026-08-17", PlannerNaming.ToIsoDate(PlannerNaming.ParseLayerName("01_17-08-26").Start));
        }

        private static void AssertPhaseStage(string name, int phase, int stage, int? subStage)
        {
            var p = PlannerNaming.ParseLayerName(name);
            Assert.True(p != null, name);
            Assert.True(p.Phase == phase, name + ": phase " + p.Phase);
            Assert.True(p.Stage == stage, name + ": stage " + p.Stage);
            Assert.True(p.SubStage == subStage, name + ": sub-stage " + p.SubStage);
        }

        private static void AssertStageOnly(string name, int stage, int? subStage)
        {
            var p = PlannerNaming.ParseLayerName(name);
            Assert.True(p != null, name);
            Assert.True(!p.Phase.HasValue, name + ": phase " + p.Phase);
            Assert.True(p.Stage == stage, name + ": stage " + p.Stage);
            Assert.True(p.SubStage == subStage, name + ": sub-stage " + p.SubStage);
        }

        [Fact]
        public void LayerName_SubNumberedOrderTokenIsMarkedLegacy()
        {
            Assert.Equal(PlannerLegacyForm.Dot, PlannerNaming.ParseLayerName("05.2_Subbase_B_03-11-26_04-11-26").LegacyForm);
            Assert.Equal(PlannerLegacyForm.Hyphen, PlannerNaming.ParseLayerName("05-2_Subbase_B_03-11-26_04-11-26").LegacyForm);
            Assert.Equal(PlannerLegacyForm.Hyphen, PlannerNaming.ParseLayerName("06-1 Temporary Hoarding - Erection_19-10-26_19-10-26").LegacyForm);
            Assert.False(PlannerNaming.ParseLayerName("05_2_Subbase_B_03-11-26_04-11-26").Legacy); // phase 5, stage 2: no sub-stage
            Assert.False(PlannerNaming.ParseLayerName("01_Temporary_Road-Road_Marking_18-09-26").Legacy); // a hyphen in the name is no sub-stage
            Assert.False(PlannerNaming.ParseLayerName("01_Sheet_Piling_SA&DS3_17-08-26_18-09-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("23_Road_Course_100mm_09-02-27").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("10 Hoarding Install - 63. No posts 6-11-26_12-11-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("02_07_2.4_High_Hoarding_03-02-27").Legacy); // a dimension in the name
            Assert.Null(PlannerNaming.ParseLayerName("2.4 High Hoarding"));
            Assert.Null(PlannerNaming.ParseLayerName("1.8 x 2.4 Heras Panel"));
        }

        [Fact]
        public void LayerName_ADateIsNeverAStageNorAStageADate()
        {
            Assert.Null(PlannerNaming.ParseLayerName("17-08-26_Kerbs")); // a date where the order goes
            var p = PlannerNaming.ParseLayerName("01_17-08-26");
            Assert.Equal("01", p.Order);
            Assert.Equal("2026-08-17", PlannerNaming.ToIsoDate(p.Start));
            Assert.Contains("no activity name", p.Issues);
            var q = PlannerNaming.ParseLayerName("06-1_17-08-26_18-08-26");
            Assert.Equal(1, q.SubStage.Value);
            Assert.Equal("2026-08-18", PlannerNaming.ToIsoDate(q.End));
            var r = PlannerNaming.ParseLayerName("01_04-2_Kerbs"); // the stage is not a date
            Assert.Null(r.Start);
            Assert.Equal(new[] { "no dates" }, r.Issues);
            Assert.Equal("Kerbs", r.Name);
            Assert.Equal("01_04-2_Kerbs", r.Label);
        }

        [Fact]
        public void LayerName_OnlyASubStageInTheStagesPositionIsLegacy()
        {
            // phasingNaming.test.ts: "flags only a sub-stage in the stage's position as legacy, never a dimension elsewhere".
            Assert.Equal(PlannerLegacyForm.Dot, PlannerNaming.ParseLayerName("05.2_Subbase_03-11-26").LegacyForm);
            Assert.Equal(PlannerLegacyForm.Dot, PlannerNaming.ParseLayerName("05.2").LegacyForm);
            Assert.Equal(PlannerLegacyForm.Dot, PlannerNaming.ParseLayerName("01 04.2_Retainment_18-09-26").LegacyForm); // a space after the phase group
            Assert.Equal(PlannerLegacyForm.Dot, PlannerNaming.ParseLayerName("01_04.2_18-09-26").LegacyForm);
            Assert.False(PlannerNaming.ParseLayerName("05_Subbase_03-11-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("01_04_2_Retainment_18-09-26").Legacy); // the current form
            Assert.Equal(PlannerLegacyForm.Hyphen, PlannerNaming.ParseLayerName("06-1 Temporary Hoarding_19-10-26").LegacyForm);
            Assert.False(PlannerNaming.ParseLayerName("01_Temporary_Road-Road_Marking_18-09-26").Legacy); // a hyphen in the name is no sub-stage
            Assert.False(PlannerNaming.ParseLayerName("01_2.4 High Hoarding_18-09-26").Legacy); // a dimension, not a sub-stage
            Assert.False(PlannerNaming.ParseLayerName("01_Heras_1.8_x_2.4_18-09-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("01_Hoarding_v.02_18-09-26").Legacy);
            Assert.Null(PlannerNaming.ParseLayerName("2.4 High Hoarding"));

            // The Planner's rule whatever separates the phase group from the stage, as long as the sub-stage is followed
            // by an underscore or the end.
            foreach (var name in new[] { "01__04.2_Retainment_18-09-26", "01 _04.2_Name", "01_ 04.2_Name", "01  04.2_Name_18-09-26", "01 04.2_Name_TBC", "05.2__X" })
            {
                Assert.True(PlannerNaming.ParseLayerName(name).LegacyForm == PlannerLegacyForm.Dot, name);
            }
            Assert.Equal(PlannerLegacyForm.Hyphen, PlannerNaming.ParseLayerName("01__04-2_Retainment_18-09-26").LegacyForm);
            Assert.False(PlannerNaming.ParseLayerName("01__04__2_Retainment_18-09-26").Legacy); // the current form
            // A measurement in that place reads like a sub-stage to both parsers; the naming check looks closer.
            Assert.True(PlannerNaming.ParseLayerName("01_1.8_x_2.4_Heras_18-09-26").Legacy);
        }

        [Fact]
        public void JavaScriptSpacesAndLineEndsReadAsThePlannerReadsThem()
        {
            // The Planner's \s and trim() take U+FEFF but not U+0085 (.NET's are the other way round), and its $ is the
            // very end of the name (.NET's also matches before a final line feed).
            var nel = ((char)0x85).ToString();
            var bom = ((char)0xFEFF).ToString();
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St04-2_IN_X_Ph2_St05-2_RM" + nel + "note").Trail);
            Assert.Equal("St05-2", PlannerNaming.ParseMeshName("Ph1_St04-2_IN_X_Ph2_St05-2_RM" + bom + "note").Trail.ActivityName);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St04_IN\n"));
            Assert.Equal("X", PlannerNaming.ParseLayerName("01_X" + bom).Name);
            Assert.Equal("X", PlannerNaming.ParseLayerName("01_X" + bom + "(1)").Name);
            Assert.Equal("01", PlannerNaming.ParseLayerName(bom + "01_X").Order);
            Assert.Null(PlannerNaming.ParseLayerName(nel + "01_X"));
            Assert.Equal("X" + nel, PlannerNaming.ParseLayerName("01_X" + nel).Name);
            int stage;
            int? subStage;
            PlannerNaming.ParseStageNumber(nel + "03", out stage, out subStage);
            Assert.Equal(0, stage);
            PlannerNaming.ParseStageNumber(bom + "03-2", out stage, out subStage);
            Assert.Equal(3, stage);
            Assert.Equal(2, subStage.Value);
        }

        private static void AssertSegment(PlannerMeshSegment segment, string folder, string activity, PlannerTaskType type)
        {
            Assert.NotNull(segment);
            Assert.Equal(folder, segment.FolderName);
            Assert.Equal(activity, segment.ActivityName);
            Assert.Equal(type, segment.Type);
        }
    }
}
