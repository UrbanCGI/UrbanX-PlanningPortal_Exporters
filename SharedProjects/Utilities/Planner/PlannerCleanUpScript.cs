using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>A Clean up result written out as one MAXScript batch, with the items it carries in the order the script numbers them.</summary>
    public sealed class PlannerCleanUpBatch
    {
        public string Script;
        /// <summary>
        /// The nodes the batch finds by handle before anything changes, and the names they have then; a helper the batch makes
        /// has a place among them too (its key below zero, its name empty), which it takes when it is made.
        /// </summary>
        public readonly List<long> NodeKeys = new List<long>();
        public readonly List<string> NodeNames = new List<string>();
        /// <summary>The Max layers the batch finds by their current names before anything changes; a layer it makes has a place too (an empty name).</summary>
        public readonly List<string> LayerNames = new List<string>();
        public readonly List<PhasingNodeRename> Renames = new List<PhasingNodeRename>();
        public readonly List<PhasingNodeMove> Moves = new List<PhasingNodeMove>();
        public readonly List<PlannerObjectLink> Links = new List<PlannerObjectLink>();
        /// <summary>The helpers put on their Max layers (the Planner's list), then the objects put on their new helpers' layers.</summary>
        public readonly List<PlannerNodeLayerMove> LayerMoves = new List<PlannerNodeLayerMove>();
        public readonly List<PhasingMaxLayerMove> MaxLayerMoves = new List<PhasingMaxLayerMove>();
        public readonly List<PhasingMaxLayerRename> MaxLayerRenames = new List<PhasingMaxLayerRename>();
        /// <summary>The temporary name of each Max layer rename, by position.</summary>
        public readonly List<string> TemporaryNames = new List<string>();
        public readonly List<PlannerCleanUpNewLayer> NewLayers = new List<PlannerCleanUpNewLayer>();
        public readonly List<PlannerCleanUpNewHelper> NewHelpers = new List<PlannerCleanUpNewHelper>();
        public readonly List<PlannerCleanUpStamp> Stamps = new List<PlannerCleanUpStamp>();
        /// <summary>Items left out because MAXScript cannot be given them safely, as sentences for the modeller.</summary>
        public readonly List<string> Refused = new List<string>();
    }

    /// <summary>What came back from running a Clean up batch.</summary>
    public sealed class PlannerCleanUpOutcome
    {
        /// <summary>The batch's end marker came back: the script ran to its last line.</summary>
        public bool Completed;
        /// <summary>The error that stopped the batch part-way, or null.</summary>
        public string Aborted;
        /// <summary>Helpers renamed (of layers, phase groups and the phasing root).</summary>
        public int HelpersRenamed;
        /// <summary>Objects renamed, split pieces included.</summary>
        public int ObjectsRenamed;
        /// <summary>Helpers linked under their new parents: layers into their phase groups, and as the Planner's list nests them.</summary>
        public int HelpersMoved;
        public int Linked;
        /// <summary>Objects put on the Max layers of the helpers an answer links them under.</summary>
        public int PutOnLayers;
        public int MaxLayersMoved;
        public int MaxLayersRenamed;
        public int MaxLayersCreated;
        public int HelpersCreated;
        /// <summary>Existing helpers put on the Max layer of their name.</summary>
        public int HelpersPutOnLayers;
        /// <summary>Planner IDs written, on existing helpers and on the helpers made.</summary>
        public int IdsWritten;
        /// <summary>Changes that were not made, as sentences for the modeller.</summary>
        public readonly List<string> Failures = new List<string>();

        /// <summary>True when the batch did not run to its end, stopped, or left something undone.</summary>
        public bool HasProblems
        {
            get { return !Completed || Aborted != null || Failures.Count > 0; }
        }

        /// <summary>
        /// The outcome in a few plain lines: what was done, what was not, the objects that keep old names
        /// (<paramref name="applied"/>, the result the batch carried out), and what the scene still needs when it is read
        /// again (<paramref name="after"/>, Clean up planned on the scene after the batch; may be null).
        /// </summary>
        public string Describe(PlannerCleanUpResult applied, PlannerCleanUpPlan after, bool held)
        {
            var lines = new List<string>();
            if (!Completed)
            {
                lines.Add("3ds Max did not report back, so it is not known how far the clean-up got.");
            }
            if (Aborted != null)
            {
                lines.Add("The clean-up stopped part-way, so some changes may be missing: " + Aborted);
            }
            var done = new List<string>();
            if (MaxLayersCreated > 0) done.Add(PhasingFixScript.Count(MaxLayersCreated, "Max layer created", "Max layers created"));
            if (HelpersCreated > 0) done.Add(PhasingFixScript.Count(HelpersCreated, "helper created", "helpers created"));
            if (HelpersRenamed > 0) done.Add(PhasingFixScript.Count(HelpersRenamed, "helper renamed", "helpers renamed"));
            if (MaxLayersRenamed > 0) done.Add(PhasingFixScript.Count(MaxLayersRenamed, "Max layer renamed", "Max layers renamed"));
            if (ObjectsRenamed > 0) done.Add(PhasingFixScript.Count(ObjectsRenamed, "object renamed", "objects renamed") + " (split pieces included)");
            if (HelpersMoved > 0) done.Add(PhasingFixScript.Count(HelpersMoved, "helper moved under its group or parent", "helpers moved under their groups or parents"));
            if (MaxLayersMoved > 0) done.Add(PhasingFixScript.Count(MaxLayersMoved, "Max layer moved under its group's or parent's", "Max layers moved under their groups' or parents'"));
            if (HelpersPutOnLayers > 0) done.Add(PhasingFixScript.Count(HelpersPutOnLayers, "helper put on its Max layer", "helpers put on their Max layers"));
            if (IdsWritten > 0) done.Add(PhasingFixScript.Count(IdsWritten, "helper given its Planner ID", "helpers given their Planner IDs"));
            if (Linked > 0) done.Add(PhasingFixScript.Count(Linked, "node linked under its layer's helper", "nodes linked under their layers' helpers"));
            if (PutOnLayers > 0) done.Add(PhasingFixScript.Count(PutOnLayers, "object put on the Max layer of its new helper", "objects put on the Max layers of their new helpers"));
            lines.Add(done.Count == 0 ? "Nothing was changed." : "Done: " + PlannerScriptText.JoinWithAnd(done) + ".");
            if (Failures.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(Failures.Count, "change was not made:", "changes were not made:"));
                lines.AddRange(Failures.Select(f => "- " + f));
            }
            var kept = applied != null ? applied.KeptOldNamesText() : null;
            if (kept != null)
            {
                lines.Add(kept);
            }
            if (after != null)
            {
                if (after.Message != null)
                {
                    lines.Add("Read again from the scene: " + after.Message);
                }
                else
                {
                    var again = after.Resolve(null);
                    if (again.HasChanges)
                    {
                        lines.Add("Read again from the scene: " + again.Summary() + " Clean up again to finish, or see the list.");
                    }
                    else
                    {
                        lines.Add("Read again from the scene: nothing is left to change"
                            + (after.Naming == PlannerNamingKind.New ? "; the scene reads as the new names" : string.Empty)
                            + (again.Open > 0 ? "; " + PhasingFixScript.Count(again.Open, "listed object still waits", "listed objects still wait") + " for an answer (Clean up scene graph\u2026 lists them)" : string.Empty)
                            + ".");
                    }
                }
            }
            lines.Add(WayBack(held, MaxLayersRenamed + MaxLayersCreated + MaxLayersMoved > 0
                || (applied != null && applied.MaxLayerRenames.Count + applied.NewLayers.Count + applied.MaxLayerMoves.Count > 0)));
            return string.Join("\r\n", lines.ToArray());
        }

        /// <summary>
        /// How the clean-up is taken back. The batch is one undo record, but whether 3ds Max's Edit > Undo takes back the Max
        /// layers it renamed, moved or made (LayerManager calls) has not been checked in 3ds Max yet, so when it changed Max
        /// layers the hold is the sure way back, as for Add layer.
        /// </summary>
        public static string WayBack(bool held, bool maxLayersChanged)
        {
            if (!maxLayersChanged)
            {
                return held
                    ? "Edit > Fetch puts the scene back as it was before the clean-up; Edit > Undo takes it back in one step."
                    : "Edit > Undo takes it back in one step.";
            }
            const string undo = "Edit > Undo takes the clean-up back in one step, but whether it also takes back the Max layers it renamed, moved or made has not been checked in 3ds Max yet";
            return held
                ? "Edit > Fetch puts the scene back as it was before the clean-up, and is the sure way back: " + undo + "."
                : undo + ": check the Max layers after an undo.";
        }
    }

    /// <summary>
    /// Writes Clean up scene graph's operations (<see cref="PlannerCleanUpResult"/>) as one MAXScript batch and reads back
    /// what it reports. Pure text, so it is tested without 3ds Max; the Max side only runs the script.
    ///
    /// Before anything changes, the batch finds every node it touches by its handle (one renamed since the list was made is
    /// left alone) and every Max layer by its current name. Inside one undo record, "Clean up scene graph", it then renames
    /// the nodes (helpers, objects and their split pieces), renames the Max layers in two steps through temporary names (so a
    /// new name never meets an old one), makes the Max layers the Planner's list adds (under their parents), moves Max layers
    /// under their groups' or parents', makes the helpers the list adds (a Dummy at the origin on its layer, linked under its
    /// parent's helper, with its Planner ID), links the layer nodes into their phase groups and the helpers under their
    /// parents as the list nests them, then the objects under their layers' helpers (attached when the parent is a 3ds Max
    /// group head, as Link objects to their helpers does; world positions are kept), puts helpers on the Max layers of their
    /// names and an object an answer links under another layer's helper on that helper's Max layer, and writes the Planner
    /// IDs (user property planner_id, as Build layers and helpers wrote them). No date is written anywhere (D20). Each item
    /// reports its own failure; the last lines are the counts and an end marker, so a batch that stopped early is recognised.
    /// Nothing is deleted.
    /// </summary>
    public static class PlannerCleanUpScript
    {
        public const string EndMarker = "PLANNERCLEANUP-END";
        public const string UndoLabel = PlannerCleanUp.UndoLabel;

        private const string Nl = "\r\n";
        // What a rename is about, for the counts: a helper (of a layer, a phase group or the root), or an object.
        private const int HelperKind = 1;
        private const int ObjectKind = 2;
        private const string NameProblem = "a name holds a control character that cannot be passed to MAXScript";

        /// <summary>
        /// The MAXScript batch for <paramref name="result"/>, which <paramref name="plan"/> resolved (the plan holds the scene it
        /// read: the batch checks each node still has the name it had then). <paramref name="temporaryPrefix"/> starts the
        /// temporary Max layer names (the Max side passes something unique, e.g. with a GUID in it).
        /// </summary>
        public static PlannerCleanUpBatch Build(PlannerCleanUpPlan plan, PlannerCleanUpResult result, string temporaryPrefix)
        {
            var batch = new PlannerCleanUpBatch();
            var prefix = string.IsNullOrEmpty(temporaryPrefix) ? "__planner_cleanup_" : temporaryPrefix;
            var original = plan != null && plan.State != null && plan.State.Original != null ? plan.State.Original : new PlannerSceneSnapshot();
            var nameNow = new Dictionary<long, string>();
            foreach (var node in original.Nodes.Where(n => n != null))
            {
                if (!nameNow.ContainsKey(node.Key))
                {
                    nameNow[node.Key] = node.Name ?? string.Empty;
                }
            }

            // Every node the batch touches, found once by its handle (numbered 1, 2, ...); null when MAXScript cannot be given
            // it. Its name is the one the plan read, so a node renamed since is left alone. The helpers the batch makes are
            // numbered among them first, with no handle (key below zero), and take their place when made.
            var nodeIndex = new Dictionary<long, int>();
            var handles = new List<string>();
            var names = new List<string>();
            foreach (var helper in result.NewHelpers)
            {
                batch.NodeKeys.Add(helper.Key);
                batch.NodeNames.Add(string.Empty);
                handles.Add("0");
                names.Add("\"\"");
                nodeIndex[helper.Key] = batch.NodeKeys.Count;
            }
            int? NodeAt(long key)
            {
                int index;
                if (nodeIndex.TryGetValue(key, out index))
                {
                    return index > 0 ? index : (int?)null;
                }
                string name;
                if (!nameNow.TryGetValue(key, out name))
                {
                    name = string.Empty;
                }
                var literal = PhasingFixScript.Quote(name);
                if (key <= 0 || key > int.MaxValue || literal == null)
                {
                    nodeIndex[key] = 0;
                    return null;
                }
                batch.NodeKeys.Add(key);
                batch.NodeNames.Add(name);
                handles.Add(Number(key));
                names.Add(literal);
                nodeIndex[key] = batch.NodeKeys.Count;
                return batch.NodeKeys.Count;
            }
            string NodeProblem(long key)
            {
                return key <= 0 || key > int.MaxValue ? "its node handle cannot be passed to MAXScript" : NameProblem;
            }

            // Every Max layer the batch touches, found once by its current name before anything is renamed; the layers it makes
            // are numbered among them first, with no name, and take their place when made.
            var layerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var newLayerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var layerNames = new List<string>();
            foreach (var layer in result.NewLayers)
            {
                batch.LayerNames.Add(string.Empty);
                layerNames.Add("\"\"");
                newLayerIndex[layer.Name] = batch.LayerNames.Count;
            }
            int? LayerAt(string name, bool isNew = false)
            {
                int index;
                if (name == null)
                {
                    return null;
                }
                if (isNew)
                {
                    return newLayerIndex.TryGetValue(name, out index) ? index : (int?)null;
                }
                if (layerIndex.TryGetValue(name, out index))
                {
                    return index > 0 ? index : (int?)null;
                }
                var literal = PhasingFixScript.Quote(name);
                if (literal == null)
                {
                    layerIndex[name] = 0;
                    return null;
                }
                batch.LayerNames.Add(name);
                layerNames.Add(literal);
                layerIndex[name] = batch.LayerNames.Count;
                return batch.LayerNames.Count;
            }

            var renameNode = new List<string>();
            var renameNew = new List<string>();
            var renameKind = new List<string>();
            foreach (var rename in result.NodeRenames)
            {
                var index = NodeAt(rename.Key);
                var newName = PhasingFixScript.Quote(rename.NewName);
                if (index == null || newName == null)
                {
                    batch.Refused.Add(rename.OldName + " is not renamed to " + rename.NewName + ": " + (index == null ? NodeProblem(rename.Key) : NameProblem));
                    continue;
                }
                batch.Renames.Add(rename);
                renameNode.Add(Number(index.Value));
                renameNew.Add(newName);
                renameKind.Add(Number(rename.Item == PhasingFixItem.Object ? ObjectKind : HelperKind));
            }

            var layerRenameIndex = new List<string>();
            var layerRenameNew = new List<string>();
            var layerRenameTemp = new List<string>();
            foreach (var rename in result.MaxLayerRenames)
            {
                var index = LayerAt(rename.OldName);
                var newName = PhasingFixScript.Quote(rename.NewName);
                if (index == null || newName == null)
                {
                    batch.Refused.Add("the Max layer " + rename.OldName + " is not renamed to " + rename.NewName + ": " + NameProblem);
                    continue;
                }
                var temporary = prefix + Number(batch.MaxLayerRenames.Count + 1);
                batch.MaxLayerRenames.Add(rename);
                batch.TemporaryNames.Add(temporary);
                layerRenameIndex.Add(Number(index.Value));
                layerRenameNew.Add(newName);
                layerRenameTemp.Add(PhasingFixScript.Quote(temporary));
            }

            var newLayerSlot = new List<string>();
            var newLayerName = new List<string>();
            var newLayerParent = new List<string>();
            foreach (var layer in result.NewLayers)
            {
                var name = PhasingFixScript.Quote(layer.Name);
                var parent = layer.ParentName == null ? 0 : LayerAt(layer.ParentName, layer.ParentIsNew);
                if (name == null || parent == null)
                {
                    batch.Refused.Add("the Max layer " + layer.Name + " is not created: " + NameProblem);
                    continue;
                }
                batch.NewLayers.Add(layer);
                newLayerSlot.Add(Number(newLayerIndex[layer.Name]));
                newLayerName.Add(name);
                newLayerParent.Add(Number(parent.Value));
            }

            var layerMoveChild = new List<string>();
            var layerMoveParent = new List<string>();
            foreach (var move in result.MaxLayerMoves)
            {
                var child = LayerAt(move.Name);
                var parent = LayerAt(move.NewParentName, move.NewParentIsNew);
                if (child == null || parent == null)
                {
                    batch.Refused.Add("the Max layer " + move.Name + " is not moved under " + move.NewParentName + ": " + NameProblem);
                    continue;
                }
                batch.MaxLayerMoves.Add(move);
                layerMoveChild.Add(Number(child.Value));
                layerMoveParent.Add(Number(parent.Value));
            }

            var newNodeSlot = new List<string>();
            var newNodeName = new List<string>();
            var newNodeLayer = new List<string>();
            var newNodeParent = new List<string>();
            var newNodeId = new List<string>();
            foreach (var helper in result.NewHelpers)
            {
                var name = PhasingFixScript.Quote(helper.Name);
                var layer = LayerAt(helper.LayerName, helper.LayerIsNew);
                var parent = helper.ParentKey.HasValue ? NodeAt(helper.ParentKey.Value) : 0;
                var id = helper.PlannerId != null ? PhasingFixScript.Quote(PlannerLayerScript.StoredId(helper.PlannerId)) : "\"\"";
                if (name == null || layer == null || parent == null || id == null)
                {
                    batch.Refused.Add("the helper " + helper.Name + " is not created: " + (parent == null ? NodeProblem(helper.ParentKey.Value) : NameProblem));
                    continue;
                }
                batch.NewHelpers.Add(helper);
                newNodeSlot.Add(Number(nodeIndex[helper.Key]));
                newNodeName.Add(name);
                newNodeLayer.Add(Number(layer.Value));
                newNodeParent.Add(Number(parent.Value));
                newNodeId.Add(id);
            }

            var moveNode = new List<string>();
            var moveParent = new List<string>();
            foreach (var move in result.NodeMoves)
            {
                var index = NodeAt(move.Key);
                var parent = NodeAt(move.ToParentKey);
                if (index == null || parent == null)
                {
                    batch.Refused.Add(move.Name + " is not moved into " + move.ToParentName + ": " + NodeProblem(index == null ? move.Key : move.ToParentKey));
                    continue;
                }
                batch.Moves.Add(move);
                moveNode.Add(Number(index.Value));
                moveParent.Add(Number(parent.Value));
            }

            var linkNode = new List<string>();
            var linkParent = new List<string>();
            foreach (var link in result.Links)
            {
                var index = NodeAt(link.Key);
                var parent = NodeAt(link.ToKey);
                if (index == null || parent == null)
                {
                    batch.Refused.Add(link.Name + " is not linked under " + link.ToName + ": " + NodeProblem(index == null ? link.Key : link.ToKey));
                    continue;
                }
                batch.Links.Add(link);
                linkNode.Add(Number(index.Value));
                linkParent.Add(Number(parent.Value));
            }

            // Helpers onto the Max layers of their names, then objects onto their new helpers' layers.
            var putNode = new List<string>();
            var putLayer = new List<string>();
            var putKind = new List<string>();
            foreach (var put in result.HelperPuts.Select(p => new { Put = p, Kind = HelperKind }).Concat(result.LayerMoves.Select(p => new { Put = p, Kind = ObjectKind })))
            {
                var index = NodeAt(put.Put.Key);
                var onto = LayerAt(put.Put.ToLayer, put.Put.ToNewLayer);
                if (index == null || onto == null)
                {
                    batch.Refused.Add(put.Put.Name + " is not put on the Max layer " + put.Put.ToLayer + ": " + (index == null ? NodeProblem(put.Put.Key) : NameProblem));
                    continue;
                }
                batch.LayerMoves.Add(put.Put);
                putNode.Add(Number(index.Value));
                putLayer.Add(Number(onto.Value));
                putKind.Add(Number(put.Kind));
            }

            var stampNode = new List<string>();
            var stampId = new List<string>();
            foreach (var stamp in result.Stamps)
            {
                var index = NodeAt(stamp.Key);
                var id = PhasingFixScript.Quote(PlannerLayerScript.StoredId(stamp.PlannerId));
                if (index == null || id == null)
                {
                    batch.Refused.Add("the helper " + stamp.Name + " does not get its Planner ID: " + (index == null ? NodeProblem(stamp.Key) : NameProblem));
                    continue;
                }
                batch.Stamps.Add(stamp);
                stampNode.Add(Number(index.Value));
                stampId.Add(id);
            }

            var s = new StringBuilder();
            s.Append("-- UrbanCGI exporter: ").Append(UndoLabel).Append(" (").Append(Number(batch.Renames.Count)).Append(" renames, ")
                .Append(Number(batch.MaxLayerRenames.Count)).Append(" Max layer renames, ").Append(Number(batch.NewLayers.Count)).Append(" Max layers to create, ")
                .Append(Number(batch.MaxLayerMoves.Count)).Append(" Max layer moves, ").Append(Number(batch.NewHelpers.Count)).Append(" helpers to create, ")
                .Append(Number(batch.Moves.Count)).Append(" helpers to move, ").Append(Number(batch.Links.Count)).Append(" links, ")
                .Append(Number(batch.LayerMoves.Count)).Append(" nodes onto other layers, ").Append(Number(batch.Stamps.Count)).Append(" Planner IDs)").Append(Nl);
            s.Append("(").Append(Nl);
            s.Append("\tlocal bLog = stringStream \"\"").Append(Nl);
            s.Append("\tlocal bNode, bParent, bLayer, bOther, bDetail").Append(Nl);
            s.Append("\tlocal bHelpersRenamed = 0, bObjectsRenamed = 0, bMoved = 0, bLinked = 0, bPut = 0, bLayersMoved = 0, bLayersRenamed = 0, bLayersCreated = 0, bHelpersCreated = 0, bHelpersPut = 0, bStamped = 0").Append(Nl);
            PlannerLayerScript.WriteArray(s, "bHandles", handles);
            PlannerLayerScript.WriteArray(s, "bNames", names);
            PlannerLayerScript.WriteArray(s, "bRenameNode", renameNode);
            PlannerLayerScript.WriteArray(s, "bRenameNew", renameNew);
            PlannerLayerScript.WriteArray(s, "bRenameKind", renameKind);
            PlannerLayerScript.WriteArray(s, "bLayerNames", layerNames);
            PlannerLayerScript.WriteArray(s, "bLayerRenameIdx", layerRenameIndex);
            PlannerLayerScript.WriteArray(s, "bLayerRenameNew", layerRenameNew);
            PlannerLayerScript.WriteArray(s, "bLayerRenameTemp", layerRenameTemp);
            PlannerLayerScript.WriteArray(s, "bNewLayer", newLayerSlot);
            PlannerLayerScript.WriteArray(s, "bNewLayerName", newLayerName);
            PlannerLayerScript.WriteArray(s, "bNewLayerParent", newLayerParent);
            PlannerLayerScript.WriteArray(s, "bLayerMoveChild", layerMoveChild);
            PlannerLayerScript.WriteArray(s, "bLayerMoveParent", layerMoveParent);
            PlannerLayerScript.WriteArray(s, "bNewNode", newNodeSlot);
            PlannerLayerScript.WriteArray(s, "bNewNodeName", newNodeName);
            PlannerLayerScript.WriteArray(s, "bNewNodeLayer", newNodeLayer);
            PlannerLayerScript.WriteArray(s, "bNewNodeParent", newNodeParent);
            PlannerLayerScript.WriteArray(s, "bNewNodeId", newNodeId);
            PlannerLayerScript.WriteArray(s, "bMoveNode", moveNode);
            PlannerLayerScript.WriteArray(s, "bMoveParent", moveParent);
            PlannerLayerScript.WriteArray(s, "bLinkNode", linkNode);
            PlannerLayerScript.WriteArray(s, "bLinkParent", linkParent);
            PlannerLayerScript.WriteArray(s, "bPutNode", putNode);
            PlannerLayerScript.WriteArray(s, "bPutLayer", putLayer);
            PlannerLayerScript.WriteArray(s, "bPutKind", putKind);
            PlannerLayerScript.WriteArray(s, "bStampNode", stampNode);
            PlannerLayerScript.WriteArray(s, "bStampId", stampId);

            // Everything found before anything changes: Max layers by their current names, nodes by handle (and name). The
            // places of the layers and helpers the batch makes stay empty until they are made.
            s.Append(@"
	local bLayers = for n in bLayerNames collect (if n == """" then undefined else (LayerManager.getLayerFromName n))
	for i = 1 to bLayers.count where bLayerNames[i] != """" and bLayers[i] == undefined do (format ""FAIL\tlayerfind\t%\tgone\n"" i to:bLog)
	local bNodes = #()
	for i = 1 to bHandles.count do
	(
		bNode = undefined
		if bHandles[i] != 0 do
		(
			bNode = maxOps.getNodeByHandle bHandles[i]
			if (bNode == undefined) then
			(
				format ""FAIL\tfind\t%\tgone\n"" i to:bLog
			)
			else if (bNode.name != bNames[i]) then
			(
				format ""FAIL\tfind\t%\tchanged\n"" i to:bLog
				bNode = undefined
			)
		)
		append bNodes bNode
	)
	local bStepped = for i = 1 to bLayerRenameIdx.count collect false
");
            s.Append("\ttry").Append(Nl);
            s.Append("\t(").Append(Nl);
            s.Append("\t\tundo ").Append(PhasingFixScript.Quote(UndoLabel)).Append(" on").Append(Nl);
            s.Append("\t\t(").Append(Nl);
            s.Append("\t\t\twith redraw off").Append(Nl);
            s.Append("\t\t\t(").Append(Nl);

            // Node renames: helpers, objects and their split pieces.
            s.Append(@"
				for i = 1 to bRenameNode.count do
				(
					try
					(
						bNode = bNodes[bRenameNode[i]]
						if (bNode != undefined) do
						(
							bNode.name = bRenameNew[i]
							if (bNode.name == bRenameNew[i]) then (if (bRenameKind[i] == " + Number(ObjectKind) + @") then (bObjectsRenamed += 1) else (bHelpersRenamed += 1)) else (format ""FAIL\trename\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("rename") + @"
					)
				)
");
            // Max layers renamed in two steps through temporary names (the steps the phasing batch has always written), before
            // any layer is made, so a new layer may take a name a renamed one gives up.
            PhasingFixScript.AppendMaxLayerRenames(s, "b", "bLayerRenameIdx", string.Empty, PlannerLayerScript.CatchLine);
            // The Max layers the Planner's list adds, parents first, as Build layers and helpers makes them.
            s.Append(@"
				for i = 1 to bNewLayer.count do
				(
					try
					(
						bLayer = LayerManager.newLayerFromName bNewLayerName[i]
						if (bLayer == undefined) then
						(
							format ""FAIL\tlayercreate\t%\ttaken\n"" i to:bLog
						)
						else
						(
							bLayers[bNewLayer[i]] = bLayer
							bLayersCreated += 1
							if (bNewLayerParent[i] > 0) do
							(
								bParent = bLayers[bNewLayerParent[i]]
								if (bParent == undefined) then (format ""FAIL\tlayerparent\t%\tgone\n"" i to:bLog) else (bLayer.setParent bParent)
							)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("layercreate") + @"
					)
				)
");
            // Max layers under their group's or parent's Max layer, new parents included.
            PhasingFixScript.AppendMaxLayerMoves(s, "b", string.Empty, PlannerLayerScript.CatchLine);
            // The helpers the Planner's list adds: a Dummy at the origin on its layer, with its Planner ID, linked under its
            // parent's helper.
            s.Append(@"
				for i = 1 to bNewNode.count do
				(
					try
					(
						bLayer = if (bNewNodeLayer[i] > 0) then bLayers[bNewNodeLayer[i]] else undefined
						if (bLayer == undefined) then
						(
							format ""FAIL\thelpercreate\t%\tnolayer\n"" i to:bLog
						)
						else
						(
							bNode = Dummy name:bNewNodeName[i] pos:[0,0,0] boxsize:" + PlannerLayerScript.DummyBoxSize + @"
							bLayer.addNode bNode
							bNodes[bNewNode[i]] = bNode
							bHelpersCreated += 1
							if (bNewNodeId[i] != """") do
							(
								setUserProp bNode " + PhasingFixScript.Quote(PlannerSceneModel.PlannerIdProperty) + @" bNewNodeId[i]
								bStamped += 1
							)
							if (bNewNodeParent[i] > 0) do
							(
								bParent = bNodes[bNewNodeParent[i]]
								if (bParent == undefined) then
								(
									format ""FAIL\thelperparent\t%\tgone\n"" i to:bLog
								)
								else
								(
" + PlannerLayerScript.LinkLines("\t\t\t\t\t\t\t\t\t") + @"
									if (bNode.parent != bParent) do (format ""FAIL\thelperparent\t%\trefused\n"" i to:bLog)
								)
							)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("helpercreate") + @"
					)
				)
");
            // Layer nodes into the group of their phase and helpers under their parents, then the objects under their layers'
            // helpers, as Link does.
            AppendLinkLoop(s, "bMoveNode", "bMoveParent", "move", "bMoved");
            AppendLinkLoop(s, "bLinkNode", "bLinkParent", "link", "bLinked");
            // Helpers onto the Max layers of their names; an object an answer links under another layer's helper onto that
            // helper's Max layer.
            s.Append(@"
				for i = 1 to bPutNode.count do
				(
					try
					(
						bNode = bNodes[bPutNode[i]]
						bLayer = bLayers[bPutLayer[i]]
						if (bNode == undefined or bLayer == undefined) then
						(
							format ""FAIL\tput\t%\tgone\n"" i to:bLog
						)
						else
						(
							bLayer.addNode bNode
							if (bNode.layer != undefined and bNode.layer.name == bLayer.name) then (if (bPutKind[i] == " + Number(HelperKind) + @") then (bHelpersPut += 1) else (bPut += 1)) else (format ""FAIL\tput\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("put") + @"
					)
				)
");
            // The Planner IDs on existing helpers.
            s.Append(@"
				for i = 1 to bStampNode.count do
				(
					try
					(
						bNode = bNodes[bStampNode[i]]
						if (bNode != undefined) do
						(
							setUserProp bNode " + PhasingFixScript.Quote(PlannerSceneModel.PlannerIdProperty) + @" bStampId[i]
							bStamped += 1
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine("stamp") + @"
					)
				)
");
            s.Append("\t\t\t)").Append(Nl);
            s.Append("\t\t)").Append(Nl);
            s.Append("\t)").Append(Nl);
            s.Append("\tcatch").Append(Nl);
            s.Append("\t(").Append(Nl);
            s.Append("\t\tbDetail = (getCurrentException()) as string").Append(Nl);
            s.Append("\t\t").Append(PlannerLayerScript.CleanDetail()).Append(Nl);
            s.Append("\t\tformat \"ABORT\\t%\\n\" bDetail to:bLog").Append(Nl);
            s.Append("\t)").Append(Nl);
            s.Append("\tformat \"DONE\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\n\" bHelpersRenamed bObjectsRenamed bMoved bLinked bPut bLayersMoved bLayersRenamed bLayersCreated bHelpersCreated bHelpersPut bStamped to:bLog").Append(Nl);
            s.Append("\tformat \"").Append(EndMarker).Append("\\n\" to:bLog").Append(Nl);
            s.Append("\tbLog as string").Append(Nl);
            s.Append(")").Append(Nl);
            batch.Script = s.ToString().Replace("\r\n", "\n").Replace("\n", Nl);
            return batch;
        }

        // A loop linking nodes[child[i]] under nodes[parent[i]], as Link objects to their helpers links them.
        private static void AppendLinkLoop(StringBuilder s, string children, string parents, string kind, string counter)
        {
            s.Append(@"
				for i = 1 to " + children + @".count do
				(
					try
					(
						bNode = bNodes[" + children + @"[i]]
						bParent = bNodes[" + parents + @"[i]]
						if (bNode == undefined or bParent == undefined) then
						(
							format ""FAIL\t" + kind + @"\t%\tgone\n"" i to:bLog
						)
						else
						(
" + PlannerLayerScript.LinkLines("\t\t\t\t\t\t\t") + @"
							if (bNode.parent == bParent) then (" + counter + @" += 1) else (format ""FAIL\t" + kind + @"\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + PlannerLayerScript.CatchLine(kind) + @"
					)
				)
");
        }

        /// <summary>Reads what the batch sent back. Anything unexpected is tolerated: a missing marker only clears <see cref="PlannerCleanUpOutcome.Completed"/>.</summary>
        public static PlannerCleanUpOutcome ReadResult(PlannerCleanUpBatch batch, string output)
        {
            var result = new PlannerCleanUpOutcome();
            result.Failures.AddRange(batch.Refused);
            foreach (var parts in PlannerScriptText.Lines(output, EndMarker, out result.Completed))
            {
                switch (parts[0])
                {
                    case "DONE":
                        if (parts.Length >= 12)
                        {
                            result.HelpersRenamed = PlannerScriptText.ParseCount(parts[1]);
                            result.ObjectsRenamed = PlannerScriptText.ParseCount(parts[2]);
                            result.HelpersMoved = PlannerScriptText.ParseCount(parts[3]);
                            result.Linked = PlannerScriptText.ParseCount(parts[4]);
                            result.PutOnLayers = PlannerScriptText.ParseCount(parts[5]);
                            result.MaxLayersMoved = PlannerScriptText.ParseCount(parts[6]);
                            result.MaxLayersRenamed = PlannerScriptText.ParseCount(parts[7]);
                            result.MaxLayersCreated = PlannerScriptText.ParseCount(parts[8]);
                            result.HelpersCreated = PlannerScriptText.ParseCount(parts[9]);
                            result.HelpersPutOnLayers = PlannerScriptText.ParseCount(parts[10]);
                            result.IdsWritten = PlannerScriptText.ParseCount(parts[11]);
                        }
                        break;
                    case "ABORT":
                        result.Aborted = parts.Length > 1 ? string.Join(" ", parts.Skip(1).ToArray()).Trim() : "unknown error";
                        break;
                    case "FAIL":
                        if (parts.Length >= 4)
                        {
                            var detail = parts.Length > 4 ? string.Join(" ", parts.Skip(4).ToArray()).Trim() : null;
                            result.Failures.Add(DescribeFailure(batch, parts[1], PlannerScriptText.ParseCount(parts[2]) - 1, parts[3], detail));
                        }
                        break;
                }
            }
            return result;
        }

        private static string DescribeFailure(PlannerCleanUpBatch batch, string kind, int index, string code, string detail)
        {
            var error = "3ds Max reported an error" + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
            Func<string, bool> gone = c => c == "gone";
            switch (kind)
            {
                case "find":
                    if (index >= 0 && index < batch.NodeNames.Count)
                    {
                        return code == "changed"
                            ? batch.NodeNames[index] + " has been renamed since the list was made, so it was left as it is"
                            : batch.NodeNames[index] + " is no longer in the scene, so it was not changed";
                    }
                    break;
                case "layerfind":
                    if (index >= 0 && index < batch.LayerNames.Count)
                    {
                        return "the Max layer " + batch.LayerNames[index] + " is no longer in the scene, so it was not changed";
                    }
                    break;
                case "rename":
                    if (index >= 0 && index < batch.Renames.Count)
                    {
                        var rename = batch.Renames[index];
                        return code == "refused"
                            ? rename.OldName + " could not be renamed to " + rename.NewName
                            : rename.OldName + " was not renamed to " + rename.NewName + ": " + error;
                    }
                    break;
                case "move":
                    if (index >= 0 && index < batch.Moves.Count)
                    {
                        var move = batch.Moves[index];
                        return gone(code) ? move.Name + " was not moved into " + move.ToParentName + ": one of them was not found"
                            : code == "refused" ? move.Name + " could not be moved into " + move.ToParentName
                            : move.Name + " was not moved into " + move.ToParentName + ": " + error;
                    }
                    break;
                case "link":
                    if (index >= 0 && index < batch.Links.Count)
                    {
                        var link = batch.Links[index];
                        return gone(code) ? link.Name + " was not linked under " + link.ToName + ": one of them was not found"
                            : code == "refused" ? link.Name + " could not be linked under " + link.ToName
                            : link.Name + " was not linked under " + link.ToName + ": " + error;
                    }
                    break;
                case "put":
                    if (index >= 0 && index < batch.LayerMoves.Count)
                    {
                        var put = batch.LayerMoves[index];
                        return gone(code) ? put.Name + " was not put on the Max layer " + put.ToLayer + ": one of them was not found"
                            : code == "refused" ? put.Name + " could not be put on the Max layer " + put.ToLayer
                            : put.Name + " was not put on the Max layer " + put.ToLayer + ": " + error;
                    }
                    break;
                case "layermove":
                    if (index >= 0 && index < batch.MaxLayerMoves.Count)
                    {
                        return PhasingFixScript.MaxLayerMoveFailure(batch.MaxLayerMoves[index], code, error);
                    }
                    break;
                case "layer":
                    if (index >= 0 && index < batch.MaxLayerRenames.Count)
                    {
                        return PhasingFixScript.MaxLayerRenameFailure(batch.MaxLayerRenames[index], batch.TemporaryNames[index], code, error);
                    }
                    break;
                case "layercreate":
                    if (index >= 0 && index < batch.NewLayers.Count)
                    {
                        var layer = batch.NewLayers[index];
                        return code == "taken"
                            ? "the Max layer " + layer.Name + " was not created: a layer of that name is already there"
                            : "the Max layer " + layer.Name + " was not created: " + error;
                    }
                    break;
                case "layerparent":
                    if (index >= 0 && index < batch.NewLayers.Count)
                    {
                        var layer = batch.NewLayers[index];
                        return "the new Max layer " + layer.Name + " was not put under " + layer.ParentName + ", which is missing; it is at the top";
                    }
                    break;
                case "helpercreate":
                    if (index >= 0 && index < batch.NewHelpers.Count)
                    {
                        var helper = batch.NewHelpers[index];
                        return code == "nolayer"
                            ? "the helper " + helper.Name + " was not created: its Max layer " + helper.LayerName + " is missing"
                            : "the helper " + helper.Name + " was not created: " + error;
                    }
                    break;
                case "helperparent":
                    if (index >= 0 && index < batch.NewHelpers.Count)
                    {
                        var helper = batch.NewHelpers[index];
                        return gone(code)
                            ? "the new helper " + helper.Name + " was not linked under " + helper.ParentName + ", which is missing; it is at the top of the scene"
                            : "the new helper " + helper.Name + " could not be linked under " + helper.ParentName;
                    }
                    break;
                case "stamp":
                    if (index >= 0 && index < batch.Stamps.Count)
                    {
                        return "the helper " + batch.Stamps[index].Name + " did not get its Planner ID: " + error;
                    }
                    break;
            }
            return "an item of the clean-up was not carried out (" + kind + " " + (index + 1).ToString(CultureInfo.InvariantCulture) + ", " + code + ")"
                + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
