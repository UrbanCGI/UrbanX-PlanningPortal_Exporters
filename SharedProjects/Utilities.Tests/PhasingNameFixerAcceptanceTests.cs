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
    /// The phasing name fixer against a real model, kept out of the repo: the node tree of the modellers' export,
    /// the node tree after the clean-up that was done on the exported file, and that clean-up's rename list.
    ///
    ///   PLANNER_FIX_V305    = node tree before the clean-up, { "roots": [i...], "nodes": [ { "name", "children": [i...] } ] }
    ///   PLANNER_FIX_V306    = node tree after the clean-up (same nodes, same order)
    ///   PLANNER_FIX_REF_CSV = the clean-up's rename list (optional)
    ///   PLANNER_FIX_OUT_CSV = where to write the fixer's own list, to compare by eye (optional)
    ///
    /// The review edits the clean-up made by hand are given as stage overrides: in phase 2, the Eastside Kerbing
    /// layers are stage 7 and the Westside Kerbing layers stage 8.
    /// </summary>
    public class PhasingNameFixerAcceptanceTests
    {
        // The GLB exporter's suffix on a repeated name; in 3ds Max those objects carry the same name.
        private static readonly Regex DuplicateSuffix = new Regex(@"\s*\([0-9]+\)\s*\z", RegexOptions.CultureInvariant);
        private readonly ITestOutputHelper output;

        public PhasingNameFixerAcceptanceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private sealed class Tree
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<List<int>> Children = new List<List<int>>();
            public List<int> Roots = new List<int>();

            public static Tree Load(string path)
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var tree = new Tree { Roots = json["roots"].Select(r => (int)r).ToList() };
                foreach (var node in json["nodes"])
                {
                    tree.Names.Add(DuplicateSuffix.Replace((string)node["name"] ?? string.Empty, string.Empty));
                    tree.Children.Add(node["children"].Select(c => (int)c).ToList());
                }
                return tree;
            }

            public int? ParentOf(int index)
            {
                for (int i = 0; i < Children.Count; i++)
                {
                    if (Children[i].Contains(index)) return i;
                }
                return null;
            }

            public List<PhasingSceneNode> SceneNodes()
            {
                var nodes = Names.Select((name, i) => new PhasingSceneNode { Key = i, Name = name, ChildIndex = Roots.Count + i }).ToList();
                for (int i = 0; i < Roots.Count; i++)
                {
                    nodes[Roots[i]].ChildIndex = i;
                }
                for (int parent = 0; parent < Children.Count; parent++)
                {
                    for (int i = 0; i < Children[parent].Count; i++)
                    {
                        nodes[Children[parent][i]].ParentKey = parent;
                        nodes[Children[parent][i]].ChildIndex = i;
                    }
                    nodes[parent].HasChildren = Children[parent].Count > 0;
                }
                return nodes;
            }

            /// <summary>The tree after a plan: renamed by key, moved nodes appended to their new parent's children.</summary>
            public Tree Applied(PhasingFixPlan plan)
            {
                var result = new Tree { Roots = Roots.ToList() };
                result.Names.AddRange(Names);
                result.Children.AddRange(Children.Select(c => c.ToList()));
                foreach (var rename in plan.NodeRenames)
                {
                    result.Names[(int)rename.Key] = rename.NewName;
                }
                foreach (var move in plan.NodeMoves)
                {
                    result.Children[(int)move.FromParentKey].Remove((int)move.Key);
                    result.Children[(int)move.ToParentKey].Add((int)move.Key);
                }
                return result;
            }

            /// <summary>The top node and everything under it down to the objects (the layers' children).</summary>
            public HashSet<int> PhasingTree(int top)
            {
                var set = new HashSet<int> { top };
                foreach (var groupOrLayer in Children[top])
                {
                    set.Add(groupOrLayer);
                    foreach (var layerOrObject in Children[groupOrLayer])
                    {
                        set.Add(layerOrObject);
                        set.UnionWith(Children[layerOrObject]);
                    }
                }
                return set;
            }
        }

        // What the review does: plan once, set the kerbing stages on the layer rows, plan again.
        private static Dictionary<long, int> KerbingOverrides(List<PhasingSceneNode> nodes)
        {
            var first = PhasingNameFixer.Plan(nodes, null, PhasingNameFixer.DefaultWordFixes(), null);
            var overrides = new Dictionary<long, int>();
            foreach (var layer in first.Layers.Where(l => l.Phase == 2))
            {
                if (layer.Description.StartsWith("Eastside_Kerbing", StringComparison.Ordinal)) overrides[layer.Key] = 7;
                if (layer.Description.StartsWith("Westside_Kerbing", StringComparison.Ordinal)) overrides[layer.Key] = 8;
            }
            return overrides;
        }

        private static List<string[]> ReadCsv(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8).TrimStart((char)0xFEFF);
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row.ToArray()); row = new List<string>(); }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }

        [Fact]
        public void ReferenceModelComesOutAsTheCleanedFile()
        {
            var beforePath = Environment.GetEnvironmentVariable("PLANNER_FIX_V305");
            var afterPath = Environment.GetEnvironmentVariable("PLANNER_FIX_V306");
            var csvPath = Environment.GetEnvironmentVariable("PLANNER_FIX_REF_CSV");
            if (string.IsNullOrEmpty(beforePath) || string.IsNullOrEmpty(afterPath) || !File.Exists(beforePath) || !File.Exists(afterPath))
            {
                output.WriteLine("PLANNER_FIX_V305 / PLANNER_FIX_V306 not set; skipped.");
                return;
            }
            var before = Tree.Load(beforePath);
            var after = Tree.Load(afterPath);
            Assert.Equal(before.Names.Count, after.Names.Count);

            var nodes = before.SceneNodes();
            var overrides = KerbingOverrides(nodes);
            var plan = PhasingNameFixer.Plan(nodes, null, PhasingNameFixer.DefaultWordFixes(), overrides);
            output.WriteLine(plan.Summary());
            output.WriteLine("Stage overrides: " + overrides.Count);
            Assert.NotNull(plan.TopKey);
            var top = (int)plan.TopKey.Value;
            var phasing = before.PhasingTree(top);

            // Every node comes out with its cleaned name and place; nothing outside the phasing tree is touched.
            var fixedTree = before.Applied(plan);
            var nameMismatches = Enumerable.Range(0, before.Names.Count).Where(i => fixedTree.Names[i] != after.Names[i]).ToList();
            var placeMismatches = Enumerable.Range(0, before.Names.Count).Where(i => !fixedTree.Children[i].SequenceEqual(after.Children[i])).ToList();
            var outsideTouched = plan.NodeRenames.Select(r => (int)r.Key).Concat(plan.NodeMoves.Select(m => (int)m.Key)).Where(k => !phasing.Contains(k)).ToList();
            var expectedMoves = Enumerable.Range(0, before.Names.Count).Where(i => before.ParentOf(i) != after.ParentOf(i)).ToList();
            var renamedInCleanUp = Enumerable.Range(0, before.Names.Count).Count(i => before.Names[i] != after.Names[i]);
            foreach (var i in nameMismatches) output.WriteLine("NAME  #" + i + ": " + fixedTree.Names[i] + "  expected " + after.Names[i]);
            foreach (var i in placeMismatches) output.WriteLine("PLACE #" + i + ": children " + string.Join(",", fixedTree.Children[i]) + "  expected " + string.Join(",", after.Children[i]));
            output.WriteLine(string.Format("Nodes {0}, phasing tree {1}; renames {2} (clean-up renamed {3}), moves {4} (expected {5}); name mismatches {6}, place mismatches {7}, outside touched {8}; checks {9}",
                before.Names.Count, phasing.Count, plan.NodeRenames.Count, renamedInCleanUp, plan.NodeMoves.Count, expectedMoves.Count,
                nameMismatches.Count, placeMismatches.Count, outsideTouched.Count, plan.ToCheck));
            Assert.Empty(nameMismatches);
            Assert.Empty(placeMismatches);
            Assert.Empty(outsideTouched);
            Assert.Equal(renamedInCleanUp, plan.NodeRenames.Count);
            Assert.Equal(expectedMoves, plan.NodeMoves.Select(m => (int)m.Key).ToList());
            Assert.Equal(after.ParentOf(expectedMoves[0]), (int)plan.NodeMoves[0].ToParentKey);

            // The cleaned file needs nothing more, with or without the review's stages.
            var cleaned = after.SceneNodes();
            foreach (var again in new[] { PhasingNameFixer.Plan(cleaned, null, PhasingNameFixer.DefaultWordFixes(), KerbingOverrides(cleaned)),
                                          PhasingNameFixer.Plan(cleaned, null, PhasingNameFixer.DefaultWordFixes(), null) })
            {
                output.WriteLine("Cleaned file: " + again.NodeRenames.Count + " renames, " + again.NodeMoves.Count + " moves, " + again.ToCheck + " to check");
                Assert.Empty(again.NodeRenames);
                Assert.Empty(again.NodeMoves);
            }

            var outPath = Environment.GetEnvironmentVariable("PLANNER_FIX_OUT_CSV");
            if (!string.IsNullOrEmpty(outPath))
            {
                File.WriteAllText(outPath, plan.ToCsv(), new UTF8Encoding(false)); // ToCsv carries the byte order mark
            }

            if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
            {
                output.WriteLine("PLANNER_FIX_REF_CSV not set; rename list not compared.");
                return;
            }

            // The rename list: Changed rows match on Type / Current / Corrected (and the layer column); the reasons
            // match too, except where the review's stage or the duplicate handling words them differently.
            var reference = ReadCsv(csvPath);
            Assert.Equal(PhasingFixPlan.CsvHeader, string.Join(",", reference[0]));
            var expectedChanged = reference.Skip(1).Where(r => r[0] == "Changed").ToList();
            var expectedChecks = reference.Skip(1).Where(r => r[0] == "Check").ToList();
            var changed = plan.Rows.Where(r => r.Status == PhasingFixStatus.Changed).ToList();
            var checks = plan.Rows.Where(r => r.Status == PhasingFixStatus.Check).ToList();
            int rowMismatches = 0, layerColumnMismatches = 0, reasonMismatches = 0, reasonsExempt = 0;
            for (int i = 0; i < Math.Max(changed.Count, expectedChanged.Count); i++)
            {
                if (i >= changed.Count || i >= expectedChanged.Count)
                {
                    rowMismatches++;
                    output.WriteLine("ROW " + i + ": only on one side");
                    continue;
                }
                var mine = changed[i];
                var theirs = expectedChanged[i];
                var theirCurrent = DuplicateSuffix.Replace(theirs[3], string.Empty);
                if (mine.TypeText != theirs[1] || mine.CurrentName != theirCurrent || mine.CorrectedName != theirs[4])
                {
                    rowMismatches++;
                    output.WriteLine("ROW " + i + ": " + mine.TypeText + " " + mine.CurrentName + " -> " + mine.CorrectedName + "  expected " + theirs[1] + " " + theirCurrent + " -> " + theirs[4]);
                }
                if (mine.LayerItSitsIn != theirs[2])
                {
                    layerColumnMismatches++;
                    output.WriteLine("LAYER " + i + ": " + mine.LayerItSitsIn + "  expected " + theirs[2]);
                }
                if (mine.WhatChanged != theirs[5])
                {
                    bool exempt = mine.WhatChanged.Contains("in the review") || mine.WhatChanged.Contains("number added") || theirCurrent != theirs[3]
                        || mine.WhatChanged.Contains("moved into");
                    if (exempt) reasonsExempt++; else reasonMismatches++;
                    output.WriteLine((exempt ? "reason (expected to differ) " : "REASON ") + i + ": " + mine.WhatChanged + "  |  reference: " + theirs[5]);
                }
            }
            int checkMismatches = Math.Abs(checks.Count - expectedChecks.Count);
            for (int i = 0; i < Math.Min(checks.Count, expectedChecks.Count); i++)
            {
                var mine = checks[i];
                var theirs = expectedChecks[i];
                if (mine.TypeText != theirs[1] || mine.LayerItSitsIn != theirs[2] || mine.CurrentName != DuplicateSuffix.Replace(theirs[3], string.Empty)
                    || mine.CorrectedName != theirs[4] || mine.WhatChanged != theirs[5])
                {
                    checkMismatches++;
                    output.WriteLine("CHECK " + i + ": " + mine.CurrentName + " | " + mine.WhatChanged + "  expected " + theirs[3] + " | " + theirs[5]);
                }
            }
            output.WriteLine(string.Format("Rename list: {0} Changed rows (reference {1}), {2} Check rows (reference {3}); row mismatches {4}, layer column mismatches {5}, reason mismatches {6} (+{7} expected to differ), check mismatches {8}",
                changed.Count, expectedChanged.Count, checks.Count, expectedChecks.Count, rowMismatches, layerColumnMismatches, reasonMismatches, reasonsExempt, checkMismatches));
            Assert.Equal(0, rowMismatches);
            Assert.Equal(0, layerColumnMismatches);
            Assert.Equal(0, reasonMismatches);
            Assert.Equal(0, checkMismatches);
        }

        // The modellers' scene also has a Max layer for the top node, each group and each layer, nested like the
        // nodes. The fix renames and re-parents those with the nodes, the batch carries every item, and afterwards
        // the scene needs nothing more.
        [Fact]
        public void ReferenceModelWithItsMaxLayersComesOutClean()
        {
            var beforePath = Environment.GetEnvironmentVariable("PLANNER_FIX_V305");
            var afterPath = Environment.GetEnvironmentVariable("PLANNER_FIX_V306");
            if (string.IsNullOrEmpty(beforePath) || string.IsNullOrEmpty(afterPath) || !File.Exists(beforePath) || !File.Exists(afterPath))
            {
                output.WriteLine("PLANNER_FIX_V305 / PLANNER_FIX_V306 not set; skipped.");
                return;
            }
            var before = Tree.Load(beforePath);
            var after = Tree.Load(afterPath);
            var scene = new FixerScene();
            scene.Nodes.AddRange(before.SceneNodes());
            var top = before.Roots.First(r => before.Names[r].IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0);

            // Max layers for the top node, its children that hold something, and the groups' children.
            var mirrored = new List<int> { top };
            mirrored.AddRange(before.Children[top].Where(c => before.Children[c].Count > 0));
            mirrored.AddRange(before.Children[top].Where(c => before.Names[c].StartsWith("_", StringComparison.Ordinal)).SelectMany(g => before.Children[g]));
            foreach (var i in mirrored)
            {
                var parent = before.ParentOf(i);
                scene.AddMaxLayer(before.Names[i], i == top ? null : before.Names[parent.Value]);
            }
            Assert.Equal(mirrored.Count, scene.MaxLayers.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var overrides = KerbingOverrides(scene.Nodes);
            var plan = scene.Plan(overrides);
            var maxLayerChecks = plan.Rows.Where(r => r.Status == PhasingFixStatus.Check && r.WhatChanged.Contains("Max layer")).ToList();
            output.WriteLine(string.Format("Max layers {0}: {1} renames, {2} moves, {3} Max layer checks; nodes: {4} renames, {5} moves",
                scene.MaxLayers.Count, plan.MaxLayerRenames.Count, plan.MaxLayerMoves.Count, maxLayerChecks.Count, plan.NodeRenames.Count, plan.NodeMoves.Count));
            Assert.Empty(maxLayerChecks);
            Assert.Single(plan.MaxLayerMoves);
            Assert.Equal(mirrored.Count(i => before.Names[i] != after.Names[i]), plan.MaxLayerRenames.Count);

            // The batch takes every item, and its arrays read back as the plan.
            var batch = PhasingFixScript.Build(plan, "__phasing_fix_test_");
            Assert.Empty(batch.Refused);
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Equal(plan.NodeRenames.Select(r => r.OldName), MaxScriptText.ArrayItems(batch.Script, "fixNodeOld"));
            Assert.Equal(plan.NodeRenames.Select(r => r.NewName), MaxScriptText.ArrayItems(batch.Script, "fixNodeNew"));
            Assert.Equal(plan.MaxLayerRenames.Select(r => r.NewName), MaxScriptText.ArrayItems(batch.Script, "fixLayerRenameNew"));
            output.WriteLine("Batch: " + batch.Script.Length + " characters");

            // Carried out, every node and Max layer has its cleaned name and place.
            scene.Apply(plan);
            var byKey = scene.Nodes.ToDictionary(n => n.Key);
            var nameMismatches = Enumerable.Range(0, after.Names.Count).Count(i => byKey[i].Name != after.Names[i]);
            var placeMismatches = Enumerable.Range(0, after.Names.Count).Count(i => byKey[i].ParentKey != after.ParentOf(i));
            int layerMismatches = 0;
            for (int m = 0; m < mirrored.Count; m++)
            {
                var i = mirrored[m];
                var parent = after.ParentOf(i);
                var expectedParent = i == top ? null : after.Names[parent.Value];
                if (scene.MaxLayers[m].Name != after.Names[i] || scene.MaxLayers[m].ParentName != expectedParent)
                {
                    layerMismatches++;
                    output.WriteLine("MAX LAYER " + scene.MaxLayers[m].Name + " under " + scene.MaxLayers[m].ParentName + "  expected " + after.Names[i] + " under " + expectedParent);
                }
            }
            output.WriteLine(string.Format("After the fix: name mismatches {0}, place mismatches {1}, Max layer mismatches {2}", nameMismatches, placeMismatches, layerMismatches));
            Assert.Equal(0, nameMismatches);
            Assert.Equal(0, placeMismatches);
            Assert.Equal(0, layerMismatches);

            foreach (var again in new[] { scene.Plan(overrides), scene.Plan() })
            {
                output.WriteLine("Fixed scene: " + again.NodeRenames.Count + " renames, " + again.NodeMoves.Count + " moves, "
                    + again.MaxLayerRenames.Count + " Max layer renames, " + again.MaxLayerMoves.Count + " Max layer moves, " + again.ToCheck + " to check");
                Assert.False(again.HasChanges);
                Assert.Null(PhasingFixScript.ExportWarning(again));
            }
        }
    }
}
