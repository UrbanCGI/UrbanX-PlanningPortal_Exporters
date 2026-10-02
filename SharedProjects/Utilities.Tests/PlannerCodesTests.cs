using System.Collections.Generic;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The normative tables of the Planner &lt;-&gt; 3ds Max contract (sections 1 to 4), row for row. The Planner's
    /// vitest suite carries the same cases; when a row changes, change it on both sides.
    /// </summary>
    public class PlannerCodesTests
    {
        // ---- 1. code helpers ----------------------------------------------------------------------------------

        [Theory]
        [InlineData("Work_Phasing", true)]
        [InlineData("work phasing", true)]
        [InlineData("WORK-PHASING", true)]
        [InlineData("Work_Phasing (1)", true)]
        [InlineData("HS2_Work_Phasing_v2", true)]
        [InlineData("HS2_EUS_HRB_Phasing_RoadConstraction_Main_Section_v.02", false)]
        [InlineData("Phasing", false)]
        [InlineData("Work", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsPhasingRoot(string name, bool expected)
        {
            Assert.Equal(expected, PlannerCodes.IsPhasingRoot(name));
        }

        [Theory]
        [InlineData("6", "05", null, "06-05")]
        [InlineData("2", "01", "5", "02-01.5")]
        [InlineData("1", "4", null, "01-04")]
        [InlineData("06", "01", "1", "06-01.1")]
        [InlineData("99", "99", null, "99-99")]
        [InlineData("4", "12", "10", "04-12.10")]
        public void PhStToCode(string phase, string stage, string sub, string expected)
        {
            Assert.Equal(expected, PlannerCodes.PhStToCode(phase, stage, sub));
        }

        [Theory]
        [InlineData("01-04", "01-04", true)]
        [InlineData("01-04", "01-04.1", true)]
        [InlineData("01-04.1", "01-04", true)]
        [InlineData("01", "01-04", true)]
        [InlineData("01", "01-04.2", true)]
        [InlineData("06-05.2", "06-05", true)]
        [InlineData("01-04", "01-05", false)]
        [InlineData("01-0", "01-04", false)]
        [InlineData("01-04.1", "01-04.2", false)]
        [InlineData("02-01.0", "02-01.3", false)]
        [InlineData("01", "011", false)]
        [InlineData(null, "01", false)]
        [InlineData("01", null, false)]
        public void CodeCovers(string folderCode, string code, bool expected)
        {
            Assert.Equal(expected, PlannerCodes.CodeCovers(folderCode, code));
        }

        // ---- 2. coded layer names -----------------------------------------------------------------------------

        [Theory]
        [InlineData("01_Zone5_Working", "01", "Zone5_Working")]
        [InlineData("01-04.2_Phase1_Retainment_Installation", "01-04.2", "Phase1_Retainment_Installation")]
        [InlineData("06-01.2_Traffic_Islands_Install", "06-01.2", "Traffic_Islands_Install")]
        [InlineData("01-04.2", "01-04.2", "")]
        [InlineData("01-04.2 Phase1 Retainment", "01-04.2", "Phase1 Retainment")]
        [InlineData("Zone5_Working", null, "Zone5_Working")]
        [InlineData("Work_Phasing", null, "Work_Phasing")]
        [InlineData("02-01.5_Construction (1)", "02-01.5", "Construction")]
        [InlineData("01_Phase_(2)", "01", "Phase_(2)")]   // the duplicate suffix needs its space: "Phase (2)" as a layer name
        public void ParseCodedLayerName(string input, string code, string name)
        {
            var parsed = PlannerCodes.ParseCodedLayerName(input);
            Assert.Equal(code, parsed.Code);
            Assert.Equal(name, parsed.Name);
        }

        [Theory]
        [InlineData("Retaining (1)", "Retaining")]
        [InlineData("Retaining  (12) ", "Retaining")]
        [InlineData("Retaining(1)", "Retaining(1)")]
        [InlineData("01_Phase_(2)", "01_Phase_(2)")]
        public void StripDuplicateSuffix_NeedsTheSpace(string input, string expected)
        {
            Assert.Equal(expected, PlannerCodes.StripDuplicateSuffix(input));
            // A folder named "Phase (2)" gets a layer that reads back as the same name.
            Assert.Equal("Phase_(2)", PlannerCodes.ParseCodedLayerName(PlannerCodes.LayerNameFor("01", "Phase (2)")).Name);
        }

        [Fact]
        public void ParseCodedLayerName_RootRowIsAlsoThePhasingRoot()
        {
            Assert.Null(PlannerCodes.ParseCodedLayerName("Work_Phasing").Code);
            Assert.True(PlannerCodes.IsPhasingRoot("Work_Phasing"));
        }

        [Fact]
        public void ParseCodedLayerName_OnlyAsciiDigits()
        {
            Assert.Null(PlannerCodes.ParseCodedLayerName("٠١_Zone").Code);
            Assert.Null(PlannerCodes.ParseCodedLayerName("01-_Zone").Code);
            Assert.Null(PlannerCodes.ParseCodedLayerName("0123abc").Code);
        }

        [Theory]
        [InlineData("01", "Zone5_Working", "01_Zone5_Working")]
        [InlineData("01-04.2", "Phase1 Retainment  Installation", "01-04.2_Phase1_Retainment_Installation")]
        [InlineData("01-04.2", "", "01-04.2")]
        [InlineData("01-04.2", null, "01-04.2")]
        [InlineData(null, "Zone5_Working", "Zone5_Working")]
        [InlineData("", "Zone5_Working", "Zone5_Working")]
        [InlineData("06-05", "_Site__Entrance_ ", "06-05_Site_Entrance")]
        [InlineData(null, null, "")]
        public void LayerNameFor(string code, string name, string expected)
        {
            Assert.Equal(expected, PlannerCodes.LayerNameFor(code, name));
        }

        [Fact]
        public void LayerNameFor_RoundTripsThroughTheParser()
        {
            var name = PlannerCodes.LayerNameFor("01-04.2", "Phase1 Retainment");
            var parsed = PlannerCodes.ParseCodedLayerName(name);
            Assert.Equal("01-04.2", parsed.Code);
            Assert.Equal("Phase1_Retainment", parsed.Name);
        }

        // ---- 3. legacy layer names ----------------------------------------------------------------------------

        [Theory]
        [InlineData("_01_Zone5_Working", "01", "Zone5_Working", null, null, false)]
        [InlineData("01_04.1_Phase1_Retainment_Installation_18-09-26_23-10-26", "01-04.1", "Phase1_Retainment_Installation", "2026-09-18", "2026-10-23", false)]
        [InlineData("01_01_Drainage_TBC_08-06-26_03-07-26", "01-01", "Drainage", "2026-06-08", "2026-07-03", true)]
        [InlineData("01_00_START_DATE_01-06-26", "01-00", "START_DATE", "2026-06-01", "2026-06-01", false)]
        [InlineData("02_02.1_Integration_of_Phase1_TM_!!!FULL_CLOSURE!!!_17-10-26", "02-02.1", "Integration_of_Phase1_TM_!!!FULL_CLOSURE!!!", "2026-10-17", "2026-10-17", false)]
        [InlineData("4_02.1_Binder_Cource_23-01-27", "04-02.1", "Binder_Cource", "2027-01-23", "2027-01-23", false)]
        [InlineData("06.01.2_Traffic_Islands_Install_05-02-27", "06-01.2", "Traffic_Islands_Install", "2027-02-05", "2027-02-05", false)]
        [InlineData("06_05_Site Entrance-Hoarding_Install_05-02-27", "06-05", "Site_Entrance-Hoarding_Install", "2027-02-05", "2027-02-05", false)]
        [InlineData("06_06_Full_Closure_of_Main_Road_TBC_07-02-27", "06-06", "Full_Closure_of_Main_Road", "2027-02-07", "2027-02-07", true)]
        public void ParseLegacyLayer(string input, string code, string name, string start, string finish, bool tbc)
        {
            var parsed = PlannerCodes.ParseLegacyLayer(input, null);
            Assert.NotNull(parsed);
            Assert.Equal(code, parsed.Code);
            Assert.Equal(name, parsed.Name);
            Assert.Equal(start, PlannerScheduleDates.IsoDay(parsed.Start));
            Assert.Equal(finish, PlannerScheduleDates.IsoDay(parsed.Finish));
            Assert.Equal(tbc, parsed.Tbc);
            Assert.Empty(parsed.Issues);
        }

        [Fact]
        public void ParseLegacyLayer_ReportsNormalisedNumbers()
        {
            Assert.Equal(new[] { "group number padded" }, PlannerCodes.ParseLegacyLayer("4_02.1_Binder_Cource_23-01-27", null).Notes);
            Assert.Equal(new[] { "dotted group number normalised" }, PlannerCodes.ParseLegacyLayer("06.01.2_Traffic_Islands_Install_05-02-27", null).Notes);
            Assert.Empty(PlannerCodes.ParseLegacyLayer("01_04.1_Phase1_18-09-26", null).Notes);
        }

        [Fact]
        public void ParseLegacyLayer_FormsAndGroups()
        {
            var group = PlannerCodes.ParseLegacyLayer("_06_Weekend_Closure", null);
            Assert.True(group.IsGroup);
            Assert.False(group.HasSchedule);
            Assert.Equal(PlannerLegacyForm.GroupRow, PlannerCodes.ParseLegacyLayer("02_01.0_Construction_of_Phase1_TM_31-08-26", null).Form);
            Assert.Equal(PlannerLegacyForm.DottedGroupRow, PlannerCodes.ParseLegacyLayer("06.02.2_Traffic_Islandsl_Infill_06-02-27", null).Form);

            // A single number at group level: no dates expected, so none are reported missing.
            var single = PlannerCodes.ParseLegacyLayer("05_Night_Closure", null);
            Assert.Equal(PlannerLegacyForm.Single, single.Form);
            Assert.Equal("05", single.Code);
            Assert.Empty(single.Issues);
        }

        [Fact]
        public void ParseLegacyLayer_ParentCodeMismatchIsKeptAndReported()
        {
            var parsed = PlannerCodes.ParseLegacyLayer("03_01.1_Temporary_Hoarding_TBC_19-10-26_09-11-26", "02");
            Assert.Equal("03-01.1", parsed.Code);
            Assert.Single(parsed.Issues);
            Assert.Contains("parent code 02", parsed.Issues[0]);
            Assert.Empty(PlannerCodes.ParseLegacyLayer("02_01.1_Construction_31-08-26_17-10-26", "02").Issues);
        }

        [Fact]
        public void ParseLegacyLayer_DateIssuesUseTheExistingRules()
        {
            Assert.Contains("no dates", PlannerCodes.ParseLegacyLayer("02_03_Utility_Crossing", null).Issues);
            Assert.Contains("unreadable date \"31-02-26\"", PlannerCodes.ParseLegacyLayer("02_03_Piling_31-02-26_05-03-26", null).Issues);
            var duplicate = PlannerCodes.ParseLegacyLayer("02_02.7_Hoarding_Install_13-10-26_05-02-27 (1)", null);
            Assert.Equal("02-02.7", duplicate.Code);
            Assert.Equal("Hoarding_Install", duplicate.Name);
        }

        [Theory]
        [InlineData("01_02_No_Dates", true)]
        [InlineData("01_02_Bad_31-02-26", true)]
        [InlineData("01_02_Bad_31-13-26_01-01-27", true)]
        [InlineData("01_02_Range_10-10-26_01-10-26", true)]
        [InlineData("06.01.2", true)]
        [InlineData("01_02_Fine_01-10-26_10-10-26", false)]
        [InlineData("_01_Zone5_Working", false)]
        [InlineData("05_Night_Closure", false)]
        public void ParseLegacyLayer_UndatedOrBadlyDatedChildLayersAreTbc(string input, bool tbc)
        {
            // Same rule as the Planner's parseLegacyLayer: only GG_RR child layers were dated.
            Assert.Equal(tbc, PlannerCodes.ParseLegacyLayer(input, null).Tbc);
        }

        [Fact]
        public void ParseLegacyLayer_NoLeadingNumberIsNull()
        {
            Assert.Null(PlannerCodes.ParseLegacyLayer("HS2_EUS_HRB_Phasing_RoadConstraction_Main_Section_v.02", null));
            Assert.Null(PlannerCodes.ParseLegacyLayer("_RetainingWall_external3", null));
            Assert.Null(PlannerCodes.ParseLegacyLayer("Work_Phasing", null));
            Assert.Null(PlannerCodes.ParseLegacyLayer(null, null));
        }

        // ---- 4. object names ----------------------------------------------------------------------------------

        [Theory]
        [InlineData("Ph1_St04_IN_Sheet_Pile_1", PlannerTaskType.Install, "Sheet_Pile_1", "01-04", null)]
        [InlineData("IN_Sheet_Pile_1", PlannerTaskType.Install, "Sheet_Pile_1", null, null)]
        [InlineData("RM_Kerb_CardingtonSt_North", PlannerTaskType.Dismantle, "Kerb_CardingtonSt_North", null, null)]
        [InlineData("Ph2_St01.5_IN_RC_Hrd_from_PDF_04_Ph6_St05_RM", PlannerTaskType.Install, "RC_Hrd_from_PDF_04", "02-01.5", "06-05")]
        [InlineData("IN_RC_Hrd_04_RM_06-05", PlannerTaskType.Install, "RC_Hrd_04", null, "06-05")]
        [InlineData("Ph6_St05_IN_RC_PlasticRoadBarrier_Full_Closure_Ph99_St99_RM", PlannerTaskType.Install, "RC_PlasticRoadBarrier_Full_Closure", "06-05", "99-99")]
        [InlineData("Ph06_St01.1_IN_TR_CycleLane_Splitter_Island_Kerbs", PlannerTaskType.Install, "TR_CycleLane_Splitter_Island_Kerbs", "06-01.1", null)]
        [InlineData("Ph2_St01.1_RM_RM_Marwood_Barrier", PlannerTaskType.Dismantle, "RM_Marwood_Barrier", "02-01.1", null)]
        [InlineData("Ph2_St02_IN_Cycling_Bollards_Ph4_St02.2_RM (1)", PlannerTaskType.Install, "Cycling_Bollards", "02-02", "04-02.2")]
        [InlineData("Sheet_Pile_7", PlannerTaskType.Install, "Sheet_Pile_7", null, null)]
        public void ParseCodedObjectName(string input, PlannerTaskType type, string description, string leadCode, string removalCode)
        {
            var parsed = PlannerCodes.ParseCodedObjectName(input);
            Assert.Equal(type, parsed.Type);
            Assert.Equal(description, parsed.Description);
            Assert.Equal(leadCode, parsed.LeadCode);
            Assert.Equal(removalCode, parsed.RemovalCode);
        }

        [Fact]
        public void ParseCodedObjectName_Tagged()
        {
            Assert.True(PlannerCodes.ParseCodedObjectName("Ph1_St04_IN_Sheet_Pile_1").Tagged);
            Assert.True(PlannerCodes.ParseCodedObjectName("IN_Sheet_Pile_1").Tagged);
            Assert.True(PlannerCodes.ParseCodedObjectName("rm_Kerb").Tagged);
            Assert.False(PlannerCodes.ParseCodedObjectName("Sheet_Pile_7").Tagged);
            Assert.False(PlannerCodes.ParseCodedObjectName("Phase1_Stage0_Install").Tagged);
        }

        [Fact]
        public void ParseCodedObjectName_EdgeCases()
        {
            // A trail abutting the lead borrows its underscore.
            var abutting = PlannerCodes.ParseCodedObjectName("Ph1_St00_IN_Ph2_St01_RM");
            Assert.Equal("01-00", abutting.LeadCode);
            Assert.Equal("02-01", abutting.RemovalCode);
            Assert.Equal(string.Empty, abutting.Description);

            // A removal only counts at the very end; a note after it hides it.
            Assert.Null(PlannerCodes.ParseCodedObjectName("Ph1_St10_IN_X_Ph2_St00_RM_extra").RemovalCode);

            // The removal trail is only read on Install objects; a trailing install tag on a removal is reported, then ignored.
            var reinstall = PlannerCodes.ParseCodedObjectName("Ph3_St01.1_RM_RC_Hrd_CardingtonSt_N_Ph3_St07.2_IN");
            Assert.Equal(PlannerTaskType.Dismantle, reinstall.Type);
            Assert.Null(reinstall.RemovalCode);
            Assert.Equal("03-07.2", reinstall.IgnoredInstallCode);
            Assert.Equal("RC_Hrd_CardingtonSt_N", reinstall.Description);

            // Spaces are kept; only edge underscores go.
            Assert.Equal("Kerbs_based on 1MC03-SCJ_SDH-EN-DPL-SS01_SL12-011003_PDF",
                PlannerCodes.ParseCodedObjectName("Ph2_St01.5_IN_Kerbs_based on 1MC03-SCJ_SDH-EN-DPL-SS01_SL12-011003_PDF").Description);
            Assert.Equal("TR_Subbase_280mm_C", PlannerCodes.ParseCodedObjectName("Ph2_St05.1_IN_TR_Subbase_280mm_C_").Description);
            Assert.Equal("RetainingWall_external3", PlannerCodes.ParseCodedObjectName("_RetainingWall_external3").Description);

            // An untagged object may still end in a removal.
            var untagged = PlannerCodes.ParseCodedObjectName("Barrier_RM_04-02");
            Assert.False(untagged.Tagged);
            Assert.Equal("04-02", untagged.RemovalCode);
            Assert.Equal("Barrier", untagged.Description);
        }

        [Fact]
        public void ResolveRemovalTarget()
        {
            var codes = new List<string> { "01", "01-04.1", "02", "02-02.1", "04", "04-02", "04-02.1", "04-02.2", null, "06-05.1", "06-05.2" };
            Assert.Equal(7, PlannerCodes.ResolveRemovalTarget("04-02.2", codes));   // exact
            Assert.Equal(5, PlannerCodes.ResolveRemovalTarget("04-02", codes));     // exact beats the sub-codes
            Assert.Equal(9, PlannerCodes.ResolveRemovalTarget("06-05", codes));     // first folder under it, outline order
            Assert.Equal(1, PlannerCodes.ResolveRemovalTarget("01-04", codes));
            Assert.Equal(-1, PlannerCodes.ResolveRemovalTarget("99-99", codes));    // unresolved
            Assert.Equal(-1, PlannerCodes.ResolveRemovalTarget("06-0", codes));     // a prefix must end at "." or "-"
            Assert.Equal(-1, PlannerCodes.ResolveRemovalTarget(null, codes));
        }
    }
}
