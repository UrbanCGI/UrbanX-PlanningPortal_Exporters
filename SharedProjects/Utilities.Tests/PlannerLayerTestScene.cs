using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Builds scenes for the layer builder tests: Max layers, helpers, objects.</summary>
    internal sealed class TestScene
    {
        public readonly PlannerSceneSnapshot Snapshot = new PlannerSceneSnapshot();
        private long next = 100;

        public TestScene()
        {
        }

        public TestScene(PlannerSceneSnapshot snapshot)
        {
            Snapshot = snapshot;
            next = snapshot.Nodes.Count == 0 ? 100 : snapshot.Nodes.Max(n => n.Key) + 1;
        }

        public PhasingMaxLayer Layer(string name, string parent = null)
        {
            var layer = new PhasingMaxLayer { Name = name, ParentName = parent };
            Snapshot.Layers.Add(layer);
            return layer;
        }

        public PlannerSceneNode Node(string name, PlannerSceneNode parent, string layer, bool helper, string plannerId = null)
        {
            var node = new PlannerSceneNode
            {
                Key = next++,
                Name = name,
                ParentKey = parent != null ? parent.Key : (long?)null,
                ChildIndex = Snapshot.Nodes.Count(n => n.ParentKey == (parent != null ? parent.Key : (long?)null)),
                IsHelper = helper,
                LayerName = layer,
                PlannerId = plannerId
            };
            if (parent != null)
            {
                parent.HasChildren = true;
            }
            Snapshot.Nodes.Add(node);
            return node;
        }

        public PlannerSceneNode Helper(string name, PlannerSceneNode parent, string layer = null, string plannerId = null)
        {
            return Node(name, parent, layer ?? name, true, plannerId);
        }

        public PlannerSceneNode Object(string name, PlannerSceneNode parent, string layer)
        {
            return Node(name, parent, layer, false);
        }

        /// <summary>A layer and its helper of the same name, the helper on the layer, nested under the parent's.</summary>
        public PlannerSceneNode Unit(string name, string parentLayer, PlannerSceneNode parentHelper, string plannerId = null)
        {
            Layer(name, parentLayer);
            return Helper(name, parentHelper, name, plannerId);
        }

        public PlannerSceneNode Find(string name)
        {
            return Snapshot.Nodes.Single(n => n.Name == name);
        }
    }

    /// <summary>The Planner's layer list written out as the Planner writes it (BOM, quotes, CRLF).</summary>
    internal static class TestCsv
    {
        public const string Header = "Planner ID,Parent ID,Layer,Kind,ID,Phase,Stage,Sub-stage,Name,Start,Finish,TBC,Order";

        /// <summary>A row: Planner ID, Parent ID, Layer, Kind; the rest worked out from the name.</summary>
        public static string[] Row(string id, string parent, string layer, string kind = null)
        {
            var parsed = PlannerNaming.ParseLayerName(layer);
            var key = PlannerLayerIdKey.FromName(layer);
            var isLayer = kind != null ? kind == "Layer" : key != null;
            return new[]
            {
                id, parent ?? string.Empty, layer, isLayer ? "Layer" : "Group",
                key != null ? key.ToString() : string.Empty,
                key != null && key.Phase.HasValue ? key.Phase.Value.ToString() : string.Empty,
                key != null ? key.Stage.ToString() : string.Empty,
                key != null && key.SubStage.HasValue ? key.SubStage.Value.ToString() : string.Empty,
                parsed != null ? parsed.Name : layer,
                parsed != null && parsed.Start.HasValue ? parsed.Start.Value.ToString("yyyy-MM-dd") : string.Empty,
                parsed != null && parsed.End.HasValue ? parsed.End.Value.ToString("yyyy-MM-dd") : string.Empty,
                parsed != null && parsed.Tbc && isLayer ? "Yes" : string.Empty,
                string.Empty
            };
        }

        public static string Write(IEnumerable<string[]> rows)
        {
            var text = new StringBuilder();
            text.Append('\uFEFF').Append(Header).Append("\r\n");
            int order = 0;
            foreach (var row in rows)
            {
                order++;
                var cells = row.ToArray();
                if (cells.Length >= 13 && cells[12].Length == 0)
                {
                    cells[12] = order.ToString();
                }
                text.Append(string.Join(",", cells.Select(PlannerReview.Field).ToArray())).Append("\r\n");
            }
            return text.ToString();
        }

        public static PlannerLayerList List(params string[][] rows)
        {
            return PlannerLayerList.Parse(Write(rows));
        }
    }

    /// <summary>Carries out a plan on a scene the way the MAXScript batch does, checking what 3ds Max would refuse.</summary>
    internal static class TestSimulator
    {
        private sealed class SimLayer
        {
            public string Name;
            public SimLayer Parent;
        }

        public static PlannerSceneSnapshot Apply(PlannerSceneSnapshot before, PlannerLayerPlan plan)
        {
            var layers = before.Layers.Select(l => new SimLayer { Name = l.Name }).ToList();
            Func<string, SimLayer> layerNamed = name => name == null ? null : layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < before.Layers.Count; i++)
            {
                layers[i].Parent = layerNamed(before.Layers[i].ParentName);
            }
            var nodes = before.Nodes.Select(Clone).ToList();
            var nodeLayer = nodes.ToDictionary(n => n.Key, n => layerNamed(n.LayerName));

            var entries = new SimLayer[plan.BuildLayers.Count];
            foreach (var entry in plan.BuildLayers.Where(l => !l.Create))
            {
                entries[entry.Index] = layerNamed(entry.CurrentName);
                Assert.True(entries[entry.Index] != null, "the layer " + entry.CurrentName + " is not in the scene");
            }
            // Renames in two steps: only the end state must have no clash.
            foreach (var entry in plan.BuildLayers.Where(l => l.Rename))
            {
                entries[entry.Index].Name = entry.FinalName;
            }
            AssertUnique(layers.Select(l => l.Name));
            foreach (var entry in plan.BuildLayers.Where(l => l.Create))
            {
                Assert.True(layerNamed(entry.FinalName) == null, "a layer called " + entry.FinalName + " is already there");
                var layer = new SimLayer { Name = entry.FinalName, Parent = entry.ParentIndex.HasValue ? entries[entry.ParentIndex.Value] : null };
                layers.Add(layer);
                entries[entry.Index] = layer;
            }
            foreach (var entry in plan.BuildLayers.Where(l => !l.Create && l.Move))
            {
                var layer = entries[entry.Index];
                var parent = entries[entry.ParentIndex.Value];
                for (var up = parent; up != null; up = up.Parent)
                {
                    Assert.True(up != layer, "moving the layer " + layer.Name + " under " + parent.Name + " makes a circle");
                }
                layer.Parent = parent;
            }

            var helperEntries = new PlannerSceneNode[plan.BuildHelpers.Count];
            foreach (var entry in plan.BuildHelpers.Where(h => !h.Create))
            {
                var node = nodes.Single(n => n.Key == entry.Key.Value);
                Assert.Equal(entry.CurrentName, node.Name);
                helperEntries[entry.Index] = node;
            }
            foreach (var entry in plan.BuildHelpers.Where(h => h.Rename))
            {
                helperEntries[entry.Index].Name = entry.FinalName;
            }
            long next = nodes.Count == 0 ? 1 : nodes.Max(n => n.Key) + 1;
            foreach (var entry in plan.BuildHelpers.Where(h => h.Create))
            {
                var parent = entry.ParentIndex.HasValue ? helperEntries[entry.ParentIndex.Value] : null;
                Assert.True(!entry.ParentIndex.HasValue || parent != null, "the parent of the new helper " + entry.FinalName + " is not there yet");
                var node = new PlannerSceneNode
                {
                    Key = next++,
                    Name = entry.FinalName,
                    ParentKey = parent != null ? parent.Key : (long?)null,
                    ChildIndex = 10000 + (int)next,
                    IsHelper = true,
                    PlannerId = entry.StampId
                };
                nodes.Add(node);
                nodeLayer[node.Key] = entries[entry.LayerIndex];
                helperEntries[entry.Index] = node;
            }
            foreach (var entry in plan.BuildHelpers.Where(h => !h.Create && h.PutOnLayer))
            {
                nodeLayer[helperEntries[entry.Index].Key] = entries[entry.LayerIndex];
            }
            var byKey = nodes.ToDictionary(n => n.Key);
            foreach (var entry in plan.BuildHelpers.Where(h => !h.Create && h.Move))
            {
                var node = helperEntries[entry.Index];
                var parent = helperEntries[entry.ParentIndex.Value];
                for (var up = parent; up != null; up = up.ParentKey.HasValue ? byKey[up.ParentKey.Value] : null)
                {
                    Assert.True(up != node, "linking " + node.Name + " under " + parent.Name + " makes a circle");
                }
                node.ParentKey = parent.Key;
            }
            foreach (var entry in plan.BuildHelpers.Where(h => !h.Create && h.StampId != null))
            {
                helperEntries[entry.Index].PlannerId = entry.StampId;
            }

            var after = new PlannerSceneSnapshot();
            foreach (var layer in layers)
            {
                after.Layers.Add(new PhasingMaxLayer { Name = layer.Name, ParentName = layer.Parent != null ? layer.Parent.Name : null });
            }
            foreach (var node in nodes)
            {
                SimLayer layer;
                nodeLayer.TryGetValue(node.Key, out layer);
                node.LayerName = layer != null ? layer.Name : null;
                node.HasChildren = nodes.Any(n => n.ParentKey == node.Key);
                after.Nodes.Add(node);
            }
            return after;
        }

        public static PlannerSceneSnapshot Apply(PlannerSceneSnapshot before, PlannerLinkPlan plan)
        {
            var nodes = before.Nodes.Select(Clone).ToList();
            var byKey = nodes.ToDictionary(n => n.Key);
            foreach (var link in plan.Links)
            {
                var node = byKey[link.Key];
                for (var up = byKey[link.ToKey]; up != null; up = up.ParentKey.HasValue ? byKey[up.ParentKey.Value] : null)
                {
                    Assert.True(up != node, "linking " + node.Name + " under " + link.ToName + " makes a circle");
                }
                node.ParentKey = link.ToKey;
            }
            var after = new PlannerSceneSnapshot();
            after.Layers.AddRange(before.Layers.Select(l => new PhasingMaxLayer { Name = l.Name, ParentName = l.ParentName }));
            foreach (var node in nodes)
            {
                node.HasChildren = nodes.Any(n => n.ParentKey == node.Key);
                after.Nodes.Add(node);
            }
            return after;
        }

        private static PlannerSceneNode Clone(PlannerSceneNode n)
        {
            return new PlannerSceneNode
            {
                Key = n.Key,
                Name = n.Name,
                ParentKey = n.ParentKey,
                ChildIndex = n.ChildIndex,
                HasChildren = n.HasChildren,
                IsGroupHead = n.IsGroupHead,
                IsGroupMember = n.IsGroupMember,
                IsHelper = n.IsHelper,
                IsShape = n.IsShape,
                LayerName = n.LayerName,
                PlannerId = n.PlannerId
            };
        }

        private static void AssertUnique(IEnumerable<string> names)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                Assert.True(seen.Add(name), "two layers would be called " + name);
            }
        }

        /// <summary>Each layer of the root's tree with its parent's name, "name &lt; parent", sorted.</summary>
        public static List<string> LayerTree(PlannerSceneSnapshot scene, string root)
        {
            var result = new List<string>();
            var pending = new Stack<string>(new[] { root });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.Count > 0)
            {
                var name = pending.Pop();
                foreach (var child in scene.Layers.Where(l => string.Equals(l.ParentName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (seen.Add(child.Name))
                    {
                        result.Add(child.Name + " < " + (string.Equals(name, root, StringComparison.OrdinalIgnoreCase) ? "ROOT" : name));
                        pending.Push(child.Name);
                    }
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>Each helper under the root node (helpers only) with its parent's name, "name &lt; parent", sorted.</summary>
        public static List<string> HelperTree(PlannerSceneSnapshot scene, long rootKey, Func<PlannerSceneNode, bool> include)
        {
            var byKey = scene.Nodes.ToDictionary(n => n.Key);
            var result = new List<string>();
            var pending = new Stack<long>(new[] { rootKey });
            while (pending.Count > 0)
            {
                var key = pending.Pop();
                foreach (var child in scene.Nodes.Where(n => n.ParentKey == key))
                {
                    if (!include(child))
                    {
                        continue;
                    }
                    result.Add(child.Name + " < " + (key == rootKey ? "ROOT" : byKey[key].Name));
                    pending.Push(child.Key);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
