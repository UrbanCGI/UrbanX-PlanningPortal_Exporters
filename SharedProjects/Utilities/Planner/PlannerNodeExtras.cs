using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>
    /// The 3ds Max user properties a Planner layer helper carries (contract section 5), persisted in the .max.
    /// Values must not contain "=", which the Max user-property buffer uses as its separator.
    /// </summary>
    public static class PlannerProps
    {
        public const string FolderId = "planner_folderId";
        public const string Code = "planner_code";
        public const string Name = "planner_name";
        public const string Root = "planner_root";
        public const string LegacyStart = "planner_legacyStart";
        public const string LegacyFinish = "planner_legacyFinish";
        public const string LegacyTbc = "planner_legacyTbc";

        public static readonly string[] All = { FolderId, Code, Name, Root, LegacyStart, LegacyFinish, LegacyTbc };
        public static readonly string[] Legacy = { LegacyStart, LegacyFinish, LegacyTbc };

        public const string True = "true";
        public const string False = "false";

        /// <summary>
        /// A value safe for the user-property buffer: "=" becomes "_", line breaks become spaces. A literal "%20"
        /// becomes a space too: the exporter stores spaces as "%20" and reads every "%20" back as a space, so the
        /// value is written as it will read back and a second run sees no change.
        /// </summary>
        public static string SanitiseValue(string value)
        {
            if (value == null)
            {
                return null;
            }
            return value.Replace("%20", " ").Replace("=", "_").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>The value of <paramref name="name"/>, or null when absent or blank.</summary>
        public static string Get(IDictionary<string, string> props, string name)
        {
            string value;
            if (props == null || !props.TryGetValue(name, out value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            return value.Trim();
        }

        public static bool IsTrue(string value)
        {
            return value != null && string.Equals(value.Trim(), True, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the props mark a Planner layer helper (a folder id, a code, a name or the root flag).</summary>
        public static bool MarksHelper(IDictionary<string, string> props)
        {
            return Get(props, FolderId) != null || Get(props, Code) != null || Get(props, Name) != null || IsTrue(Get(props, Root));
        }
    }

    /// <summary>
    /// Builds the <c>extras.planner</c> block the exporter writes on every glTF node (contract section 5) and merges
    /// it into whatever extras the node already has. Babylon's glTF loader hands node extras to the Planner as
    /// <c>node.metadata.gltf.extras</c>.
    /// </summary>
    public static class PlannerNodeExtras
    {
        public const string Key = "planner";

        /// <summary>
        /// <c>{ guid, folderId?, code?, name?, root?, legacy? }</c> for one node. Absent values are omitted; legacy
        /// dates are dropped once a Planner folder id is set (a schedule has been applied) or when they are not
        /// "YYYY-MM-DD".
        /// </summary>
        public static Dictionary<string, object> Build(string guid, IDictionary<string, string> props)
        {
            var planner = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(guid))
            {
                planner["guid"] = guid.ToLowerInvariant();
            }
            var folderId = PlannerProps.Get(props, PlannerProps.FolderId);
            if (folderId != null)
            {
                planner["folderId"] = folderId;
            }
            var code = PlannerProps.Get(props, PlannerProps.Code);
            if (code != null)
            {
                planner["code"] = code;
            }
            var name = PlannerProps.Get(props, PlannerProps.Name);
            if (name != null)
            {
                planner["name"] = name;
            }
            if (PlannerProps.IsTrue(PlannerProps.Get(props, PlannerProps.Root)))
            {
                planner["root"] = true;
            }
            if (folderId == null)
            {
                var legacy = new Dictionary<string, object>(StringComparer.Ordinal);
                var start = IsoDayOrNull(PlannerProps.Get(props, PlannerProps.LegacyStart));
                var finish = IsoDayOrNull(PlannerProps.Get(props, PlannerProps.LegacyFinish));
                var tbc = PlannerProps.Get(props, PlannerProps.LegacyTbc);
                if (start != null)
                {
                    legacy["start"] = start;
                }
                if (finish != null)
                {
                    legacy["finish"] = finish;
                }
                if (tbc != null)
                {
                    legacy["tbc"] = PlannerProps.IsTrue(tbc);
                }
                if (legacy.Count > 0)
                {
                    planner["legacy"] = legacy;
                }
            }
            return planner;
        }

        /// <summary>The node's glTF-only extras: <c>{ planner: Build(...) }</c>, or null when there is nothing to write.</summary>
        public static Dictionary<string, object> ForNode(string guid, IDictionary<string, string> props)
        {
            var planner = Build(guid, props);
            if (planner.Count == 0)
            {
                return null;
            }
            return new Dictionary<string, object>(StringComparer.Ordinal) { { Key, planner } };
        }

        /// <summary>
        /// <paramref name="extras"/> with <paramref name="additions"/> merged in, as a NEW dictionary: the input may
        /// be a node's Babylon metadata and stays untouched. Keys of both are kept; on a clash the addition wins,
        /// except that two dictionaries under the same key are merged the same way (so a user-defined "planner"
        /// block keeps its other keys).
        /// </summary>
        public static Dictionary<string, object> Merge(Dictionary<string, object> extras, Dictionary<string, object> additions)
        {
            if (additions == null || additions.Count == 0)
            {
                return extras;
            }
            var merged = extras != null ? new Dictionary<string, object>(extras) : new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var entry in additions)
            {
                object existing;
                var existingMap = merged.TryGetValue(entry.Key, out existing) ? existing as IDictionary<string, object> : null;
                var additionMap = entry.Value as Dictionary<string, object>;
                if (existingMap != null && additionMap != null)
                {
                    merged[entry.Key] = Merge(existingMap.ToDictionary(e => e.Key, e => e.Value), additionMap);
                }
                else
                {
                    merged[entry.Key] = entry.Value;
                }
            }
            return merged;
        }

        private static string IsoDayOrNull(string text)
        {
            DateTime day;
            return text != null && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                ? text
                : null;
        }
    }
}
