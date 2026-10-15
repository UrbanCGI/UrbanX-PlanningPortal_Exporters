using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// Build layers and helpers against a real model, kept out of the repo: the node tree of the HS2 export after its
    /// clean-up (v3_06), in the underscore form the convention writes now ("v3_06u", as Fix phasing names would write it).
    ///
    ///   PLANNER_FIX_V306       = the node tree, { "roots": [i...], "nodes": [ { "name", "children": [i...], "mesh" } ] }
    ///   PLANNER_LAYERS_CSV     = the Planner's "Layer list for 3ds Max (CSV)" for that model (optional; without it a list
    ///                            written from the tree itself stands in)
    ///   PLANNER_LAYERS_REPORT  = where to write what the runs found, to read (optional)
    ///
    /// (a) On an empty scene the plan builds exactly the v3_06u phasing tree (layers and helpers, same names, same
    /// nesting) under a new Work_Phasing root. (b) On the v3_06u scene (its layers and same-named helpers, as the GLB
    /// has them) every row is up to date: found by ID or name, its helper only gets the Planner ID. (c) After a re-date,
    /// a rename and a move in the Planner and a row removed, the plan renames and moves exactly those and lists the
    /// removed one, deleting nothing.
    ///
    /// With the Planner's own list, (a) builds exactly what the list says and reports where that differs from the model,
    /// and (b) allows renames (a name the Planner writes otherwise, reported) but no new, moved or problem row; those
    /// differences are the Planner's to explain. With a list written from the model, both are exact.
    /// </summary>
    public class PlannerLayerAcceptanceTests
    {
        private readonly ITestOutputHelper output;

        public PlannerLayerAcceptanceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private sealed class Model
        {
            public string RootName;
            /// <summary>Groups and layers, parents first: name, its parent's name (null under the root), group or layer.</summary>
            public readonly List<Tuple<string, string, bool>> Containers = new List<Tuple<string, string, bool>>();
            public PlannerSceneSnapshot Scene;
            /// <summary>"name &lt; parent" for every group and layer, ROOT for the root.</summary>
            public List<string> Tree;
        }

        private static Model Load(string path)
        {
            var json = JObject.Parse(File.ReadAllText(path));
            var names = json["nodes"].Select(n => PhasingNameFixerAcceptanceTests.Underscore(PhasingNameFixerAcceptanceTests.StripDuplicateSuffix((string)n["name"]))).ToList();
            var children = json["nodes"].Select(n => n["children"].Select(c => (int)c).ToList()).ToList();
            var mesh = json["nodes"].Select(n => n["mesh"] == null || (bool)n["mesh"]).ToList();
            var roots = json["roots"].Select(r => (int)r).ToList();
            var root = roots.First(r => names[r].IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0);

            var model = new Model { RootName = names[root] };
            var scene = new TestScene();
            scene.Layer("0");
            var rootNode = scene.Unit(model.RootName, null, null);
            var keyOf = new Dictionary<int, PlannerSceneNode> { { root, rootNode } };
            // Groups under the root, layers under the groups: helpers on Max layers of their names; everything else
            // (objects) on its container's layer.
            Action<int, int, string, int> walk = null;
            walk = (index, parent, layer, depth) =>
            {
                foreach (var child in children[index])
                {
                    var name = names[child];
                    bool container = depth < 2 && !mesh[child] && (depth == 0 ? Regex.IsMatch(name, "^_[0-9]+_") : PlannerNaming.ParseLayerName(name) != null);
                    PlannerSceneNode node;
                    if (container)
                    {
                        node = scene.Unit(name, layer, keyOf[index]);
                        model.Containers.Add(Tuple.Create(name, depth == 0 ? null : layer, depth == 1));
                        keyOf[child] = node;
                        walk(child, child, name, depth + 1);
                    }
                    else
                    {
                        node = scene.Node(name, keyOf[index], layer, !mesh[child]);
                        keyOf[child] = node;
                        walk(child, child, layer, 99);
                    }
                }
            };
            walk(root, root, model.RootName, 0);
            foreach (var other in roots.Where(r => r != root))
            {
                keyOf[other] = scene.Node(names[other], null, "0", !mesh[other]);
                walk(other, other, "0", 99);
            }
            model.Scene = scene.Snapshot;
            model.Tree = model.Containers.Select(c => c.Item1 + " < " + (c.Item2 ?? "ROOT")).OrderBy(t => t, StringComparer.Ordinal).ToList();
            return model;
        }

        // The list the Planner would export for the model: its groups and layers with made-up Planner IDs.
        private static string ListFromModel(Model model)
        {
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            var rows = new List<string[]>();
            foreach (var container in model.Containers)
            {
                var id = "f" + (ids.Count + 1).ToString();
                ids[container.Item1] = id;
                rows.Add(TestCsv.Row(id, container.Item2 != null ? ids[container.Item2] : null, container.Item1, container.Item3 ? "Layer" : "Group"));
            }
            return TestCsv.Write(rows);
        }

        private bool TryLoad(out Model model, out string csv, out bool fromPlanner)
        {
            model = null;
            csv = null;
            fromPlanner = false;
            var path = Environment.GetEnvironmentVariable("PLANNER_FIX_V306");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                output.WriteLine("PLANNER_FIX_V306 is not set; the acceptance run is skipped.");
                return false;
            }
            model = Load(path);
            var csvPath = Environment.GetEnvironmentVariable("PLANNER_LAYERS_CSV");
            fromPlanner = !string.IsNullOrEmpty(csvPath) && File.Exists(csvPath);
            csv = fromPlanner ? PlannerLayerList.Decode(File.ReadAllBytes(csvPath), out _) : ListFromModel(model);
            return true;
        }

        private static void Report(string title, IEnumerable<string> lines)
        {
            var path = Environment.GetEnvironmentVariable("PLANNER_LAYERS_REPORT");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            var text = new StringBuilder();
            text.Append("== ").Append(title).Append("\r\n");
            foreach (var line in lines)
            {
                text.Append(line).Append("\r\n");
            }
            File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        [Fact]
        public void A_AnEmptySceneGetsExactlyTheModelsPhasingTree()
        {
            Model model;
            string csv;
            bool fromPlanner;
            if (!TryLoad(out model, out csv, out fromPlanner))
            {
                return;
            }
            var list = PlannerLayerList.Parse(csv);
            Assert.True(list.Usable, string.Join(" ", list.Errors));
            var empty = new PlannerSceneSnapshot();
            empty.Layers.Add(new PhasingMaxLayer { Name = "0" });
            var plan = PlannerLayerPlanner.Plan(list, empty);
            var after = TestSimulator.Apply(empty, plan);
            var layers = TestSimulator.LayerTree(after, PlannerSceneModel.DefaultRootName);
            var rootHelper = after.Nodes.Single(n => n.Name == PlannerSceneModel.DefaultRootName);
            var helpers = TestSimulator.HelperTree(after, rootHelper.Key, n => true);

            var missing = model.Tree.Except(layers).ToList();
            var extra = layers.Except(model.Tree).ToList();
            var lines = new List<string>
            {
                (fromPlanner ? "The Planner's list" : "A list written from the model") + ": " + list.Describe(),
                "Plan on an empty scene: " + plan.Summary(),
                "Model groups and layers: " + model.Tree.Count + "; built layers: " + layers.Count + "; built helpers: " + helpers.Count,
                "In the model, not built (" + missing.Count + "):"
            };
            lines.AddRange(missing.Select(m => "  " + m));
            lines.Add("Built, not in the model (" + extra.Count + "):");
            lines.AddRange(extra.Select(e => "  " + e));
            lines.AddRange(plan.Rows.Where(r => r.State == PlannerRowState.Problem).Select(r => "  Problem: " + r.Row.Layer + ": " + r.Problem));
            Report("(a) empty scene", lines);
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(list.Rows.Count, plan.NewCount);
            // Exactly what the list says, layers and helpers alike.
            var listed = list.Rows.Select(r => r.Layer + " < " + (r.Parent != null ? r.Parent.Layer : "ROOT")).OrderBy(t => t, StringComparer.Ordinal).ToList();
            Assert.Equal(listed, layers);
            Assert.Equal(layers, helpers);
            Assert.All(after.Nodes.Where(n => n != rootHelper), h => Assert.Equal(h.Name, h.LayerName));
            Assert.False(PlannerLayerPlanner.Plan(list, after).HasChanges);
            // The export log's counts pair up.
            var counts = PlannerLayerPlannerTests.CountsOf(after);
            output.WriteLine(counts.Describe(false));
            Assert.Null(counts.PairingNote());
            Assert.Equal(list.Rows.Count, counts.Layers);
            Assert.Equal(list.Rows.Count, counts.Helpers);
            if (!fromPlanner)
            {
                // A list written from the model gives the model's tree; the Planner's own list is compared above and its
                // differences from the model reported, as they are the Planner's to explain.
                Assert.Empty(missing);
                Assert.Empty(extra);
            }
        }

        [Fact]
        public void B_TheModelsOwnSceneIsUpToDate()
        {
            Model model;
            string csv;
            bool fromPlanner;
            if (!TryLoad(out model, out csv, out fromPlanner))
            {
                return;
            }
            var list = PlannerLayerList.Parse(csv);
            var plan = PlannerLayerPlanner.Plan(list, model.Scene);
            var lines = new List<string>
            {
                "Plan on the v3_06u scene: " + plan.Summary(),
                "Root: " + plan.RootName + "; found by layer ID " + plan.Rows.Count(r => r.MatchedBy == PlannerMatchKind.LayerId)
                    + ", by name " + plan.Rows.Count(r => r.MatchedBy == PlannerMatchKind.Name) + "; Planner IDs to stamp " + plan.AdoptCount
            };
            lines.AddRange(plan.Rows.Where(r => r.State != PlannerRowState.UpToDate).Select(r => "  " + r.Row.Layer + ": " + r.StatusText));
            lines.AddRange(plan.NotInList.Select(n => "  Not in the list: " + n.Name + " (" + n.What + ")"));
            Report("(b) v3_06u scene", lines);
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            Assert.Equal(model.RootName, plan.RootName);
            Assert.Equal(0, plan.NewCount);
            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(0, plan.MoveCount);
            Assert.All(plan.BuildLayers, l => Assert.False(l.Create || l.Move));
            Assert.All(plan.BuildHelpers, h => Assert.False(h.Create || h.Move || h.PutOnLayer));
            if (!fromPlanner)
            {
                // A list written from the model has the model's names: nothing is renamed either. The Planner's own list
                // may write a name otherwise (reported above, the Planner's to explain): those rows are renamed.
                Assert.Equal(0, plan.ChangeCount);
                Assert.Equal(list.Rows.Count, plan.UpToDateCount);
                Assert.Equal(list.Rows.Count, plan.AdoptCount);
                Assert.Empty(plan.NotInList);
            }
            else
            {
                Assert.Equal(plan.ChangeCount, plan.RenameCount);
            }

            // The export log's counts pair up: every phasing layer has its helper.
            var linked = PlannerLinkPlanner.Plan(model.Scene);
            Assert.Equal(0, linked.ObjectsToLink);
            Assert.Equal(0, linked.ProblemCount);
        }

        [Fact]
        public void C_ChangesInThePlannerAreFollowedAndNothingIsDeleted()
        {
            Model model;
            string csv;
            bool fromPlanner;
            if (!TryLoad(out model, out csv, out fromPlanner))
            {
                return;
            }
            var list = PlannerLayerList.Parse(csv);
            var built = TestSimulator.Apply(model.Scene, PlannerLayerPlanner.Plan(list, model.Scene));
            // What the scene has that the list never had (the Planner's own list may leave out a layer with no work).
            var before = PlannerLayerPlanner.Plan(list, built).NotInList.Select(n => n.Name).ToList();

            // Four layers in the Planner: one re-dated, one renamed, one moved to another group, one removed.
            var layerRows = list.Rows.Where(r => r.Kind == PlannerLayerKind.Layer && r.Parent != null).ToList();
            var dated = new Regex("[0-9]{2}-[0-9]{2}-[0-9]{2}\\z");
            var redate = layerRows.First(r => dated.IsMatch(r.Layer));
            var rename = layerRows.First(r => r != redate && r.IdKey != null);
            var move = layerRows.First(r => r != redate && r != rename && list.Rows.Any(g => g.Kind == PlannerLayerKind.Group && g != r.Parent && g.Parent == null));
            var newGroup = list.Rows.First(g => g.Kind == PlannerLayerKind.Group && g != move.Parent && g.Parent == null);
            var remove = layerRows.Last(r => r != redate && r != rename && r != move && r.Children.Count == 0);
            var redated = dated.Replace(redate.Layer, "28-02-29");
            var renamed = rename.IdKey + "_Renamed_in_the_Planner" + rename.Layer.Substring(rename.IdKey.ToString().Length);

            var rows = new List<string[]>();
            foreach (var row in list.Outline().Where(r => r != remove))
            {
                var cells = Cells(row);
                if (row == redate) cells[2] = redated;
                if (row == rename) cells[2] = renamed;
                if (row == move) cells[1] = newGroup.PlannerId;
                rows.Add(cells);
            }
            var changed = TestCsv.List(rows.ToArray());
            var plan = PlannerLayerPlanner.Plan(changed, built);
            var lines = new List<string>
            {
                "Re-dated " + redate.Layer + " to " + redated + "; renamed " + rename.Layer + " to " + renamed + "; moved " + move.Layer + " to " + newGroup.Layer + "; removed " + remove.Layer,
                "Plan: " + plan.Summary()
            };
            lines.AddRange(plan.Rows.Where(r => r.State != PlannerRowState.UpToDate).Select(r => "  " + r.Row.Layer + ": " + r.StatusText));
            lines.AddRange(plan.NotInList.Select(n => "  Not in the list: " + n.Name + " (" + n.What + "; " + n.Note + ")"));
            Report("(c) changes in the Planner", lines);
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            Assert.Equal(0, plan.ProblemCount);
            Assert.Equal(0, plan.NewCount);
            Assert.Equal(3, plan.ChangeCount);
            Assert.Equal("Rename from " + redate.Layer, plan.Rows.Single(r => r.Row.PlannerId == redate.PlannerId).StatusText);
            Assert.Equal("Rename from " + rename.Layer, plan.Rows.Single(r => r.Row.PlannerId == rename.PlannerId).StatusText);
            Assert.Equal("Move under " + newGroup.Layer, plan.Rows.Single(r => r.Row.PlannerId == move.PlannerId).StatusText);
            Assert.Equal(before.Concat(new[] { remove.Layer }).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                plan.NotInList.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Contains("Planner ID " + remove.PlannerId, plan.NotInList.Single(n => n.Name == remove.Layer).Note);

            var after = TestSimulator.Apply(built, plan);
            Assert.Equal(built.Layers.Count, after.Layers.Count);
            Assert.Equal(built.Nodes.Count, after.Nodes.Count);
            Assert.False(PlannerLayerPlanner.Plan(changed, after).HasChanges);
        }

        private static string[] Cells(PlannerLayerRow row)
        {
            return new[]
            {
                row.PlannerId, row.ParentId, row.Layer, row.KindText, row.Id, row.Phase, row.Stage, row.SubStage, row.Name,
                row.Start, row.Finish, row.TbcText, row.OrderText
            };
        }
    }
}
