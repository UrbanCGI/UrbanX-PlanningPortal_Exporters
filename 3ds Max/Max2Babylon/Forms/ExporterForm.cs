using Autodesk.Max;
using BabylonExport.Entities;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Utilities;
using Utilities.Updates;
using Color = System.Drawing.Color;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Collections.Generic;
using System.Linq;

namespace Max2Babylon
{
    public partial class ExporterForm : Form
    {
        private readonly BabylonExportActionItem babylonExportAction;
        private BabylonExporter exporter;
        private bool gltfPipelineInstalled = true;  // true if the gltf-pipeline is installed and runnable.

        TreeNode currentNode;
        int currentRank;

        private ExportItem singleExportItem;

        // UrbanCGI fork: opens the phasing name fixer; added at run time (see AddFixPhasingNamesButton).
        private Button butFixPhasingNames;

        /// <summary>UrbanCGI fork: true while this window runs an export, so the phasing name fixer waits for it.</summary>
        internal static bool ExportRunning { get; private set; }

        // UrbanCGI fork: version and update controls, added at run time (see AddUpdatePanel).
        private Panel updatePanel;
        private Label lblUpdateVersion;
        private Label lblUpdateStatus;
        private Label lblUpdateProgress;
        private Button butCheckForUpdates;
        private Button butUpdate;
        private ToolTip updateToolTip;
        private bool updateBusy;           // a check or a download is running
        private bool exportBusy;           // this window is exporting
        private UpdateVerdict updateVerdict = UpdateVerdict.Unknown;
        private PreparedUpdate preparedUpdate;  // downloaded but not installed (e.g. the permission prompt was declined)
        private readonly CancellationTokenSource updateCancel = new CancellationTokenSource();
        private System.Windows.Forms.Timer maxCloseWatch;  // after asking 3ds Max to close, notices that it did not

        /// <summary>How long 3ds Max may take to close for an update before the window says it did not.</summary>
        private static readonly TimeSpan MaxCloseWait = TimeSpan.FromSeconds(60);

        private bool filePostOpenCallback = false;
        private GlobalDelegates.Delegate5 m_FilePostOpenDelegate;

        private void RegisterFilePostOpen()
        {
            if (!filePostOpenCallback)
            {
                m_FilePostOpenDelegate = new GlobalDelegates.Delegate5(OnFilePostOpen);
                GlobalInterface.Instance.RegisterNotification(this.m_FilePostOpenDelegate, null, SystemNotificationCode.FilePostOpen);

                filePostOpenCallback = true;
            }
        }

        private void OnFilePostOpen(IntPtr param0, IntPtr param1)
        {
            LoadOptions();
        }

        private void OnFilePostOpen(IntPtr param0, INotifyInfo param1)
        {
            LoadOptions();
        }

        public ExporterForm(BabylonExportActionItem babylonExportAction)
        {
            InitializeComponent();
            RegisterFilePostOpen();

            this.pictureBox1.Image = ResourceHelper.LoadImage("Max2Babylon.Resources.MaxExporter.png");
            this.Text = $"Babylon.js - Export scene to babylon or glTF format v{BabylonExporter.exporterVersion} - {ReleaseInfo.ShortText}";

            this.babylonExportAction = babylonExportAction;

            // Check if the gltf-pipeline module is installed
            this.gltfPipelineInstalled = GLTFPipelineUtilities.IsGLTFPipelineInstalled();

            exportOptionsScrollPanel.MouseMove += exportOptionsScrollPanel_MouseMove;
        }


        private void LoadOptions()
        {
            string storedModelPath = Loader.Core.RootNode.GetStringProperty(ExportParameters.ModelFilePathProperty, string.Empty);
            string absoluteModelPath = Tools.ResolveRelativePath(storedModelPath);
            txtModelPath.MaxPath(absoluteModelPath);

            string storedFolderPath = Loader.Core.RootNode.GetStringProperty(ExportParameters.TextureFolderPathProperty, string.Empty);
            string absoluteTexturesFolderPath = Tools.ResolveRelativePath(storedFolderPath);
            txtTexturesPath.MaxPath(absoluteTexturesFolderPath);

            singleExportItem = new ExportItem(absoluteModelPath);

            Tools.PrepareCheckBox(chkManifest, Loader.Core.RootNode, "babylonjs_generatemanifest");
            Tools.PrepareCheckBox(chkWriteTextures, Loader.Core.RootNode, "babylonjs_writetextures", 1);
            Tools.PrepareCheckBox(chkOverwriteTextures, Loader.Core.RootNode, "babylonjs_overwritetextures", 1);
            Tools.PrepareCheckBox(chkHidden, Loader.Core.RootNode, "babylonjs_exporthidden");
            Tools.PrepareCheckBox(chkAutoSave, Loader.Core.RootNode, "babylonjs_autosave", 1);
            Tools.PrepareCheckBox(chkOnlySelected, Loader.Core.RootNode, "babylonjs_onlySelected");
            Tools.PrepareCheckBox(chkExportTangents, Loader.Core.RootNode, "babylonjs_exporttangents");
            Tools.PrepareComboBox(comboOutputFormat, Loader.Core.RootNode, "babylonjs_outputFormat", "babylon");
            Tools.PrepareTextBox(txtScaleFactor, Loader.Core.RootNode, "babylonjs_txtScaleFactor", "1");
            Tools.PrepareTextBox(txtQuality, Loader.Core.RootNode, "babylonjs_txtCompression", "100");
            Tools.PrepareCheckBox(chkMergeAO, Loader.Core.RootNode, "babylonjs_mergeAOwithMR", 1);
            Tools.PrepareCheckBox(chkDracoCompression, Loader.Core.RootNode, "babylonjs_dracoCompression", 0);
            Tools.PrepareCheckBox(chkKHRLightsPunctual, Loader.Core.RootNode, "babylonjs_khrLightsPunctual");
            Tools.PrepareCheckBox(chkKHRTextureTransform, Loader.Core.RootNode, "babylonjs_khrTextureTransform");
            Tools.PrepareCheckBox(chkAnimgroupExportNonAnimated, Loader.Core.RootNode, "babylonjs_animgroupexportnonanimated");
            Tools.PrepareCheckBox(chkDoNotOptimizeAnimations, Loader.Core.RootNode, "babylonjs_donotoptimizeanimations");
            Tools.PrepareCheckBox(chkKHRMaterialsUnlit, Loader.Core.RootNode, "babylonjs_khr_materials_unlit");
            Tools.PrepareCheckBox(chkExportMaterials, Loader.Core.RootNode, "babylonjs_export_materials", 1);
            Tools.PrepareCheckBox(chkExportAnimations, Loader.Core.RootNode, "babylonjs_export_animations", 1);
            Tools.PrepareCheckBox(chkExportAnimationsOnly, Loader.Core.RootNode, "babylonjs_export_animations_only");
            Tools.PrepareCheckBox(chkExportMorphTangents, Loader.Core.RootNode, "babylonjs_export_Morph_Tangents", 0);
            Tools.PrepareCheckBox(chkExportMorphNormals, Loader.Core.RootNode, "babylonjs_export_Morph_Normals", 1);
            Tools.PrepareCheckBox(chkExportMorphNames, Loader.Core.RootNode, "babylonjs_export_Morph_Names", 0);
            Tools.PrepareCheckBox(chkExportTextures, Loader.Core.RootNode, "babylonjs_export_Textures", 1);
            Tools.PrepareComboBox(cmbBakeAnimationOptions, Loader.Core.RootNode, "babylonjs_bakeAnimationsType", (int)BakeAnimationType.DoNotBakeAnimation);
            Tools.PrepareCheckBox(chkApplyPreprocessToScene, Loader.Core.RootNode, "babylonjs_applyPreprocess", 0);
            

            if (comboOutputFormat.SelectedText == "babylon" || comboOutputFormat.SelectedText == "binary babylon" || !gltfPipelineInstalled)
            {
                chkDracoCompression.Checked = false;
                chkDracoCompression.Enabled = false;
            }


            Tools.PrepareCheckBox(chkFullPBR, Loader.Core.RootNode, ExportParameters.PBRFullPropertyName);
            Tools.PrepareCheckBox(chkNoAutoLight, Loader.Core.RootNode, ExportParameters.PBRNoLightPropertyName);
            string storedEnvironmentPath = Loader.Core.RootNode.GetStringProperty(ExportParameters.PBREnvironmentPathPropertyName, string.Empty);
            string absoluteEnvironmentPath = Tools.ResolveRelativePath(storedEnvironmentPath);
            txtEnvironmentName.MaxPath(absoluteEnvironmentPath);

            Tools.PrepareCheckBox(chkUsePreExportProces, Loader.Core.RootNode, "babylonjs_preproces", 0);
            Tools.PrepareCheckBox(chkFlatten, Loader.Core.RootNode, "babylonjs_flattenScene", 0);
            Tools.PrepareCheckBox(chkMrgContainersAndXref, Loader.Core.RootNode, "babylonjs_mergecontainersandxref", 0);
            Tools.PrepareCheckBox(chkTryReuseTexture, Loader.Core.RootNode, "babylonjs_tryReuseTexture", 0);

            Tools.PrepareCheckBox(chkUseClone, Loader.Core.RootNode, "babylonjs_useClone", 0);
            Tools.PrepareCheckBox(chkPlannerNaming, Loader.Core.RootNode, MaxExportParameters.PlannerNamingCheckProperty, 1);
            Tools.PrepareCheckBox(chkPlannerNamingStrict, Loader.Core.RootNode, MaxExportParameters.PlannerNamingStrictProperty, 0);


            #region prepare draco            
            LoadDracoOptions();
            dracoUserControl.UpdateValueLabels(); // force value label to be updated
            dracoGroupBox.Enabled = chkDracoCompression.Enabled && chkDracoCompression.Checked;
            #endregion
        }

