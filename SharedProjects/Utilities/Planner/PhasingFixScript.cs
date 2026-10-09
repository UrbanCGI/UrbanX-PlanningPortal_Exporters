using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>A plan written out as one MAXScript batch, with the items it carries in the order the script numbers them.</summary>
    public sealed class PhasingFixBatch
    {
        public string Script;
        public readonly List<PhasingNodeRename> NodeRenames = new List<PhasingNodeRename>();
        public readonly List<PhasingNodeMove> NodeMoves = new List<PhasingNodeMove>();
        public readonly List<PhasingMaxLayerMove> MaxLayerMoves = new List<PhasingMaxLayerMove>();
        public readonly List<PhasingMaxLayerRename> MaxLayerRenames = new List<PhasingMaxLayerRename>();
        /// <summary>The temporary name of each Max layer rename, by position.</summary>
        public readonly List<string> TemporaryNames = new List<string>();
        /// <summary>Items left out because MAXScript cannot be given them safely, as sentences for the modeller.</summary>
        public readonly List<string> Refused = new List<string>();
    }

    /// <summary>What came back from running a batch.</summary>
    public sealed class PhasingFixResult
    {
        /// <summary>The batch's end marker came back: the script ran to its last line.</summary>
        public bool Completed;
        /// <summary>The error that stopped the batch part-way, or null.</summary>
        public string Aborted;
        public int NodesRenamed;
        public int NodesMoved;
        public int MaxLayersRenamed;
        public int MaxLayersMoved;
        /// <summary>Changes that were not made, as sentences for the modeller.</summary>
        public readonly List<string> Failures = new List<string>();
        /// <summary>The batch this result is about.</summary>
        public PhasingFixBatch Batch;

        /// <summary>True when the batch did not run to its end, stopped, or left something undone.</summary>
        public bool HasProblems
        {
            get { return !Completed || Aborted != null || Failures.Count > 0; }
        }

        /// <summary>
        /// The outcome in a few plain lines: what was done, what was not, and what the scene still needs when it is
        /// read again (<paramref name="after"/>, the plan made from the scene after the batch; may be null).
        /// </summary>
        public string Describe(PhasingFixPlan after, bool held)
        {
            var lines = new List<string>();
            if (!Completed)
            {
                lines.Add("3ds Max did not report back, so it is not known how far the fix got.");
            }
            if (Aborted != null)
            {
                lines.Add("The fix stopped part-way, so some changes may be missing: " + Aborted);
            }
            var done = new List<string>();
            if (NodesRenamed > 0) done.Add(PhasingFixScript.Count(NodesRenamed, "name corrected", "names corrected"));
            if (NodesMoved > 0) done.Add(PhasingFixScript.Count(NodesMoved, "layer moved into its group", "layers moved into their groups"));
            if (MaxLayersRenamed > 0) done.Add(PhasingFixScript.Count(MaxLayersRenamed, "Max layer renamed", "Max layers renamed"));
            if (MaxLayersMoved > 0) done.Add(PhasingFixScript.Count(MaxLayersMoved, "Max layer moved under its group", "Max layers moved under their groups"));
            lines.Add(done.Count == 0 ? "Nothing was changed." : "Done: " + JoinWithAnd(done) + ".");
            if (Failures.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(Failures.Count, "change was not made:", "changes were not made:"));
                lines.AddRange(Failures.Select(f => "- " + f));
            }
            if (after != null)
            {
                if (after.TopKey == null)
                {
                    lines.Add("Read again from the scene: " + after.Message);
                }
                else if (after.HasChanges)
                {
                    lines.Add("Read again from the scene: " + after.Summary());
                }
                else if (HasProblems)
                {
                    // The plan pairs a Max layer with a node by the node's name, so a Max layer left behind by a node
                    // that was renamed is not seen again: what was not done is not "nothing left".
                    lines.Add("Read again from the scene: the names read as fixed, "
                        + (Failures.Count > 0 ? "apart from the changes listed above, which need doing by hand" : "but the fix did not finish, so check the Max layers by hand")
                        + (after.ToCheck > 0 ? "; " + after.ToCheck.ToString(CultureInfo.InvariantCulture) + " to check by hand in the list." : "."));
                }
                else
                {
                    lines.Add("Read again from the scene: nothing is left to change"
                        + (after.ToCheck > 0 ? ", " + after.ToCheck.ToString(CultureInfo.InvariantCulture) + " still to check by hand." : "."));
                }
            }
            if (held)
            {
                lines.Add("Edit > Fetch puts the scene back as it was before the fix.");
            }
            return string.Join("\r\n", lines.ToArray());
        }

        private static string JoinWithAnd(List<string> parts)
        {
            if (parts.Count == 1)
            {
                return parts[0];
            }
            return string.Join(", ", parts.Take(parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
        }
    }

    /// <summary>
    /// Writes a <see cref="PhasingFixPlan"/> as one MAXScript batch and reads back what it reports. Pure text, so it
    /// is tested without 3ds Max; the Max side only runs the script.
    ///
    /// The batch finds nodes by handle (maxOps.getNodeByHandle) and Max layers by their current names, all before
    /// anything is renamed; renames the nodes, moves the layer nodes (attachNodesToGroup when the target is a group
    /// head), re-parents the Max layers, then renames the Max layers in two steps through temporary names so a new
    /// name never meets an old one. It all runs inside one undo record. Each item reports its own failure; the last
    /// lines are the counts and an end marker, so a batch that stopped early is recognised.
    /// </summary>
    public static class PhasingFixScript
    {
        public const string EndMarker = "PHASINGFIX-END";
        public const string UndoLabel = "Fix phasing names";

        private const string Nl = "\r\n";

        /// <summary>
        /// The text as a MAXScript string literal, quotes included, or null when it holds a control character other
        /// than tab, line feed or carriage return (which MAXScript cannot be given safely as a literal).
        /// </summary>
        public static string Quote(string text)
        {
            var value = text ?? string.Empty;
            var literal = new StringBuilder(value.Length + 2);
            literal.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\':
                        literal.Append(@"\\");
                        break;
                    case '"':
                        literal.Append("\\\"");
                        break;
                    case '\n':
                        literal.Append(@"\n");
                        break;
                    case '\r':
                        literal.Append(@"\r");
                        break;
                    case '\t':
                        literal.Append(@"\t");
                        break;
                    default:
                        if (c < ' ' || c == '\u007f')
                        {
                            return null;
                        }
                        literal.Append(c);
                        break;
                }
            }
            return literal.Append('"').ToString();
        }

        /// <summary>
        /// The MAXScript batch for <paramref name="plan"/>. <paramref name="temporaryPrefix"/> starts the temporary Max
        /// layer names (the Max side passes something unique, e.g. with a GUID in it).
        /// </summary>
        public static PhasingFixBatch Build(PhasingFixPlan plan, string temporaryPrefix)
        {
            var batch = new PhasingFixBatch();
            var prefix = string.IsNullOrEmpty(temporaryPrefix) ? "__phasing_fix_" : temporaryPrefix;

            var nodeHandles = new List<string>();
            var nodeOld = new List<string>();
            var nodeNew = new List<string>();
            foreach (var rename in plan.NodeRenames)
            {
                var handle = Handle(rename.Key);
                var oldName = Quote(rename.OldName);
                var newName = Quote(rename.NewName);
                if (handle == null || oldName == null || newName == null)
                {
                    batch.Refused.Add(rename.OldName + " is not renamed to " + rename.NewName + ": " + Unwritable(handle == null));
                    continue;
                }
                batch.NodeRenames.Add(rename);
                nodeHandles.Add(handle);
                nodeOld.Add(oldName);
                nodeNew.Add(newName);
            }

            var moveHandles = new List<string>();
            var moveParents = new List<string>();
            foreach (var move in plan.NodeMoves)
            {
                var handle = Handle(move.Key);
                var parent = Handle(move.ToParentKey);
                if (handle == null || parent == null)
                {
                    batch.Refused.Add(move.Name + " is not moved into " + move.ToParentName + ": " + Unwritable(true));
                    continue;
                }
                batch.NodeMoves.Add(move);
                moveHandles.Add(handle);
                moveParents.Add(parent);
            }

            // Every Max layer the batch touches, found once by its current name before anything is renamed.
            var layerNames = new List<string>();
            var layerIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            Func<string, int> indexOf = name =>
            {
                int index;
                if (!layerIndex.TryGetValue(name, out index))
                {
                    layerNames.Add(Quote(name));
                    index = layerNames.Count;
                    layerIndex[name] = index;
                }
                return index;
            };

            var layerMoveChild = new List<string>();
            var layerMoveParent = new List<string>();
            foreach (var move in plan.MaxLayerMoves)
            {
                if (Quote(move.Name) == null || Quote(move.NewParentName) == null)
                {
                    batch.Refused.Add("the Max layer " + move.Name + " is not moved under " + move.NewParentName + ": " + Unwritable(false));
                    continue;
                }
                batch.MaxLayerMoves.Add(move);
                layerMoveChild.Add(Number(indexOf(move.Name)));
                layerMoveParent.Add(Number(indexOf(move.NewParentName)));
            }

            var layerRenameIndex = new List<string>();
            var layerRenameNew = new List<string>();
            var layerRenameTemp = new List<string>();
            foreach (var rename in plan.MaxLayerRenames)
            {
                var newName = Quote(rename.NewName);
                if (Quote(rename.OldName) == null || newName == null)
                {
                    batch.Refused.Add("the Max layer " + rename.OldName + " is not renamed to " + rename.NewName + ": " + Unwritable(false));
                    continue;
                }
                var temporary = prefix + (batch.MaxLayerRenames.Count + 1).ToString(CultureInfo.InvariantCulture);
                batch.MaxLayerRenames.Add(rename);
                batch.TemporaryNames.Add(temporary);
                layerRenameIndex.Add(Number(indexOf(rename.OldName)));
                layerRenameNew.Add(newName);
                layerRenameTemp.Add(Quote(temporary));
            }

            var s = new StringBuilder();
            s.Append("-- UrbanCGI exporter: ").Append(UndoLabel).Append(" (").Append(Number(batch.NodeRenames.Count)).Append(" renames, ")
                .Append(Number(batch.NodeMoves.Count)).Append(" moves, ").Append(Number(batch.MaxLayerRenames.Count)).Append(" Max layer renames, ")
                .Append(Number(batch.MaxLayerMoves.Count)).Append(" Max layer moves)").Append(Nl);
            s.Append("(").Append(Nl);
            s.Append("\tlocal fixLog = stringStream \"\"").Append(Nl);
            s.Append("\tlocal fixNode, fixParent, fixLayer, fixOther, fixDetail").Append(Nl);
            s.Append("\tlocal fixRenamed = 0, fixMoved = 0, fixLayersRenamed = 0, fixLayersMoved = 0").Append(Nl);
            WriteArray(s, "fixNodeHandles", nodeHandles);
            WriteArray(s, "fixNodeOld", nodeOld);
            WriteArray(s, "fixNodeNew", nodeNew);
            WriteArray(s, "fixMoveHandles", moveHandles);
            WriteArray(s, "fixMoveParents", moveParents);
            WriteArray(s, "fixLayerNames", layerNames);
            WriteArray(s, "fixLayerMoveChild", layerMoveChild);
            WriteArray(s, "fixLayerMoveParent", layerMoveParent);
            WriteArray(s, "fixLayerRenameIndex", layerRenameIndex);
            WriteArray(s, "fixLayerRenameNew", layerRenameNew);
            WriteArray(s, "fixLayerRenameTemp", layerRenameTemp);
            s.Append("\tlocal fixLayers = for n in fixLayerNames collect (LayerManager.getLayerFromName n)").Append(Nl);
            s.Append("\tlocal fixStepped = for i = 1 to fixLayerRenameIndex.count collect false").Append(Nl);
            s.Append("\ttry").Append(Nl);
            s.Append("\t(").Append(Nl);
            s.Append("\t\tundo ").Append(Quote(UndoLabel)).Append(" on").Append(Nl);
            s.Append("\t\t(").Append(Nl);
            s.Append("\t\t\twith redraw off").Append(Nl);
            s.Append("\t\t\t(").Append(Nl);

            // Node renames, by handle; a node renamed since the list was made is left alone.
            s.Append(@"
				for i = 1 to fixNodeHandles.count do
				(
					try
					(
						fixNode = maxOps.getNodeByHandle fixNodeHandles[i]
						if (fixNode == undefined) then
						(
							format ""FAIL\tnode\t%\tgone\n"" i to:fixLog
						)
						else if (fixNode.name != fixNodeOld[i]) then
						(
							format ""FAIL\tnode\t%\tchanged\n"" i to:fixLog
						)
						else
						(
							fixNode.name = fixNodeNew[i]
							if (fixNode.name == fixNodeNew[i]) then (fixRenamed += 1) else (format ""FAIL\tnode\t%\trefused\n"" i to:fixLog)
						)
					)
					catch
					(
" + CatchLine("node") + @"
					)
				)
");

            // Layer nodes into the group of their phase: attached when the group is a 3ds Max group head, linked otherwise.
            s.Append(@"
				for i = 1 to fixMoveHandles.count do
				(
					try
					(
						fixNode = maxOps.getNodeByHandle fixMoveHandles[i]
						fixParent = maxOps.getNodeByHandle fixMoveParents[i]
						if (fixNode == undefined or fixParent == undefined) then
						(
							format ""FAIL\tmove\t%\tgone\n"" i to:fixLog
						)
						else
						(
							if (isGroupHead fixParent) then
							(
								if (isGroupMember fixNode) do (detachNodesFromGroup #(fixNode))
								attachNodesToGroup #(fixNode) fixParent
							)
							else
							(
								if ((isGroupMember fixNode) and not (isGroupMember fixParent)) do (detachNodesFromGroup #(fixNode))
								fixNode.parent = fixParent
							)
							if (fixNode.parent == fixParent) then (fixMoved += 1) else (format ""FAIL\tmove\t%\trefused\n"" i to:fixLog)
						)
					)
					catch
					(
" + CatchLine("move") + @"
					)
				)
");

            // Max layers under their group's Max layer.
            s.Append(@"
				for i = 1 to fixLayerMoveChild.count do
				(
					try
					(
						fixLayer = fixLayers[fixLayerMoveChild[i]]
						fixParent = fixLayers[fixLayerMoveParent[i]]
						if (fixLayer == undefined or fixParent == undefined) then
						(
							format ""FAIL\tlayermove\t%\tgone\n"" i to:fixLog
						)
						else
						(
							fixLayer.setParent fixParent
							fixOther = fixLayer.getParent()
							if (fixOther != undefined and fixOther.name == fixParent.name) then (fixLayersMoved += 1) else (format ""FAIL\tlayermove\t%\trefused\n"" i to:fixLog)
						)
					)
					catch
					(
" + CatchLine("layermove") + @"
					)
				)
");

            // Max layer renames, step 1: every layer that changes takes a temporary name.
            s.Append(@"
				for i = 1 to fixLayerRenameIndex.count do
				(
					try
					(
						fixLayer = fixLayers[fixLayerRenameIndex[i]]
						if (fixLayer == undefined) then
						(
							format ""FAIL\tlayer\t%\tgone\n"" i to:fixLog
						)
						else
						(
							fixLayer.setName fixLayerRenameTemp[i]
							if (fixLayer.name == fixLayerRenameTemp[i]) then (fixStepped[i] = true) else (format ""FAIL\tlayer\t%\trefused\n"" i to:fixLog)
						)
					)
					catch
					(
" + CatchLine("layer") + @"
					)
				)
");

            // Step 2: the new names; a layer whose new name is refused goes back to its old one.
            s.Append(@"
				for i = 1 to fixLayerRenameIndex.count where fixStepped[i] do
				(
					try
					(
						fixLayer = fixLayers[fixLayerRenameIndex[i]]
						fixLayer.setName fixLayerRenameNew[i]
						if (fixLayer.name == fixLayerRenameNew[i]) then
						(
							fixLayersRenamed += 1
						)
						else
						(
							fixLayer.setName fixLayerNames[fixLayerRenameIndex[i]]
							if (fixLayer.name == fixLayerNames[fixLayerRenameIndex[i]]) then (format ""FAIL\tlayer\t%\ttaken\n"" i to:fixLog) else (format ""FAIL\tlayer\t%\tstuck\n"" i to:fixLog)
						)
					)
					catch
					(
" + CatchLine("layer") + @"
					)
				)
");

            s.Append("\t\t\t)").Append(Nl);
            s.Append("\t\t)").Append(Nl);
            s.Append("\t)").Append(Nl);
            s.Append("\tcatch").Append(Nl);
            s.Append("\t(").Append(Nl);
            s.Append("\t\tfixDetail = (getCurrentException()) as string").Append(Nl);
            s.Append("\t\t").Append(CleanDetail()).Append(Nl);
            s.Append("\t\tformat \"ABORT\\t%\\n\" fixDetail to:fixLog").Append(Nl);
            s.Append("\t)").Append(Nl);
            s.Append("\tformat \"DONE\\t%\\t%\\t%\\t%\\n\" fixRenamed fixMoved fixLayersRenamed fixLayersMoved to:fixLog").Append(Nl);
            s.Append("\tformat \"").Append(EndMarker).Append("\\n\" to:fixLog").Append(Nl);
            s.Append("\tfixLog as string").Append(Nl);
            s.Append(")").Append(Nl);
            batch.Script = s.ToString().Replace("\r\n", "\n").Replace("\n", Nl);
            return batch;
        }

        /// <summary>Reads what the batch sent back. Anything unexpected is tolerated: a missing marker only clears <see cref="PhasingFixResult.Completed"/>.</summary>
        public static PhasingFixResult ReadResult(PhasingFixBatch batch, string output)
        {
            var result = new PhasingFixResult { Batch = batch };
            result.Failures.AddRange(batch.Refused);
            var text = output ?? string.Empty;
            if (!HasMarker(text) && text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                // Some 3ds Max versions may hand back the printed form of the string: quoted and escaped.
                text = Unescape(text.Substring(1, text.Length - 2));
            }
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line == EndMarker)
                {
                    result.Completed = true;
                    continue;
                }
                var parts = line.Split('\t');
                switch (parts[0])
                {
                    case "DONE":
                        if (parts.Length >= 5)
                        {
                            result.NodesRenamed = ParseCount(parts[1]);
                            result.NodesMoved = ParseCount(parts[2]);
                            result.MaxLayersRenamed = ParseCount(parts[3]);
                            result.MaxLayersMoved = ParseCount(parts[4]);
                        }
                        break;
                    case "ABORT":
                        result.Aborted = parts.Length > 1 ? string.Join(" ", parts.Skip(1).ToArray()).Trim() : "unknown error";
                        break;
                    case "FAIL":
                        if (parts.Length >= 4)
                        {
                            var detail = parts.Length > 4 ? string.Join(" ", parts.Skip(4).ToArray()).Trim() : null;
                            result.Failures.Add(DescribeFailure(batch, parts[1], ParseCount(parts[2]) - 1, parts[3], detail));
                        }
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// The Max layer changes of a batch with problems that the scene (<paramref name="maxLayersNow"/>) still does
        /// not show, as Check rows. A plan cannot find them again: it pairs a Max layer with a node by the node's
        /// current name, and the node may already have its new one. Empty when the batch had no problems.
        /// </summary>
        public static List<PhasingFixRow> UnfinishedMaxLayerRows(PhasingFixResult result, IEnumerable<PhasingMaxLayer> maxLayersNow)
        {
            var rows = new List<PhasingFixRow>();
            if (result == null || result.Batch == null || !result.HasProblems)
            {
                return rows;
            }
            var batch = result.Batch;
            var now = (maxLayersNow ?? Enumerable.Empty<PhasingMaxLayer>()).Where(l => l != null && l.Name != null).ToList();
            Func<string, PhasingMaxLayer> find = name => name == null ? null : now.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            // An old name another rename hands on (a swap) may be there after a rename that worked; so a swap that
            // never started reads as done, which only the counts below can rule out.
            var handedOn = new HashSet<string>(batch.MaxLayerRenames.Select(r => r.NewName), StringComparer.OrdinalIgnoreCase);
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool allRenamed = result.Completed && result.MaxLayersRenamed == batch.MaxLayerRenames.Count;
            bool allMoved = result.Completed && result.MaxLayersMoved == batch.MaxLayerMoves.Count;
            for (int i = 0; i < batch.MaxLayerRenames.Count && !allRenamed; i++)
            {
                var rename = batch.MaxLayerRenames[i];
                var temporary = find(i < batch.TemporaryNames.Count ? batch.TemporaryNames[i] : null);
                var old = handedOn.Contains(rename.OldName) ? null : find(rename.OldName);
                if (temporary == null && old == null && now.Any(l => l.Name == rename.NewName))
                {
                    current[rename.OldName] = rename.NewName;
                    continue;
                }
                var left = temporary ?? old;
                if (left == null)
                {
                    // No longer in the scene, or renamed by hand since.
                    continue;
                }
                current[rename.OldName] = left.Name;
                rows.Add(UnfinishedRow(rename.Item, left.Name, rename.NewName, temporary != null
                    ? "the Max layer " + rename.OldName + " was left as " + left.Name + " by the fix; rename it " + rename.NewName + " by hand"
                    : "the Max layer " + left.Name + " still has its old name after the fix; rename it " + rename.NewName + " by hand"));
            }
            if (allRenamed)
            {
                foreach (var rename in batch.MaxLayerRenames)
                {
                    current[rename.OldName] = rename.NewName;
                }
            }
            foreach (var move in allMoved ? Enumerable.Empty<PhasingMaxLayerMove>() : batch.MaxLayerMoves)
            {
                string childName, parentName;
                var child = find(current.TryGetValue(move.Name, out childName) ? childName : move.Name);
                var parent = find(current.TryGetValue(move.NewParentName, out parentName) ? parentName : move.NewParentName);
                if (child == null || parent == null || string.Equals(child.ParentName, parent.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                rows.Add(UnfinishedRow(PhasingFixItem.Layer, child.Name, child.Name,
                    "the Max layer " + child.Name + " is not under " + parent.Name + " after the fix; move it there by hand"));
            }
            return rows;
        }

        private static PhasingFixRow UnfinishedRow(PhasingFixItem item, string current, string corrected, string note)
        {
            return new PhasingFixRow
            {
                Status = PhasingFixStatus.Check,
                Item = item,
                LayerItSitsIn = string.Empty,
                CurrentName = current,
                CorrectedName = corrected,
                WhatChanged = note
            };
        }

        /// <summary>The text on one line (line ends and tabs as spaces), cut to <paramref name="max"/> characters.</summary>
        public static string OneLine(string text, int max)
        {
            var flat = (text ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max) + "\u2026";
        }

        /// <summary>Everything the plan would do, as text: two plans with the same fingerprint make the same changes and list.</summary>
        public static string Fingerprint(PhasingFixPlan plan)
        {
            var text = new StringBuilder(plan.ToCsv());
            foreach (var rename in plan.NodeRenames)
            {
                text.Append("R\t").Append(Number(rename.Key)).Append('\t').Append(rename.OldName).Append('\t').Append(rename.NewName).Append('\n');
            }
            foreach (var move in plan.NodeMoves)
            {
                text.Append("M\t").Append(Number(move.Key)).Append('\t').Append(Number(move.FromParentKey)).Append('\t').Append(Number(move.ToParentKey)).Append('\n');
            }
            foreach (var rename in plan.MaxLayerRenames)
            {
                text.Append("LR\t").Append(rename.OldName).Append('\t').Append(rename.NewName).Append('\n');
            }
            foreach (var move in plan.MaxLayerMoves)
            {
                text.Append("LM\t").Append(move.Name).Append('\t').Append(move.NewParentName).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>The names the fix would correct: its Changed rows (or, failing those, its operations).</summary>
        public static int CorrectableCount(PhasingFixPlan plan)
        {
            if (plan == null || !plan.HasChanges)
            {
                return 0;
            }
            var rows = plan.Rows.Count(r => r.Status == PhasingFixStatus.Changed);
            return rows > 0 ? rows : plan.NodeRenames.Count + plan.NodeMoves.Count + plan.MaxLayerRenames.Count + plan.MaxLayerMoves.Count;
        }

        /// <summary>The export log's pointer to the fixer, or null when there is nothing to correct.</summary>
        public static string ExportWarning(PhasingFixPlan plan)
        {
            var count = CorrectableCount(plan);
            if (count == 0)
            {
                return null;
            }
            // A Changed row is a layer or object whose name or place changes (a moved layer may keep its name).
            return Count(count, "phasing layer or object can", "phasing layers and objects can")
                + " be corrected automatically \u2014 use Fix phasing names\u2026 on the exporter window";
        }

        internal static string Count(int count, string one, string many)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);
        }

        private static string DescribeFailure(PhasingFixBatch batch, string kind, int index, string code, string detail)
        {
            var error = "3ds Max reported an error" + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
            switch (kind)
            {
                case "node":
                    if (index >= 0 && index < batch.NodeRenames.Count)
                    {
                        var rename = batch.NodeRenames[index];
                        switch (code)
                        {
                            case "gone":
                                return rename.OldName + " is no longer in the scene";
                            case "changed":
                                return rename.OldName + " has been renamed since the list was made; left as it is";
                            case "refused":
                                return rename.OldName + " could not be renamed to " + rename.NewName;
                            default:
                                return rename.OldName + " was not renamed: " + error;
                        }
                    }
                    break;
                case "move":
                    if (index >= 0 && index < batch.NodeMoves.Count)
                    {
                        var move = batch.NodeMoves[index];
                        switch (code)
                        {
                            case "gone":
                                return move.Name + " was not moved into " + move.ToParentName + ": one of them is no longer in the scene";
                            case "refused":
                                return move.Name + " could not be moved into " + move.ToParentName;
                            default:
                                return move.Name + " was not moved into " + move.ToParentName + ": " + error;
                        }
                    }
                    break;
                case "layermove":
                    if (index >= 0 && index < batch.MaxLayerMoves.Count)
                    {
                        var move = batch.MaxLayerMoves[index];
                        switch (code)
                        {
                            case "gone":
                                return "the Max layer " + move.Name + " was not moved under " + move.NewParentName + ": one of them is no longer in the scene";
                            case "refused":
                                return "the Max layer " + move.Name + " could not be moved under " + move.NewParentName;
                            default:
                                return "the Max layer " + move.Name + " was not moved under " + move.NewParentName + ": " + error;
                        }
                    }
                    break;
                case "layer":
                    if (index >= 0 && index < batch.MaxLayerRenames.Count)
                    {
                        var rename = batch.MaxLayerRenames[index];
                        switch (code)
                        {
                            case "gone":
                                return "the Max layer " + rename.OldName + " is no longer in the scene";
                            case "refused":
                                return "the Max layer " + rename.OldName + " could not be renamed; it keeps its name";
                            case "taken":
                                return "the Max layer " + rename.OldName + " could not become " + rename.NewName + ": that name is taken; it keeps its name";
                            case "stuck":
                                return "the Max layer " + rename.OldName + " could not become " + rename.NewName + " and is left as " + batch.TemporaryNames[index] + " - rename it by hand";
                            default:
                                return "the Max layer " + rename.OldName + " was not renamed to " + rename.NewName + ": " + error;
                        }
                    }
                    break;
            }
            return "an item of the fix was not carried out (" + kind + " " + (index + 1).ToString(CultureInfo.InvariantCulture) + ", " + code + ")"
                + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
        }

        private static string CatchLine(string kind)
        {
            return "\t\t\t\t\t\tfixDetail = (getCurrentException()) as string\r\n"
                + "\t\t\t\t\t\t" + CleanDetail() + "\r\n"
                + "\t\t\t\t\t\tformat \"FAIL\\t" + kind + "\\t%\\terror\\t%\\n\" i fixDetail to:fixLog";
        }

        // The error text on one line, so it cannot break the report's line format.
        private static string CleanDetail()
        {
            return "fixDetail = substituteString (substituteString (substituteString fixDetail \"\\r\" \" \") \"\\n\" \" \") \"\\t\" \" \"";
        }

        private static void WriteArray(StringBuilder s, string name, List<string> items)
        {
            s.Append("\tlocal ").Append(name).Append(" = #(");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    s.Append(i % 8 == 0 ? "," + Nl + "\t\t" : ", ");
                }
                s.Append(items[i]);
            }
            s.Append(")").Append(Nl);
        }

        // A node handle as a MAXScript integer literal, or null when it is out of MAXScript's integer range.
        private static string Handle(long key)
        {
            return key >= 0 && key <= int.MaxValue ? Number(key) : null;
        }

        private static string Unwritable(bool handle)
        {
            return handle ? "its node handle cannot be passed to MAXScript" : "the name holds a control character that cannot be passed to MAXScript";
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool HasMarker(string text)
        {
            return text.Split('\n').Any(l => l.TrimEnd('\r') == EndMarker);
        }

        private static int ParseCount(string text)
        {
            int value;
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        // MAXScript's string escapes, read back.
        private static string Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '\\' || i + 1 == text.Length)
                {
                    result.Append(c);
                    continue;
                }
                var next = text[++i];
                switch (next)
                {
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    default:
                        result.Append(next);
                        break;
                }
            }
            return result.ToString();
        }
    }
}
