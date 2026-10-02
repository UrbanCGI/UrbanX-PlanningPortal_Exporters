using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Babylon2GLTF;
using BabylonExport.Entities;
using Max2Babylon;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>The glTF extras the exporter writes for the Planner (contract section 5) and the schedule file reader (section 6).</summary>
    public class PlannerExtrasTests
    {
        private const string Guid1 = "0f8fad5b-d9cb-469f-a165-70867728950e";

        [Fact]
        public void Build_EveryNodeGetsItsGuid()
        {
            var planner = PlannerNodeExtras.Build(Guid1.ToUpperInvariant(), new Dictionary<string, string>());
            Assert.Equal(new[] { "guid" }, planner.Keys.ToArray());
            Assert.Equal(Guid1, planner["guid"]);
        }

        [Fact]
        public void Build_HelperCarriesFolderCodeNameAndRoot()
        {
            var planner = PlannerNodeExtras.Build(Guid1, new Dictionary<string, string>
            {
                { PlannerProps.FolderId, "f1" },
                { PlannerProps.Code, "01-04.2" },
                { PlannerProps.Name, "Phase1_Retainment_Installation" },
                { PlannerProps.Root, "true" },
                { PlannerProps.LegacyStart, "2026-09-18" }, // dropped: a schedule has been applied
                { "planner_unknown", "x" }
            });
            Assert.Equal(new[] { "guid", "folderId", "code", "name", "root" }, planner.Keys.ToArray());
            Assert.Equal("f1", planner["folderId"]);
            Assert.Equal("01-04.2", planner["code"]);
            Assert.Equal("Phase1_Retainment_Installation", planner["name"]);
            Assert.Equal(true, planner["root"]);
        }

        [Fact]
        public void Build_LegacyDatesUntilAScheduleIsApplied()
        {
            var planner = PlannerNodeExtras.Build(Guid1, new Dictionary<string, string>
            {
                { PlannerProps.Code, "01-04.1" },
                { PlannerProps.LegacyStart, "2026-09-18" },
                { PlannerProps.LegacyFinish, "2026-10-23" },
                { PlannerProps.LegacyTbc, "false" },
                { PlannerProps.Root, "false" }
            });
            Assert.False(planner.ContainsKey("root"));
            var legacy = Assert.IsType<Dictionary<string, object>>(planner["legacy"]);
            Assert.Equal("2026-09-18", legacy["start"]);
            Assert.Equal("2026-10-23", legacy["finish"]);
            Assert.Equal(false, legacy["tbc"]);

            // Absent or unreadable values are omitted.
            var tbcOnly = PlannerNodeExtras.Build(Guid1, new Dictionary<string, string> { { PlannerProps.LegacyStart, "18-09-26" }, { PlannerProps.LegacyTbc, "true" } });
            var tbcLegacy = Assert.IsType<Dictionary<string, object>>(tbcOnly["legacy"]);
            Assert.Equal(new[] { "tbc" }, tbcLegacy.Keys.ToArray());
            Assert.Equal(true, tbcLegacy["tbc"]);
        }

        [Fact]
        public void Merge_KeepsEveryKeyAndNeverTouchesTheInput()
        {
            var metadata = new Dictionary<string, object>
            {
                { "babylonAlphaTest", true },
                { "planner", new Dictionary<string, object> { { "note", "user" }, { "guid", "old" } } }
            };
            var additions = PlannerNodeExtras.ForNode(Guid1, null);
            var merged = PlannerNodeExtras.Merge(metadata, additions);

            Assert.NotSame(metadata, merged);
            Assert.Equal(true, merged["babylonAlphaTest"]);
            var planner = Assert.IsType<Dictionary<string, object>>(merged["planner"]);
            Assert.Equal("user", planner["note"]);
            Assert.Equal(Guid1, planner["guid"]);
            Assert.Equal("old", ((Dictionary<string, object>)metadata["planner"])["guid"]);
            Assert.Equal(2, metadata.Count);

            Assert.Same(metadata, PlannerNodeExtras.Merge(metadata, null));
            Assert.Equal(new[] { "planner" }, PlannerNodeExtras.Merge(null, additions).Keys.ToArray());
        }

        [Fact]
        public void Merge_KeepsAUserPlannerBlockReadFromTheExtrasProperty()
        {
            // As the exporter reads a node's "extras" user property (Newtonsoft leaves nested objects as JObject).
            const string json = "{ \"babylonAlphaTest\": true, \"planner\": { \"note\": \"user\", \"guid\": \"old\", \"detail\": { \"a\": 1 } }, \"other\": { \"b\": 2 } }";
            bool hasPlannerKey;
            var metadata = PlannerUserExtras.Parse(json, out hasPlannerKey);
            Assert.True(hasPlannerKey);
            Assert.IsType<JObject>(metadata["other"]);   // only the reserved key is converted

            var merged = PlannerNodeExtras.Merge(metadata, PlannerNodeExtras.ForNode(Guid1, new Dictionary<string, string> { { PlannerProps.FolderId, "f1" } }));
            var planner = Assert.IsType<Dictionary<string, object>>(merged["planner"]);
            Assert.Equal("user", ((JValue)planner["note"]).Value);
            Assert.Equal(Guid1, planner["guid"]);
            Assert.Equal("f1", planner["folderId"]);
            Assert.IsType<Dictionary<string, object>>(planner["detail"]);

            bool none;
            Assert.False(PlannerUserExtras.Parse("{ \"a\": 1 }", out none).ContainsKey("planner"));
            Assert.False(none);
        }

        [Fact]
        public void GltfExport_WritesThePlannerExtrasAndLeavesTheRestAlone()
        {
            var directory = Path.Combine(Path.GetTempPath(), "planner-extras-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var scene = new BabylonScene(directory)
                {
                    producer = new BabylonProducer { name = "3dsmax", version = "2024", exporter_version = "1.1-urbancgi", file = "model.babylon" }
                };
                var helper = new BabylonMesh { name = "01-04.1_Phase1", id = Guid1, isDummy = true, position = new float[3], rotation = new float[3], scaling = new[] { 1f, 1f, 1f } };
                helper.gltfExtras = PlannerNodeExtras.ForNode(Guid1, new Dictionary<string, string> { { PlannerProps.Code, "01-04.1" }, { PlannerProps.Name, "Phase1" } });
                var tagged = new BabylonMesh { name = "Ph1_St04_IN_Sheet_Pile_1", id = "1b4e28ba-2fa1-11d2-883f-0016d3cca427", parentId = Guid1, isDummy = true, position = new float[3], rotation = new float[3], scaling = new[] { 1f, 1f, 1f } };
                tagged.metadata = new Dictionary<string, object> { { "userKey", "kept" } };
                tagged.gltfExtras = PlannerNodeExtras.ForNode(tagged.id, null);
                var plain = new BabylonMesh { name = "Context", id = "6fa459ea-ee8a-3ca4-894e-db77e160355e", isDummy = true, position = new float[3], rotation = new float[3], scaling = new[] { 1f, 1f, 1f } };
                scene.MeshesList.Add(helper);
                scene.MeshesList.Add(tagged);
                scene.MeshesList.Add(plain);
                scene.Prepare(false, false);

                new GLTFExporter().ExportGltf(new ExportParameters(), scene, directory, "model.gltf", false, new TestLogger());

                var gltf = JObject.Parse(File.ReadAllText(Path.Combine(directory, "model.gltf")));
                Assert.Equal("UrbanCGI Planner Exporters for 3dsmax 2024 v1.1-urbancgi", (string)gltf["asset"]["generator"]);
                var nodes = ((JArray)gltf["nodes"]).Cast<JObject>().ToDictionary(n => (string)n["name"]);

                var helperPlanner = nodes["01-04.1_Phase1"]["extras"]["planner"];
                Assert.Equal(Guid1, (string)helperPlanner["guid"]);
                Assert.Equal("01-04.1", (string)helperPlanner["code"]);
                Assert.Equal("Phase1", (string)helperPlanner["name"]);

                Assert.Equal("kept", (string)nodes["Ph1_St04_IN_Sheet_Pile_1"]["extras"]["userKey"]);
                Assert.Equal(tagged.id, (string)nodes["Ph1_St04_IN_Sheet_Pile_1"]["extras"]["planner"]["guid"]);
                Assert.Single(tagged.metadata); // the Babylon metadata itself is not modified

                // A node without glTF extras (what Maya exports) is written exactly as before.
                Assert.Null(nodes["Context"]["extras"]);
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        }

        // ---- schedule file ---------------------------------------------------------------------------------------

        private const string ScheduleJson = @"{
  ""format"": ""urbancgi-planner-schedule"",
  ""version"": 1,
  ""exportedAt"": ""2026-10-02T05:30:00.000Z"",
  ""workspace"": { ""id"": ""ws-id"", ""name"": ""HS2 Euston"" },
  ""project"": { ""id"": ""p-id"", ""name"": ""EUS_HmRd_HRB"", ""timeZone"": null },
  ""folders"": [
    { ""id"": ""f1"", ""parentId"": null, ""code"": ""01"", ""name"": ""Zone5_Working"",
      ""start"": ""2026-06-08T00:00"", ""finish"": ""2026-10-24T00:00"", ""tbc"": false, ""colour"": ""#fff"" },
    { ""id"": ""f2"", ""parentId"": ""f1"", ""code"": null, ""name"": ""Drainage"", ""start"": null, ""finish"": null, ""tbc"": true }
  ],
  ""activities"": [
    { ""id"": ""a1"", ""folderId"": ""f1"", ""name"": ""Sheet_Pile_1_Ph1_St04_IN"", ""type"": ""Install"",
      ""start"": ""2026-09-18T00:00"", ""finish"": ""2026-09-24T00:00"", ""tbc"": false,
      ""objects"": [""Ph1_St04_IN_Sheet_Pile_1""] },
    { ""id"": ""a2"", ""folderId"": null, ""name"": ""Loose"", ""type"": ""Dismantle"", ""start"": ""2026-13-40T00:00"", ""finish"": null, ""tbc"": true, ""objects"": [] }
  ],
  ""somethingNew"": { ""ignored"": true }
}";

        [Fact]
        public void ScheduleJson_ReadsTheContractExample()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var schedule = PlannerScheduleJson.Parse(ScheduleJson, errors, warnings);

            Assert.Empty(errors);
            Assert.NotNull(schedule);
            Assert.Equal(1, schedule.Version);
            Assert.Equal("2026-10-02T05:30:00.000Z", schedule.ExportedAt);
            Assert.Equal("HS2 Euston", schedule.WorkspaceName);
            Assert.Equal("p-id", schedule.ProjectId);
            Assert.Null(schedule.ProjectTimeZone);

            Assert.Equal(2, schedule.Folders.Count);
            var zone = schedule.Folders[0];
            Assert.Equal("01", zone.Code);
            Assert.Null(zone.ParentId);
            Assert.Equal("01_Zone5_Working", zone.LayerName);
            Assert.Equal("2026-10-24T00:00", zone.Finish);
            Assert.False(zone.Tbc);
            var drainage = schedule.Folders[1];
            Assert.Equal("f1", drainage.ParentId);
            Assert.Null(drainage.Code);
            Assert.Equal("Drainage", drainage.LayerName);
            Assert.True(drainage.Tbc);

            Assert.Equal(2, schedule.Activities.Count);
            Assert.Equal(PlannerTaskType.Install, schedule.Activities[0].Type);
            Assert.Equal(new[] { "Ph1_St04_IN_Sheet_Pile_1" }, schedule.Activities[0].Objects);
            Assert.Null(schedule.Activities[1].FolderId);
            Assert.Equal(PlannerTaskType.Dismantle, schedule.Activities[1].Type);
            Assert.Single(warnings);
            Assert.Contains("unreadable start", warnings[0]);

            DateTime finish;
            Assert.True(PlannerScheduleDates.TryParse(schedule.Activities[0].Finish, out finish));
            Assert.Equal(new DateTime(2026, 9, 23), PlannerScheduleDates.LastDay(finish)); // finish is exclusive
            Assert.Equal(new DateTime(2026, 9, 24), PlannerScheduleDates.LastDay(finish.AddHours(13)));
        }

        [Theory]
        [InlineData("not json", "not valid JSON")]
        [InlineData("[]", "does not hold a schedule")]
        [InlineData(@"{ ""format"": ""something-else"", ""version"": 1, ""folders"": [] }", "not a Planner schedule file")]
        [InlineData(@"{ ""format"": ""urbancgi-planner-schedule"", ""version"": 2, ""folders"": [] }", "version 2")]
        [InlineData(@"{ ""format"": ""urbancgi-planner-schedule"", ""version"": 0, ""folders"": [] }", "version 0, which is not a valid")]
        [InlineData(@"{ ""format"": ""urbancgi-planner-schedule"", ""version"": ""1"", ""folders"": [] }", "no version")]
        [InlineData(@"{ ""format"": ""urbancgi-planner-schedule"", ""folders"": [] }", "no version")]
        [InlineData(@"{ ""format"": ""urbancgi-planner-schedule"", ""version"": 1 }", "no folders list")]
        public void ScheduleJson_RefusesWhatItCannotRead(string json, string reason)
        {
            var errors = new List<string>();
            Assert.Null(PlannerScheduleJson.Parse(json, errors, null));
            Assert.Contains(errors, e => e.Contains(reason));
        }
    }
}
