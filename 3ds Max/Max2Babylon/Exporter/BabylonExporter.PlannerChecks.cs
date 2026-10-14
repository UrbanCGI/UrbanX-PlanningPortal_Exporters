using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Max;
using Utilities;
using Utilities.Planner;
using Color = System.Drawing.Color;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: pre-flight checks that protect the Planner (planning.urbancgi.co.uk) from two recurring
    /// export faults, the naming convention its 4D generator depends on and textures whose bytes do not match
    /// their extension. The naming rules live in <see cref="PlannerNamingValidator"/> and the phasing counts in
    /// <see cref="PhasingNameFixer"/> (shared, Max-free); this file only gathers the scene nodes and reports.
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
            // The fixer's plan for the scene: which top node it corrects (the check says the layers of any other one are
            // renamed by hand), its pointer to the button, and the phasing counts.
            var phasing = PlanPhasingNames();
            NamingReport report;
            try
            {
                // Without a plan the check takes the first top node with "phasing" in its name; with one that found no
                // top node, none (an empty name).
                var phasingTop = phasing != null ? (phasing.TopName ?? string.Empty) : null;
                report = PlannerNamingValidator.Validate(CollectSceneNodes(gameScene), DateTime.Today, phasingTop);
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
            var fixable = phasing != null ? PhasingFixScript.ExportWarning(phasing) : null;
            if (fixable != null)
            {
                RaiseWarning(fixable, 1);
            }
            RaiseMessage(report.Summary(), report.Errors > 0 ? Color.Red : Color.Black, 0, true);
            if (counts != null)
            {
                // The tagged objects of the export are in the line above.
                RaiseMessage(counts.Describe(false), Color.Black, 0, true);
            }

            if (parameters.plannerNamingStrict && report.Errors > 0)
            {
                RaiseError(string.Format("Export stopped: fix the {0} naming error(s) above, or untick 'Stop export on naming errors'.", report.Errors));
                return false;
            }
            return true;
        }

        /// <summary>
        /// The phasing name fixer's plan for the scene (with the word fixes saved in it, no stage changes), or null when
        /// it cannot be made: the naming check stands on its own and never stops for the fixer.
        /// </summary>
        private static PhasingFixPlan PlanPhasingNames()
        {
            try
            {
                return PhasingScene.Plan(PhasingScene.LoadWordFixes(), null);
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
