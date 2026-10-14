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
            Assert.True(PlannerNaming.SameStage(5, null, 5, 1)); // group 05 dates St05.1 work
            Assert.True(PlannerNaming.SameStage(5, 1, 5, null)); // object St05 under group 05-1, as before
            Assert.True(PlannerNaming.SameStage(5, 1, 5, 1));
            Assert.False(PlannerNaming.SameStage(5, 1, 5, 2));
            Assert.False(PlannerNaming.SameStage(5, null, 6, null));
            Assert.False(PlannerNaming.SameStage(5, 1, 6, 1));
            Assert.Equal("St05", PlannerNaming.StageText(5, null));
            Assert.Equal("St05-1", PlannerNaming.StageText(5, 1));
            Assert.Equal("St12-10", PlannerNaming.StageText(12, 10));
        }

        [Fact]
        public void MeshName_HyphenSubStage()
        {
            var p = PlannerNaming.ParseMeshName("Ph1_St04-2_IN_Sheet_Pile_6");
            AssertSegment(p.Lead, "PH1", "St04-2", PlannerTaskType.Install);
            Assert.Equal(4, p.Lead.Stage);
            Assert.Equal(2, p.Lead.SubStage.Value);
            Assert.False(p.Lead.Legacy);
            Assert.False(p.Legacy);
            Assert.Equal("Sheet_Pile_6", p.Description);

            var q = PlannerNaming.ParseMeshName("Ph2_St01-8_IN_RC_Hrd_from_PDF_04_Ph6_St05-2_RM");
            AssertSegment(q.Trail, "PH6", "St05-2", PlannerTaskType.Dismantle);
            Assert.Equal(2, q.Trail.SubStage.Value);
            Assert.Equal("RC_Hrd_from_PDF_04", q.Description);
            Assert.Equal(10, PlannerNaming.ParseMeshName("Ph1_St03-10_IN_Kerb").Lead.SubStage.Value);
            Assert.Equal(3, PlannerNaming.ParseMeshName("Ph1_St3-2_IN_Kerb").Lead.Stage);
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03-_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St03-x_IN_Kerb"));
        }

        [Fact]
        public void MeshName_TheFullStopStillReadsTheSameAndIsMarked()
        {
            var dot = PlannerNaming.ParseMeshName("Ph1_St04.2_IN_Sheet_Pile_6");
            var hyphen = PlannerNaming.ParseMeshName("Ph1_St04-2_IN_Sheet_Pile_6");
            Assert.Equal(hyphen.Lead.Stage, dot.Lead.Stage);
            Assert.Equal(hyphen.Lead.SubStage, dot.Lead.SubStage);
            Assert.Equal(hyphen.Description, dot.Description);
            Assert.Equal("St04.2", dot.Lead.ActivityName); // the activity keeps the spelling of the model
            Assert.True(dot.Lead.Legacy);
            Assert.True(dot.Legacy);
            Assert.True(PlannerNaming.ParseMeshName("Ph2_St01-8_IN_X_Ph6_St05.2_RM").Legacy); // either tag
            Assert.False(PlannerNaming.ParseMeshName("Ph2_St01-8_IN_X_Ph6_St05.2_RM").Lead.Legacy);
            Assert.False(PlannerNaming.ParseMeshName("Ph1_St04_IN_1.8_x_2.4_Heras_Panel").Legacy); // full stops in the description are not sub-stages
            Assert.True(PlannerNaming.SameStage(dot.Lead.Stage, dot.Lead.SubStage, hyphen.Lead.Stage, hyphen.Lead.SubStage));
        }

        [Fact]
        public void MeshName_ADateIsNeverAStage()
        {
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St17-08-26_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St04-2-1_IN_Kerb"));
            Assert.Null(PlannerNaming.ParseMeshName("Ph1_St01_IN_X_Ph2_St17-08-26_RM").Trail);
        }

        [Fact]
        public void LayerName_HyphenAfterTheStageReadsAsTheFullStopDid()
        {
            var hyphen = PlannerNaming.ParseLayerName("01_04-2_Phase1_Retainment_Installation_18-09-26_23-10-26");
            var dot = PlannerNaming.ParseLayerName("01_04.2_Phase1_Retainment_Installation_18-09-26_23-10-26");
            Assert.Equal("01", hyphen.Order);
            Assert.Equal(dot.Order, hyphen.Order);
            Assert.Equal(1, hyphen.Phase.Value); // phase then stage
            Assert.Equal(dot.Phase, hyphen.Phase);
            Assert.Equal(4, hyphen.Stage);
            Assert.Equal(2, hyphen.SubStage.Value);
            Assert.Equal(dot.Stage, hyphen.Stage);
            Assert.Equal(dot.SubStage, hyphen.SubStage);
            Assert.Equal("Phase1_Retainment_Installation", hyphen.Name); // the name after the ID
            Assert.Equal("Phase1_Retainment_Installation", dot.Name);
            Assert.Equal("01_04-2_Phase1_Retainment_Installation", hyphen.Label);
            Assert.Equal("2026-09-18", PlannerNaming.ToIsoDate(hyphen.Start));
            Assert.Equal("2026-10-23", PlannerNaming.ToIsoDate(hyphen.End));
            Assert.Equal(PlannerNaming.ToIsoDate(dot.Start), PlannerNaming.ToIsoDate(hyphen.Start));
            Assert.Equal(PlannerNaming.ToIsoDate(dot.End), PlannerNaming.ToIsoDate(hyphen.End));
            Assert.False(hyphen.Tbc);
            Assert.Equal(dot.Tbc, hyphen.Tbc);
            Assert.Empty(hyphen.Issues);
            Assert.Empty(dot.Issues);
            Assert.False(hyphen.Legacy);
            Assert.True(dot.Legacy);
        }

        [Fact]
        public void LayerName_TwoNumbersArePhaseThenStage()
        {
            // The convention's layer ID, <PP>_<SS>[-<m>]_<name>_<dates>: phase PP, stage SS (sub-stage m).
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
            Assert.False(p.Legacy);
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
        public void LayerName_DottedOrderTokenIsMarkedLegacy()
        {
            Assert.True(PlannerNaming.ParseLayerName("05.2_Subbase_B_03-11-26_04-11-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("05-2_Subbase_B_03-11-26_04-11-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("06-1 Temporary Hoarding - Erection_19-10-26_19-10-26").Legacy);
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
        public void LayerName_OnlyAFullStopInTheSubStagePositionIsLegacy()
        {
            // phasingNaming.test.ts: "flags only a dot in the sub-stage position as legacy, never a dimension".
            Assert.True(PlannerNaming.ParseLayerName("05.2_Subbase_03-11-26").Legacy);
            Assert.True(PlannerNaming.ParseLayerName("05.2").Legacy);
            Assert.True(PlannerNaming.ParseLayerName("01 04.2_Retainment_18-09-26").Legacy); // a space after the phase group
            Assert.True(PlannerNaming.ParseLayerName("01_04.2_18-09-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("05_Subbase_03-11-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("06-1 Temporary Hoarding_19-10-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("01_2.4 High Hoarding_18-09-26").Legacy); // a dimension, not a sub-stage
            Assert.False(PlannerNaming.ParseLayerName("01_Heras_1.8_x_2.4_18-09-26").Legacy);
            Assert.False(PlannerNaming.ParseLayerName("01_Hoarding_v.02_18-09-26").Legacy);
            Assert.Null(PlannerNaming.ParseLayerName("2.4 High Hoarding"));

            // The Planner's rule whatever separates the phase group from the stage, as long as the sub-stage is followed
            // by an underscore or the end.
            foreach (var name in new[] { "01__04.2_Retainment_18-09-26", "01 _04.2_Name", "01_ 04.2_Name", "01  04.2_Name_18-09-26", "01 04.2_Name_TBC", "05.2__X" })
            {
                Assert.True(PlannerNaming.ParseLayerName(name).Legacy, name);
            }
            Assert.False(PlannerNaming.ParseLayerName("01__04-2_Retainment_18-09-26").Legacy);
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
