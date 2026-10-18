using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    public static partial class PlannerNamingValidator
    {
        /// <summary>Naming v2: the code of the issue for a number-led node that is no layer ID and holds tagged objects (one of the Planner's layer issues).</summary>
        public const string NotALayerCode = "not-a-layer";
        /// <summary>Naming v2: the code of the issue for layer nodes sharing one ID whose names differ (the Planner's shared IDs).</summary>
        public const string SharedIdCode = "shared-id";
        /// <summary>Naming v2: the code of the issue for a tagged object under no layer's helper (the Planner's unfiled objects).</summary>
        public const string UnfiledCode = "unfiled";

        /// <summary>
        /// The one note the old names' check adds (spec 5.11, D23): the Schedule tab needs the new names, and Clean up scene
        /// graph converts the scene; on a live project only once the planning team says so, as the Planner reads the new names
        /// only from its next release (D25).
        /// </summary>
        public const string OldNamesNote = "This scene uses the old Ph/St names. The Schedule tab needs the new names: Clean up scene graph\u2026 there converts the scene. "
            + "On a live project, wait for the planning team's go-ahead first, as the Planner reads the new names only from its next release.";

        /// <summary>
        /// The export naming check on the new names (naming v2, spec 5.10, with D10 and the rules of spec 2.2), for a scene that
        /// reads as new-name (spec 2.3). It reads the scene as the Planner's Generate reads the exported model
        /// (<see cref="PlannerSchedule.ReadNamesV2"/>): every layer node, empty ones included, and every object with its own
        /// layer. So whatever the Planner lists for the model, the check reports, with the Planner's code and severity:
        ///
        ///   Error   - an illegal name (the same type at both ends, a doubled type word, a type word followed by digits that are
        ///             not an ID, two tags at the end, a layer ID before IN_ or RM_, a tag after a space, nothing between the
        ///             tags), two exported objects with one name, old Ph/St tags (gathered, pointing to Clean up scene graph),
        ///             a number-led node that is no layer ID holding tagged objects, layer nodes sharing an ID with different
        ///             names;
        ///   Warning - a reference to a layer neither the scene nor the imported list has (parked TBC; layer 99_99 is work
        ///             whose date is not known yet), own names shared, the same or nearly the same own names in one layer
        ///             naming different layers (a copy 3ds Max may have renumbered, D10), the old order without letters, a
        ///             reference to the object's own layer, a split piece whose name alone names a layer, a type word in the
        ///             middle, lower-case near-tags, hyphens (gathered), dates or TBC in a helper's name (gathered), a space in
        ///             a layer name, nothing after a layer ID, a tagged object under no layer's helper;
        ///   Note    - underscores at the ends of an own name (gathered).
        ///
        /// and what only 3ds Max shows, as the old names' check has it where the rule is the same:
        ///
        ///   Error   - text after a space that makes two own names one;
        ///   Warning - a layer helper that does not reach the Planner (hidden, or not exported, with nothing exported under it),
        ///             dates or TBC in a Max layer's name, a helper in the phasing tree led by a number that is no layer ID,
        ///             a space in a tagged object's name (as today), two exported groups with one name (as today);
        ///   Note    - a reference to a layer only the imported list has (planned in the Planner, not built yet), an install
        ///             at the end only (Kerb_IN), an untagged object in a layer (as today), a layer that holds no object (the
        ///             Planner makes a folder for it all the same), a layer ID written another way than the current one, a Max
        ///             layer and its helper named differently, layers and helpers that do not pair (as today).
        ///
        /// Layer checks of 3ds Max's own (Max layers, helpers left out of the export, pairing) run only in the phasing tree, so
        /// Max's layer 0 is never checked. The report also counts what the Planner reads (<see cref="NamingReport.Reads"/>) and
        /// the names Clean up scene graph converts (<see cref="NamingReport.CleanUpPointer"/>). Errors first, then warnings,
        /// then notes, each in the order found.
        /// </summary>
        /// <param name="model">The scene as the export hands it to the Planner.</param>
        /// <param name="listLayers">
        /// The imported layer list's Layer column, the Planner's folders: a reference to a layer ID only they have is noted, one
        /// that neither they nor the scene has is warned. Null when no list is imported.
        /// </param>
        public static NamingReport ValidateV2(PlannerScheduleModel model, IEnumerable<string> listLayers)
        {
            var listIds = listLayers == null ? null
                : listLayers.Where(l => l != null).Select(PlannerNaming.FolderLayerId).Where(id => id != null && id.Phase.HasValue).ToList();
            var read = PlannerSchedule.ReadNamesV2(model, listIds);
            var report = new NamingReport { Naming = PlannerNamingKind.New, V2 = read };
            var scene = model.Scene;

            report.TaggedObjects = read.Matched.Count + read.Unfiled.Count;
            report.UnfiledObjects = read.Unfiled.Count;
            report.Layers = read.Layers.Count;
            var holding = new HashSet<string>(read.Nodes.Objects.Values, StringComparer.Ordinal);
            var empty = read.Layers.Where(l => !l.Nodes.Any(holding.Contains)).ToList();
            report.EmptyLayers = empty.Count;
            if (empty.Count > 0)
            {
                // Every layer node reaches the Planner, an empty one too (spec 3.1).
                var names = empty.SelectMany(l => l.Nodes).ToList();
                AddV2(report, NamingSeverity.Note, names[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} no object yet: the Planner makes a folder for {1} all the same (a new folder is flagged TBC): {2}.",
                    PhasingFixScript.Count(empty.Count, "layer holds", "layers hold"), empty.Count == 1 ? "it" : "each", PhasingFixScript.FirstNames(names, NamesShown)), null, names);
            }
            report.Installs = read.Installs;
            report.Removals = read.Removals;
            report.ReferencedLayers = read.ReferencedLayers.Count;
            // Geometry only: an untagged helper or group (the phasing root among them) shows nothing.
            report.Untagged = read.Skipped.Count(n => IsMeshV2(read, n));

            var told = CheckObjectNamesV2(read, listIds != null, report);
            CheckOwnNamesAfterSpaceV2(read, report, told);
            CheckLayerNamesV2(read, model, report);
            CheckLayersLeftOutV2(read, model, report);
            CheckUnfiledV2(read, model, report);
            CheckDuplicatesV2(read, model, report);
            NoteReferencesAndEndsV2(read, listIds, report);
            NoteUntaggedV2(read, report, told);
            CheckPairingV2(scene, report);
            // A name Clean up converts for more than one reason is one name.
            var cleanUp = report.CleanUp.Distinct(StringComparer.Ordinal).ToList();
            report.CleanUp.Clear();
            report.CleanUp.AddRange(cleanUp);

            // Errors first so the log leads with what actually blocks the Planner. OrderBy is stable.
            var ordered = report.Issues.OrderByDescending(i => (int)i.Severity).ToList();
            report.Issues.Clear();
            report.Issues.AddRange(ordered);
            return report;
        }

        private static NamingIssue AddV2(NamingReport report, NamingSeverity severity, string subject, string message, string code, IEnumerable<string> names)
        {
            var issue = new NamingIssue { Severity = severity, Subject = subject, Message = message, Code = code };
            issue.Names.AddRange(names);
            report.Issues.Add(issue);
            return issue;
        }

        // Names in quotes, every one: "'a', 'b' and 'c'".
        private static string AllQuoted(IList<string> names)
        {
            return Joined(names.Select(n => "'" + n + "'").ToList());
        }

        /// <summary>
        /// The objects' naming issues as the Planner lists them (namingIssuesV2): one issue each, an illegal name with how the
        /// Planner reads it; hyphens and underscores at the ends gathered, as are the old Ph/St tags, which the Planner lists
        /// apart. Returns the objects told about a tag the Planner does not read as written, or about a space (a tag after it),
        /// which the checks of spaces and untagged objects then leave alone.
        /// </summary>
        private static HashSet<string> CheckObjectNamesV2(PlannerSceneNamesV2 read, bool withList, NamingReport report)
        {
            var told = new HashSet<string>(StringComparer.Ordinal);
            if (read.OtherNaming.Count > 0)
            {
                bool one = read.OtherNaming.Count == 1;
                AddV2(report, NamingSeverity.Error, read.OtherNaming[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} an old Ph/St tag, which the new names do not read, so the Planner will not schedule {1}: {2}. Clean up scene graph\u2026 on the Schedule tab converts {1}.",
                    PhasingFixScript.Count(read.OtherNaming.Count, "object still carries", "objects still carry"), one ? "it" : "them", PhasingFixScript.FirstNames(read.OtherNaming, NamesShown)),
                    PlannerNaming.CodeText(PlannerNamingCode.OldTag), read.OtherNaming);
                report.CleanUp.AddRange(read.OtherNaming);
            }
            var hyphens = new List<string>();
            var trimmed = new List<string>();
            foreach (var issue in read.NamingIssues)
            {
                var info = PlannerNaming.NamingIssueV2[issue.Code];
                var code = info.Code;
                var first = issue.Names[0];
                switch (issue.Code)
                {
                    case PlannerNamingCode.Hyphen:
                        hyphens.AddRange(issue.Names);
                        break;
                    case PlannerNamingCode.Trimmed:
                        trimmed.AddRange(issue.Names);
                        break;
                    case PlannerNamingCode.DuplicateExport:
                        var maxName = MaxNameOf(read, first);
                        AddV2(report, info.Severity, maxName, string.Format(CultureInfo.InvariantCulture,
                            "'{0}' is the name of {1} exported objects, so the export renames {2} {3}. The Planner reads each as the name says, and their activities share a name where they fall in one layer: give each object its own name. Clean up scene graph\u2026 on the Schedule tab numbers the later ones.",
                            maxName, issue.Names.Count, issue.Names.Count == 2 ? "the second" : "the others", AllQuoted(issue.Names.Skip(1).ToList())), code, issue.Names);
                        report.CleanUp.AddRange(issue.Names.Skip(1));
                        break;
                    case PlannerNamingCode.UnknownLayer:
                        AddV2(report, info.Severity, first, UnknownLayerText(first, Parsed(read, first), withList), code, issue.Names);
                        break;
                    case PlannerNamingCode.PieceNamesLayer:
                        AddV2(report, info.Severity, first, string.Format(CultureInfo.InvariantCulture, "'{0}' (its piece '{1}'): {2}", first, issue.Names[1], info.Text), code, issue.Names);
                        break;
                    case PlannerNamingCode.SharedOwnName:
                        // Clean up numbers the own names of the later ones (D15).
                        AddV2(report, info.Severity, first, AllQuoted(issue.Names) + ": " + info.Text + " Clean up scene graph\u2026 on the Schedule tab numbers the later ones.", code, issue.Names);
                        report.CleanUp.AddRange(issue.Names.Skip(1));
                        break;
                    case PlannerNamingCode.NearOwnNames:
                        AddV2(report, info.Severity, first, AllQuoted(issue.Names) + ": " + info.Text, code, issue.Names);
                        break;
                    default:
                        // One object's own issue. An illegal name is not read as written: say how it is read.
                        var text = "'" + first + "': " + info.Text;
                        if (info.Severity == NamingSeverity.Error)
                        {
                            text += " " + ReadAsText(Parsed(read, first));
                            told.Add(first);
                        }
                        else if (issue.Code == PlannerNamingCode.LowerCase || issue.Code == PlannerNamingCode.TypeInMiddle)
                        {
                            told.Add(first);
                        }
                        AddV2(report, info.Severity, first, text, code, issue.Names);
                        break;
                }
            }
            if (hyphens.Count > 0)
            {
                bool one = hyphens.Count == 1;
                AddV2(report, NamingSeverity.Warning, hyphens[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} a hyphen. Unreal's Datasmith import turns every hyphen in an object's name into an underscore, so {1} would not match between the Planner and Unreal: use underscores instead. Clean up scene graph\u2026 on the Schedule tab converts {2}: {3}.",
                    PhasingFixScript.Count(hyphens.Count, "object name contains", "object names contain"), one ? "the name" : "these names", one ? "it" : "them",
                    PhasingFixScript.FirstNames(hyphens, NamesShown)),
                    PlannerNaming.CodeText(PlannerNamingCode.Hyphen), hyphens);
                report.CleanUp.AddRange(hyphens);
            }
            if (trimmed.Count > 0)
            {
                AddV2(report, NamingSeverity.Note, trimmed[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} underscores at the ends of the name between the tags, which are not part of it: {1}. Clean up scene graph\u2026 on the Schedule tab trims them.",
                    PhasingFixScript.Count(trimmed.Count, "object name has", "object names have"), PhasingFixScript.FirstNames(trimmed, NamesShown)),
                    PlannerNaming.CodeText(PlannerNamingCode.Trimmed), trimmed);
                report.CleanUp.AddRange(trimmed);
            }
            return told;
        }

        // A name the Planner reads is geometry: a mesh in the GLB.
        private static bool IsMeshV2(PlannerSceneNamesV2 read, string name)
        {
            PlannerModelNode node;
            return read.Index.TryGetValue(name, out node) && node.Mesh;
        }

        private static PlannerObjectNameV2 Parsed(PlannerSceneNamesV2 read, string name)
        {
            PlannerObjectNameV2 p;
            return read.Parsed.TryGetValue(name, out p) ? p : PlannerNaming.ParseObjectNameV2(name);
        }

        // The name in 3ds Max of a name the Planner reads: its node's, without the " (n)" the export may have added.
        private static string MaxNameOf(PlannerSceneNamesV2 read, string name)
        {
            PlannerModelNode node;
            return read.Index.TryGetValue(name, out node) ? node.MaxName : PlannerNaming.DuplicateSuffix.Replace(name, string.Empty, 1);
        }

        /// <summary>
        /// How the Planner reads an object's name, for an illegal one: its own name and its events ("The Planner reads it as
        /// 'Kerb', installed while its layer runs and removed in layer 06_05_2."), or that it reads no tag.
        /// </summary>
        internal static string ReadAsText(PlannerObjectNameV2 p)
        {
            if (p == null || !p.Kind.HasValue)
            {
                return "The Planner reads no tag in it, so it shows for the whole programme.";
            }
            var events = PlannerNaming.ObjectEventsV2(p).Select(e =>
            {
                var done = e.Type == PlannerTaskType.Install ? "installed" : "removed";
                return e.When == PlannerEventTime.During ? done + " while its layer runs"
                    : e.When == PlannerEventTime.End ? done + " at the end of its layer"
                    : done + " in layer " + PlannerNaming.LayerIdText(e.Ref);
            }).ToList();
            return "The Planner reads it as '" + p.Own + "', " + Joined(events) + ".";
        }

        // A reference to a layer neither the scene nor the Planner's list has: where the Planner parks the event. Layer 99_99
        // writes work whose date is not known yet (spec 3.6).
        private static string UnknownLayerText(string name, PlannerObjectNameV2 p, bool withList)
        {
            var kind = p.Trail == PlannerTaskType.Install ? "install" : "removal";
            // The export does not know the Planner's dates: the Planner's own rule, as its Generate report says it.
            var where = p.Trail == PlannerTaskType.Install ? "on the project start" : PlannerSchedule.MissingRemovalRule;
            var id = p.Ref != null ? PlannerNaming.LayerIdText(p.Ref) : "?";
            if (id == PlannerNaming.DateUnknownLayer)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "'{0}': its {1} is for layer {2}, work whose date is not known yet: the Planner parks it TBC {3}.", name, kind, id, where);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "'{0}': its {1} names layer {2}, which {3}: the Planner parks it TBC {4}. Add the layer, or correct the tag.", name, kind, id,
                withList ? "neither the scene nor the imported layer list has" : "the scene does not have (no layer list is imported to say whether the Planner has it)", where);
        }

        /// <summary>
        /// Text after a space that makes two own names one (spec 5.10): the Planner reads only the text before the first space,
        /// so "IN_Road Plates" and "IN_Road Barrier" are both the object "Road", and neither follows its own tag change. One
        /// error for each own name so shared, when a name of the group has text after a space and their names differ; and,
        /// as today, a warning for every other tagged object whose name has a space (one told about a tag after its space
        /// already is left out).
        /// </summary>
        private static void CheckOwnNamesAfterSpaceV2(PlannerSceneNamesV2 read, NamingReport report, HashSet<string> told)
        {
            var tagged = read.Matched.Concat(read.Unfiled).ToList();
            Func<string, string> written = n => PlannerNaming.DuplicateSuffix.Replace(n, string.Empty, 1).Trim(PlannerNaming.JavaScriptSpaceChars);
            Func<string, bool> spaced = n => written(n).IndexOfAny(PlannerNaming.JavaScriptSpaceChars) >= 0;
            var shared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in tagged.GroupBy(n => Parsed(read, n).Own, StringComparer.Ordinal))
            {
                var names = group.ToList();
                if (names.Count < 2 || !names.Any(spaced) || names.Select(written).Distinct(StringComparer.Ordinal).Count() < 2)
                {
                    continue;
                }
                AddV2(report, NamingSeverity.Error, names[0], string.Format(CultureInfo.InvariantCulture,
                    "{0}: the Planner reads only the text before a space, so these read as one name between their tags, '{1}', and it cannot tell them apart. Give each a name without spaces.",
                    AllQuoted(names), group.Key), null, names);
                shared.UnionWith(names);
            }
            foreach (var name in tagged.Where(n => spaced(n) && !shared.Contains(n) && !told.Contains(n)))
            {
                var text = written(name);
                AddV2(report, NamingSeverity.Warning, name, string.Format(CultureInfo.InvariantCulture,
                    "'{0}' contains a space. Names must not contain spaces: the Planner reads only '{1}', and the rest is a note.",
                    name, text.Substring(0, text.IndexOfAny(PlannerNaming.JavaScriptSpaceChars))), null, new[] { name });
            }
        }

        /// <summary>
        /// The layer names, as the Planner lists them (its layer issues): dates or TBC in a helper's name (gathered), a space in
        /// it, nothing after its ID, and a number-led node that is no layer ID holding tagged objects, whose objects are not
        /// scheduled. Then what only 3ds Max shows, in the phasing tree: dates or TBC in a Max layer's name, a helper led by a
        /// number that is no layer ID where layers go (directly under the phasing root or a phase group), holding no tagged
        /// object, so the Planner makes no folder for it, and a layer ID written another way than the current one.
        /// </summary>
        private static void CheckLayerNamesV2(PlannerSceneNamesV2 read, PlannerScheduleModel model, NamingReport report)
        {
            var scene = model.Scene;
            var dated = new List<string>();
            foreach (var pair in read.LayerNameIssues)
            {
                var info = PlannerNaming.NamingIssueV2[pair.Value];
                if (pair.Value == PlannerNamingCode.Dates)
                {
                    if (!dated.Contains(pair.Key))
                    {
                        dated.Add(pair.Key);
                    }
                    continue;
                }
                AddV2(report, info.Severity, pair.Key, "Helper '" + pair.Key + "': " + info.Text, info.Code, new[] { pair.Key });
            }
            if (dated.Count > 0)
            {
                AddV2(report, NamingSeverity.Warning, dated[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} still {1} dates or TBC, which the Planner does not read: it holds every date. Clean up scene graph\u2026 on the Schedule tab takes them out: {2}.",
                    PhasingFixScript.Count(dated.Count, "helper name", "helper names"), dated.Count == 1 ? "carries" : "carry", PhasingFixScript.FirstNames(dated, NamesShown)),
                    PlannerNaming.CodeText(PlannerNamingCode.Dates), dated);
                report.CleanUp.AddRange(dated);
            }
            foreach (var pair in read.NotLayers)
            {
                AddV2(report, NamingSeverity.Error, pair.Key, string.Format(CultureInfo.InvariantCulture, "Helper '{0}': {1} Its {2} not scheduled.",
                    pair.Key, PlannerSchedule.NotALayerText(pair.Key), PhasingFixScript.Count(pair.Value, "tagged object is", "tagged objects are")), NotALayerCode, new[] { pair.Key });
            }

            // 3ds Max's own: the layers' Max layers in the phasing tree, whose names Clean up keeps equal to their helpers' and
            // takes the dates out of. A phase group's or another group's name is the Planner's folder name, kept as written
            // (Clean up never takes dates out of it), so it is not pointed to Clean up.
            var helperNames = new HashSet<string>(dated, StringComparer.OrdinalIgnoreCase);
            var maxDated = scene.Layers.Where(l => scene.TreeLayers.Contains(l.Name) && PlannerNaming.ParseLayerName(l.Name) != null
                    && PlannerNaming.LayerNameCarriesDates(l.Name) && !helperNames.Contains(l.Name))
                .Select(l => l.Name).ToList();
            if (maxDated.Count > 0)
            {
                AddV2(report, NamingSeverity.Warning, maxDated[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} in the phasing tree still {1} dates or TBC: {2}. Clean up scene graph\u2026 on the Schedule tab takes them out, with their helpers'.",
                    PhasingFixScript.Count(maxDated.Count, "Max layer name", "Max layer names"), maxDated.Count == 1 ? "carries" : "carry", PhasingFixScript.FirstNames(maxDated, NamesShown)),
                    null, maxDated);
                report.CleanUp.AddRange(maxDated);
            }
            var notLayers = new HashSet<string>(read.NotLayers.Select(p => p.Key), StringComparer.Ordinal);
            foreach (var name in read.Nodes.Skipped.Where(n => !notLayers.Contains(n)))
            {
                var node = SceneNodeOf(read, model, name);
                var parent = node != null ? scene.ParentOf(node) : null;
                if (node == null || !scene.TreeNodes.Contains(node.Key) || !node.IsHelper || scene.IsObjectGroup(node) || parent == null
                    || !(parent.Key == scene.RootNode.Key || PlannerSchedule.ParsePhaseGroup(parent.Name) != null))
                {
                    continue;
                }
                AddV2(report, NamingSeverity.Warning, name, string.Format(CultureInfo.InvariantCulture,
                    "Helper '{0}' is not a layer with the new names ({1}), so the Planner makes no folder for it.", name, PlannerSchedule.NotALayerWhy(name)), null, new[] { name });
            }
            var rewritten = new List<string>();
            var rewrittenAs = new List<string>();
            foreach (var layer in read.Layers)
            {
                foreach (var name in layer.Nodes)
                {
                    var p = PlannerNaming.ParseLayerNameV2(name);
                    var written = PlannerNaming.DuplicateSuffix.Replace(name, string.Empty, 1).Trim(PlannerNaming.JavaScriptSpaceChars);
                    if (p != null && !written.StartsWith(PlannerNaming.LayerIdText(p.Id) + (p.Name.Length > 0 ? "_" : string.Empty), StringComparison.Ordinal))
                    {
                        rewritten.Add(name);
                        rewrittenAs.Add(name + " (" + p.Label + ")");
                    }
                }
            }
            if (rewritten.Count > 0)
            {
                AddV2(report, NamingSeverity.Note, rewritten[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} the layer ID another way than the current one (two-digit phase and stage, then the sub-stage): {1}. The Planner reads {2} the same; Clean up scene graph\u2026 on the Schedule tab writes {2} the current way.",
                    PhasingFixScript.Count(rewritten.Count, "helper name writes", "helper names write"), PhasingFixScript.FirstNames(rewrittenAs, NamesShown), rewritten.Count == 1 ? "it" : "them"),
                    null, rewritten);
                report.CleanUp.AddRange(rewritten);
            }
        }

        // The scene node a name the Planner reads stands for, or null.
        private static PlannerSceneNode SceneNodeOf(PlannerSceneNamesV2 read, PlannerScheduleModel model, string name)
        {
            PlannerModelNode modelNode;
            PlannerSceneNode node;
            return read.Index.TryGetValue(name, out modelNode) && model.Scene.NodeByKey.TryGetValue(modelNode.Key, out node) ? node : null;
        }

        /// <summary>
        /// The layer helpers in the phasing tree that do not reach the Planner (spec 3.6, 5.10): left out of the export
        /// (hidden while Export hidden objects is off, Do not export, outside what this export takes) with nothing exported
        /// under them. The Planner gets no folder for such a layer, and parks TBC any work named for it, unless its list has it.
        /// One warning for them all.
        /// </summary>
        private static void CheckLayersLeftOutV2(PlannerSceneNamesV2 read, PlannerScheduleModel model, NamingReport report)
        {
            var scene = model.Scene;
            var left = new List<string>();
            var said = new List<string>();
            foreach (var node in scene.Nodes)
            {
                if (!node.IsHelper || !scene.TreeNodes.Contains(node.Key) || model.ByKey.ContainsKey(node.Key) || PlannerNaming.ParseLayerNameV2(node.Name) == null)
                {
                    continue;
                }
                string why;
                if (!model.LeftOut.TryGetValue(node.Key, out why))
                {
                    why = "a node above it is left out of the export";
                }
                left.Add(node.Name);
                said.Add("'" + node.Name + "' (" + why + ")");
            }
            if (left.Count > 0)
            {
                AddV2(report, NamingSeverity.Warning, left[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} will not reach the Planner, as {1} left out of the export with nothing exported under {2}: the Planner makes no folder for {2}, and parks TBC any work named for {2} that its layer list does not place: {3}. Show the helper or tick Export hidden objects, untick Do not export, or take it into the export.",
                    PhasingFixScript.Count(left.Count, "layer helper", "layer helpers"), left.Count == 1 ? "it is" : "they are", left.Count == 1 ? "it" : "them",
                    PhasingFixScript.FirstNames(said, NamesShown)), null, left);
            }
        }

        /// <summary>
        /// The tagged objects under no layer's helper, one warning each, as the Planner lists them (unfiled): under no helper
        /// named &lt;ID&gt;_&lt;Name&gt;, or under a number-led node that is no layer ID (which says why), with a hint when the
        /// object sits on a phasing Max layer (Link objects to their helpers moves it under the layer's helper).
        /// </summary>
        private static void CheckUnfiledV2(PlannerSceneNamesV2 read, PlannerScheduleModel model, NamingReport report)
        {
            var skipped = new HashSet<string>(read.Nodes.Skipped, StringComparer.Ordinal);
            foreach (var name in read.Unfiled)
            {
                var text = "'" + name + "' is not linked under a layer's helper (a helper named <ID>_<Name>, the ID two or three numbers), so the Planner lists it as unfiled and cannot schedule it.";
                PlannerModelNode modelNode;
                read.Index.TryGetValue(name, out modelNode);
                string under = null;
                int hops = 0;
                for (var p = modelNode != null ? modelNode.Parent : null; p != null && hops < 64; p = p.Parent, hops++)
                {
                    if (skipped.Contains(p.Name))
                    {
                        under = p.Name;
                        break;
                    }
                }
                var node = SceneNodeOf(read, model, name);
                var unit = node != null && node.LayerName != null ? model.Scene.UnitOfLayer(node.LayerName) : null;
                if (under != null)
                {
                    text += " It sits under '" + under + "', which is not a layer with the new names: " + PlannerSchedule.NotALayerWhy(under) + ".";
                }
                else if (unit != null && unit.InPhasingTree && PlannerNaming.ParseLayerNameV2(unit.Name) != null)
                {
                    text += " It sits on the Max layer '" + node.LayerName + "': Link objects to their helpers on the Schedule tab puts it under the layer's helper.";
                }
                AddV2(report, NamingSeverity.Warning, name, text, UnfiledCode, new[] { name });
            }
        }

        /// <summary>
        /// Layer nodes sharing an ID with different names (spec 3.1): the Planner reads them as one layer, with one folder,
        /// and lists them; one error each. A helper copied in 3ds Max ("X (1)" in the GLB) is the same layer, not a clash.
        /// Then, as today, two exported objects with one name that the Planner does not list (untagged ones: tagged ones are
        /// its duplicate-export issue), an error each, and two exported groups with one name, a warning each (layer nodes,
        /// phase groups and objects left out).
        /// </summary>
        private static void CheckDuplicatesV2(PlannerSceneNamesV2 read, PlannerScheduleModel model, NamingReport report)
        {
            foreach (var shared in read.SharedIds)
            {
                AddV2(report, NamingSeverity.Error, shared.Names[0], string.Format(CultureInfo.InvariantCulture,
                    "Helpers {0} share the layer ID {1} but not the name after it: the Planner reads them as one layer, with one folder named after the first. Give each layer its own ID, or one name.",
                    AllQuoted(shared.Names), shared.Id), SharedIdCode, shared.Names);
            }
            var listed = new HashSet<string>(read.NamingIssues.Where(i => i.Code == PlannerNamingCode.DuplicateExport).SelectMany(i => i.Names), StringComparer.Ordinal);
            foreach (var duplicate in model.Nodes.Where(n => n.Mesh).GroupBy(n => n.MaxName, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                if (duplicate.Any(n => listed.Contains(n.Name)))
                {
                    continue;
                }
                AddV2(report, NamingSeverity.Error, duplicate.Key, string.Format(CultureInfo.InvariantCulture,
                    "'{0}' is the name of {1} objects. Every exported object needs a unique name: the Planner binds activities to objects by name.", duplicate.Key, duplicate.Count()),
                    null, duplicate.Select(n => n.Name));
            }
            var objects = new HashSet<string>(read.Objects.Select(o => o.Name), StringComparer.Ordinal);
            foreach (var duplicate in model.Nodes.Where(n => !n.Mesh).GroupBy(n => n.MaxName, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                if (PlannerNaming.ParseLayerNameV2(duplicate.Key) != null || PlannerSchedule.ParsePhaseGroup(duplicate.Key) != null
                    || duplicate.Any(n => listed.Contains(n.Name) || (objects.Contains(n.Name) && Parsed(read, n.Name).Kind.HasValue)))
                {
                    continue;
                }
                AddV2(report, NamingSeverity.Warning, duplicate.Key, string.Format(CultureInfo.InvariantCulture,
                    "'{0}' is the name of {1} groups; give each group its own name.", duplicate.Key, duplicate.Count()), null, duplicate.Select(n => n.Name));
            }
        }

        /// <summary>
        /// Two notes on what scheduled objects' tags say: a reference to a layer only the imported list has (planned in the
        /// Planner, not built in 3ds Max yet), where the Planner files that work; and an install at the end of the object's own
        /// layer only ("Kerb_IN"), which writing "IN_Kerb" makes an install while the layer runs.
        /// </summary>
        private static void NoteReferencesAndEndsV2(PlannerSceneNamesV2 read, List<PlannerLayerIdKey> listIds, NamingReport report)
        {
            var sceneIds = new HashSet<string>(read.Layers.Select(l => PlannerNaming.LayerIdKey(l.Id)), StringComparer.Ordinal);
            var listKeys = new HashSet<string>((listIds ?? new List<PlannerLayerIdKey>()).Select(PlannerNaming.LayerIdKey), StringComparer.Ordinal);
            var planned = new List<string>();
            var said = new List<string>();
            var ends = new List<string>();
            foreach (var name in read.Matched)
            {
                var p = Parsed(read, name);
                if (p.Ref != null && !sceneIds.Contains(PlannerNaming.LayerIdKey(p.Ref)) && listKeys.Contains(PlannerNaming.LayerIdKey(p.Ref)))
                {
                    planned.Add(name);
                    said.Add("'" + name + "' (" + PlannerNaming.LayerIdText(p.Ref) + ")");
                }
                if (!p.Lead.HasValue && p.Trail == PlannerTaskType.Install && p.Ref == null)
                {
                    ends.Add(name);
                }
            }
            if (planned.Count > 0)
            {
                AddV2(report, NamingSeverity.Note, planned[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} only the imported layer list has (planned in the Planner, not built in 3ds Max yet), where the Planner files that work: {1}.",
                    PhasingFixScript.Count(planned.Count, "object names a layer", "objects name layers"), PhasingFixScript.FirstNames(said, NamesShown)), null, planned);
            }
            if (ends.Count > 0)
            {
                AddV2(report, NamingSeverity.Note, ends[0], string.Format(CultureInfo.InvariantCulture,
                    "{0} installed at the end of {1} layer, as a closing _IN with no leading tag says (Kerb_IN); to install one while its layer runs, start the name with IN_ instead: {2}.",
                    PhasingFixScript.Count(ends.Count, "object is", "objects are"), ends.Count == 1 ? "its" : "their", PhasingFixScript.FirstNames(ends, NamesShown)), null, ends);
            }
        }

        /// <summary>
        /// As today, a note for each untagged object in a layer: the Planner shows it for the whole programme. Only geometry,
        /// as today (an untagged helper or group is no slip), and not one linked under a scheduled object (shown and hidden
        /// with it), nor one the Planner lists for a tag it does not read.
        /// </summary>
        private static void NoteUntaggedV2(PlannerSceneNamesV2 read, NamingReport report, HashSet<string> told)
        {
            var scheduled = new HashSet<string>(read.Matched, StringComparer.Ordinal);
            var issues = new HashSet<string>(read.NamingIssues.SelectMany(i => i.Names), StringComparer.Ordinal);
            foreach (var name in read.Skipped)
            {
                string layer;
                if (!read.Nodes.Objects.TryGetValue(name, out layer) || told.Contains(name) || issues.Contains(name) || !IsMeshV2(read, name))
                {
                    continue;
                }
                var node = read.Index[name];
                bool underScheduled = false;
                int hops = 0;
                for (var p = node != null ? node.Parent : null; p != null && hops < 64 && !underScheduled; p = p.Parent, hops++)
                {
                    underScheduled = scheduled.Contains(p.Name);
                }
                if (underScheduled)
                {
                    continue;
                }
                AddV2(report, NamingSeverity.Note, name, string.Format(CultureInfo.InvariantCulture,
                    "'{0}' sits in the layer '{1}' but has no IN or RM tag, so the Planner shows it for the whole programme.", name, layer), null, new[] { name });
            }
        }

        /// <summary>
        /// The phasing tree's Max layers and helpers: counted as the export log has always counted them (the Max layers in the
        /// phasing root layer's tree, the helpers under the phasing root node), with a note on those that do not pair up by
        /// name, as today; a layer and its helper paired by the scene's units (by ID, or the helper sitting on the layer)
        /// whose names differ get a note of their own, as on the new names the two share one name and the Planner reads only
        /// the helper's.
        /// </summary>
        private static void CheckPairingV2(PlannerSceneModel scene, NamingReport report)
        {
            if (scene.RootNode == null)
            {
                return;
            }
            var counts = new PhasingCounts { TopName = scene.RootNode.Name, TopLayerName = scene.RootLayer != null ? scene.RootLayer.Name : null };
            var helpers = new List<string>();
            var seen = new HashSet<long> { scene.RootNode.Key };
            var pending = new Stack<PlannerSceneNode>(Enumerable.Reverse(scene.ChildrenOf(scene.RootNode)));
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!seen.Add(node.Key) || scene.LeftOut(node))
                {
                    continue;
                }
                if (node.IsHelper || node.IsShape)
                {
                    helpers.Add(node.Name);
                }
                foreach (var child in Enumerable.Reverse(scene.ChildrenOf(node)))
                {
                    pending.Push(child);
                }
            }
            counts.Helpers = helpers.Count;
            report.Counts = counts;
            if (scene.RootLayer == null)
            {
                return;
            }
            var layers = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { scene.RootLayer.Name };
            var waiting = new Stack<PhasingMaxLayer>(Enumerable.Reverse(scene.ChildLayersOf(scene.RootLayer.Name).ToList()));
            while (waiting.Count > 0)
            {
                var layer = waiting.Pop();
                if (!visited.Add(layer.Name))
                {
                    continue;
                }
                layers.Add(layer.Name);
                foreach (var child in Enumerable.Reverse(scene.ChildLayersOf(layer.Name).ToList()))
                {
                    waiting.Push(child);
                }
            }
            counts.Layers = layers.Count;

            var differ = scene.Units.Where(u => u.InPhasingTree && u.Layer != null && u.Helper != null && !string.Equals(u.Layer.Name, u.Helper.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var differLayers = new HashSet<string>(differ.Select(u => u.Layer.Name), StringComparer.OrdinalIgnoreCase);
            var differHelpers = new HashSet<string>(differ.Select(u => u.Helper.Name), StringComparer.OrdinalIgnoreCase);
            var helperNames = new HashSet<string>(helpers, StringComparer.OrdinalIgnoreCase);
            var layerNames = new HashSet<string>(layers, StringComparer.OrdinalIgnoreCase);
            counts.LayersWithoutHelper.AddRange(layers.Where(n => !helperNames.Contains(n) && !differLayers.Contains(n)));
            counts.HelpersWithoutLayer.AddRange(helpers.Where(n => !layerNames.Contains(n) && !differHelpers.Contains(n)).Distinct(StringComparer.Ordinal));
            if (differ.Count > 0)
            {
                var pairs = differ.Select(u => "'" + u.Layer.Name + "' and '" + u.Helper.Name + "'").ToList();
                AddV2(report, NamingSeverity.Note, differ[0].Layer.Name, string.Format(CultureInfo.InvariantCulture,
                    "{0} named differently: {1}. On the new names a layer and its helper share one name, <ID>_<Name>, and the Planner reads only the helper's.",
                    PhasingFixScript.Count(differ.Count, "Max layer and its helper are", "Max layers and their helpers are"), PhasingFixScript.FirstNames(pairs, NamesShown)),
                    null, differ.SelectMany(u => new[] { u.Layer.Name, u.Helper.Name }));
            }
            var pairing = counts.PairingNote();
            if (pairing != null)
            {
                AddV2(report, NamingSeverity.Note, counts.TopName, pairing, null, counts.LayersWithoutHelper.Concat(counts.HelpersWithoutLayer));
            }
        }
    }
}
