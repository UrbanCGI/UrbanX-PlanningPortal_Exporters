using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// The schedule the Schedule tab works out (<see cref="PlannerSchedule"/>) against the Planner's own code run on the
    /// same models: every activity (name, install or removal, folder, TBC, start and finish instants, the objects it
    /// binds), every layer folder, the unfiled objects and the 4D view's hidden objects at every instant something
    /// changes (and a minute either side) and every midnight.
    ///
    /// The Planner's results are made by a harness that bundles the Planner's main branch (phasingFromModel.ts,
    /// nodeNames.ts, schedule.ts, timeline.ts) and runs Generate from model as the store does, on each scene as the
    /// export writes it (its nodes in the export's order, a second node of a name called "&lt;name&gt; (1)"):
    ///   PlannerScheduleScenes.json         synthetic scenes (removed then installed in both segment orders, strays, TBC,
    ///                                      one-number layers, sub-stages, splits, copies, clock changes, ...)
    ///   PlannerScheduleScenes.utc.json     the Planner's results in UTC
    ///   PlannerScheduleScenes.sydney.json  the Planner's results in Sydney time (clocks change on 4 Oct 2026 and 5 Apr 2027)
    /// and, kept out of the repo, the HS2 model:
    ///   PLANNER_SCHEDULE_HS2          the v3_06u node tree { "roots", "nodes": [ { "name", "children", "mesh" } ] }
    ///   PLANNER_SCHEDULE_HS2_PLANNER  the harness's results for it (any zone the harness ran in: UTC or Sydney)
    /// </summary>
    public class PlannerScheduleParityTests
    {
        private readonly ITestOutputHelper output;

        public PlannerScheduleParityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static string Here([CallerFilePath] string path = null)
        {
            return Path.GetDirectoryName(path);
        }

        /// <summary>A scene of the parity files: nodes with their parent's index (or -1) and whether each is a mesh.</summary>
        internal static PlannerSceneSnapshot SnapshotOf(JArray nodes)
        {
            var snapshot = new PlannerSceneSnapshot();
            var childCount = new Dictionary<long, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = (JObject)nodes[i];
                int parent = (int)node["parent"];
                long? parentKey = parent >= 0 ? parent + 1 : (long?)null;
                int index;
                childCount.TryGetValue(parentKey ?? 0, out index);
                childCount[parentKey ?? 0] = index + 1;
                snapshot.Nodes.Add(new PlannerSceneNode
                {
                    Key = i + 1,
                    Name = (string)node["name"],
                    ParentKey = parentKey,
                    ChildIndex = index,
                    IsHelper = !(bool)node["mesh"]
                });
            }
            var byKey = snapshot.Nodes.ToDictionary(n => n.Key);
            foreach (var node in snapshot.Nodes.Where(n => n.ParentKey.HasValue))
            {
                byKey[node.ParentKey.Value].HasChildren = true;
            }
            return snapshot;
        }

        /// <summary>A GLB node tree { roots, nodes: [ { name, children, mesh } ] } as a scene.</summary>
        internal static JArray NodesOfTree(JObject tree)
        {
            var nodes = (JArray)tree["nodes"];
            var parent = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                foreach (var child in nodes[i]["children"])
                {
                    parent[(int)child] = i;
                }
            }
            var result = new JArray();
            for (int i = 0; i < nodes.Count; i++)
            {
                int p;
                result.Add(new JObject
                {
                    ["name"] = nodes[i]["name"],
                    ["parent"] = parent.TryGetValue(i, out p) ? p : -1,
                    ["mesh"] = nodes[i]["mesh"] == null || (bool)nodes[i]["mesh"]
                });
            }
            return result;
        }

        internal static TimeZoneInfo ZoneOf(string zone)
        {
            switch (zone)
            {
                case "UTC":
                    return TimeZoneInfo.Utc;
                case "Australia/Sydney":
                    return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
                case "Europe/London":
                    return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                default:
                    return TimeZoneInfo.Local;
            }
        }

        internal sealed class Counts
        {
            public int Scenes;
            public int Activities;
            public int Objects;
            public int Instants;
            public int Checks;
            public int Folders;
        }

        /// <summary>Runs the schedule on a scene and lists every way it differs from the Planner's result for it.</summary>
        internal static List<string> Compare(JArray nodes, string workspaceStart, JObject planner, Counts counts, out PlannerSchedule schedule)
        {
            var differences = new List<string>();
            var id = (string)planner["id"];
            var options = new PlannerScheduleOptions
            {
                Zone = ZoneOf((string)planner["zone"]),
                Today = new DateTime(2026, 10, 15),
                WorkspaceStart = workspaceStart != null ? DateTime.ParseExact(workspaceStart, "yyyy-MM-dd", CultureInfo.InvariantCulture) : (DateTime?)null
            };
            var built = PlannerSchedule.Build(SnapshotOf(nodes), options);
            schedule = built;
            counts.Scenes++;
            Action<string> differ = text => differences.Add(id + ": " + text);
            var mode = (string)planner["mode"];
            if (mode != "layers")
            {
                if (built.Usable)
                {
                    differ("the Planner uses " + mode + " mode, the schedule layer mode");
                }
                return differences;
            }
            if (!built.Usable)
            {
                differ("the schedule says: " + built.Message);
                return differences;
            }
            var start = built.ProjectStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (start != (string)planner["projectStart"])
            {
                differ("project start " + start + " against " + planner["projectStart"]);
            }

            // Activities, one to one by name.
            var mine = built.Activities.ToDictionary(a => a.Name, StringComparer.Ordinal);
            var theirs = planner["activities"].Cast<JObject>().ToList();
            foreach (var a in theirs)
            {
                var name = (string)a["name"];
                PlannerScheduleActivity activity;
                if (!mine.TryGetValue(name, out activity))
                {
                    differ("missing activity " + name);
                    continue;
                }
                counts.Activities++;
                var type = activity.Type == PlannerTaskType.Install ? "Install" : "Dismantle";
                if (type != (string)a["type"]) differ(name + ": type " + type + " against " + a["type"]);
                if (activity.Home.Label != (string)a["folder"]) differ(name + ": folder " + activity.Home.Label + " against " + a["folder"]);
                if (activity.Tbc != (bool)a["tbc"]) differ(name + ": TBC " + activity.Tbc + " against " + a["tbc"]);
                if (activity.StartMs != (long)a["startMs"]) differ(name + ": start " + Show(built, activity.StartMs) + " against " + Show(built, (long)a["startMs"]));
                if (activity.EndMs != (long)a["endMs"]) differ(name + ": finish " + Show(built, activity.EndMs) + " against " + Show(built, (long)a["endMs"]));
                var members = a["members"].Select(m => (string)m).ToList();
                if (!members.SequenceEqual(activity.Members)) differ(name + ": binds " + string.Join(",", activity.Members) + " against " + string.Join(",", members));
            }
            foreach (var name in mine.Keys.Except(theirs.Select(a => (string)a["name"])))
            {
                differ("extra activity " + name);
            }
            counts.Objects += built.Objects.Count;

            // Layer folders: name, the group folder holding it, TBC.
            var folders = planner["folders"].Cast<JObject>().ToDictionary(f => (string)f["name"], StringComparer.Ordinal);
            foreach (var layer in built.Layers)
            {
                JObject folder;
                if (!folders.TryGetValue(layer.Label, out folder))
                {
                    differ("missing folder " + layer.Label);
                    continue;
                }
                counts.Folders++;
                var group = layer.Group != null ? PlannerSchedule.ParsePhaseGroup(layer.Group) : null;
                if (group != (string)folder["parent"]) differ(layer.Label + ": in " + (group ?? "(top)") + " against " + ((string)folder["parent"] ?? "(top)"));
                if (layer.Tbc != (bool)folder["tbc"]) differ(layer.Label + ": TBC " + layer.Tbc + " against " + folder["tbc"]);
            }

            // Unfiled objects and layer issues.
            // The Planner names them as the GLB does (a second node of a name is "<name> (1)").
            var unfiled = built.NotScheduled.Where(n => n.Reason.StartsWith("it is not linked under a dated layer", StringComparison.Ordinal))
                .Select(n => built.NameOf(n.Key) ?? n.Name).OrderBy(n => n, StringComparer.Ordinal);
            var theirUnfiled = planner["unfiled"].Select(n => (string)n).OrderBy(n => n, StringComparer.Ordinal);
            if (!unfiled.SequenceEqual(theirUnfiled)) differ("unfiled " + string.Join(",", unfiled) + " against " + string.Join(",", theirUnfiled));
            var issues = built.LayerIssues.OrderBy(n => n, StringComparer.Ordinal);
            var theirIssues = planner["layerIssues"].Select(n => (string)n).OrderBy(n => n, StringComparer.Ordinal);
            if (!issues.SequenceEqual(theirIssues)) differ("layer issues " + string.Join(" | ", issues) + " against " + string.Join(" | ", theirIssues));

            // The 4D view at every instant.
            var instants = planner["instants"].Select(t => (long)t).ToList();
            var hidden = (JArray)planner["hidden"];
            for (int i = 0; i < instants.Count; i++)
            {
                var expected = hidden[i].Select(n => (string)n).ToList();
                var actual = built.Events.Keys.Where(n => !built.VisibleAt(n, instants[i])).OrderBy(n => n, StringComparer.Ordinal).ToList();
                counts.Instants++;
                counts.Checks += built.Events.Count;
                if (!expected.SequenceEqual(actual))
                {
                    differ("at " + Show(built, instants[i]) + " hidden " + string.Join(",", actual.Except(expected)) + " / shown " + string.Join(",", expected.Except(actual)));
                }
            }
            return differences;
        }

        private static string Show(PlannerSchedule schedule, long ms)
        {
            return schedule.LocalTime(ms).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private void RunGolden(string file)
        {
            var scenes = JArray.Parse(File.ReadAllText(Path.Combine(Here(), "PlannerScheduleScenes.json")));
            var results = JArray.Parse(File.ReadAllText(Path.Combine(Here(), file)));
            var counts = new Counts();
            var differences = new List<string>();
            foreach (JObject result in results)
            {
                var scene = scenes.Cast<JObject>().Single(s => (string)s["id"] == (string)result["id"]);
                PlannerSchedule schedule;
                differences.AddRange(Compare((JArray)scene["nodes"], (string)scene["workspaceStart"], result, counts, out schedule));
            }
            output.WriteLine(file + ": " + counts.Scenes + " scenes, " + counts.Activities + " activities, " + counts.Objects + " objects, "
                + counts.Folders + " layer folders, " + counts.Instants + " instants (" + counts.Checks + " object checks); " + differences.Count + " differences");
            Assert.True(differences.Count == 0, string.Join("\n", differences.Take(40)));
        }

        [Fact]
        public void SyntheticScenesMatchThePlannerInUtc()
        {
            RunGolden("PlannerScheduleScenes.utc.json");
        }

        [Fact]
        public void SyntheticScenesMatchThePlannerInSydneyTime()
        {
            RunGolden("PlannerScheduleScenes.sydney.json");
        }

        [Fact]
        public void Hs2MatchesThePlanner()
        {
            var treePath = Environment.GetEnvironmentVariable("PLANNER_SCHEDULE_HS2");
            var plannerPath = Environment.GetEnvironmentVariable("PLANNER_SCHEDULE_HS2_PLANNER");
            if (string.IsNullOrEmpty(treePath) || string.IsNullOrEmpty(plannerPath) || !File.Exists(treePath) || !File.Exists(plannerPath))
            {
                output.WriteLine("PLANNER_SCHEDULE_HS2 / PLANNER_SCHEDULE_HS2_PLANNER are not set; the HS2 parity run is skipped.");
                return;
            }
            var nodes = NodesOfTree(JObject.Parse(File.ReadAllText(treePath)));
            var counts = new Counts();
            var differences = new List<string>();
            foreach (JObject result in JArray.Parse(File.ReadAllText(plannerPath)))
            {
                PlannerSchedule schedule;
                differences.AddRange(Compare(nodes, null, result, counts, out schedule));
                var lines = new List<string>
                {
                    "HS2 v3_06u schedule parity (" + result["zone"] + "): " + counts.Activities + " activities, " + counts.Objects + " objects, "
                        + counts.Folders + " layer folders, " + counts.Instants + " instants (" + counts.Checks + " object checks); " + differences.Count + " differences",
                    "Project start " + schedule.ProjectStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", works end "
                        + (schedule.WorksEnd.HasValue ? schedule.WorksEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-")
                        + "; layers " + schedule.Layers.Count + ", IN " + schedule.Activities.Where(a => a.Type == PlannerTaskType.Install).Sum(a => a.Members.Count)
                        + ", RM " + schedule.Activities.Where(a => a.Type == PlannerTaskType.Dismantle).Sum(a => a.Members.Count)
                        + ", placed by rule " + schedule.Activities.Count(a => a.DatedBy == null) + ", not scheduled " + schedule.NotScheduled.Count
                };
                lines.AddRange(differences.Take(40));
                foreach (var line in lines)
                {
                    output.WriteLine(line);
                }
                var report = Environment.GetEnvironmentVariable("PLANNER_LAYERS_REPORT");
                if (!string.IsNullOrEmpty(report))
                {
                    File.AppendAllText(report, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
                }
            }
            Assert.True(differences.Count == 0, string.Join("\n", differences.Take(40)));
        }
    }
}
