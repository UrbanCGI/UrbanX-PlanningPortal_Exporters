using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// A stand-in for 3ds Max running a Clean up batch: it reads the batch's arrays and carries them out on a scene snapshot
    /// as the script's loops do (find everything first, then rename the nodes, rename the Max layers through temporary names,
    /// make the list's new Max layers, move Max layers, make the list's new helpers, link, put on layers, write the Planner
    /// IDs), and writes the report the script writes. A helper it makes takes the key below zero its place stands for (the
    /// batch numbers the new helpers first: place 1 is -1). It checks the data the script carries, not MAXScript itself.
    /// </summary>
    internal sealed class CleanUpScriptRun
    {
        public PlannerSceneSnapshot Scene;
        /// <summary>The user properties the batch wrote, by node handle.</summary>
        public readonly Dictionary<long, Dictionary<string, string>> UserProps = new Dictionary<long, Dictionary<string, string>>();
        public string Output;

        public static CleanUpScriptRun Run(string script, PlannerSceneSnapshot before)
        {
            var run = new CleanUpScriptRun { Scene = Copy(before) };
            var scene = run.Scene;
            var log = new StringBuilder();
            Func<string, List<string>> strings = name => MaxScriptText.ArrayItems(script, name);
            Func<string, List<int>> numbers = name => MaxScriptText.ArrayItems(script, name).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList();

            var layerNames = strings("bLayerNames");
            var layers = layerNames.Select(n => n.Length == 0 ? null : scene.Layers.FirstOrDefault(l => string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
            for (int i = 0; i < layers.Count; i++)
            {
                if (layerNames[i].Length > 0 && layers[i] == null) log.Append("FAIL\tlayerfind\t" + (i + 1) + "\tgone\n");
            }
            var handles = numbers("bHandles");
            var names = strings("bNames");
            var nodes = new List<PlannerSceneNode>();
            for (int i = 0; i < handles.Count; i++)
            {
                if (handles[i] == 0)
                {
                    nodes.Add(null);
                    continue;
                }
                var node = scene.Nodes.FirstOrDefault(n => n.Key == handles[i]);
                if (node == null)
                {
                    log.Append("FAIL\tfind\t" + (i + 1) + "\tgone\n");
                }
                else if (node.Name != names[i])
                {
                    log.Append("FAIL\tfind\t" + (i + 1) + "\tchanged\n");
                    node = null;
                }
                nodes.Add(node);
            }

            int helpersRenamed = 0, objectsRenamed = 0, moved = 0, linked = 0, put = 0, layersMoved = 0, layersRenamed = 0, layersCreated = 0, helpersCreated = 0, helpersPut = 0, stamped = 0;
            var renameNode = numbers("bRenameNode");
            var renameNew = strings("bRenameNew");
            var renameKind = numbers("bRenameKind");
            for (int i = 0; i < renameNode.Count; i++)
            {
                var node = nodes[renameNode[i] - 1];
                if (node == null) continue;
                node.Name = renameNew[i];
                if (renameKind[i] == 2) objectsRenamed++; else helpersRenamed++;
            }
            var renameIdx = numbers("bLayerRenameIdx");
            var renameLayerNew = strings("bLayerRenameNew");
            var renameTemp = strings("bLayerRenameTemp");
            var stepped = new bool[renameIdx.Count];
            for (int i = 0; i < renameIdx.Count; i++)
            {
                var layer = layers[renameIdx[i] - 1];
                if (layer == null)
                {
                    log.Append("FAIL\tlayer\t" + (i + 1) + "\tgone\n");
                    continue;
                }
                RenameLayer(scene, layer, renameTemp[i]);
                stepped[i] = true;
            }
            for (int i = 0; i < renameIdx.Count; i++)
            {
                if (!stepped[i]) continue;
                var layer = layers[renameIdx[i] - 1];
                // 3ds Max refuses a name another layer has (any capitals): the layer goes back to its old name.
                if (scene.Layers.Any(l => l != layer && string.Equals(l.Name, renameLayerNew[i], StringComparison.OrdinalIgnoreCase)))
                {
                    RenameLayer(scene, layer, layerNames[renameIdx[i] - 1]);
                    log.Append("FAIL\tlayer\t" + (i + 1) + "\ttaken\n");
                    continue;
                }
                RenameLayer(scene, layer, renameLayerNew[i]);
                layersRenamed++;
            }
            var newLayer = numbers("bNewLayer");
            var newLayerName = strings("bNewLayerName");
            var newLayerParent = numbers("bNewLayerParent");
            for (int i = 0; i < newLayer.Count; i++)
            {
                // LayerManager.newLayerFromName gives undefined for a name a layer has.
                if (scene.Layers.Any(l => string.Equals(l.Name, newLayerName[i], StringComparison.OrdinalIgnoreCase)))
                {
                    log.Append("FAIL\tlayercreate\t" + (i + 1) + "\ttaken\n");
                    continue;
                }
                var layer = new PhasingMaxLayer { Name = newLayerName[i] };
                scene.Layers.Add(layer);
                layers[newLayer[i] - 1] = layer;
                layersCreated++;
                if (newLayerParent[i] > 0)
                {
                    var parent = layers[newLayerParent[i] - 1];
                    if (parent == null) log.Append("FAIL\tlayerparent\t" + (i + 1) + "\tgone\n");
                    else layer.ParentName = parent.Name;
                }
            }
            var moveChild = numbers("bLayerMoveChild");
            var moveParent = numbers("bLayerMoveParent");
            for (int i = 0; i < moveChild.Count; i++)
            {
                var layer = layers[moveChild[i] - 1];
                var parent = layers[moveParent[i] - 1];
                if (layer == null || parent == null)
                {
                    log.Append("FAIL\tlayermove\t" + (i + 1) + "\tgone\n");
                    continue;
                }
                layer.ParentName = parent.Name;
                layersMoved++;
            }
            var newNode = numbers("bNewNode");
            var newNodeName = strings("bNewNodeName");
            var newNodeLayer = numbers("bNewNodeLayer");
            var newNodeParent = numbers("bNewNodeParent");
            var newNodeId = strings("bNewNodeId");
            for (int i = 0; i < newNode.Count; i++)
            {
                var layer = newNodeLayer[i] > 0 ? layers[newNodeLayer[i] - 1] : null;
                if (layer == null)
                {
                    log.Append("FAIL\thelpercreate\t" + (i + 1) + "\tnolayer\n");
                    continue;
                }
                var node = new PlannerSceneNode { Key = -newNode[i], Name = newNodeName[i], IsHelper = true, LayerName = layer.Name, ChildIndex = scene.Nodes.Count(n => !n.ParentKey.HasValue) };
                scene.Nodes.Add(node);
                nodes[newNode[i] - 1] = node;
                helpersCreated++;
                if (newNodeId[i].Length > 0)
                {
                    node.PlannerId = newNodeId[i].Replace("%20", " ");
                    Prop(run, node.Key)[PlannerSceneModel.PlannerIdProperty] = newNodeId[i];
                    stamped++;
                }
                if (newNodeParent[i] > 0)
                {
                    var parent = nodes[newNodeParent[i] - 1];
                    if (parent == null)
                    {
                        log.Append("FAIL\thelperparent\t" + (i + 1) + "\tgone\n");
                        continue;
                    }
                    var siblings = scene.Nodes.Where(n => n.ParentKey == parent.Key && n != node).ToList();
                    node.ParentKey = parent.Key;
                    node.ChildIndex = siblings.Count == 0 ? 0 : siblings.Max(n => n.ChildIndex) + 1;
                }
            }
            moved = LinkAll(scene, nodes, numbers("bMoveNode"), numbers("bMoveParent"), "move", log);
            linked = LinkAll(scene, nodes, numbers("bLinkNode"), numbers("bLinkParent"), "link", log);
            var putNode = numbers("bPutNode");
            var putLayer = numbers("bPutLayer");
            var putKind = numbers("bPutKind");
            for (int i = 0; i < putNode.Count; i++)
            {
                var node = nodes[putNode[i] - 1];
                var layer = layers[putLayer[i] - 1];
                if (node == null || layer == null)
                {
                    log.Append("FAIL\tput\t" + (i + 1) + "\tgone\n");
                    continue;
                }
                node.LayerName = layer.Name;
                if (putKind[i] == 1) helpersPut++; else put++;
            }
            var stampNode = numbers("bStampNode");
            var stampId = strings("bStampId");
            for (int i = 0; i < stampNode.Count; i++)
            {
                var node = nodes[stampNode[i] - 1];
                if (node == null) continue;
                node.PlannerId = stampId[i].Replace("%20", " ");
                Prop(run, node.Key)[PlannerSceneModel.PlannerIdProperty] = stampId[i];
                stamped++;
            }
            log.Append("DONE\t" + string.Join("\t", new[] { helpersRenamed, objectsRenamed, moved, linked, put, layersMoved, layersRenamed, layersCreated, helpersCreated, helpersPut, stamped }
                .Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()) + "\n");
            log.Append(PlannerCleanUpScript.EndMarker + "\n");
            run.Output = log.ToString();
            var parents = new HashSet<long>(scene.Nodes.Where(n => n.ParentKey.HasValue).Select(n => n.ParentKey.Value));
            foreach (var node in scene.Nodes.Where(n => parents.Contains(n.Key)))
            {
                node.HasChildren = true;
            }
            return run;
        }

        private static Dictionary<string, string> Prop(CleanUpScriptRun run, long key)
        {
            Dictionary<string, string> props;
            if (!run.UserProps.TryGetValue(key, out props))
            {
                props = new Dictionary<string, string>(StringComparer.Ordinal);
                run.UserProps[key] = props;
            }
            return props;
        }

        // Links each node under its parent, last among the parent's children (world positions are not modelled).
        private static int LinkAll(PlannerSceneSnapshot scene, List<PlannerSceneNode> nodes, List<int> children, List<int> parents, string kind, StringBuilder log)
        {
            int done = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var node = nodes[children[i] - 1];
                var parent = nodes[parents[i] - 1];
                if (node == null || parent == null)
                {
                    log.Append("FAIL\t" + kind + "\t" + (i + 1) + "\tgone\n");
                    continue;
                }
                var siblings = scene.Nodes.Where(n => n.ParentKey == parent.Key && n != node).ToList();
                node.ParentKey = parent.Key;
                node.ChildIndex = siblings.Count == 0 ? 0 : siblings.Max(n => n.ChildIndex) + 1;
                done++;
            }
            return done;
        }

        // A Max layer renamed: its nodes and its child layers follow, as they hold the layer itself in 3ds Max.
        private static void RenameLayer(PlannerSceneSnapshot scene, PhasingMaxLayer layer, string name)
        {
            foreach (var node in scene.Nodes.Where(n => string.Equals(n.LayerName, layer.Name, StringComparison.OrdinalIgnoreCase)))
            {
                node.LayerName = name;
            }
            foreach (var child in scene.Layers.Where(l => string.Equals(l.ParentName, layer.Name, StringComparison.OrdinalIgnoreCase)))
            {
                child.ParentName = name;
            }
            layer.Name = name;
        }

        public static PlannerSceneSnapshot Copy(PlannerSceneSnapshot before)
        {
            var copy = new PlannerSceneSnapshot { ExportHidden = before.ExportHidden };
            copy.Layers.AddRange(before.Layers.Select(l => new PhasingMaxLayer { Name = l.Name, ParentName = l.ParentName }));
            copy.Nodes.AddRange(before.Nodes.Select(n => new PlannerSceneNode
            {
                Key = n.Key, Name = n.Name, ParentKey = n.ParentKey, ChildIndex = n.ChildIndex, HasChildren = n.HasChildren, IsGroupHead = n.IsGroupHead,
                IsGroupMember = n.IsGroupMember, IsHelper = n.IsHelper, IsShape = n.IsShape, LayerName = n.LayerName, PlannerId = n.PlannerId, Hidden = n.Hidden,
                NoExport = n.NoExport, NotInThisExport = n.NotInThisExport
            }));
            return copy;
        }

        /// <summary>Every difference between the scene the batch left and the one the plan promises (names, parents, layers, Planner IDs), or none.</summary>
        public static List<string> Differences(PlannerSceneSnapshot ran, PlannerSceneSnapshot promised)
        {
            var bad = new List<string>();
            var byKey = promised.Nodes.ToDictionary(n => n.Key);
            foreach (var node in ran.Nodes)
            {
                PlannerSceneNode want;
                if (!byKey.TryGetValue(node.Key, out want))
                {
                    bad.Add("node " + node.Key + " " + node.Name + " not promised");
                    continue;
                }
                if (node.Name != want.Name) bad.Add("node " + node.Key + ": " + node.Name + ", promised " + want.Name);
                if (node.ParentKey != want.ParentKey) bad.Add("node " + node.Name + ": under " + node.ParentKey + ", promised " + want.ParentKey);
                if (!string.Equals(node.LayerName, want.LayerName, StringComparison.OrdinalIgnoreCase)) bad.Add("node " + node.Name + ": on " + node.LayerName + ", promised " + want.LayerName);
                if (node.PlannerId != want.PlannerId) bad.Add("node " + node.Name + ": Planner ID " + node.PlannerId + ", promised " + want.PlannerId);
            }
            bad.AddRange(promised.Nodes.Where(n => !ran.Nodes.Any(r => r.Key == n.Key)).Select(n => "node " + n.Key + " " + n.Name + " promised, not there"));
            var layers = ran.Layers.Select(l => l.Name + " < " + (l.ParentName ?? "-")).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var promisedLayers = promised.Layers.Select(l => l.Name + " < " + (l.ParentName ?? "-")).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            bad.AddRange(layers.Except(promisedLayers, StringComparer.OrdinalIgnoreCase).Select(x => "Max layer " + x + " not promised"));
            bad.AddRange(promisedLayers.Except(layers, StringComparer.OrdinalIgnoreCase).Select(x => "Max layer " + x + " promised, not there"));
            return bad;
        }
    }

    /// <summary>The MAXScript batch Clean up scene graph runs, and what it reports.</summary>
    public class PlannerCleanUpScriptTests
    {
        private const string Root = "Work_Phasing";

        // A scene with something of every kind for the batch: dated layers and TBC (helper and Max layer), a
        // layer under the root (into its phase group, its Max layer too), an object to link, an object with a split piece, a
        // closing _RM to answer, and a stray leading tag answered by linking it under the layer it names (and onto its Max layer).
        private static TestScene EveryKind()
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            var works = scene.Unit("_02_Works", Root, root);
            var kerbs = scene.Unit("02_01_Kerbs_TBC_17-10-26_20-10-26", "_02_Works", works);
            var kerb = scene.Object("Ph2_St01_IN_Kerb", kerbs, "02_01_Kerbs_TBC_17-10-26_20-10-26");
            scene.Object("Ph2_St01_IN_Kerb_1", kerb, "02_01_Kerbs_TBC_17-10-26_20-10-26");
            scene.Object("Ph2_St01_IN_Barrier_RM", kerbs, "02_01_Kerbs_TBC_17-10-26_20-10-26");
            scene.Object("Ph2_St01_IN_Gully", null, "02_01_Kerbs_TBC_17-10-26_20-10-26");
            var hoarding = scene.Unit("02_02_3_Hoarding_04-10-26_05-02-27", "_02_Works", works);
            scene.Object("Ph2_St02_10_IN_Panel", hoarding, "02_02_3_Hoarding_04-10-26_05-02-27");
            scene.Unit("02_02_10_Integration_17-10-26", "_02_Works", works);
            var ducts = scene.Unit("02_03_Ducts_17-10-26", Root, root);
            scene.Object("Ph2_St03_IN_Duct", ducts, "02_03_Ducts_17-10-26");
            return scene;
        }

        private static Dictionary<string, string> Answers(PlannerCleanUpPlan plan, TestScene scene)
        {
            var closing = plan.Questions.Single(q => q.Case == PlannerCleanUpCase.ClosingTag);
            var stray = plan.Questions.Single(q => q.Case == PlannerCleanUpCase.StrayLead);
            return new Dictionary<string, string>
            {
                { closing.Id, "end" },
                { stray.Id, "link:" + scene.Find("02_02_10_Integration_17-10-26").Key }
            };
        }

        [Fact]
        public void TheBatchCarriesEveryOperationAndRunsToTheScenePlanned()
        {
            var scene = EveryKind();
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
            var result = plan.Resolve(Answers(plan, scene));
            Assert.Equal(0, result.Open);
            Assert.NotEmpty(result.NodeMoves);
            Assert.NotEmpty(result.MaxLayerMoves);
            Assert.Equal(2, result.Links.Count);
            Assert.Single(result.LayerMoves);

            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            var script = batch.Script;
            MaxScriptText.AssertBalanced(script);
            Assert.Empty(batch.Refused);

            // Each node once, by handle, with the name the plan read.
            var handles = MaxScriptText.ArrayItems(script, "bHandles");
            Assert.Equal(handles.Count, handles.Distinct().Count());
            var names = MaxScriptText.ArrayItems(script, "bNames");
            for (int i = 0; i < handles.Count; i++)
            {
                Assert.Equal(scene.Snapshot.Nodes.Single(n => n.Key.ToString(CultureInfo.InvariantCulture) == handles[i]).Name, names[i]);
            }
            Assert.Equal(result.NodeRenames.Select(r => r.NewName).ToArray(), MaxScriptText.ArrayItems(script, "bRenameNew").ToArray());
            Assert.Equal(result.NodeRenames.Select(r => r.Item == PhasingFixItem.Object ? "2" : "1").ToArray(), MaxScriptText.ArrayItems(script, "bRenameKind").ToArray());
            Assert.Equal(result.MaxLayerRenames.Select(r => r.NewName).ToArray(), MaxScriptText.ArrayItems(script, "bLayerRenameNew").ToArray());
            Assert.Equal(Enumerable.Range(1, result.MaxLayerRenames.Count).Select(i => "__t_" + i).ToArray(), MaxScriptText.ArrayItems(script, "bLayerRenameTemp").ToArray());
            // No list: nothing to make, and no Planner ID or date written (D20).
            Assert.Empty(MaxScriptText.ArrayItems(script, "bNewLayer"));
            Assert.Empty(MaxScriptText.ArrayItems(script, "bNewNode"));
            Assert.Empty(MaxScriptText.ArrayItems(script, "bStampNode"));
            Assert.DoesNotContain("planner_start", script);
            Assert.DoesNotContain("planner_tbc", script);
            Assert.DoesNotContain("2026", script);

            // One undo record; everything found before it; the steps in their order (the Max layers renamed before any is made,
            // made before any is moved under a new one; the helpers made before anything is linked under them).
            Assert.Equal(1, Occurrences(script, "undo \"Clean up scene graph\" on"));
            var undo = script.IndexOf("undo \"", StringComparison.Ordinal);
            Assert.True(script.IndexOf("maxOps.getNodeByHandle", StringComparison.Ordinal) < undo);
            Assert.True(script.IndexOf("LayerManager.getLayerFromName", StringComparison.Ordinal) < undo);
            var steps = new[]
            {
                "bNode.name = bRenameNew[i]",
                "bLayer.setName bLayerRenameTemp[i]",
                "bLayer.setName bLayerRenameNew[i]",
                "LayerManager.newLayerFromName bNewLayerName[i]",
                "bLayer.setParent bParent",
                "Dummy name:bNewNodeName[i]",
                "for i = 1 to bMoveNode.count do",
                "for i = 1 to bLinkNode.count do",
                "for i = 1 to bPutNode.count do",
                "setUserProp bNode \"planner_id\" bStampId[i]"
            };
            var at = steps.Select(s => script.IndexOf(s, StringComparison.Ordinal)).ToList();
            Assert.All(at, i => Assert.True(i > undo));
            Assert.Equal(at.OrderBy(i => i).ToList(), at);
            Assert.Contains("attachNodesToGroup #(bNode) bParent", script);
            Assert.Contains("format \"" + PlannerCleanUpScript.EndMarker + "\\n\" to:bLog", script);
            Assert.DoesNotContain("delete", script);
            Assert.DoesNotContain("\n", script.Replace("\r\n", string.Empty));

            // Run: the scene the batch leaves is the one the plan promises, and the report reads back clean.
            var run = CleanUpScriptRun.Run(script, scene.Snapshot);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene.Snapshot)));
            Assert.Empty(run.UserProps);
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems, string.Join("\n", outcome.Failures));
            Assert.Equal(result.NodeRenames.Count(r => r.Item != PhasingFixItem.Object), outcome.HelpersRenamed);
            Assert.Equal(result.NodeRenames.Count(r => r.Item == PhasingFixItem.Object), outcome.ObjectsRenamed);
            Assert.Equal(result.NodeMoves.Count, outcome.HelpersMoved);
            Assert.Equal(result.Links.Count, outcome.Linked);
            Assert.Equal(1, outcome.PutOnLayers);
            Assert.Equal(result.MaxLayerMoves.Count, outcome.MaxLayersMoved);
            Assert.Equal(result.MaxLayerRenames.Count, outcome.MaxLayersRenamed);
            Assert.Equal(0, outcome.MaxLayersCreated + outcome.HelpersCreated + outcome.HelpersPutOnLayers + outcome.IdsWritten);

            // Read again, the scene is clean and on the new names.
            var after = PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes());
            var text = outcome.Describe(result, after, true);
            Assert.Contains("Done: " + result.NodeRenames.Count(r => r.Item != PhasingFixItem.Object) + " helpers renamed", text);
            Assert.Contains("Read again from the scene: nothing is left to change; the scene reads as the new names.", text);
            // Max layers were renamed and moved: whether Edit > Undo takes those back is not checked in 3ds Max yet, so the hold
            // is the sure way back (as for Add layer), and Undo is not promised for them.
            Assert.EndsWith("Edit > Fetch puts the scene back as it was before the clean-up, and is the sure way back: Edit > Undo takes the clean-up back in one step, "
                + "but whether it also takes back the Max layers it renamed, moved or made has not been checked in 3ds Max yet.", text);
            Assert.Equal("Edit > Undo takes the clean-up back in one step, but whether it also takes back the Max layers it renamed, moved or made has not been checked in 3ds Max yet: "
                + "check the Max layers after an undo.", PlannerCleanUpOutcome.WayBack(false, true));
            Assert.Equal("Edit > Fetch puts the scene back as it was before the clean-up; Edit > Undo takes it back in one step.", PlannerCleanUpOutcome.WayBack(true, false));
            Assert.Equal("Edit > Undo takes it back in one step.", PlannerCleanUpOutcome.WayBack(false, false));
        }

        [Fact]
        public void WhatChangedSinceTheListIsLeftAloneAndNamed()
        {
            var scene = EveryKind();
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
            var result = plan.Resolve(Answers(plan, scene));
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");

            // Since the list was made: one object renamed by hand, one Max layer deleted, and a layer made with a new name.
            var changed = CleanUpScriptRun.Copy(scene.Snapshot);
            changed.Nodes.Single(n => n.Name == "Ph2_St03_IN_Duct").Name = "Duct_by_hand";
            changed.Layers.RemoveAll(l => l.Name == "02_02_10_Integration_17-10-26");
            changed.Layers.Add(new PhasingMaxLayer { Name = "02_01_KERBS" });
            var run = CleanUpScriptRun.Run(batch.Script, changed);
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);

            Assert.True(outcome.HasProblems);
            Assert.Contains("Ph2_St03_IN_Duct has been renamed since the list was made, so it was left as it is", outcome.Failures);
            Assert.Contains("the Max layer 02_02_10_Integration_17-10-26 is no longer in the scene, so it was not changed", outcome.Failures);
            Assert.Contains(outcome.Failures, f => f.StartsWith("the Max layer 02_01_Kerbs_TBC_17-10-26_20-10-26 could not become 02_01_Kerbs: that name is taken", StringComparison.Ordinal));
            Assert.Contains(outcome.Failures, f => f.StartsWith("Ph2_St02_10_IN_Panel was not put on the Max layer 02_02_10_Integration_17-10-26", StringComparison.Ordinal));
            Assert.Equal("Duct_by_hand", run.Scene.Nodes.Single(n => n.Key == scene.Find("Ph2_St03_IN_Duct").Key).Name);
            // Everything else is done.
            Assert.Equal("IN_Kerb", run.Scene.Nodes.Single(n => n.Key == scene.Find("Ph2_St01_IN_Kerb").Key).Name);
            var text = outcome.Describe(result, null, false);
            Assert.Contains("changes were not made:", text);
            Assert.EndsWith(PlannerCleanUpOutcome.WayBack(false, true), text);
        }

        [Fact]
        public void WhatMaxScriptCannotTakeIsLeftOut()
        {
            var scene = new TestScene();
            var root = scene.Unit(Root, null, null);
            var works = scene.Unit("_02_Works", Root, root);
            var kerbs = scene.Unit("02_01_Kerbs_17-10-26", "_02_Works", works);
            scene.Snapshot.Nodes.Add(new PlannerSceneNode { Key = 3000000000L, Name = "Ph2_St01_IN_Big", ParentKey = kerbs.Key, LayerName = "02_01_Kerbs_17-10-26" });
            scene.Object("Ph2_St01_IN_Odd\u0001", kerbs, "02_01_Kerbs_17-10-26");
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
            var result = plan.Resolve(null);
            Assert.Contains(result.NodeRenames, r => r.Key == 3000000000L);
            var batch = PlannerCleanUpScript.Build(plan, result, null);
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Contains("Ph2_St01_IN_Big is not renamed to IN_Big: its node handle cannot be passed to MAXScript", batch.Refused);
            Assert.Contains(batch.Refused, r => r.StartsWith("Ph2_St01_IN_Odd", StringComparison.Ordinal) && r.EndsWith("a name holds a control character that cannot be passed to MAXScript", StringComparison.Ordinal));
            Assert.DoesNotContain("3000000000", batch.Script);
            Assert.Contains("__planner_cleanup_1", batch.Script);
            var outcome = PlannerCleanUpScript.ReadResult(batch, "DONE\t1\t0\t0\t0\t0\t0\t1\t0\t0\t0\t0\n" + PlannerCleanUpScript.EndMarker + "\n");
            Assert.True(outcome.HasProblems);
            Assert.Equal(2, outcome.Failures.Count);
        }

        [Fact]
        public void TheReportReadsBackEveryKindOfLine()
        {
            var scene = EveryKind();
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
            var result = plan.Resolve(Answers(plan, scene));
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            var rename = batch.Renames[0];
            var link = batch.Links[0];
            var layer = batch.MaxLayerRenames[0];
            var output = "FAIL\tfind\t1\tgone\n"
                + "FAIL\trename\t1\trefused\n"
                + "FAIL\tlink\t1\terror\t-- Runtime error: no\n"
                + "FAIL\tlayer\t1\tstuck\n"
                + "FAIL\tstamp\t1\terror\tbad\n"
                + "FAIL\tmystery\t9\tgone\n"
                + "ABORT\t-- Unknown property\n"
                + "DONE\t3\t4\t1\t2\t1\t1\t5\t6\t7\t8\t9\n";
            var outcome = PlannerCleanUpScript.ReadResult(batch, output);
            Assert.False(outcome.Completed);
            Assert.Equal("-- Unknown property", outcome.Aborted);
            Assert.Equal(new[] { 3, 4, 1, 2, 1, 1, 5, 6, 7, 8, 9 }, new[] { outcome.HelpersRenamed, outcome.ObjectsRenamed, outcome.HelpersMoved, outcome.Linked, outcome.PutOnLayers,
                outcome.MaxLayersMoved, outcome.MaxLayersRenamed, outcome.MaxLayersCreated, outcome.HelpersCreated, outcome.HelpersPutOnLayers, outcome.IdsWritten });
            Assert.Equal(batch.NodeNames[0] + " is no longer in the scene, so it was not changed", outcome.Failures[0]);
            Assert.Equal(rename.OldName + " could not be renamed to " + rename.NewName, outcome.Failures[1]);
            Assert.Equal(link.Name + " was not linked under " + link.ToName + ": 3ds Max reported an error: -- Runtime error: no", outcome.Failures[2]);
            Assert.Equal("the Max layer " + layer.OldName + " could not become " + layer.NewName + " and is left as __t_1 - rename it by hand", outcome.Failures[3]);
            // No Planner ID in this batch: a line about one it does not have is read as an unknown item.
            Assert.Equal("an item of the clean-up was not carried out (stamp 1, error): bad", outcome.Failures[4]);
            Assert.Equal("an item of the clean-up was not carried out (mystery 9, gone)", outcome.Failures[5]);
            var text = outcome.Describe(result, null, true);
            Assert.StartsWith("3ds Max did not report back, so it is not known how far the clean-up got.\r\nThe clean-up stopped part-way, so some changes may be missing: -- Unknown property\r\n", text);
            Assert.Contains("6 Max layers created, 7 helpers created, 3 helpers renamed", text);
            Assert.Contains("8 helpers put on their Max layers, 9 helpers given their Planner IDs", text);

            // The printed form of the string, quoted and escaped, reads the same.
            var quoted = "\"DONE\\t1\\t2\\t0\\t0\\t0\\t0\\t3\\t0\\t0\\t0\\t4\\n" + PlannerCleanUpScript.EndMarker + "\\n\"";
            var read = PlannerCleanUpScript.ReadResult(batch, quoted);
            Assert.True(read.Completed);
            Assert.Equal(2, read.ObjectsRenamed);
            Assert.Equal(4, read.IdsWritten);
        }

        [Fact]
        public void EveryMaxLayerStepTheBatchSaysFailedIsSaidPlainly()
        {
            // The Max layer steps' failures, each way the batch reports one (the steps were Fix phasing names' before Clean up's).
            var scene = EveryKind();
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
            var result = plan.Resolve(Answers(plan, scene));
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            Assert.NotEmpty(batch.MaxLayerMoves);
            Assert.NotEmpty(batch.MaxLayerRenames);
            var move = batch.MaxLayerMoves[0];
            var layer = batch.MaxLayerRenames[0];
            var output = "FAIL\tlayermove\t1\tgone\n"
                + "FAIL\tlayermove\t1\trefused\n"
                + "FAIL\tlayermove\t1\terror\tno parent\n"
                + "FAIL\tlayer\t1\tgone\n"
                + "FAIL\tlayer\t1\trefused\n"
                + "FAIL\tlayer\t1\ttaken\n"
                + "FAIL\tlayer\t1\terror\tno name\n"
                + "DONE\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\n" + PlannerCleanUpScript.EndMarker + "\n";
            var outcome = PlannerCleanUpScript.ReadResult(batch, output);
            Assert.True(outcome.Completed);
            Assert.Equal(new[]
            {
                "the Max layer " + move.Name + " was not moved under " + move.NewParentName + ": one of them is no longer in the scene",
                "the Max layer " + move.Name + " could not be moved under " + move.NewParentName,
                "the Max layer " + move.Name + " was not moved under " + move.NewParentName + ": 3ds Max reported an error: no parent",
                "the Max layer " + layer.OldName + " is no longer in the scene",
                "the Max layer " + layer.OldName + " could not be renamed; it keeps its name",
                "the Max layer " + layer.OldName + " could not become " + layer.NewName + ": that name is taken; it keeps its name",
                "the Max layer " + layer.OldName + " was not renamed to " + layer.NewName + ": 3ds Max reported an error: no name"
            }, outcome.Failures.ToArray());
        }

        [Fact]
        public void TheReportSaysWhatIsLeftAfterwards()
        {
            var scene = EveryKind();
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes());
            // No answers: the closing _RM and the stray keep their old names.
            var result = plan.Resolve(null);
            Assert.Equal(2, result.Open);
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            var run = CleanUpScriptRun.Run(batch.Script, scene.Snapshot);
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems);
            var after = PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes());
            var text = outcome.Describe(result, after, false);
            Assert.Contains("2 objects keep old Ph/St names; the Planner will not schedule them.", text);
            Assert.Contains("Read again from the scene: nothing is left to change; the scene reads as the new names; 2 listed objects still wait for an answer (Clean up scene graph\u2026 lists them).", text);

            // An empty result: nothing to do, and nothing done.
            var empty = PlannerCleanUpScript.Build(plan, new PlannerCleanUpResult(), "__t_");
            MaxScriptText.AssertBalanced(empty.Script);
            Assert.Empty(MaxScriptText.ArrayItems(empty.Script, "bHandles"));
            var none = PlannerCleanUpScript.ReadResult(empty, "DONE\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\n" + PlannerCleanUpScript.EndMarker + "\n");
            Assert.StartsWith("Nothing was changed.", none.Describe(null, null, false));
        }

        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }
}
