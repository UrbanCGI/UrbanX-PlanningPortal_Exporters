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
        /// <summary>The node's id in the <see cref="PlannerScene"/> handed to the validator (the 3ds Max node handle), when known.</summary>
        public string Id;
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
        /// <summary>
        /// Legacy scenes: tagged objects that are not inside any dated group. Scenes with a phasing root: objects on
        /// a Planner layer that are not linked under its helper, plus tagged objects outside the root.
        /// </summary>
        public int UnfiledObjects;
        /// <summary>The phasing root the scene was read with (the coded scheme), or null for a legacy scene.</summary>
        public string RootLayerName;
        /// <summary>Coded scheme: the folder layers under the root.</summary>
        public int PlannerLayers;
        /// <summary>Coded scheme: exported objects on the root or a folder layer (the activities).</summary>
        public int PhasedObjects;

        public int Errors { get { return Issues.Count(i => i.Severity == NamingSeverity.Error); } }
        public int Warnings { get { return Issues.Count(i => i.Severity == NamingSeverity.Warning); } }
        public int Notes { get { return Issues.Count(i => i.Severity == NamingSeverity.Note); } }

        public string Summary()
        {
            string text;
            if (RootLayerName != null)
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    "Planner naming check: {0} object(s) on {1} Planner layer(s) under '{2}', {3} tagged", PhasedObjects, PlannerLayers, RootLayerName, TaggedObjects);
                if (UnfiledObjects > 0)
                {
                    text += string.Format(CultureInfo.InvariantCulture, ", {0} not filed under their layer", UnfiledObjects);
                }
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    "Planner naming check: {0} tagged object(s) in {1} dated group(s)", TaggedObjects, DatedGroups);
                if (UnfiledObjects > 0)
                {
                    text += string.Format(CultureInfo.InvariantCulture, ", {0} unfiled", UnfiledObjects);
                }
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
    ///             (unfiled, undated, stage mismatch, empty description, case-only twins), or the name
    ///             breaks the convention although it reads (a space anywhere in a tagged object's or a
    ///             dated group's name). Stray underscores at the edge of a description are trimmed by
    ///             the Planner and not reported.
    ///   Note    - cosmetic or advisory (zero padding, untagged object inside a dated group, odd years).
    ///
    /// A scene with a Work_Phasing root is read with the coded scheme instead (<see cref="Validate(IEnumerable{SceneNodeInfo}, PlannerScene, DateTime)"/>):
    /// the layer decides an object's folder, tags are optional, and what matters is that each object hangs under
    /// its layer's helper, since the exported hierarchy is all the Planner sees.
    /// </summary>
    public static class PlannerNamingValidator
    {
        // A name that was clearly meant to start with a Ph tag but does not parse ("Ph1_S03_..", "PH 1_St01..").
        private static readonly Regex LooksTagged = new Regex(@"^\s*ph\s*[-_ ]?\s*[0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // A second Ph tag hiding inside the description ("..._Ph1_S03_RM", "..._Ph2_St00_RM_extra").
        private static readonly Regex StrayTrail = new Regex(@"_Ph[0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Coded scheme: an install tag at the end of an install object's name, which no longer means anything.
        private static readonly Regex TrailingInstall = new Regex(@"_Ph[0-9]+_St[0-9]+(?:\.[0-9]+)?_IN$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private const int NamesListed = 5;

        /// <summary>
        /// Checks the export against the scene's layers. When <paramref name="scene"/> has a Work_Phasing root, the
        /// layers under it are read with the coded scheme (contract v1); otherwise, or without a scene, the legacy
        /// rules of <see cref="Validate(IEnumerable{SceneNodeInfo}, DateTime)"/> apply unchanged.
        /// </summary>
        public static NamingReport Validate(IEnumerable<SceneNodeInfo> nodes, PlannerScene scene, DateTime today)
        {
            var tree = scene != null ? PlannerLayerTree.Read(scene) : null;
            if (tree == null || tree.Root == null)
            {
                return Validate(nodes, today);
            }
            return ValidateCoded(nodes, tree);
        }

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

        // ---- coded scheme (a Work_Phasing root) ---------------------------------------------------------------------

        private static NamingReport ValidateCoded(IEnumerable<SceneNodeInfo> nodes, PlannerLayerTree tree)
        {
            var report = new NamingReport { RootLayerName = tree.Root.LayerName, PlannerLayers = tree.Folders.Count() };
            var all = (nodes ?? Enumerable.Empty<SceneNodeInfo>()).Where(n => n != null && n.Name != null).ToList();
            foreach (var node in all)
            {
                if (node.Ancestors == null)
                {
                    node.Ancestors = new List<string>();
                }
            }

            CheckPlannerLayers(tree, report);
            var outsideReported = CheckLayersOutsideRoot(tree, report);

            var codes = tree.FolderCodes();
            var helperByName = new Dictionary<string, PlannerLayerInfo>(StringComparer.Ordinal);
            foreach (var info in tree.Layers.Where(l => l.Helper != null))
            {
                if (!helperByName.ContainsKey(info.Helper.Name))
                {
                    helperByName[info.Helper.Name] = info;
                }
            }
            // Objects not filed under their own layer's helper: the ones Update layers links (loose, or under another
            // Planner helper), and the ones linked to some other object, which it deliberately leaves alone.
            var notFiled = new Dictionary<PlannerLayerInfo, List<string>>();
            var linkedElsewhere = new Dictionary<PlannerLayerInfo, List<string>>();
            var taggedOutside = new Dictionary<string, List<string>>(PlannerScene.LayerNameComparer);
            var contextFiled = new Dictionary<PlannerLayerInfo, List<string>>();

            foreach (var mesh in all.Where(n => n.IsMesh))
            {
                var sceneNode = mesh.Id != null ? tree.Scene.FindNode(mesh.Id) : null;
                var layerName = mesh.LayerName ?? (sceneNode != null ? sceneNode.LayerName : null);
                var layer = tree.Find(layerName);
                var parsed = PlannerCodes.ParseCodedObjectName(mesh.Name);
                if (layer == null)
                {
                    // Context. Linked under a Planner helper, the exported model files it under that folder anyway.
                    var filedContext = sceneNode != null ? tree.FiledUnder(sceneNode) : FiledUnderByName(mesh.Ancestors, helperByName);
                    if (filedContext != null)
                    {
                        Collect(contextFiled, filedContext, mesh.Name);
                        continue;
                    }
                    // A tag says it was probably meant to be phased; a layer already reported as a phasing layer
                    // outside the root covers its objects.
                    if (parsed.Tagged && (layerName == null || !outsideReported.Contains(layerName)))
                    {
                        Collect(taggedOutside, layerName ?? string.Empty, mesh.Name);
                    }
                    continue;
                }

                report.PhasedObjects++;
                if (parsed.Tagged)
                {
                    report.TaggedObjects++;
                }
                CheckCodedObject(mesh.Name, parsed, layer, codes, report);

                if (layer.Helper != null)
                {
                    var filed = sceneNode != null ? tree.FiledUnder(sceneNode) : FiledUnderByName(mesh.Ancestors, helperByName);
                    if (filed != layer)
                    {
                        Collect(UpdateLinks(tree, sceneNode, mesh, helperByName) ? notFiled : linkedElsewhere, layer, mesh.Name);
                    }
                }
            }

            foreach (var entry in notFiled)
            {
                report.UnfiledObjects += entry.Value.Count;
                Add(report, NamingSeverity.Warning, entry.Key.LayerName, string.Format(CultureInfo.InvariantCulture,
                    "{0} object(s) on the Planner layer '{1}' are not linked under its helper '{2}', so the exported model does not file them under this folder: {3}. Run Update layers in the Planner layers window to link them.",
                    entry.Value.Count, entry.Key.LayerName, entry.Key.Helper.Name, NameList(entry.Value)));
            }
            foreach (var entry in linkedElsewhere)
            {
                report.UnfiledObjects += entry.Value.Count;
                Add(report, NamingSeverity.Warning, entry.Key.LayerName, string.Format(CultureInfo.InvariantCulture,
                    "{0} object(s) on the Planner layer '{1}' are linked to objects that are not under its helper '{2}', so the exported model does not file them under this folder: {3}. Update layers leaves such links alone: link them (or the object they hang from) to '{2}' by hand.",
                    entry.Value.Count, entry.Key.LayerName, entry.Key.Helper.Name, NameList(entry.Value)));
            }
            foreach (var entry in contextFiled)
            {
                report.UnfiledObjects += entry.Value.Count;
                Add(report, NamingSeverity.Warning, entry.Value[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} object(s) on layers outside '{1}' are linked under the helper of '{2}', so the exported model files them under that folder and the Planner phases them: {3}. If they are context, unlink them; if they are phasing work, move them onto a Planner layer.",
                    entry.Value.Count, tree.Root.LayerName, entry.Key.LayerName, NameList(entry.Value)));
            }
            foreach (var entry in taggedOutside.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                report.UnfiledObjects += entry.Value.Count;
                Add(report, NamingSeverity.Warning, entry.Value[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} tagged object(s) on {1} sit outside '{2}', so the Planner treats them as context and does not phase them: {3}. Move them to their Planner layer under '{2}'.",
                    entry.Value.Count, entry.Key.Length > 0 ? "the layer '" + entry.Key + "'" : "no known layer", tree.Root.LayerName, NameList(entry.Value)));
            }

            CheckDuplicates(all, report);

            var ordered = report.Issues.OrderByDescending(i => (int)i.Severity).ToList();
            report.Issues.Clear();
            report.Issues.AddRange(ordered);
            return report;
        }

        private static void CheckPlannerLayers(PlannerLayerTree tree, NamingReport report)
        {
            var roots = tree.Scene.ChildLayers(null).Where(l => PlannerCodes.IsPhasingRoot(l.Name)).ToList();
            if (roots.Count > 1)
            {
                Add(report, NamingSeverity.Warning, tree.Root.LayerName, string.Format(CultureInfo.InvariantCulture,
                    "There are {0} phasing root layers ({1}); only '{2}' is read, everything under the others is context.",
                    roots.Count, string.Join(", ", roots.Select(r => "'" + r.Name + "'").ToArray()), tree.Root.LayerName));
            }
            foreach (var info in tree.Layers)
            {
                if (info.Helper == null)
                {
                    Add(report, NamingSeverity.Warning, info.LayerName, info.IsRoot
                        ? string.Format(CultureInfo.InvariantCulture, "The phasing root '{0}' has no helper yet, so the exported model has no root node for the Planner. Run Update layers (or Adopt existing layers) in the Planner layers window.", info.LayerName)
                        : string.Format(CultureInfo.InvariantCulture, "The Planner layer '{0}' has no helper yet, so the exported model has no folder node for it. Run Update layers in the Planner layers window.", info.LayerName));
                }
                if (info.LegacyName)
                {
                    Add(report, NamingSeverity.Warning, info.LayerName, string.Format(CultureInfo.InvariantCulture,
                        "The layer '{0}' under '{1}' still has an old dated name: it reads as code {2} and its dates are not read from the name any more. Run Adopt existing layers in the Planner layers window to convert it.",
                        info.LayerName, tree.Root.LayerName, info.Code ?? "(none)"));
                }
            }
            foreach (var duplicate in tree.Folders.Where(f => f.Code != null).GroupBy(f => f.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                Add(report, NamingSeverity.Warning, duplicate.Key, string.Format(CultureInfo.InvariantCulture,
                    "Code {0} is used by {1} Planner layers: {2}. Duplicate codes are allowed; check they are meant.",
                    duplicate.Key, duplicate.Count(), string.Join(", ", duplicate.Select(f => "'" + f.LayerName + "'").ToArray())));
            }
        }

        /// <summary>Warns about phasing layers (old dated layers, or layers whose helper carries Planner properties) outside the root; returns every layer covered by a warning.</summary>
        private static HashSet<string> CheckLayersOutsideRoot(PlannerLayerTree tree, NamingReport report)
        {
            var covered = new HashSet<string>(PlannerScene.LayerNameComparer);
            var phasing = new HashSet<string>(PlannerScene.LayerNameComparer);
            foreach (var layer in tree.Scene.Layers)
            {
                if (tree.Find(layer.Name) != null || PlannerCodes.IsPhasingRoot(layer.Name))
                {
                    continue;
                }
                var legacy = PlannerCodes.ParseLegacyLayer(layer.Name, null);
                var helper = tree.Scene.FindLayerHelper(layer.Name);
                if ((legacy != null && (legacy.IsGroup || legacy.HasSchedule)) || (helper != null && PlannerProps.MarksHelper(helper.Props)))
                {
                    phasing.Add(layer.Name);
                }
            }
            foreach (var layer in tree.Scene.Layers.Where(l => phasing.Contains(l.Name)))
            {
                // Only the top-most of nested phasing layers is reported; the ones inside it are counted.
                var nestedInReported = false;
                var guard = 0;
                for (var parent = tree.Scene.FindLayer(layer.ParentName); parent != null && guard < 10000; parent = tree.Scene.FindLayer(parent.ParentName), guard++)
                {
                    if (phasing.Contains(parent.Name))
                    {
                        nestedInReported = true;
                        break;
                    }
                }
                if (nestedInReported)
                {
                    continue;
                }
                var inside = tree.Scene.Descendants(layer.Name);
                covered.Add(layer.Name);
                foreach (var child in inside)
                {
                    covered.Add(child.Name);
                }
                var nested = inside.Count(l => phasing.Contains(l.Name));
                Add(report, NamingSeverity.Warning, layer.Name, string.Format(CultureInfo.InvariantCulture,
                    "The layer '{0}' looks like a phasing layer but sits outside '{1}'{2}, so its objects are exported as context and are not phased. Move it under '{1}' (Adopt existing layers does this for old dated layers).",
                    layer.Name, tree.Root.LayerName, nested > 0 ? string.Format(CultureInfo.InvariantCulture, " (with {0} more inside it)", nested) : string.Empty));
            }
            return covered;
        }

        private static void CheckCodedObject(string name, PlannerCodedObjectName parsed, PlannerLayerInfo layer, IList<string> codes, NamingReport report)
        {
            if (ContainsWhitespace(name))
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}' contains a space. Names must not contain spaces: use underscores, and keep notes out of the name.", name));
            }

            if (parsed.LeadCode == null && LooksTagged.IsMatch(name))
            {
                Add(report, NamingSeverity.Error, name, string.Format(
                    "'{0}': the leading tag is malformed. Tags are optional, but a tag reads Ph<n>_St<nn>[.<m>]_IN|RM_<description>, e.g. Ph1_St03_IN_Sheet_Pile_1, or just IN_ / RM_ in front of the description.", name));
            }
            else if (parsed.RemovalCode == null && parsed.IgnoredInstallCode == null)
            {
                if (parsed.Type == PlannerTaskType.Install && TrailingInstall.IsMatch(PlannerCodes.StripDuplicateSuffix(name)))
                {
                    Add(report, NamingSeverity.Warning, name, string.Format(
                        "'{0}' ends in an install tag, which the Planner does not read: the layer decides when it is installed. Only a removal goes at the end of a name (_Ph<n>_St<nn>_RM or _RM_<code>).", name));
                }
                else if (StrayTrail.IsMatch(parsed.Description))
                {
                    Add(report, NamingSeverity.Error, name, string.Format(
                        "'{0}': the end of the name looks like a removal tag but does not read as one. A removal ends the name as _Ph<n>_St<nn>[.<m>]_RM or _RM_<code>, e.g. _Ph6_St05_RM or _RM_06-05.", name));
                }
            }
            if (parsed.IgnoredInstallCode != null)
            {
                Add(report, NamingSeverity.Note, name, string.Format(
                    "'{0}' ends in an install tag ({1}) after its removal; that re-install is ignored for now.", name, parsed.IgnoredInstallCode));
            }

            if (parsed.Tagged && parsed.Description.Length == 0)
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}': no description after the tag, so the Planner has to name the activity after its folder.", name));
            }

            var legacy = PlannerNaming.ParseMeshName(name);
            if (legacy != null && (NeedsPadding(legacy.Lead) || NeedsPadding(legacy.Trail)))
            {
                Add(report, NamingSeverity.Note, name, string.Format(
                    "'{0}': write stage numbers with two digits (St01, St06, St06.1) so names read and sort consistently.", name));
            }

            if (parsed.LeadCode != null && layer.Code != null && !PlannerCodes.CodeCovers(layer.Code, parsed.LeadCode))
            {
                Add(report, NamingSeverity.Warning, name, string.Format(CultureInfo.InvariantCulture,
                    "'{0}': its tag reads {1} but it sits on '{2}' ({3}). The layer decides the folder; check the tag, or move the object to the right layer.",
                    name, parsed.LeadCode, layer.LayerName, layer.Code));
            }
            if (parsed.RemovalCode != null && PlannerCodes.ResolveRemovalTarget(parsed.RemovalCode, codes) < 0)
            {
                Add(report, NamingSeverity.Warning, name, string.Format(CultureInfo.InvariantCulture,
                    "'{0}': no Planner layer has the code {1}, so the Planner cannot place its removal and parks it as TBC.", name, parsed.RemovalCode));
            }
        }

        private static PlannerLayerInfo FiledUnderByName(IList<string> ancestors, Dictionary<string, PlannerLayerInfo> helperByName)
        {
            foreach (var ancestor in ancestors)
            {
                PlannerLayerInfo info;
                if (ancestor != null && helperByName.TryGetValue(ancestor, out info))
                {
                    return info;
                }
            }
            return null;
        }

        private static void Collect<TKey>(Dictionary<TKey, List<string>> map, TKey key, string name)
        {
            List<string> names;
            if (!map.TryGetValue(key, out names))
            {
                map[key] = names = new List<string>();
            }
            names.Add(name);
        }

        /// <summary>
        /// True when Update layers links the object to its layer's helper: it hangs loose or directly under another
        /// Planner helper. An object linked to some other object is left alone (and needs a hand).
        /// </summary>
        private static bool UpdateLinks(PlannerLayerTree tree, PlannerSceneNode sceneNode, SceneNodeInfo mesh, Dictionary<string, PlannerLayerInfo> helperByName)
        {
            if (sceneNode != null)
            {
                return sceneNode.ParentId == null || tree.Scene.FindNode(sceneNode.ParentId) == null || tree.IsPlannerHelper(sceneNode.ParentId);
            }
            return mesh.Ancestors.Count == 0 || (mesh.Ancestors[0] != null && helperByName.ContainsKey(mesh.Ancestors[0]));
        }

        private static string NameList(List<string> names)
        {
            var listed = string.Join(", ", names.Take(NamesListed).Select(n => "'" + n + "'").ToArray());
            return names.Count > NamesListed
                ? listed + string.Format(CultureInfo.InvariantCulture, " and {0} more", names.Count - NamesListed)
                : listed;
        }

        // ---- legacy scheme ------------------------------------------------------------------------------------------

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
                        "'{0}': the leading tag is malformed. Objects are tagged Ph<n>_St<nn>_IN|RM_<description>, with an optional sub-stage St<nn>.<m>, e.g. Ph1_St03_IN_Sheet_Pile_1 or Ph1_St03.2_IN_Sheet_Pile_1.", name));
                }
                else if (datedGroup != null)
                {
                    Add(report, NamingSeverity.Note, name, string.Format(
                        "'{0}' sits inside the dated group '{1}' but carries no Ph/St tag, so the Planner will not schedule it.", name, datedGroupName));
                }
                return;
            }

            report.TaggedObjects++;

            // The convention allows no spaces at all, even though the parser tolerates them (a space after the order
            // number, spaces in a name, a note after the trailing tag) so that real slips still read.
            if (ContainsWhitespace(name))
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}' contains a space. Names must not contain spaces: use underscores, and keep notes out of the name.", name));
            }

            if (parsed.Trail == null && StrayTrail.IsMatch(parsed.Description))
            {
                Add(report, NamingSeverity.Error, name, string.Format(
                    "'{0}': the text after the description looks like a second Ph/St tag but does not read as one. A trailing tag must be _Ph<n>_St<nn>[.<m>]_IN|RM at the very end of the name (notes go after a space).", name));
            }

            // A stray underscore at the description's edge ("..._C_") is trimmed by the Planner and is deliberately
            // not reported: on real models it drowned the log (Arjun, 2026-09-09).
            if (parsed.Description.Length == 0)
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}': no description after the tag, so the Planner has to name the activity after its group.", name));
            }

            if (NeedsPadding(parsed.Lead) || NeedsPadding(parsed.Trail))
            {
                Add(report, NamingSeverity.Note, name, string.Format(
                    "'{0}': write stage numbers with two digits (St01, St06, St06.1) so names read and sort consistently.", name));
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
                bool leadMatches = MatchesStage(parsed.Lead, datedGroup);
                bool trailMatches = parsed.Trail != null && MatchesStage(parsed.Trail, datedGroup);
                if (!leadMatches && !trailMatches)
                {
                    Add(report, NamingSeverity.Warning, name, string.Format(
                        "'{0}': neither tag matches the stage of its group '{1}' ({2}), so the group cannot own this object's work and the Planner falls back to the leading tag. Check the St numbers, or move the object to the group for its stage.",
                        name, datedGroupName, PlannerNaming.StageText(datedGroup.Stage, datedGroup.SubStage)));
                }
            }
        }

        private static void CheckGroups(Dictionary<string, PlannerLayerName> datedGroups, DateTime today, NamingReport report)
        {
            foreach (var entry in datedGroups.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var name = entry.Key;
                var parsed = entry.Value;
                if (ContainsWhitespace(name))
                {
                    Add(report, NamingSeverity.Warning, name, string.Format(
                        "Group '{0}' contains a space. Group names must not contain spaces: write N_<Activity>_DD-MM-YY[_DD-MM-YY] with underscores only.", name));
                }
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

        // A group owns the object's work when the stage matches and the sub-stages agree (a plain group number
        // covers the whole stage, and a plain St<n> sits under a sub-numbered group as it always has).
        private static bool MatchesStage(PlannerMeshSegment segment, PlannerLayerName group)
        {
            return PlannerNaming.SameStage(segment.Stage, segment.SubStage, group.Stage, group.SubStage);
        }

        // "St6" and "St6.1" want a zero; the digits before any sub-stage are what is padded.
        private static bool NeedsPadding(PlannerMeshSegment segment)
        {
            if (segment == null || segment.Stage >= 10)
            {
                return false;
            }
            int dot = segment.StageToken.IndexOf('.');
            return (dot < 0 ? segment.StageToken.Length : dot) < 2;
        }

        private static bool ContainsWhitespace(string text)
        {
            return text.Any(char.IsWhiteSpace);
        }

        private static void Add(NamingReport report, NamingSeverity severity, string subject, string message)
        {
            report.Issues.Add(new NamingIssue { Severity = severity, Subject = subject, Message = message });
        }
    }
}
