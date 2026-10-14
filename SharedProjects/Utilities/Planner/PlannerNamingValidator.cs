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
    /// The rules follow the convention as Fix phasing names (<see cref="PhasingNameFixer"/>) writes it, so a scene
    /// the fixer leaves clean reads clean here. Inside the phasing hierarchy (a dated group directly under a phase
    /// group _&lt;n&gt;_&lt;name&gt; of the phasing top node, or directly under that top node) a group is a layer and its
    /// name reads as the fixer reads it, &lt;PP&gt;_&lt;SS&gt;[-&lt;split&gt;]_&lt;name&gt;: an object belongs to it when a tag has
    /// the layer's phase and stage. Any other dated group is read as the Planner reads it: a name that starts with two
    /// numbers is phase then stage (an object belongs to it when a tag has both), one that starts with one number is a
    /// stage in the phase the Planner reads off its objects: the phase most of their leading tags with its stage carry,
    /// or, only when no leading tag has its stage, most of their trailing tags with it (an object belongs to it when a
    /// tag has the stage; one whose tag has the stage in another phase is reported when the Planner dates it from another
    /// layer or when only its trailing tag has the stage, naming the objects that set the phase when leading tags did).
    ///
    /// Severity guide:
    ///   Error   - the Planner cannot read the name at all, or two exported objects share a name.
    ///   Warning - the name reads, but the Planner will schedule it differently from what was meant
    ///             (unfiled, undated, phase or stage mismatch, a tag whose phase and stage no layer has, gathered
    ///             in one warning for removal tags and one for install tags, empty description, case-only twins),
    ///             or the name breaks the convention although it reads (a space anywhere in a tagged object's or a
    ///             dated group's name; a sub-stage written the old way with a full stop, in object tags
    ///             or group names, one warning for each with the count and some of the names). Stray
    ///             underscores at the edge of a description are trimmed by the Planner and not reported.
    ///   Note    - cosmetic or advisory (zero padding, untagged object inside a dated group, odd years, a tag whose
    ///             phase and stage only an undated layer has).
    /// </summary>
    public static class PlannerNamingValidator
    {
        /// <summary>Names listed at most in a warning that gathers several names.</summary>
        public const int NamesShown = 10;

        // A name that was clearly meant to start with a Ph tag but does not parse ("Ph1_S03_..", "PH 1_St01..").
        private static readonly Regex LooksTagged = new Regex(@"^\s*ph\s*[-_ ]?\s*[0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // A second Ph tag hiding inside the description ("..._Ph1_S03_RM", "..._Ph2_St00_RM_extra").
        private static readonly Regex StrayTrail = new Regex(@"_Ph[0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Outside the phasing layers, a group's full stop that the parser takes as a sub-stage (PlannerLayerName.Legacy)
        // is reported only after a two-digit stage, as the convention writes stages ("05.2_...", "01_04.2_..."), and not
        // before "_x_": a measurement in that place ("2.4_High_Hoarding", "01_2.4_High_Hoarding_...",
        // "1.8_x_2.4_Heras_Panel", "01_10.8_x_2.4_...") reads the same to the parser but is not a sub-stage.
        private static readonly Regex GroupSubStageFullStop = new Regex(@"^(?:[0-9]+[_ ]+)?[0-9]{2,}\.[0-9]+(?=_|\z)(?!_[xX]_)", RegexOptions.CultureInvariant);
        // A phasing layer's ID with a full stop after the phase ("06.01.2_..."), which the Planner cannot read.
        private static readonly Regex PhaseFullStop = new Regex(@"^[0-9]+\.", RegexOptions.CultureInvariant);

        public static NamingReport Validate(IEnumerable<SceneNodeInfo> nodes, DateTime today)
        {
            return Validate(nodes, today, null);
        }

        /// <param name="nodes">The nodes about to be exported.</param>
        /// <param name="today">For the check on odd years.</param>
        /// <param name="phasingTop">
        /// The top node Fix phasing names corrects (<see cref="PhasingFixPlan.TopName"/>), or null when that is not known:
        /// then the first top-level node with "phasing" in its name that the nodes show. Only the layers under that one are
        /// said to be corrected by the fixer; those under any other top node with "phasing" in its name are renamed by hand.
        /// An empty name: the fixer corrects none.
        /// </param>
        public static NamingReport Validate(IEnumerable<SceneNodeInfo> nodes, DateTime today, string phasingTop)
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
            var phasing = PhasingLayers(all, groupNames, phasingTop);

            CheckGroups(datedGroups, phasing, today, report);
            var legacyObjects = new FullStops();
            var filed = new List<FiledObject>();
            foreach (var mesh in all.Where(n => n.IsMesh))
            {
                var placed = CheckMesh(mesh, datedGroups, phasing, report, legacyObjects);
                if (placed != null)
                {
                    filed.Add(placed);
                }
            }
            AddFullStopWarning(report, legacyObjects, "object", "Ph1_St04.2_IN_...", "Ph1_St04-2_IN_...", phasing.FixerTop);
            CheckStagesWithoutLayer(filed, datedGroups, phasing, report);
            CheckDuplicates(all, report);

            // Errors first so the log leads with what actually blocks the Planner. OrderBy is stable.
            var ordered = report.Issues.OrderByDescending(i => (int)i.Severity).ToList();
            report.Issues.Clear();
            report.Issues.AddRange(ordered);
            return report;
        }

        /// <summary>
        /// The groups that are layers of the phasing hierarchy, read as Fix phasing names reads them: a name that reads as
        /// a layer ID, directly under a phase group of a top-level node with "phasing" in its name, or directly under such a
        /// top node (where the fixer moves it into its group). Fix phasing names corrects only the layers of one top node
        /// (see <see cref="Validate(IEnumerable{SceneNodeInfo}, DateTime, string)"/>). A layer ID the Planner cannot read as
        /// a dated group (a full stop after the phase, "06.01.2_...") still counts: the fixer corrects it.
        /// </summary>
        private static PhasingTree PhasingLayers(List<SceneNodeInfo> all, IEnumerable<string> groupNames, string phasingTop)
        {
            // Where each group sits, from the ancestor chains (nearest first): its parent's name, or the scene root.
            var parents = new Dictionary<string, string>(StringComparer.Ordinal);
            var roots = new List<string>();
            var atRoot = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in all)
            {
                var chain = node.Ancestors.Where(a => a != null).ToList();
                if (!node.IsMesh)
                {
                    Place(node.Name, chain.Count > 0 ? chain[0] : null, parents, atRoot, roots);
                }
                for (int i = 0; i < chain.Count; i++)
                {
                    Place(chain[i], i + 1 < chain.Count ? chain[i + 1] : null, parents, atRoot, roots);
                }
            }
            Func<string, bool> isTop = name => name != null && atRoot.Contains(name) && name.IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0;

            var tree = new PhasingTree { FixerTop = phasingTop ?? roots.FirstOrDefault(isTop) };
            if (tree.FixerTop != null && !isTop(tree.FixerTop))
            {
                tree.FixerTop = null;
            }
            foreach (var groupName in groupNames)
            {
                var id = PhasingNameFixer.ReadLayerId(groupName);
                string parent, grandparent;
                int phase;
                if (id == null || !parents.TryGetValue(groupName, out parent))
                {
                    continue;
                }
                string top = null;
                if (isTop(parent))
                {
                    top = parent;
                }
                else if (PhasingNameFixer.TryReadPhaseGroup(parent, out phase) && parents.TryGetValue(parent, out grandparent) && isTop(grandparent))
                {
                    top = grandparent;
                }
                if (top != null)
                {
                    tree.Layers[groupName] = id;
                    tree.TopOf[groupName] = top;
                }
            }
            return tree;
        }

        // The first place a name is seen in wins.
        private static void Place(string name, string parent, Dictionary<string, string> parents, HashSet<string> atRoot, List<string> roots)
        {
            if (parents.ContainsKey(name) || atRoot.Contains(name))
            {
                return;
            }
            if (parent == null)
            {
                atRoot.Add(name);
                roots.Add(name);
            }
            else
            {
                parents[name] = parent;
            }
        }

        /// <returns>The object and its dated group when it carries a tag and sits in one, else null.</returns>
        private static FiledObject CheckMesh(SceneNodeInfo mesh, Dictionary<string, PlannerLayerName> datedGroups, PhasingTree phasing,
            NamingReport report, FullStops legacyObjects)
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
            // Fix phasing names corrects an object directly in a layer of its top node (also one whose ID the Planner cannot
            // read as a dated group); one linked under another node inside the layer (a Max group, an object), or in a layer
            // of another top node with "phasing" in its name, it leaves as it is.
            PhasingLayerId layerId = null;
            bool inPhasingLayer = datedGroupName != null && phasing.Layers.TryGetValue(datedGroupName, out layerId);
            bool underFixerTop = inPhasingLayer && phasing.Corrects(datedGroupName);
            bool fixerCorrects = mesh.Ancestors.Count > 0 && mesh.Ancestors[0] != null && phasing.Corrects(mesh.Ancestors[0]);

            if (parsed == null)
            {
                if (LooksTagged.IsMatch(name))
                {
                    Add(report, NamingSeverity.Error, name, string.Format(
                        "'{0}': the leading tag is malformed. Objects are tagged Ph<n>_St<nn>_IN|RM_<description>, with an optional sub-stage St<nn>-<m>, e.g. Ph1_St03_IN_Sheet_Pile_1 or Ph1_St03-2_IN_Sheet_Pile_1.", name));
                }
                else if (datedGroup != null)
                {
                    Add(report, NamingSeverity.Note, name, string.Format(
                        "'{0}' sits inside the dated group '{1}' but carries no Ph/St tag, so the Planner will not schedule it.", name, datedGroupName));
                }
                return null;
            }

            report.TaggedObjects++;
            if (parsed.Legacy)
            {
                legacyObjects.Add(name, fixerCorrects);
            }

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
                    "'{0}': the text after the description looks like a second Ph/St tag but does not read as one. A trailing tag must be _Ph<n>_St<nn>[-<m>]_IN|RM at the very end of the name (notes go after a space).", name));
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
                    "'{0}': write stage numbers with two digits (St01, St06, St06-1) so names read and sort consistently.", name));
            }

            if (datedGroup == null)
            {
                report.UnfiledObjects++;
                var message = string.Format("'{0}' is not inside a dated group, so the Planner lists it as unfiled and cannot schedule it.", name);
                // A layer of the phasing hierarchy whose ID only Fix phasing names reads ("06.01.2_..."): it has its dates, and
                // what the Planner cannot read is the ID.
                var unreadLayer = mesh.Ancestors.FirstOrDefault(a => a != null && phasing.Layers.ContainsKey(a));
                if (unreadLayer != null)
                {
                    message += string.Format(" Its layer '{0}' has a phase and stage ID the Planner cannot read{1}: {2}", unreadLayer,
                        PhaseFullStop.IsMatch(unreadLayer) ? " (a full stop after the phase number)" : string.Empty,
                        phasing.Corrects(unreadLayer)
                            ? "Fix phasing names\u2026 on the exporter window corrects it."
                            : "rename it by hand to " + LayerIdText(phasing.Layers[unreadLayer]) + "_<name>_DD-MM-YY[_DD-MM-YY] ("
                              + (phasing.FixerTop != null ? "Fix phasing names\u2026 corrects only the layers under '" + phasing.FixerTop + "'" : "Fix phasing names\u2026 does not correct it")
                              + ").");
                }
                else if (mesh.Ancestors.Count > 0)
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
                return null;
            }

            var placed = new FiledObject { Name = name, Parsed = parsed, Group = datedGroupName };
            if (inPhasingLayer)
            {
                // A layer of the phasing hierarchy: its ID gives the phase and stage, as Fix phasing names reads it.
                if (!MatchesLayer(parsed.Lead, layerId) && !(parsed.Trail != null && MatchesLayer(parsed.Trail, layerId)))
                {
                    var mismatch = string.Format(CultureInfo.InvariantCulture, "'{0}': neither tag matches its layer '{1}' (Ph{2}_{3}). ",
                        name, datedGroupName, layerId.Phase, PlannerNaming.StageText(layerId.Stage, layerId.Split));
                    string advice;
                    if (fixerCorrects)
                    {
                        advice = "Fix phasing names\u2026 gives the object its layer's tag; check the Ph and St numbers, or move the object to the layer for its stage.";
                    }
                    else if (!underFixerTop)
                    {
                        advice = string.Format(phasing.FixerTop != null
                                ? "The layer is under '{0}', and Fix phasing names\u2026 corrects only '{1}': correct the Ph and St numbers by hand, or move the object to the layer for its stage."
                                : "The layer is under '{0}', which Fix phasing names\u2026 does not correct: correct the Ph and St numbers by hand, or move the object to the layer for its stage.",
                            phasing.TopOf[datedGroupName], phasing.FixerTop);
                    }
                    else
                    {
                        advice = string.Format("It is linked under '{0}' inside the layer, and Fix phasing names\u2026 leaves such objects as they are: correct the Ph and St numbers by hand, or move the object to the layer for its stage.", mesh.Ancestors[0]);
                    }
                    Add(report, NamingSeverity.Warning, name, mismatch + advice);
                }
                return placed;
            }
            bool leadMatches = MatchesGroup(parsed.Lead, datedGroup);
            bool trailMatches = parsed.Trail != null && MatchesGroup(parsed.Trail, datedGroup);
            if (!leadMatches && !trailMatches)
            {
                // A group named phase then stage ("02_01-2_...") owns the work of that phase and stage; a group named with one
                // number owns its stage, in the phase its objects' tags of that stage give (GroupPhase), which only the whole
                // group shows: a tag of its stage in another phase is looked at after every object is read
                // (CheckStagesWithoutLayer).
                Add(report, NamingSeverity.Warning, name, datedGroup.Phase.HasValue
                    ? string.Format(CultureInfo.InvariantCulture,
                        "'{0}': neither tag matches the phase and stage of its group '{1}' (Ph{2}_{3}), so the group cannot own this object's work and the Planner falls back to the leading tag. Check the Ph and St numbers, or move the object to the group for its stage.",
                        name, datedGroupName, datedGroup.Phase.Value, PlannerNaming.StageText(datedGroup.Stage, datedGroup.SubStage))
                    : string.Format(
                        "'{0}': neither tag matches the stage of its group '{1}' ({2}), so the group cannot own this object's work and the Planner falls back to the leading tag. Check the St numbers, or move the object to the group for its stage.",
                        name, datedGroupName, PlannerNaming.StageText(datedGroup.Stage, datedGroup.SubStage)));
            }
            return placed;
        }

        /// <summary>
        /// What only the whole scene shows: which layer the Planner dates each tag's work from. A dated group is a layer to
        /// the Planner when it holds a tagged object, and it can date another stage's work only when it has a start date.
        ///
        /// One warning for the objects with a removal tag, and one for those with an install tag, whose phase and stage no
        /// layer in the file has: the Planner reads the tag's work as another layer's, finds none, and cannot date it (the
        /// Check lines "removal tag ... names a stage that has no layer in this file" of Fix phasing names). A tag is
        /// looked at only where the Planner looks for another layer: not the tag its own group owns, and not the leading tag
        /// when the group owns neither (the group's dates are its dates, and the mismatch is reported on its own). An
        /// install in phase 0 is there before the works begin, which the Planner reads as the project's start. A tag whose
        /// only layer has no dates yet (TBC) gets a note instead, one for each kind: the Planner cannot date it either, but
        /// that is the layer's TBC. A group with that phase and stage that holds no tagged object is not a layer to the
        /// Planner, and is named in the warning.
        ///
        /// A warning for each object in a group named with one number whose tags have its stage but not its phase (the
        /// phase <see cref="GroupPhase(PlannerLayerName, IEnumerable{PlannerMeshName})"/> reads off its objects), so that
        /// the group owns neither tag's work: when the leading tag has the group's stage and another dated layer has its
        /// phase and stage (the Planner dates the object from that layer, though it stays in its group's folder), and
        /// whenever only the trailing tag has the group's stage (the group does not own that work, the removal as a rule;
        /// with no dated layer for the leading tag's phase and stage, the Planner takes the leading tag's work as the
        /// group's own). Each warning says how the group got its phase and, when leading tags set it, names the objects
        /// that did: when they are fewer than the objects warned in the group, the slip is more likely theirs.
        /// </summary>
        private static void CheckStagesWithoutLayer(List<FiledObject> filed, Dictionary<string, PlannerLayerName> datedGroups, PhasingTree phasing, NamingReport report)
        {
            var members = filed.ToLookup(f => f.Group, StringComparer.Ordinal);
            // The groups the Planner reads as layers that own a phase and stage, and the groups that would own one but hold
            // no tagged object.
            var layers = new List<StageOwner>();
            var empty = new List<StageOwner>();
            var groupStages = new Dictionary<string, StageOwner>(StringComparer.Ordinal);
            foreach (var entry in datedGroups.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var own = OwnerOf(entry.Key, entry.Value, phasing, members[entry.Key]);
                groupStages[entry.Key] = own;
                if (own.Phase.HasValue)
                {
                    (members[entry.Key].Any() ? layers : empty).Add(own);
                }
            }

            var removals = new MissingLayers();
            var installs = new MissingLayers();
            var undatedRemovals = new MissingLayers();
            var undatedInstalls = new MissingLayers();
            var phaseWarnings = new List<PhaseWarning>();
            foreach (var item in filed)
            {
                var group = groupStages[item.Group];
                var lead = item.Parsed.Lead;
                var trail = item.Parsed.Trail;
                bool leadOwn = group.Owns(lead);
                bool trailOwn = trail != null && group.Owns(trail);
                var elsewhere = new List<PlannerMeshSegment>();
                if (leadOwn || trailOwn)
                {
                    if (!leadOwn) elsewhere.Add(lead);
                }
                else
                {
                    var warning = CheckPhaseOfOneNumberGroup(item, datedGroups[item.Group], group, layers, phasing);
                    if (warning != null)
                    {
                        phaseWarnings.Add(warning);
                    }
                }
                if (trail != null && !trailOwn)
                {
                    elsewhere.Add(trail);
                }
                foreach (var segment in elsewhere)
                {
                    bool removal = segment.Type == PlannerTaskType.Dismantle;
                    if (DatingLayer(segment, layers) != null || (!removal && segment.Phase == 0))
                    {
                        continue;
                    }
                    var undated = layers.Where(o => !o.Start.HasValue && o.Covers(segment)).ToList();
                    if (undated.Count > 0)
                    {
                        (removal ? undatedRemovals : undatedInstalls).Add(item.Name, segment, undated);
                    }
                    else
                    {
                        (removal ? removals : installs).Add(item.Name, segment, empty.Where(o => o.Covers(segment)));
                    }
                }
            }
            // The advice weighs the objects that set a group's phase against those warned in it for another phase, so it
            // is written once every object has been looked at; the warnings keep the order the objects were read in.
            var warnedIn = phaseWarnings.GroupBy(w => w.Owner.Group, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var warning in phaseWarnings)
            {
                Add(report, NamingSeverity.Warning, warning.Subject, warning.Text + " " + PhaseAdvice(warning.Owner, warnedIn[warning.Owner.Group]));
            }
            AddMissingLayerWarning(report, removals, "a removal", "after the end of the works", "comes out at");
            AddMissingLayerWarning(report, installs, "an install", "at the start of the project", "goes in at");
            AddUndatedLayerNote(report, undatedRemovals, "a removal", "after the end of the works");
            AddUndatedLayerNote(report, undatedInstalls, "an install", "at the start of the project");
        }

        // An object in a group named with one number, outside the phasing layers, with a tag of the group's stage but in
        // another phase than the group's (GroupPhase): the stage-only check in CheckMesh passes it, but the group owns
        // neither tag's work. The Planner then dates the leading tag's work from the dated layer with its phase and stage,
        // or, with none, takes it as the group's own work, and dates the trailing tag's work like any other stage's
        // (CheckStagesWithoutLayer).
        //  - The leading tag has the group's stage: with another dated layer for it, the Planner dates the object from that
        //    layer, which is warned; with none, it keeps the object on the group's dates, as meant: nothing to say.
        //  - Only the trailing tag has the group's stage: the group does not own that work (the removal, as a rule), which
        //    is warned, saying where the Planner dates it and where it puts the leading tag's work. With no dated layer for
        //    the leading tag, that work is the group's own, on the group's dates, which nothing else reports.
        // Returns the warning without its advice (PhaseAdvice), or null when there is nothing to say.
        private static PhaseWarning CheckPhaseOfOneNumberGroup(FiledObject item, PlannerLayerName datedGroup, StageOwner group, List<StageOwner> layers,
            PhasingTree phasing)
        {
            var lead = item.Parsed.Lead;
            var trail = item.Parsed.Trail;
            if (phasing.Layers.ContainsKey(item.Group) || datedGroup.Phase.HasValue || !group.Phase.HasValue)
            {
                return null;
            }
            var groupText = string.Format(CultureInfo.InvariantCulture, "Ph{0}_{1}: {2}", group.Phase.Value, PlannerNaming.StageText(datedGroup.Stage, datedGroup.SubStage), PhaseRule(group));
            var leadLayer = DatingLayer(lead, layers);
            if (MatchesGroup(lead, datedGroup))
            {
                if (leadLayer == null)
                {
                    return null;
                }
                return new PhaseWarning
                {
                    Subject = item.Name,
                    Owner = group,
                    Text = string.Format(CultureInfo.InvariantCulture,
                        "'{0}': neither tag matches the phase and stage of its group '{1}' ({2}), so the group cannot own this object's work: the Planner dates it from '{3}' ({4}), the layer for its leading tag, though it stays in the group's folder.",
                        item.Name, item.Group, groupText, leadLayer.Group, TagText(leadLayer))
                };
            }
            if (trail == null || !MatchesGroup(trail, datedGroup))
            {
                // Neither tag has the group's stage: CheckMesh has warned.
                return null;
            }
            var trailKind = KindOf(trail);
            var trailLayer = DatingLayer(trail, layers);
            var trailText = trailLayer != null
                ? string.Format(CultureInfo.InvariantCulture, "the Planner dates the {0} from '{1}' ({2})", trailKind, trailLayer.Group, TagText(trailLayer))
                : string.Format(CultureInfo.InvariantCulture, "no dated layer has {0} either, so the Planner puts the {1} {2}", TagText(trail), trailKind, WithoutLayer(trail));
            string leadText;
            if (leadLayer != null)
            {
                leadText = string.Format(CultureInfo.InvariantCulture, "the leading tag's work ({0}) is dated from '{1}', its own layer, as usual", TagText(lead), leadLayer.Group);
            }
            else if (!datedGroup.Tbc)
            {
                leadText = string.Format(CultureInfo.InvariantCulture, "as no dated layer has {0}, the Planner spreads the leading tag's work over the group's dates as the group's own work, rather than putting it {1}", TagText(lead), WithoutLayer(lead));
            }
            else if (datedGroup.Start.HasValue)
            {
                // A TBC group: everything in it is TBC, its own work included.
                leadText = string.Format(CultureInfo.InvariantCulture, "as no dated layer has {0}, the Planner spreads the leading tag's work over the group's dates as the group's own work, marked TBC like the rest of the group", TagText(lead));
            }
            else
            {
                // A group with no dates yet: its own work sits at the start of the project, TBC like the rest of the group.
                leadText = string.Format(CultureInfo.InvariantCulture, "as no dated layer has {0}, the Planner takes the leading tag's work as the group's own work, and as the group has no dates yet, puts it at the start of the project, marked TBC like the rest of the group", TagText(lead));
            }
            return new PhaseWarning
            {
                Subject = item.Name,
                Owner = group,
                Text = string.Format(CultureInfo.InvariantCulture,
                    "'{0}': its {1} tag {2} has the stage of its group '{3}' but not its phase ({4}), so the group does not own the {1}: {5}; {6}.",
                    item.Name, trailKind, TagText(trail), item.Group, groupText, trailText, leadText)
            };
        }

        // How a group named with one number got its phase, as GroupPhase reads it: the rule, then which objects set it here.
        // The objects are named when leading tags set it (one of them may be the slip); when trailing tags set it, the object
        // warned is among the fewer, and only the counts are given.
        private static string PhaseRule(StageOwner group)
        {
            var text = "named with one number, the group takes the phase most of its objects' leading tags with its stage carry, and looks at the trailing tags only when no leading tag has its stage; here ";
            string portion;
            if (group.PhaseSetters.Count == group.PhaseCounted)
            {
                portion = group.PhaseCounted == 1 ? "the only object" : "every object";
            }
            else
            {
                portion = string.Format(CultureInfo.InvariantCulture, "{0} of the {1} objects", group.PhaseSetters.Count, group.PhaseCounted);
            }
            if (group.PhaseFromLeading)
            {
                text += "it comes from " + Quoted(group.PhaseSetters) + ", " + portion + " whose leading tag has its stage";
            }
            else
            {
                text += "no leading tag has its stage, and it comes from " + portion + " whose trailing tag has it";
            }
            if (group.PhaseTie)
            {
                text += ", the lower phase on a tie";
            }
            return text;
        }

        // What to do about an object warned for a tag of its group's stage in another phase. When fewer objects set the
        // group's phase with their leading tags than are warned in the group for another, the slip is more likely theirs:
        // corrected, they give the group the phase the others carry.
        private static string PhaseAdvice(StageOwner group, int warned)
        {
            if (group.PhaseFromLeading && group.PhaseSetters.Count < warned)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Check the Ph numbers: fewer objects set the group's phase ({0}) than are warned in it for another ({1}), so the slip may be in {2} rather than in this object; otherwise move the object to the group for its phase and stage.",
                    group.PhaseSetters.Count, warned, Quoted(group.PhaseSetters));
            }
            return "Check the Ph numbers, or move the object to the group for its phase and stage.";
        }

        // Object names in quotes, "'a', 'b' and 'c'", five at most, then how many more.
        private static string Quoted(IList<string> names)
        {
            const int shown = NamesShown / 2;
            var quoted = names.Take(shown).Select(n => "'" + n + "'").ToList();
            if (names.Count > shown)
            {
                return string.Join(", ", quoted.ToArray()) + " and " + (names.Count - shown).ToString(CultureInfo.InvariantCulture) + " more";
            }
            if (quoted.Count < 2)
            {
                return string.Join(string.Empty, quoted.ToArray());
            }
            return string.Join(", ", quoted.Take(quoted.Count - 1).ToArray()) + " and " + quoted[quoted.Count - 1];
        }

        // "removal" or "install".
        private static string KindOf(PlannerMeshSegment segment)
        {
            return segment.Type == PlannerTaskType.Dismantle ? "removal" : "install";
        }

        // Where the Planner puts a tag's work that no dated layer has: a removal after the works, an install at the start of
        // the project (not TBC in phase 0, which is there before the works).
        private static string WithoutLayer(PlannerMeshSegment segment)
        {
            if (segment.Type == PlannerTaskType.Dismantle)
            {
                return "after the end of the works, marked TBC";
            }
            return segment.Phase == 0 ? "at the start of the project" : "at the start of the project, marked TBC";
        }

        // A tag's phase and stage as the messages write them: "Ph2_St06-2".
        private static string TagText(PlannerMeshSegment segment)
        {
            return "Ph" + segment.Phase.ToString(CultureInfo.InvariantCulture) + "_" + PlannerNaming.StageText(segment.Stage, segment.SubStage);
        }

        // A layer's phase and stage likewise.
        private static string TagText(StageOwner layer)
        {
            return "Ph" + (layer.Phase.HasValue ? layer.Phase.Value.ToString(CultureInfo.InvariantCulture) : "?") + "_" + PlannerNaming.StageText(layer.Stage, layer.SubStage);
        }

        // The layer the Planner dates a tag's work from when it is not its own group's: among the dated layers with its
        // phase and stage, the one for that very sub-stage first (a whole-stage layer for plain St<n> work), then the
        // earliest start, then by name. Null when there is none.
        private static StageOwner DatingLayer(PlannerMeshSegment segment, List<StageOwner> layers)
        {
            return layers.Where(o => o.Start.HasValue && o.Covers(segment))
                .OrderBy(o => o.SubStage == segment.SubStage ? 0 : 1)
                .ThenBy(o => o.Start.Value)
                .ThenBy(o => o.Group, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        // The phase and stage a dated group owns, as the Planner reads it: a phasing layer's ID, a group named phase then
        // stage, or a group named with one number in the phase GroupPhase reads off its objects, with which objects set it
        // (for the messages). No phase when none of its objects has its stage.
        private static StageOwner OwnerOf(string groupName, PlannerLayerName group, PhasingTree phasing, IEnumerable<FiledObject> members)
        {
            var owner = new StageOwner { Group = groupName, Start = group.Start };
            PhasingLayerId id;
            if (phasing.Layers.TryGetValue(groupName, out id))
            {
                owner.Phase = id.Phase;
                owner.Stage = id.Stage;
                owner.SubStage = id.Split;
                return owner;
            }
            owner.Stage = group.Stage;
            owner.SubStage = group.SubStage;
            var list = members.ToList();
            bool fromLeading;
            owner.Phase = GroupPhase(group, list.Select(m => m.Parsed), out fromLeading);
            if (owner.Phase.HasValue && !group.Phase.HasValue)
            {
                // The objects whose tag of the group's stage counted (leading, or trailing when no leading tag has it), and
                // those of them that carry the phase the group took.
                Func<FiledObject, PlannerMeshSegment> counted = m => fromLeading ? m.Parsed.Lead : m.Parsed.Trail;
                var voters = list.Where(m => HasStageOf(counted(m), group)).ToList();
                owner.PhaseFromLeading = fromLeading;
                owner.PhaseCounted = voters.Count;
                owner.PhaseSetters = voters.Where(m => counted(m).Phase == owner.Phase.Value).Select(m => m.Name).ToList();
                owner.PhaseTie = voters.GroupBy(m => counted(m).Phase).Count(g => g.Count() == owner.PhaseSetters.Count) > 1;
            }
            return owner;
        }

        /// <summary>
        /// The phase the Planner gives a dated group: the one its name gives (phase then stage), else, for a name with one
        /// number, the phase most of its objects' LEADING tags with its stage carry, and only when no leading tag has its
        /// stage, the phase most of their trailing tags with its stage carry (the lower phase on a tie, either way). So one
        /// object whose leading tag has the stage sets the phase on its own, whatever the trailing tags of the others say.
        /// Null when no tag of its objects has its stage. <paramref name="members"/> are the tagged objects whose nearest
        /// dated group it is.
        /// </summary>
        internal static int? GroupPhase(PlannerLayerName group, IEnumerable<PlannerMeshName> members)
        {
            bool fromLeading;
            return GroupPhase(group, members, out fromLeading);
        }

        // As above; fromLeading: a name with one number took its phase from leading tags (false when trailing tags gave it,
        // or the name gave it, or there is none).
        private static int? GroupPhase(PlannerLayerName group, IEnumerable<PlannerMeshName> members, out bool fromLeading)
        {
            fromLeading = false;
            if (group.Phase.HasValue)
            {
                return group.Phase;
            }
            var list = members.ToList();
            var leading = Majority(list.Select(m => m.Lead).Where(s => HasStageOf(s, group)).Select(s => s.Phase));
            if (leading.HasValue)
            {
                fromLeading = true;
                return leading;
            }
            return Majority(list.Select(m => m.Trail).Where(s => HasStageOf(s, group)).Select(s => s.Phase));
        }

        // The tag has the group's stage (sub-stages as SameStage), whatever its phase.
        private static bool HasStageOf(PlannerMeshSegment segment, PlannerLayerName group)
        {
            return segment != null && PlannerNaming.SameStage(segment.Stage, segment.SubStage, group.Stage, group.SubStage);
        }

        private static int? Majority(IEnumerable<int> values)
        {
            var best = values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
            return best != null ? best.Key : (int?)null;
        }

        private static void AddMissingLayerWarning(NamingReport report, MissingLayers missing, string kind, string where, string when)
        {
            if (missing.Objects.Count == 0)
            {
                return;
            }
            var text = string.Format(CultureInfo.InvariantCulture,
                "{0} object(s) have {1} tag whose phase and stage have no layer in this file ({2}), so the Planner cannot date that work: it puts it {3}, marked TBC. Give each tag the stage the object {4}, or add the layer for that stage: {5}.",
                missing.Objects.Count, kind, PhasingFixScript.FirstNames(missing.Tags, NamesShown), where, when, PhasingFixScript.FirstNames(missing.Objects, NamesShown));
            if (missing.Layers.Count > 0)
            {
                text += string.Format(CultureInfo.InvariantCulture,
                    " The group(s) {0} have such a phase and stage but hold no tagged object, so the Planner does not read them as layers.",
                    PhasingFixScript.FirstNames(missing.Layers, NamesShown));
            }
            Add(report, NamingSeverity.Warning, missing.Objects[0], text);
        }

        private static void AddUndatedLayerNote(NamingReport report, MissingLayers missing, string kind, string where)
        {
            if (missing.Objects.Count == 0)
            {
                return;
            }
            Add(report, NamingSeverity.Note, missing.Objects[0], string.Format(CultureInfo.InvariantCulture,
                "{0} object(s) have {1} tag whose phase and stage only an undated layer has ({2}; {3}), so the Planner cannot date that work until the layer has dates: it puts it {4}, marked TBC: {5}.",
                missing.Objects.Count, kind, PhasingFixScript.FirstNames(missing.Tags, NamesShown), PhasingFixScript.FirstNames(missing.Layers, NamesShown),
                where, PhasingFixScript.FirstNames(missing.Objects, NamesShown)));
        }

        // A phasing layer's ID as the convention writes it: "06_01-2".
        private static string LayerIdText(PhasingLayerId id)
        {
            return id.Phase.ToString("00", CultureInfo.InvariantCulture) + "_" + id.Stage.ToString("00", CultureInfo.InvariantCulture)
                + (id.Split.HasValue ? "-" + id.Split.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

        private static void CheckGroups(Dictionary<string, PlannerLayerName> datedGroups, PhasingTree phasing, DateTime today, NamingReport report)
        {
            var legacy = new FullStops();
            foreach (var entry in datedGroups.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var name = entry.Key;
                var parsed = entry.Value;
                // A phasing layer's full stop is a sub-stage only after its stage (02_05.1), and Fix phasing names
                // converts it (in the layers of the top node it corrects; those of another top node are renamed by hand);
                // after the phase (6.06) it is a slip that the fixer corrects with the rest of the ID. Any other group is
                // renamed by hand, and its full stop counts only after a two-digit stage, not before "_x_"
                // (GroupSubStageFullStop).
                PhasingLayerId layerId;
                if (phasing.Layers.TryGetValue(name, out layerId))
                {
                    if (layerId.SplitAfterFullStop)
                    {
                        legacy.Add(name, phasing.Corrects(name));
                    }
                }
                else if (parsed.Legacy && GroupSubStageFullStop.IsMatch(name))
                {
                    legacy.Add(name, false);
                }
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
            AddFullStopWarning(report, legacy, "group", "01_04.2_... or 05.2_...", "01_04-2_..., 05-2_...", phasing.FixerTop);

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
        // covers the whole stage, and a plain St<n> sits under a sub-numbered group as it always has), and, for a group
        // named phase then stage ("02_01-2_..."), the phase is the group's.
        private static bool MatchesGroup(PlannerMeshSegment segment, PlannerLayerName group)
        {
            return (!group.Phase.HasValue || segment.Phase == group.Phase.Value)
                && PlannerNaming.SameStage(segment.Stage, segment.SubStage, group.Stage, group.SubStage);
        }

        // A phasing layer owns the object's work when the tag has its phase and its stage (sub-stages as above).
        private static bool MatchesLayer(PlannerMeshSegment segment, PhasingLayerId layer)
        {
            return segment.Phase == layer.Phase && PlannerNaming.SameStage(segment.Stage, segment.SubStage, layer.Stage, layer.Split);
        }

        // "St6" and "St6-1" want a zero; the digits before any sub-stage are what is padded.
        private static bool NeedsPadding(PlannerMeshSegment segment)
        {
            if (segment == null || segment.Stage >= 10)
            {
                return false;
            }
            int split = segment.StageToken.IndexOfAny(new[] { '-', '.' });
            return (split < 0 ? segment.StageToken.Length : split) < 2;
        }

        private static bool ContainsWhitespace(string text)
        {
            return text.Any(char.IsWhiteSpace);
        }

        // One warning for the names of a kind that write a sub-stage with a full stop: those Fix phasing names converts
        // and those to rename by hand, each with some of the names. The fixer converts only the layers of its top node
        // (fixerTop) and the objects directly in them.
        private static void AddFullStopWarning(NamingReport report, FullStops names, string kind, string oldForm, string newForm, string fixerTop)
        {
            int total = names.Converted.Count + names.ByHand.Count;
            if (total == 0)
            {
                return;
            }
            var text = string.Format(CultureInfo.InvariantCulture,
                "{0} {1} name(s) write the sub-stage with a full stop, the old form ({2}); the convention now writes a hyphen ({3}). The Planner reads both the same. ",
                total, kind, oldForm, newForm);
            var scope = "Fix phasing names\u2026 converts only the phasing layers" + (fixerTop != null ? " under '" + fixerTop + "'" : string.Empty)
                + " and the objects directly in them";
            if (names.ByHand.Count == 0)
            {
                text += "Fix phasing names\u2026 on the exporter window converts them: " + PhasingFixScript.FirstNames(names.Converted, NamesShown) + ".";
            }
            else if (names.Converted.Count == 0)
            {
                text += "Rename them by hand (" + scope + "): " + PhasingFixScript.FirstNames(names.ByHand, NamesShown) + ".";
            }
            else
            {
                text += string.Format(CultureInfo.InvariantCulture,
                    "Fix phasing names\u2026 on the exporter window converts {0} of them: {1}; rename the other {2} by hand ({3}): {4}.",
                    names.Converted.Count, PhasingFixScript.FirstNames(names.Converted, NamesShown / 2),
                    names.ByHand.Count, scope, PhasingFixScript.FirstNames(names.ByHand, NamesShown / 2));
            }
            Add(report, NamingSeverity.Warning, names.First, text);
        }

        private static void Add(NamingReport report, NamingSeverity severity, string subject, string message)
        {
            report.Issues.Add(new NamingIssue { Severity = severity, Subject = subject, Message = message });
        }

        /// <summary>Names that write a sub-stage with a full stop, each once, in the order found.</summary>
        private sealed class FullStops
        {
            /// <summary>Fix phasing names converts these.</summary>
            public readonly List<string> Converted = new List<string>();
            /// <summary>These are renamed by hand.</summary>
            public readonly List<string> ByHand = new List<string>();
            /// <summary>The first name found.</summary>
            public string First;
            private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            public void Add(string name, bool converted)
            {
                if (!seen.Add(name))
                {
                    return;
                }
                if (First == null)
                {
                    First = name;
                }
                (converted ? Converted : ByHand).Add(name);
            }
        }

        /// <summary>The layers of the phasing hierarchy, by group name, and which of them Fix phasing names corrects.</summary>
        private sealed class PhasingTree
        {
            /// <summary>Each layer's ID, read as the fixer reads it.</summary>
            public readonly Dictionary<string, PhasingLayerId> Layers = new Dictionary<string, PhasingLayerId>(StringComparer.Ordinal);
            /// <summary>The top node each layer is under.</summary>
            public readonly Dictionary<string, string> TopOf = new Dictionary<string, string>(StringComparer.Ordinal);
            /// <summary>The top node whose layers the fixer corrects, or null when there is none.</summary>
            public string FixerTop;

            /// <summary>The fixer corrects this layer (and the objects directly in it).</summary>
            public bool Corrects(string layer)
            {
                string top;
                return FixerTop != null && TopOf.TryGetValue(layer, out top) && top == FixerTop;
            }
        }

        /// <summary>A tagged object inside a dated group.</summary>
        private sealed class FiledObject
        {
            public string Name;
            public PlannerMeshName Parsed;
            /// <summary>Its nearest dated group.</summary>
            public string Group;
        }

        /// <summary>The phase and stage a dated group owns; no phase when its objects do not say.</summary>
        private sealed class StageOwner
        {
            /// <summary>The group's name.</summary>
            public string Group;
            /// <summary>The group's first date; null when it has none (TBC).</summary>
            public DateTime? Start;
            public int? Phase;
            public int Stage;
            public int? SubStage;
            /// <summary>A group named with one number: its phase came from leading tags (else from trailing ones).</summary>
            public bool PhaseFromLeading;
            /// <summary>A group named with one number: its objects whose tag (leading, or trailing as above) has its stage.</summary>
            public int PhaseCounted;
            /// <summary>A group named with one number: those of them that carry its phase, by name, in the order read.</summary>
            public List<string> PhaseSetters = new List<string>();
            /// <summary>A group named with one number: another phase had as many of them, and the lower phase won.</summary>
            public bool PhaseTie;

            /// <summary>The group owns this tag's work: its stage (sub-stages as SameStage), in the group's phase when there is one.</summary>
            public bool Owns(PlannerMeshSegment segment)
            {
                return (!Phase.HasValue || segment.Phase == Phase.Value) && PlannerNaming.SameStage(segment.Stage, segment.SubStage, Stage, SubStage);
            }

            /// <summary>The group has a phase, and this tag's phase and stage are the group's: another layer's tag it can date.</summary>
            public bool Covers(PlannerMeshSegment segment)
            {
                return Phase.HasValue && segment.Phase == Phase.Value && PlannerNaming.SameStage(Stage, SubStage, segment.Stage, segment.SubStage);
            }
        }

        /// <summary>A warning on a tag of a one-number group's stage in another phase, before its advice is added.</summary>
        private sealed class PhaseWarning
        {
            public string Subject;
            /// <summary>The object's group, with how it got its phase.</summary>
            public StageOwner Owner;
            public string Text;
        }

        /// <summary>Objects with a tag no (dated) layer has, each once, those tags, and the groups to name, each once, in the order found.</summary>
        private sealed class MissingLayers
        {
            public readonly List<string> Objects = new List<string>();
            public readonly List<string> Tags = new List<string>();
            public readonly List<string> Layers = new List<string>();
            private readonly HashSet<string> objects = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> tags = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> layers = new HashSet<string>(StringComparer.Ordinal);

            public void Add(string name, PlannerMeshSegment segment, IEnumerable<StageOwner> groups)
            {
                if (objects.Add(name))
                {
                    Objects.Add(name);
                }
                var tag = TagText(segment);
                if (tags.Add(tag))
                {
                    Tags.Add(tag);
                }
                foreach (var group in groups)
                {
                    if (layers.Add(group.Group))
                    {
                        Layers.Add(group.Group);
                    }
                }
            }
        }
    }
}
