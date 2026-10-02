using System.Collections.Generic;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the planner_* user properties of a node (contract section 5), read and written through the
    /// exporter's usual user-property helpers. The rules about what they mean live in the shared, Max-free
    /// <see cref="PlannerProps"/> and <see cref="PlannerNodeExtras"/>.
    /// </summary>
    static class PlannerNodeProps
    {
        /// <summary>The planner_* properties the node carries; empty when it carries none.</summary>
        public static Dictionary<string, string> GetPlannerProps(this IINode node)
        {
            var props = new Dictionary<string, string>();
            if (node == null)
            {
                return props;
            }
            foreach (var name in PlannerProps.All)
            {
                var value = node.GetStringProperty(name, null);
                if (!string.IsNullOrEmpty(value))
                {
                    props[name] = value;
                }
            }
            return props;
        }

        /// <summary>Sets a planner_* property ("=" is not allowed in values and is replaced), or deletes it for a null value.</summary>
        public static void SetPlannerProp(this IINode node, string name, string value)
        {
            value = PlannerProps.SanitiseValue(value);
            if (string.IsNullOrEmpty(value))
            {
                node.DeleteProperty(name);
            }
            else
            {
                node.SetStringProperty(name, value);
            }
        }

        /// <summary>
        /// The node's glTF-only extras: <c>{ planner: { guid, ... } }</c> with the node's fixed GUID (the one the
        /// exporter keeps per node and uses as the Babylon id) and, on a Planner layer helper, its folder.
        /// </summary>
        public static Dictionary<string, object> PlannerGltfExtras(this IINode node)
        {
            return PlannerNodeExtras.ForNode(node.GetGuid().ToString(), node.GetPlannerProps());
        }
    }
}
