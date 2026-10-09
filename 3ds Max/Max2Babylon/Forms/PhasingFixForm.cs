using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the review window of the phasing name fixer. Lists what <see cref="PhasingNameFixer"/> would
    /// rename or move in the scene, lets the modeller give a layer another stage and edit the project's word fixes
    /// (both re-plan at once), saves the list as CSV, and applies it in one go. Nothing is renamed without it.
    /// Built in code rather than with the designer; parented to the 3ds Max window like the exporter.
    /// </summary>
    internal sealed class PhasingFixForm : Form
    {
        private const int StatusColumn = 0;
        private const int TypeColumn = 1;
        private const int StageColumn = 2;
        private const int LayerColumn = 3;
        private const int CurrentColumn = 4;
        private const int CorrectedColumn = 5;
        private const int WhyColumn = 6;
        private const int MessageLines = 25;
        private static readonly Color CheckBack = Color.FromArgb(255, 243, 205);

        private static PhasingFixForm current;
        private static NativeWindow maxWindow;

        private readonly Label summaryLabel = new Label();
        private readonly Label detailLabel = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly CheckBox everyLayerBox = new CheckBox();
        private readonly CheckBox holdBox = new CheckBox();
        private readonly Button wordFixesButton = new Button();
        private readonly Button saveListButton = new Button();
        private readonly Button refreshButton = new Button();
        private readonly Button applyButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly Font summaryFont;
        private Font boldFont;

        // The scene changes the window follows: it reads the new scene, with its own word fixes.
        private static readonly Autodesk.Max.SystemNotificationCode[] SceneChanges =
        {
            Autodesk.Max.SystemNotificationCode.FilePostOpen,
            Autodesk.Max.SystemNotificationCode.SystemPostNew,
            Autodesk.Max.SystemNotificationCode.SystemPostReset
        };

        // Stages given in this window, by layer node handle; they live only while the window is open.
        private readonly Dictionary<long, StageOverride> stageOverrides = new Dictionary<long, StageOverride>();
        // Fixes applied from this window that left something undone: the Max layer changes the scene still lacks
        // stay in the list as Check rows (a plan cannot see them again) until the scene changes.
        private readonly List<PhasingFixResult> unfinished = new List<PhasingFixResult>();
        private List<PhasingWordFix> wordFixes;
        private bool wordFixesSaved;
        // The scene's own word-fix list when it was last read (see SceneWordFixes), to notice another scene.
        private string loadedWordFixes;
        private PhasingFixPlan plan;
        private string readError;
        // Kept in a field so it lives as long as the notifications it is registered for.
        private Autodesk.Max.GlobalDelegates.Delegate5 sceneChanged;

        private sealed class StageOverride
        {
            public int Stage;
            /// <summary>The layer's name when the stage was given: a different name means it is not the same layer.</summary>
            public string LayerName;
        }

        private sealed class RowTag
        {
            public PhasingFixRow Row;
            public PhasingLayerInfo Layer;
        }

        /// <summary>Opens the window (or brings it forward and reads the scene again).</summary>
        public static void ShowForScene()
        {
            if (current == null || current.IsDisposed)
            {
                current = new PhasingFixForm();
            }
            else
            {
                current.Reload();
            }
            if (maxWindow == null)
            {
                maxWindow = new NativeWindow();
            }
            if (maxWindow.Handle == IntPtr.Zero)
            {
                maxWindow.AssignHandle(Loader.Core.MAXHWnd);
            }
            if (!current.Visible)
            {
                current.Show(maxWindow);
            }
            current.WindowState = FormWindowState.Normal;
            current.BringToFront();
            current.Activate();
        }

        private PhasingFixForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = "Fix phasing names";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 640);
            MinimumSize = new Size(760, 400);
            ShowIcon = false;

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8, 8, 8, 4)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            summaryLabel.AutoSize = true;
            summaryLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            summaryFont = new Font(Font, FontStyle.Bold);
            summaryLabel.Font = summaryFont;
            summaryLabel.Margin = new Padding(0, 0, 0, 4);
            detailLabel.AutoSize = true;
            detailLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            detailLabel.Margin = new Padding(0);
            top.Controls.Add(summaryLabel, 0, 0);
            top.Controls.Add(detailLabel, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.None;
            AddColumn("Status", 6, true);
            AddColumn("Type", 6, true);
            AddColumn("Stage", 5, false);
            AddColumn("Layer it sits in (corrected name)", 22, true);
            AddColumn("Current name in Max", 25, true);
            AddColumn("Corrected name", 25, true);
            AddColumn("What changed", 32, true);
            grid.CellBeginEdit += Grid_CellBeginEdit;
            grid.CellEndEdit += Grid_CellEndEdit;

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 8,
                RowCount = 1,
                Padding = new Padding(6, 6, 6, 6)
            };
            for (int i = 0; i < 8; i++)
            {
                bottom.ColumnStyles.Add(i == 4 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.AutoSize));
            }
            SetUpButton(wordFixesButton, "Word fixes\u2026", WordFixesButton_Click);
            SetUpButton(saveListButton, "Save list\u2026", SaveListButton_Click);
            SetUpButton(refreshButton, "Read the scene again", (s, e) => Reload());
            SetUpButton(applyButton, "Apply", ApplyButton_Click);
            SetUpButton(closeButton, "Close", (s, e) => Close());
            everyLayerBox.Text = "Show every layer";
            everyLayerBox.AutoSize = true;
            everyLayerBox.Checked = true;
            everyLayerBox.Anchor = AnchorStyles.Left;
            everyLayerBox.Margin = new Padding(12, 3, 3, 3);
            everyLayerBox.CheckedChanged += (s, e) => Fill();
            holdBox.Text = "Hold the scene first";
            holdBox.AutoSize = true;
            holdBox.Checked = true;
            holdBox.Anchor = AnchorStyles.Right;
            holdBox.Margin = new Padding(3, 3, 12, 3);
            bottom.Controls.Add(wordFixesButton, 0, 0);
            bottom.Controls.Add(saveListButton, 1, 0);
            bottom.Controls.Add(refreshButton, 2, 0);
            bottom.Controls.Add(everyLayerBox, 3, 0);
            bottom.Controls.Add(holdBox, 5, 0);
            bottom.Controls.Add(applyButton, 6, 0);
            bottom.Controls.Add(closeButton, 7, 0);

            // Docking runs from the last control added to the first, so the grid takes what the bars leave.
            Controls.Add(grid);
            Controls.Add(top);
            Controls.Add(bottom);
            CancelButton = closeButton;

            Activated += (s, e) => Loader.Global.DisableAccelerators();
            Deactivate += (s, e) => Loader.Global.EnableAccelerators();
            FormClosed += (s, e) =>
            {
                // The window is made again on every open: a registration left behind would call a closed window.
                WatchScene(false);
                Loader.Global.EnableAccelerators();
                if (current == this)
                {
                    current = null;
                }
            };
            ResumeLayout(false);
            PerformLayout();
            Reload();
            WatchScene(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                WatchScene(false);
                if (boldFont != null)
                {
                    boldFont.Dispose();
                }
                summaryFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void AddColumn(string header, float weight, bool readOnly)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MinimumWidth = 40
            };
            grid.Columns.Add(column);
        }

        private static void SetUpButton(Button button, string text, EventHandler click)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(80, 23);
            button.UseVisualStyleBackColor = true;
            button.Click += click;
        }

        // Following the scene

        private void WatchScene(bool watch)
        {
            try
            {
                if (watch && sceneChanged == null)
                {
                    sceneChanged = new Autodesk.Max.GlobalDelegates.Delegate5(OnSceneChanged);
                    foreach (var code in SceneChanges)
                    {
                        Autodesk.Max.GlobalInterface.Instance.RegisterNotification(sceneChanged, null, code);
                    }
                }
                else if (!watch && sceneChanged != null)
                {
                    var registered = sceneChanged;
                    sceneChanged = null;
                    foreach (var code in SceneChanges)
                    {
                        Autodesk.Max.GlobalInterface.Instance.UnRegisterNotification(registered, null, code);
                    }
                }
            }
            catch (Exception)
            {
                // Without the notifications Apply still notices another scene, by its word fixes, before changing anything.
            }
        }

        private void OnSceneChanged(IntPtr param, Autodesk.Max.INotifyInfo info)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                // Once 3ds Max has finished opening, starting or resetting the scene.
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed)
                    {
                        return;
                    }
                    stageOverrides.Clear();
                    unfinished.Clear();
                    Reload();
                }));
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
            }
        }

        // Reading and planning

        /// <summary>The scene's own word-fix list as text, empty when it has none of its own, or null when it cannot be read.</summary>
        private static string SceneWordFixes()
        {
            try
            {
                return PhasingScene.HasSavedWordFixes() ? PhasingWordFixText.Write(PhasingScene.LoadWordFixes()) : string.Empty;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads the word fixes and the scene again, keeping the stages given in this window.</summary>
        private void Reload()
        {
            loadedWordFixes = SceneWordFixes();
            try
            {
                wordFixesSaved = PhasingScene.HasSavedWordFixes();
                wordFixes = PhasingScene.LoadWordFixes();
            }
            catch (Exception)
            {
                if (wordFixes == null)
                {
                    wordFixes = PhasingNameFixer.DefaultWordFixes();
                }
            }
            Replan();
        }

        /// <summary>Plans again from the scene as it is now.</summary>
        private void Replan()
        {
            try
            {
                readError = null;
                plan = PhasingScene.Plan(wordFixes, Overrides(), unfinished);
                // A stage given to a layer that is no longer there (renamed, deleted, another scene) is forgotten.
                var stale = stageOverrides
                    .Where(o => !plan.Layers.Any(l => l.Key == o.Key && l.CurrentName == o.Value.LayerName))
                    .Select(o => o.Key)
                    .ToList();
                if (stale.Count > 0)
                {
                    foreach (var key in stale)
                    {
                        stageOverrides.Remove(key);
                    }
                    plan = PhasingScene.Plan(wordFixes, Overrides(), unfinished);
                }
            }
            catch (Exception e)
            {
                plan = null;
                readError = e.Message;
            }
            Fill();
        }

        private Dictionary<long, int> Overrides()
        {
            return stageOverrides.ToDictionary(o => o.Key, o => o.Value.Stage);
        }

        // The list

        private void Fill()
        {
            int firstRow = grid.Rows.Count > 0 ? grid.FirstDisplayedScrollingRowIndex : -1;
            grid.Rows.Clear();
            if (plan == null)
            {
                summaryLabel.Text = "The scene could not be read.";
                detailLabel.Text = readError ?? string.Empty;
                applyButton.Enabled = false;
                saveListButton.Enabled = false;
                return;
            }

            summaryLabel.Text = plan.Summary();
            var rows = new List<DataGridViewRow>();
            foreach (var row in plan.Rows.Where(r => r.Status == PhasingFixStatus.Changed && (r.Item == PhasingFixItem.TopLayer || r.Item == PhasingFixItem.Group)))
            {
                rows.Add(MakeRow(row, null));
            }
            var layerRows = new Dictionary<long, PhasingFixRow>();
            foreach (var row in plan.Rows.Where(r => r.Status == PhasingFixStatus.Changed && r.Item == PhasingFixItem.Layer && r.NodeKey.HasValue))
            {
                if (!layerRows.ContainsKey(row.NodeKey.Value))
                {
                    layerRows[row.NodeKey.Value] = row;
                }
            }
            foreach (var layer in plan.Layers)
            {
                PhasingFixRow row;
                if (layerRows.TryGetValue(layer.Key, out row))
                {
                    rows.Add(MakeRow(row, layer));
                }
                else if (everyLayerBox.Checked)
                {
                    rows.Add(MakeRow(null, layer));
                }
            }
            foreach (var row in plan.Rows.Where(r => r.Status == PhasingFixStatus.Changed && r.Item == PhasingFixItem.Object))
            {
                rows.Add(MakeRow(row, null));
            }
            foreach (var row in plan.Rows.Where(r => r.Status == PhasingFixStatus.Check))
            {
                rows.Add(MakeRow(row, null));
            }
            grid.Rows.AddRange(rows.ToArray());
            if (firstRow >= 0 && grid.Rows.Count > 0)
            {
                try
                {
                    grid.FirstDisplayedScrollingRowIndex = Math.Min(firstRow, grid.Rows.Count - 1);
                }
                catch (InvalidOperationException)
                {
                    // The grid has no room to show rows (e.g. minimised); it starts at the top.
                }
            }

            var details = new List<string>();
            details.Add(wordFixesSaved
                ? WordFixCount(wordFixes.Count, "word fix") + ", saved with this scene (Word fixes\u2026 to change them)."
                : "The " + WordFixCount(wordFixes.Count, "word fix") + " of the HS2 model; this scene has no list of its own yet (Word fixes\u2026 to change and save one).");
            details.AddRange(plan.Notes);
            if (plan.Layers.Count > 0)
            {
                details.Add("To give a layer another stage, type the number in its Stage column: its ID, its split number and the tags of its objects follow.");
            }
            detailLabel.Text = string.Join("\r\n", details.ToArray());
            applyButton.Enabled = plan.HasChanges;
            saveListButton.Enabled = plan.TopKey != null;
        }

        private DataGridViewRow MakeRow(PhasingFixRow row, PhasingLayerInfo layer)
        {
            var result = new DataGridViewRow();
            result.CreateCells(grid);
            result.Cells[StatusColumn].Value = row != null ? row.StatusText : "No change";
            result.Cells[TypeColumn].Value = row != null ? row.TypeText : PhasingFixPlan.ItemText(PhasingFixItem.Layer);
            result.Cells[StageColumn].Value = layer != null ? layer.Stage.ToString("00", CultureInfo.InvariantCulture) : string.Empty;
            result.Cells[LayerColumn].Value = row != null ? row.LayerItSitsIn : layer.Group;
            result.Cells[CurrentColumn].Value = row != null ? row.CurrentName : layer.CurrentName;
            result.Cells[CorrectedColumn].Value = row != null ? row.CorrectedName : layer.CorrectedName;
            result.Cells[WhyColumn].Value = row != null ? row.WhatChanged : string.Empty;
            result.Tag = new RowTag { Row = row, Layer = layer };
            if (row != null && row.Status == PhasingFixStatus.Check)
            {
                result.DefaultCellStyle.BackColor = CheckBack;
            }
            if (row == null)
            {
                result.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }
            if (layer != null)
            {
                result.Cells[StageColumn].ToolTipText = layer.StageOverridden
                    ? "Stage given here; the name says " + layer.WrittenStage.ToString("00", CultureInfo.InvariantCulture) + ". Clear the cell to go back to it."
                    : "Type another stage number to move this layer to that stage.";
                if (layer.StageOverridden)
                {
                    if (boldFont == null)
                    {
                        boldFont = new Font(grid.Font, FontStyle.Bold);
                    }
                    result.Cells[StageColumn].Style.Font = boldFont;
                }
            }
            return result;
        }

        private RowTag TagOf(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return null;
            }
            return grid.Rows[rowIndex].Tag as RowTag;
        }

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            var tag = TagOf(e.RowIndex);
            if (e.ColumnIndex != StageColumn || tag == null || tag.Layer == null)
            {
                e.Cancel = true;
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            var tag = TagOf(e.RowIndex);
            if (e.ColumnIndex != StageColumn || tag == null || tag.Layer == null)
            {
                return;
            }
            var layer = tag.Layer;
            var text = (Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            int stage;
            if (text.Length == 0)
            {
                stage = layer.WrittenStage;
            }
            else if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out stage) || stage > 99)
            {
                // The grid is still inside its edit; change it once the edit is over.
                BeginInvoke(new Action(() =>
                {
                    Fill();
                    MessageBox.Show(this, "Write the stage as a whole number from 0 to 99.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
                return;
            }
            if (stage == layer.WrittenStage)
            {
                stageOverrides.Remove(layer.Key);
            }
            else
            {
                stageOverrides[layer.Key] = new StageOverride { Stage = stage, LayerName = layer.CurrentName };
            }
            BeginInvoke(new Action(Replan));
        }

        // Buttons

        private void WordFixesButton_Click(object sender, EventArgs e)
        {
            if (SceneWordFixes() != loadedWordFixes)
            {
                // Another scene, or its list changed elsewhere: edit this scene's own list.
                Reload();
            }
            using (var dialog = new PhasingWordFixForm(wordFixes))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                wordFixes = dialog.WordFixes;
                try
                {
                    PhasingScene.SaveWordFixes(wordFixes);
                    wordFixesSaved = true;
                    loadedWordFixes = SceneWordFixes();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The word fixes are used now but could not be saved with the scene: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                Replan();
            }
        }

        private void SaveListButton_Click(object sender, EventArgs e)
        {
            if (plan == null)
            {
                return;
            }
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Save the list";
                dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                dialog.DefaultExt = "csv";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                string sceneName = null;
                try
                {
                    sceneName = Path.GetFileNameWithoutExtension(Loader.Core.CurFileName);
                    var scenePath = Loader.Core.CurFilePath;
                    var folder = string.IsNullOrEmpty(scenePath) ? null : Path.GetDirectoryName(scenePath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        dialog.InitialDirectory = folder;
                    }
                }
                catch (Exception)
                {
                    // The dialog opens in its own default folder.
                }
                dialog.FileName = (string.IsNullOrEmpty(sceneName) ? "scene" : sceneName) + "_phasing_names.csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    // ToCsv starts with the byte order mark itself.
                    File.WriteAllText(dialog.FileName, plan.ToCsv(), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The list could not be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (ExporterForm.ExportRunning)
            {
                MessageBox.Show(this, "An export is running. Apply the fix once it has finished.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // The window does not stop anyone working in the scene, so the list is checked against it first:
            // another scene (or a list changed elsewhere) brings that scene's own word fixes.
            const string changed = "The scene has changed since the list was made. The list has been read again: check it, then press Apply again.";
            if (SceneWordFixes() != loadedWordFixes)
            {
                Reload();
                MessageBox.Show(this, changed, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PhasingFixPlan fresh;
            try
            {
                fresh = PhasingScene.Plan(wordFixes, Overrides(), unfinished);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (plan == null || PhasingFixScript.Fingerprint(fresh) != PhasingFixScript.Fingerprint(plan))
            {
                Replan();
                MessageBox.Show(this, changed, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!plan.HasChanges)
            {
                MessageBox.Show(this, "There is nothing to change.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var question = "Correct the phasing names in the scene?\r\n\r\n" + plan.Summary() + "\r\n\r\n"
                + (holdBox.Checked
                    ? "The scene is held first: Edit > Fetch puts it back as it is now."
                    : "The scene is not held first, so only Edit > Undo can take the changes back.");
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            PhasingFixResult result;
            bool held = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (holdBox.Checked)
                {
                    Loader.Core.FileHold();
                    held = true;
                }
                result = PhasingScene.Apply(plan);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                Replan();
                MessageBox.Show(this, "The fix could not be applied: " + ex.Message + (held ? "\r\n\r\nEdit > Fetch puts the scene back as it was before the fix." : string.Empty),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;

            if (result.HasProblems)
            {
                unfinished.Add(result);
            }
            Replan();
            var report = result.Describe(plan, held);
            MessageBox.Show(this, Shorten(report), Text, MessageBoxButtons.OK, result.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // A message box with every failure in it would not fit the screen.
        private static string Shorten(string text)
        {
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length <= MessageLines)
            {
                return text;
            }
            var kept = lines.Take(MessageLines - 2).ToList();
            kept.Add("- and " + (lines.Length - MessageLines + 2).ToString(CultureInfo.InvariantCulture) + " more lines; the list in the window shows what is still to change.");
            kept.Add(lines[lines.Length - 1]);
            return string.Join("\r\n", kept.ToArray());
        }

        private static string WordFixCount(int count, string one)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : one + "es");
        }
    }
}
