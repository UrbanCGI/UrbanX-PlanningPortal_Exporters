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

        private static void AssertSegment(PlannerMeshSegment segment, string folder, string activity, PlannerTaskType type)
        {
            Assert.NotNull(segment);
            Assert.Equal(folder, segment.FolderName);
            Assert.Equal(activity, segment.ActivityName);
            Assert.Equal(type, segment.Type);
        }
    }
}
