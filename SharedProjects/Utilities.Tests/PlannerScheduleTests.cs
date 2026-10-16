using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The schedule the Schedule tab works out from the scene (Fill IN / RM from names and the date scrubber): what the
    /// export carries, the IN and RM columns, the objects not scheduled and why, and the 4D view at a date. Its parity with
    /// the Planner's own code is in <see cref="PlannerScheduleParityTests"/>.
    /// </summary>
    public class PlannerScheduleTests
    {
        internal const string Strip = "01_02_Strip_Out_01-09-26_05-09-26";
        internal const string Reinstate = "02_01_Reinstate_10-09-26_12-09-26";
        internal const string Undated = "01_03_Undated_TBC";
        internal const string Empty = "01_09_Empty_01-01-27";

        internal static readonly PlannerScheduleOptions Utc = new PlannerScheduleOptions { Zone = TimeZoneInfo.Utc, Today = new DateTime(2026, 10, 15) };

        /// <summary>A built scene: root, two zones, the layers and their helpers, objects linked under their layers' helpers.</summary>
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

        internal static List<string> Column(PlannerSchedule schedule, string label, PlannerTaskType type)
        {
            var layer = schedule.Layers.Single(l => l.Label == label);
            return schedule.ColumnOf(layer, type).SelectMany(a => a.Members).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        private static long At(PlannerSchedule schedule, int year, int month, int day)
        {
            return schedule.Midnight(new DateTime(year, month, day));
        }

        [Fact]
        public void TheColumnsHoldWhatEachLayersDatesPlace()
        {
            var scene = Scene();
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.True(schedule.Usable, schedule.Message);
            Assert.Equal(new DateTime(2026, 9, 1), schedule.ProjectStart);
            Assert.Equal(new DateTime(2026, 9, 12), schedule.WorksEnd);

            // Removed in Ph1 St02 and installed again in Ph2 St01, both segment orders, wherever the object sits: the
            // removal in the RM of 01_02, the install in the IN of 02_01.
            Assert.Equal(new[] { "Ph1_St02_RM_Old_Fence", "Ph2_St01_IN_Fence_Ph1_St02_RM", "ph1_st02_RM_Kerb_A_ph2_st01_IN", "ph1_st02_RM_Kerb_B_ph2_st01_IN" },
                Column(schedule, "01_02_Strip_Out", PlannerTaskType.Dismantle).ToArray());
            Assert.Empty(Column(schedule, "01_02_Strip_Out", PlannerTaskType.Install));
            Assert.Equal(new[] { "Ph2_St01_IN_Fence_Ph1_St02_RM", "Ph2_St01_IN_New_Kerb", "ph1_st02_RM_Kerb_A_ph2_st01_IN", "ph1_st02_RM_Kerb_B_ph2_st01_IN" },
                Column(schedule, "02_01_Reinstate", PlannerTaskType.Install).ToArray());
            Assert.Equal(new[] { "IN 02_01_Reinstate", "RM 01_02_Strip_Out" }, schedule.PlacesOf("ph1_st02_RM_Kerb_B_ph2_st01_IN").ToArray());

            // An undated layer's work is its own, at the project start, TBC.
            var undated = schedule.ColumnOf(schedule.Layers.Single(l => l.Label == "01_03_Undated"), PlannerTaskType.Install).Single();
            Assert.True(undated.Tbc);
            Assert.Equal(At(schedule, 2026, 9, 1), undated.StartMs);

            // A layer with no tagged object is no layer to the Planner.
            Assert.DoesNotContain(schedule.Layers, l => l.Label == "01_09_Empty");
            // The layers know their helpers.
            Assert.Equal(scene.Find(Reinstate).Key, schedule.Layers.Single(l => l.Label == "02_01_Reinstate").NodeKeys.Single());
            Assert.Equal("_02_Zone", schedule.Layers.Single(l => l.Label == "02_01_Reinstate").Group);
        }

        [Fact]
        public void RemovedThenInstalledObjectsShowFromTheStartGoAtTheRemovalAndComeBackAtTheInstall()
        {
            var schedule = PlannerSchedule.Build(Scene().Snapshot, Utc);
            // Kerb A sits in 02_01: its removal is another layer's, a day at the start of 01_02 (1 Sep); its install
            // 02_01's own work.
            Assert.True(schedule.VisibleAt("ph1_st02_RM_Kerb_A_ph2_st01_IN", At(schedule, 2026, 9, 1)));
            Assert.False(schedule.VisibleAt("ph1_st02_RM_Kerb_A_ph2_st01_IN", At(schedule, 2026, 9, 2)));
            Assert.False(schedule.VisibleAt("ph1_st02_RM_Kerb_A_ph2_st01_IN", At(schedule, 2026, 9, 10) - 60000));
            Assert.True(schedule.VisibleAt("ph1_st02_RM_Kerb_A_ph2_st01_IN", At(schedule, 2026, 9, 10)));
            // The install-first spelling of the same: the same.
            Assert.True(schedule.VisibleAt("Ph2_St01_IN_Fence_Ph1_St02_RM", At(schedule, 2026, 9, 1)));
            Assert.False(schedule.VisibleAt("Ph2_St01_IN_Fence_Ph1_St02_RM", At(schedule, 2026, 9, 5)));
            Assert.True(schedule.VisibleAt("Ph2_St01_IN_Fence_Ph1_St02_RM", At(schedule, 2026, 9, 13)));
            // Installed only: hidden until its install.
            Assert.False(schedule.VisibleAt("Ph2_St01_IN_New_Kerb", At(schedule, 2026, 9, 11)));
            Assert.True(schedule.VisibleAt("Ph2_St01_IN_New_Kerb", At(schedule, 2026, 9, 13)));
            // Untagged and unbound: always shown.
            Assert.True(schedule.VisibleAt("Plain_Box", At(schedule, 2026, 8, 1)));
        }

        [Fact]
        public void ARemovalEndingAsTheInstallStartsKeepsTheObjectInView()
        {
            // As the Planner reads it since 2026-10-15: at one instant a removal goes before an install. A one-day layer
            // removes the kerb on 3 Sep and the next layer puts it back first thing on 4 Sep, so the removal ends at the
            // instant the install starts; the kerb is there before, through and after it.
            const string removal = "01_02_Removal_03-09-26";
            const string reinstate = "02_01_Reinstate_04-09-26";
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            scene.Object("Ph1_St02_RM_Old_Sign", scene.Unit(removal, "Work_Phasing", root), removal);
            var layer = scene.Unit(reinstate, "Work_Phasing", root);
            scene.Object("ph1_st02_RM_Kerb_ph2_st01_IN", layer, reinstate);
            scene.Object("Ph2_St01_IN_New_Kerb", layer, reinstate);
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            var events = schedule.Events["ph1_st02_RM_Kerb_ph2_st01_IN"];
            Assert.Equal(new[] { At(schedule, 2026, 9, 4), At(schedule, 2026, 9, 4) }, events.Select(e => e.Instant).ToArray());
            Assert.Equal(new[] { false, true }, events.Select(e => e.Install).ToArray());
            foreach (var instant in new[] { At(schedule, 2026, 9, 1), At(schedule, 2026, 9, 4) - 60000, At(schedule, 2026, 9, 4), At(schedule, 2026, 9, 10) })
            {
                Assert.True(schedule.VisibleAt("ph1_st02_RM_Kerb_ph2_st01_IN", instant));
            }
            Assert.DoesNotContain(scene.Find("ph1_st02_RM_Kerb_ph2_st01_IN").Key, schedule.HiddenAt(At(schedule, 2026, 9, 4)));
            // Without a tie nothing changes: the new kerb is hidden until its install, half way through 4 Sep.
            Assert.False(schedule.VisibleAt("Ph2_St01_IN_New_Kerb", At(schedule, 2026, 9, 4)));
            Assert.True(schedule.VisibleAt("Ph2_St01_IN_New_Kerb", At(schedule, 2026, 9, 5)));
        }

        [Fact]
        public void WithinOneLayerTheNameSaysWhetherTheObjectGoesOrComesFirst()
        {
            // A layer's own work as the Planner orders it since 2026-10-15: first the removals that clear the way (of
            // objects removed only, or whose name writes the removal first), then by name, an object's install and removal
            // in the order its name writes them.
            const string works = "01_03_Temporary_Removal_14-09-26";
            var scene = new TestScene();
            var layer = scene.Unit(works, "Site_Phasing", scene.Unit("Site_Phasing", null, null));
            foreach (var name in new[] { "Ph1_St03_RM_Bollard_Ph1_St03_IN", "Ph1_St03_IN_Fence_Ph1_St03_RM", "Ph1_St03_IN_Aaa_New", "Ph1_St03_RM_Zzz_Old" })
            {
                scene.Object(name, layer, works);
            }
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.Equal(new[] { "Bollard_Ph1_St03_RM", "Zzz_Old_Ph1_St03_RM", "Aaa_New_Ph1_St03_IN", "Fence_Ph1_St03_IN", "Fence_Ph1_St03_RM", "Bollard_Ph1_St03_IN" },
                schedule.Activities.OrderBy(a => a.StartMs).Select(a => a.Name).ToArray());
            Func<int, int, long> on14th = (hour, minute) => At(schedule, 2026, 9, 14) + (hour * 60 + minute) * 60000L;
            // Taken away, then put back: there from the start, gone from the end of its removal (04:00), back from its
            // install (20:00).
            const string bollard = "Ph1_St03_RM_Bollard_Ph1_St03_IN";
            Assert.True(schedule.VisibleAt(bollard, At(schedule, 2026, 9, 13)));
            Assert.True(schedule.VisibleAt(bollard, on14th(3, 59)));
            Assert.False(schedule.VisibleAt(bollard, on14th(4, 0)));
            Assert.False(schedule.VisibleAt(bollard, on14th(19, 59)));
            Assert.True(schedule.VisibleAt(bollard, on14th(20, 0)));
            Assert.True(schedule.VisibleAt(bollard, At(schedule, 2026, 9, 20)));
            // Put up, then taken down: there from its install (12:00) to the end of its removal (20:00).
            const string fence = "Ph1_St03_IN_Fence_Ph1_St03_RM";
            Assert.False(schedule.VisibleAt(fence, At(schedule, 2026, 9, 13)));
            Assert.True(schedule.VisibleAt(fence, on14th(12, 0)));
            Assert.True(schedule.VisibleAt(fence, on14th(19, 59)));
            Assert.False(schedule.VisibleAt(fence, on14th(20, 0)));
            // Both are in the layer's IN and RM.
            Assert.Equal(new[] { "Ph1_St03_IN_Aaa_New", fence, bollard }, Column(schedule, "01_03_Temporary_Removal", PlannerTaskType.Install).ToArray());
            Assert.Equal(new[] { fence, bollard, "Ph1_St03_RM_Zzz_Old" }, Column(schedule, "01_03_Temporary_Removal", PlannerTaskType.Dismantle).ToArray());
        }

        [Fact]
        public void TakenAwayThenPutBackByALayerItIsNotFiledUnderTheRemovalComesFirst()
        {
            // As the Planner reads it since its fixes of 2026-10-15: an object filed under 02_01 whose tags are both
            // 02_02's work (another stage's, twice) has its removal in the first half of 02_02's first day and its install
            // in the second, so it is taken away, then put back at once: there throughout. Put up, then taken down the same
            // way keeps the whole day for both.
            const string kerbs = "02_01_Kerbs_01-09-26_05-09-26";
            const string strip = "02_02_Strip_08-09-26";
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var layer = scene.Unit(kerbs, "Work_Phasing", root);
            scene.Object("Ph2_St01_IN_Kerb", layer, kerbs);
            scene.Object("Ph2_St02_RM_Bench_Ph2_St02_IN", layer, kerbs);
            scene.Object("Ph2_St02_IN_Sign_Ph2_St02_RM", layer, kerbs);
            scene.Object("Ph2_St02_IN_Strip", scene.Unit(strip, "Work_Phasing", root), strip);
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            var day = At(schedule, 2026, 9, 8);
            var rm = schedule.Activities.Single(a => a.Name == "Bench_Ph2_St02_RM");
            var put = schedule.Activities.Single(a => a.Name == "Bench_Ph2_St02_IN");
            Assert.Equal(new[] { day, day + 720 * 60000L }, new[] { rm.StartMs, rm.EndMs });
            Assert.Equal(new[] { day + 720 * 60000L, day + 1440 * 60000L }, new[] { put.StartMs, put.EndMs });
            Assert.Equal("02_01_Kerbs", rm.Home.Label);
            Assert.Equal("02_02_Strip", rm.DatedBy.Label);
            const string bench = "Ph2_St02_RM_Bench_Ph2_St02_IN";
            foreach (var instant in new[] { At(schedule, 2026, 9, 1), day + 720 * 60000L - 60000, day + 720 * 60000L, At(schedule, 2026, 9, 20) })
            {
                Assert.True(schedule.VisibleAt(bench, instant));
            }
            const string sign = "Ph2_St02_IN_Sign_Ph2_St02_RM";
            Assert.All(schedule.Activities.Where(a => a.Members.Contains(sign)), a => Assert.Equal(new[] { day, day + 1440 * 60000L }, new[] { a.StartMs, a.EndMs }));
            Assert.False(schedule.VisibleAt(sign, day - 60000));
            Assert.True(schedule.VisibleAt(sign, day + 720 * 60000L));
            Assert.False(schedule.VisibleAt(sign, At(schedule, 2026, 9, 9)));
            // A naming error all the same (the Planner's of 2026-10-16): taken away and put back in one stage, the bench
            // belongs in that stage's layer. Put up, then taken down, the sign is not listed.
            Assert.Equal(new[] { kerbs + ": " + Misfiled(bench, "Ph2_St02", "Ph2_St01_RM_Bench_Ph2_St01_IN") }, schedule.LayerIssues.ToArray());
            Assert.Equal("Ph2_St02", schedule.Objects[bench].MisfiledStage);
            Assert.Null(schedule.Objects[sign].MisfiledStage);
        }

        // The Planner's issue for an object taken away and put back in one stage that sits in another stage's layer.
        private static string Misfiled(string part, string tag, string retagged)
        {
            return "\"" + part + "\" is taken away and put back in " + tag + ", but it sits in this layer, which is not " + tag + "'s — correct it in 3ds Max: move it to the "
                + tag + " layer, or retag it for this layer as " + retagged;
        }

        // A scene of layers under Work_Phasing, each with its objects (helpers and Max layers of one name).
        private static TestScene LayersScene(params KeyValuePair<string, string[]>[] layers)
        {
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            foreach (var pair in layers)
            {
                var helper = scene.Unit(pair.Key, "Work_Phasing", root);
                foreach (var name in pair.Value)
                {
                    scene.Object(name, helper, pair.Key);
                }
            }
            return scene;
        }

        private static KeyValuePair<string, string[]> L(string layer, params string[] objects)
        {
            return new KeyValuePair<string, string[]>(layer, objects);
        }

        [Fact]
        public void TakenAwayAndPutBackInOneStageButFiledUnderAnotherStagesLayerIsListedAsANamingError()
        {
            // The Planner's rule of 2026-10-16 and its examples: listed under the layer it sits in, with the name retagged
            // for that layer; the scheduling is that of any object filed so.
            const string kerbs = "02_01_Kerbs_01-09-26_05-09-26";
            const string strip = "02_02_Strip_08-09-26";
            var schedule = PlannerSchedule.Build(LayersScene(L(kerbs, "Ph2_St01_IN_Kerb", "Ph2_St02_RM_Bench_Ph2_St02_IN"), L(strip, "Ph2_St02_IN_Strip")).Snapshot, Utc);
            Assert.Equal(new[] { kerbs + ": " + Misfiled("Ph2_St02_RM_Bench_Ph2_St02_IN", "Ph2_St02", "Ph2_St01_RM_Bench_Ph2_St01_IN") }, schedule.LayerIssues.ToArray());
            Assert.Equal("Ph2_St01_RM_Bench_Ph2_St01_IN", schedule.Objects["Ph2_St02_RM_Bench_Ph2_St02_IN"].Retagged);
            // Retagged as advised, or moved to that stage's layer: nothing listed.
            Assert.Empty(PlannerSchedule.Build(LayersScene(L(kerbs, "Ph2_St01_IN_Kerb", "Ph2_St01_RM_Bench_Ph2_St01_IN"), L(strip, "Ph2_St02_IN_Strip")).Snapshot, Utc).LayerIssues);
            Assert.Empty(PlannerSchedule.Build(LayersScene(L(kerbs, "Ph2_St01_IN_Kerb"), L(strip, "Ph2_St02_IN_Strip", "Ph2_St02_RM_Bench_Ph2_St02_IN")).Snapshot, Utc).LayerIssues);

            // A three-number layer, a lower-case tag with the older hyphen and a note after the trailing tag: the rest of the
            // name kept as written, the tags written the current way.
            const string sub = "02_01_2_Kerbs_01-09-26_05-09-26";
            const string old = "ph2_st03-1_RM_Old_Bench_2_Ph2_St03-1_in (1)";
            schedule = PlannerSchedule.Build(LayersScene(L(sub, "Ph2_St01_2_IN_Kerb", old), L("02_03_1_Y_08-09-26", "Ph2_St03_1_IN_X")).Snapshot, Utc);
            Assert.Equal(new[] { sub + ": " + Misfiled(old, "Ph2_St03_1", "Ph2_St01_2_RM_Old_Bench_2_Ph2_St01_2_IN (1)") }, schedule.LayerIssues.ToArray());

            // A one-number layer (its phase its objects'), an object with no description.
            const string five = "05_Works_01-09-26";
            schedule = PlannerSchedule.Build(LayersScene(L(five, "Ph1_St05_IN_A", "Ph1_St07_RM_Ph1_St07_IN")).Snapshot, Utc);
            Assert.Equal(new[] { five + ": " + Misfiled("Ph1_St07_RM_Ph1_St07_IN", "Ph1_St07", "Ph1_St05_RM_Ph1_St05_IN") }, schedule.LayerIssues.ToArray());

            // With no dated layer for that stage too.
            schedule = PlannerSchedule.Build(LayersScene(L(kerbs, "Ph2_St01_IN_Kerb", "Ph2_St09_RM_Bench_Ph2_St09_IN")).Snapshot, Utc);
            Assert.Equal(new[] { kerbs + ": " + Misfiled("Ph2_St09_RM_Bench_Ph2_St09_IN", "Ph2_St09", "Ph2_St01_RM_Bench_Ph2_St01_IN") }, schedule.LayerIssues.ToArray());

            // Not listed: two stages, put up then taken down, a stage and its sub-stage, and in the layer that owns it.
            schedule = PlannerSchedule.Build(LayersScene(
                L(kerbs, "Ph2_St01_IN_Kerb", "Ph2_St02_RM_Bench_Ph2_St03_IN", "Ph2_St02_IN_Fence_Ph2_St02_RM", "Ph2_St03_RM_Post_Ph2_St03_1_IN", "Ph2_St01_RM_Seat_Ph2_St01_IN"),
                L(strip, "Ph2_St02_IN_Strip"), L("02_03_Top_15-09-26", "Ph2_St03_IN_Top")).Snapshot, Utc);
            Assert.Empty(schedule.LayerIssues);
            Assert.All(schedule.Objects.Values, o => Assert.Null(o.MisfiledStage));
        }

        [Fact]
        public void TheIssuesComeInThePlannersOrder()
        {
            // The layers' own name issues and the "same name but different dates" warnings first, then these, layer by layer
            // in the order their first object is read (each under its layer's first name), then the merged and opposite-order
            // issues.
            const string kerbs = "02_01_Kerbs_01-09-26_05-09-26";
            const string copy = "02_01_Kerbs_02-09-26";
            const string works = "01_03_Works_14-09-26";
            var schedule = PlannerSchedule.Build(LayersScene(
                L(kerbs, "Ph2_St01_IN_Kerb", "Ph2_St02_RM_Zed_Ph2_St02_IN", "Ph2_St02_RM_Alpha_Ph2_St02_IN"),
                L(copy, "Ph2_St02_RM_Copy_Ph2_St02_IN"),
                L("02_02_Strip_08-09-26", "Ph2_St02_IN_Strip"),
                L(works, "Ph1_St03_IN_Pole_Ph1_St03_RM", "Ph1_St03_RM_Pole_Ph1_St03_IN")).Snapshot, Utc);
            var issues = schedule.LayerIssues.ToList();
            Assert.Equal(5, issues.Count);
            Assert.StartsWith(copy + ": same name as", issues[0]);
            Assert.Equal(kerbs + ": " + Misfiled("Ph2_St02_RM_Zed_Ph2_St02_IN", "Ph2_St02", "Ph2_St01_RM_Zed_Ph2_St01_IN"), issues[1]);
            Assert.Equal(kerbs + ": " + Misfiled("Ph2_St02_RM_Alpha_Ph2_St02_IN", "Ph2_St02", "Ph2_St01_RM_Alpha_Ph2_St01_IN"), issues[2]);
            Assert.Equal(kerbs + ": " + Misfiled("Ph2_St02_RM_Copy_Ph2_St02_IN", "Ph2_St02", "Ph2_St01_RM_Copy_Ph2_St01_IN"), issues[3]);
            Assert.StartsWith(works + ": \"Ph1_St03_RM_Pole_Ph1_St03_IN\" is taken away, then put back", issues[4]);
        }

        [Fact]
        public void RetaggedKeepsTheRestOfTheNameAsWritten()
        {
            Assert.Equal("Ph2_St01_RM_Bench_Ph2_St01_IN", PlannerSchedule.Retagged("Ph2_St02_RM_Bench_Ph2_St02_IN", 2, 1, null));
            Assert.Equal("Ph2_St01_2_RM_Old_Bench_2_Ph2_St01_2_IN (1)", PlannerSchedule.Retagged("ph2_st03-1_RM_Old_Bench_2_Ph2_St03-1_in (1)", 2, 1, 2));
            Assert.Equal("Ph1_St05_RM_Ph1_St05_IN", PlannerSchedule.Retagged("Ph1_St07_RM_Ph1_St07_IN", 1, 5, null));
            // The trailing tag is the one at the end (or before a note), not a tag-like text inside the description.
            Assert.Equal("Ph3_St10_RM_A_Ph2_St02_IN_B_Ph3_St10_IN", PlannerSchedule.Retagged("Ph2_St02_RM_A_Ph2_St02_IN_B_Ph2_St02_IN", 3, 10, null));
            Assert.Equal("Ph2_St01_RM_C_Ph2_St01_IN note", PlannerSchedule.Retagged("Ph2_St02_RM_C_Ph2_St02_IN note", 2, 1, null));
        }

        [Fact]
        public void ObjectsOfOneDescriptionNeedingOppositeOrdersAreListed()
        {
            // One description, phase, stage and type is one activity, so a pole taken away, then put back and one put up,
            // then taken down share an install and a removal that cannot show both in order: the one shown wrong is listed
            // as the Planner lists it (since 2026-10-15). A description of its own is not.
            const string works = "01_03_Works_14-09-26";
            var scene = new TestScene();
            var layer = scene.Unit(works, "Site_Phasing", scene.Unit("Site_Phasing", null, null));
            foreach (var name in new[] { "Ph1_St03_IN_Pole_Ph1_St03_RM", "Ph1_St03_RM_Pole_Ph1_St03_IN", "Ph1_St03_IN_Aaa", "Ph1_St03_RM_Post_Ph1_St03_IN" })
            {
                scene.Object(name, layer, works);
            }
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.Equal(new[]
            {
                works + ": \"Ph1_St03_RM_Pole_Ph1_St03_IN\" is taken away, then put back (its name writes RM first), but \"Pole_Ph1_St03_RM\" and \"Pole_Ph1_St03_IN\" also hold other objects and put it back before it is taken away — give it a description of its own"
            }, schedule.LayerIssues.ToArray());
            Assert.True(schedule.VisibleAt("Ph1_St03_RM_Post_Ph1_St03_IN", At(schedule, 2026, 9, 13)));
            Assert.True(schedule.VisibleAt("Ph1_St03_RM_Post_Ph1_St03_IN", At(schedule, 2026, 9, 20)));
        }

        [Fact]
        public void StartsAreTheirTimeOnTheWallClockOnClockChangeDays()
        {
            Func<int, int, int, int, int, long> utc = (y, mo, d, h, mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var uk = new PlannerSchedule { Zone = PlannerScheduleParityTests.ZoneOf("Europe/London") };
            var sydney = new PlannerSchedule { Zone = PlannerScheduleParityTests.ZoneOf("Australia/Sydney") };
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

            // A one-day layer on 25 Oct 2026 in London (25 hours): four objects of 375 minutes each follow on without a gap
            // or an overlap; the second starts at 05:15 on the wall clock.
            const string autumn = "01_01_Autumn_25-10-26";
            var scene = new TestScene();
            var layer = scene.Unit(autumn, "Phasing", scene.Unit("Phasing", null, null));
            foreach (var name in new[] { "Ph1_St01_IN_A", "Ph1_St01_IN_B", "Ph1_St01_IN_C", "Ph1_St01_IN_D" })
            {
                scene.Object(name, layer, autumn);
            }
            var schedule = PlannerSchedule.Build(scene.Snapshot, new PlannerScheduleOptions { Zone = uk.Zone, Today = new DateTime(2026, 10, 15) });
            var activities = schedule.Activities.OrderBy(a => a.StartMs).ToList();
            Assert.Equal(new long[] { 375, 375, 375, 375 }, activities.Select(a => a.Duration).ToArray());
            Assert.Equal(utc(2026, 10, 25, 5, 15), activities[1].StartMs);
            for (int i = 1; i < activities.Count; i++)
            {
                Assert.Equal(activities[i - 1].EndMs, activities[i].StartMs);
            }
            Assert.Equal(schedule.Midnight(new DateTime(2026, 10, 26)), activities[3].EndMs);
        }

        [Fact]
        public void ObjectsThePlannerDoesNotScheduleAreListedWithWhy()
        {
            var scene = Scene();
            var hidden = scene.Object("Ph2_St01_IN_Hidden", scene.Find(Reinstate), Reinstate);
            hidden.Hidden = true;
            var noExport = scene.Object("Ph2_St01_IN_No_Export", scene.Find(Reinstate), Reinstate);
            noExport.NoExport = true;
            scene.Object("Ph1_S02_IN_Typo", scene.Find(Strip), Strip);
            scene.Object("Kerb_Ph1_St02_IN", scene.Find(Strip), Strip);
            scene.Object("Kerb_Ph1_St02_IN_001", scene.Find(Strip), Strip);
            scene.Object("Kerb_Ph1_St02_IN001", scene.Find(Strip), Strip);
            scene.Object("Loose_Ph1_St02_IN", null, "0");
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            Func<string, string> reason = name => schedule.NotScheduled.Single(n => n.Name == name).Reason;
            Assert.Equal("it has no Ph/St tag", reason("Plain_Box"));
            Assert.StartsWith("it is not linked under a dated layer's helper", reason("Ph2_St01_IN_Loose"));
            Assert.Contains("Link objects to their helpers", reason("Ph2_St01_IN_Loose"));
            Assert.Equal("not exported: " + PlannerScheduleModel.HiddenReason, reason("Ph2_St01_IN_Hidden"));
            Assert.Equal("not exported: " + PlannerScheduleModel.NoExportReason, reason("Ph2_St01_IN_No_Export"));
            Assert.StartsWith("its tag does not read", reason("Ph1_S02_IN_Typo"));
            // A tag after the start of the name (at its end, in its middle, 3ds Max's clone number after it): the Planner
            // reads an object only when its name starts with its tag.
            Assert.Equal(PlannerSchedule.TagNotAtStartReason, reason("Kerb_Ph1_St02_IN"));
            Assert.Equal(PlannerSchedule.TagNotAtStartReason, reason("Kerb_Ph1_St02_IN_001"));
            Assert.Equal(PlannerSchedule.TagNotAtStartReason, reason("Kerb_Ph1_St02_IN001"));
            Assert.Equal(PlannerSchedule.TagNotAtStartReason + "; no dated layer is above it either", reason("Loose_Ph1_St02_IN"));
            Assert.False(PlannerSchedule.HasTagNotAtStart("Ph1_St02_IN_Kerb"));
            Assert.False(PlannerSchedule.HasTagNotAtStart("Kerb_Ph1_St02_INSIDE"));
            Assert.DoesNotContain(schedule.NotScheduled, n => n.Name == "Ph2_St01_IN_Fence_Ph1_St02_RM");
            // Helpers are not objects.
            Assert.DoesNotContain(schedule.NotScheduled, n => n.Name == Empty || n.Name == "_02_Zone");

            // With Export hidden objects on, the hidden one is scheduled.
            scene.Snapshot.ExportHidden = true;
            var withHidden = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.Contains("Ph2_St01_IN_Hidden", Column(withHidden, "02_01_Reinstate", PlannerTaskType.Install));
        }

        [Fact]
        public void ASecondNodeOfANameIsAnObjectOfItsOwnAsTheExportRenamesIt()
        {
            // The glTF export writes the nodes depth first (the top-level trees holding geometry first) and calls a name
            // it has written already "<name> (1)": the Planner reads that as an object of its own and schedules it.
            var scene = Scene();
            var stripCopy = scene.Object("Ph2_St01_IN_New_Kerb", scene.Find(Strip), Strip);
            var reinstateCopy = scene.Find(Reinstate);
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            var original = scene.Snapshot.Nodes.Single(n => n.Name == "Ph2_St01_IN_New_Kerb" && n.ParentKey == reinstateCopy.Key);
            // _01_Zone comes before _02_Zone, so the copy in 01_02 is written first and keeps the name.
            Assert.Equal("Ph2_St01_IN_New_Kerb", schedule.NameOf(stripCopy.Key));
            Assert.Equal("Ph2_St01_IN_New_Kerb (1)", schedule.NameOf(original.Key));
            var renamed = schedule.ObjectOf(original.Key);
            Assert.Equal("Ph2_St01_IN_New_Kerb (1)", renamed.Name);
            Assert.Equal("Ph2_St01_IN_New_Kerb", renamed.MaxName);
            Assert.Equal(new[] { "IN 02_01_Reinstate" }, schedule.PlacesOfNode(original.Key).ToArray());
            Assert.Equal(new[] { "IN 02_01_Reinstate" }, schedule.PlacesOfNode(stripCopy.Key).ToArray());
            Assert.Contains("Ph2_St01_IN_New_Kerb (1)", Column(schedule, "02_01_Reinstate", PlannerTaskType.Install));
            Assert.DoesNotContain(schedule.NotScheduled, n => n.Name == "Ph2_St01_IN_New_Kerb");
            // Both are hidden until their install.
            var early = schedule.HiddenAt(At(schedule, 2026, 9, 2));
            Assert.Contains(stripCopy.Key, early);
            Assert.Contains(original.Key, early);

            // A node called "X (1)" in 3ds Max, written before a second X the export renames so: the GLB has two nodes of
            // that name, and the Planner shows and hides only the first.
            var literal = scene.Object("Ph1_St02_RM_Post (1)", scene.Find(Strip), Strip);
            scene.Object("Ph1_St02_RM_Post", scene.Find(Strip), Strip);
            var second = scene.Object("Ph1_St02_RM_Post", scene.Find(Strip), Strip);
            var clash = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.Equal("Ph1_St02_RM_Post (1)", clash.NameOf(second.Key));
            Assert.Equal(literal.Key, clash.ObjectOf(literal.Key).Key);
            Assert.Null(clash.ObjectOf(second.Key));
            Assert.StartsWith("another node reaches the Planner as Ph1_St02_RM_Post (1) too", clash.NotScheduled.Single(n => n.Key == second.Key).Reason);
        }

        [Fact]
        public void AWorkspaceThatStartsBeforeItsWorksKeepsItsStart()
        {
            // Generate from model moves the workspace's start back to the earliest dated layer, never forward: a workspace
            // made on 3 Aug for works from 1 Sep keeps 3 Aug, where an undated layer's work sits.
            var undatedInstall = new Func<PlannerSchedule, PlannerScheduleActivity>(s => s.ColumnOf(s.Layers.Single(l => l.Label == "01_03_Undated"), PlannerTaskType.Install).Single());
            var early = PlannerSchedule.Build(Scene().Snapshot, new PlannerScheduleOptions { Zone = TimeZoneInfo.Utc, WorkspaceStart = new DateTime(2026, 8, 3) });
            Assert.Equal(new DateTime(2026, 8, 3), early.ProjectStart);
            Assert.Equal(At(early, 2026, 8, 3), undatedInstall(early).StartMs);
            Assert.Equal(new[] { "01_03_Undated" }, early.ActiveOn(new DateTime(2026, 8, 3)).Select(l => l.Label).ToArray());
            DateTime first, last;
            early.Span(out first, out last);
            Assert.Equal(new DateTime(2026, 8, 3), first);
            // Dated layers' work does not move.
            var assumed = PlannerSchedule.Build(Scene().Snapshot, Utc);
            Assert.Equal(assumed.Activities.Where(a => a.DatedBy != null && a.DatedBy.Dated).Select(a => a.Name + a.StartMs),
                early.Activities.Where(a => a.DatedBy != null && a.DatedBy.Dated).Select(a => a.Name + a.StartMs));
            // A workspace start after the earliest dated layer is moved back to it.
            var late = PlannerSchedule.Build(Scene().Snapshot, new PlannerScheduleOptions { Zone = TimeZoneInfo.Utc, WorkspaceStart = new DateTime(2026, 10, 1) });
            Assert.Equal(new DateTime(2026, 9, 1), late.ProjectStart);
        }

        [Fact]
        public void AHiddenHelperStillCarriesItsLayerAsAnEmptyNode()
        {
            // The export makes an empty node of a hidden node with exported objects under it, so its layer still reads.
            var scene = Scene();
            scene.Find(Reinstate).Hidden = true;
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.Contains("Ph2_St01_IN_New_Kerb", Column(schedule, "02_01_Reinstate", PlannerTaskType.Install));
            // A hidden object with nothing under it is left out.
            scene.Find("Ph2_St01_IN_New_Kerb").Hidden = true;
            Assert.DoesNotContain("Ph2_St01_IN_New_Kerb", Column(PlannerSchedule.Build(scene.Snapshot, Utc), "02_01_Reinstate", PlannerTaskType.Install));
        }

        [Fact]
        public void TheViewHidesWhatIsUnderAnObjectItHides()
        {
            var scene = Scene();
            var kerb = scene.Find("Ph2_St01_IN_New_Kerb");
            var bolt = scene.Object("Bolt", kerb, Reinstate);
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            // Part of the kerb, shown and hidden with it: not an object the Planner leaves out.
            Assert.DoesNotContain(schedule.NotScheduled, n => n.Name == "Bolt");
            var early = schedule.HiddenAt(At(schedule, 2026, 9, 2));
            Assert.Contains(kerb.Key, early);
            Assert.Contains(bolt.Key, early);
            Assert.Contains(scene.Find("ph1_st02_RM_Kerb_A_ph2_st01_IN").Key, early);
            Assert.DoesNotContain(scene.Find("Plain_Box").Key, early);
            Assert.DoesNotContain(scene.Find(Reinstate).Key, early);
            var late = schedule.HiddenAt(At(schedule, 2026, 9, 14));
            Assert.DoesNotContain(kerb.Key, late);
            Assert.DoesNotContain(bolt.Key, late);
            Assert.Contains(scene.Find("Ph1_St02_RM_Old_Fence").Key, late);
        }

        [Fact]
        public void TheScrubberKnowsTheSpanTheLayersAtWorkAndTheDaysChanges()
        {
            var scene = Scene();
            scene.Object("Ph1_St02_IN_Nobody_Ph9_St01_RM", scene.Find(Strip), Strip);
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            DateTime first, last;
            schedule.Span(out first, out last);
            Assert.Equal(new DateTime(2026, 9, 1), first);
            // A trailing removal no dated layer has is parked the day after the works end (12 Sep).
            Assert.Equal(new DateTime(2026, 9, 13), last);
            Assert.Equal(new[] { "01_02_Strip_Out", "01_03_Undated" }, schedule.ActiveOn(new DateTime(2026, 9, 1)).Select(l => l.Label).ToArray());
            Assert.Equal(new[] { "02_01_Reinstate" }, schedule.ActiveOn(new DateTime(2026, 9, 11)).Select(l => l.Label).ToArray());
            Assert.Empty(schedule.ActiveOn(new DateTime(2026, 9, 7)));
            // On 1 Sep: Kerb A and Fence go (the one-day removal at the start of 01_02), Kerb B's removal and Old Fence's run on.
            var changing = schedule.ChangingOn(new DateTime(2026, 9, 1)).Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Contains("ph1_st02_RM_Kerb_A_ph2_st01_IN", changing);
            Assert.Contains("Ph2_St01_IN_Fence_Ph1_St02_RM", changing);
            Assert.Contains("Ph1_St03_IN_Undated_Work", changing);
            Assert.Equal(new[] { "Ph1_St02_IN_Nobody_Ph9_St01_RM" }, schedule.ChangingOn(new DateTime(2026, 9, 13)).Select(o => o.Name).ToArray());
            Assert.Equal(new[] { "IN 01_02_Strip_Out", "RM (after the end)" }, schedule.PlacesOf("Ph1_St02_IN_Nobody_Ph9_St01_RM").ToArray());
            // A stray whose only tag no dated layer has is its own layer's work, not parked.
            scene.Object("Ph9_St01_RM_Stray", scene.Find(Strip), Strip);
            Assert.Equal(new[] { "RM 01_02_Strip_Out" }, PlannerSchedule.Build(scene.Snapshot, Utc).PlacesOf("Ph9_St01_RM_Stray").ToArray());
        }

        [Fact]
        public void LeavingThePreviewGivesEveryNodeItsOwnHiddenStateBack()
        {
            var scene = Scene();
            var modellerHidden = scene.Find("Ph2_St01_IN_New_Kerb");
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            // The node's own hidden flags, as 3ds Max holds them, the preview's changes carried out on them.
            var own = scene.Snapshot.Nodes.ToDictionary(n => n.Key, n => n.Key == modellerHidden.Key);
            var original = new Dictionary<long, bool>(own);
            var preview = new PlannerPreview();
            foreach (var day in new[] { 1, 2, 11, 30, 3, 13 })
            {
                var toHide = new List<long>();
                var toShow = new List<long>();
                var view = schedule.HiddenAt(At(schedule, 2026, 9, day));
                preview.Step(view, k => own[k], toHide, toShow);
                Assert.DoesNotContain(modellerHidden.Key, toHide);
                Assert.DoesNotContain(modellerHidden.Key, toShow);
                toHide.ForEach(k => { Assert.False(own[k]); own[k] = true; });
                toShow.ForEach(k => { Assert.True(own[k]); own[k] = false; });
                // Exactly what the view hides is hidden, and what the modeller hid stays hidden.
                Assert.All(view, k => Assert.True(own[k]));
                Assert.All(own.Where(p => !view.Contains(p.Key)), p => Assert.Equal(original[p.Key], p.Value));
            }
            Assert.NotEqual(0, preview.Count);
            preview.Restore().ForEach(k => own[k] = false);
            Assert.Equal(original, own);
            Assert.Equal(0, preview.Count);
        }

        [Fact]
        public void APreviewStepStaysQuickOnALargeScene()
        {
            // 20,000 objects in 100 layers over a year: a scrub step works out the view and the changes.
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var start = new DateTime(2026, 1, 5);
            for (int l = 1; l <= 100; l++)
            {
                var day = start.AddDays(l * 3);
                var name = "01_" + (l % 100).ToString("00") + "_Layer" + l + "_" + day.ToString("dd-MM-yy") + "_" + day.AddDays(2).ToString("dd-MM-yy");
                var helper = scene.Unit(name, "Work_Phasing", root);
                for (int o = 0; o < 200; o++)
                {
                    scene.Object("Ph1_St" + (l % 100).ToString("00") + "_IN_Obj_" + l + "_" + o + (o % 3 == 0 ? "_Ph1_St" + ((l + 7) % 100).ToString("00") + "_RM" : string.Empty), helper, name);
                }
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            var build = watch.ElapsedMilliseconds;
            Assert.Equal(20000, schedule.Objects.Count);
            var own = scene.Snapshot.Nodes.ToDictionary(n => n.Key, n => false);
            var preview = new PlannerPreview();
            watch.Restart();
            int steps = 0;
            for (var day = start; day < start.AddDays(320); day = day.AddDays(4), steps++)
            {
                var toHide = new List<long>();
                var toShow = new List<long>();
                preview.Step(schedule.HiddenAt(schedule.Midnight(day)), k => own[k], toHide, toShow);
            }
            var perStep = watch.ElapsedMilliseconds / (double)steps;
            Assert.True(perStep < 250, "a step took " + perStep + " ms (build " + build + " ms)");
        }

        [Fact]
        public void WithoutDatedLayersTheTabSaysTheSegmentModeIsNotShown()
        {
            var scene = new TestScene();
            var root = scene.Unit("Phasing", null, null);
            var group = scene.Unit("Group_A", "Phasing", root);
            scene.Object("Ph1_St01_IN_A", group, "Group_A");
            var schedule = PlannerSchedule.Build(scene.Snapshot, Utc);
            Assert.False(schedule.Usable);
            Assert.Contains("segment mode", schedule.Message);
            Assert.Empty(schedule.Layers);
            Assert.True(schedule.VisibleAt("Ph1_St01_IN_A", 0));
        }
    }
}
