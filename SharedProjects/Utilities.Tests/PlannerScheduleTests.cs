using System;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// What the schedule shares with the export naming check and the tab, apart from the engine: a scene on the old names
    /// (the tab reads its tree before Clean up), the time helpers on clock-change days, and the old names' retagging the
    /// check's advice writes. The new names' engine is tested in <see cref="PlannerScheduleV2Tests"/> and against the
    /// Planner in <see cref="PlannerScheduleV2ParityTests"/>; the old names' engine is gone.
    /// </summary>
    public class PlannerScheduleTests
    {
        internal const string Strip = "01_02_Strip_Out_01-09-26_05-09-26";
        internal const string Reinstate = "02_01_Reinstate_10-09-26_12-09-26";
        internal const string Undated = "01_03_Undated_TBC";
        internal const string Empty = "01_09_Empty_01-01-27";

        /// <summary>A scene on the old names: root, two zones, dated layers and their helpers, objects linked under their layers' helpers.</summary>
        internal static TestScene Scene()
        {
            var scene = new TestScene();
            scene.Layer("0");
            var root = scene.Unit("Work_Phasing", null, null);
            var zone1 = scene.Unit("_01_Zone", "Work_Phasing", root);
            var strip = scene.Unit(Strip, "_01_Zone", zone1);
            scene.Object("Ph1_St02_RM_Old_Fence", strip, Strip);
            scene.Object("ph1_st02_RM_Kerb_B_ph2_st01_IN", strip, Strip);
            var undated = scene.Unit(Undated, "_01_Zone", zone1);
            scene.Object("Ph1_St03_IN_Undated_Work", undated, Undated);
            scene.Unit(Empty, "_01_Zone", zone1);
            var zone2 = scene.Unit("_02_Zone", "Work_Phasing", root);
            var reinstate = scene.Unit(Reinstate, "_02_Zone", zone2);
            scene.Object("ph1_st02_RM_Kerb_A_ph2_st01_IN", reinstate, Reinstate);
            scene.Object("Ph2_St01_IN_Fence_Ph1_St02_RM", reinstate, Reinstate);
            scene.Object("Ph2_St01_IN_New_Kerb", reinstate, Reinstate);
            scene.Object("Plain_Box", reinstate, Reinstate);
            scene.Object("Ph2_St01_IN_Loose", null, Reinstate);
            return scene;
        }

        [Fact]
        public void RetaggedKeepsTheRestOfTheNameAsWritten()
        {
            Assert.Equal("Ph2_St01_RM_Bench_Ph2_St01_IN", PlannerSchedule.Retagged("Ph2_St02_RM_Bench_Ph2_St02_IN", 2, 1, null));
            Assert.Equal("Ph2_St01_2_RM_Old_Bench_2_Ph2_St01_2_IN (1)", PlannerSchedule.Retagged("ph2_st03-1_RM_Old_Bench_2_Ph2_St03-1_in (1)", 2, 1, 2));
            Assert.Equal("Ph1_St05_RM_Ph1_St05_IN", PlannerSchedule.Retagged("Ph1_St07_RM_Ph1_St07_IN", 1, 5, null));
            // The trailing tag is the one at the end (or before a note), not a tag-like text inside the description.
            Assert.Equal("Ph3_St10_RM_A_Ph2_St02_IN_B_Ph3_St10_IN", PlannerSchedule.Retagged("Ph2_St02_RM_A_Ph2_St02_IN_B_Ph2_St02_IN", 3, 10, null));
            Assert.Equal("Ph2_St01_RM_C_Ph2_St01_IN note", PlannerSchedule.Retagged("Ph2_St02_RM_C_Ph2_St02_IN note", 2, 1, null));
        }

        [Fact]
        public void LocalInstantsAreTheTimeOnTheWallClockOnClockChangeDays()
        {
            Func<int, int, int, int, int, long> utc = (y, mo, d, h, mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var uk = new PlannerSchedule { Zone = PlannerSceneFiles.ZoneOf("Europe/London") };
            var sydney = new PlannerSchedule { Zone = PlannerSceneFiles.ZoneOf("Australia/Sydney") };
            // After the clocks change, 13:00 is 13:00, not 13 hours after midnight.
            Assert.Equal(utc(2026, 10, 24, 23, 0), uk.Midnight(new DateTime(2026, 10, 25)));
            Assert.Equal(utc(2026, 10, 25, 13, 0), uk.LocalInstant(new DateTime(2026, 10, 25), 13 * 60));
            Assert.Equal(utc(2027, 3, 28, 12, 0), uk.LocalInstant(new DateTime(2027, 3, 28), 13 * 60));
            Assert.Equal(utc(2026, 10, 4, 2, 0), sydney.LocalInstant(new DateTime(2026, 10, 4), 13 * 60));
            Assert.Equal(utc(2027, 4, 4, 3, 0), sydney.LocalInstant(new DateTime(2027, 4, 4), 13 * 60));
            // A time the clocks repeat is its first occurrence, as JavaScript reads it (.NET alone would take the second).
            Assert.Equal(utc(2026, 10, 25, 0, 30), uk.LocalInstant(new DateTime(2026, 10, 25), 90));
            Assert.Equal(utc(2027, 4, 3, 15, 30), sydney.LocalInstant(new DateTime(2027, 4, 4), 150));
            // A time the clocks skip moves forward by the gap: 01:30 on 29 Mar 2026 is 02:30 summer time.
            Assert.Equal(utc(2026, 3, 29, 1, 30), uk.LocalInstant(new DateTime(2026, 3, 29), 90));
            Assert.Equal(utc(2026, 10, 3, 16, 30), sydney.LocalInstant(new DateTime(2026, 10, 4), 150));
            // A day of 25 hours is 1,500 minutes from midnight to midnight, one of 23 hours 1,380.
            Assert.Equal(1500, uk.MinutesBetween(new DateTime(2026, 10, 25), new DateTime(2026, 10, 26)));
            Assert.Equal(1380, sydney.MinutesBetween(new DateTime(2026, 10, 4), new DateTime(2026, 10, 5)));
        }
    }
}
