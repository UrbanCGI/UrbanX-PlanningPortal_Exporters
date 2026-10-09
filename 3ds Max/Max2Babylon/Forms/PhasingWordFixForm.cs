using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: edits the project's word fixes for the phasing name fixer (find / replace, in order). The
    /// caller saves the result with the scene.
    /// </summary>
    internal sealed class PhasingWordFixForm : Form
    {
        private const int FindColumn = 0;
        private const int ReplaceColumn = 1;

        private readonly DataGridView grid = new DataGridView();

        /// <summary>The list as edited, set when the dialog closes with OK.</summary>
        public List<PhasingWordFix> WordFixes { get; private set; }

        public PhasingWordFixForm(IEnumerable<PhasingWordFix> fixes)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = "Word fixes";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 400);
            MinimumSize = new Size(480, 300);
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var intro = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 64,
                Padding = new Padding(8, 8, 8, 4),
                Text = "Spelling fixes for this project, applied in this order to the phasing layer, group and object names before "
                    + "spaces become underscores. The text under Find is replaced by the text under Replace wherever it appears, "
                    + "exactly as written (capitals count). The list is saved with the scene."
            };

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersWidth = 24;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.None;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Find", SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Replace", SortMode = DataGridViewColumnSortMode.NotSortable });

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(6)
            };
            for (int i = 0; i < 6; i++)
            {
                bottom.ColumnStyles.Add(i == 3 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.AutoSize));
            }
            var upButton = MakeButton("Move up", (s, e) => MoveCurrentRow(-1));
            var downButton = MakeButton("Move down", (s, e) => MoveCurrentRow(1));
            var defaultsButton = MakeButton("Start from the HS2 list", (s, e) => FillGrid(PhasingNameFixer.DefaultWordFixes()));
            var okButton = MakeButton("OK", OkButton_Click);
            var cancelButton = MakeButton("Cancel", (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            bottom.Controls.Add(upButton, 0, 0);
            bottom.Controls.Add(downButton, 1, 0);
            bottom.Controls.Add(defaultsButton, 2, 0);
            bottom.Controls.Add(okButton, 4, 0);
            bottom.Controls.Add(cancelButton, 5, 0);

            Controls.Add(grid);
            Controls.Add(intro);
            Controls.Add(bottom);
            CancelButton = cancelButton;

            Activated += (s, e) => Loader.Global.DisableAccelerators();
            Deactivate += (s, e) => Loader.Global.EnableAccelerators();
            ResumeLayout(false);
            PerformLayout();
            FillGrid(fixes);
        }

        private static Button MakeButton(string text, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(80, 23),
                UseVisualStyleBackColor = true
            };
            button.Click += click;
            return button;
        }

        private void FillGrid(IEnumerable<PhasingWordFix> fixes)
        {
            grid.Rows.Clear();
            foreach (var fix in fixes ?? Enumerable.Empty<PhasingWordFix>())
            {
                if (fix != null)
                {
                    grid.Rows.Add(fix.Find ?? string.Empty, fix.Replace ?? string.Empty);
                }
            }
        }

        private void MoveCurrentRow(int step)
        {
            if (grid.CurrentCell == null)
            {
                return;
            }
            grid.EndEdit();
            int from = grid.CurrentCell.RowIndex;
            int to = from + step;
            int column = grid.CurrentCell.ColumnIndex;
            if (from < 0 || grid.Rows[from].IsNewRow || to < 0 || to >= grid.Rows.Count || grid.Rows[to].IsNewRow)
            {
                return;
            }
            for (int c = 0; c < grid.Columns.Count; c++)
            {
                var swap = grid.Rows[from].Cells[c].Value;
                grid.Rows[from].Cells[c].Value = grid.Rows[to].Cells[c].Value;
                grid.Rows[to].Cells[c].Value = swap;
            }
            grid.CurrentCell = grid.Rows[to].Cells[column];
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            grid.EndEdit();
            var fixes = new List<PhasingWordFix>();
            var problems = new List<string>();
            int line = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }
                line++;
                // As typed: a space in a word fix can be meant.
                var find = CellText(row, FindColumn);
                var replace = CellText(row, ReplaceColumn);
                if (find.Length == 0 && replace.Length == 0)
                {
                    continue;
                }
                var fix = new PhasingWordFix(find, replace);
                var problem = PhasingNameFixer.DescribeWordFixProblem(fix);
                if (problem != null)
                {
                    problems.Add("Line " + line.ToString(CultureInfo.InvariantCulture) + " (" + find + " \u2192 " + replace + "): " + problem + ".");
                    continue;
                }
                fixes.Add(fix);
            }
            if (problems.Count > 0)
            {
                MessageBox.Show(this, "These word fixes cannot be used:\r\n\r\n" + string.Join("\r\n", problems.ToArray()) + "\r\n\r\nCorrect or delete them, then press OK.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            WordFixes = fixes;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string CellText(DataGridViewRow row, int column)
        {
            return Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
