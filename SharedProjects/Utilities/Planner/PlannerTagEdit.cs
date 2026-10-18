using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>Put objects into a layer's IN or RM column, or take them out of it.</summary>
    public enum PlannerTagEditKind
    {
        Add,
        Remove
    }

    /// <summary>
    /// The layer an IN / RM edit is about (naming v2, spec 5.2): its layer ID, which a tag naming it writes, and the preview's
    /// folder for it, where its IN and RM columns are read. A layer only the Planner's list has is one too: the tag at the end
    /// of an object's name can name it before the scene has a Max layer for it.
    /// </summary>
    public sealed class PlannerTagTarget
    {
        /// <summary>The layer's name as the tab shows it: its layer node's (its helper's) in the scene, else the list row's.</summary>
        public string LayerName;
        /// <summary>The name of the preview's folder for it, by which an activity's place is read (<see cref="PlannerSchedule.PlaceOf"/>).</summary>
        public string Label;
        /// <summary>Its layer ID, with its phase.</summary>
        public PlannerLayerIdKey Id;
        /// <summary>The scene has a layer node of its ID; false for a layer only the Planner's list has (planned in the Planner).</summary>
        public bool InScene;

        /// <summary>"06_05_2": its ID written the current way, as a tag naming it writes it.</summary>
        public string IdText
        {
            get { return PlannerNaming.LayerIdText(Id); }
        }

        /// <summary>The target for a layer of the new names' preview; null for null and for a folder with no layer ID.</summary>
        public static PlannerTagTarget Of(PlannerScheduleLayer layer)
        {
            if (layer == null || layer.Id == null || !layer.Id.Phase.HasValue)
            {
                return null;
            }
            return new PlannerTagTarget { LayerName = layer.Raw, Label = layer.Label, Id = layer.Id, InScene = layer.InScene };
        }

        /// <summary>
        /// The target for a row of the Schedule tab: the preview's layer it stands for (found by its helper, its list row or
        /// its Max layer's ID, as the rows are filled), else the layer its Max layer's name reads as. Null when it stands for
        /// no layer (a phase group).
        /// </summary>
        public static PlannerTagTarget For(PlannerScheduleRow row, PlannerSchedule schedule)
        {
            if (row == null)
            {
                return null;
            }
            if (row.Layer != null)
            {
                return Of(row.Layer);
            }
            var parsed = row.LayerName != null ? PlannerNaming.ParseLayerNameV2(row.LayerName) : null;
            if (parsed == null)
            {
                return null;
            }
            var key = PlannerNaming.LayerIdKey(parsed.Id);
            var layer = schedule != null ? schedule.Layers.FirstOrDefault(l => l.InScene && l.Id != null && PlannerNaming.LayerIdKey(l.Id) == key) : null;
            return layer != null ? Of(layer) : new PlannerTagTarget { LayerName = row.LayerName, Label = parsed.Label, Id = parsed.Id, InScene = true };
        }
    }

    /// <summary>One object to put into, or take out of, a layer's IN or RM column.</summary>
    public sealed class PlannerTagEditRequest
    {
        /// <summary>The object's node handle.</summary>
        public long Key;
        public PlannerTaskType Type;
        public PlannerTagEditKind Kind;
        public PlannerTagTarget Target;
    }

    /// <summary>An object renamed so that its tags say what the edit asks.</summary>
    public sealed class PlannerTagRename
    {
        public long Key;
        public string OldName;
        public string NewName;
        public string Why;
    }

    /// <summary>What an IN / RM edit would do: the renames, each checked by reading the scene again as the Planner would.</summary>
    public sealed class PlannerTagEditPlan
    {
        public readonly List<PlannerTagRename> Renames = new List<PlannerTagRename>();
        /// <summary>The renames, then the problems (skipped), then other objects the Planner reads anew.</summary>
        public readonly List<PlannerReviewRow> Review = new List<PlannerReviewRow>();
        public readonly List<string> Notes = new List<string>();
        public int ProblemCount;
        public int Unchanged;
        /// <summary>The new names' preview with the renames made, as the Planner would read the scene.</summary>
        public PlannerSchedule After;

        public bool HasChanges
        {
            get { return Renames.Count > 0; }
        }

        /// <summary>One line, e.g. "3 objects to rename, 1 problem."</summary>
        public string Summary()
        {
            var text = PhasingFixScript.Count(Renames.Count, "object to rename", "objects to rename");
            if (Unchanged > 0)
            {
                text += ", " + PhasingFixScript.Count(Unchanged, "already as asked", "already as asked");
            }
            return text + ", " + PhasingFixScript.Count(ProblemCount, "problem", "problems") + ".";
        }

        public string Fingerprint()
        {
            var text = new StringBuilder();
            foreach (var rename in Renames)
            {
                text.Append(rename.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(rename.OldName).Append('\t').Append(rename.NewName).Append('\n');
            }
            foreach (var row in Review)
            {
                text.Append(row.Status).Append('\t').Append(row.Name).Append('\t').Append(row.Why).Append('\n');
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// The IN and RM columns edited by renaming objects, on the new names (naming v2, spec 5.2): an object's tags are what the
    /// Planner's Generate from model reads, so the lists and the names always agree. L is the layer chosen, Lo the object's
    /// own layer (the layer node it sits under) and T the type:
    ///  - Add T for L: the object's T tag comes out wherever it is (one install and one removal per object, D8), then T goes
    ///    in. For Lo at the start (IN_X) if that place is free, else at the end (X_IN: at the end of Lo), else it is refused;
    ///    for another layer at the end, naming it (X_IN_06_05_2, a layer only the Planner's list has included), if that place
    ///    is free, else it is refused, as an object has one leading and one trailing tag. An object whose T is in L already
    ///    keeps its name; one whose tag at the end names its own layer gets the plain trailing tag it means (X_RM).
    ///  - Remove T: its T tag comes out where it is. The other tag stays where it is, as a tag's place carries its meaning;
    ///    an object left with no tag is no longer scheduled, which the review says.
    /// The own name, and any note after a space or " (n)" after it, stay as written; a tag at the end goes before the note.
    /// Every new name is read back as the Planner reads it (<see cref="PlannerNaming.ParseObjectNameV2"/>): one that would
    /// not read as meant (the same type at both ends, another own name, a type word read where none was meant, a new
    /// naming error) is refused. Refused too: a name with an old Ph/St tag (Clean up scene graph converts it), a layer's
    /// helper, the phasing root, a layer node, a split piece chosen alone and an object under no layer's helper (link it
    /// first). Nothing is moved or linked.
    ///
    /// Each edit is then checked by reading the scene again with the new names, as the new names' preview reads it
    /// (<see cref="PlannerSchedule.BuildV2(PlannerSceneSnapshot, PlannerScheduleOptions)"/>): an object that would not then
    /// be in the column asked for (the export leaves it out, the Planner files the work in another folder of that ID, a name
    /// another node has) is a Problem and keeps its name; other objects the renames make the Planner read differently are
    /// listed, each with why. Objects are known by the names the GLB gives them (a second node of a name is "X (1)", an
    /// object of its own). The review notes an object left with no tag, one whose own name another object shares (the
    /// Planner cannot follow a tag change on it, spec 4.1), naming issues the new names bring, two events the preview then
    /// runs the other way round from the name, and a layer the preview parks.
    ///
    /// What an Add takes as the object: selecting a closed group in 3ds Max selects its head and every member, and a
    /// hierarchy may be selected whole, so a node under another node being added is part of it (the Planner shows and
    /// hides it with it) and is left out of the edit, unless it has a tag of its own: then it is its own object, kept
    /// when it is not in a group, and a Problem inside a closed group (open the group to choose). So a closed group is
    /// tagged on its head. A split piece (Name_2 under Name, read as part of Name) is renamed with its object, so it stays
    /// part of it; chosen alone, it is a Problem. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerTagEdit
    {
        public const string UndoLabel = "Planner IN / RM";
        public const string EndMarker = "PLANNERTAGS-END";
        private const string Nl = "\r\n";

        /// <summary>Why a name with an old Ph/St tag is not edited.</summary>
        public const string OldTagProblem = "it still carries an old Ph/St tag, which the new names do not read: Clean up scene graph\u2026 converts it first";

        /// <summary>Why an object under no layer's helper is not given a tag.</summary>
        public const string NoLayerProblem = "it is not under any layer's helper, so the Planner cannot place it: link it under its layer's helper first (Link objects to their helpers\u2026)";

        // Where a name read the new way puts its event of one type: while its own layer runs, at the end of it, or in the
        // layer its trailing tag names ("in " and the layer's key).
        private const string During = "during";
        private const string End = "end";

        private static string In(PlannerLayerIdKey layer)
        {
            return "in " + PlannerNaming.LayerIdKey(layer);
        }

        private static string EventOf(PlannerObjectNameV2 p, PlannerTaskType type)
        {
            if (p.Kind != PlannerNamingKind.New)
            {
                return null;
            }
            if (p.Lead == type)
            {
                return During;
            }
            if (p.Trail == type)
            {
                return p.Ref == null ? End : In(p.Ref);
            }
            return null;
        }

        // The event said plainly: "installed while its own layer runs", "removed in layer 06_05_2", "not installed".
        private static string EventText(PlannerTaskType type, PlannerObjectNameV2 p)
        {
            var verb = type == PlannerTaskType.Install ? "installed" : "removed";
            var at = EventOf(p, type);
            if (at == null)
            {
                return "not " + verb;
            }
            if (at == During)
            {
                return verb + " while its own layer runs";
            }
            return at == End ? verb + " at the end of its own layer" : verb + " in layer " + PlannerNaming.LayerIdText(p.Ref);
        }

        private static bool SameLayer(PlannerLayerIdKey a, PlannerLayerIdKey b)
        {
            return a != null && b != null && a.Phase.HasValue && b.Phase.HasValue && PlannerNaming.LayerIdKey(a) == PlannerNaming.LayerIdKey(b);
        }

        private static string Word(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "IN" : "RM";
        }

        private static PlannerTaskType Other(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? PlannerTaskType.Dismantle : PlannerTaskType.Install;
        }

        private static string Kind(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "install" : "removal";
        }

        private static string Column(PlannerTaskType type)
        {
            return Word(type);
        }

        // A name as its tags sit in it (spec 2.2): the part the Planner reads (up to the first whitespace, once one " (n)" is
        // off) as its leading tag, what lies between the tags and its trailing tag, and the text around that part as written.
        private sealed class Parts
        {
            public string Before = string.Empty;
            public string Lead = string.Empty;
            public string Middle = string.Empty;
            public string Trail = string.Empty;
            public string After = string.Empty;
            public PlannerTaskType? LeadType;
            public PlannerTaskType? TrailType;

            // The name with other tags, everything else as written.
            public string With(string lead, string trail)
            {
                return Before + lead + Middle + trail + After;
            }

            // "RM_..._RM" (with an ellipsis): the tags as written.
            public string Tags
            {
                get { return Lead + "\u2026" + Trail; }
            }
        }

        private static Parts Split(string name)
        {
            var text = name ?? string.Empty;
            var suffix = PlannerNaming.DuplicateSuffix.Match(text);
            var body = suffix.Success ? text.Substring(0, suffix.Index) : text;
            int start = 0;
            while (start < body.Length && Array.IndexOf(PlannerNaming.JavaScriptSpaceChars, body[start]) >= 0)
            {
                start++;
            }
            int space = body.IndexOfAny(PlannerNaming.JavaScriptSpaceChars, start);
            var read = space < 0 ? body.Substring(start) : body.Substring(start, space - start);
            var parts = new Parts
            {
                Before = body.Substring(0, start),
                After = (space < 0 ? string.Empty : body.Substring(space)) + (suffix.Success ? text.Substring(suffix.Index) : string.Empty)
            };
            var lead = PlannerNaming.TagLeadV2.Match(read);
            var rest = read;
            if (lead.Success)
            {
                parts.Lead = lead.Value;
                parts.LeadType = lead.Groups[1].Value == "IN" ? PlannerTaskType.Install : PlannerTaskType.Dismantle;
                rest = read.Substring(lead.Length);
            }
            // Pinned to the end, and never the whole rest, as the reader takes it.
            var trail = PlannerNaming.TagTrailV2.Match(rest);
            if (trail.Success && trail.Index > 0)
            {
                parts.Middle = rest.Substring(0, trail.Index);
                parts.Trail = trail.Value;
                parts.TrailType = trail.Groups[1].Value == "IN" ? PlannerTaskType.Install : PlannerTaskType.Dismantle;
            }
            else
            {
                parts.Middle = rest;
            }
            return parts;
        }

        private static string BothEnds(PlannerTaskType type, Parts parts)
        {
            return "its name has " + Word(type) + " at both ends (" + parts.Tags + "), which the Planner reads as the one at the start only: edit its name by hand";
        }

        // The new name read back as the Planner reads it: its event of the type edited where meant (null: none), its other
        // event, its own name and its errors as they were. Null when it does; else why not.
        private static string ReadBack(PlannerObjectNameV2 before, string name, PlannerLayerIdKey ownLayer, PlannerTaskType type, string meant)
        {
            var after = PlannerNaming.ParseObjectNameV2(name, ownLayer);
            var other = Other(type);
            var wrong = new List<string>();
            if (EventOf(after, type) != meant)
            {
                wrong.Add(EventText(type, after));
            }
            if (EventOf(after, other) != EventOf(before, other))
            {
                wrong.Add(EventText(other, after) + " (it is " + EventText(other, before) + " now)");
            }
            if (wrong.Count > 0)
            {
                return "the new name " + name + " would read as " + string.Join(" and ", wrong.ToArray()) + ", which is not what was asked: edit its name by hand";
            }
            if (after.Kind == PlannerNamingKind.New && !string.Equals(after.Own, before.Own, StringComparison.Ordinal))
            {
                return "the new name " + name + " would read with the own name " + after.Own + " (it is " + before.Own + " now): edit its name by hand";
            }
            var errors = after.Issues.Where(c => PlannerNaming.NamingIssueV2[c].Severity == NamingSeverity.Error && !before.Issues.Contains(c)).Distinct().ToList();
            if (errors.Count > 0)
            {
                return "the new name " + name + " would be a naming error (" + string.Join(" ", errors.Select(c => PlannerNaming.NamingIssueV2[c].Text).ToArray()).TrimEnd('.')
                    + "): edit its name by hand";
            }
            return null;
        }

        /// <summary>
        /// The name with its install (or removal) in <paramref name="layer"/> (spec 5.2), for an object whose own layer is
        /// <paramref name="ownLayer"/> (null: under none, so any layer is another): its tag of that type comes out wherever it
        /// is, then goes in at the start (IN_X) for its own layer, or at the end (X_IN) when the start holds the other type;
        /// at the end naming the layer (X_IN_06_05_2) for another layer. An object with that event in that layer already keeps
        /// its name, except that a tag at the end naming its own layer becomes the plain one it means (X_RM_02_01 in 02_01:
        /// X_RM). False with the reason when it cannot be written so, or the new name would not read back as meant (then
        /// <paramref name="result"/> is the name refused, or the name as it is when there is none).
        /// </summary>
        public static bool TrySetTag(string name, PlannerTaskType type, PlannerLayerIdKey ownLayer, PlannerLayerIdKey layer, out string result, out string problem)
        {
            var text = name ?? string.Empty;
            result = text;
            problem = null;
            if (layer == null || !layer.Phase.HasValue)
            {
                problem = "the layer has no layer ID to write";
                return false;
            }
            if (PlannerNaming.IsOldKindName(text))
            {
                problem = OldTagProblem;
                return false;
            }
            var parts = Split(text);
            if (parts.LeadType == type && parts.TrailType == type)
            {
                problem = BothEnds(type, parts);
                return false;
            }
            var before = PlannerNaming.ParseObjectNameV2(text, ownLayer);
            var word = Word(type);
            bool own = SameLayer(ownLayer, layer);
            var now = EventOf(before, type);
            string lead, trail, meant;
            if (own && (now == During || now == End))
            {
                // In its own layer already. A tag at the end naming its own layer reads as the plain one: written so.
                if (now == During || parts.TrailType != type || !before.Issues.Contains(PlannerNamingCode.OwnLayer))
                {
                    return true;
                }
                lead = parts.Lead;
                trail = "_" + word;
                meant = End;
            }
            else if (!own && now == In(layer))
            {
                return true;
            }
            else
            {
                // Its tag of this type comes out, wherever it is (one install and one removal per object, D8).
                lead = parts.LeadType == type ? string.Empty : parts.Lead;
                trail = parts.TrailType == type ? string.Empty : parts.Trail;
                if (own && lead.Length == 0)
                {
                    lead = word + "_";
                    meant = During;
                }
                else if (own && trail.Length == 0)
                {
                    trail = "_" + word;
                    meant = End;
                }
                else if (own)
                {
                    problem = "both ends of its name hold a tag already (" + parts.Tags + "), so there is no place for " + word
                        + ": an object has one leading and one trailing tag; edit its name by hand";
                    return false;
                }
                else if (trail.Length > 0)
                {
                    problem = "the end of its name holds its " + Kind(Other(type)) + " tag (" + trail.Substring(1) + "), and only a tag at the end can name another layer: "
                        + "an object has one leading and one trailing tag, so take that one out first";
                    return false;
                }
                else
                {
                    trail = "_" + word + "_" + PlannerNaming.LayerIdText(layer);
                    meant = In(layer);
                }
            }
            result = parts.With(lead, trail);
            problem = ReadBack(before, result, ownLayer, type, meant);
            return problem == null;
        }

        /// <summary>
        /// The name without its tag of <paramref name="type"/>, taken out where it is (spec 5.2): the other tag stays where it
        /// is. False with the reason when it has none, or the new name would not read back as meant (then
        /// <paramref name="result"/> is the name refused).
        /// </summary>
        public static bool TryRemoveTag(string name, PlannerTaskType type, PlannerLayerIdKey ownLayer, out string result, out string problem)
        {
            var text = name ?? string.Empty;
            result = text;
            problem = null;
            if (PlannerNaming.IsOldKindName(text))
            {
                problem = OldTagProblem;
                return false;
            }
            var parts = Split(text);
            if (parts.LeadType == type && parts.TrailType == type)
            {
                problem = BothEnds(type, parts);
                return false;
            }
            var before = PlannerNaming.ParseObjectNameV2(text, ownLayer);
            var lead = parts.Lead;
            var trail = parts.Trail;
            if (parts.LeadType == type)
            {
                lead = string.Empty;
            }
            else if (parts.TrailType == type)
            {
                trail = string.Empty;
            }
            else
            {
                problem = "it has no " + Word(type) + " tag";
                return false;
            }
            result = parts.With(lead, trail);
            problem = ReadBack(before, result, ownLayer, type, null);
            return problem == null;
        }

        // A split piece the renames would part from its object.
        private const string PieceLost = "would no longer be read as part of it";

        // ---------------------------------------------------------------------------------------------------------
        // Planning and checking

        /// <summary>
        /// Plans the edits on the scene and checks each by reading the scene again with the new names, as the new names'
        /// preview reads it. Requests for one object are applied in turn.
        /// </summary>
        public static PlannerTagEditPlan Plan(PlannerSceneSnapshot snapshot, PlannerScheduleOptions options, IList<PlannerTagEditRequest> requests)
        {
            var plan = new PlannerTagEditPlan();
            var before = PlannerSchedule.BuildV2(snapshot, options);
            var scene = before.Model.Scene;
            var nodes = snapshot.Nodes.Where(n => n != null).GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First());
            var problems = new Dictionary<long, string>();
            var proposals = new Dictionary<long, string>();
            // The name an edit refused on reading it back would have given, for the review.
            var refusedAs = new Dictionary<long, string>();
            var asked = new Dictionary<long, List<PlannerTagEditRequest>>();
            var valid = (requests ?? new List<PlannerTagEditRequest>()).Where(r => r != null && r.Target != null && r.Target.Id != null && nodes.ContainsKey(r.Key)).ToList();

            // Helpers carry their layers' names, and splines the export leaves out are never seen: never objects.
            var unitHelpers = new Dictionary<long, PlannerSceneUnit>();
            foreach (var unit in scene.Units.Where(u => u.Helper != null))
            {
                unitHelpers[unit.Helper.Key] = unit;
            }
            Func<long, string> notAnObject = key =>
            {
                PlannerSceneUnit unit;
                if (unitHelpers.TryGetValue(key, out unit) || (scene.RootNode != null && scene.RootNode.Key == key))
                {
                    return "it is the helper of " + (unit != null ? "the layer " + unit.Name : "the phasing root") + ": its name is the layer's, not an object's";
                }
                PlannerSceneNode sceneNode;
                if (scene.NodeByKey.TryGetValue(key, out sceneNode) && scene.LeftOut(sceneNode))
                {
                    return "it is a spline with nothing exported under it, which the export leaves out, so the Planner never sees it";
                }
                return null;
            };

            // What an Add takes as the object (see the class): a node under another node being added is part of it.
            var adding = new HashSet<long>(valid.Where(r => r.Kind == PlannerTagEditKind.Add && notAnObject(r.Key) == null).Select(r => r.Key));
            var leftOut = new HashSet<long>();
            var partsOf = new Dictionary<long, List<string>>();
            var notOwn = new Dictionary<long, string>();
            foreach (var key in adding)
            {
                var node = nodes[key];
                var above = AddedAbove(scene, key, adding);
                // A split piece by name and ancestor, as the Planner folds it, whether or not its object is tagged.
                var piece = before.MergedOwnerOf(key);
                bool tagged = PlannerNaming.ParseObjectNameV2(before.NameOf(key) ?? node.Name ?? string.Empty).Kind.HasValue;
                if (above != null && (piece != null || !tagged))
                {
                    leftOut.Add(key);
                    if (piece != null && adding.Contains(piece.Key))
                    {
                        // Renamed with its object, below.
                        continue;
                    }
                    List<string> names;
                    if (!partsOf.TryGetValue(above.Key, out names))
                    {
                        names = new List<string>();
                        partsOf[above.Key] = names;
                    }
                    names.Add(node.Name ?? string.Empty);
                }
                else if (above != null && node.IsGroupMember)
                {
                    var head = GroupHeadOf(scene, key);
                    notOwn[key] = "it is inside the closed group " + (head != null ? head.Name : above.Name) + ", selected with it, and has a tag of its own: to change it, "
                        + "open the group (Group > Open) and select it alone; to change the group, select its head alone";
                }
            }

            foreach (var request in valid)
            {
                if (request.Kind == PlannerTagEditKind.Add && leftOut.Contains(request.Key))
                {
                    continue;
                }
                var node = nodes[request.Key];
                List<PlannerTagEditRequest> list;
                if (!asked.TryGetValue(request.Key, out list))
                {
                    list = new List<PlannerTagEditRequest>();
                    asked[request.Key] = list;
                }
                list.Add(request);
                if (problems.ContainsKey(request.Key))
                {
                    continue;
                }
                var why = notAnObject(request.Key);
                if (why == null && request.Kind == PlannerTagEditKind.Add)
                {
                    notOwn.TryGetValue(request.Key, out why);
                }
                if (why == null)
                {
                    why = ObjectProblem(before, request.Key, request.Kind);
                }
                var places = before.PlacesOfNode(request.Key);
                if (why == null && request.Kind == PlannerTagEditKind.Remove && !places.Contains(Column(request.Type) + " " + request.Target.Label))
                {
                    why = "it is not in the " + Column(request.Type) + " of " + request.Target.LayerName + ": the Planner reads it as " + Places(places);
                }
                if (why != null)
                {
                    problems[request.Key] = why;
                    continue;
                }
                string current;
                if (!proposals.TryGetValue(request.Key, out current))
                {
                    current = node.Name ?? string.Empty;
                }
                var own = before.OwnLayerOfNode(request.Key);
                string next, problem;
                bool ok = request.Kind == PlannerTagEditKind.Add
                    ? TrySetTag(current, request.Type, own, request.Target.Id, out next, out problem)
                    : TryRemoveTag(current, request.Type, own, out next, out problem);
                if (!ok)
                {
                    problems[request.Key] = problem;
                    if (next != current)
                    {
                        refusedAs[request.Key] = next;
                    }
                    continue;
                }
                proposals[request.Key] = next;
            }

            // An object's split pieces (Name_2 under Name) are renamed with it (NewName_2), so the Planner still reads them
            // as part of it. The Planner folds a piece into its object by name and ancestor alone, so an untagged object's
            // pieces (tagged now) and the pieces of an object losing its only tag go with it too.
            var pieceOwner = new Dictionary<long, long>();
            var piecesOf = new Dictionary<long, List<PlannerSceneNode>>();
            if (proposals.Count > 0)
            {
                foreach (var node in nodes.Values)
                {
                    var owner = before.MergedOwnerOf(node.Key);
                    if (owner == null || asked.ContainsKey(node.Key))
                    {
                        continue;
                    }
                    List<PlannerSceneNode> pieces;
                    if (!piecesOf.TryGetValue(owner.Key, out pieces))
                    {
                        pieces = new List<PlannerSceneNode>();
                        piecesOf[owner.Key] = pieces;
                    }
                    pieces.Add(node);
                }
            }
            foreach (var pair in proposals.ToList())
            {
                List<PlannerSceneNode> pieces;
                var oldName = nodes[pair.Key].Name ?? string.Empty;
                // The pieces follow the object's own name (one the export renames is read by another).
                if (pair.Value == oldName || before.NameOf(pair.Key) != oldName || !piecesOf.TryGetValue(pair.Key, out pieces))
                {
                    continue;
                }
                foreach (var piece in pieces)
                {
                    var pieceName = piece.Name ?? string.Empty;
                    if (before.NameOf(piece.Key) != pieceName || !pieceName.StartsWith(oldName, StringComparison.Ordinal) || pieceName.Length == oldName.Length)
                    {
                        continue;
                    }
                    proposals[piece.Key] = pair.Value + pieceName.Substring(oldName.Length);
                    pieceOwner[piece.Key] = pair.Key;
                }
            }
            Action<long, string> fail = (key, why) =>
            {
                long owner;
                if (pieceOwner.TryGetValue(key, out owner))
                {
                    problems[owner] = "its split piece " + (nodes[key].Name ?? string.Empty) + " " + why + ", so it is not renamed either"
                        + (why.Contains(PieceLost) ? string.Empty : " (the piece would no longer be read as part of it)");
                }
                else
                {
                    problems[key] = "it " + why;
                }
            };

            // Read the scene again with the renames; an edit that does not give what it asks for keeps its name, and the
            // rest are read again without it, until every one left does.
            PlannerSchedule after = before;
            var verified = new Dictionary<long, string>();
            for (int round = 0; round < 8; round++)
            {
                Func<KeyValuePair<long, string>, bool> going = p =>
                {
                    long owner;
                    return !problems.ContainsKey(p.Key) && p.Value != (nodes[p.Key].Name ?? string.Empty)
                        && (!pieceOwner.TryGetValue(p.Key, out owner) || !problems.ContainsKey(owner));
                };
                var renamed = proposals.Where(going).ToDictionary(p => p.Key, p => p.Value);
                // A new name another node keeps, or that two edits give: the GLB carries one node of a name, and the export
                // calls the other "<name> (1)", which the Planner reads as an object of its own.
                var taken = new HashSet<string>(snapshot.Nodes.Where(n => n.Name != null && !renamed.ContainsKey(n.Key)).Select(n => n.Name), StringComparer.Ordinal);
                foreach (var group in renamed.GroupBy(p => p.Value, StringComparer.Ordinal))
                {
                    if (taken.Contains(group.Key) || group.Count() > 1)
                    {
                        foreach (var p in group)
                        {
                            fail(p.Key, "would be called " + group.Key + ", which " + (taken.Contains(group.Key) ? "another node is called already" : "another object of this edit would be called too")
                                + " (the export would then call one of the two " + group.Key + " (1), which the Planner reads as an object of its own)");
                        }
                    }
                }
                renamed = renamed.Where(going).ToDictionary(p => p.Key, p => p.Value);
                after = renamed.Count > 0 ? PlannerSchedule.BuildV2(Renamed(snapshot, renamed), options) : before;
                bool changed = false;
                foreach (var pair in asked)
                {
                    if (problems.ContainsKey(pair.Key))
                    {
                        continue;
                    }
                    var wanted = Wanted(before.PlacesOfNode(pair.Key), pair.Value);
                    var got = after.PlacesOfNode(pair.Key);
                    if (!wanted.SequenceEqual(got))
                    {
                        problems[pair.Key] = Explain(after, pair.Key, pair.Value.Last(), got);
                        changed = true;
                    }
                }
                foreach (var pair in renamed.Where(p => pieceOwner.ContainsKey(p.Key)))
                {
                    var owner = pieceOwner[pair.Key];
                    var now = after.MergedOwnerOf(pair.Key);
                    if (!problems.ContainsKey(owner) && (now == null || now.Key != owner))
                    {
                        fail(pair.Key, PieceLost + " with the new names");
                        changed = true;
                    }
                }
                if (!changed)
                {
                    verified = renamed;
                    break;
                }
            }
            plan.After = after;

            // The review: renames (each object's split pieces after it), then problems, then other objects read anew.
            var noTagLeft = new List<string>();
            foreach (var pair in asked)
            {
                var oldName = nodes[pair.Key].Name ?? string.Empty;
                var last = pair.Value.Last();
                string reason;
                if (problems.TryGetValue(pair.Key, out reason))
                {
                    string would;
                    if (!proposals.TryGetValue(pair.Key, out would))
                    {
                        refusedAs.TryGetValue(pair.Key, out would);
                    }
                    plan.ProblemCount++;
                    plan.Review.Add(new PlannerReviewRow
                    {
                        Status = "Problem",
                        Item = "Object",
                        Name = oldName,
                        Change = "Skipped",
                        From = oldName,
                        To = would != null && would != oldName ? would : string.Empty,
                        Why = reason
                    });
                    continue;
                }
                string newName;
                if (!verified.TryGetValue(pair.Key, out newName))
                {
                    plan.Unchanged++;
                    continue;
                }
                var own = before.OwnLayerOfNode(pair.Key);
                var read = PlannerNaming.ParseObjectNameV2(newName, own);
                var why = last.Kind == PlannerTagEditKind.Add ? AddedWhy(last, read) : "out of the " + Column(last.Type) + " of " + last.Target.LayerName + ": its " + Word(last.Type) + " tag taken out";
                if (last.Kind == PlannerTagEditKind.Remove && read.Kind != PlannerNamingKind.New)
                {
                    why += "; no tag is left, so the Planner no longer schedules it (it shows for the whole programme)";
                    noTagLeft.Add(newName);
                }
                plan.Renames.Add(new PlannerTagRename { Key = pair.Key, OldName = oldName, NewName = newName, Why = why });
                plan.Review.Add(new PlannerReviewRow { Status = "Change", Item = "Object", Name = oldName, Change = "Rename", From = oldName, To = newName, Why = why });
                foreach (var piece in verified.Where(p => pieceOwner.ContainsKey(p.Key) && pieceOwner[p.Key] == pair.Key))
                {
                    var pieceName = nodes[piece.Key].Name ?? string.Empty;
                    var pieceWhy = "a split piece of " + oldName + ": renamed with it, so the Planner still reads it as part of it";
                    plan.Renames.Add(new PlannerTagRename { Key = piece.Key, OldName = pieceName, NewName = piece.Value, Why = pieceWhy });
                    plan.Review.Add(new PlannerReviewRow { Status = "Change", Item = "Object", Name = pieceName, Change = "Rename", From = pieceName, To = piece.Value, Why = pieceWhy });
                }
            }

            var editedKeys = new HashSet<long>(plan.Renames.Select(r => r.Key));
            foreach (var node in snapshot.Nodes.Where(n => n.Name != null && !editedKeys.Contains(n.Key)))
            {
                var was = StateOf(before, node.Key);
                var now = StateOf(after, node.Key);
                if (was.SequenceEqual(now) || (was.Count == 0 && now.Count == 0))
                {
                    continue;
                }
                plan.Review.Add(new PlannerReviewRow
                {
                    Status = "Also",
                    Item = "Object",
                    Name = node.Name,
                    Change = "Read anew",
                    From = Places(was),
                    To = Places(now),
                    Why = WhyAnew(before, after, node.Key)
                });
            }
            if (plan.Review.Any(r => r.Status == "Also"))
            {
                plan.Notes.Add("Objects marked Also keep their names, but the Planner reads them differently once the others are renamed; each row says why.");
            }
            if (partsOf.Count > 0 && partsOf.Count <= 3)
            {
                foreach (var pair in partsOf)
                {
                    plan.Notes.Add(PhasingFixScript.Count(pair.Value.Count, "object", "objects") + " under " + (nodes[pair.Key].Name ?? string.Empty)
                        + " (" + Shortlist(pair.Value) + ") left out: selected with it, they are part of it, and the Planner shows and hides them with it.");
                }
            }
            else if (partsOf.Count > 3)
            {
                plan.Notes.Add(PhasingFixScript.Count(partsOf.Values.Sum(v => v.Count), "object", "objects") + " under " + partsOf.Count.ToString(CultureInfo.InvariantCulture)
                    + " of the selected objects (a group's members, a hierarchy, split pieces) left out: selected with them, they are part of them, and the Planner shows and hides them with them.");
            }
            if (noTagLeft.Count > 0)
            {
                plan.Notes.Add(PhasingFixScript.Count(noTagLeft.Count, "object is", "objects are") + " left with no IN or RM tag, so the Planner no longer schedules "
                    + (noTagLeft.Count == 1 ? "it" : "them") + " and " + (noTagLeft.Count == 1 ? "it shows" : "they show") + " for the whole programme: " + Shortlist(noTagLeft) + ".");
            }
            AddNamingNotes(plan, before, after, verified.Where(p => !pieceOwner.ContainsKey(p.Key)).Select(p => p.Key).ToList(), asked, options != null && options.Folders != null && options.Folders.Count > 0);
            return plan;
        }

        // Why an object chosen is not one to edit, read the new way: the export leaves it out; a split piece chosen alone; a
        // node the Planner reads as no object (a layer node, a phase group, a node led by a number that is no layer ID); an
        // object to tag under no layer's helper. Null when it is one to edit (or the check after the renames says why not).
        private static string ObjectProblem(PlannerSchedule before, long key, PlannerTagEditKind kind)
        {
            var name = before.NameOf(key);
            if (name == null)
            {
                string why;
                return "the export leaves it out (" + (before.Model.LeftOut.TryGetValue(key, out why) ? why : "a node above it is left out") + "), so the Planner never sees it";
            }
            PlannerModelNode indexed;
            if (!before.Index.TryGetValue(name, out indexed) || indexed.Key != key)
            {
                return null;
            }
            var piece = before.MergedOwnerOf(key);
            if (piece != null)
            {
                return "it is a split piece of " + piece.MaxName + ", which the Planner reads as one object with it: " + (kind == PlannerTagEditKind.Add ? "add " : "choose ")
                    + piece.MaxName + " instead (its pieces are renamed with it)";
            }
            PlannerLayerIdKey own;
            if (!before.OwnLayers.TryGetValue(name, out own))
            {
                if (PlannerSchedule.ParsePhaseGroup(name) != null)
                {
                    return "it is named as a phase group (_<n>_<Name>), which the Planner reads as a folder, not an object";
                }
                if (PlannerNaming.ParseLayerNameV2(name) != null)
                {
                    return "it is a layer node (named <ID>_<Name>, as a layer's helper is), which the Planner reads as a layer, not an object";
                }
                return "its name starts with a number that is no layer ID, so the Planner passes over it as a layer node, not an object: start its name with a letter first";
            }
            if (own == null && kind == PlannerTagEditKind.Add)
            {
                var listed = before.NotScheduled.FirstOrDefault(n => n.Key == key && !n.InPart);
                return listed != null && listed.Reason.StartsWith("it sits under ", StringComparison.Ordinal) ? listed.Reason + ": rename that node, or link the object under its layer's helper" : NoLayerProblem;
            }
            return null;
        }

        // Where an Add put the tag, said for the review: "RM of L: _RM_06_05_2 at the end (removed in L)".
        private static string AddedWhy(PlannerTagEditRequest request, PlannerObjectNameV2 read)
        {
            var target = request.Target;
            var word = Word(request.Type);
            var verb = request.Type == PlannerTaskType.Install ? "installed" : "removed";
            var at = EventOf(read, request.Type);
            string where;
            if (at == During)
            {
                where = word + "_ at the start (" + verb + " while " + target.LayerName + " runs)";
            }
            else if (at == End)
            {
                where = "_" + word + " at the end (" + verb + " at the end of " + target.LayerName + ")";
            }
            else
            {
                where = "_" + word + "_" + target.IdText + " at the end (" + verb + " in " + target.LayerName
                    + (target.InScene ? string.Empty : ", a layer only the Planner's list has so far") + ")";
            }
            return Column(request.Type) + " of " + target.LayerName + ": " + where;
        }

        // The notes on what the new names bring (spec 5.2, 3.7): an own name another object shares (the Planner cannot follow
        // a tag change on it), other naming issues the renamed objects have now and did not before, two events the preview
        // runs the other way round from the name, and a layer given work that the preview parks.
        private static void AddNamingNotes(PlannerTagEditPlan plan, PlannerSchedule before, PlannerSchedule after, List<long> renamed, Dictionary<long, List<PlannerTagEditRequest>> asked, bool listImported)
        {
            if (renamed.Count == 0 || after.V2 == null || before.V2 == null)
            {
                return;
            }
            var newOf = renamed.Where(k => after.NameOf(k) != null).ToDictionary(k => k, k => after.NameOf(k));
            var renamedNames = new HashSet<string>(newOf.Values, StringComparer.Ordinal);
            foreach (var issue in after.V2.NamingIssues.Where(i => i.Code == PlannerNamingCode.SharedOwnName))
            {
                foreach (var name in issue.Names.Where(n => renamedNames.Contains(n)))
                {
                    var p = PlannerNaming.ParseObjectNameV2(name);
                    plan.Notes.Add(name + " shares its own name " + p.Own + " with " + string.Join(", ", issue.Names.Where(n => n != name).ToArray())
                        + ": the Planner cannot follow a tag change on objects sharing an own name, so it reads this one's earlier activities as gone and its work as new.");
                }
            }
            var codesBefore = new Dictionary<string, HashSet<PlannerNamingCode>>(StringComparer.Ordinal);
            foreach (var issue in before.V2.NamingIssues)
            {
                foreach (var name in issue.Names)
                {
                    HashSet<PlannerNamingCode> codes;
                    if (!codesBefore.TryGetValue(name, out codes))
                    {
                        codes = new HashSet<PlannerNamingCode>();
                        codesBefore[name] = codes;
                    }
                    codes.Add(issue.Code);
                }
            }
            foreach (var pair in newOf)
            {
                var was = before.NameOf(pair.Key);
                HashSet<PlannerNamingCode> had;
                if (was == null || !codesBefore.TryGetValue(was, out had))
                {
                    had = new HashSet<PlannerNamingCode>();
                }
                var brought = after.V2.NamingIssues.Where(i => i.Names.Contains(pair.Value) && i.Code != PlannerNamingCode.SharedOwnName && i.Code != PlannerNamingCode.DuplicateExport
                    && !had.Contains(i.Code)).Select(i => i.Code).Distinct().ToList();
                foreach (var code in brought)
                {
                    plan.Notes.Add("After the renames, " + pair.Value + ": " + PlannerNaming.NamingIssueV2[code].Text);
                }
            }
            var wrongBefore = new HashSet<string>(before.V2.WrongOrder, StringComparer.Ordinal);
            foreach (var pair in newOf.Where(p => after.V2.WrongOrder.Contains(p.Value) && !wrongBefore.Contains(before.NameOf(p.Key) ?? string.Empty)))
            {
                plan.Notes.Add("After the renames, the preview runs the two events of " + pair.Value + " the other way round from its name (the layers' dates put them in the other order): "
                    + "check the layer it names.");
            }
            var parked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in asked.Where(p => renamed.Contains(p.Key)).SelectMany(p => p.Value).Where(r => r.Kind == PlannerTagEditKind.Add))
            {
                var layer = after.Layers.FirstOrDefault(l => l.Label == request.Target.Label);
                if (layer != null && layer.ParkedOn.HasValue && parked.Add(layer.Label))
                {
                    plan.Notes.Add(layer.Label + (listImported ? " has no dates in the Planner's list" : " has no dates here, as no layer list is imported") + ", so the preview parks its work on "
                        + layer.ParkedOn.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ", TBC, until the Planner dates it.");
                }
            }
        }

        // The nearest node above this one that is being added too (an object, not a helper), or null.
        private static PlannerSceneNode AddedAbove(PlannerSceneModel scene, long key, HashSet<long> adding)
        {
            PlannerSceneNode node;
            if (!scene.NodeByKey.TryGetValue(key, out node))
            {
                return null;
            }
            int hops = 0;
            for (var parent = scene.ParentOf(node); parent != null && hops < 64; parent = scene.ParentOf(parent), hops++)
            {
                if (adding.Contains(parent.Key))
                {
                    return parent;
                }
            }
            return null;
        }

        // The head of the closed group a member is in: the nearest group head above it.
        private static PlannerSceneNode GroupHeadOf(PlannerSceneModel scene, long key)
        {
            PlannerSceneNode node;
            if (!scene.NodeByKey.TryGetValue(key, out node))
            {
                return null;
            }
            int hops = 0;
            for (var parent = scene.ParentOf(node); parent != null && hops < 64; parent = scene.ParentOf(parent), hops++)
            {
                if (parent.IsGroupHead)
                {
                    return parent;
                }
            }
            return null;
        }

        private static string Shortlist(List<string> names)
        {
            var shown = names.Take(3).ToList();
            return string.Join(", ", shown.ToArray()) + (names.Count > 3 ? ", \u2026" : string.Empty);
        }

        // Where a node's work is: its places, or "part of X" for a split piece of X.
        private static List<string> StateOf(PlannerSchedule schedule, long key)
        {
            var piece = schedule.MergedOwnerOf(key);
            return piece != null ? new List<string> { "part of " + piece.Name } : schedule.PlacesOfNode(key);
        }

        // Why a node not renamed is read differently once the others are.
        private static string WhyAnew(PlannerSchedule before, PlannerSchedule after, long key)
        {
            var wasPiece = before.MergedOwnerOf(key);
            var nowPiece = after.MergedOwnerOf(key);
            if (wasPiece != null && nowPiece == null)
            {
                return "not renamed, but no longer read as a split piece of " + wasPiece.Name + " once that is renamed, so the Planner reads it as an object of its own";
            }
            if (wasPiece == null && nowPiece != null)
            {
                return "not renamed, but the Planner reads it as a split piece of " + nowPiece.Name + " once that is renamed";
            }
            return "not renamed, but the Planner reads it differently once the others are renamed";
        }

        // What an object's places should be once the requests are done: an Add replaces the places of its type with the
        // target layer's folder, a Remove takes the target layer's place of its type out.
        private static List<string> Wanted(List<string> places, List<PlannerTagEditRequest> requests)
        {
            var result = new List<string>(places);
            foreach (var request in requests)
            {
                var prefix = Column(request.Type) + " ";
                if (request.Kind == PlannerTagEditKind.Add)
                {
                    result.RemoveAll(p => p.StartsWith(prefix, StringComparison.Ordinal));
                    result.Add(prefix + request.Target.Label);
                }
                else
                {
                    result.Remove(prefix + request.Target.Label);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static string Places(List<string> places)
        {
            return places.Count == 0 ? "not scheduled" : string.Join("; ", places.ToArray());
        }

        // Why an object would not be where the edit asks, read off the preview with the new names.
        private static string Explain(PlannerSchedule after, long key, PlannerTagEditRequest request, List<string> got)
        {
            var target = request.Target;
            var obj = after.ObjectOf(key);
            if (obj == null)
            {
                var listed = after.NotScheduled.FirstOrDefault(n => n.Key == key);
                return (request.Kind == PlannerTagEditKind.Add ? "the Planner would not schedule it" : "the Planner would no longer schedule it")
                    + (listed != null ? ": " + listed.Reason : string.Empty);
            }
            if (request.Kind == PlannerTagEditKind.Add)
            {
                var activity = obj.Activities.FirstOrDefault(a => a.Type == request.Type);
                if (activity != null && activity.Home != null && activity.Home.Label != target.Label && SameLayer(activity.Home.Id, target.Id))
                {
                    return "the Planner files its " + Kind(request.Type) + " in " + activity.Home.Label + ", the first of its folders with the layer ID " + target.IdText
                        + ", not in " + target.Label + ": give each layer its own ID in the Planner";
                }
            }
            return "the Planner would read it as " + Places(got);
        }

        // The scene with some nodes renamed.
        internal static PlannerSceneSnapshot Renamed(PlannerSceneSnapshot snapshot, Dictionary<long, string> names)
        {
            var copy = new PlannerSceneSnapshot { ExportHidden = snapshot.ExportHidden };
            copy.Layers.AddRange(snapshot.Layers);
            foreach (var node in snapshot.Nodes)
            {
                string name;
                copy.Nodes.Add(new PlannerSceneNode
                {
                    Key = node.Key,
                    Name = names.TryGetValue(node.Key, out name) ? name : node.Name,
                    ParentKey = node.ParentKey,
                    ChildIndex = node.ChildIndex,
                    HasChildren = node.HasChildren,
                    IsGroupHead = node.IsGroupHead,
                    IsGroupMember = node.IsGroupMember,
                    IsHelper = node.IsHelper,
                    IsShape = node.IsShape,
                    LayerName = node.LayerName,
                    PlannerId = node.PlannerId,
                    Hidden = node.Hidden,
                    NoExport = node.NoExport,
                    NotInThisExport = node.NotInThisExport
                });
            }
            return copy;
        }

        // ---------------------------------------------------------------------------------------------------------
        // The MAXScript batch

        /// <summary>The renames as one MAXScript batch in one undo record; nodes found by handle and checked by name first.</summary>
        public static string Script(PlannerTagEditPlan plan, out List<string> refused, out List<PlannerTagRename> sent)
        {
            refused = new List<string>();
            sent = new List<PlannerTagRename>();
            var handles = new List<string>();
            var olds = new List<string>();
            var news = new List<string>();
            foreach (var rename in plan.Renames)
            {
                var oldName = PhasingFixScript.Quote(rename.OldName);
                var newName = PhasingFixScript.Quote(rename.NewName);
                if (rename.Key <= 0 || rename.Key > int.MaxValue || oldName == null || newName == null)
                {
                    refused.Add(rename.OldName + " was not renamed: its handle or name cannot be passed to MAXScript");
                    continue;
                }
                sent.Add(rename);
                handles.Add(PlannerLayerScript.Number(rename.Key));
                olds.Add(oldName);
                news.Add(newName);
            }
            var s = new StringBuilder();
            s.Append("-- UrbanCGI exporter: ").Append(UndoLabel).Append(" (").Append(PlannerLayerScript.Number(sent.Count)).Append(" renames)").Append(Nl);
            s.Append("(").Append(Nl);
            s.Append("\tlocal bLog = stringStream \"\"").Append(Nl);
            s.Append("\tlocal bNode, bDetail").Append(Nl);
            s.Append("\tlocal bRenamed = 0").Append(Nl);
            PlannerLayerScript.WriteArray(s, "bHandles", handles);
            PlannerLayerScript.WriteArray(s, "bOld", olds);
            PlannerLayerScript.WriteArray(s, "bNew", news);
            s.Append(@"
	local bNodes = for h in bHandles collect (maxOps.getNodeByHandle h)
	try
	(
		undo ").Append(PhasingFixScript.Quote(UndoLabel)).Append(@" on
		(
			with redraw off
			(
				for i = 1 to bNodes.count do
				(
					try
					(
						bNode = bNodes[i]
						if (bNode == undefined) then
						(
							format ""FAIL\trename\t%\tgone\n"" i to:bLog
						)
						else if (bNode.name != bOld[i]) then
						(
							format ""FAIL\trename\t%\tchanged\n"" i to:bLog
						)
						else
						(
							bNode.name = bNew[i]
							if (bNode.name == bNew[i]) then (bRenamed += 1) else (format ""FAIL\trename\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("rename") + @"
					)
				)
			)
		)
	)
	catch
	(
		bDetail = (getCurrentException()) as string
		" + PlannerLayerScript.CleanDetail() + @"
		format ""ABORT\t%\n"" bDetail to:bLog
	)
	format ""DONE\t%\n"" bRenamed to:bLog
	format """ + EndMarker + @"\n"" to:bLog
	bLog as string
)
");
            return s.ToString().Replace("\r\n", "\n").Replace("\n", Nl);
        }

        /// <summary>What came back from the batch: renamed, failures as sentences, completed, aborted.</summary>
        public static void ReadResult(List<PlannerTagRename> sent, string output, out int renamed, out List<string> failures, out bool completed, out string aborted)
        {
            renamed = 0;
            failures = new List<string>();
            aborted = null;
            foreach (var parts in PlannerScriptText.Lines(output, EndMarker, out completed))
            {
                switch (parts[0])
                {
                    case "DONE":
                        if (parts.Length >= 2)
                        {
                            renamed = PlannerScriptText.ParseCount(parts[1]);
                        }
                        break;
                    case "ABORT":
                        aborted = parts.Length > 1 ? string.Join(" ", parts.Skip(1).ToArray()).Trim() : "unknown error";
                        break;
                    case "FAIL":
                        if (parts.Length >= 4)
                        {
                            int index = PlannerScriptText.ParseCount(parts[2]) - 1;
                            var rename = index >= 0 && index < sent.Count ? sent[index] : null;
                            var detail = parts.Length > 4 ? string.Join(" ", parts.Skip(4).ToArray()).Trim() : null;
                            var who = rename != null ? rename.OldName : "an object";
                            switch (parts[3])
                            {
                                case "gone":
                                    failures.Add(who + " is no longer in the scene, so it was not renamed");
                                    break;
                                case "changed":
                                    failures.Add(who + " has been renamed since the list was made, so it was not renamed");
                                    break;
                                case "refused":
                                    failures.Add(who + " could not be renamed to " + (rename != null ? rename.NewName : "its new name"));
                                    break;
                                default:
                                    failures.Add(who + " was not renamed: 3ds Max reported an error" + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
                                    break;
                            }
                        }
                        break;
                }
            }
        }
    }
}
