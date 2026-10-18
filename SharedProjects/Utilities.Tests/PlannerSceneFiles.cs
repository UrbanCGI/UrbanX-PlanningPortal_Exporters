using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Utilities.Planner;

namespace Utilities.Tests
{
    /// <summary>
    /// The scenes of the parity files as the tests read them: the Planner harness's node lists, a GLB's node tree, and the
    /// time zones its results were made in.
    /// </summary>
    internal static class PlannerSceneFiles
    {
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
    }
}
