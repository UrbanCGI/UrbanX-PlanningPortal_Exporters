using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>A layer plan written out as one MAXScript batch, and what each of its entries is called, for the report.</summary>
    public sealed class PlannerLayerBatch
    {
        public string Script;
        public PlannerLayerPlan Plan;
        /// <summary>The temporary name of each layer rename, by layer entry index.</summary>
        public readonly Dictionary<int, string> TemporaryNames = new Dictionary<int, string>();
        /// <summary>Entries left out because MAXScript cannot be given them safely, as sentences for the modeller.</summary>
        public readonly List<string> Refused = new List<string>();
        public int LayerRenames;
        public int LayerCreates;
        public int LayerMoves;
        public int HelperRenames;
        public int HelperCreates;
        public int HelperPuts;
        public int HelperMoves;
        public int Stamps;
    }

    /// <summary>What came back from running a layer batch.</summary>
    public sealed class PlannerLayerResult
    {
        /// <summary>The batch's end marker came back: the script ran to its last line.</summary>
        public bool Completed;
        /// <summary>The error that stopped the batch part-way, or null.</summary>
        public string Aborted;
        public int LayersRenamed;
        public int LayersCreated;
        public int LayersMoved;
        public int HelpersRenamed;
        public int HelpersCreated;
        public int HelpersMoved;
        public int HelpersPutOnLayer;
        public int IdsStamped;
        /// <summary>Changes that were not made, as sentences for the modeller.</summary>
        public readonly List<string> Failures = new List<string>();

        public bool HasProblems
        {
            get { return !Completed || Aborted != null || Failures.Count > 0; }
        }

        /// <summary>
        /// The outcome in a few plain lines: what was done, what was not, and what the scene still needs when it is read
        /// again (<paramref name="after"/>, the plan made from the scene after the batch; may be null).
        /// </summary>
        public string Describe(PlannerLayerPlan after, bool held)
        {
            var lines = new List<string>();
            if (!Completed)
            {
                lines.Add("3ds Max did not report back, so it is not known how far the build got.");
            }
            if (Aborted != null)
            {
                lines.Add("The build stopped part-way, so some changes may be missing: " + Aborted);
            }
            var done = new List<string>();
            if (LayersCreated > 0) done.Add(PhasingFixScript.Count(LayersCreated, "layer created", "layers created"));
            if (HelpersCreated > 0) done.Add(PhasingFixScript.Count(HelpersCreated, "helper created", "helpers created"));
            if (LayersRenamed > 0) done.Add(PhasingFixScript.Count(LayersRenamed, "layer renamed", "layers renamed"));
            if (HelpersRenamed > 0) done.Add(PhasingFixScript.Count(HelpersRenamed, "helper renamed", "helpers renamed"));
            if (LayersMoved > 0) done.Add(PhasingFixScript.Count(LayersMoved, "layer moved", "layers moved"));
            if (HelpersMoved > 0) done.Add(PhasingFixScript.Count(HelpersMoved, "helper moved", "helpers moved"));
            if (HelpersPutOnLayer > 0) done.Add(PhasingFixScript.Count(HelpersPutOnLayer, "helper put on its layer", "helpers put on their layers"));
            if (IdsStamped > 0) done.Add(PhasingFixScript.Count(IdsStamped, "helper given its Planner ID", "helpers given their Planner IDs"));
            lines.Add(done.Count == 0 ? "Nothing was changed." : "Done: " + PlannerScriptText.JoinWithAnd(done) + ".");
            if (Failures.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(Failures.Count, "change was not made:", "changes were not made:"));
                lines.AddRange(Failures.Select(f => "- " + f));
            }
            if (after != null)
            {
                lines.Add(after.HasChanges
                    ? "Read again from the scene: " + after.Summary() + " Build again to finish, or see the list."
                    : "Read again from the scene: " + after.Summary());
            }
            if (held)
            {
                // The build is one undo record, but whether 3ds Max undoes layer changes with the nodes' is not known: Fetch is the sure way back.
                lines.Add("Edit > Fetch puts the scene back as it was before the build.");
            }
            return string.Join("\r\n", lines.ToArray());
        }
    }

    /// <summary>
    /// Writes a <see cref="PlannerLayerPlan"/> as one MAXScript batch and reads back what it reports. Pure text, so it is
    /// tested without 3ds Max; the Max side only runs the script.
    ///
    /// Before anything changes, the batch finds every existing layer by its current name and every existing helper by its
    /// handle (a helper renamed since the list was made is left alone). Inside one undo record it then renames layers in
    /// two steps through temporary names (so a new name never meets an old one, and names can swap), creates the new
    /// layers under their parents, moves existing layers, renames helpers, creates the new helpers (a Dummy at the origin
    /// on its own layer, linked under its parent's helper, with its Planner ID), puts existing helpers onto their layers,
    /// links existing helpers under their parents (attached when the parent is a 3ds Max group head) and writes the
    /// Planner IDs (user property planner_id). Each item reports its own failure; the last lines are the counts and an end
    /// marker, so a batch that stopped early is recognised. Nothing is deleted.
    /// </summary>
    public static class PlannerLayerScript
    {
        public const string EndMarker = "PLANNERBUILD-END";
        public const string UndoLabel = "Build Planner layers";
        /// <summary>The size of a new helper Dummy's box, in scene units.</summary>
        public const string DummyBoxSize = "[1,1,1]";

        private const string Nl = "\r\n";

        /// <summary>
        /// The Planner ID as the user property stores it: spaces written %20, as the exporter's own user properties are
        /// (Tools.SetStringProperty), so the exporter's reader gives the ID back.
        /// </summary>
        public static string StoredId(string plannerId)
        {
            return (plannerId ?? string.Empty).Replace(" ", "%20");
        }

        public static PlannerLayerBatch Build(PlannerLayerPlan plan, string temporaryPrefix)
        {
            var batch = new PlannerLayerBatch { Plan = plan };
            var prefix = string.IsNullOrEmpty(temporaryPrefix) ? "__planner_build_" : temporaryPrefix;
            var layers = plan.BuildLayers;
            var helpers = plan.BuildHelpers;

            // Entries MAXScript cannot be given: left out, with what depends on them.
            var badLayers = new HashSet<int>();
            foreach (var layer in layers)
            {
                if (PhasingFixScript.Quote(layer.FinalName) == null || (layer.CurrentName != null && PhasingFixScript.Quote(layer.CurrentName) == null))
                {
                    badLayers.Add(layer.Index);
                    batch.Refused.Add("the layer " + (layer.CurrentName ?? layer.FinalName) + " is left out: its name holds a character that cannot be passed to MAXScript");
                }
            }
            var badHelpers = new HashSet<int>();
            foreach (var helper in helpers)
            {
                bool badName = PhasingFixScript.Quote(helper.FinalName) == null || (helper.CurrentName != null && PhasingFixScript.Quote(helper.CurrentName) == null)
                    || (helper.StampId != null && PhasingFixScript.Quote(StoredId(helper.StampId)) == null);
                bool badHandle = helper.Key.HasValue && (helper.Key.Value <= 0 || helper.Key.Value > int.MaxValue);
                if (badName || badHandle)
                {
                    badHelpers.Add(helper.Index);
                    batch.Refused.Add("the helper " + (helper.CurrentName ?? helper.FinalName) + " is left out: "
                        + (badHandle ? "its node handle cannot be passed to MAXScript" : "its name or Planner ID holds a character that cannot be passed to MAXScript"));
                }
            }

            var layerOld = new List<string>();
            var layerNew = new List<string>();
            var layerParent = new List<string>();
            foreach (var layer in layers)
            {
                bool bad = badLayers.Contains(layer.Index);
                layerOld.Add(bad || layer.CurrentName == null ? "\"\"" : PhasingFixScript.Quote(layer.CurrentName));
                layerNew.Add(bad ? "\"\"" : PhasingFixScript.Quote(layer.FinalName));
                layerParent.Add(Number(layer.ParentIndex.HasValue && !badLayers.Contains(layer.ParentIndex.Value) ? layer.ParentIndex.Value + 1 : 0));
            }
            var renameIdx = new List<string>();
            var renameTemp = new List<string>();
            var createLayerIdx = new List<string>();
            var moveLayerIdx = new List<string>();
            foreach (var layer in layers.Where(l => !badLayers.Contains(l.Index)))
            {
                if (layer.Rename)
                {
                    var temporary = prefix + Number(batch.TemporaryNames.Count + 1);
                    batch.TemporaryNames[layer.Index] = temporary;
                    renameIdx.Add(Number(layer.Index + 1));
                    renameTemp.Add(PhasingFixScript.Quote(temporary));
                    batch.LayerRenames++;
                }
                if (layer.Create)
                {
                    createLayerIdx.Add(Number(layer.Index + 1));
                    batch.LayerCreates++;
                }
                else if (layer.Move && layer.ParentIndex.HasValue && !badLayers.Contains(layer.ParentIndex.Value))
                {
                    moveLayerIdx.Add(Number(layer.Index + 1));
                    batch.LayerMoves++;
                }
            }

            var nodeHandle = new List<string>();
            var nodeOld = new List<string>();
            var nodeNew = new List<string>();
            var nodeLayer = new List<string>();
            var nodeParent = new List<string>();
            var nodeId = new List<string>();
            foreach (var helper in helpers)
            {
                bool bad = badHelpers.Contains(helper.Index);
                nodeHandle.Add(Number(bad || !helper.Key.HasValue ? 0 : helper.Key.Value));
                nodeOld.Add(bad || helper.CurrentName == null ? "\"\"" : PhasingFixScript.Quote(helper.CurrentName));
                nodeNew.Add(bad ? "\"\"" : PhasingFixScript.Quote(helper.FinalName));
                nodeLayer.Add(Number(badLayers.Contains(helper.LayerIndex) ? 0 : helper.LayerIndex + 1));
                nodeParent.Add(Number(helper.ParentIndex.HasValue && !badHelpers.Contains(helper.ParentIndex.Value) ? helper.ParentIndex.Value + 1 : 0));
                nodeId.Add(bad || helper.StampId == null ? "\"\"" : PhasingFixScript.Quote(StoredId(helper.StampId)));
            }
            var renameNodeIdx = new List<string>();
            var createNodeIdx = new List<string>();
            var putIdx = new List<string>();
            var moveNodeIdx = new List<string>();
            var stampIdx = new List<string>();
            foreach (var helper in helpers.Where(h => !badHelpers.Contains(h.Index)))
            {
                var index = Number(helper.Index + 1);
                if (helper.Create)
                {
                    createNodeIdx.Add(index);
                    batch.HelperCreates++;
                    if (helper.StampId != null)
                    {
                        batch.Stamps++;
                    }
                    continue;
                }
                if (helper.Rename)
                {
                    renameNodeIdx.Add(index);
                    batch.HelperRenames++;
                }
                if (helper.PutOnLayer && !badLayers.Contains(helper.LayerIndex))
                {
                    putIdx.Add(index);
                    batch.HelperPuts++;
                }
                if (helper.Move && helper.ParentIndex.HasValue && !badHelpers.Contains(helper.ParentIndex.Value))
                {
                    moveNodeIdx.Add(index);
                    batch.HelperMoves++;
                }
                if (helper.StampId != null)
                {
                    stampIdx.Add(index);
                    batch.Stamps++;
                }
            }

            var s = new StringBuilder();
            s.Append("-- UrbanCGI exporter: ").Append(UndoLabel).Append(" (").Append(Number(batch.LayerCreates)).Append(" layers to create, ")
                .Append(Number(batch.LayerRenames)).Append(" to rename, ").Append(Number(batch.LayerMoves)).Append(" to move; ")
                .Append(Number(batch.HelperCreates)).Append(" helpers to create, ").Append(Number(batch.HelperRenames)).Append(" to rename, ")
                .Append(Number(batch.HelperMoves)).Append(" to move, ").Append(Number(batch.HelperPuts)).Append(" to put on their layers; ")
                .Append(Number(batch.Stamps)).Append(" Planner IDs)").Append(Nl);
            s.Append("(").Append(Nl);
            s.Append("\tlocal bLog = stringStream \"\"").Append(Nl);
            s.Append("\tlocal bLayer, bNode, bParent, bOther, bDetail").Append(Nl);
            s.Append("\tlocal bLayersRenamed = 0, bLayersCreated = 0, bLayersMoved = 0, bHelpersRenamed = 0, bHelpersCreated = 0, bHelpersMoved = 0, bHelpersPut = 0, bIdsStamped = 0").Append(Nl);
            WriteArray(s, "bLayerOld", layerOld);
            WriteArray(s, "bLayerNew", layerNew);
            WriteArray(s, "bLayerParent", layerParent);
            WriteArray(s, "bLayerRenameIdx", renameIdx);
            WriteArray(s, "bLayerRenameTemp", renameTemp);
            WriteArray(s, "bLayerCreateIdx", createLayerIdx);
            WriteArray(s, "bLayerMoveIdx", moveLayerIdx);
            WriteArray(s, "bNodeHandle", nodeHandle);
            WriteArray(s, "bNodeOld", nodeOld);
            WriteArray(s, "bNodeNew", nodeNew);
            WriteArray(s, "bNodeLayer", nodeLayer);
            WriteArray(s, "bNodeParent", nodeParent);
            WriteArray(s, "bNodeId", nodeId);
            WriteArray(s, "bNodeRenameIdx", renameNodeIdx);
            WriteArray(s, "bNodeCreateIdx", createNodeIdx);
            WriteArray(s, "bNodePutIdx", putIdx);
            WriteArray(s, "bNodeMoveIdx", moveNodeIdx);
            WriteArray(s, "bNodeStampIdx", stampIdx);
            s.Append("\tlocal bStepped = for i = 1 to bLayerOld.count collect false").Append(Nl);

            // Everything found before anything changes: layers by their current names, helpers by handle (and name).
            s.Append(@"
	local bLayers = for i = 1 to bLayerOld.count collect (if bLayerOld[i] == """" then undefined else (LayerManager.getLayerFromName bLayerOld[i]))
	for i = 1 to bLayers.count where bLayerOld[i] != """" and bLayers[i] == undefined do (format ""FAIL\tlayerfind\t%\tgone\n"" i to:bLog)
	local bNodes = #()
	for i = 1 to bNodeHandle.count do
	(
		bNode = undefined
		if bNodeHandle[i] != 0 do
		(
			bNode = maxOps.getNodeByHandle bNodeHandle[i]
			if (bNode == undefined) then
			(
				format ""FAIL\tfind\t%\tgone\n"" i to:bLog
			)
			else if (bNode.name != bNodeOld[i]) then
			(
				format ""FAIL\tfind\t%\tchanged\n"" i to:bLog
				bNode = undefined
			)
		)
		append bNodes bNode
	)
");
            s.Append("\ttry").Append(Nl);
            s.Append("\t(").Append(Nl);
            s.Append("\t\tundo ").Append(PhasingFixScript.Quote(UndoLabel)).Append(" on").Append(Nl);
            s.Append("\t\t(").Append(Nl);
            s.Append("\t\t\twith redraw off").Append(Nl);
            s.Append("\t\t\t(").Append(Nl);

            // Layer renames, step 1: every layer that changes takes a temporary name.
            s.Append(@"
				for k = 1 to bLayerRenameIdx.count do
				(
					local i = bLayerRenameIdx[k]
					try
					(
						bLayer = bLayers[i]
						if (bLayer != undefined) do
						(
							bLayer.setName bLayerRenameTemp[k]
							if (bLayer.name == bLayerRenameTemp[k]) then (bStepped[i] = true) else (format ""FAIL\tlayer\t%\trefused\n"" i to:bLog)
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
				for k = 1 to bLayerRenameIdx.count do
				(
					local i = bLayerRenameIdx[k]
					if bStepped[i] do
					(
						try
						(
							bLayer = bLayers[i]
							bLayer.setName bLayerNew[i]
							if (bLayer.name == bLayerNew[i]) then
							(
								bLayersRenamed += 1
							)
							else
							(
								bLayer.setName bLayerOld[i]
								if (bLayer.name == bLayerOld[i]) then (format ""FAIL\tlayer\t%\ttaken\n"" i to:bLog) else (format ""FAIL\tlayer\t%\tstuck\n"" i to:bLog)
							)
						)
						catch
						(
" + CatchLine("layer") + @"
						)
					)
				)
");
            // New layers, parents first (the list is in outline order).
            s.Append(@"
				for k = 1 to bLayerCreateIdx.count do
				(
					local i = bLayerCreateIdx[k]
					try
					(
						bLayer = LayerManager.newLayerFromName bLayerNew[i]
						if (bLayer == undefined) then
						(
							format ""FAIL\tlayercreate\t%\ttaken\n"" i to:bLog
						)
						else
						(
							bLayers[i] = bLayer
							bLayersCreated += 1
							if (bLayerParent[i] > 0) do
							(
								bParent = bLayers[bLayerParent[i]]
								if (bParent == undefined) then (format ""FAIL\tlayerparent\t%\tgone\n"" i to:bLog) else (bLayer.setParent bParent)
							)
						)
					)
					catch
					(
" + CatchLine("layercreate") + @"
					)
				)
");
            // Existing layers under their parents.
            s.Append(@"
				for k = 1 to bLayerMoveIdx.count do
				(
					local i = bLayerMoveIdx[k]
					try
					(
						bLayer = bLayers[i]
						bParent = bLayers[bLayerParent[i]]
						if (bLayer == undefined or bParent == undefined) then
						(
							format ""FAIL\tlayermove\t%\tgone\n"" i to:bLog
						)
						else
						(
							bLayer.setParent bParent
							bOther = bLayer.getParent()
							if (bOther != undefined and bOther.name == bParent.name) then (bLayersMoved += 1) else (format ""FAIL\tlayermove\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + CatchLine("layermove") + @"
					)
				)
");
            // Helper renames.
            s.Append(@"
				for k = 1 to bNodeRenameIdx.count do
				(
					local i = bNodeRenameIdx[k]
					try
					(
						bNode = bNodes[i]
						if (bNode != undefined) do
						(
							bNode.name = bNodeNew[i]
							if (bNode.name == bNodeNew[i]) then (bHelpersRenamed += 1) else (format ""FAIL\thelper\t%\trefused\n"" i to:bLog)
						)
					)
					catch
					(
" + CatchLine("helper") + @"
					)
				)
");
            // New helpers: a Dummy at the origin on its own layer, linked under its parent's helper, with its Planner ID.
            s.Append(@"
				for k = 1 to bNodeCreateIdx.count do
				(
					local i = bNodeCreateIdx[k]
					try
					(
						bLayer = if (bNodeLayer[i] > 0) then bLayers[bNodeLayer[i]] else undefined
						if (bLayer == undefined) then
						(
							format ""FAIL\thelpercreate\t%\tnolayer\n"" i to:bLog
						)
						else
						(
							bNode = Dummy name:bNodeNew[i] pos:[0,0,0] boxsize:" + DummyBoxSize + @"
							bLayer.addNode bNode
							bNodes[i] = bNode
							bHelpersCreated += 1
							if (bNodeId[i] != """") do
							(
								setUserProp bNode ""planner_id"" bNodeId[i]
								bIdsStamped += 1
							)
							if (bNodeParent[i] > 0) do
							(
								bParent = bNodes[bNodeParent[i]]
								if (bParent == undefined) then
								(
									format ""FAIL\thelperparent\t%\tgone\n"" i to:bLog
								)
								else
								(
" + LinkLines("\t\t\t\t\t\t\t\t\t") + @"
									if (bNode.parent != bParent) do (format ""FAIL\thelperparent\t%\trefused\n"" i to:bLog)
								)
							)
						)
					)
					catch
					(
" + CatchLine("helpercreate") + @"
					)
				)
");
            // Existing helpers onto their layers.
            s.Append(@"
				for k = 1 to bNodePutIdx.count do
				(
					local i = bNodePutIdx[k]
					try
					(
						bNode = bNodes[i]
						bLayer = bLayers[bNodeLayer[i]]
						if (bNode != undefined) do
						(
							if (bLayer == undefined) then
							(
								format ""FAIL\tput\t%\tnolayer\n"" i to:bLog
							)
							else
							(
								bLayer.addNode bNode
								if (bNode.layer != undefined and bNode.layer.name == bLayer.name) then (bHelpersPut += 1) else (format ""FAIL\tput\t%\trefused\n"" i to:bLog)
							)
						)
					)
					catch
					(
" + CatchLine("put") + @"
					)
				)
");
            // Existing helpers under their parents, parents first.
            s.Append(@"
				for k = 1 to bNodeMoveIdx.count do
				(
					local i = bNodeMoveIdx[k]
					try
					(
						bNode = bNodes[i]
						bParent = bNodes[bNodeParent[i]]
						if (bNode != undefined) do
						(
							if (bParent == undefined) then
							(
								format ""FAIL\thelpermove\t%\tgone\n"" i to:bLog
							)
							else
							(
" + LinkLines("\t\t\t\t\t\t\t\t") + @"
								if (bNode.parent == bParent) then (bHelpersMoved += 1) else (format ""FAIL\thelpermove\t%\trefused\n"" i to:bLog)
							)
						)
					)
					catch
					(
" + CatchLine("helpermove") + @"
					)
				)
");
            // Planner IDs on existing helpers.
            s.Append(@"
				for k = 1 to bNodeStampIdx.count do
				(
					local i = bNodeStampIdx[k]
					try
					(
						bNode = bNodes[i]
						if (bNode != undefined) do
						(
							setUserProp bNode ""planner_id"" bNodeId[i]
							bIdsStamped += 1
						)
					)
					catch
					(
" + CatchLine("stamp") + @"
					)
				)
");
            s.Append("\t\t\t)").Append(Nl);
            s.Append("\t\t)").Append(Nl);
            s.Append("\t)").Append(Nl);
            s.Append("\tcatch").Append(Nl);
            s.Append("\t(").Append(Nl);
            s.Append("\t\tbDetail = (getCurrentException()) as string").Append(Nl);
            s.Append("\t\t").Append(CleanDetail()).Append(Nl);
            s.Append("\t\tformat \"ABORT\\t%\\n\" bDetail to:bLog").Append(Nl);
            s.Append("\t)").Append(Nl);
            s.Append("\tformat \"DONE\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\t%\\n\" bLayersRenamed bLayersCreated bLayersMoved bHelpersRenamed bHelpersCreated bHelpersMoved bHelpersPut bIdsStamped to:bLog").Append(Nl);
            s.Append("\tformat \"").Append(EndMarker).Append("\\n\" to:bLog").Append(Nl);
            s.Append("\tbLog as string").Append(Nl);
            s.Append(")").Append(Nl);
            batch.Script = s.ToString().Replace("\r\n", "\n").Replace("\n", Nl);
            return batch;
        }

        /// <summary>Reads what the batch sent back. Anything unexpected is tolerated: a missing marker only clears <see cref="PlannerLayerResult.Completed"/>.</summary>
        public static PlannerLayerResult ReadResult(PlannerLayerBatch batch, string output)
        {
            var result = new PlannerLayerResult();
            result.Failures.AddRange(batch.Refused);
            foreach (var parts in PlannerScriptText.Lines(output, EndMarker, out result.Completed))
            {
                switch (parts[0])
                {
                    case "DONE":
                        if (parts.Length >= 9)
                        {
                            result.LayersRenamed = PlannerScriptText.ParseCount(parts[1]);
                            result.LayersCreated = PlannerScriptText.ParseCount(parts[2]);
                            result.LayersMoved = PlannerScriptText.ParseCount(parts[3]);
                            result.HelpersRenamed = PlannerScriptText.ParseCount(parts[4]);
                            result.HelpersCreated = PlannerScriptText.ParseCount(parts[5]);
                            result.HelpersMoved = PlannerScriptText.ParseCount(parts[6]);
                            result.HelpersPutOnLayer = PlannerScriptText.ParseCount(parts[7]);
                            result.IdsStamped = PlannerScriptText.ParseCount(parts[8]);
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

        private static string DescribeFailure(PlannerLayerBatch batch, string kind, int index, string code, string detail)
        {
            var plan = batch.Plan;
            var error = "3ds Max reported an error" + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
            var layer = index >= 0 && index < plan.BuildLayers.Count ? plan.BuildLayers[index] : null;
            var helper = index >= 0 && index < plan.BuildHelpers.Count ? plan.BuildHelpers[index] : null;
            Func<PlannerBuildLayer, string> parentLayerName = l => l.ParentIndex.HasValue && l.ParentIndex.Value < plan.BuildLayers.Count ? plan.BuildLayers[l.ParentIndex.Value].FinalName : "its parent";
            Func<PlannerBuildHelper, string> parentHelperName = h => h.ParentIndex.HasValue && h.ParentIndex.Value < plan.BuildHelpers.Count ? plan.BuildHelpers[h.ParentIndex.Value].FinalName : "its parent";
            switch (kind)
            {
                case "layerfind":
                    if (layer != null)
                    {
                        return "the layer " + layer.CurrentName + " is no longer in the scene, so it was not changed";
                    }
                    break;
                case "find":
                    if (helper != null)
                    {
                        return code == "changed"
                            ? "the helper " + helper.CurrentName + " has been renamed since the list was made, so it was not changed"
                            : "the helper " + helper.CurrentName + " is no longer in the scene, so it was not changed";
                    }
                    break;
                case "layer":
                    if (layer != null)
                    {
                        string temporary;
                        batch.TemporaryNames.TryGetValue(layer.Index, out temporary);
                        switch (code)
                        {
                            case "refused":
                                return "the layer " + layer.CurrentName + " could not be renamed; it keeps its name";
                            case "taken":
                                return "the layer " + layer.CurrentName + " could not become " + layer.FinalName + ": that name is taken; it keeps its name";
                            case "stuck":
                                return "the layer " + layer.CurrentName + " could not become " + layer.FinalName + " and is left as " + temporary + " - rename it by hand";
                            default:
                                return "the layer " + layer.CurrentName + " was not renamed to " + layer.FinalName + ": " + error;
                        }
                    }
                    break;
                case "layercreate":
                    if (layer != null)
                    {
                        return code == "taken"
                            ? "the layer " + layer.FinalName + " was not created: a layer of that name is already there"
                            : "the layer " + layer.FinalName + " was not created: " + error;
                    }
                    break;
                case "layerparent":
                    if (layer != null)
                    {
                        return "the new layer " + layer.FinalName + " was not put under " + parentLayerName(layer) + ", which is missing; it is at the top";
                    }
                    break;
                case "layermove":
                    if (layer != null)
                    {
                        return code == "gone"
                            ? "the layer " + layer.FinalName + " was not moved under " + parentLayerName(layer) + ": one of them is missing"
                            : code == "refused"
                                ? "the layer " + layer.FinalName + " could not be moved under " + parentLayerName(layer)
                                : "the layer " + layer.FinalName + " was not moved under " + parentLayerName(layer) + ": " + error;
                    }
                    break;
                case "helper":
                    if (helper != null)
                    {
                        return code == "refused"
                            ? "the helper " + helper.CurrentName + " could not be renamed to " + helper.FinalName
                            : "the helper " + helper.CurrentName + " was not renamed to " + helper.FinalName + ": " + error;
                    }
                    break;
                case "helpercreate":
                    if (helper != null)
                    {
                        return code == "nolayer"
                            ? "the helper " + helper.FinalName + " was not created: its layer is missing"
                            : "the helper " + helper.FinalName + " was not created: " + error;
                    }
                    break;
                case "helperparent":
                    if (helper != null)
                    {
                        return code == "gone"
                            ? "the new helper " + helper.FinalName + " was not linked under " + parentHelperName(helper) + ", which is missing; it is at the top"
                            : "the new helper " + helper.FinalName + " could not be linked under " + parentHelperName(helper);
                    }
                    break;
                case "put":
                    if (helper != null)
                    {
                        return code == "nolayer"
                            ? "the helper " + helper.FinalName + " was not put on its layer, which is missing"
                            : code == "refused"
                                ? "the helper " + helper.FinalName + " could not be put on its layer"
                                : "the helper " + helper.FinalName + " was not put on its layer: " + error;
                    }
                    break;
                case "helpermove":
                    if (helper != null)
                    {
                        return code == "gone"
                            ? "the helper " + helper.FinalName + " was not linked under " + parentHelperName(helper) + ", which is missing"
                            : code == "refused"
                                ? "the helper " + helper.FinalName + " could not be linked under " + parentHelperName(helper)
                                : "the helper " + helper.FinalName + " was not linked under " + parentHelperName(helper) + ": " + error;
                    }
                    break;
                case "stamp":
                    if (helper != null)
                    {
                        return "the helper " + helper.FinalName + " did not get its Planner ID: " + error;
                    }
                    break;
            }
            return "an item of the build was not carried out (" + kind + " " + (index + 1).ToString(CultureInfo.InvariantCulture) + ", " + code + ")"
                + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
        }

        // Links bNode under bParent, as Fix phasing names moves a layer: attached when the parent is a 3ds Max group head,
        // linked otherwise (leaving a group first when the parent is not in one).
        internal static string LinkLines(string indent)
        {
            return indent + "if (isGroupHead bParent) then" + Nl
                + indent + "(" + Nl
                + indent + "\tif (isGroupMember bNode) do (detachNodesFromGroup #(bNode))" + Nl
                + indent + "\tattachNodesToGroup #(bNode) bParent" + Nl
                + indent + ")" + Nl
                + indent + "else" + Nl
                + indent + "(" + Nl
                + indent + "\tif ((isGroupMember bNode) and not (isGroupMember bParent)) do (detachNodesFromGroup #(bNode))" + Nl
                + indent + "\tbNode.parent = bParent" + Nl
                + indent + ")";
        }

        internal static string CatchLine(string kind)
        {
            return "\t\t\t\t\t\tbDetail = (getCurrentException()) as string" + Nl
                + "\t\t\t\t\t\t" + CleanDetail() + Nl
                + "\t\t\t\t\t\tformat \"FAIL\\t" + kind + "\\t%\\terror\\t%\\n\" i bDetail to:bLog";
        }

        // The error text on one line, so it cannot break the report's line format.
        internal static string CleanDetail()
        {
            return "bDetail = substituteString (substituteString (substituteString bDetail \"\\r\" \" \") \"\\n\" \" \") \"\\t\" \" \"";
        }

        internal static void WriteArray(StringBuilder s, string name, List<string> items)
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

        internal static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Reading back what a MAXScript batch reports, shared by the layer build and the object links.</summary>
    internal static class PlannerScriptText
    {
        /// <summary>
        /// The report's lines split at tabs, without the end marker (whose presence sets <paramref name="completed"/>).
        /// Some 3ds Max versions may hand back the printed form of the string, quoted and escaped; that is read too.
        /// </summary>
        public static List<string[]> Lines(string output, string endMarker, out bool completed)
        {
            completed = false;
            var text = output ?? string.Empty;
            if (!HasMarker(text, endMarker) && text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                text = Unescape(text.Substring(1, text.Length - 2));
            }
            var lines = new List<string[]>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line == endMarker)
                {
                    completed = true;
                    continue;
                }
                lines.Add(line.Split('\t'));
            }
            return lines;
        }

        public static int ParseCount(string text)
        {
            int value;
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        public static string JoinWithAnd(List<string> parts)
        {
            if (parts.Count == 1)
            {
                return parts[0];
            }
            return string.Join(", ", parts.Take(parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
        }

        private static bool HasMarker(string text, string endMarker)
        {
            return text.Split('\n').Any(l => l.TrimEnd('\r') == endMarker);
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
