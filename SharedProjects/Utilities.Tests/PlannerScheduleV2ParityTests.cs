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
    /// The new names' schedule the Schedule tab works out (<see cref="PlannerSchedule.BuildV2(PlannerSceneSnapshot, PlannerScheduleOptions)"/>,
    /// naming v2) against the Planner's own code run on the same scenes: the scene's naming, every activity (matched by the
    /// object it is made for and its type, as names can repeat on the new names: its name, folder, TBC, start and finish
    /// instants, the objects it binds, its event layer, the activity it follows, and the days the tab's Gantt colours for
    /// it, hatched as the Planner hatches them), every folder (where it sits, TBC, its parked day), the report's lists
    /// (scheduled, untagged and unfiled objects, layer issues, TBC work, parked layers, references to layers nobody has,
    /// naming issues, old names, folders without a layer, shared IDs, renamed layers, names ending " (n)", the wrong-order
    /// list) and the 4D view's hidden objects at every instant something changes (and a minute either side) and every
    /// midnight.
    ///
    /// The Planner's results come from a harness that runs its committed naming v2 code through the real store: a folder
    /// per ID of the scene's layer list (a dated one holding its days as its own work), the scene published as the viewport
    /// publishes it, then Generate (Merge into that workspace). The tab's preview is that first Generate (spec 5.6):
    ///   PlannerScheduleScenesV2.json          41 synthetic scenes (spec 2 and 3), with the list's days per ID
    ///                                         (layerDates), list-only IDs (listOnly) and the project start
    ///   PlannerScheduleScenesV2.utc.json      the Planner's results in UTC
    ///   PlannerScheduleScenesV2.sydney.json   in Sydney time (clocks change on 4 Oct 2026, 4 Apr 2027 and 3 Oct 2027)
    ///   PlannerScheduleScenesV2.london.json   in London time (clocks change on 25 Oct 2026 and 28 Mar 2027)
    ///   PlannerScheduleScenesV2Edge*.json     the edge scenes review found (2026-10-18), in the three zones
    /// and, kept out of the repo (about 14 MB of results each), v30 and HS2 v3_06u converted to the new names:
    ///   PLANNER_SCHEDULE_V2_REAL          PlannerScheduleRealV2.json (its Merge scenes, *-edits, are not the tab's)
    ///   PLANNER_SCHEDULE_V2_REAL_PLANNER  the Planner's results for it in one zone (UTC, Sydney or London)
    /// (skipped, and reported so, when they are not set). Folders are matched by the paths the harness writes (two folders
    /// may share a name), else by their names. A scene the Planner reads as the old names is checked to read so here too,
    /// and nothing more.
    /// </summary>
    public class PlannerScheduleV2ParityTests
    {
        private readonly ITestOutputHelper output;

        public PlannerScheduleV2ParityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static string Here([CallerFilePath] string path = null)
        {
            return Path.GetDirectoryName(path);
        }

        private static DateTime Day(string iso)
        {
            return DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string Iso(DateTime day)
        {
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // The layer node name a scene layer of the ID has, as the harness names its folder: its <ID>_<Name>.
        private static string ModelLabel(JArray nodes, string id)
        {
            foreach (JObject node in nodes)
            {
                if ((bool)node["mesh"])
                {
                    continue;
                }
                var p = PlannerNaming.ParseLayerNameV2((string)node["name"]);
                if (p != null && PlannerNaming.LayerIdText(p.Id) == id)
                {
                    return p.Label;
                }
            }
            return null;
        }

        /// <summary>
        /// The options the harness gives the Planner for a scene, as the tab gives them from a layer list: the project start,
        /// and a folder per ID of its layerDates and listOnly (named as the scene layer of the ID is, else as listOnly names
        /// it; TBC as the row says; a list-only row in its group's folder), list-only groups first, as the harness stores them.
        /// </summary>
        internal static PlannerScheduleOptions OptionsOf(JObject scene, TimeZoneInfo zone)
        {
            var options = new PlannerScheduleOptions
            {
                Zone = zone,
                Today = new DateTime(2026, 10, 15),
                ProjectStart = Day((string)scene["projectStart"])
            };
            var nodes = (JArray)scene["nodes"];
            var listOnly = (scene["listOnly"] as JArray ?? new JArray()).Cast<JObject>().ToList();
            var groups = new Dictionary<string, PlannerPlanFolder>(StringComparer.Ordinal);
            foreach (var row in listOnly)
            {
                var parent = (string)row["parent"];
                if (parent != null && !groups.ContainsKey(parent))
                {
                    var group = new PlannerPlanFolder { Name = parent };
                    groups[parent] = group;
                    options.Folders.Add(group);
                }
            }
            var layerDates = scene["layerDates"] as JObject ?? new JObject();
            foreach (var pair in layerDates.Properties())
            {
                var id = pair.Name;
                var dates = (JObject)pair.Value;
                var listed = listOnly.FirstOrDefault(l => (string)l["id"] == id);
                var folder = new PlannerPlanFolder
                {
                    Name = listed != null ? (string)listed["name"] : ModelLabel(nodes, id),
                    Tbc = (bool)dates["tbc"]
                };
                Assert.True(folder.Name != null, (string)scene["id"] + ": layerDates " + id + " names no layer node and no listOnly row");
                if (listed != null && listed["parent"] != null)
                {
                    folder.Parent = groups[(string)listed["parent"]];
                }
                if (dates["start"].Type == JTokenType.String && dates["finish"].Type == JTokenType.String)
                {
                    folder.Start = Day((string)dates["start"]);
                    folder.Finish = Day((string)dates["finish"]);
                }
                options.Folders.Add(folder);
            }
            foreach (var row in listOnly.Where(l => layerDates[(string)l["id"]] == null))
            {
                var folder = new PlannerPlanFolder { Name = (string)row["name"], Tbc = true };
                if (row["parent"] != null)
                {
                    folder.Parent = groups[(string)row["parent"]];
                }
                options.Folders.Add(folder);
            }
            return options;
        }

        internal sealed class Counts
        {
            public int Scenes;
            public int OldScenes;
            public int Activities;
            public int Objects;
            public int Folders;
            public int Instants;
            public int Checks;
            public int GanttBars;
            public int Lists;
        }

        // A folder's TBC as the Planner's exports read it: its own flag, or a folder's above it. Folders are found by their
        // paths (the harness writes each folder's, so two folders of one name tell apart), else by their names.
        private static bool FolderTbc(Dictionary<string, JObject> folders, string key, bool byPath)
        {
            for (int hops = 0; key != null && hops < 64; hops++)
            {
                JObject folder;
                if (!folders.TryGetValue(key, out folder))
                {
                    return false;
                }
                if ((bool)folder["tbc"])
                {
                    return true;
                }
                if (byPath)
                {
                    int cut = key.LastIndexOf(PathSeparator, StringComparison.Ordinal);
                    key = cut >= 0 ? key.Substring(0, cut) : null;
                }
                else
                {
                    key = (string)folder["parent"];
                }
            }
            return false;
        }

        // A folder's path, as the harness writes it: the names from the top folder down to it.
        private const string PathSeparator = " / ";

        private static string PathOf(PlannerScheduleFolder folder)
        {
            var names = new List<string>();
            for (int hops = 0; folder != null && hops < 64; folder = folder.Parent, hops++)
            {
                names.Insert(0, folder.Name);
            }
            return names.Count > 0 ? string.Join(PathSeparator, names) : null;
        }

        private static List<string> Strings(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? new List<string>() : token.Select(t => (string)t).ToList();
        }

        /// <summary>
        /// The Planner's untagged objects without the phasing roots: its results here were made before roots stopped being
        /// objects (R19), when it listed each root as an untagged object. A root is an empty node at the top whose name has
        /// "phasing" in it and reads as neither a phase group nor a layer (R10). Once the results are made again, this
        /// leaves them as they are.
        /// </summary>
        internal static List<string> WithoutRoots(IEnumerable<string> names, Dictionary<string, PlannerModelNode> index)
        {
            return names.Where(n =>
            {
                PlannerModelNode node;
                return !(index.TryGetValue(n, out node) && node.Parent == null && !node.Mesh && n.IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0
                    && !PlannerSceneModel.ReadsAsPhasingName(n));
            }).ToList();
        }

        /// <summary>Runs the tab's schedule on a scene and lists every way it differs from the Planner's result for it.</summary>
        internal static List<string> Compare(JObject scene, JObject planner, Counts counts, out PlannerSchedule schedule)
        {
            var differences = new List<string>();
            var id = (string)planner["id"];
            Action<string> differ = text => differences.Add(id + ": " + text);
            var options = OptionsOf(scene, PlannerSceneFiles.ZoneOf((string)planner["zone"]));
            var model = new PlannerScheduleModel(PlannerSceneFiles.SnapshotOf((JArray)scene["nodes"]));
            var naming = PlannerSchedule.NamingOfScene(model);
            var built = PlannerSchedule.BuildV2(model, options, naming);
            schedule = built;

            // The scene's naming, and what decided it.
            var mine = naming != null && naming.Naming == PlannerNamingKind.New ? "new" : "old";
            if (mine != (string)planner["naming"])
            {
                differ("naming " + mine + " against " + planner["naming"]);
            }
            var from = naming == null ? null : naming.From == PlannerNamingSource.Objects ? "objects" : "layers";
            if (from != (string)planner["namingFrom"])
            {
                differ("naming read from " + (from ?? "nothing") + " against " + ((string)planner["namingFrom"] ?? "nothing"));
            }
            if (planner["v2"] == null || planner["v2"].Type == JTokenType.Null)
            {
                // The Planner generates it on the old names: the Schedule tab does not preview such a scene, it asks for Clean up
                // scene graph first (D23), so it must read as the old names here too.
                counts.OldScenes++;
                if (naming == null || naming.Naming != PlannerNamingKind.Old)
                {
                    differ("the tab would preview it on the new names, the Planner generates it on the old");
                }
                return differences;
            }
            counts.Scenes++;
            if (built.Naming != PlannerNamingKind.New)
            {
                differ("built on the old names, the Planner on the new");
                return differences;
            }
            if (!built.Usable)
            {
                differ("the schedule says: " + built.Message);
                return differences;
            }
            if (Iso(built.ProjectStart) != (string)planner["projectStart"])
            {
                differ("project start " + Iso(built.ProjectStart) + " against " + planner["projectStart"]);
            }
            // Every activity is filed in a layer's folder, which dates it: the tab's rows have no place for work no layer
            // dates (the old names' engine had one; it is gone).
            foreach (var a in built.Activities.Where(a => a.DatedBy == null || a.DatedBy.Id == null))
            {
                differ(a.Name + " is filed in no layer's folder");
            }

            // Activities, by the object each is made for and its type. Folders by their paths when the results give them (two
            // folders may share a name), else by their names.
            bool byPath = planner["folders"].Cast<JObject>().All(f => f["path"] != null && f["path"].Type == JTokenType.String);
            Func<JObject, string> folderKey = f => byPath ? (string)f["path"] : (string)f["name"];
            var folderByKey = planner["folders"].Cast<JObject>().GroupBy(folderKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Func<PlannerScheduleActivity, string> keyOf = a => a.Object + "|" + (a.Type == PlannerTaskType.Install ? "Install" : "Dismantle");
            var mineByKey = built.Activities.GroupBy(keyOf, StringComparer.Ordinal).ToDictionary(g => g.Key, g => new Queue<PlannerScheduleActivity>(g), StringComparer.Ordinal);
            var matched = new HashSet<PlannerScheduleActivity>();
            foreach (JObject a in planner["activities"])
            {
                var key = (string)a["object"] + "|" + (string)a["type"];
                var name = (string)a["name"];
                Queue<PlannerScheduleActivity> queue;
                if (!mineByKey.TryGetValue(key, out queue) || queue.Count == 0)
                {
                    differ("missing activity " + name + " for " + key);
                    continue;
                }
                var activity = queue.Dequeue();
                matched.Add(activity);
                counts.Activities++;
                var label = name + " (" + key + ")";
                if (activity.Name != name) differ(label + ": named " + activity.Name);
                var folder = activity.Home == null ? null : byPath ? PathOf(activity.Home.Folder) : activity.Home.Label;
                var theirFolder = byPath ? (string)a["folderPath"] : (string)a["folder"];
                if (folder != theirFolder) differ(label + ": folder " + folder + " against " + theirFolder);
                if (activity.Tbc != (bool)a["tbc"]) differ(label + ": TBC " + activity.Tbc + " against " + a["tbc"]);
                if (activity.StartMs != (long)a["startMs"]) differ(label + ": start " + Show(built, activity.StartMs) + " against " + Show(built, (long)a["startMs"]));
                if (activity.EndMs != (long)a["endMs"]) differ(label + ": finish " + Show(built, activity.EndMs) + " against " + Show(built, (long)a["endMs"]));
                var members = Strings(a["members"]);
                if (!members.SequenceEqual(activity.Members)) differ(label + ": binds " + string.Join(",", activity.Members) + " against " + string.Join(",", members));
                var layer = activity.EventLayer != null ? PlannerNaming.LayerIdText(activity.EventLayer) : null;
                if (layer != (string)a["layer"]) differ(label + ": event layer " + layer + " against " + a["layer"]);
                var after = activity.After != null ? activity.After.Name : null;
                if (after != (string)a["after"]) differ(label + ": follows " + (after ?? "nothing") + " against " + ((string)a["after"] ?? "nothing"));
                if (a["startDate"] != null)
                {
                    counts.GanttBars++;
                    var work = PlannerGanttModel.WorkOf(built, activity);
                    var days = Iso(work.FirstDay) + " to " + Iso(work.LastDay);
                    var theirDays = (string)a["startDate"] + " to " + (string)a["endDate"];
                    if (days != theirDays) differ(label + ": Gantt days " + days + " against " + theirDays);
                    bool hatched = (bool)a["tbc"] || FolderTbc(folderByKey, theirFolder, byPath);
                    if (work.Tbc != hatched) differ(label + ": Gantt TBC " + work.Tbc + " against " + hatched);
                }
            }
            foreach (var activity in built.Activities.Where(x => !matched.Contains(x)))
            {
                differ("extra activity " + activity.Name + " for " + keyOf(activity));
            }
            counts.Objects += built.Objects.Count;

            // Folders: where each sits (its path, else its name and its parent's), its TBC and its parked day, one for one.
            Func<string, string, bool, string, string> folderText = (name, parent, tbc, parked) =>
                (byPath ? name : name + " in " + (parent ?? "(top)")) + (tbc ? ", TBC" : string.Empty) + (parked != null ? ", parked on " + parked : string.Empty);
            var theirFolders = planner["folders"].Cast<JObject>()
                .Select(f => folderText(folderKey(f), (string)f["parent"], (bool)f["tbc"], f["parkedOn"] != null ? string.Join(",", Strings(f["parkedOn"])) : null))
                .OrderBy(t => t, StringComparer.Ordinal).ToList();
            var myFolders = built.Folders.Select(f => folderText(byPath ? PathOf(f) : f.Name, f.ParentName, f.Tbc, f.ParkedOn.HasValue ? Iso(f.ParkedOn.Value) : null))
                .OrderBy(t => t, StringComparer.Ordinal).ToList();
            counts.Folders += theirFolders.Count;
            foreach (var text in theirFolders.Except(myFolders))
            {
                differ("folder " + text + " is missing");
            }
            foreach (var text in myFolders.Except(theirFolders))
            {
                differ("folder " + text + " is extra");
            }

            // The report's lists.
            var v2 = (JObject)planner["v2"];
            var report = built.V2;
            Action<string, IEnumerable<string>, IEnumerable<string>> list = (what, ours, theirs) =>
            {
                counts.Lists++;
                var a = ours.ToList();
                var b = theirs.ToList();
                if (!a.SequenceEqual(b))
                {
                    differ(what + " " + string.Join(" | ", a) + " against " + string.Join(" | ", b));
                }
            };
            list("matched", report.Matched, Strings(planner["matched"]));
            list("skipped", report.Skipped, WithoutRoots(Strings(planner["skipped"]), built.Index));
            list("unfiled", report.Unfiled, Strings(planner["unfiled"]));
            list("layer issues", built.LayerIssues.OrderBy(t => t, StringComparer.Ordinal), Strings(planner["layerIssues"]).OrderBy(t => t, StringComparer.Ordinal));
            list("TBC activities", report.TbcActivities, Strings(planner["tbcActivities"]));
            list("duplicate names", report.DuplicateNames, Strings(planner["duplicateNames"]));
            list("parked layers", report.ParkedLayers.Select(p => p.Folder + " on " + Iso(p.Day)), v2["parkedLayers"].Select(p => (string)p["folder"] + " on " + (string)p["day"]));
            list("missing layers", report.MissingLayers.Select(m => m.ActivityName + " for " + m.Layer), v2["missingLayers"].Select(m => (string)m["activityName"] + " for " + (string)m["layer"]));
            list("naming issues", report.NamingIssues.Select(i => PlannerNaming.CodeText(i.Code) + ": " + string.Join(",", i.Names)),
                v2["namingIssues"].Select(i => (string)i["code"] + ": " + string.Join(",", Strings(i["names"]))));
            list("other naming", report.OtherNaming, Strings(v2["otherNaming"]));
            list("folders without a layer", report.FoldersWithoutLayer, Strings(v2["foldersWithoutLayer"]));
            list("ID differs", new string[0], v2["idDiffers"].Select(d => (string)d["folder"] + " for " + (string)d["layer"]));
            list("renamed layers", report.RenamedLayers.Select(r => r.Folder + " for " + r.Layer), v2["renamedLayers"].Select(d => (string)d["folder"] + " for " + (string)d["layer"]));
            list("shared IDs", report.SharedIds.Select(s => s.Id + " " + (s.Folders ? "folders" : "layers") + ": " + string.Join(",", s.Names)),
                v2["sharedIds"].Select(s => (string)s["id"] + " " + (string)s["kind"] + ": " + string.Join(",", Strings(s["names"]))));
            list("numbered bindings", report.NumberedBindings, Strings(v2["numberedBindings"]));
            list("wrong order", report.WrongOrder, Strings(v2["wrongOrder"]));

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

        private static string Describe(string what, Counts counts, int differences)
        {
            return what + ": " + counts.Scenes + " new-name scenes (" + counts.OldScenes + " read as the old names), " + counts.Activities + " activities, "
                + counts.Objects + " objects, " + counts.Folders + " folders, " + counts.Lists + " lists, " + counts.Instants + " instants ("
                + counts.Checks + " object checks), " + counts.GanttBars + " Gantt bars; " + differences + " differences";
        }

        private void RunGolden(string file, string scenesFile = "PlannerScheduleScenesV2.json")
        {
            var scenes = JArray.Parse(File.ReadAllText(Path.Combine(Here(), scenesFile)));
            var results = JArray.Parse(File.ReadAllText(Path.Combine(Here(), file)));
            Assert.Equal(scenes.Count, results.Count);
            var counts = new Counts();
            var differences = new List<string>();
            foreach (JObject result in results)
            {
                var scene = scenes.Cast<JObject>().Single(s => (string)s["id"] == (string)result["id"]);
                PlannerSchedule schedule;
                differences.AddRange(Compare(scene, result, counts, out schedule));
            }
            output.WriteLine(Describe(file, counts, differences.Count));
            Assert.True(differences.Count == 0, string.Join("\n", differences.Take(60)));
        }

        [Fact]
        public void SyntheticScenesMatchThePlannerInUtc()
        {
            RunGolden("PlannerScheduleScenesV2.utc.json");
        }

        [Fact]
        public void SyntheticScenesMatchThePlannerInSydneyTime()
        {
            RunGolden("PlannerScheduleScenesV2.sydney.json");
        }

        [Fact]
        public void SyntheticScenesMatchThePlannerInLondonTime()
        {
            RunGolden("PlannerScheduleScenesV2.london.json");
        }

        // The edge scenes review found (PlannerScheduleScenesV2Edge.json): own names the Planner's sort ignores or folds parts
        // of, activity names that compare equal met A B B A, two folders of one name, two folders sharing an ID.

        [Fact]
        public void EdgeScenesMatchThePlannerInUtc()
        {
            RunGolden("PlannerScheduleScenesV2Edge.utc.json", "PlannerScheduleScenesV2Edge.json");
        }

        [Fact]
        public void EdgeScenesMatchThePlannerInSydneyTime()
        {
            RunGolden("PlannerScheduleScenesV2Edge.sydney.json", "PlannerScheduleScenesV2Edge.json");
        }

        [Fact]
        public void EdgeScenesMatchThePlannerInLondonTime()
        {
            RunGolden("PlannerScheduleScenesV2Edge.london.json", "PlannerScheduleScenesV2Edge.json");
        }

        [EnvironmentFact("PLANNER_SCHEDULE_V2_REAL", "PLANNER_SCHEDULE_V2_REAL_PLANNER")]
        public void ConvertedV30AndHs2MatchThePlanner()
        {
            var scenesPath = EnvironmentFactAttribute.InputFile("PLANNER_SCHEDULE_V2_REAL");
            var plannerPath = EnvironmentFactAttribute.InputFile("PLANNER_SCHEDULE_V2_REAL_PLANNER");
            var scenes = JArray.Parse(File.ReadAllText(scenesPath)).Cast<JObject>().ToList();
            var differences = new List<string>();
            var lines = new List<string>();
            foreach (JObject result in JArray.Parse(File.ReadAllText(plannerPath)))
            {
                var scene = scenes.Single(s => (string)s["id"] == (string)result["id"]);
                if ((string)scene["kind"] == "merge")
                {
                    // What the Planner does on the next Generate after model changes: not the tab's preview (spec 5.6).
                    continue;
                }
                var counts = new Counts();
                PlannerSchedule schedule;
                var found = Compare(scene, result, counts, out schedule);
                differences.AddRange(found);
                lines.Add(Describe((string)result["id"] + " (" + result["zone"] + ")", counts, found.Count));
                if (schedule.V2 != null)
                {
                    lines.Add("  layers " + schedule.Layers.Count + ", parked " + schedule.V2.ParkedLayers.Count + ", IN " + schedule.Activities.Count(a => a.Type == PlannerTaskType.Install)
                        + ", RM " + schedule.Activities.Count(a => a.Type == PlannerTaskType.Dismantle) + ", references to layers nobody has " + schedule.V2.MissingLayers.Count
                        + ", not scheduled " + schedule.NotScheduled.Count + ", works end " + (schedule.WorksEnd.HasValue ? Iso(schedule.WorksEnd.Value) : "-"));
                }
                lines.AddRange(found.Take(20));
            }
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }
            var report = Environment.GetEnvironmentVariable("PLANNER_LAYERS_REPORT");
            if (!string.IsNullOrEmpty(report))
            {
                File.AppendAllText(report, "Naming v2 parity:\r\n" + string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            }
            Assert.True(differences.Count == 0, string.Join("\n", differences.Take(60)));
        }

        [Fact]
        public void OldNameScenesReadAsTheOldNames()
        {
            // No old-name parity scene reads as the new names: one that reads as the old names is the scene the Schedule tab
            // asks to clean up first (D23), and one with nothing that tells is read as neither. PlannerScheduleScenes.json holds
            // the synthetic scenes the old names' engine was checked on against the Planner (removed then installed, strays,
            // TBC, one-number layers, sub-stages, splits, copies, clock changes, objects taken away and put back in another
            // stage's layer, ...); that engine is gone, and its scenes stay as the naming reader's old-name cases.
            var scenes = JArray.Parse(File.ReadAllText(Path.Combine(Here(), "PlannerScheduleScenes.json")));
            var wrong = new List<string>();
            int old = 0;
            foreach (JObject scene in scenes)
            {
                var model = new PlannerScheduleModel(PlannerSceneFiles.SnapshotOf((JArray)scene["nodes"]));
                var naming = PlannerSchedule.NamingOfScene(model);
                if (naming != null && naming.Naming == PlannerNamingKind.New)
                {
                    wrong.Add((string)scene["id"]);
                }
                else if (naming != null)
                {
                    old++;
                }
            }
            Assert.True(wrong.Count == 0, "read as the new names: " + string.Join(", ", wrong));
            Assert.True(old > 0, "no scene reads as the old names");
            output.WriteLine(old + " of " + scenes.Count + " old-name parity scenes read as the old names; the rest have nothing that tells.");
            var treePath = Environment.GetEnvironmentVariable("PLANNER_SCHEDULE_HS2");
            if (!string.IsNullOrEmpty(treePath) && File.Exists(treePath))
            {
                var nodes = PlannerSceneFiles.NodesOfTree(JObject.Parse(File.ReadAllText(treePath)));
                var naming = PlannerSchedule.NamingOfScene(new PlannerScheduleModel(PlannerSceneFiles.SnapshotOf(nodes)));
                Assert.NotNull(naming);
                Assert.Equal(PlannerNamingKind.Old, naming.Naming);
                output.WriteLine("HS2 v3_06u reads as the old names, from its " + naming.From + ".");
            }
        }
    }
}
