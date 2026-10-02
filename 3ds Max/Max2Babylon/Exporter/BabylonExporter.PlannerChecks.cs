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
    /// their extension. The naming rules live in <see cref="PlannerNamingValidator"/> (shared, Max-free); this
    /// file only gathers the scene nodes and reports.
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

            // A scene with a Work_Phasing root is checked by its Planner layers; without one, the legacy rules apply.
            PlannerScene layers = null;
            try
            {
                layers = PlannerMaxScene.Read();
            }
            catch (Exception e)
            {
                RaiseWarning("Planner layers could not be read, so the naming check uses the older dated-group rules: " + e.Message, 1);
            }

            NamingReport report;
            try
            {
                report = PlannerNamingValidator.Validate(CollectSceneNodes(gameScene, layers), layers, DateTime.Today);
            }
            catch (Exception e)
            {
                RaiseWarning("Planner naming check skipped: " + e.Message, 1);
                return true;
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
            RaiseMessage(report.Summary(), report.Errors > 0 ? Color.Red : Color.Black, 0, true);

            if (parameters.plannerNamingStrict && report.Errors > 0)
            {
                RaiseError(string.Format("Export stopped: fix the {0} naming error(s) above, or untick 'Stop export on naming errors'.", report.Errors));
                return false;
            }
            return true;
        }

        /// <summary>
        /// The exportable mesh nodes with their ancestor chain (what becomes the GLB hierarchy the Planner reads),
        /// plus every ancestor as a group record. Each record carries the node handle, the id the Planner layers
        /// scene uses.
        /// </summary>
        private IEnumerable<SceneNodeInfo> CollectSceneNodes(IIGameScene gameScene, PlannerScene layers)
        {
            var layerOf = layers != null ? LayerMapOf(layers) : BuildNodeLayerMap();
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
                            Id = PlannerMaxScene.IdOf(parent),
                            Name = parent.Name,
                            IsMesh = false,
                            Ancestors = AncestorNames(parent),
                            LayerName = LayerNameOf(layerOf, parent)
                        };
                    }
                }
                result.Add(new SceneNodeInfo
                {
                    Id = PlannerMaxScene.IdOf(maxNode),
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

        /// <summary>Node handle to layer name, from the Planner layers scene already read.</summary>
        private static Dictionary<uint, string> LayerMapOf(PlannerScene layers)
        {
            var map = new Dictionary<uint, string>();
            foreach (var node in layers.Nodes)
            {
                uint handle;
                if (node.LayerName != null && uint.TryParse(node.Id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out handle))
                {
                    map[handle] = node.LayerName;
                }
            }
            return map;
        }

        /// <summary>
        /// Node handle to layer name for the whole scene, when the Planner layers could not be read. Layer membership
        /// then only refines the wording of hints.
        /// </summary>
        private static Dictionary<uint, string> BuildNodeLayerMap()
        {
            try
            {
                return PlannerMaxScene.NodeLayerMap();
            }
            catch (Exception)
            {
                // The check works without layer information.
                return new Dictionary<uint, string>();
            }
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