        private void LoadDracoOptions()
        {
            Tools.PrepareNumericUpDown(dracoUserControl.CompressionLevelNumericUpDown, Loader.Core.RootNode, $"babylonjs_{DracoParameters.compressionLevel_param_name}", DracoParameters.compressionLevel_default);
            Tools.PrepareTrackBar(dracoUserControl.QPositionTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizePositionBits_param_name}", DracoParameters.quantizePositionBits_default);
            Tools.PrepareTrackBar(dracoUserControl.QNormalTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeNormalBits_param_name}", DracoParameters.quantizeNormalBits_default);
            Tools.PrepareTrackBar(dracoUserControl.QTexcoordTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeTexcoordBits_param_name}", DracoParameters.quantizeTexcoordBits_default);
            Tools.PrepareTrackBar(dracoUserControl.QColorTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeColorBits_param_name}", DracoParameters.quantizeColorBits_default);
            Tools.PrepareTrackBar(dracoUserControl.QGenericTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeGenericBits_param_name}", DracoParameters.quantizeGenericBits_default);
            Tools.PrepareCheckBox(dracoUserControl.UnifiedCheckBox, Loader.Core.RootNode, $"babylonjs_{DracoParameters.unifiedQuantization_param_name}", DracoParameters.unifiedQuantization_default?1:0);
        }

        private void ExporterForm_Load(object sender, EventArgs e)
        {
            AddFixPhasingNamesButton();
            AddUpdatePanel();
            LoadOptions();

            var maxVersion = Tools.GetMaxVersion();
            if (maxVersion.Major == 22 && maxVersion.Minor < 2)
            {
                CreateErrorMessage("You must update 3dsMax 2020 to version 2020.2 to use Max2Babylon. Unpatched versions of 3dsMax will crash during export.", 0);
            }
            else
            {
                CreateMessage(String.Format("Using Max2Babylon for 3dsMax version v{0}.{1}.{2}.{3}", maxVersion.Major, maxVersion.Minor, maxVersion.Revision, maxVersion.BuildNumber), Color.Black, 0, true);
            }

            // In the background, at most once a day, silent when offline (UrbanCGI fork).
            StartUpdateCheck(false);
        }

