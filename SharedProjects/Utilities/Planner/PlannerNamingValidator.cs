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
        /// <summary>Objects that carry a Ph/St tag, as the Planner reads them: a tagged group head counts, a split piece is part of its object.</summary>
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
    /// name reads as the fixer reads it, &lt;PP&gt;_&lt;SS&gt;[_&lt;split&gt;]_&lt;name&gt; (or one number with an old sub-stage,
    /// 06-2_&lt;name&gt;, in the phase its objects' tags agree on): an object belongs to it when a tag has the layer's phase
    /// and stage. Any other dated group is read as the Planner reads it: a name that starts with two or three numbers is
    /// phase then stage (and sub-stage; an object belongs to it when a tag has both), one that starts with one number is a
    /// stage in the phase the Planner reads off its objects: the phase most of their leading tags with its stage carry,
    /// or, only when no leading tag has its stage, most of their trailing tags with it (an object belongs to it when a
    /// tag has the stage; one whose tag has the stage in another phase is reported when the Planner dates it from another
    /// layer or when only its trailing tag has the stage, naming the objects that set the phase when leading tags did).
    ///
    /// The objects are read as the Planner reads them: the geometry, and the tagged nodes objects are linked under (a Max
    /// group's head, which Add to IN / Add to RM tags for a closed group; the export passes these as groups), each split
    /// piece (Name_N linked under Name) as part of its object, checked only for what its own name writes; an untagged node
    /// linked under a scheduled object is shown and hidden with it.
    ///
    /// Severity guide:
    ///   Error   - the Planner cannot read the name at all, or two exported objects share a name.
    ///   Warning - the name reads, but the Planner will schedule it differently from what was meant
    ///             (unfiled, undated, phase or stage mismatch, a tag whose phase and stage no layer has, gathered
    ///             in one warning for removal tags and one for install tags, empty description, case-only twins,
    ///             objects taken away and put back in one stage that sit in another stage's layer, which the Planner lists
    ///             as a naming error, gathered in one warning with what each should be retagged as),
    ///             or the name breaks the convention although it reads (a space anywhere in a tagged object's or a
    ///             dated group's name; a sub-stage written an old way, with a hyphen or a full stop, in object tags
    ///             or group names, one warning for each with the count and some of the names; a group of one number
    ///             with such a sub-stage, one warning each, saying the phase it needs and what it becomes; a hyphen
    ///             anywhere else in a tagged object's name, one warning, as Unreal's Datasmith import turns it into an
    ///             underscore and the object no longer matches its phasing there). Stray
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
        // A group of one number with an old sub-stage, as written: groups 1 stage, 2 sub-stage, 3 the rest (from the
        // separator after the ID on).
        private static readonly Regex OneNumberId = new Regex(@"^([0-9]+)[.-]([0-9]+)([_ ][\s\S]*)?\z", RegexOptions.CultureInvariant);

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
            return Validate(nodes, today, phasingTop, null);
        }

        /// <param name="nodes">The nodes about to be exported.</param>
        /// <param name="today">For the check on odd years.</param>
        /// <param name="phasingTop">The top node Fix phasing names corrects (see above).</param>
        /// <param name="fixerLeavesMisfiled">
        /// The names of the objects Fix phasing names leaves as they are because each is taken away and put back in one
        /// stage while it sits in another stage's layer (<see cref="PhasingFixPlan.LeftMisfiled"/>, the fixer's own plan for
        /// the scene), or null: then they are worked out from each layer's ID as the fixer reads it now
        /// (<see cref="PhasingNameFixer.MisfiledIn(string, PhasingNameFixer.LayerRef)"/>), which can differ where the fixer
        /// numbers a stage's layers again.
        /// </param>
        public static NamingReport Validate(IEnumerable<SceneNodeInfo> nodes, DateTime today, string phasingTop, ICollection<string> fixerLeavesMisfiled)
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

            var oneNumber = CheckGroups(datedGroups, phasing, today, report);
            var legacyObjects = new OldForms();
            var hyphenObjects = new OldForms();
            var filed = new List<FiledObject>();
            var keptNames = KeptNames(all, groupNames);
            var objects = ObjectsRead(all);
            var misfiled = MisfiledObjects(objects, datedGroups);
            var misfiledNames = new HashSet<string>(misfiled.Select(m => m.Name), StringComparer.Ordinal);
            // The objects the Planner schedules: a node linked under one is shown and hidden with it.
            var scheduled = new HashSet<string>(objects.Where(o => o.Piece == null && o.Parsed != null && NearestDatedGroup(o.Node, datedGroups) != null)
                .Select(o => o.Node.Name), StringComparer.Ordinal);
            foreach (var item in objects)
            {
                var placed = CheckMesh(item, datedGroups, phasing, report, legacyObjects, hyphenObjects, keptNames, misfiledNames, scheduled, fixerLeavesMisfiled);
                if (placed != null)
                {
                    filed.Add(placed);
                }
            }
            AddOldFormWarning(report, legacyObjects, "object", "Ph1_St04-2_IN_...", "Ph1_St04.2_IN_...", "Ph1_St04_2_IN_...", phasing.FixerTop);
            AddHyphenWarning(report, hyphenObjects, phasing.FixerTop);
            CheckOneNumberGroups(oneNumber, datedGroups, filed, phasing, report);
            CheckStagesWithoutLayer(filed, datedGroups, phasing, report, misfiledNames);
            AddMisfiledWarning(report, misfiled);
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
        /// a dated group (a full stop after the phase, "06.01.2_...") still counts: the fixer corrects it. So does one number
        /// with an old sub-stage ("06-2_...") when the objects directly in it agree on its phase, as the fixer reads it.
        ///
        /// Where the fixer's top node has its layers, the names it leaves as they are, with why, so that the warnings say
        /// "rename by hand" with the reason rather than promise a conversion (<see cref="PhasingTree.LeftAlone"/>): a layer
        /// directly under the top node with no phase group of its phase, or several, to move it into (the fixer's
        /// "left as it is" Check line), and a name that reads as a layer ID that the fixer cannot read
        /// (<see cref="PhasingNameFixer.ReadLayerName"/>: one number whose objects directly in it give no phase or several,
        /// a number of three digits or more, an ID the Planner reads otherwise).
        /// </summary>
        private static PhasingTree PhasingLayers(List<SceneNodeInfo> all, IEnumerable<string> groupNames, string phasingTop)
        {
            // The tagged objects directly in each group, as the fixer reads a layer's objects.
            var objectsIn = all.Where(n => n.IsMesh && n.Ancestors.Count > 0 && n.Ancestors[0] != null)
                .Select(n => new { Parent = n.Ancestors[0], Parsed = PlannerNaming.ParseMeshName(n.Name) })
                .Where(o => o.Parsed != null)
                .ToLookup(o => o.Parent, o => o.Parsed, StringComparer.Ordinal);
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
            // The phases of the phase groups directly under the fixer's top node, once for each group, where it moves the
            // layers directly under that top node.
            var groupPhases = parents.Where(p => p.Value == tree.FixerTop)
                .Select(p => { int phase; return PhasingNameFixer.TryReadPhaseGroup(p.Key, out phase) ? phase : (int?)null; })
                .Where(p => p.HasValue).Select(p => p.Value).ToList();
            foreach (var groupName in groupNames)
            {
                string parent, grandparent;
                int phase;
                if (!parents.TryGetValue(groupName, out parent))
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
                if (top == null)
                {
                    continue;
                }
                var read = PhasingNameFixer.ReadLayerName(groupName, objectsIn[groupName]);
                if (read.Id == null)
                {
                    if (read.Left != PhasingNameFixer.LayerLeft.None && top == tree.FixerTop)
                    {
                        tree.LeftAlone[groupName] = LeftWhy(read);
                    }
                    continue;
                }
                tree.Layers[groupName] = read.Id;
                tree.TopOf[groupName] = top;
                if (top == tree.FixerTop && parent == top)
                {
                    // Directly under the top node, the fixer moves a layer into the one phase group of its phase; with none,
                    // or several, it leaves the layer, and its objects, as they are.
                    var homes = groupPhases.Count(p => p == read.Id.Phase);
                    if (homes != 1)
                    {
                        tree.LeftAlone[groupName] = string.Format(CultureInfo.InvariantCulture, homes == 0
                            ? "it is directly under '{0}', with no phase group _{1:00}_<name> to move it into"
                            : "it is directly under '{0}', and {2} phase groups there have phase {1}", top, read.Id.Phase, homes);
                    }
                }
            }
            return tree;
        }

        // Why Fix phasing names leaves a name that reads as a layer ID as it is (PhasingNameFixer.ReadLayerName), as the
        // naming check words it.
        private static string LeftWhy(PhasingNameFixer.LayerRead read)
        {
            switch (read.Left)
            {
                case PhasingNameFixer.LayerLeft.NoPhase:
                case PhasingNameFixer.LayerLeft.SeveralPhases:
                    var stageText = read.One.Stage.ToString("00", CultureInfo.InvariantCulture) + "_" + read.One.SplitText;
                    return read.Left == PhasingNameFixer.LayerLeft.NoPhase
                        ? "Fix phasing names\u2026 reads only the objects directly in the layer, and none of them has a tag of stage " + stageText
                        : "Fix phasing names\u2026 reads only the objects directly in the layer, and their tags of stage " + stageText + " carry phases "
                          + Joined(read.One.Phases.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList());
                case PhasingNameFixer.LayerLeft.LongNumber:
                    return "the convention writes each number of a layer ID with one or two digits";
                default:
                    return "the Planner reads its ID as " + PhasingNameFixer.PlannerIdText(read.Planner) + ", which Fix phasing names\u2026 does not";
            }
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

        // The names Fix phasing names leaves as they are, which it never renames another object into: every group's (an
        // object's corrected name has no hyphen, a dated group's has), every untagged object's, and every tagged object's
        // with no hyphen and no tag's sub-stage written with a full stop (one the fixer gives another tag, its layer's, is
        // taken to keep its name: the naming check does not work out the fixer's tags).
        private static HashSet<string> KeptNames(List<SceneNodeInfo> all, IEnumerable<string> groupNames)
        {
            var kept = new HashSet<string>(groupNames, StringComparer.Ordinal);
            foreach (var node in all.Where(n => n.IsMesh))
            {
                var parsed = PlannerNaming.ParseMeshName(node.Name);
                if (parsed == null || Underscored(node.Name, parsed) == node.Name)
                {
                    kept.Add(node.Name);
                }
            }
            return kept;
        }

        // A tagged object's name with the underscores Fix phasing names gives it: for every hyphen, and for the full stop
        // before a tag's sub-stage ("Ph1_St04.2_IN_Kerb-1" gives "Ph1_St04_2_IN_Kerb_1").
        private static string Underscored(string name, PlannerMeshName parsed)
        {
            var text = name.Replace('-', '_');
            foreach (var segment in new[] { parsed.Lead, parsed.Trail })
            {
                if (segment != null && segment.LegacyForm == PlannerLegacyForm.Dot)
                {
                    text = text.Replace("_St" + segment.StageToken + "_", "_St" + segment.StageToken.Replace('.', '_') + "_");
                }
            }
            return text;
        }

        // The nearest dated group above a node (the Planner's layer for it), or null.
        private static string NearestDatedGroup(SceneNodeInfo node, Dictionary<string, PlannerLayerName> datedGroups)
        {
            return node.Ancestors.FirstOrDefault(a => a != null && datedGroups.ContainsKey(a));
        }

        /// <summary>A node the Planner reads as an object, or as a split piece of one.</summary>
        private sealed class ObjectRead
        {
            public SceneNodeInfo Node;
            /// <summary>Its tags; null when its name has none.</summary>
            public PlannerMeshName Parsed;
            /// <summary>A split piece (Name_N linked under Name): the object it is part of; else null.</summary>
            public string Piece;
        }

        /// <summary>
        /// The nodes as the Planner reads them as objects (the viewport's name index, phasingFromModel.ts toObjects): every
        /// exported node by its name, the geometry first, then the nodes objects are linked under that the export passes as
        /// groups (a Max group's head, a dummy), of which only a tagged one can be scheduled, the first node of a name
        /// standing for it; a split piece (Name_N, Name_primitiveN linked under a node named Name, meshes.ts mergedName) is
        /// read as part of its object, never as an object of its own.
        /// </summary>
        private static List<ObjectRead> ObjectsRead(List<SceneNodeInfo> all)
        {
            var result = new List<ObjectRead>();
            var meshNames = new HashSet<string>(all.Where(n => n.IsMesh).Select(n => n.Name), StringComparer.Ordinal);
            var groupsSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in all.Where(n => n.IsMesh))
            {
                result.Add(new ObjectRead { Node = node, Parsed = PlannerNaming.ParseMeshName(node.Name), Piece = PieceOf(node) });
            }
            foreach (var node in all.Where(n => !n.IsMesh))
            {
                if (meshNames.Contains(node.Name) || !groupsSeen.Add(node.Name))
                {
                    continue;
                }
                var parsed = PlannerNaming.ParseMeshName(node.Name);
                if (parsed != null)
                {
                    result.Add(new ObjectRead { Node = node, Parsed = parsed, Piece = PieceOf(node) });
                }
            }
            return result;
        }

        // The object a split piece belongs to, or null when the node is not one: its name less "_N" (or "_primitiveN"),
        // again while that is the name of a node it is linked under (meshes.ts mergedName).
        private static string PieceOf(SceneNodeInfo node)
        {
            var current = node.Name;
            while (PlannerSchedule.SplitSuffix.IsMatch(current))
            {
                var stripped = PlannerSchedule.SplitSuffix.Replace(current, string.Empty, 1);
                if (stripped.Length == 0 || !node.Ancestors.Contains(stripped))
                {
                    break;
                }
                current = stripped;
            }
            return current == node.Name ? null : current;
        }

        /// <summary>An object taken away and put back in one stage that sits in another stage's layer.</summary>
        private sealed class Misfiled
        {
            public string Name;
            public string Group;
            /// <summary>"Ph2_St02": the stage it is taken away and put back in.</summary>
            public string Stage;
            /// <summary>Its name with both tags giving the layer it sits in.</summary>
            public string Retagged;
            /// <summary>Its split pieces, which are renamed with it.</summary>
            public int Pieces;
        }

        /// <summary>
        /// The objects whose names take them away and put them back in one stage (the removal tag first, then the install
        /// tag, the same phase, stage and sub-stage) while they sit in a dated group that owns neither tag's work: another
        /// stage's layer. The Planner lists each as a naming error ("Layer names that need attention"): such an object
        /// belongs in that stage's layer. Read as the Planner reads them (<see cref="PlannerSchedule"/>, its classification of
        /// a layer's objects): the objects are <see cref="ObjectsRead"/>'s (group heads included, split pieces folded into
        /// their object), the nearest dated group is the object's layer, and groups of one label are one layer; a group named
        /// phase then stage owns that phase and stage, one named with one number its stage in the phase
        /// <see cref="GroupPhase(PlannerLayerName, IEnumerable{PlannerMeshName})"/> reads off the objects of its label. In the
        /// order the objects are read.
        /// </summary>
        private static List<Misfiled> MisfiledObjects(List<ObjectRead> objects, Dictionary<string, PlannerLayerName> datedGroups)
        {
            var filed = new List<KeyValuePair<string, KeyValuePair<string, PlannerMeshName>>>();
            foreach (var item in objects.Where(o => o.Piece == null && o.Parsed != null))
            {
                var group = NearestDatedGroup(item.Node, datedGroups);
                if (group != null)
                {
                    filed.Add(new KeyValuePair<string, KeyValuePair<string, PlannerMeshName>>(group, new KeyValuePair<string, PlannerMeshName>(item.Node.Name, item.Parsed)));
                }
            }
            var pieces = objects.Where(o => o.Piece != null).GroupBy(o => o.Piece, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            // The Planner reads groups of one label (the ID and name, without dates or a " (1)") as one layer: its phase is
            // read off all their objects.
            var members = filed.ToLookup(f => datedGroups[f.Key].Label, f => f.Value.Value, StringComparer.Ordinal);
            var phases = new Dictionary<string, int?>(StringComparer.Ordinal);
            var result = new List<Misfiled>();
            foreach (var item in filed)
            {
                var parsed = item.Value.Value;
                var lead = parsed.Lead;
                var trail = parsed.Trail;
                if (trail == null || lead.Type != PlannerTaskType.Dismantle || trail.Type != PlannerTaskType.Install
                    || lead.Phase != trail.Phase || lead.Stage != trail.Stage || lead.SubStage != trail.SubStage)
                {
                    continue;
                }
                var group = datedGroups[item.Key];
                int? phase;
                if (!phases.TryGetValue(group.Label, out phase))
                {
                    phase = GroupPhase(group, members[group.Label]);
                    phases[group.Label] = phase;
                }
                Func<PlannerMeshSegment, bool> owns = s => PlannerNaming.SameStage(s.Stage, s.SubStage, group.Stage, group.SubStage) && (!phase.HasValue || s.Phase == phase.Value);
                if (owns(lead) || owns(trail))
                {
                    continue;
                }
                int count;
                pieces.TryGetValue(item.Value.Key, out count);
                result.Add(new Misfiled
                {
                    Name = item.Value.Key,
                    Group = item.Key,
                    Stage = "Ph" + lead.Phase.ToString(CultureInfo.InvariantCulture) + "_" + PlannerSchedule.StageTag(lead),
                    Retagged = PlannerSchedule.Retagged(item.Value.Key, phase ?? lead.Phase, group.Stage, group.SubStage),
                    Pieces = count
                });
            }
            return result;
        }

        // One warning for every object taken away and put back in one stage that sits in another stage's layer, with the
        // Planner's advice for each (its split pieces, Name_N linked under it, renamed with it: the Planner reads them as
        // part of it only while their names start with its own).
        private static void AddMisfiledWarning(NamingReport report, List<Misfiled> misfiled)
        {
            if (misfiled.Count == 0)
            {
                return;
            }
            var each = misfiled.Select(m => string.Format(CultureInfo.InvariantCulture, "'{0}' in '{1}' (move it to the {2} layer, or retag it as {3}{4})", m.Name, m.Group, m.Stage, m.Retagged,
                    m.Pieces == 0 ? string.Empty : m.Pieces == 1 ? ", and its split piece likewise" : ", and its " + m.Pieces.ToString(CultureInfo.InvariantCulture) + " split pieces likewise"))
                .ToList();
            Add(report, NamingSeverity.Warning, misfiled[0].Name, string.Format(CultureInfo.InvariantCulture,
                "{0} object(s) are taken away and put back in one stage (a removal tag, then an install tag of the same phase and stage) but sit in another stage's layer, "
                + "which the Planner lists as a naming error: an object taken away and put back in one stage belongs in that stage's layer. Correct each in 3ds Max: move it "
                + "to that stage's layer, or retag it for the layer it sits in: {1}.",
                misfiled.Count, PhasingFixScript.FirstNames(each, NamesShown)));
        }

        /// <returns>The object and its dated group when it carries a tag and sits in one, else null.</returns>
        /// <param name="item">An object as the Planner reads it (<see cref="ObjectsRead"/>). A split piece is read as part of
        /// its object, which is checked on its own: only what its own name writes (spaces, hyphens, a sub-stage written an
        /// old way) is told about it.</param>
        /// <param name="keptNames">The names Fix phasing names leaves as they are (<see cref="KeptNames"/>).</param>
        /// <param name="misfiled">The objects <see cref="MisfiledObjects"/> finds, told in a warning of their own.</param>
        /// <param name="scheduled">The objects the Planner schedules: an untagged node linked under one is shown and hidden with it.</param>
        /// <param name="fixerLeavesMisfiled">The fixer's own list of the objects it leaves as taken away and put back in another stage's layer, or null.</param>
        private static FiledObject CheckMesh(ObjectRead item, Dictionary<string, PlannerLayerName> datedGroups, PhasingTree phasing,
            NamingReport report, OldForms legacyObjects, OldForms hyphenObjects, HashSet<string> keptNames, HashSet<string> misfiled, HashSet<string> scheduled,
            ICollection<string> fixerLeavesMisfiled)
        {
            var mesh = item.Node;
            var name = mesh.Name;
            var parsed = item.Parsed;
            bool piece = item.Piece != null;

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
            // of another top node with "phasing" in its name, it leaves as it is, as it does the objects of a layer it leaves
            // as it is (PhasingTree.LeftAlone).
            PhasingLayerId layerId = null;
            bool inPhasingLayer = datedGroupName != null && phasing.Layers.TryGetValue(datedGroupName, out layerId);
            bool underFixerTop = inPhasingLayer && phasing.UnderFixerTop(datedGroupName);
            var parent = mesh.Ancestors.Count > 0 ? mesh.Ancestors[0] : null;
            bool fixerCorrects = parent != null && phasing.Corrects(parent);

            if (parsed == null)
            {
                if (piece)
                {
                    // Its object is told about.
                }
                else if (LooksTagged.IsMatch(name))
                {
                    Add(report, NamingSeverity.Error, name, string.Format(
                        "'{0}': the leading tag is malformed. Objects are tagged Ph<n>_St<nn>_IN|RM_<description>, with an optional sub-stage St<nn>_<m>, e.g. Ph1_St03_IN_Sheet_Pile_1 or Ph1_St03_2_IN_Sheet_Pile_1.", name));
                }
                else if (datedGroup != null && !mesh.Ancestors.Any(a => a != null && scheduled.Contains(a)))
                {
                    // One linked under a scheduled object (a member of its Max group, a part linked under it) is shown and
                    // hidden with it.
                    Add(report, NamingSeverity.Note, name, string.Format(
                        "'{0}' sits inside the dated group '{1}' but carries no Ph/St tag, so the Planner will not schedule it.", name, datedGroupName));
                }
                return null;
            }

            if (!piece)
            {
                report.TaggedObjects++;
            }
            // Where the fixer would convert the name, it does not when the name it would take is one that another node keeps
            // (KeepTakenNames: "Kerb-1" beside "Kerb_1"). The name it takes is told here by its underscores only: the fixer
            // also gives the object its layer's tag, which may differ, but a clash that the underscores alone make is the
            // one that matters ("Kerb-1" and "Kerb_1" in the same layer).
            var converts = fixerCorrects;
            var byHandWhy = phasing.ObjectsLeftWhy(parent);
            var underscored = Underscored(name, parsed);
            if (converts && underscored != name && keptNames.Contains(underscored))
            {
                converts = false;
                byHandWhy = "Fix phasing names\u2026 leaves a name as it is when the one it would take with underscores is already another node's";
            }
            // Taken away and put back in one stage while its layer is another stage's: the fixer leaves the object as it is,
            // for the modeller to correct, so nothing here sends it there. By the fixer's own plan when given, else by the
            // layer's ID as the fixer reads it now (PhasingNameFixer.MisfiledIn).
            PhasingLayerId fixerLayer;
            if (converts && (fixerLeavesMisfiled != null
                ? fixerLeavesMisfiled.Contains(name)
                : phasing.Layers.TryGetValue(parent, out fixerLayer) && PhasingNameFixer.MisfiledIn(name, new PhasingNameFixer.LayerRef
                {
                    Phase = fixerLayer.Phase,
                    OldStage = fixerLayer.Stage,
                    OldSplit = fixerLayer.Split,
                    Stage = fixerLayer.Stage,
                    Split = fixerLayer.Split
                }) != null))
            {
                converts = false;
                byHandWhy = "Fix phasing names\u2026 leaves an object taken away and put back in one stage that sits in another stage's layer as it is, for the modeller to correct";
            }
            foreach (var segment in new[] { parsed.Lead, parsed.Trail })
            {
                if (segment != null && segment.Legacy)
                {
                    legacyObjects.Add(name, converts, segment.LegacyForm, byHandWhy);
                }
            }
            // Unreal's Datasmith import turns every hyphen in an object's name into an underscore. A hyphen before a tag's
            // sub-stage is told with the old forms above; any other is told here.
            int tagHyphens = new[] { parsed.Lead, parsed.Trail }.Count(s => s != null && s.LegacyForm == PlannerLegacyForm.Hyphen);
            if (name.Count(c => c == '-') > tagHyphens)
            {
                hyphenObjects.Add(name, converts, PlannerLegacyForm.None, byHandWhy);
            }

            // The convention allows no spaces at all, even though the parser tolerates them (a space after the order
            // number, spaces in a name, a note after the trailing tag) so that real slips still read.
            if (ContainsWhitespace(name))
            {
                Add(report, NamingSeverity.Warning, name, string.Format(
                    "'{0}' contains a space. Names must not contain spaces: use underscores, and keep notes out of the name.", name));
            }

            if (piece)
            {
                // A split piece: the Planner reads it as part of its object, with its object's tags, layer and work, which
                // are checked on the object ("Kerb_Ph2_St02_RM_1" carries its object's trailing tag before its number).
                return null;
            }

            if (parsed.Trail == null && StrayTrail.IsMatch(parsed.Description))
            {
                Add(report, NamingSeverity.Error, name, string.Format(
                    "'{0}': the text after the description looks like a second Ph/St tag but does not read as one. A trailing tag must be _Ph<n>_St<nn>[_<m>]_IN|RM at the very end of the name (notes go after a space).", name));
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
                    "'{0}': write stage numbers with two digits (St01, St06, St06_1) so names read and sort consistently.", name));
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
                              + (phasing.LeftWhy(unreadLayer)
                                 ?? (phasing.FixerTop != null ? "Fix phasing names\u2026 corrects only the layers under '" + phasing.FixerTop + "'" : "Fix phasing names\u2026 does not correct it"))
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
            if (misfiled.Contains(name))
            {
                // Taken away and put back in one stage, in another stage's layer: told with the others like it, with the
                // Planner's advice (AddMisfiledWarning), rather than as a tag that does not match its layer.
                return placed;
            }
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
                    else if (phasing.LeftWhy(datedGroupName) != null)
                    {
                        advice = string.Format("Fix phasing names\u2026 leaves the layer as it is ({0}): correct the Ph and St numbers by hand, or move the object to the layer for its stage.",
                            phasing.LeftWhy(datedGroupName));
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
                // A group named phase then stage ("02_01_2_...") owns the work of that phase and stage; a group named with one
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
        private static void CheckStagesWithoutLayer(List<FiledObject> filed, Dictionary<string, PlannerLayerName> datedGroups, PhasingTree phasing, NamingReport report,
            HashSet<string> misfiled)
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
                else if (!misfiled.Contains(item.Name))
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

        /// <summary>
        /// One warning for each group of one number whose sub-stage is written an old way ("06-2_Hoarding_...",
        /// "05.2_Subbase_..."): the convention writes a sub-stage only in an ID of three numbers, so the group needs its
        /// phase in front ("03_06_2_Hoarding_..."). The phase is the one its objects' tags with its stage agree on
        /// (<see cref="PhasingNameFixer.OneNumberPhase"/>): for a phasing layer, the objects directly in it, as Fix phasing
        /// names reads them, which then renames it (in the layers of its top node); for any other group, the tagged objects
        /// it is the nearest dated group of, as the Planner files them, and it is renamed by hand. When they give no phase,
        /// or several, the warning says so; when the fixer leaves it for another reason (<see cref="PhasingTree.LeftAlone"/>),
        /// the warning says why. The new name is given in full only when the fixer will write it so: when another layer of
        /// the fixer's has the same phase and stage, the fixer numbers the stage's layers 1, 2, ... in start-date order, so
        /// the sub-stage is not known yet. A number of three digits or more has no place in the convention's ID, which the
        /// warning says instead.
        /// </summary>
        private static void CheckOneNumberGroups(List<string> groups, Dictionary<string, PlannerLayerName> datedGroups, List<FiledObject> filed,
            PhasingTree phasing, NamingReport report)
        {
            var members = filed.ToLookup(f => f.Group, f => f.Parsed, StringComparer.Ordinal);
            foreach (var name in groups)
            {
                var parsed = datedGroups[name];
                var written = OneNumberId.Match(name);
                if (written.Success && (written.Groups[1].Length > 2 || written.Groups[2].Length > 2))
                {
                    Add(report, NamingSeverity.Warning, name, string.Format(CultureInfo.InvariantCulture,
                        "Group '{0}' is one number with a sub-stage written the old way ({1}), and the convention writes each number of a layer ID with one or two digits, so Fix phasing names\u2026 leaves it: rename it by hand, with its phase in front and a sub-stage of one or two digits (<phase>_{2:00}_<m>_<name>).",
                        name, parsed.Order, parsed.Stage));
                    continue;
                }
                PhasingLayerId layerId;
                bool fixer = phasing.Layers.TryGetValue(name, out layerId) && phasing.Corrects(name);
                List<int> phases;
                int? phase;
                if (layerId != null)
                {
                    phase = layerId.Phase;
                    phases = new List<int> { layerId.Phase };
                }
                else
                {
                    phase = PhasingNameFixer.OneNumberPhase(parsed.Stage, parsed.SubStage, members[name], out phases);
                }
                var stageText = parsed.Stage.ToString("00", CultureInfo.InvariantCulture) + "_"
                    + (parsed.SubStage.HasValue ? parsed.SubStage.Value.ToString(CultureInfo.InvariantCulture) : "<m>");
                var text = string.Format(CultureInfo.InvariantCulture,
                    "Group '{0}' is one number with a sub-stage written the old way ({1}). An ID of one number cannot carry a sub-stage, so the phase goes in front: <phase>_{2}_<name>. ",
                    name, parsed.Order, stageText);
                // The fixer's other layers of the same phase and stage: with any, it numbers them all again.
                var shared = fixer && phasing.Layers.Any(l => l.Key != name && l.Value.Phase == layerId.Phase && l.Value.Stage == layerId.Stage && phasing.Corrects(l.Key));
                if (shared)
                {
                    var stageId = layerId.Phase.ToString("00", CultureInfo.InvariantCulture) + "_" + layerId.Stage.ToString("00", CultureInfo.InvariantCulture);
                    text += string.Format(CultureInfo.InvariantCulture,
                        "Its objects' tags say phase {0}, so it becomes {1}: stage {2} has other layers, and Fix phasing names\u2026 on the exporter window renames it with them, numbering the stage's layers 1, 2, ... in start-date order.",
                        phase.Value, OneNumberRename(name, phase.Value, "<m>") ?? stageId + "_<m>_<name>", stageId);
                }
                else if (phase.HasValue)
                {
                    var renamed = OneNumberRename(name, phase.Value, null) ?? phase.Value.ToString("00", CultureInfo.InvariantCulture) + "_" + stageText + "_<name>";
                    text += string.Format(CultureInfo.InvariantCulture, "Its objects' tags say phase {0}, so it becomes {1}", phase.Value, renamed);
                    text += fixer
                        ? ": Fix phasing names\u2026 on the exporter window renames it."
                        : string.Format(": rename it by hand ({0}).", phasing.LeftWhy(name) ?? (phasing.FixerTop != null
                            ? "Fix phasing names\u2026 renames only the phasing layers under '" + phasing.FixerTop + "'"
                            : "Fix phasing names\u2026 renames only the phasing layers"));
                }
                else
                {
                    text += (phases.Count == 0
                            ? "None of its objects has a tag of stage " + stageText + " to give the phase"
                            : "Its objects' tags of that stage carry phases " + Joined(phases.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList()))
                        + ", so rename it by hand with the phase it belongs to.";
                }
                Add(report, NamingSeverity.Warning, name, text);
            }
        }

        // The name a group of one number with an old sub-stage gets with its phase in front ("06-2_Hoarding_17-08-26" with
        // phase 3 gives "03_06_2_Hoarding_17-08-26", the stage with two digits, the rest as it is), as Fix phasing names
        // writes it before any word fix, with the sub-stage given (a placeholder such as "<m>") or, when null, as written;
        // null when the name does not start so.
        private static string OneNumberRename(string name, int phase, string subStage)
        {
            var one = OneNumberId.Match(name);
            int stage;
            if (!one.Success || !int.TryParse(one.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out stage))
            {
                return null;
            }
            return phase.ToString("00", CultureInfo.InvariantCulture) + "_" + stage.ToString("00", CultureInfo.InvariantCulture) + "_" + (subStage ?? one.Groups[2].Value)
                + one.Groups[3].Value;
        }

        // "a", "a and b", "a, b and c".
        private static string Joined(IList<string> parts)
        {
            if (parts.Count < 2)
            {
                return string.Join(string.Empty, parts.ToArray());
            }
            return string.Join(", ", parts.Take(parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
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

        // A tag's phase and stage as the messages write them: "Ph2_St06_2".
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

        // A phasing layer's ID as the convention writes it: "06_01_2".
        private static string LayerIdText(PhasingLayerId id)
        {
            return id.Phase.ToString("00", CultureInfo.InvariantCulture) + "_" + id.Stage.ToString("00", CultureInfo.InvariantCulture)
                + (id.Split.HasValue ? "_" + id.Split.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

        /// <returns>The groups of one number with a sub-stage written an old way, which <see cref="CheckOneNumberGroups"/> tells about once their objects are read.</returns>
        private static List<string> CheckGroups(Dictionary<string, PlannerLayerName> datedGroups, PhasingTree phasing, DateTime today, NamingReport report)
        {
            var legacy = new OldForms();
            var oneNumber = new List<string>();
            foreach (var entry in datedGroups.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var name = entry.Key;
                var parsed = entry.Value;
                // A phasing layer's hyphen or full stop is a sub-stage only after its stage (02_05-1, 02_05.1), and Fix
                // phasing names converts it (in the layers of the top node it corrects; those of another top node are
                // renamed by hand); a full stop after the phase (6.06) is a slip that the fixer corrects with the rest of the
                // ID. Any other group is renamed by hand, and its full stop counts only after a two-digit stage, not before
                // "_x_" (GroupSubStageFullStop). A group of one number with an old sub-stage (06-2_..., 05.2_...) needs its
                // phase in front as well, which is told on its own.
                // Where the fixer's top node has its layers, a name it leaves as it is (a layer it cannot move into its phase
                // group, an ID with a number of three digits or more) is renamed by hand for that reason.
                PhasingLayerId layerId;
                if (phasing.Layers.TryGetValue(name, out layerId))
                {
                    if (layerId.OneNumber)
                    {
                        oneNumber.Add(name);
                    }
                    else if (layerId.SplitForm != PlannerLegacyForm.None)
                    {
                        legacy.Add(name, phasing.Corrects(name), layerId.SplitForm, phasing.LeftWhy(name));
                    }
                }
                else if (parsed.LegacyForm == PlannerLegacyForm.Hyphen || (parsed.LegacyForm == PlannerLegacyForm.Dot && GroupSubStageFullStop.IsMatch(name)))
                {
                    if (parsed.Phase.HasValue)
                    {
                        legacy.Add(name, false, parsed.LegacyForm, phasing.LeftWhy(name));
                    }
                    else
                    {
                        oneNumber.Add(name);
                    }
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
            AddOldFormWarning(report, legacy, "group", "01_04-2_...", "01_04.2_...", "01_04_2_...", phasing.FixerTop);

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
            return oneNumber;
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

        // "St6" and "St6_1" want a zero; the digits before any sub-stage are what is padded.
        private static bool NeedsPadding(PlannerMeshSegment segment)
        {
            if (segment == null || segment.Stage >= 10)
            {
                return false;
            }
            int split = segment.StageToken.IndexOfAny(new[] { '_', '-', '.' });
            return (split < 0 ? segment.StageToken.Length : split) < 2;
        }

        private static bool ContainsWhitespace(string text)
        {
            return text.Any(char.IsWhiteSpace);
        }

        // One warning for the names of a kind that write a sub-stage an old way, with a hyphen or a full stop: those Fix
        // phasing names converts and those to rename by hand, each with some of the names. The fixer converts only the
        // layers of its top node (fixerTop) and the objects directly in them. An object's hyphen also breaks its match in
        // Unreal, whose Datasmith import turns it into an underscore; a layer name keeps its hyphens there.
        private static void AddOldFormWarning(NamingReport report, OldForms names, string kind, string hyphenForm, string fullStopForm, string newForm,
            string fixerTop)
        {
            if (names.Total == 0)
            {
                return;
            }
            string with;
            if (names.Hyphen && names.Dot)
            {
                with = "with a hyphen or a full stop (" + hyphenForm + ", " + fullStopForm + ")";
            }
            else
            {
                with = names.Hyphen ? "with a hyphen (" + hyphenForm + ")" : "with a full stop (" + fullStopForm + ")";
            }
            var text = string.Format(CultureInfo.InvariantCulture,
                "{0} {1} name(s) write the sub-stage the old way, {2}; the convention now writes an underscore ({3}). The Planner reads {4} the same",
                names.Total, kind, with, newForm, names.Hyphen && names.Dot ? "them all" : "both");
            text += names.Hyphen && kind == "object"
                ? ", but Unreal's Datasmith import turns a hyphen in an object's name into an underscore, so the names would not match between the Planner and Unreal. "
                : ". ";
            AddConversion(report, names, text, fixerTop);
        }

        // One warning for the tagged objects with a hyphen in their names other than before a tag's sub-stage (those are
        // told with the old forms): Unreal's Datasmith import turns every hyphen in an object's name into an underscore,
        // so the object would carry another name in Unreal than in the Planner. Fix phasing names replaces them in the
        // objects directly in the phasing layers of its top node.
        private static void AddHyphenWarning(NamingReport report, OldForms names, string fixerTop)
        {
            if (names.Total == 0)
            {
                return;
            }
            AddConversion(report, names, string.Format(CultureInfo.InvariantCulture,
                "{0} object name(s) contain a hyphen. Unreal's Datasmith import turns every hyphen in an object's name into an underscore, so these names would not match between the Planner and Unreal: use underscores instead. ",
                names.Total), fixerTop);
        }

        // A gathered warning: its text, then the names Fix phasing names converts and those to rename by hand, with why the
        // fixer leaves them: outside what it corrects (the layers of its top node and the objects directly in them), or the
        // reason given with the name (OldForms.Why), one list for each reason.
        private static void AddConversion(NamingReport report, OldForms names, string text, string fixerTop)
        {
            var scope = "Fix phasing names\u2026 converts only the phasing layers" + (fixerTop != null ? " under '" + fixerTop + "'" : string.Empty)
                + " and the objects directly in them";
            if (names.ByHand.Count == 0)
            {
                text += "Fix phasing names\u2026 on the exporter window converts them: " + PhasingFixScript.FirstNames(names.Converted, NamesShown) + ".";
            }
            else if (names.Converted.Count == 0)
            {
                text += "Rename them by hand" + ByHandList(names, scope, NamesShown) + ".";
            }
            else
            {
                text += string.Format(CultureInfo.InvariantCulture,
                    "Fix phasing names\u2026 on the exporter window converts {0} of them: {1}; rename the other {2} by hand{3}.",
                    names.Converted.Count, PhasingFixScript.FirstNames(names.Converted, NamesShown / 2),
                    names.ByHand.Count, ByHandList(names, scope, NamesShown / 2));
            }
            Add(report, NamingSeverity.Warning, names.First, text);
        }

        // The names to rename by hand after their reason, " (reason): a, b", or, with several reasons, each list followed by
        // its own: ": a, b (reason); c (another reason)".
        private static string ByHandList(OldForms names, string scope, int shown)
        {
            var lists = new List<KeyValuePair<string, List<string>>>();
            foreach (var name in names.ByHand)
            {
                string why;
                if (!names.Why.TryGetValue(name, out why))
                {
                    why = scope;
                }
                var list = lists.FirstOrDefault(l => l.Key == why).Value;
                if (list == null)
                {
                    list = new List<string>();
                    lists.Add(new KeyValuePair<string, List<string>>(why, list));
                }
                list.Add(name);
            }
            if (lists.Count == 1)
            {
                return " (" + lists[0].Key + "): " + PhasingFixScript.FirstNames(lists[0].Value, shown);
            }
            return ": " + string.Join("; ", lists.Select(l => PhasingFixScript.FirstNames(l.Value, shown) + " (" + l.Key + ")").ToArray());
        }

        private static void Add(NamingReport report, NamingSeverity severity, string subject, string message)
        {
            report.Issues.Add(new NamingIssue { Severity = severity, Subject = subject, Message = message });
        }

        /// <summary>
        /// Names to correct for one reason (a sub-stage written an old way, or a hyphen), each once, in the order found,
        /// with the old forms seen.
        /// </summary>
        private sealed class OldForms
        {
            /// <summary>Fix phasing names converts these.</summary>
            public readonly List<string> Converted = new List<string>();
            /// <summary>These are renamed by hand.</summary>
            public readonly List<string> ByHand = new List<string>();
            /// <summary>Why a name renamed by hand is left by the fixer, when it is not that it is outside what the fixer corrects.</summary>
            public readonly Dictionary<string, string> Why = new Dictionary<string, string>(StringComparer.Ordinal);
            /// <summary>The first name found.</summary>
            public string First;
            /// <summary>A sub-stage after a hyphen was seen.</summary>
            public bool Hyphen;
            /// <summary>A sub-stage after a full stop was seen.</summary>
            public bool Dot;
            private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            public int Total { get { return Converted.Count + ByHand.Count; } }

            /// <param name="why">For a name renamed by hand, why the fixer leaves it, or null: it is outside what the fixer corrects.</param>
            public void Add(string name, bool converted, PlannerLegacyForm form, string why = null)
            {
                Hyphen |= form == PlannerLegacyForm.Hyphen;
                Dot |= form == PlannerLegacyForm.Dot;
                if (!seen.Add(name))
                {
                    return;
                }
                if (First == null)
                {
                    First = name;
                }
                (converted ? Converted : ByHand).Add(name);
                if (!converted && why != null)
                {
                    Why[name] = why;
                }
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
            /// <summary>
            /// Where the fixer's top node has its layers, the names the fixer leaves as they are, with why, worded to follow
            /// "rename it by hand (...)": a layer it cannot move into its phase group (also in <see cref="Layers"/>), or a
            /// name that reads as a layer ID it cannot read (not in <see cref="Layers"/>).
            /// </summary>
            public readonly Dictionary<string, string> LeftAlone = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>The layer is under the fixer's top node.</summary>
            public bool UnderFixerTop(string layer)
            {
                string top;
                return FixerTop != null && TopOf.TryGetValue(layer, out top) && top == FixerTop;
            }

            /// <summary>The fixer corrects this layer (and the objects directly in it).</summary>
            public bool Corrects(string layer)
            {
                return UnderFixerTop(layer) && !LeftAlone.ContainsKey(layer);
            }

            /// <summary>Why the fixer leaves this group as it is, or null.</summary>
            public string LeftWhy(string group)
            {
                string why;
                return group != null && LeftAlone.TryGetValue(group, out why) ? why : null;
            }

            /// <summary>
            /// Why the fixer leaves the objects directly in this group as they are when it leaves the group (a layer it
            /// cannot correct), or null.
            /// </summary>
            public string ObjectsLeftWhy(string group)
            {
                var why = LeftWhy(group);
                return why == null ? null : "Fix phasing names\u2026 leaves their layer '" + group + "' as it is: " + why + "; once the layer is corrected, it converts them";
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
