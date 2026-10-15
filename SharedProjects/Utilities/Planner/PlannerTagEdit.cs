using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>Put objects into a layer's IN or RM column, or take them out of it.</summary>
    public enum PlannerTagEditKind
    {
        Add,
        Remove
    }

    /// <summary>The layer an IN / RM edit is about: its name in the scene and the phase, stage and sub-stage its work carries.</summary>
    public sealed class PlannerTagTarget
    {
        /// <summary>The layer's (and its helper's) name.</summary>
        public string LayerName;
        /// <summary>The label the Planner knows the layer by (dates and TBC left out).</summary>
        public string Label;
        public int? Phase;
        public int Stage;
        public int? SubStage;

        /// <summary>
        /// The target for a layer of the scene: its ID read from its name; the phase of a one-number ID is the one the
        /// Planner reads off its objects (null when it has none, so nothing can be tagged for it). Null when the name does
        /// not read as a layer.
        /// </summary>
        public static PlannerTagTarget Of(string layerName, PlannerSchedule schedule)
        {
            var parsed = PlannerNaming.ParseLayerName(layerName);
            if (parsed == null)
            {
                return null;
            }
            var target = new PlannerTagTarget { LayerName = layerName, Label = parsed.Label, Phase = parsed.Phase, Stage = parsed.Stage, SubStage = parsed.SubStage };
            if (!target.Phase.HasValue && schedule != null)
            {
                var layer = schedule.Layers.FirstOrDefault(l => l.Label == parsed.Label);
                target.Phase = layer != null ? layer.Phase : null;
            }
            return target;
        }

        /// <summary>
        /// The target for a layer as the Planner reads it: its helper's name (the name the GLB carries), its label, and the
        /// phase, stage and sub-stage the Planner gives it. Null for null.
        /// </summary>
        public static PlannerTagTarget Of(PlannerScheduleLayer layer)
        {
            if (layer == null)
            {
                return null;
            }
            return new PlannerTagTarget { LayerName = layer.Raw, Label = layer.Label, Phase = layer.Phase, Stage = layer.Parsed.Stage, SubStage = layer.Parsed.SubStage };
        }

        /// <summary>
        /// The target for a row of the Schedule tab: the layer the Planner reads there, found by the row's helper (whose name
        /// the Planner reads, even when the Max layer's name has drifted from it), else the row's Max layer. Null when
        /// neither reads as a layer.
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
            return row.LayerName != null ? Of(row.LayerName, schedule) : null;
        }

        /// <summary>"Ph2_St01_2": the tag this layer's work carries (Ph?_ when the phase is not known).</summary>
        public string TagText
        {
            get
            {
                return "Ph" + (Phase.HasValue ? Phase.Value.ToString(CultureInfo.InvariantCulture) : "?") + "_" + PlannerNaming.StageText(Stage, SubStage);
            }
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
        /// <summary>The schedule with the renames made, as the Planner would read it.</summary>
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
    /// The IN and RM columns edited by renaming objects: an object's install or removal tag is what the Planner's Generate
    /// from model reads, so the lists and the names always agree. "Add to IN" sets the object's install tag to the layer's
    /// phase and stage, "Add to RM" its removal tag; "Remove" takes the tag out. The tag of the same type is replaced where
    /// it sits (leading or trailing); a missing one is added as the trailing tag (as the leading one when the name has no
    /// tag at all, as a name only reads with one); every other part of the name, the other tag included, stays exactly as
    /// written; a tag the tool writes takes the convention's form, Ph&lt;p&gt;_St&lt;ss&gt;[_&lt;m&gt;]_IN|RM. When the
    /// leading tag is removed and a trailing one stays, the trailing one, as written, moves to the front. Nothing is moved
    /// or linked: an object sitting in another layer than the one installing it stays there.
    ///
    /// Each edit is checked by reading the scene again with the new names, exactly as the Planner would
    /// (<see cref="PlannerSchedule"/>): an object that would not then be in the column asked for (an undated target layer,
    /// one that holds no tagged object yet, another layer the Planner dates that stage from, an object not under a dated
    /// layer's helper, a name another node has) is a Problem and keeps its name; other objects the renames make the
    /// Planner read differently are listed, each with why. Objects are known by the names the GLB gives them (a second
    /// node of a name is "X (1)", an object of its own).
    ///
    /// What an Add takes as the object: selecting a closed group in 3ds Max selects its head and every member, and a
    /// hierarchy may be selected whole, so a node under another node being added is part of it (the Planner shows and
    /// hides it with it) and is left out of the edit, unless it has a tag of its own: then it is its own object, kept
    /// when it is not in a group, and a Problem inside a closed group (open the group to choose). A split piece
    /// (Name_2 under Name, read as part of Name) is renamed with its object, so it stays part of it; chosen alone, it is a
    /// Problem. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerTagEdit
    {
        public const string UndoLabel = "Planner IN / RM";
        public const string EndMarker = "PLANNERTAGS-END";
        private const string Nl = "\r\n";

        // The parser's leading and trailing tags (PlannerNaming), with their places in the name.
        private static readonly Regex Lead = new Regex(@"^Ph([0-9]+)_St([0-9]+(?:[_.-][0-9]+)?)_(IN|RM)(?:_|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Trail = new Regex(@"_Ph([0-9]+)_St([0-9]+(?:[_.-][0-9]+)?)_(IN|RM)(?=\z|" + PlannerNaming.JavaScriptSpace + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>"Ph2_St01_2_IN": a tag in the convention's form.</summary>
        public static string Canonical(PlannerTaskType type, int phase, int stage, int? subStage)
        {
            return "Ph" + phase.ToString(CultureInfo.InvariantCulture) + "_" + PlannerNaming.StageText(stage, subStage) + "_" + (type == PlannerTaskType.Install ? "IN" : "RM");
        }

        private sealed class Tag
        {
            public int Start;
            public int End;
            public PlannerMeshSegment Segment;
        }

        // The leading and trailing tags of a name, with where each is written (the tag itself, without the underscores
        // around it); null when the name has no leading tag (it does not read as a phasing object).
        private static bool Tags(string name, out Tag lead, out Tag trail)
        {
            lead = null;
            trail = null;
            var parsed = PlannerNaming.ParseMeshName(name);
            var leadMatch = Lead.Match(name);
            if (parsed == null || !leadMatch.Success)
            {
                return false;
            }
            lead = new Tag { Start = 0, End = leadMatch.Groups[3].Index + leadMatch.Groups[3].Length, Segment = parsed.Lead };
            var trailMatch = Trail.Match(name);
            if (trailMatch.Success && parsed.Trail != null)
            {
                trail = new Tag { Start = trailMatch.Index + 1, End = trailMatch.Index + trailMatch.Length, Segment = parsed.Trail };
            }
            return true;
        }

        private static bool Matches(PlannerMeshSegment segment, int phase, int stage, int? subStage)
        {
            return segment.Phase == phase && segment.Stage == stage && segment.SubStage == subStage;
        }

        /// <summary>
        /// The name with its tag of <paramref name="type"/> set to the phase, stage and sub-stage given; false with a
        /// reason when it cannot be written so.
        /// </summary>
        public static bool TrySetTag(string name, PlannerTaskType type, int phase, int stage, int? subStage, out string result, out string problem)
        {
            result = name;
            problem = null;
            var text = name ?? string.Empty;
            var tag = Canonical(type, phase, stage, subStage);
            Tag lead, trail;
            if (!Tags(text, out lead, out trail))
            {
                // No tag yet: a name reads only with a leading tag.
                result = text.Length > 0 ? tag + "_" + text : tag;
                return true;
            }
            bool leadIs = lead.Segment.Type == type;
            bool trailIs = trail != null && trail.Segment.Type == type;
            if (leadIs && trailIs)
            {
                problem = "it has two " + Kind(type) + " tags; edit its name by hand";
                return false;
            }
            var chosen = leadIs ? lead : trailIs ? trail : null;
            if (chosen != null)
            {
                if (Matches(chosen.Segment, phase, stage, subStage))
                {
                    result = text;
                    return true;
                }
                result = text.Substring(0, chosen.Start) + tag + text.Substring(chosen.End);
                return true;
            }
            // A missing type goes in as the trailing tag, at the very end: everything after the leading tag stays the
            // description, as it was.
            result = text + "_" + tag;
            return true;
        }

        /// <summary>
        /// The name without its tag of <paramref name="type"/> (the one with the phase and stage given when it has two);
        /// false with a reason when it cannot be written so.
        /// </summary>
        public static bool TryRemoveTag(string name, PlannerTaskType type, int? phase, int stage, int? subStage, out string result, out string problem)
        {
            result = name;
            problem = null;
            var text = name ?? string.Empty;
            Tag lead, trail;
            if (!Tags(text, out lead, out trail))
            {
                problem = "it has no tag";
                return false;
            }
            var candidates = new[] { lead, trail }.Where(t => t != null && t.Segment.Type == type).ToList();
            if (candidates.Count > 1)
            {
                candidates = candidates.Where(t => (!phase.HasValue || t.Segment.Phase == phase.Value)
                    && PlannerNaming.SameStage(t.Segment.Stage, t.Segment.SubStage, stage, subStage)).ToList();
            }
            if (candidates.Count != 1)
            {
                problem = candidates.Count == 0 ? "it has no " + Kind(type) + " tag" : "it has two " + Kind(type) + " tags for this layer; edit its name by hand";
                return false;
            }
            var chosen = candidates[0];
            if (chosen == trail)
            {
                result = text.Substring(0, chosen.Start - 1) + text.Substring(chosen.End);
                return true;
            }
            // The leading tag goes. The text after it (and its underscore) stays; a trailing tag, as written, then leads.
            int descriptionStart = lead.End < text.Length && text[lead.End] == '_' ? lead.End + 1 : lead.End;
            if (trail == null)
            {
                result = text.Substring(descriptionStart);
                if (result.Length == 0)
                {
                    problem = "its name is only the tag, so it would have no name left";
                    return false;
                }
                return true;
            }
            var trailText = text.Substring(trail.Start, trail.End - trail.Start);
            var description = trail.Start - 1 > descriptionStart ? text.Substring(descriptionStart, trail.Start - 1 - descriptionStart) : string.Empty;
            result = trailText + (description.Length > 0 ? "_" + description : string.Empty) + text.Substring(trail.End);
            return true;
        }

        private static string Kind(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "install" : "removal";
        }

        private static string Column(PlannerTaskType type)
        {
            return type == PlannerTaskType.Install ? "IN" : "RM";
        }

        // ---------------------------------------------------------------------------------------------------------
        // Planning and checking

        /// <summary>
        /// Plans the edits on the scene and checks each by reading the scene again with the new names, exactly as the
        /// Planner would. Requests for one object are applied in turn.
        /// </summary>
        public static PlannerTagEditPlan Plan(PlannerSceneSnapshot snapshot, PlannerScheduleOptions options, IList<PlannerTagEditRequest> requests)
        {
            var plan = new PlannerTagEditPlan();
            var before = PlannerSchedule.Build(snapshot, options);
            var scene = before.Model.Scene;
            var nodes = snapshot.Nodes.Where(n => n != null).GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First());
            var problems = new Dictionary<long, string>();
            var proposals = new Dictionary<long, string>();
            var asked = new Dictionary<long, List<PlannerTagEditRequest>>();
            var valid = (requests ?? new List<PlannerTagEditRequest>()).Where(r => r != null && r.Target != null && nodes.ContainsKey(r.Key)).ToList();

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
                var piece = before.PieceOf(key);
                bool tagged = PlannerNaming.ParseMeshName(before.NameOf(key) ?? node.Name ?? string.Empty) != null;
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
                else if (piece != null)
                {
                    notOwn[key] = "it is a split piece of " + piece.MaxName + ", which the Planner reads as one object with it: add " + piece.MaxName
                        + " instead (its pieces are renamed with it)";
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
                var target = request.Target;
                string next, problem;
                bool ok;
                if (request.Kind == PlannerTagEditKind.Add)
                {
                    if (!target.Phase.HasValue)
                    {
                        problems[request.Key] = "the layer " + target.LayerName + " has one number in its ID and none of its objects gives it a phase, so there is no phase to write; give the layer its phase (<phase>_<stage>_...)";
                        continue;
                    }
                    ok = TrySetTag(current, request.Type, target.Phase.Value, target.Stage, target.SubStage, out next, out problem);
                }
                else
                {
                    ok = TryRemoveTag(current, request.Type, target.Phase, target.Stage, target.SubStage, out next, out problem);
                }
                if (!ok)
                {
                    problems[request.Key] = problem;
                    continue;
                }
                proposals[request.Key] = next;
            }

            // An object's split pieces (Name_2 under Name) are renamed with it (NewName_2), so the Planner still reads them
            // as part of it.
            var pieceOwner = new Dictionary<long, long>();
            var piecesOf = new Dictionary<long, List<PlannerSceneNode>>();
            if (proposals.Count > 0)
            {
                foreach (var node in nodes.Values)
                {
                    var owner = before.PieceOf(node.Key);
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
                var obj = before.ObjectOf(pair.Key);
                if (pair.Value == oldName || obj == null || obj.Name != oldName || !piecesOf.TryGetValue(pair.Key, out pieces))
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
                    problems[owner] = "its split piece " + (nodes[key].Name ?? string.Empty) + " " + why + ", so it is not renamed either (the piece would no longer be read as part of it)";
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
                after = renamed.Count > 0 ? PlannerSchedule.Build(Renamed(snapshot, renamed), options) : before;
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
                        string newName;
                        if (!renamed.TryGetValue(pair.Key, out newName))
                        {
                            newName = nodes[pair.Key].Name ?? string.Empty;
                        }
                        problems[pair.Key] = Explain(after, pair.Key, newName, pair.Value.Last(), got);
                        changed = true;
                    }
                }
                foreach (var pair in renamed.Where(p => pieceOwner.ContainsKey(p.Key)))
                {
                    var owner = pieceOwner[pair.Key];
                    var now = after.PieceOf(pair.Key);
                    if (!problems.ContainsKey(owner) && (now == null || now.Key != owner))
                    {
                        fail(pair.Key, "would no longer be read as part of it with the new names");
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
            foreach (var pair in asked)
            {
                var oldName = nodes[pair.Key].Name ?? string.Empty;
                var last = pair.Value.Last();
                string reason;
                if (problems.TryGetValue(pair.Key, out reason))
                {
                    string would;
                    proposals.TryGetValue(pair.Key, out would);
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
                var why = last.Kind == PlannerTagEditKind.Add
                    ? Column(last.Type) + " of " + last.Target.LayerName + ": " + Kind(last.Type) + " tag " + last.Target.TagText
                    : "out of the " + Column(last.Type) + " of " + last.Target.LayerName + ": " + Kind(last.Type) + " tag taken out";
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
            var phaseNow = after.Layers.GroupBy(l => l.Label, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Phase, StringComparer.Ordinal);
            var phaseChanged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var layer in before.Layers)
            {
                int? now;
                if (phaseNow.TryGetValue(layer.Label, out now) && now != layer.Phase)
                {
                    phaseChanged[layer.Label] = "phase " + PhaseText(now) + " (it was phase " + PhaseText(layer.Phase) + ")";
                }
            }
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
                    Why = WhyAnew(before, after, node.Key, phaseChanged)
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
            return plan;
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
            return string.Join(", ", shown.ToArray()) + (names.Count > 3 ? ", …" : string.Empty);
        }

        private static string PhaseText(int? phase)
        {
            return phase.HasValue ? phase.Value.ToString(CultureInfo.InvariantCulture) : "none";
        }

        // Where a node's work is: its places, or "part of X" for a split piece of X.
        private static List<string> StateOf(PlannerSchedule schedule, long key)
        {
            var piece = schedule.PieceOf(key);
            return piece != null ? new List<string> { "part of " + piece.Name } : schedule.PlacesOfNode(key);
        }

        // Why a node not renamed is read differently once the others are.
        private static string WhyAnew(PlannerSchedule before, PlannerSchedule after, long key, Dictionary<string, string> phaseChanged)
        {
            var wasPiece = before.PieceOf(key);
            var nowPiece = after.PieceOf(key);
            if (wasPiece != null && nowPiece == null)
            {
                return "not renamed, but no longer read as a split piece of " + wasPiece.Name + " once that is renamed, so the Planner reads it as an object of its own";
            }
            if (wasPiece == null && nowPiece != null)
            {
                return "not renamed, but the Planner reads it as a split piece of " + nowPiece.Name + " once that is renamed";
            }
            foreach (var obj in new[] { before.ObjectOf(key), after.ObjectOf(key) })
            {
                string change;
                if (obj != null && obj.FiledIn != null && phaseChanged.TryGetValue(obj.FiledIn.Label, out change))
                {
                    return "not renamed, but its layer " + obj.FiledIn.Raw + " has one number in its ID and takes its phase from its objects' tags: with the renames it is "
                        + change + ", and the Planner dates its objects by that phase";
                }
            }
            var dated = after.ObjectOf(key) ?? before.ObjectOf(key);
            return "not renamed, but the Planner reads it differently once the others are renamed"
                + (dated != null ? " (another layer now dates its stage, or its name now reads another way)" : string.Empty);
        }

        // What an object's places should be once the requests are done: an Add replaces the places of its type with the
        // target layer, a Remove takes the target layer's place of its type out.
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

        // Why an object would not be where the edit asks, read off the schedule with the new names.
        private static string Explain(PlannerSchedule after, long key, string newName, PlannerTagEditRequest request, List<string> got)
        {
            var target = request.Target;
            var targetLayer = after.Layers.FirstOrDefault(l => l.Label == target.Label);
            var obj = after.ObjectOf(key);
            if (obj == null)
            {
                var listed = after.NotScheduled.FirstOrDefault(n => n.Key == key);
                return (request.Kind == PlannerTagEditKind.Add ? "the Planner would not schedule it" : "the Planner would no longer schedule it")
                    + (listed != null ? ": " + listed.Reason : string.Empty);
            }
            if (request.Kind == PlannerTagEditKind.Remove)
            {
                return "the Planner would read it as " + Places(got);
            }
            var activity = obj.Activities.FirstOrDefault(a => a.Type == request.Type);
            if (activity == null)
            {
                return "the Planner would read it as " + Places(got);
            }
            var stage = "Ph" + (target.Phase.HasValue ? target.Phase.Value.ToString(CultureInfo.InvariantCulture) : "?") + "_" + PlannerNaming.StageText(target.Stage, target.SubStage);
            if (activity.DatedBy == null)
            {
                string cause = targetLayer == null
                    ? target.LayerName + " holds no tagged object yet, and the Planner dates another layer's work only from a layer that holds one"
                    : !targetLayer.Dated
                        ? target.LayerName + " has no dates, and the Planner dates another layer's work only from a dated layer"
                        : "no dated layer has " + stage;
                return cause + ": it would put the " + Kind(request.Type) + " " + (request.Type == PlannerTaskType.Install ? "at the project start" : "after the end of the works")
                    + ", marked TBC (it sits in " + obj.FiledIn.Raw + "; this tool never moves objects: link it under " + target.LayerName + "'s helper to have it there)";
            }
            if (activity.IsOwn && activity.Home == obj.FiledIn && activity.DatedBy == obj.FiledIn)
            {
                return "it sits in " + obj.FiledIn.Raw + ", and the Planner gives that layer's dates to an object whose tag it does not read as another dated layer's"
                    + (targetLayer == null ? " (" + target.LayerName + " holds no tagged object yet)" : !targetLayer.Dated ? " (" + target.LayerName + " has no dates)" : string.Empty)
                    + "; this tool never moves objects";
            }
            return "the Planner dates " + stage + " work from " + activity.DatedBy.Raw + " (the layer of that very sub-stage first, then the one that starts first)";
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
                    NoExport = node.NoExport
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
