using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the review list shown before the Schedule tab changes the scene (Link objects to their helpers,
    /// Add layer, Add to IN / RM): one line per change with its reason, the problems that are skipped, a "Hold the scene
    /// first" box and Apply. Built in code; modal over the exporter window.
    /// </summary>
    internal sealed class PlannerReviewForm : Form
    {
        private static readonly Color NewBack = Color.FromArgb(226, 243, 226);
        private static readonly Color ChangeBack = Color.FromArgb(255, 243, 205);
        private static readonly Color ProblemBack = Color.FromArgb(248, 215, 218);

        private readonly DataGridView grid = new DataGridView();
        private readonly CheckBox holdBox = new CheckBox();
        private readonly Button applyButton = new Button();
        private readonly Button saveButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Font summaryFont;
        private readonly List<PlannerReviewRow> rows;
        private readonly string csvName;

        /// <summary>The modeller left "Hold the scene first" ticked.</summary>
        public bool HoldFirst
        {
            get { return holdBox.Checked; }
        }

        public PlannerReviewForm(string title, string summary, IEnumerable<string> notes, List<PlannerReviewRow> rows, bool canApply, string applyText, string csvName)
        {
            this.rows = rows;
            this.csvName = csvName;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1100, 560);
            MinimumSize = new Size(640, 320);
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;

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
            summaryFont = new Font(Font, FontStyle.Bold);
            var summaryLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = summaryFont, Margin = new Padding(0, 0, 0, 4), Text = summary ?? string.Empty };
            var noteLines = (notes ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var notesLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0), Text = string.Join("\r\n", noteLines.ToArray()), Visible = noteLines.Count > 0 };
            top.Controls.Add(summaryLabel, 0, 0);
            top.Controls.Add(notesLabel, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.None;
            // The header follows the font, which display scaling enlarges (its default height does not).
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            AddColumn("Status", 7);
            AddColumn("Item", 9);
            AddColumn("Name", 28);
            AddColumn("Change", 10);
            AddColumn("From", 18);
            AddColumn("To", 18);
            AddColumn("Why", 26);
            var gridRows = new List<DataGridViewRow>();
            foreach (var row in rows)
            {
                var line = new DataGridViewRow();
                line.CreateCells(grid, row.Status, row.Item, row.Name, row.Change, row.From, row.To, row.Why);
                line.DefaultCellStyle.BackColor = row.IsProblem ? ProblemBack : row.Status == "New" ? NewBack : row.Status == "Change" ? ChangeBack : SystemColors.Window;
                if (row.Status == "Adopt")
                {
                    line.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                }
                gridRows.Add(line);
            }
            grid.Rows.AddRange(gridRows.ToArray());

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(6)
            };
            for (int i = 0; i < 5; i++)
            {
                bottom.ColumnStyles.Add(i == 1 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.AutoSize));
            }
            SetUpButton(saveButton, "Save list\u2026");
            saveButton.Click += SaveButton_Click;
            saveButton.Enabled = rows.Count > 0;
            holdBox.Text = "Hold the scene first";
            holdBox.AutoSize = true;
            holdBox.Checked = true;
            holdBox.Anchor = AnchorStyles.Right;
            holdBox.Margin = new Padding(3, 3, 12, 3);
            SetUpButton(applyButton, applyText);
            applyButton.Enabled = canApply;
            applyButton.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            SetUpButton(cancelButton, canApply ? "Cancel" : "Close");
            cancelButton.DialogResult = DialogResult.Cancel;
            bottom.Controls.Add(saveButton, 0, 0);
            bottom.Controls.Add(holdBox, 2, 0);
            bottom.Controls.Add(applyButton, 3, 0);
            bottom.Controls.Add(cancelButton, 4, 0);

            // Docking runs from the last control added to the first, so the grid takes what the bars leave.
            Controls.Add(grid);
            Controls.Add(top);
            Controls.Add(bottom);
            AcceptButton = canApply ? applyButton : cancelButton;
            CancelButton = cancelButton;

            // As the exporter window does: keys typed here are not 3ds Max shortcuts.
            Activated += (s, e) => Loader.Global.DisableAccelerators();
            Deactivate += (s, e) => Loader.Global.EnableAccelerators();
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                summaryFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void AddColumn(string header, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MinimumWidth = 40
            });
        }

        private static void SetUpButton(Button button, string text)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(80, 23);
            button.UseVisualStyleBackColor = true;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
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
                dialog.FileName = (string.IsNullOrEmpty(sceneName) ? "scene" : sceneName) + "_" + csvName + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    // ToCsv starts with the byte order mark itself.
                    File.WriteAllText(dialog.FileName, PlannerReview.ToCsv(rows), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The list could not be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
