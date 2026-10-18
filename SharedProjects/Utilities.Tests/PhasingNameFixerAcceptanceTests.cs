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
    /// The clean-up wrote sub-stages with a full stop; the convention now writes an underscore, and an object's name has
    /// no hyphen at all (Unreal's Datasmith import turns each one into an underscore). So the expected result is v3_06
    /// with every sub-stage full stop (layer IDs and Ph/St tags) turned into an underscore and every hyphen in a tagged
    /// object's name too, "v3_06u": v3_05 must come out as v3_06u, v3_06 as v3_06u through renames that only do that, and
    /// v3_06u must need nothing. The rename list is compared with the clean-up's with the same changes made on both
    /// sides, leaving out the fixer's own reasons for them and its rows that only do that (the clean-up had no such
    /// change).
    ///
    /// The review edits the clean-up made by hand are given as stage overrides: in phase 2, the Eastside Kerbing
    /// layers are stage 7 and the Westside Kerbing layers stage 8.
    /// </summary>
    public class PhasingNameFixerAcceptanceTests
    {
        // The GLB exporter's suffix on a repeated name; in 3ds Max those objects carry the same name.
        private static readonly Regex DuplicateSuffix = new Regex(@"\s*\([0-9]+\)\s*\z", RegexOptions.CultureInvariant);
        // A sub-stage after a full stop: in a layer ID at the start of a name (<PP>_<SS>.<n>_), and in a Ph/St tag.
        // Written here independently of the fixer, as the definition of v3_06u.
        private static readonly Regex LayerIdFullStop = new Regex(@"^([0-9]+_[0-9]+)\.([0-9]+)(?=_)", RegexOptions.CultureInvariant);
        private static readonly Regex TagFullStop = new Regex(@"(Ph[0-9]+_St[0-9]+)\.([0-9]+)(?=_)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        // A tagged object's name: it starts with a Ph/St tag. Its hyphens become underscores (Unreal's Datasmith import
        // does it anyway); a layer's name keeps them (its dates, and the rest).
        private static readonly Regex TaggedObject = new Regex(@"^Ph[0-9]+_St[0-9]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        // The same in running text (the What changed column): IDs and tags wherever they stand.
        private static readonly Regex TextIdFullStop = new Regex(@"(?<![0-9])([0-9]{2}_[0-9]{2})\.([0-9]+)", RegexOptions.CultureInvariant);
        private static readonly Regex TextTagFullStop = new Regex(@"(Ph[0-9]+_St[0-9]+)\.([0-9]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private readonly ITestOutputHelper output;

        /// <summary>
        /// A name as the convention writes it now (v3_06 to v3_06u): its sub-stage full stops turned into underscores,
        /// and, for a tagged object, every hyphen too.
        /// </summary>
        internal static string Underscore(string name)
        {
            var result = TagFullStop.Replace(LayerIdFullStop.Replace(name ?? string.Empty, "$1_$2"), "$1_$2");
            return TaggedObject.IsMatch(result) ? result.Replace('-', '_') : result;
        }

        internal static string StripDuplicateSuffix(string name)
        {
            return DuplicateSuffix.Replace(name ?? string.Empty, string.Empty);
        }

        private static string UnderscoreText(string text)
        {
            return TextTagFullStop.Replace(TextIdFullStop.Replace(text ?? string.Empty, "$1_$2"), "$1_$2");
        }

        // The fixer's reasons for what the clean-up never did: the sub-stage's underscore, and the object's hyphens.
        private static bool NewReason(string reason)
        {
            return reason == PhasingNameFixer.FullStopReason || reason == PhasingNameFixer.HyphenReason || reason == PhasingNameFixer.DatasmithReason;
        }

        // A reason list without those reasons.
        private static string WithoutNewReasons(string reasons)
        {
            return string.Join("; ", (reasons ?? string.Empty).Split(new[] { "; " }, StringSplitOptions.None).Where(r => !NewReason(r)).ToArray());
        }

        // A Changed row that only puts the underscores in: the clean-up did not have it.
        private static bool UnderscoreOnly(PhasingFixRow row)
        {
            return row.Status == PhasingFixStatus.Changed && WithoutNewReasons(row.WhatChanged).Length == 0 && Underscore(row.CurrentName) == row.CorrectedName;
        }

        public PhasingNameFixerAcceptanceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private sealed class Tree
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<List<int>> Children = new List<List<int>>();
            /// <summary>The GLB node carries a mesh (the extract's "mesh" flag; true when the file has none).</summary>
            public readonly List<bool> Mesh = new List<bool>();
            public List<int> Roots = new List<int>();

            public static Tree Load(string path)
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var tree = new Tree { Roots = json["roots"].Select(r => (int)r).ToList() };
                foreach (var node in json["nodes"])
                {
                    tree.Names.Add(DuplicateSuffix.Replace((string)node["name"] ?? string.Empty, string.Empty));
                    tree.Children.Add(node["children"].Select(c => (int)c).ToList());
                    tree.Mesh.Add(node["mesh"] == null || (bool)node["mesh"]);
                }
                return tree;
            }

            /// <summary>The same tree with its names written as the convention writes them now (<see cref="Underscore"/>).</summary>
            public Tree Underscored()
            {
                var result = new Tree { Roots = Roots.ToList() };
                result.Names.AddRange(Names.Select(Underscore));
                result.Children.AddRange(Children.Select(c => c.ToList()));
                result.Mesh.AddRange(Mesh);
                return result;
            }

            /// <summary>Every node under <paramref name="node"/>, at any depth.</summary>
            public List<int> Descendants(int node)
            {
                var result = new List<int>();
                var pending = new Stack<int>(Children[node]);
                while (pending.Count > 0)
                {
                    var next = pending.Pop();
                    result.Add(next);
                    foreach (var child in Children[next]) pending.Push(child);
                }
                return result;
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
                for (int i = 0; i < nodes.Count; i++)
                {
                    // The GLB does not say which nodes are helpers, but the export writes a node without a mesh only for
                    // a helper (a dummy, a point, a group head) or for a node holding exported objects: those stand in.
                    nodes[i].IsHelper = !Mesh[i];
                }
                return nodes;
            }

            /// <summary>The tree after a plan: renamed by key, moved nodes appended to their new parent's children.</summary>
            public Tree Applied(PhasingFixPlan plan)
            {
                var result = new Tree { Roots = Roots.ToList() };
                result.Names.AddRange(Names);
                result.Children.AddRange(Children.Select(c => c.ToList()));
                result.Mesh.AddRange(Mesh);
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
            var expected = after.Underscored(); // v3_06u
            Assert.Equal(before.Names.Count, after.Names.Count);
            var underscored = Enumerable.Range(0, after.Names.Count).Count(i => after.Names[i] != expected.Names[i]);
            output.WriteLine("v3_06u: " + underscored + " of v3_06's " + after.Names.Count + " names take underscores; dimension and version full stops kept: "
                + string.Join(", ", expected.Names.Where(n => n.Contains(".")).Take(5).ToArray()) + "; tagged objects whose hyphens go: "
                + Enumerable.Range(0, after.Names.Count).Count(i => after.Names[i].Contains("-") && TaggedObject.IsMatch(after.Names[i])));
            Assert.True(underscored > 100);

            var nodes = before.SceneNodes();
            var overrides = KerbingOverrides(nodes);
            var plan = PhasingNameFixer.Plan(nodes, null, PhasingNameFixer.DefaultWordFixes(), overrides);
            output.WriteLine(plan.Summary());
            output.WriteLine("Stage overrides: " + overrides.Count);
            Assert.NotNull(plan.TopKey);
            var top = (int)plan.TopKey.Value;
            var phasing = before.PhasingTree(top);

            // v3_05: every node comes out with its v3_06u name and place; nothing outside the phasing tree is touched.
            var fixedTree = before.Applied(plan);
            var nameMismatches = Enumerable.Range(0, before.Names.Count).Where(i => fixedTree.Names[i] != expected.Names[i]).ToList();
            var placeMismatches = Enumerable.Range(0, before.Names.Count).Where(i => !fixedTree.Children[i].SequenceEqual(expected.Children[i])).ToList();
            var outsideTouched = plan.NodeRenames.Select(r => (int)r.Key).Concat(plan.NodeMoves.Select(m => (int)m.Key)).Where(k => !phasing.Contains(k)).ToList();
            var expectedMoves = Enumerable.Range(0, before.Names.Count).Where(i => before.ParentOf(i) != after.ParentOf(i)).ToList();
            var renamesExpected = Enumerable.Range(0, before.Names.Count).Count(i => before.Names[i] != expected.Names[i]);
            foreach (var i in nameMismatches) output.WriteLine("NAME  #" + i + ": " + fixedTree.Names[i] + "  expected " + expected.Names[i]);
            foreach (var i in placeMismatches) output.WriteLine("PLACE #" + i + ": children " + string.Join(",", fixedTree.Children[i]) + "  expected " + string.Join(",", expected.Children[i]));
            output.WriteLine(string.Format("v3_05 -> v3_06u: nodes {0}, phasing tree {1}; renames {2} (expected {3}), moves {4} (expected {5}); name mismatches {6}, place mismatches {7}, outside touched {8}; checks {9}",
                before.Names.Count, phasing.Count, plan.NodeRenames.Count, renamesExpected, plan.NodeMoves.Count, expectedMoves.Count,
                nameMismatches.Count, placeMismatches.Count, outsideTouched.Count, plan.ToCheck()));
            output.WriteLine(plan.Counts.Describe(true));
            Assert.Empty(nameMismatches);
            Assert.Empty(placeMismatches);
            Assert.Empty(outsideTouched);
            Assert.Equal(renamesExpected, plan.NodeRenames.Count);
            Assert.Equal(expectedMoves, plan.NodeMoves.Select(m => (int)m.Key).ToList());
            Assert.Equal(after.ParentOf(expectedMoves[0]), (int)plan.NodeMoves[0].ToParentKey);

            // v3_06 (the clean-up, full stops and hyphens): renames that only put the underscores in, no moves, and it comes
            // out as v3_06u.
            var cleaned = after.SceneNodes();
            foreach (var again in new[] { PhasingNameFixer.Plan(cleaned, null, PhasingNameFixer.DefaultWordFixes(), KerbingOverrides(cleaned)),
                                          PhasingNameFixer.Plan(cleaned, null, PhasingNameFixer.DefaultWordFixes(), null) })
            {
                var applied = after.Applied(again);
                var mismatches = Enumerable.Range(0, after.Names.Count).Count(i => applied.Names[i] != expected.Names[i]);
                var notUnderscoreOnly = again.NodeRenames.Count(r => Underscore(r.OldName) != r.NewName);
                var otherReasons = again.Rows.Count(r => r.Status == PhasingFixStatus.Changed && WithoutNewReasons(r.WhatChanged).Length > 0);
                output.WriteLine(string.Format("v3_06 -> v3_06u: {0} renames (expected {1}), {2} moves, {3} to check; name mismatches {4}, renames that do more than the underscores {5}, other reasons {6}",
                    again.NodeRenames.Count, underscored, again.NodeMoves.Count, again.ToCheck(), mismatches, notUnderscoreOnly, otherReasons));
                Assert.Equal(0, mismatches);
                Assert.Equal(0, notUnderscoreOnly);
                Assert.Equal(0, otherReasons);
                Assert.Equal(underscored, again.NodeRenames.Count);
                Assert.Empty(again.NodeMoves);
            }

            // v3_06u needs nothing more, with or without the review's stages.
            var done = expected.SceneNodes();
            foreach (var again in new[] { PhasingNameFixer.Plan(done, null, PhasingNameFixer.DefaultWordFixes(), KerbingOverrides(done)),
                                          PhasingNameFixer.Plan(done, null, PhasingNameFixer.DefaultWordFixes(), null) })
            {
                output.WriteLine("v3_06u: " + again.NodeRenames.Count + " renames, " + again.NodeMoves.Count + " moves, " + again.ToCheck() + " to check");
                Assert.Empty(again.NodeRenames);
                Assert.Empty(again.NodeMoves);
                Assert.False(again.HasChanges());
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
            // match too, except where the review's stage or the duplicate handling words them differently. The
            // clean-up's names and reasons are taken with their underscores put in, the fixer's reasons without its
            // reasons for them, and its rows that only put them in (names the clean-up left alone) are counted apart.
            var reference = ReadCsv(csvPath);
            Assert.Equal(PhasingFixPlanText.CsvHeader, string.Join(",", reference[0]));
            var expectedChanged = reference.Skip(1).Where(r => r[0] == "Changed").ToList();
            var expectedChecks = reference.Skip(1).Where(r => r[0] == "Check").ToList();
            var underscoreOnly = plan.Rows.Where(UnderscoreOnly).ToList();
            var changed = plan.Rows.Where(r => r.Status == PhasingFixStatus.Changed && !UnderscoreOnly(r)).ToList();
            var checks = plan.Rows.Where(r => r.Status == PhasingFixStatus.Check).ToList();
            var leftAloneInCleanUp = Enumerable.Range(0, before.Names.Count).Count(i => before.Names[i] == after.Names[i] && after.Names[i] != expected.Names[i]);
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
                if (mine.TypeText() != theirs[1] || mine.CurrentName != theirCurrent || mine.CorrectedName != Underscore(theirs[4]))
                {
                    rowMismatches++;
                    output.WriteLine("ROW " + i + ": " + mine.TypeText() + " " + mine.CurrentName + " -> " + mine.CorrectedName + "  expected " + theirs[1] + " " + theirCurrent + " -> " + Underscore(theirs[4]));
                }
                if (mine.LayerItSitsIn != Underscore(theirs[2]))
                {
                    layerColumnMismatches++;
                    output.WriteLine("LAYER " + i + ": " + mine.LayerItSitsIn + "  expected " + Underscore(theirs[2]));
                }
                var mineReason = UnderscoreText(WithoutNewReasons(mine.WhatChanged));
                var theirReason = UnderscoreText(theirs[5]);
                if (mineReason != theirReason)
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
                if (mine.TypeText() != theirs[1] || mine.LayerItSitsIn != Underscore(theirs[2]) || mine.CurrentName != DuplicateSuffix.Replace(theirs[3], string.Empty)
                    || mine.CorrectedName != Underscore(theirs[4]) || UnderscoreText(mine.WhatChanged) != UnderscoreText(theirs[5]))
                {
                    checkMismatches++;
                    output.WriteLine("CHECK " + i + ": " + mine.CurrentName + " | " + mine.WhatChanged + "  expected " + theirs[3] + " | " + theirs[5]);
                }
            }
            output.WriteLine(string.Format("Rename list: {0} Changed rows (reference {1}) + {2} underscore-only (names the clean-up left alone: {3}), {4} Check rows (reference {5}); row mismatches {6}, layer column mismatches {7}, reason mismatches {8} (+{9} expected to differ), check mismatches {10}",
                changed.Count, expectedChanged.Count, underscoreOnly.Count, leftAloneInCleanUp, checks.Count, expectedChecks.Count, rowMismatches, layerColumnMismatches, reasonMismatches, reasonsExempt, checkMismatches));
            Assert.Equal(0, rowMismatches);
            Assert.Equal(0, layerColumnMismatches);
            Assert.Equal(0, reasonMismatches);
            Assert.Equal(0, checkMismatches);
            Assert.Equal(leftAloneInCleanUp, underscoreOnly.Count);
        }

        // The modellers' scene also has a Max layer for the top node, each group and each layer, nested like the
        // nodes. The fix renames and re-parents those with the nodes, and afterwards the scene needs nothing more.
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
            var after = Tree.Load(afterPath).Underscored(); // v3_06u
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

            // The counts: every Max layer under the top one; the nodes without a mesh stand in for the helpers. Every
            // group and layer has a Max layer of its name, the empty 01_00_START_DATE_01-06-26 too, so they pair up.
            output.WriteLine(plan.Counts.Describe(true));
            output.WriteLine(plan.Counts.PairingNote() ?? "Layers and helpers pair up.");
            Assert.Equal(mirrored.Count - 1, plan.Counts.Layers);
            Assert.Equal(before.Names[top], plan.Counts.TopLayerName);
            Assert.Equal(before.Descendants(top).Count(i => !before.Mesh[i]), plan.Counts.Helpers);
            Assert.Equal(plan.Counts.Layers, plan.Counts.Helpers);
            Assert.Null(plan.Counts.PairingNote());

            // Carried out, every node and Max layer has its v3_06u name and place.
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
                    + again.MaxLayerRenames.Count + " Max layer renames, " + again.MaxLayerMoves.Count + " Max layer moves, " + again.ToCheck() + " to check");
                Assert.False(again.HasChanges());
                Assert.Equal(0, PhasingFixPlanText.CorrectableCount(again));
            }
        }
    }
}