        private void butModelBrowse_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtModelPath.Text))
            {
                string intialDirectory = Path.GetDirectoryName(txtModelPath.Text);

                if (!Directory.Exists(intialDirectory))
                {
                    intialDirectory = Loader.Core.GetDir((int)MaxDirectory.ProjectFolder);
                }

                if (!Directory.Exists(intialDirectory))
                {
                    intialDirectory = null;
                }

                saveFileDialog.InitialDirectory = intialDirectory;
            }

            if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                txtModelPath.MaxPath(saveFileDialog.FileName);
                if (string.IsNullOrWhiteSpace(txtTexturesPath.Text))
                {
                    string defaultTexturesDir = Path.GetDirectoryName(txtModelPath.Text);
                    txtTexturesPath.MaxPath(defaultTexturesDir);
                }

                if (!PathUtilities.IsBelowPath(txtTexturesPath.Text, txtModelPath.Text))
                {
                    CreateWarningMessage("WARNING: textures path should be below model file path, not all client renderers support this feature", 0);
                }
            }
        }

        private void btnTextureBrowse_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtModelPath.Text))
            {
                MessageBox.Show("Select model file path first");
                return;
            }

            CommonOpenFileDialog dialog = new CommonOpenFileDialog();

            string intialDirectory = txtTexturesPath.Text;

            if (!Directory.Exists(intialDirectory))
            {
                intialDirectory = Path.GetDirectoryName(txtModelPath.Text);
            }

            if (!Directory.Exists(intialDirectory))
            {
                intialDirectory = Loader.Core.GetDir((int)MaxDirectory.ProjectFolder);
            }

            if (!Directory.Exists(intialDirectory))
            {
                intialDirectory = null;
            }

            dialog.InitialDirectory = intialDirectory;
            dialog.IsFolderPicker = true;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string selectedFolderPath = dialog.FileName;
                string absoluteModelPath = txtModelPath.Text;

                if (!PathUtilities.IsBelowPath(selectedFolderPath, absoluteModelPath))
                {
                    CreateWarningMessage("WARNING: textures path should be below model file path, not all client renderers support this feature", 0);
                }

                txtTexturesPath.MaxPath(selectedFolderPath);
            }
        }

        private void btnEnvBrowse_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtEnvironmentName.Text))
            {
                string intialDirectory = Path.GetDirectoryName(txtEnvironmentName.Text);

                if (!Directory.Exists(intialDirectory))
                {
                    intialDirectory = Loader.Core.GetDir((int)MaxDirectory.ProjectFolder);
                }

                if (!Directory.Exists(intialDirectory))
                {
                    intialDirectory = null;
                }

                envFileDialog.InitialDirectory = intialDirectory;
            }

            if (envFileDialog.ShowDialog() == DialogResult.OK)
            {
                txtEnvironmentName.MaxPath(envFileDialog.FileName);
            }
        }

        public async void butExport_Click(object sender, EventArgs e)
        {
            try
            {
                if (chkUsePreExportProces.Checked)
                {
                    Loader.Core.FileHold();
                }

                await DoExport(singleExportItem);
            }
            catch { }
            finally
            {
                if (chkUsePreExportProces.Checked && !chkApplyPreprocessToScene.Checked)
                {
                    Loader.Core.SetQuietMode(true);
                    Loader.Core.FileFetch();
                    Loader.Core.SetQuietMode(false);
                }
            }
        }

        private async Task<bool> DoExport(ExportItemList exportItemList)
        {
            logTreeView.Nodes.Clear();

            bool allSucceeded = true;
            foreach (ExportItem item in exportItemList)
            {
                if (!item.Selected) continue;

                allSucceeded = allSucceeded && await DoExport(item, true, false);

                if (exporter.IsCancelled)
                    break;
            }

            return allSucceeded;
        }

        private void saveOptionBtn_Click(object sender, EventArgs e)
        {
            SaveOptions();
        }

        private void SaveOptions()
        {
            Tools.UpdateCheckBox(chkManifest, Loader.Core.RootNode, "babylonjs_generatemanifest");
            Tools.UpdateCheckBox(chkWriteTextures, Loader.Core.RootNode, "babylonjs_writetextures");
            Tools.UpdateCheckBox(chkOverwriteTextures, Loader.Core.RootNode, "babylonjs_overwritetextures");
            Tools.UpdateCheckBox(chkHidden, Loader.Core.RootNode, "babylonjs_exporthidden");
            Tools.UpdateCheckBox(chkAutoSave, Loader.Core.RootNode, "babylonjs_autosave");
            Tools.UpdateCheckBox(chkOnlySelected, Loader.Core.RootNode, "babylonjs_onlySelected");
            Tools.UpdateCheckBox(chkExportTangents, Loader.Core.RootNode, "babylonjs_exporttangents");
            Tools.UpdateComboBox(comboOutputFormat, Loader.Core.RootNode, "babylonjs_outputFormat");
            Tools.UpdateTextBox(txtScaleFactor, Loader.Core.RootNode, "babylonjs_txtScaleFactor");
            Tools.UpdateTextBox(txtQuality, Loader.Core.RootNode, "babylonjs_txtCompression");
            Tools.UpdateCheckBox(chkMergeAO, Loader.Core.RootNode, "babylonjs_mergeAOwithMR");
            Tools.UpdateCheckBox(chkDracoCompression, Loader.Core.RootNode, "babylonjs_dracoCompression");
            Tools.UpdateCheckBox(chkKHRTextureTransform, Loader.Core.RootNode, "babylonjs_khrTextureTransform");
            Tools.UpdateCheckBox(chkKHRLightsPunctual, Loader.Core.RootNode, "babylonjs_khrLightsPunctual");
            Tools.UpdateCheckBox(chkKHRMaterialsUnlit, Loader.Core.RootNode, "babylonjs_khr_materials_unlit");
            Tools.UpdateCheckBox(chkExportMaterials, Loader.Core.RootNode, "babylonjs_export_materials");
            Tools.UpdateCheckBox(chkExportAnimations, Loader.Core.RootNode, "babylonjs_export_animations");
            Tools.UpdateCheckBox(chkExportAnimationsOnly, Loader.Core.RootNode, "babylonjs_export_animations_only");
            Tools.UpdateCheckBox(chkAnimgroupExportNonAnimated, Loader.Core.RootNode, "babylonjs_animgroupexportnonanimated");
            Tools.UpdateCheckBox(chkDoNotOptimizeAnimations, Loader.Core.RootNode, "babylonjs_donotoptimizeanimations");
            Tools.UpdateCheckBox(chkExportMorphTangents, Loader.Core.RootNode, "babylonjs_export_Morph_Tangents");
            Tools.UpdateCheckBox(chkExportMorphNormals, Loader.Core.RootNode, "babylonjs_export_Morph_Normals");
            Tools.UpdateCheckBox(chkExportMorphNames, Loader.Core.RootNode, "babylonjs_export_Morph_Names");
            Tools.UpdateCheckBox(chkExportTextures, Loader.Core.RootNode, "babylonjs_export_Textures");
            Tools.UpdateComboBoxByIndex(cmbBakeAnimationOptions, Loader.Core.RootNode, "babylonjs_bakeAnimationsType");
            Tools.UpdateCheckBox(chkApplyPreprocessToScene, Loader.Core.RootNode, "babylonjs_applyPreprocess");

            Loader.Core.RootNode.SetStringProperty(ExportParameters.ModelFilePathProperty, Tools.RelativePathStore(txtModelPath.Text));
            Loader.Core.RootNode.SetStringProperty(ExportParameters.TextureFolderPathProperty, Tools.RelativePathStore(txtTexturesPath.Text));

            Tools.UpdateCheckBox(chkFullPBR, Loader.Core.RootNode, ExportParameters.PBRFullPropertyName);
            Tools.UpdateCheckBox(chkNoAutoLight, Loader.Core.RootNode, ExportParameters.PBRNoLightPropertyName);
            Loader.Core.RootNode.SetStringProperty(ExportParameters.PBREnvironmentPathPropertyName, Tools.RelativePathStore(txtEnvironmentName.Text));

            Tools.UpdateCheckBox(chkUsePreExportProces, Loader.Core.RootNode, "babylonjs_preproces");
            Tools.UpdateCheckBox(chkFlatten, Loader.Core.RootNode, "babylonjs_flattenScene");
            Tools.UpdateCheckBox(chkMrgContainersAndXref, Loader.Core.RootNode, "babylonjs_mergecontainersandxref");
            Tools.UpdateCheckBox(chkTryReuseTexture, Loader.Core.RootNode, "babylonjs_tryReuseTexture");
            Tools.UpdateCheckBox(chkPlannerNaming, Loader.Core.RootNode, MaxExportParameters.PlannerNamingCheckProperty);
            Tools.UpdateCheckBox(chkPlannerNamingStrict, Loader.Core.RootNode, MaxExportParameters.PlannerNamingStrictProperty);

            SaveDracoOptions();
        }

        private void SaveDracoOptions()
        {
            Tools.UpdateNumericUpDown(dracoUserControl.CompressionLevelNumericUpDown, Loader.Core.RootNode, $"babylonjs_{DracoParameters.compressionLevel_param_name}");
            Tools.UpdateTrackBar(dracoUserControl.QPositionTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizePositionBits_param_name}");
            Tools.UpdateTrackBar(dracoUserControl.QNormalTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeNormalBits_param_name}");
            Tools.UpdateTrackBar(dracoUserControl.QTexcoordTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeTexcoordBits_param_name}");
            Tools.UpdateTrackBar(dracoUserControl.QColorTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeColorBits_param_name}");
            Tools.UpdateTrackBar(dracoUserControl.QGenericTrackBar, Loader.Core.RootNode, $"babylonjs_{DracoParameters.quantizeGenericBits_param_name}");
            Tools.UpdateCheckBox(dracoUserControl.UnifiedCheckBox, Loader.Core.RootNode, $"babylonjs_{DracoParameters.unifiedQuantization_param_name}");
        }


        private async Task<bool> DoExport(ExportItem exportItem, bool multiExport = false, bool clearLogs = true)
        {
            new BabylonAnimationActionItem().Close();
            SaveOptions();

            //store layer visibility status and force visibility on

            Dictionary<IILayer, bool> layerState = new Dictionary<IILayer, bool>();
            if (exportItem.Layers != null)
            {
                foreach (IILayer layer in exportItem.Layers)
                {
                    List<IILayer> treeLayers = layer.LayerTree().ToList();
                    treeLayers.Add(layer);

                    foreach (IILayer l in treeLayers)
                    {
#if MAX2015
                        layerState.Add( l,  l.IsHidden);
#else
                        layerState.Add(l, l.IsHidden(false));
#endif
                        l.Hide(false, false);
                    }

                }
            }

            exporter = new BabylonExporter();

            if (clearLogs)
                logTreeView.Nodes.Clear();

            exporter.OnExportProgressChanged += progress =>
            {
                progressBar.Value = progress;
                Application.DoEvents();
            };

            exporter.OnWarning += (warning, rank) => CreateWarningMessage(warning, rank);

            exporter.OnError += (error, rank) => CreateErrorMessage(error, rank);

            exporter.OnMessage += (message, color, rank, emphasis) => CreateMessage(message, color, rank, emphasis);

            exporter.OnVerbose += (message, color, rank, emphasis) =>
            {
                try
                {
                    currentNode = CreateTreeNode(rank, message, color);

                    if (emphasis)
                    {
                        currentNode.EnsureVisible();
                    }
                }
                catch
                {
                    //do nothing
                }
                Application.DoEvents();
            };

            // disable UI controls during export.
            butExport.Enabled = false;
            butExportAndRun.Enabled = false;
            butMultiExport.Enabled = false;
            butCancel.Enabled = true;
            if (butFixPhasingNames != null)
            {
                butFixPhasingNames.Enabled = false;
            }
            exportBusy = true;
            RefreshUpdateButtons();

            // switch to the log tab.
            exporterTabControl.SelectTab(logTabPage.Name);

            bool success = true;
            string logOutputPath = null;
            try
            {
                string modelAbsolutePath = multiExport ? exportItem.ExportFilePathAbsolute : txtModelPath.Text;
                logOutputPath = modelAbsolutePath;
                string textureExportPath = multiExport ? exportItem.ExportTexturesesFolderAbsolute : txtTexturesPath.Text;

                var scaleFactorParsed = 1.0f;
                var textureQualityParsed = 100L;
                try
                {
                    scaleFactorParsed = float.Parse(txtScaleFactor.Text);
                }
                catch (Exception)
                {
                    throw new InvalidDataException(String.Format("Invalid Scale Factor value: {0}", txtScaleFactor.Text));
                }
                try
                {
                    textureQualityParsed = long.Parse(txtQuality.Text);
                }
                catch (Exception)
                {
                    throw new InvalidDataException(String.Format("Invalid Texture Quality value: {0}", txtScaleFactor.Text));
                }

                MaxExportParameters exportParameters = new MaxExportParameters
                {
                    outputPath = modelAbsolutePath,
                    textureFolder = textureExportPath,
                    outputFormat = comboOutputFormat.SelectedItem.ToString(),
                    scaleFactor = scaleFactorParsed,
                    exportTextures = chkExportTextures.Checked,
                    writeTextures = chkWriteTextures.Checked,
                    overwriteTextures = chkOverwriteTextures.Checked,
                    exportHiddenObjects = chkHidden.Checked,
                    exportOnlySelected = chkOnlySelected.Checked,
                    generateManifest = chkManifest.Checked,
                    autoSaveSceneFile = chkAutoSave.Checked,
                    exportTangents = chkExportTangents.Checked,
                    exportMorphTangents = chkExportMorphTangents.Checked,
                    exportMorphNormals = chkExportMorphNormals.Checked,
                    exportMorphTargetsNames = chkExportMorphNames.Checked,
                    txtQuality = textureQualityParsed,
                    mergeAO = chkMergeAO.Checked,
                    bakeAnimationType = (BakeAnimationType)cmbBakeAnimationOptions.SelectedIndex,
                    dracoCompression = chkDracoCompression.Enabled && chkDracoCompression.Checked,
                    enableKHRLightsPunctual = chkKHRLightsPunctual.Checked,
                    enableKHRTextureTransform = chkKHRTextureTransform.Checked,
                    enableKHRMaterialsUnlit = chkKHRMaterialsUnlit.Checked,
                    exportMaterials = chkExportMaterials.Checked,
                    exportAnimations = chkExportAnimations.Checked,
                    exportAnimationsOnly = chkExportAnimationsOnly.Checked,
                    optimizeAnimations = !chkDoNotOptimizeAnimations.Checked,
                    animgroupExportNonAnimated = chkAnimgroupExportNonAnimated.Checked,
                    exportNode = exportItem?.Node,
                    exportKeepNodePosition = exportItem?.KeepPosition??false,
                    exportLayers = exportItem?.Layers,
                    pbrNoLight = chkNoAutoLight.Checked,
                    pbrFull = chkFullPBR.Checked,
                    pbrEnvironment = txtEnvironmentName.Text,
                    usePreExportProcess = chkUsePreExportProces.Checked,
                    flattenScene = chkFlatten.Checked,
                    mergeContainersAndXRef = chkMrgContainersAndXref.Checked,
                    useMultiExporter = multiExport,
                    tryToReuseOpaqueAndBlendTexture = chkTryReuseTexture.Checked,
                    useClone = chkUseClone.Checked,
                    plannerNamingCheck = chkPlannerNaming.Checked,
                    plannerNamingStrict = chkPlannerNamingStrict.Checked
                };

                if (exportParameters.dracoCompression)
                {
                    exportParameters.dracoParams = new DracoParameters()
                    {
                        compressionLevel = (int)dracoUserControl.CompressionLevelNumericUpDown.Value,
                        quantizePositionBits = dracoUserControl.QPositionTrackBar.Value,
                        quantizeNormalBits = dracoUserControl.QNormalTrackBar.Value,
                        quantizeTexcoordBits = dracoUserControl.QTexcoordTrackBar.Value,
                        quantizeColorBits = dracoUserControl.QColorTrackBar.Value,
                        quantizeGenericBits = dracoUserControl.QGenericTrackBar.Value,
                        unifiedQuantization = dracoUserControl.UnifiedCheckBox.Checked
                    }; 
                }

                exporter.callerForm = this;

                ExportRunning = true;
                exporter.Export(exportParameters);
            }
            catch (OperationCanceledException)
            {
                progressBar.Value = 0;
                success = false;
                ScriptsUtilities.ExecuteMaxScriptCommand(@"global BabylonExporterStatus = ""Available""");
            }
            catch (Exception ex)
            {
                IUTF8Str operationStatus = GlobalInterface.Instance.UTF8Str.Create("BabylonExportAborted");
                Loader.Global.BroadcastNotification(SystemNotificationCode.PreExport, operationStatus);

                currentNode = CreateTreeNode(0, "Export cancelled: " + ex.Message, Color.Red);
                currentNode = CreateTreeNode(1, ex.ToString(), Color.Red);
                currentNode.EnsureVisible();

                progressBar.Value = 0;
                success = false;
                ScriptsUtilities.ExecuteMaxScriptCommand(@"global BabylonExporterStatus = Available");
            }
            finally
            {
                // Even when reporting the failure fails, so the phasing name fixer is not locked out.
                ExportRunning = false;
            }

            SaveExportLog(logOutputPath);

            butCancel.Enabled = false;
            butExport.Enabled = true;
            butMultiExport.Enabled = true;
            butExportAndRun.Enabled = WebServer.IsSupported;
            if (butFixPhasingNames != null)
            {
                butFixPhasingNames.Enabled = true;
            }
            exportBusy = false;
            RefreshUpdateButtons();

            BringToFront();

            //re-store layer visibility status
            if (exportItem.Layers != null)
            {
                foreach (IILayer layer in exportItem.Layers)
                {
                    List<IILayer> treeLayers = layer.LayerTree().ToList();
                    treeLayers.Add(layer);
                    foreach (IILayer l in treeLayers)
                    {
                        bool exist;
                        layerState.TryGetValue(l, out exist);
                        if (exist)
                        {
                            l.Hide(layerState[l], false);
                        }
                    }
                }
            }

            return success;
        }

        void CreateWarningMessage(string warning, int rank)
        {
            try
            {
                currentNode = CreateTreeNode(rank, warning, Color.DarkOrange);
                currentNode.EnsureVisible();
            }
            catch
            {
                //do nothing
            }
            Application.DoEvents();
        }

        void CreateErrorMessage(string error, int rank)
        {
            try
            {
                currentNode = CreateTreeNode(rank, error, Color.Red);
                currentNode.EnsureVisible();
            }
            catch
            {
                //do nothing
            }
            Application.DoEvents();
        }

        void CreateMessage(string message, Color color, int rank, bool emphasis)
        {
            try
            {
                currentNode = CreateTreeNode(rank, message, color);

                if (emphasis)
                {
                    currentNode.EnsureVisible();
                }
            }
            catch
            {
                //do nothing
            }
            Application.DoEvents();
        }

        private TreeNode CreateTreeNode(int rank, string text, Color color)
        {
            TreeNode newNode = null;

            Invoke(new Action(() =>
            {
                newNode = new TreeNode(text) { ForeColor = color };
                if (rank < 0 || rank > currentRank + 1)
                {
                    rank = 0;
                    logTreeView.Nodes.Add(new TreeNode("Invalid rank passed to CreateTreeNode (through RaiseMessage, RaiseWarning or RaiseError)!") { ForeColor = Color.DarkOrange });
                }
                if (rank == 0)
                {
                    logTreeView.Nodes.Add(newNode);
                }
                else if (rank == currentRank + 1)
                {
                    currentNode.Nodes.Add(newNode);
                }
                else
                {
                    var parentNode = currentNode;
                    while (currentRank != rank - 1)
                    {
                        parentNode = parentNode.Parent;
                        currentRank--;
                    }
                    parentNode.Nodes.Add(newNode);
                }

                currentRank = rank;
            }));

            return newNode;
        }

        private void ExporterForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (exporter != null)
            {
                exporter.IsCancelled = true;
            }
            try
            {
                updateCancel.Cancel(); // stops a running check or download
                StopMaxCloseWatch();
            }
            catch (Exception)
            {
                // Nothing to stop.
            }
            babylonExportAction.Close();
        }

        private void txtFilename_TextChanged(object sender, EventArgs e)
        {
            butExport.Enabled = !string.IsNullOrEmpty(txtModelPath.Text.Trim());
            butExportAndRun.Enabled = butExport.Enabled && WebServer.IsSupported;
        }

        private void butCancel_Click(object sender, EventArgs e)
        {
            exporter.IsCancelled = true;
        }

        private void butCopyToClipboard_Click(object sender, EventArgs e)
        {
            // The whole log, collapsed branches included.
            var textString = logTreeView.ToPrettyString(false);
            if (textString != string.Empty)
            {
                System.Windows.Forms.Clipboard.SetText(textString);
            }
        }

        /// <summary>UrbanCGI fork: keeps the Log tab as a text file next to the exported model, so it can be shared.</summary>
        private void SaveExportLog(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                return;
            }
            string error;
            var logPath = ExportLog.Write(outputPath, logTreeView.ToPrettyString(false), out error);
            if (logPath != null)
            {
                currentNode = CreateTreeNode(0, "Log saved to " + logPath, Color.Gray);
            }
            else
            {
                currentNode = CreateTreeNode(0, "Could not save the log next to the export: " + error, Color.DarkOrange);
            }
        }

        /// <summary>
        /// UrbanCGI fork: the "Fix phasing names..." button, added here rather than in the designer file. It goes
        /// beside the Planner naming options, at the first place that overlaps no other control, measured once the
        /// window is laid out; failing both, under everything else in the options panel.
        /// </summary>
        private void AddFixPhasingNamesButton()
        {
            if (butFixPhasingNames != null)
            {
                return;
            }
            try
            {
                var panel = exportOptionsScrollPanel;
                var button = new Button
                {
                    Name = "butFixPhasingNames",
                    Text = "Fix phasing names\u2026",
                    FlatStyle = FlatStyle.Flat,
                    UseVisualStyleBackColor = true,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    TabIndex = chkPlannerNamingStrict.TabIndex + 1
                };
                button.Click += butFixPhasingNames_Click;
                button.KeyDown += ExporterForm_KeyDown;
                var others = panel.Controls.Cast<Control>().Select(c => c.Bounds).ToList();
                panel.Controls.Add(button);
                button.MinimumSize = new System.Drawing.Size(0, butExport.Height);
                var size = button.GetPreferredSize(System.Drawing.Size.Empty);
                size = new System.Drawing.Size(Math.Max(size.Width, button.MinimumSize.Width), Math.Max(size.Height, button.MinimumSize.Height));

                int gap = Math.Max(4, chkPlannerNaming.Height / 2);
                int rightEdge = panel.ClientSize.Width - panel.Padding.Right;
                Func<System.Drawing.Point, bool> fits = place =>
                {
                    var bounds = new System.Drawing.Rectangle(place, size);
                    return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= rightEdge
                        && !others.Any(o => System.Drawing.Rectangle.Inflate(o, gap / 2, gap / 2).IntersectsWith(bounds));
                };
                var candidates = new[]
                {
                    // Beside the two Planner check boxes, centred on them.
                    new System.Drawing.Point(Math.Max(chkPlannerNaming.Right, chkPlannerNamingStrict.Right) + 2 * gap,
                        (chkPlannerNaming.Top + chkPlannerNamingStrict.Bottom - size.Height) / 2),
                    // Under them.
                    new System.Drawing.Point(chkPlannerNamingStrict.Left, chkPlannerNamingStrict.Bottom + gap)
                };
                var location = new System.Drawing.Point(chkPlannerNaming.Left, (others.Count > 0 ? others.Max(o => o.Bottom) : 0) + gap);
                foreach (var candidate in candidates)
                {
                    if (fits(candidate))
                    {
                        location = candidate;
                        break;
                    }
                }
                button.Location = location;
                butFixPhasingNames = button;
            }
            catch (Exception)
            {
                // The exporter works without the button; the fixer is still reachable from MAXScript.
            }
        }

        private void butFixPhasingNames_Click(object sender, EventArgs e)
        {
            try
            {
                PhasingFixForm.ShowForScene();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fix phasing names could not open: " + ex.Message, "Fix phasing names", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// UrbanCGI fork: the version and update block, added here rather than in the designer file. It sits in the
        /// empty space right of the banner and above the tabs, against the right edge (and stays there when the
        /// window is resized): the version, a status line, "Check for updates" and "Update", and the download
        /// progress. Sizes are measured from the laid-out window, so they follow the display scaling; when the three
        /// text lines do not fit above the tabs, the progress shares the status line.
        /// </summary>
        private void AddUpdatePanel()
        {
            if (updatePanel != null)
            {
                return;
            }
            try
            {
                int gap = Math.Max(2, Font.Height / 5);
                int lineHeight = Font.Height + 2;
                updateToolTip = components != null ? new ToolTip(components) : new ToolTip();

                butCheckForUpdates = MakeUpdateButton("butCheckForUpdates", "Check for updates", butCheckForUpdates_Click);
                butUpdate = MakeUpdateButton("butUpdate", "Update", butUpdate_Click);
                updateToolTip.SetToolTip(butCheckForUpdates, "Ask GitHub whether a newer release of the exporter exists.");
                updateToolTip.SetToolTip(butUpdate, "Download the latest release, close 3ds Max, install it and open 3ds Max again.");
                var checkSize = butCheckForUpdates.GetPreferredSize(System.Drawing.Size.Empty);
                var updateSize = butUpdate.GetPreferredSize(System.Drawing.Size.Empty);
                int buttonHeight = Math.Max(butExport.Height, Math.Max(checkSize.Height, updateSize.Height));
                butCheckForUpdates.Size = new System.Drawing.Size(checkSize.Width, buttonHeight);
                butUpdate.Size = new System.Drawing.Size(Math.Max(updateSize.Width, butExport.Width), buttonHeight);

                int width = Math.Max(butCheckForUpdates.Width + gap + butUpdate.Width,
                    TextRenderer.MeasureText("Update available: v20261231.9999", Font).Width + gap);
                int top = pictureBox1.Top;
                int room = exporterTabControl.Top - gap - top;
                bool progressLine = 3 * (lineHeight + gap) + buttonHeight <= room;

                lblUpdateVersion = MakeUpdateLabel("lblUpdateVersion", width, lineHeight);
                lblUpdateVersion.Text = ReleaseInfo.VersionText;
                lblUpdateStatus = MakeUpdateLabel("lblUpdateStatus", width, lineHeight);
                lblUpdateStatus.Location = new System.Drawing.Point(0, lineHeight + gap);
                int buttonsTop = 2 * (lineHeight + gap);
                butCheckForUpdates.Location = new System.Drawing.Point(0, buttonsTop);
                butUpdate.Location = new System.Drawing.Point(butCheckForUpdates.Right + gap, buttonsTop);
                int height = buttonsTop + buttonHeight;
                if (progressLine)
                {
                    lblUpdateProgress = MakeUpdateLabel("lblUpdateProgress", width, lineHeight);
                    lblUpdateProgress.Location = new System.Drawing.Point(0, height + gap);
                    lblUpdateProgress.ForeColor = Color.DimGray;
                    height += gap + lineHeight;
                }

                var panel = new Panel
                {
                    Name = "updatePanel",
                    Size = new System.Drawing.Size(width, height),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    TabIndex = pictureBox1.TabIndex + 1
                };
                panel.Controls.Add(lblUpdateVersion);
                panel.Controls.Add(lblUpdateStatus);
                panel.Controls.Add(butCheckForUpdates);
                panel.Controls.Add(butUpdate);
                if (lblUpdateProgress != null)
                {
                    panel.Controls.Add(lblUpdateProgress);
                }
                int rightMargin = Math.Max(gap, ClientSize.Width - butClose.Right);
                panel.Location = new System.Drawing.Point(Math.Max(0, ClientSize.Width - rightMargin - width), top);
                Controls.Add(panel);
                panel.BringToFront();
                updatePanel = panel;
                RefreshUpdateButtons();
            }
            catch (Exception)
            {
                // The exporter works without it; the installer still updates.
                butCheckForUpdates = null;
                butUpdate = null;
                lblUpdateStatus = null;
                lblUpdateProgress = null;
            }
        }

        private Button MakeUpdateButton(string name, string text, EventHandler click)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = true,
                AutoSize = false
            };
            button.Click += click;
            button.KeyDown += ExporterForm_KeyDown;
            return button;
        }

        private static Label MakeUpdateLabel(string name, int width, int height)
        {
            return new Label
            {
                Name = name,
                AutoSize = false,
                AutoEllipsis = true,
                Size = new System.Drawing.Size(width, height),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
        }

        private void ShowUpdateStatus(string text, Color color, string detail = null)
        {
            if (lblUpdateStatus == null)
            {
                return;
            }
            lblUpdateStatus.Text = text ?? string.Empty;
            lblUpdateStatus.ForeColor = color;
            updateToolTip.SetToolTip(lblUpdateStatus, string.IsNullOrEmpty(detail) ? null : detail);
        }

        private void ShowUpdateProgress(string text)
        {
            if (lblUpdateProgress != null)
            {
                lblUpdateProgress.Text = text ?? string.Empty;
            }
            else if (!string.IsNullOrEmpty(text))
            {
                ShowUpdateStatus(text, Color.DimGray);
            }
        }

        private void RefreshUpdateButtons()
        {
            if (butCheckForUpdates == null || butUpdate == null)
            {
                return;
            }
            butCheckForUpdates.Enabled = !updateBusy && !exportBusy;
            butUpdate.Enabled = !updateBusy && !exportBusy && UpdateCheckLogic.CanUpdate(updateVerdict);
        }

        /// <summary>
        /// Checks for a newer release off the UI thread. From the window opening: at most once a day (the remembered
        /// result otherwise), never for a development build, and silent when it fails. From the button: always, with
        /// a short message when it fails.
        /// </summary>
        private void StartUpdateCheck(bool fromButton)
        {
            if (updateBusy || butCheckForUpdates == null)
            {
                return;
            }
            try
            {
                updateBusy = true;
                RefreshUpdateButtons();
                if (fromButton)
                {
                    ShowUpdateStatus("Checking\u2026", Color.DimGray);
                }
                var cancel = updateCancel.Token;
                var check = fromButton
                    ? Task.Run(() => UpdateChecker.CheckNowAsync(cancel))
                    : Task.Run(() => UpdateChecker.CheckOnWindowOpenAsync(cancel));
                check.ContinueWith(done => OnUiThread(() =>
                {
                    updateBusy = false;
                    ShowCheckOutcome(done.Status == TaskStatus.RanToCompletion ? done.Result : null, fromButton);
                    RefreshUpdateButtons();
                }), TaskScheduler.Default);
            }
            catch (Exception)
            {
                updateBusy = false;
                RefreshUpdateButtons();
            }
        }

        private void ShowCheckOutcome(UpdateCheckOutcome outcome, bool fromButton)
        {
            if (outcome == null || outcome.Failure != UpdateCheckFailure.None || outcome.LatestTag == null)
            {
                if (fromButton)
                {
                    var failure = outcome != null ? outcome.Failure : UpdateCheckFailure.Offline;
                    ShowUpdateStatus(UpdateCheckLogic.FailureText(failure == UpdateCheckFailure.None ? UpdateCheckFailure.BadAnswer : failure),
                        Color.Firebrick, outcome != null ? outcome.Detail : null);
                }
                return;
            }
            updateVerdict = UpdateCheckLogic.Compare(ReleaseInfo.Tag, outcome.LatestTag);
            if (outcome.Release != null && UpdateCheckLogic.CanUpdate(updateVerdict))
            {
                // A release without this 3ds Max's package (or installer) is not offered: Update could only fail.
                string detail;
                var unavailable = UpdatePackages.UnavailableStatus(outcome.Release, ReleaseInfo.MaxYear, out detail);
                if (unavailable != null)
                {
                    updateVerdict = UpdateVerdict.Unknown;
                    ShowUpdateStatus(unavailable, Color.Firebrick, detail);
                    return;
                }
            }
            ShowUpdateStatus(UpdateCheckLogic.StatusText(updateVerdict, outcome.LatestTag),
                updateVerdict == UpdateVerdict.UpdateAvailable ? System.Drawing.SystemColors.HotTrack : System.Drawing.SystemColors.ControlText);
        }

        private void butCheckForUpdates_Click(object sender, EventArgs e)
        {
            StartUpdateCheck(true);
        }

        /// <summary>
        /// The one-click update: download (off the UI thread), then ask, let 3ds Max offer to save, start the
        /// installer and close 3ds Max (see UpdateRunner). A download from an earlier attempt in this window is used
        /// again, e.g. after Windows' permission prompt was declined.
        /// </summary>
        private void butUpdate_Click(object sender, EventArgs e)
        {
            try
            {
                if (updateBusy)
                {
                    return;
                }
                if (exportBusy || ExportRunning)
                {
                    MessageBox.Show(this, "Wait for the export to finish, then update.", UpdateRunner.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                int maxYear = ReleaseInfo.MaxYear;
                if (maxYear <= 0)
                {
                    MessageBox.Show(this, "This build of the exporter does not know which 3ds Max it is for. Update with the installer from "
                                          + UpdateChecker.ReleasesPageUrl + ".", UpdateRunner.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (preparedUpdate != null && File.Exists(preparedUpdate.PackagePath) && File.Exists(preparedUpdate.InstallerPath))
                {
                    InstallPreparedUpdate(preparedUpdate);
                    return;
                }

                updateBusy = true;
                RefreshUpdateButtons();
                ShowUpdateStatus("Preparing the update\u2026", Color.DimGray);
                var cancel = updateCancel.Token;
                Action<string> progress = text => OnUiThread(() => ShowUpdateProgress(text));
                Task.Run(() => UpdateRunner.PrepareAsync(maxYear, progress, cancel))
                    .ContinueWith(done => OnUiThread(() => FinishUpdateDownload(done)), TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                updateBusy = false;
                RefreshUpdateButtons();
                ShowUpdateStatus("Update failed", Color.Firebrick, ex.Message);
            }
        }

        private void FinishUpdateDownload(Task<PreparedUpdate> done)
        {
            updateBusy = false;
            ShowUpdateProgress(string.Empty);
            if (done.Status != TaskStatus.RanToCompletion)
            {
                if (updateCancel.IsCancellationRequested)
                {
                    return;
                }
                var notNeeded = UpdateRunner.NotNeeded(done.Exception);
                if (notNeeded != null)
                {
                    // The remembered "Update available" was out of date: nothing to do, and nothing went wrong.
                    preparedUpdate = null;
                    updateVerdict = UpdateVerdict.UpToDate;
                    ShowUpdateStatus(UpdateCheckLogic.StatusText(updateVerdict, notNeeded.LatestTag), System.Drawing.SystemColors.ControlText);
                    RefreshUpdateButtons();
                    return;
                }
                var reason = done.Exception != null ? UpdateRunner.Describe(done.Exception) : "The download was stopped.";
                ShowUpdateStatus("Update failed", Color.Firebrick, reason);
                RefreshUpdateButtons();
                MessageBox.Show(this, reason + "\n\nThe exporter can still be updated with the installer from " + UpdateChecker.ReleasesPageUrl + ".",
                    UpdateRunner.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            preparedUpdate = done.Result;
            updateVerdict = UpdateCheckLogic.Compare(ReleaseInfo.Tag, preparedUpdate.Tag);
            ShowUpdateStatus(UpdateCheckLogic.StatusText(updateVerdict, preparedUpdate.Tag), System.Drawing.SystemColors.HotTrack);
            RefreshUpdateButtons();
            if (exportBusy || ExportRunning)
            {
                MessageBox.Show(this, "An export started while the update downloaded. Press Update again once it has finished.",
                    UpdateRunner.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            InstallPreparedUpdate(preparedUpdate);
        }

        private void InstallPreparedUpdate(PreparedUpdate update)
        {
            // The whole window waits while 3ds Max asks its questions. Its save prompt and Save As dialog belong to
            // 3ds Max's main window and leave this window usable otherwise: a second Update would start a second
            // installer from inside the first, and Close would dispose the window half-way through.
            updateBusy = true;
            RefreshUpdateButtons();
            Enabled = false;
            string message = null;
            var launch = UpdateLaunch.Failed;
            try
            {
                bool discardChanges;
                launch = UpdateRunner.StartInstaller(this, update, out message, out discardChanges);
                if (launch == UpdateLaunch.Started)
                {
                    // Stays disabled: the installer waits for this 3ds Max to exit, and the window may be gone once
                    // QuitMax returns.
                    ShowUpdateStatus("Installing " + update.Tag + "\u2026", Color.DimGray);
                    var error = UpdateRunner.QuitMax(discardChanges);
                    if (IsDisposed)
                    {
                        return;
                    }
                    if (error != null)
                    {
                        OnMaxDidNotClose(error);
                    }
                    else
                    {
                        WatchForMaxStillOpen();
                    }
                    return;
                }
            }
            finally
            {
                if (launch != UpdateLaunch.Started && !IsDisposed)
                {
                    Enabled = true;
                    updateBusy = false;
                }
            }

            if (IsDisposed)
            {
                return;
            }
            switch (launch)
            {
                case UpdateLaunch.Cancelled:
                    ShowUpdateStatus(message ?? "Update cancelled", System.Drawing.SystemColors.ControlText);
                    RefreshUpdateButtons();
                    break;
                default:
                    ShowUpdateStatus("Update failed", Color.Firebrick, message);
                    RefreshUpdateButtons();
                    MessageBox.Show(this, (message ?? "The update could not start.") + "\n\nThe exporter can still be updated with the installer from "
                                          + UpdateChecker.ReleasesPageUrl + ".", UpdateRunner.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }

        /// <summary>
        /// After 3ds Max was asked to close for the update: if this window is still here after <see cref="MaxCloseWait"/>,
        /// 3ds Max did not close (a prompt was cancelled, or something kept it open).
        /// </summary>
        private void WatchForMaxStillOpen()
        {
            StopMaxCloseWatch();
            var timer = components != null ? new System.Windows.Forms.Timer(components) : new System.Windows.Forms.Timer();
            timer.Interval = (int)MaxCloseWait.TotalMilliseconds;
            timer.Tick += (sender, e) =>
            {
                StopMaxCloseWatch();
                if (!IsDisposed)
                {
                    OnMaxDidNotClose(null);
                }
            };
            maxCloseWatch = timer;
            timer.Start();
        }

        private void StopMaxCloseWatch()
        {
            if (maxCloseWatch == null)
            {
                return;
            }
            var timer = maxCloseWatch;
            maxCloseWatch = null;
            timer.Stop();
            timer.Dispose();
        }

        /// <summary>
        /// 3ds Max is still open although the installer was started and is waiting for it to close. The window works
        /// again and says so (no message box once 3ds Max has begun to exit, so the exit is never held up).
        /// </summary>
        private void OnMaxDidNotClose(string error)
        {
            Enabled = true;
            updateBusy = false;
            const string text = "3ds Max did not close. The installer is waiting: close 3ds Max to finish the update, or press Cancel in the installer window.";
            ShowUpdateStatus("3ds Max did not close", Color.Firebrick, error != null ? text + " (" + error + ")" : text);
            RefreshUpdateButtons();
            if (!UpdateRunner.MaxIsShuttingDown)
            {
                MessageBox.Show(this, text + (error != null ? "\n\n(" + error + ")" : ""), UpdateRunner.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Runs <paramref name="action"/> on this window's thread, later; nothing when the window has closed.</summary>
        private void OnUiThread(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed)
                    {
                        return;
                    }
                    try
                    {
                        action();
                    }
                    catch (Exception)
                    {
                        // Never into 3ds Max.
                    }
                });
            }
            catch (Exception)
            {
                // The window closed in between.
            }
        }

        private void ExporterForm_Activated(object sender, EventArgs e)
        {
            Loader.Global.DisableAccelerators();
        }

        private void ExporterForm_Deactivate(object sender, EventArgs e)
        {
            Loader.Global.EnableAccelerators();
        }

        private async void butExportAndRun_Click(object sender, EventArgs e)
        {
            try
            {
                if (chkUsePreExportProces.Checked)
                {
                    Loader.Core.FileHold();
                }

                if (await DoExport(singleExportItem))
                {
                    WebServer.SceneFilename = Path.GetFileName(txtModelPath.Text);
                    WebServer.SceneFolder = Path.GetDirectoryName(txtModelPath.Text);

                    var startInfo = new ProcessStartInfo(WebServer.url + WebServer.SceneFilename)
                    {
                        // defaults to false on netcore
                        UseShellExecute=true,
                        // just a safety precaution
                        WorkingDirectory= WebServer.SceneFolder
                    };
                    Process.Start(startInfo);
                    WindowState = FormWindowState.Minimized;
                }
            }
            catch { }
            finally
            {
                if (chkUsePreExportProces.Checked && !chkApplyPreprocessToScene.Checked)
                {
                    Loader.Core.SetQuietMode(true);
                    Loader.Core.FileFetch();
                    Loader.Core.SetQuietMode(false);
                }
            }
        }

        private void butClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void comboOutputFormat_SelectedIndexChanged(object sender, EventArgs e)
        {
            var outputFormat = comboOutputFormat.SelectedItem.ToString();
            switch (outputFormat)
            {
                case "babylon":
                case "binary babylon":
                    this.saveFileDialog.DefaultExt = "babylon";
                    this.saveFileDialog.Filter = "Babylon files|*.babylon";
                    chkDracoCompression.Checked = false;
                    chkDracoCompression.Enabled = false;
                    chkWriteTextures.Enabled = true;
                    chkOverwriteTextures.Enabled = true;
                    txtTexturesPath.Text = string.Empty;
                    txtTexturesPath.Enabled = false;
                    textureLabel.Enabled = false;
                    btnTxtBrowse.Enabled = false;
                    chkFullPBR.Enabled = true;
                    btnEnvBrowse.Enabled = true;
                    txtEnvironmentName.Enabled = true;
                    chkKHRMaterialsUnlit.Enabled = false;
                    chkKHRLightsPunctual.Enabled = false;
                    chkKHRTextureTransform.Enabled = false;
                    break;
                case "gltf":
                    this.saveFileDialog.DefaultExt = "gltf";
                    this.saveFileDialog.Filter = "glTF files|*.gltf";
                    chkDracoCompression.Enabled = gltfPipelineInstalled;
                    chkWriteTextures.Enabled = true;
                    chkOverwriteTextures.Enabled = true;
                    txtTexturesPath.Enabled = true;
                    textureLabel.Enabled = true;
                    btnTxtBrowse.Enabled = true;
                    chkFullPBR.Enabled = false;
                    chkFullPBR.Checked = false;
                    btnEnvBrowse.Enabled = false;
                    txtEnvironmentName.Enabled = false;
                    txtEnvironmentName.Text = string.Empty;
                    chkKHRMaterialsUnlit.Enabled = true;
                    chkKHRLightsPunctual.Enabled = true;
                    chkKHRTextureTransform.Enabled = true;
                    break;
                case "glb":
                    this.saveFileDialog.DefaultExt = "glb";
                    this.saveFileDialog.Filter = "glb files|*.glb";
                    chkDracoCompression.Enabled = gltfPipelineInstalled;
                    chkWriteTextures.Checked = true;
                    chkWriteTextures.Enabled = false;
                    chkOverwriteTextures.Checked = true;
                    chkOverwriteTextures.Enabled = false;
                    txtTexturesPath.Text = string.Empty;
                    txtTexturesPath.Enabled = false;
                    textureLabel.Enabled = false;
                    btnTxtBrowse.Enabled = false;
                    chkFullPBR.Enabled = false;
                    chkFullPBR.Checked = false;
                    btnEnvBrowse.Enabled = false;
                    txtEnvironmentName.Enabled = false;
                    txtEnvironmentName.Text = string.Empty;
                    chkKHRMaterialsUnlit.Enabled = true;
                    chkKHRLightsPunctual.Enabled = true;
                    chkKHRTextureTransform.Enabled = true;
                    break;
            }

            dracoGroupBox.Enabled = chkDracoCompression.Enabled && chkDracoCompression.Checked;

            string newModelPath = Path.ChangeExtension(txtModelPath.Text, this.saveFileDialog.DefaultExt);
            this.txtModelPath.MaxPath(newModelPath);
        }

        /// <summary>
        /// Show a toolTip when the mouse is over the chkDracoCompression checkBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        bool IsShown = false;
        private void exportOptionsScrollPanel_MouseMove(object sender, MouseEventArgs e)
        {
            Control ctrl = exportOptionsScrollPanel.GetChildAtPoint(e.Location);

            if (ctrl != null)
            {
                if (ctrl == chkDracoCompression && !ctrl.Enabled && !IsShown)
                {
                    string tip = "For glTF and glb export only.\nNode.js and gltf-pipeline modules are required.";
                    toolTipDracoCompression.Show(tip, chkDracoCompression, chkDracoCompression.Width / 2, chkDracoCompression.Height / 2);
                    IsShown = true;
                }
            }
            else
            {
                toolTipDracoCompression.Hide(chkDracoCompression);
                IsShown = false;
            }
        }

        /// <summary>
        /// Handle the tab navigation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExporterForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab)
            {
                if (e.Modifiers == Keys.Shift)
                    ProcessTabKey(false);
                else
                    ProcessTabKey(true);
            }
        }

        private async void butMultiExport_Click(object sender, EventArgs e)
        {
            string outputFileExt;
            outputFileExt = comboOutputFormat.SelectedItem.ToString();
            if (outputFileExt.Contains("binary babylon"))
                outputFileExt = "babylon";

            ExportItemList exportItemList = new ExportItemList(outputFileExt);

            exportItemList.LoadFromData();

            int numLoadedItems = exportItemList.Count;

            if (ModifierKeys == Keys.Shift)
            {
                MultiExportForm form = new MultiExportForm(exportItemList);
                form.ShowDialog(this);
            }
            else if (numLoadedItems > 0)
            {
                try
                {
                    if (chkUsePreExportProces.Checked)
                    {
                        Loader.Core.FileHold();
                    }
                    await DoExport(exportItemList);
                }
                catch { }
                finally
                {
                    if (chkUsePreExportProces.Checked && !chkApplyPreprocessToScene.Checked)
                    {
                        Loader.Core.SetQuietMode(true);
                        Loader.Core.FileFetch();
                        Loader.Core.SetQuietMode(false);
                    }
                }
            }
        }

        private void chkUsePreExportProces_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkUsePreExportProces.Checked)
            {
                chkMrgContainersAndXref.Enabled = false;
                cmbBakeAnimationOptions.Enabled = false;
                lblBakeAnimation.Enabled = false;
                chkApplyPreprocessToScene.Enabled = false;
            }
            else
            {
                chkMrgContainersAndXref.Enabled = true;
                cmbBakeAnimationOptions.Enabled = true;
                lblBakeAnimation.Enabled = true;
                chkApplyPreprocessToScene.Enabled = true;
            }
        }

        private void chkDracoCompression_CheckedChanged(object sender, EventArgs e)
        {
            dracoGroupBox.Enabled = chkDracoCompression.Checked;
        }
    }
}
