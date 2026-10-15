using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>Link one node (an object, or the top of a hierarchy of them, or a group head) under its layer's helper.</summary>
    public sealed class PlannerObjectLink
    {
        public long Key;
        public string Name;
        /// <summary>The node it is linked under today, or null at the scene root.</summary>
        public long? FromParentKey;
        public string FromParentName;
        /// <summary>The layer's helper.</summary>
        public long ToKey;
        public string ToName;
        public string LayerName;
        /// <summary>The helper is a 3ds Max group head: the node is attached to the group.</summary>
        public bool IntoGroupHead;
        public bool IsGroupMember;
        public bool IsGroupHead;
        /// <summary>The layer's objects this link carries (the node itself and those under it).</summary>
        public int Objects;
    }

    /// <summary>What Link objects to their helpers would do.</summary>
    public sealed class PlannerLinkPlan
    {
        /// <summary>Set when there is nothing to link at all (no phasing root).</summary>
        public string Message;
        public readonly List<PlannerObjectLink> Links = new List<PlannerObjectLink>();
        /// <summary>Objects on the phasing layers already under their layer's helper.</summary>
        public int AlreadyLinked;
        /// <summary>Objects on the phasing layers the links carry.</summary>
        public int ObjectsToLink;
        public int ProblemCount;
        /// <summary>The links, then the problems.</summary>
        public readonly List<PlannerReviewRow> Review = new List<PlannerReviewRow>();
        public readonly List<string> Notes = new List<string>();

        public bool HasChanges
        {
            get { return Links.Count > 0; }
        }

        /// <summary>One line, e.g. "120 objects to link (in 40 links), 300 already under their helpers, 2 problems."</summary>
        public string Summary()
        {
            if (Message != null)
            {
                return Message;
            }
            return PhasingFixScript.Count(ObjectsToLink, "object", "objects") + " to link"
                + (Links.Count != ObjectsToLink ? " (in " + PhasingFixScript.Count(Links.Count, "link", "links") + ")" : string.Empty)
                + ", " + AlreadyLinked.ToString(CultureInfo.InvariantCulture) + " already under their helpers, "
                + PhasingFixScript.Count(ProblemCount, "problem", "problems") + ".";
        }

        public string ReviewCsv()
        {
            return PlannerReview.ToCsv(Review);
        }

        public string Fingerprint()
        {
            var text = new StringBuilder();
            foreach (var link in Links)
            {
                text.Append(link.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(link.Name).Append('\t')
                    .Append(link.ToKey.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            foreach (var row in Review)
            {
                text.Append(row.Status).Append('\t').Append(row.Name).Append('\t').Append(row.Why).Append('\n');
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// Plans Link objects to their helpers: every object on a phasing layer (a layer inside the root layer's tree with its
    /// helper of the same name) goes under that helper. What is linked is the object's top-most ancestor that is not a
    /// phasing helper (the object itself when it has none), so a hierarchy moves whole and a 3ds Max group by its group
    /// head; world positions are kept. Helpers and shapes the export leaves out (splines such as RailClone paths) are not
    /// objects. An object whose top ancestor sits on another layer is listed as a Problem and not moved, and so are the
    /// objects of a phasing layer that has no helper yet. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerLinkPlanner
    {
        public const string NoRoot = "The scene has no phasing root (a top-level node or layer with \"phasing\" in its name): build the layers and helpers first.";

        public static PlannerLinkPlan Plan(PlannerSceneSnapshot snapshot)
        {
            return Plan(new PlannerSceneModel(snapshot));
        }

        public static PlannerLinkPlan Plan(PlannerSceneModel scene)
        {
            var plan = new PlannerLinkPlan();
            if (scene.RootLayer == null)
            {
                plan.Message = scene.RootNode == null ? NoRoot : "The phasing root " + scene.RootNode.Name + " has no layer of its name, so the scene has no phasing layers yet: build the layers and helpers first.";
                return plan;
            }
            var byLayer = scene.Nodes.Where(n => n.LayerName != null).ToLookup(n => n.LayerName, StringComparer.OrdinalIgnoreCase);
            var links = new Dictionary<long, PlannerObjectLink>();
            var problems = new List<PlannerReviewRow>();
            foreach (var unit in scene.Units.Where(u => u.InPhasingTree && u.Layer != null && scene.TreeLayers.Contains(u.Layer.Name)))
            {
                var layerName = unit.Layer.Name;
                var objects = byLayer[layerName].Where(n => IsObject(n, scene)).ToList();
                if (objects.Count == 0)
                {
                    continue;
                }
                var helper = unit.Helper;
                if (helper == null)
                {
                    problems.Add(Problem(PhasingFixScript.Count(objects.Count, "object", "objects"), layerName,
                        "the layer " + layerName + " has no helper of its name yet: Build layers and helpers adds one when the Planner's list has this layer"
                        + " (otherwise, make a Dummy of the layer's name on it, linked under its parent layer's helper)"));
                    continue;
                }
                foreach (var node in objects)
                {
                    var top = TopOf(node, scene);
                    var above = scene.ParentOf(top);
                    if (above != null && above.Key == helper.Key)
                    {
                        plan.AlreadyLinked++;
                        continue;
                    }
                    if (!string.Equals(top.LayerName, layerName, StringComparison.OrdinalIgnoreCase))
                    {
                        problems.Add(Problem(node.Name, layerName, "it is linked under " + top.Name + ", which sits on "
                            + (top.LayerName != null ? "the layer " + top.LayerName : "another layer") + "; left as it is"));
                        continue;
                    }
                    if (scene.IsUnder(helper, top))
                    {
                        problems.Add(Problem(node.Name, layerName, "the helper " + helper.Name + " is linked under " + top.Name + ", so it cannot go under the helper; left as it is"));
                        continue;
                    }
                    PlannerObjectLink link;
                    if (!links.TryGetValue(top.Key, out link))
                    {
                        link = new PlannerObjectLink
                        {
                            Key = top.Key,
                            Name = top.Name,
                            FromParentKey = above != null ? above.Key : (long?)null,
                            FromParentName = above != null ? above.Name : null,
                            ToKey = helper.Key,
                            ToName = helper.Name,
                            LayerName = layerName,
                            IntoGroupHead = helper.IsGroupHead,
                            IsGroupMember = top.IsGroupMember,
                            IsGroupHead = top.IsGroupHead
                        };
                        links[top.Key] = link;
                        plan.Links.Add(link);
                    }
                    link.Objects++;
                    plan.ObjectsToLink++;
                }
            }

            foreach (var link in plan.Links)
            {
                plan.Review.Add(new PlannerReviewRow
                {
                    Status = "Change",
                    Item = link.IsGroupHead ? "Group" : "Object",
                    Name = link.Name,
                    Change = "Link",
                    From = link.FromParentName != null ? "under " + link.FromParentName : "at the top",
                    To = "under " + link.ToName,
                    Why = "on the layer " + link.LayerName + (link.Objects > 1 ? " (with " + PhasingFixScript.Count(link.Objects - 1, "more object", "more objects") + " of the layer under it)" : string.Empty)
                });
            }
            plan.Review.AddRange(problems);
            plan.ProblemCount = problems.Count;

            // A Max group, a Point or another helper, or a spline with objects under it, linked in under the phasing root:
            // the export carries it as an empty node, which the export log counts as a helper node without a layer of
            // its name. Expected, and said so here, as the export log's counts are left as they were.
            var counted = plan.Links
                .Where(l =>
                {
                    PlannerSceneNode node;
                    return scene.NodeByKey.TryGetValue(l.Key, out node) && (node.IsHelper || node.IsShape)
                        && !scene.TreeNodes.Contains(node.Key) && scene.TreeNodes.Contains(l.ToKey)
                        && !(node.Name != null && scene.TreeLayers.Contains(node.Name));
                })
                .Select(l => l.Name)
                .ToList();
            if (counted.Count > 0)
            {
                plan.Notes.Add(PhasingFixScript.Count(counted.Count, "node linked is a Max group, a Point or another helper, or a spline with objects under it",
                    "nodes linked are Max groups, Points or other helpers, or splines with objects under them")
                    + " (" + string.Join(", ", counted.Take(5).ToArray()) + (counted.Count > 5 ? ", ..." : string.Empty) + "). Under the phasing root the export"
                    + " carries " + (counted.Count == 1
                        ? "it as an empty node, so the export log's pairing note will name it as a helper node without a layer of its name"
                        : "them as empty nodes, so the export log's pairing note will name them as helper nodes without a layer of their name")
                    + ". That is expected: the export and the Planner's Generate from model are not affected.");
            }

            var onRoot = byLayer[scene.RootLayer.Name].Count(n => IsObject(n, scene));
            if (onRoot > 0)
            {
                plan.Notes.Add(PhasingFixScript.Count(onRoot, "object sits", "objects sit") + " on the phasing root layer " + scene.RootLayer.Name
                    + " itself, which holds no work; " + (onRoot == 1 ? "it is" : "they are") + " left as " + (onRoot == 1 ? "it is" : "they are") + ".");
            }
            return plan;
        }

        // An object of a layer: not a phasing helper, not another helper, not a shape the export leaves out.
        private static bool IsObject(PlannerSceneNode node, PlannerSceneModel scene)
        {
            return !scene.PhasingHelpers.Contains(node.Key) && !node.IsHelper && !scene.LeftOut(node);
        }

        // The top-most ancestor that is not a phasing helper: the object itself when its parent is one, or the scene root.
        private static PlannerSceneNode TopOf(PlannerSceneNode node, PlannerSceneModel scene)
        {
            var top = node;
            var seen = new HashSet<long> { node.Key };
            for (var parent = scene.ParentOf(top); parent != null && !scene.PhasingHelpers.Contains(parent.Key) && seen.Add(parent.Key); parent = scene.ParentOf(top))
            {
                top = parent;
            }
            return top;
        }

        private static PlannerReviewRow Problem(string name, string layerName, string why)
        {
            return new PlannerReviewRow { Status = "Problem", Item = "Object", Name = name, Change = "Skipped", From = "layer " + layerName, To = string.Empty, Why = why };
        }
    }

    /// <summary>A link plan written out as one MAXScript batch.</summary>
    public sealed class PlannerLinkBatch
    {
        public string Script;
        public readonly List<PlannerObjectLink> Links = new List<PlannerObjectLink>();
        public readonly List<string> Refused = new List<string>();
    }

    /// <summary>What came back from running a link batch.</summary>
    public sealed class PlannerLinkResult
    {
        public bool Completed;
        public string Aborted;
        public int Linked;
        public readonly List<string> Failures = new List<string>();

        public bool HasProblems
        {
            get { return !Completed || Aborted != null || Failures.Count > 0; }
        }

        public string Describe(PlannerLinkPlan after, bool held)
        {
            var lines = new List<string>();
            if (!Completed)
            {
                lines.Add("3ds Max did not report back, so it is not known how far the linking got.");
            }
            if (Aborted != null)
            {
                lines.Add("The linking stopped part-way, so some links may be missing: " + Aborted);
            }
            lines.Add(Linked == 0 ? "Nothing was linked." : "Done: " + PhasingFixScript.Count(Linked, "node linked under its layer's helper", "nodes linked under their layers' helpers") + ".");
            if (Failures.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(Failures.Count, "link was not made:", "links were not made:"));
                lines.AddRange(Failures.Select(f => "- " + f));
            }
            if (after != null)
            {
                lines.Add("Read again from the scene: " + after.Summary());
            }
            if (held)
            {
                lines.Add("Edit > Fetch puts the scene back as it was before the linking; Edit > Undo takes it back in one step.");
            }
            return string.Join("\r\n", lines.ToArray());
        }
    }

    /// <summary>
    /// Writes a <see cref="PlannerLinkPlan"/> as one MAXScript batch (one undo record) and reads back what it reports.
    /// Nodes are found by handle before anything changes; one renamed since the list was made is left alone. Setting a
    /// node's parent keeps it where it is in the world.
    /// </summary>
    public static class PlannerLinkScript
    {
        public const string EndMarker = "PLANNERLINK-END";
        public const string UndoLabel = "Link objects to their helpers";
        private const string Nl = "\r\n";

        public static PlannerLinkBatch Build(PlannerLinkPlan plan)
        {
            var batch = new PlannerLinkBatch();
            var handles = new List<string>();
            var names = new List<string>();
            var parents = new List<string>();
            var parentNames = new List<string>();
            foreach (var link in plan.Links)
            {
                var name = PhasingFixScript.Quote(link.Name);
                var parentName = PhasingFixScript.Quote(link.ToName);
                if (link.Key <= 0 || link.Key > int.MaxValue || link.ToKey <= 0 || link.ToKey > int.MaxValue || name == null || parentName == null)
                {
                    batch.Refused.Add(link.Name + " was not linked under " + link.ToName + ": its handle or name cannot be passed to MAXScript");
                    continue;
                }
                batch.Links.Add(link);
                handles.Add(PlannerLayerScript.Number(link.Key));
                names.Add(name);
                parents.Add(PlannerLayerScript.Number(link.ToKey));
                parentNames.Add(parentName);
            }

            var s = new StringBuilder();
            s.Append("-- UrbanCGI exporter: ").Append(UndoLabel).Append(" (").Append(PlannerLayerScript.Number(batch.Links.Count)).Append(" links)").Append(Nl);
            s.Append("(").Append(Nl);
            s.Append("\tlocal bLog = stringStream \"\"").Append(Nl);
            s.Append("\tlocal bNode, bParent, bDetail").Append(Nl);
            s.Append("\tlocal bLinked = 0").Append(Nl);
            PlannerLayerScript.WriteArray(s, "bHandles", handles);
            PlannerLayerScript.WriteArray(s, "bNames", names);
            PlannerLayerScript.WriteArray(s, "bParents", parents);
            PlannerLayerScript.WriteArray(s, "bParentNames", parentNames);
            s.Append(@"
	local bNodes = for h in bHandles collect (maxOps.getNodeByHandle h)
	local bHelpers = for h in bParents collect (maxOps.getNodeByHandle h)
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
						bParent = bHelpers[i]
						if (bNode == undefined or bParent == undefined) then
						(
							format ""FAIL\tlink\t%\tgone\n"" i to:bLog
						)
						else if (bNode.name != bNames[i] or bParent.name != bParentNames[i]) then
						(
							format ""FAIL\tlink\t%\tchanged\n"" i to:bLog
						)
						else
						(
" + PlannerLayerScript.LinkLines("\t\t\t\t\t\t\t") + @"
							if (bNode.parent == bParent) then (bLinked += 1) else (format ""FAIL\tlink\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("link") + @"
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
	format ""DONE\t%\n"" bLinked to:bLog
	format """ + EndMarker + @"\n"" to:bLog
	bLog as string
)
");
            batch.Script = s.ToString().Replace("\r\n", "\n").Replace("\n", Nl);
            return batch;
        }

        public static PlannerLinkResult ReadResult(PlannerLinkBatch batch, string output)
        {
            var result = new PlannerLinkResult();
            result.Failures.AddRange(batch.Refused);
            foreach (var parts in PlannerScriptText.Lines(output, EndMarker, out result.Completed))
            {
                switch (parts[0])
                {
                    case "DONE":
                        if (parts.Length >= 2)
                        {
                            result.Linked = PlannerScriptText.ParseCount(parts[1]);
                        }
                        break;
                    case "ABORT":
                        result.Aborted = parts.Length > 1 ? string.Join(" ", parts.Skip(1).ToArray()).Trim() : "unknown error";
                        break;
                    case "FAIL":
                        if (parts.Length >= 4)
                        {
                            var index = PlannerScriptText.ParseCount(parts[2]) - 1;
                            var detail = parts.Length > 4 ? string.Join(" ", parts.Skip(4).ToArray()).Trim() : null;
                            var link = index >= 0 && index < batch.Links.Count ? batch.Links[index] : null;
                            if (link == null)
                            {
                                result.Failures.Add("a link was not made (" + parts[1] + " " + parts[2] + ", " + parts[3] + ")" + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
                                break;
                            }
                            switch (parts[3])
                            {
                                case "gone":
                                    result.Failures.Add(link.Name + " was not linked under " + link.ToName + ": one of them is no longer in the scene");
                                    break;
                                case "changed":
                                    result.Failures.Add(link.Name + " was not linked under " + link.ToName + ": one of them has been renamed since the list was made");
                                    break;
                                case "refused":
                                    result.Failures.Add(link.Name + " could not be linked under " + link.ToName);
                                    break;
                                default:
                                    result.Failures.Add(link.Name + " was not linked under " + link.ToName + ": 3ds Max reported an error" + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
                                    break;
                            }
                        }
                        break;
                }
            }
            return result;
        }
    }
}
