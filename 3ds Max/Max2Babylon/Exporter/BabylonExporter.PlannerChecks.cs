using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Max;
using Utilities;
using Utilities.Planner;
using Color = System.Drawing.Color;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: pre-flight checks that protect the Planner (planning.urbancgi.co.uk) from two recurring
    /// export faults, the naming convention its 4D generator depends on and textures whose bytes do not match
    /// their extension. The naming rules live in <see cref="PlannerNamingValidator"/> (today's for a scene on the old
    /// names, naming v2's for one on the new names, picked by the scene's naming as the Planner reads it) and the phasing
    /// counts in <see cref="PhasingNameFixer"/> (shared, Max-free); this file only gathers the scene nodes and reports.
    /// </summary>
    partial class BabylonExporter
    {
        /// <returns>False when the export must stop: strict mode is on and at least one naming error was found.</returns>
        private bool RunPlannerNamingCheck(IIGameScene gameScene, MaxExportParameters parameters)
        {
            if (parameters == null || !parameters.plannerNamingCheck)
            {
                return true;
            }

            RaiseMessage("Checking Planner naming convention", Color.Blue);
            // The scene as this export hands it to the Planner, and its naming (spec 2.3), read by the Planner's one rule: a
            // new-name scene is checked by naming v2's rules; an old-name one by today's, unchanged, with one note (spec 5.11).
            PlannerScheduleModel exported = null;
            PlannerModelNaming naming = null;
            try
            {
                exported = new PlannerScheduleModel(ReadSceneForExport(parameters));
                naming = PlannerSchedule.NamingOfScene(exported);
            }
            catch (Exception e)
            {
                RaiseWarning("Planner naming check could not tell which naming the scene uses, so it checks the old names: " + e.Message, 1);
            }
            if (naming != null && naming.Naming == PlannerNamingKind.New)
            {
                return RunPlannerNamingCheckV2(exported, parameters);
            }

            // The old phasing rules' plan for the scene: which top node they read, the objects they leave for the modeller to
            // correct (taken away and put back in one stage in another stage's layer), and the phasing counts. No button
            // corrects old names any more (Fix phasing names is replaced by Clean up scene graph, which converts the scene to
            // the new names): the check's advice is what to rename by hand, and its note points to Clean up.
            var phasing = PlanPhasingNames();
            NamingReport report;
            try
            {
                // Without a plan the check takes the first top node with "phasing" in its name; with one that found no
                // top node, none (an empty name).
                var phasingTop = phasing != null ? (phasing.TopName ?? string.Empty) : null;
                report = PlannerNamingValidator.Validate(CollectSceneNodes(gameScene), DateTime.Today, phasingTop, phasing != null ? phasing.LeftMisfiled : null);
            }
            catch (Exception e)
            {
                RaiseWarning("Planner naming check skipped: " + e.Message, 1);
                return true;
            }

            // A note on layers and helpers that do not pair up goes in with the other notes.
            var counts = phasing != null ? phasing.Counts : null;
            var pairing = counts != null ? counts.PairingNote() : null;
            if (pairing != null)
            {
                report.Issues.Add(new NamingIssue { Severity = NamingSeverity.Note, Subject = counts.TopName, Message = pairing });
            }
            // A scene read as the old names (not one with nothing that tells): what Clean up scene graph does for it.
            if (naming != null)
            {
                report.Issues.Add(new NamingIssue { Severity = NamingSeverity.Note, Subject = counts != null ? counts.TopName : null, Message = PlannerNamingValidator.OldNamesNote });
            }

            RaiseNamingIssues(report);
            RaiseMessage(report.Summary(), report.Errors > 0 ? Color.Red : Color.Black, 0, true);
            if (counts != null)
            {
                // The tagged objects of the export are in the line above.
                RaiseMessage(counts.Describe(false), Color.Black, 0, true);
            }
            return !StopsOnNamingErrors(report, parameters);
        }

        /// <summary>
        /// The naming check on the new names (naming v2, spec 5.10; <see cref="PlannerNamingValidator.ValidateV2"/>): the rules
        /// read the scene as the Planner reads the exported model, with the imported layer list's layers as the Planner's. The
        /// log lists the issues, the pointer to Clean up scene graph, the summary, what the Planner reads, and the phasing counts.
        /// </summary>
        /// <returns>False when the export must stop: strict mode is on and at least one naming error was found.</returns>
        private bool RunPlannerNamingCheckV2(PlannerScheduleModel exported, MaxExportParameters parameters)
        {
            NamingReport report;
            try
            {
                report = PlannerNamingValidator.ValidateV2(exported, ImportedListLayers());
            }
            catch (Exception e)
            {
                RaiseWarning("Planner naming check skipped: " + e.Message, 1);
                return true;
            }
            RaiseNamingIssues(report);
            var pointer = report.CleanUpPointer();
            if (pointer != null)
            {
                RaiseWarning(pointer, 1);
            }
            RaiseMessage(report.Summary(), report.Errors > 0 ? Color.Red : Color.Black, 0, true);
            RaiseMessage(report.Reads(), Color.Black, 0, true);
            if (report.Counts != null)
            {
                RaiseMessage(report.Counts.Describe(false), Color.Black, 0, true);
            }
            return !StopsOnNamingErrors(report, parameters);
        }

        private void RaiseNamingIssues(NamingReport report)
        {
            foreach (var issue in report.Issues)
            {
                switch (issue.Severity)
                {
                    case NamingSeverity.Error:
                        RaiseError(issue.Message, 1);
                        break;
                    case NamingSeverity.Warning:
                        RaiseWarning(issue.Message, 1);
                        break;
                    default:
                        RaiseMessage(issue.Message, Color.Gray, 1);
                        break;
                }
            }
        }

        // Strict mode stops the export on a naming error, and says so.
        private bool StopsOnNamingErrors(NamingReport report, MaxExportParameters parameters)
        {
            if (parameters.plannerNamingStrict && report.Errors > 0)
            {
                RaiseError(string.Format("Export stopped: fix the {0} naming error(s) above, or untick 'Stop export on naming errors'.", report.Errors));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The scene as this export hands it to the Planner: the Schedule tab's reading of it, with this export's own settings
        /// (BabylonExporter.IsNodeExportable): Export hidden objects, and the nodes it leaves out besides (Export only selected,
        /// the layers chosen, nodes flattened away, the exporter's own helpers). A node exported on its own (the
        /// multi-exporter's) is the top of what is read.
        /// </summary>
        private static PlannerSceneSnapshot ReadSceneForExport(MaxExportParameters parameters)
        {
            var snapshot = PlannerScheduleScene.Read();
            snapshot.ExportHidden = parameters.exportHiddenObjects;
            var exportNode = parameters.exportNode;
            if (exportNode != null && !exportNode.IsRootNode)
            {
                var children = new Dictionary<long, List<PlannerSceneNode>>();
                foreach (var node in snapshot.Nodes.Where(n => n.ParentKey.HasValue))
                {
                    List<PlannerSceneNode> list;
                    if (!children.TryGetValue(node.ParentKey.Value, out list))
                    {
                        list = new List<PlannerSceneNode>();
                        children[node.ParentKey.Value] = list;
                    }
                    list.Add(node);
                }
                var keep = new HashSet<long> { exportNode.Handle };
                var pending = new Queue<long>(keep);
                while (pending.Count > 0)
                {
                    List<PlannerSceneNode> list;
                    if (children.TryGetValue(pending.Dequeue(), out list))
                    {
                        foreach (var child in list.Where(c => keep.Add(c.Key)))
                        {
                            pending.Enqueue(child.Key);
                        }
                    }
                }
                snapshot.Nodes.RemoveAll(n => !keep.Contains(n.Key));
                foreach (var top in snapshot.Nodes.Where(n => n.Key == exportNode.Handle))
                {
                    top.ParentKey = null;
                }
            }
            foreach (var node in snapshot.Nodes)
            {
                var maxNode = Loader.Core.GetINodeByHandle((uint)node.Key);
                if (maxNode == null)
                {
                    continue;
                }
                if (maxNode.GetBoolProperty("babylonjs_flatteningTemp"))
                {
                    // The export always takes a node flattening made.
                    node.Hidden = false;
                    node.NoExport = false;
                    continue;
                }
                if ((parameters.exportLayers != null && parameters.exportLayers.Count > 0 && !parameters.exportLayers.HaveNode(maxNode))
                    || maxNode.GetBoolProperty("babylonjs_flattened") || maxNode.IsBabylonContainerHelper() || maxNode.IsBabylonAnimationHelper()
                    || (parameters.exportOnlySelected && !maxNode.Selected))
                {
                    node.NotInThisExport = true;
                }
            }
            return snapshot;
        }

        /// <summary>The Layer column of the layer list imported on the Schedule tab and kept with the scene: the Planner's folders; null with none.</summary>
        private static List<string> ImportedListLayers()
        {
            try
            {
                string csvText, fileName;
                DateTime? importedUtc;
                if (!PlannerScheduleScene.TryLoadList(out csvText, out fileName, out importedUtc))
                {
                    return null;
                }
                var list = PlannerLayerList.Parse(csvText);
                return list.Usable ? list.Rows.Select(r => r.Layer).ToList() : null;
            }
            catch (Exception)
            {
                // Checked as with no list.
                return null;
            }
        }

        /// <summary>
        /// The phasing name fixer's plan for the scene (with the word fixes saved in it, no stage changes), or null when
        /// it cannot be made: the naming check stands on its own and never stops for the fixer.
        /// </summary>
        private static PhasingFixPlan PlanPhasingNames()
        {
            try
            {
                return PhasingScene.Plan(PhasingScene.LoadWordFixes());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The exportable mesh nodes with their ancestor chain (what becomes the GLB hierarchy the Planner reads),
        /// plus every ancestor as a group record.
        /// </summary>
        private IEnumerable<SceneNodeInfo> CollectSceneNodes(IIGameScene gameScene)
        {
            var layerOf = BuildNodeLayerMap();
            var result = new List<SceneNodeInfo>();
            var groups = new Dictionary<uint, SceneNodeInfo>();

            var meshNodes = TabToList(gameScene.GetIGameNodeByType(Autodesk.Max.IGameObject.ObjectTypes.Mesh)) ?? new List<IIGameNode>();
            foreach (var gameNode in meshNodes)
            {
                if (gameNode == null || gameNode.MaxNode == null || !IsNodeExportable(gameNode))
                {
                    continue;
                }
                var maxNode = gameNode.MaxNode;
                var ancestors = new List<string>();
                for (var parent = maxNode.ParentNode; parent != null && !parent.IsRootNode; parent = parent.ParentNode)
                {
                    ancestors.Add(parent.Name);
                    if (!groups.ContainsKey(parent.Handle))
                    {
                        groups[parent.Handle] = new SceneNodeInfo
                        {
                            Name = parent.Name,
                            IsMesh = false,
                            Ancestors = AncestorNames(parent),
                            LayerName = LayerNameOf(layerOf, parent)
                        };
                    }
                }
                result.Add(new SceneNodeInfo
                {
                    Name = maxNode.Name,
                    IsMesh = true,
                    Ancestors = ancestors,
                    LayerName = LayerNameOf(layerOf, maxNode)
                });
            }
            result.AddRange(groups.Values);
            return result;
        }

        private static List<string> AncestorNames(IINode node)
        {
            var names = new List<string>();
            for (var parent = node.ParentNode; parent != null && !parent.IsRootNode; parent = parent.ParentNode)
            {
                names.Add(parent.Name);
            }
            return names;
        }

        private static string LayerNameOf(Dictionary<uint, string> layerOf, IINode node)
        {
            string layerName;
            return layerOf.TryGetValue(node.Handle, out layerName) ? layerName : null;
        }

        /// <summary>Node handle to layer name for the whole scene. Layer membership only refines the wording of hints.</summary>
        private static Dictionary<uint, string> BuildNodeLayerMap()
        {
            var map = new Dictionary<uint, string>();
            try
            {
                var manager = Loader.Core.LayerManager;
                for (int i = 0; i < manager.LayerCount; i++)
                {
                    var layer = manager.GetLayer(i);
                    if (layer == null)
                    {
                        continue;
                    }
                    foreach (var node in layer.LayerNodes())
                    {
                        if (node != null)
                        {
                            map[node.Handle] = layer.Name;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The check works without layer information.
            }
            return map;
        }

        /// <summary>Lists, at the end of the export, every texture whose extension lied about its content.</summary>
        private void ReportTextureCorrections()
        {
            var corrections = TextureUtilities.Corrections;
            if (corrections.Count == 0)
            {
                return;
            }
            RaiseWarning(string.Format(
                "{0} texture file(s) had an extension that does not match their content and were re-encoded for this export. Fix the source files so the model stops relying on this correction:",
                corrections.Count));
            foreach (var correction in corrections)
            {
                RaiseWarning(string.Format("{0}: .{1} extension, {2} content, exported as {3}",
                    Path.GetFileName(correction.SourcePath),
                    correction.DeclaredFormat,
                    correction.ActualFormat.ToUpperInvariant(),
                    correction.ExportedFormat != null ? correction.ExportedFormat.ToUpperInvariant() : "nothing"), 1);
            }
        }
    }
}
