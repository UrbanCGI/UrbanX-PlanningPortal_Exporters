using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    public enum NamingSeverity
    {
        Note = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class NamingIssue
    {
        public NamingSeverity Severity;
        /// <summary>The object or group the message is about.</summary>
        public string Subject;
        public string Message;

        public override string ToString()
        {
            return Severity.ToString().ToUpperInvariant() + ": " + Message;
        }
    }

    /// <summary>What the validator needs to know about one node that is about to be exported.</summary>
    public sealed class SceneNodeInfo
    {
        public string Name;
        /// <summary>True for geometry; false for the group / dummy nodes that objects are parented under.</summary>
        public bool IsMesh;
        /// <summary>Names of the node's ancestors, nearest parent first. Empty at the scene root.</summary>
        public IList<string> Ancestors = new List<string>();
        /// <summary>The 3ds Max layer the node sits on, when known. Only used to word hints.</summary>
        public string LayerName;
    }

    public sealed class NamingReport
    {
        public readonly List<NamingIssue> Issues = new List<NamingIssue>();
        /// <summary>Objects that carry a Ph/St tag.</summary>
        public int TaggedObjects;
        /// <summary>Distinct group names that read as an order number plus dates / TBC.</summary>
        public int DatedGroups;
        /// <summary>Tagged objects that are not inside any dated group.</summary>
        public int UnfiledObjects;

        public int Errors { get { return Issues.Count(i => i.Severity == NamingSeverity.Error); } }
        public int Warnings { get { return Issues.Count(i => i.Severity == NamingSeverity.Warning); } }
        public int Notes { get { return Issues.Count(i => i.Severity == NamingSeverity.Note); } }

        public string Summary()
        {
            var text = string.Format(CultureInfo.InvariantCulture,
                "Planner naming check: {0} tagged object(s) in {1} dated group(s)", TaggedObjects, DatedGroups);
            if (UnfiledObjects > 0)
            {
                text += string.Format(CultureInfo.InvariantCulture, ", {0} unfiled", UnfiledObjects);
            }
            text += string.Format(CultureInfo.InvariantCulture, "; {0} error(s), {1} warning(s), {2} note(s).", Errors, Warnings, Notes);
            if (Errors == 0 && Warnings == 0)
            {
                text += " No naming problems found.";
            }
            return text;
        }
    }

    /// <summary>
    /// Checks the nodes of an export against the Planner naming convention and explains, in the artist's
    /// terms, what the Planner will make of each slip. Pure logic: the 3ds Max side only collects
    /// <see cref="SceneNodeInfo"/> records, so the rules are unit-testable without Max.
    ///
    /// Severity guide:
    ///   Error   - the Planner cannot read the name at all, or two exported objects share a name.
    ///   Warning - the name reads, but the Planner will schedule it differently from what was meant
    ///             (unfiled, undated, stage mismatch, empty description, case-only twins).
    ///   Note    - cosmetic or advisory (zero padding, untagged object inside a dated group, odd years).
    /// </summary>
    public static class PlannerNamingValidator
    {
        // A name that was clearly meant to start with a Ph tag but does not parse ("Ph1_S03_..", "PH 1_St01..").
        private static readonly Regex LooksTagged = new Regex(@"^\s*ph\s*[-_ ]?\s*\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // A second Ph tag hiding inside the description ("..._Ph1_S03_RM", "..._Ph2_St00_RM_extra").
        private static readonly Regex StrayTrail = new Regex(@"_Ph\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static NamingReport Validate(IEnumerable<SceneNodeInfo> nodes, DateTime today)
        {
            var report = new NamingReport();
            var all = (nodes ?? Enumerable.Empty<SceneNodeInfo>()).Where(n => n != null && n.Name != null).ToList();
            foreach (var node in all)
            {
                if (node.Ancestors == null)
                {
                    node.Ancestors = new List<string>();
                }
            }

            // Group candidates: every non-mesh node plus every ancestor name (a group may only be known through its children).
            var groupNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in all)
            {
                if (!node.IsMesh)
                {
                    groupNames.Add(node.Name);
                }
                foreach (var ancestor in node.Ancestors)
                {
                    if (ancestor != null)
                    {
                        groupNames.Add(ancestor);
                    }
                }
            }
            var datedGroups = new Dictionary<string, PlannerLayerName>(StringComparer.Ordinal);
            foreach (var groupName in groupNames)
            {
                var parsed = PlannerNaming.ParseLayerName(groupName);
                if (parsed != null)
                {
                    datedGroups[groupName] = parsed;
                }
            }
            report.DatedGroups = datedGroups.Count;

            CheckGroups(datedGroups, today, report);
            foreach (var mesh in all.Where(n => n.IsMesh))
            {
                CheckMesh(mesh, datedGroups, report);
            }
            CheckDuplicates(all, report);

            // Errors first so the log leads with what actually blocks the Planner. OrderBy is stable.
            var ordered = report.Issues.OrderByDescending(i => (int)i.Severity).ToList();
            report.Issues.Clear();
            report.Issues.AddRange(ordered);
            return report;
        }

        private static void CheckMesh(SceneNodeInfo mesh, Dictionary<string, PlannerLayerName> datedGroups, NamingReport report)
        {
            var name = mesh.Name;
            var parsed = PlannerNaming.ParseMeshName(name);

            string datedGroupName = null;
            PlannerLayerName datedGroup = null;
            foreach (var ancestor in mesh.Ancestors)
            {
                if (ancestor != null && datedGroups.TryGetValue(ancestor, out datedGroup))
                {
                    datedGroupName = ancestor;
                    break;
                }
                datedGroup = null;
            }

            if (parsed == null)
            {
                if (LooksTagged.IsMatch(name))
                {
                    Add(report, NamingSeverity.Error, name, string.Format(
                        "'{0}': the leading tag is malformed. Objects are tagged Ph<n>_St<nn>_IN|RM_<description>, e.g. Ph1_St03_IN_Sheet_Pile_1.", name));
                }
                else if (datedGroup != null)
                {
                    Add(report, NamingSeverity.Note, name, string.Format(
                        "'{0}' sits inside the dated group '{1}' but carries no Ph/St tag, so the Planner will not schedule it.", name, datedGroupName));
                }
                return;
            }

            report.TaggedObjects++;

            if (parsed.Trail == null && StrayTrail.IsMatch(parsed.Description))
            {
                Add(report, NamingSeverity.Error, name, string.Format(
                    "'{0}': the text after the description looks like a second Ph/St tag but does not read as one. A trailing tag must be _Ph<n>_St<nn>_IN|RM at the very end of the name (notes go after a space).", name));
            }

            if (parsed.Description.Length == 0)
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}': no description after the tag, so the Planner has to name the activity after its group.", name));
            }
            else if (parsed.Description.StartsWith("_", StringComparison.Ordinal) || parsed.Description.EndsWith("_", StringComparison.Ordinal))
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}': the description '{1}' has a stray underscore at its edge.", name, parsed.Description));
            }

            if (NeedsPadding(parsed.Lead) || NeedsPadding(parsed.Trail))
            {
                Add(report, NamingSeverity.Note, name, string.Format(
                    "'{0}': write stage numbers with two digits (St01, St06) so names read and sort consistently.", name));
            }

            if (datedGroup == null)
            {
                report.UnfiledObjects++;
                var message = string.Format("'{0}' is not inside a dated group, so the Planner lists it as unfiled and cannot schedule it.", name);
                if (mesh.Ancestors.Count > 0)
                {
                    message += string.Format(" Its group '{0}' needs an order number and dates: N_<Activity>_DD-MM-YY[_DD-MM-YY], or _TBC while the dates are unknown.", mesh.Ancestors[0]);
                }
                else
                {
                    message += " Group it under a node named N_<Activity>_DD-MM-YY[_DD-MM-YY] (or _TBC while the dates are unknown).";
                }
                if (!string.IsNullOrEmpty(mesh.LayerName) && PlannerNaming.ParseLayerName(mesh.LayerName) != null)
                {
                    message += string.Format(" It sits on the 3ds Max layer '{0}', but the export reads groups, not layers.", mesh.LayerName);
                }
                Add(report, NamingSeverity.Warning, name, message);
            }
            else
            {
                bool leadMatches = parsed.Lead.Stage == datedGroup.Stage;
                bool trailMatches = parsed.Trail != null && parsed.Trail.Stage == datedGroup.Stage;
                if (!leadMatches && !trailMatches)
                {
                    Add(report, NamingSeverity.Warning, name, string.Format(
                        "'{0}': neither tag matches the stage of its group '{1}' (St{2:00}), so the group cannot own this object's work and the Planner falls back to the leading tag. Check the St numbers, or move the object to the group for its stage.",
                        name, datedGroupName, datedGroup.Stage));
                }
            }
        }

        private static void CheckGroups(Dictionary<string, PlannerLayerName> datedGroups, DateTime today, NamingReport report)
        {
            foreach (var entry in datedGroups.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var name = entry.Key;
                var parsed = entry.Value;
                foreach (var issue in parsed.Issues)
                {
                    if (issue.StartsWith("unreadable date", StringComparison.Ordinal))
                    {
                        Add(report, NamingSeverity.Error, name, string.Format("Group '{0}': {1}. Dates are written DD-MM-YY, e.g. 17-08-26.", name, issue));
                    }
                    else if (issue.StartsWith("finish", StringComparison.Ordinal))
                    {
                        Add(report, NamingSeverity.Error, name, string.Format("Group '{0}': {1}. Check the year and the order of the two dates.", name, issue));
                    }
                    else if (issue == "no dates")
                    {
                        Add(report, NamingSeverity.Warning, name, string.Format("Group '{0}' has neither dates nor TBC, so the Planner treats it as TBC. Add _DD-MM-YY[_DD-MM-YY], or _TBC while the dates are unknown.", name));
                    }
                    else if (issue == "no activity name")
                    {
                        Add(report, NamingSeverity.Warning, name, string.Format("Group '{0}' has an order number but no activity name.", name));
                    }
                    else
                    {
                        Add(report, NamingSeverity.Warning, name, string.Format("Group '{0}': {1}.", name, issue));
                    }
                }

                var dates = new List<DateTime>();
                if (parsed.Start.HasValue) dates.Add(parsed.Start.Value);
                if (parsed.End.HasValue && parsed.End != parsed.Start) dates.Add(parsed.End.Value);
                foreach (var date in dates)
                {
                    if (date.Year < today.Year - 3 || date.Year > today.Year + 10)
                    {
                        Add(report, NamingSeverity.Note, name, string.Format(CultureInfo.InvariantCulture,
                            "Group '{0}': the date {1:dd-MM-yy} falls in {2}; check the year.", name, date, date.Year));
                    }
                }
            }

            // Labels that differ only by letter case are almost always the same work typed twice.
            foreach (var twins in datedGroups.Values.GroupBy(p => p.Label.ToLowerInvariant()))
            {
                var labels = twins.Select(p => p.Label).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal).ToList();
                if (labels.Count > 1)
                {
                    Add(report, NamingSeverity.Warning, labels[0], string.Format(
                        "Groups {0} differ only by letter case; the Planner treats them as different activities. Use one spelling if they are the same work.",
                        string.Join(" and ", labels.Select(l => "'" + l + "'").ToArray())));
                }
            }
        }

        private static void CheckDuplicates(List<SceneNodeInfo> all, NamingReport report)
        {
            foreach (var duplicate in all.Where(n => n.IsMesh).GroupBy(n => n.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                Add(report, NamingSeverity.Error, duplicate.Key, string.Format(
                    "'{0}' is the name of {1} objects. Every exported object needs a unique name: the Planner binds activities to objects by name.", duplicate.Key, duplicate.Count()));
            }
            var distinctGroups = all.Where(n => !n.IsMesh).GroupBy(n => n.Name, StringComparer.Ordinal).Where(g => g.Count() > 1);
            foreach (var duplicate in distinctGroups)
            {
                Add(report, NamingSeverity.Warning, duplicate.Key, string.Format(
                    "'{0}' is the name of {1} groups; give each group its own name.", duplicate.Key, duplicate.Count()));
            }
        }

        private static bool NeedsPadding(PlannerMeshSegment segment)
        {
            return segment != null && segment.Stage < 10 && segment.StageToken.Length < 2;
        }

        private static void Add(NamingReport report, NamingSeverity severity, string subject, string message)
        {
            report.Issues.Add(new NamingIssue { Severity = severity, Subject = subject, Message = message });
        }
    }
}
