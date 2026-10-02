using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: reads the JSON a modeller puts in a node's "extras" user property. Newtonsoft leaves nested
    /// objects as <see cref="JObject"/>, which the Planner extras merge cannot see into, so a user "planner" block
    /// (a key the Planner owns) is turned into plain dictionaries: the exporter's guid and folder values then merge
    /// into it and the modeller's other keys stay. No 3ds Max calls, so the unit tests compile this file directly.
    /// </summary>
    static class PlannerUserExtras
    {
        /// <summary>The extras as a dictionary; <paramref name="hasPlannerKey"/> says the JSON used the reserved "planner" key.</summary>
        public static Dictionary<string, object> Parse(string json, out bool hasPlannerKey)
        {
            var extras = JObject.Parse(json).ToObject<Dictionary<string, object>>();
            object planner;
            hasPlannerKey = extras.TryGetValue(PlannerNodeExtras.Key, out planner);
            var block = planner as JObject;
            if (block != null)
            {
                extras[PlannerNodeExtras.Key] = ToDictionary(block);
            }
            return extras;
        }

        private static Dictionary<string, object> ToDictionary(JObject json)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in json.Properties())
            {
                var inner = property.Value as JObject;
                result[property.Name] = inner != null ? (object)ToDictionary(inner) : property.Value;
            }
            return result;
        }
    }
}
