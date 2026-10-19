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
    /// UrbanCGI fork: the review window of Clean up scene graph (naming v2, spec 5.1; it replaces Fix phasing names, and with
    /// the Planner's layer list imported it does what Build layers and helpers did, D22). Lists what
    /// <see cref="PlannerCleanUp"/> would change in the scene (the layers and helpers it makes have no current name), and
    /// first the objects it cannot convert safely, each with
    /// its answers in the Answer column: nothing is applied for one until the modeller chooses (a reference to a stage no
    /// layer has is written as the tag writes it unless another answer is chosen). Every answer plans again at once, the
    /// numbering of clashing names included. The project's word fixes are edited here too. On a scene with the old names it
    /// warns that the Planner reads the new names only from its next release, so a live project's scene waits for the
    /// planning team's go-ahead (D25), and offers to save a copy first. Apply closes
    /// the window: the Schedule tab checks the scene has not changed, saves the copy, holds the scene and runs the batch.
    /// Built in code; modal over the exporter window.
    /// </summary>
    internal sealed class PlannerCleanUpForm : Form
    {
        private const int StatusColumn = 0;
        private const int ItemColumn = 1;
        private const int LayerColumn = 2;
        private const int CurrentColumn = 3;
        private const int NewColumn = 4;
        private const int WhyColumn = 5;
        private const int AnswerColumn = 6;
        private const string LeaveText = "Leave it for now: it keeps its old name";
        private static readonly Color ChangeBack = Color.FromArgb(255, 243, 205);
        private static readonly Color ProblemBack = Color.FromArgb(248, 215, 218);
        private static readonly Color ChooseBack = Color.FromArgb(255, 222, 173);
        private static readonly Color AnsweredBack = Color.FromArgb(226, 243, 226);
        private static readonly Color DefaultBack = Color.FromArgb(222, 235, 250);
        private static readonly Color LeftBack = Color.FromArgb(232, 232, 232);

        private readonly Label warningLabel = new Label();
        private readonly Label summaryLabel = new Label();
        private readonly Label notesLabel = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly TableLayoutPanel bottom;
        private readonly ToolTip tips = new ToolTip();
        private readonly CheckBox listedOnlyBox = new CheckBox();
        private readonly CheckBox copyBox = new CheckBox();
        private readonly CheckBox holdBox = new CheckBox();
        private readonly Button wordFixesButton = new Button();
        private readonly Button saveListButton = new Button();
        private readonly Button applyButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Font boldFont;
        private readonly Func<List<PhasingWordFix>, PlannerCleanUpPlan> replan;
        private readonly Func<List<PhasingWordFix>, string> saveWordFixes;
        // The modeller's answers, by question ID; a question with none takes its default (or keeps its object's name).
        private readonly Dictionary<string, string> choices = new Dictionary<string, string>(StringComparer.Ordinal);
        private PlannerCleanUpPlan plan;
        private PlannerCleanUpResult result;
        private List<PhasingWordFix> wordFixes;
        private bool wordFixesSaved;
        private bool filling;

        // A question's line: the question, and the choice each text of its Answer list stands for.
        private sealed class QuestionLine
        {
            public PlannerCleanUpQuestion Question;
            public readonly Dictionary<string, string> ChoiceOf = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>The plan as last read (word fixes edited here re-read the scene).</summary>
        public PlannerCleanUpPlan Plan
        {
            get { return plan; }
        }

        /// <summary>What Apply carries out: the plan resolved with the answers given.</summary>
        public PlannerCleanUpResult Result
        {
            get { return result; }
        }

        /// <summary>The answers given, by question ID.</summary>
        public Dictionary<string, string> Choices
        {
            get { return new Dictionary<string, string>(choices, StringComparer.Ordinal); }
        }

        public List<PhasingWordFix> WordFixes
        {
            get { return wordFixes; }
        }

        /// <summary>The modeller left "Hold the scene first" ticked.</summary>
        public bool HoldFirst
        {
            get { return holdBox.Checked; }
        }

        /// <summary>Where to save a copy of the scene before Apply, or null for none.</summary>
        public string CopyPath { get; private set; }

        /// <param name="plan">Clean up planned on the scene as it is now.</param>
        /// <param name="wordFixes">The word fixes it was planned with.</param>
        /// <param name="wordFixesSaved">The scene has a word-fix list of its own (otherwise the HS2 list is used).</param>
        /// <param name="replan">Reads the scene and plans again with other word fixes.</param>
        /// <param name="saveWordFixes">Keeps the word fixes with the scene: null when saved, else why not.</param>
        public PlannerCleanUpForm(PlannerCleanUpPlan plan, List<PhasingWordFix> wordFixes, bool wordFixesSaved,
            Func<List<PhasingWordFix>, PlannerCleanUpPlan> replan, Func<List<PhasingWordFix>, string> saveWordFixes)
        {
            this.plan = plan;
            this.wordFixes = wordFixes ?? PhasingNameFixer.DefaultWordFixes();
            this.wordFixesSaved = wordFixesSaved;
            this.replan = replan;
            this.saveWordFixes = saveWordFixes;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = "Clean up scene graph";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1180, 640);
            MinimumSize = new Size(760, 400);
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            boldFont = new Font(Font, FontStyle.Bold);

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8, 8, 8, 4)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // On a scene with the old names, what Clean up means for its model, above everything else.
            warningLabel.AutoSize = true;
            warningLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            warningLabel.Margin = new Padding(0, 0, 0, 6);
            warningLabel.Padding = new Padding(4);
            warningLabel.BackColor = ChooseBack;
            warningLabel.Font = boldFont;
            summaryLabel.AutoSize = true;
            summaryLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            summaryLabel.Font = boldFont;
            summaryLabel.Margin = new Padding(0, 0, 0, 4);
            notesLabel.AutoSize = true;
            notesLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            notesLabel.Margin = new Padding(0);
            top.Controls.Add(warningLabel, 0, 0);
            top.Controls.Add(summaryLabel, 0, 1);
            top.Controls.Add(notesLabel, 0, 2);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.None;
            grid.ShowCellToolTips = true;
            // The header follows the font, which display scaling enlarges (its default height does not).
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            AddColumn("Status", 8);
            AddColumn("Item", 10);
            AddColumn("Layer it sits in", 17);
            AddColumn("Current name in Max", 19);
            AddColumn("New name", 19);
            AddColumn("What changes", 27);
            AddColumn("Answer", 22);
            // Only a listed object's Answer list can be changed; every other cell in the column is read-only (ChangeRow).
            grid.Columns[AnswerColumn].ReadOnly = false;
            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // An answer chosen in the list counts at once, not when the cell is left.
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.DataError += (s, e) => e.ThrowException = false;
            grid.EditingControlShowing += (s, e) =>
            {
                var combo = e.Control as ComboBox;
                if (combo != null)
                {
                    combo.MouseWheel -= Answer_MouseWheel;
                    combo.MouseWheel += Answer_MouseWheel;
                }
            };

            bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 8,
                RowCount = 1,
                Padding = new Padding(6)
            };
            for (int i = 0; i < 8; i++)
            {
                bottom.ColumnStyles.Add(i == 3 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.AutoSize));
            }
            SetUpButton(wordFixesButton, "Word fixes\u2026", WordFixesButton_Click);
            SetUpButton(saveListButton, "Save list\u2026", SaveListButton_Click);
            SetUpButton(applyButton, "Apply", ApplyButton_Click);
            SetUpButton(cancelButton, "Cancel", null);
            cancelButton.DialogResult = DialogResult.Cancel;
            listedOnlyBox.Text = "Only rows to choose or check";
            listedOnlyBox.AutoSize = true;
            listedOnlyBox.Anchor = AnchorStyles.Left;
            listedOnlyBox.Margin = new Padding(12, 3, 3, 3);
            listedOnlyBox.CheckedChanged += (s, e) => Fill();
            // The copy's file name goes in the tip, not the box: a long scene name would push the box under the other controls.
            string copyName = null;
            try
            {
                copyName = Path.GetFileName(PlannerCleanUp.BeforeV2FileName(Loader.Core.CurFileName));
            }
            catch (Exception)
            {
                // The tip names no file; the Save As dialog still offers one.
            }
            copyBox.Text = "Save a copy first";
            tips.SetToolTip(copyBox, PlannerTipText.Wrap("Apply first asks where to save the scene as it is now"
                + (string.IsNullOrEmpty(copyName) ? string.Empty : ", as " + copyName) + ", then cleans it up."));
            copyBox.AutoSize = true;
            copyBox.Anchor = AnchorStyles.Right;
            copyBox.Margin = new Padding(3, 3, 12, 3);
            copyBox.Checked = plan != null && plan.OldNames;
            holdBox.Text = "Hold the scene first";
            tips.SetToolTip(holdBox, PlannerTipText.Wrap("Edit > Fetch then puts the scene back as it was before the clean-up. Edit > Undo takes the clean-up back in one step, but whether it "
                + "also takes back the Max layers it renamed, moved or made has not been checked in 3ds Max yet: the hold is the sure way back."));
            holdBox.AutoSize = true;
            holdBox.Checked = true;
            holdBox.Anchor = AnchorStyles.Right;
            holdBox.Margin = new Padding(3, 3, 12, 3);
            bottom.Controls.Add(wordFixesButton, 0, 0);
            bottom.Controls.Add(saveListButton, 1, 0);
            bottom.Controls.Add(listedOnlyBox, 2, 0);
            bottom.Controls.Add(copyBox, 4, 0);
            bottom.Controls.Add(holdBox, 5, 0);
            bottom.Controls.Add(applyButton, 6, 0);
            bottom.Controls.Add(cancelButton, 7, 0);

            // Docking runs from the last control added to the first, so the grid takes what the bars leave.
            Controls.Add(grid);
            Controls.Add(top);
            Controls.Add(bottom);
            // No accept button: Enter belongs to the Answer lists.
            CancelButton = cancelButton;

            // As the exporter window does: keys typed here are not 3ds Max shortcuts.
            Activated += (s, e) => Loader.Global.DisableAccelerators();
            Deactivate += (s, e) => Loader.Global.EnableAccelerators();
            ResumeLayout(false);
            PerformLayout();
            Fill();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Scaled for the display by now: the window is never made narrower than its bar of buttons and boxes needs, so
            // none of them is drawn over another.
            var needed = bottom.GetPreferredSize(Size.Empty).Width + (Width - ClientSize.Width);
            if (needed > MinimumSize.Width)
            {
                MinimumSize = new Size(needed, MinimumSize.Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                boldFont.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }

        // The wheel over an Answer list scrolls the rows, as it does over any other cell: it never changes the answer, which
        // only a choice in the open list does (a list made current by a click keeps the focus under the pointer).
        private void Answer_MouseWheel(object sender, MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            var combo = sender as ComboBox;
            if (handled == null || (combo != null && combo.DroppedDown))
            {
                return;
            }
            handled.Handled = true;
            if (grid.Rows.Count == 0)
            {
                return;
            }
            var lines = SystemInformation.MouseWheelScrollLines;
            var step = lines > 0 ? lines : Math.Max(1, grid.DisplayedRowCount(false));
            var rows = -e.Delta * step / 120;
            if (rows == 0)
            {
                rows = -Math.Sign(e.Delta);
            }
            try
            {
                grid.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(grid.Rows.Count - 1, grid.FirstDisplayedScrollingRowIndex + rows));
            }
            catch (InvalidOperationException)
            {
                // The grid has no room to show rows.
            }
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

        private static void SetUpButton(Button button, string text, EventHandler click)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(80, 23);
            button.UseVisualStyleBackColor = true;
            if (click != null)
            {
                button.Click += click;
            }
        }

        // The list

        private void Fill()
        {
            if (filling)
            {
                return;
            }
            filling = true;
            try
            {
                FillLines();
            }
            finally
            {
                filling = false;
            }
        }

        private void FillLines()
        {
            int firstRow = grid.Rows.Count > 0 ? grid.FirstDisplayedScrollingRowIndex : -1;
            var current = grid.CurrentCell != null ? grid.Rows[grid.CurrentCell.RowIndex].Tag as QuestionLine : null;
            if (grid.IsCurrentCellInEditMode)
            {
                grid.EndEdit();
            }
            grid.Rows.Clear();
            result = plan != null ? plan.Resolve(choices) : null;
            warningLabel.Visible = plan != null && plan.OldNames;
            warningLabel.Text = PlannerCleanUp.OldNamesWarning + " Save a copy first to keep the scene as it is now.";
            if (plan == null || plan.Message != null)
            {
                summaryLabel.Text = plan != null ? plan.Message : "The scene could not be read.";
                notesLabel.Text = string.Empty;
                applyButton.Enabled = false;
                saveListButton.Enabled = false;
                return;
            }
            summaryLabel.Text = result.Summary();

            var notes = new List<string>();
            notes.Add(plan.List != null
                ? "The layers and helpers are brought in line with the Planner's layer list (" + Count(plan.List.Rows.Count, "row", "rows")
                    + "): made, renamed and nested as it has them, never with dates. Nothing is deleted."
                : "No layer list is imported, so only the scene's own names are cleaned. Import the Planner's layer list first to bring the layers and helpers in line with it.");
            notes.Add(wordFixesSaved
                ? Count(wordFixes.Count, "word fix", "word fixes") + ", saved with this scene (Word fixes\u2026 to change them)."
                : "The " + Count(wordFixes.Count, "word fix", "word fixes") + " of the HS2 model; this scene has no list of its own yet (Word fixes\u2026 to change and save one).");
            notes.AddRange(result.Notes);
            var kept = result.KeptOldNamesText();
            if (kept != null)
            {
                notes.Add(kept);
            }
            if (plan.Questions.Count > 0)
            {
                notes.Add("The first rows are the objects Clean up cannot convert safely: choose an answer in each one's Answer column. "
                    + "Nothing is applied for a row left as it is, and its object keeps its old name.");
            }
            notesLabel.Text = string.Join("\r\n", notes.Distinct(StringComparer.Ordinal).ToArray());

            var lines = new List<DataGridViewRow>();
            foreach (var question in plan.Questions)
            {
                lines.Add(QuestionRow(question));
            }
            foreach (var row in result.Rows.Where(r => r.Status != PlannerCleanUpStatus.Choose && (!listedOnlyBox.Checked || r.Status == PlannerCleanUpStatus.Check)))
            {
                lines.Add(ChangeRow(row));
            }
            grid.Rows.AddRange(lines.ToArray());
            if (firstRow >= 0 && grid.Rows.Count > 0)
            {
                try
                {
                    grid.FirstDisplayedScrollingRowIndex = Math.Min(firstRow, grid.Rows.Count - 1);
                }
                catch (InvalidOperationException)
                {
                    // The grid has no room to show rows; it starts at the top.
                }
            }
            if (current != null)
            {
                var again = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is QuestionLine && ((QuestionLine)r.Tag).Question.Id == current.Question.Id);
                if (again != null)
                {
                    grid.CurrentCell = again.Cells[AnswerColumn];
                }
            }
            applyButton.Enabled = result.HasChanges;
            saveListButton.Enabled = true;
        }

        // A listed object: what it asks, its answers, and the name it gets with the answer chosen.
        private DataGridViewRow QuestionRow(PlannerCleanUpQuestion question)
        {
            var line = new QuestionLine { Question = question };
            var open = result.Rows.Any(r => r.Status == PlannerCleanUpStatus.Choose && r.QuestionId == question.Id);
            // An object can ask two things: answered here, it still waits for the other answer.
            var otherOpen = !open && result.Rows.Any(r => r.Status == PlannerCleanUpStatus.Choose && r.NodeKey == question.Key);
            var rename = result.NodeRenames.FirstOrDefault(r => r.Key == question.Key);
            var check = result.Rows.FirstOrDefault(r => r.Status == PlannerCleanUpStatus.Check && r.NodeKey == question.Key);
            bool answered = choices.ContainsKey(question.Id);
            string status, why;
            Color back;
            if (open && LeftAsItIs(question.Id))
            {
                status = "Left";
                why = question.Why + "; left as it is for now: it keeps its old name";
                back = LeftBack;
            }
            else if (open)
            {
                status = "Choose";
                why = question.Why + "; it keeps its name until answered";
                back = ChooseBack;
            }
            else if (rename != null || otherOpen)
            {
                status = answered ? "Answered" : "Default";
                why = question.Why + (answered ? string.Empty : "; this answer is taken unless another is chosen")
                    + (otherOpen ? "; it keeps its name until its other question is answered" : string.Empty);
                back = answered ? AnsweredBack : DefaultBack;
            }
            else
            {
                status = "Check";
                why = question.Why + (check != null ? "; " + check.Why : "; it keeps its name");
                back = ProblemBack;
            }

            var row = new DataGridViewRow { Tag = line };
            row.CreateCells(grid, status, "Object", question.LayerName, question.CurrentName, rename != null ? rename.NewName : question.CurrentName, why, string.Empty);
            row.DefaultCellStyle.BackColor = back;
            row.Cells[StatusColumn].Style.Font = boldFont;
            row.Cells[WhyColumn].ToolTipText = PlannerTipText.Wrap(why);

            // The answers: leave, each option, and each layer for an option that needs one (not its own: that is "at the end").
            var items = new List<string>();
            Action<string, string> add = (text, choice) =>
            {
                if (!line.ChoiceOf.ContainsKey(text))
                {
                    items.Add(text);
                    line.ChoiceOf[text] = choice;
                }
            };
            add(LeaveText, PlannerCleanUpOption.Leave);
            var ownLayer = PlannerNaming.ParseLayerNameV2(question.LayerName);
            var ownId = ownLayer != null ? PlannerNaming.LayerIdKey(ownLayer.Id) : null;
            foreach (var option in question.Options)
            {
                if (!option.NeedsLayer)
                {
                    add(option.Text + (option.Id == question.DefaultChoice ? " (default)" : string.Empty), option.Id);
                    continue;
                }
                var lead = option.Text.Split(':')[0];
                foreach (var id in question.LayerIds)
                {
                    var key = PlannerLayerIdKey.FromText(id);
                    if (key != null && ownId != null && PlannerNaming.LayerIdKey(key) == ownId)
                    {
                        continue;
                    }
                    var layer = plan.Layers.FirstOrDefault(l => l.Id == id);
                    add(lead + ": " + (layer != null ? layer.NewName : id), PlannerCleanUpOption.LayerPrefix + id);
                }
            }
            string chosen;
            if (!choices.TryGetValue(question.Id, out chosen))
            {
                chosen = question.DefaultChoice ?? PlannerCleanUpOption.Leave;
            }
            var value = line.ChoiceOf.FirstOrDefault(p => p.Value == chosen).Key;
            if (value == null)
            {
                // An answer naming a layer the list does not offer (typed by MAXScript, or a layer gone since): shown as it is.
                value = chosen;
                add(chosen, chosen);
            }
            var cell = new DataGridViewComboBoxCell
            {
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                MaxDropDownItems = 16
            };
            cell.Items.AddRange(items.Cast<object>().ToArray());
            using (var graphics = grid.CreateGraphics())
            {
                cell.DropDownWidth = Math.Max(120, items.Max(t => TextRenderer.MeasureText(graphics, t, grid.Font).Width) + SystemInformation.VerticalScrollBarWidth + 12);
            }
            row.Cells[AnswerColumn] = cell;
            cell.Value = value;
            cell.ReadOnly = false;
            cell.ToolTipText = PlannerTipText.Wrap("Choose what Clean up does with " + question.CurrentName + ".");
            return row;
        }

        private DataGridViewRow ChangeRow(PlannerCleanUpRow change)
        {
            var row = new DataGridViewRow { Tag = change };
            row.CreateCells(grid, change.StatusText, change.Item, change.LayerItSitsIn, change.CurrentName, change.NewName, change.Why, string.Empty);
            row.DefaultCellStyle.BackColor = change.Status == PlannerCleanUpStatus.Check ? ProblemBack : ChangeBack;
            row.Cells[WhyColumn].ToolTipText = PlannerTipText.Wrap(change.Why);
            row.Cells[AnswerColumn].ReadOnly = true;
            return row;
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (filling || e.RowIndex < 0 || e.ColumnIndex != AnswerColumn)
            {
                return;
            }
            var line = grid.Rows[e.RowIndex].Tag as QuestionLine;
            var text = grid.Rows[e.RowIndex].Cells[AnswerColumn].Value as string;
            string choice;
            if (line == null || text == null || !line.ChoiceOf.TryGetValue(text, out choice))
            {
                return;
            }
            var question = line.Question;
            // The default answer needs no entry. "Leave" chosen is kept, even for a question with no default: the row then
            // reads as left on purpose, not as waiting for an answer.
            if (choice == question.DefaultChoice)
            {
                choices.Remove(question.Id);
            }
            else
            {
                choices[question.Id] = choice;
            }
            // The grid is still inside its edit: plan again once it is over.
            BeginInvoke(new Action(Fill));
        }

        // Buttons

        private void WordFixesButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new PhasingWordFixForm(wordFixes))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                wordFixes = dialog.WordFixes;
            }
            var error = saveWordFixes != null ? saveWordFixes(wordFixes) : null;
            if (error == null)
            {
                wordFixesSaved = true;
            }
            else
            {
                MessageBox.Show(this, "The word fixes are used now but could not be saved with the scene: " + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            try
            {
                plan = replan(wordFixes);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Fill();
        }

        private void SaveListButton_Click(object sender, EventArgs e)
        {
            if (result == null)
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
                    var folder = SceneFolder();
                    if (folder != null)
                    {
                        dialog.InitialDirectory = folder;
                    }
                }
                catch (Exception)
                {
                    // The dialog opens in its own default folder.
                }
                dialog.FileName = (string.IsNullOrEmpty(sceneName) ? "scene" : sceneName) + "_clean_up.csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    // ToCsv starts with the byte order mark itself.
                    File.WriteAllText(dialog.FileName, result.ToCsv(), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The list could not be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (grid.IsCurrentCellInEditMode)
            {
                grid.EndEdit();
            }
            if (result == null || !result.HasChanges)
            {
                return;
            }
            // The listed objects that keep their old names: left as they are on purpose, or with a question not answered yet.
            var keeping = result.Rows.Where(r => r.Status == PlannerCleanUpStatus.Choose && r.NodeKey.HasValue).GroupBy(r => r.NodeKey.Value).ToList();
            if (keeping.Count > 0)
            {
                var one = keeping.Count == 1;
                var unanswered = keeping.Count(g => g.Any(r => !LeftAsItIs(r.QuestionId)));
                var left = keeping.Count - unanswered;
                var how = unanswered == 0 ? (one ? "left as it is" : "left as they are")
                    : left == 0 ? "no answer yet"
                    : Count(left, "left as it is", "left as they are") + ", " + Count(unanswered, "with no answer yet", "with no answer yet");
                var question = Count(keeping.Count, "listed object keeps its old name", "listed objects keep their old names") + " (" + how + "), so the Planner will not schedule "
                    + (one ? "it" : "them") + " on the new names. Clean up the rest now?";
                if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }
            }
            CopyPath = null;
            if (copyBox.Checked)
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Save a copy of the scene first";
                    dialog.Filter = "3ds Max scenes (*.max)|*.max";
                    dialog.DefaultExt = "max";
                    dialog.AddExtension = true;
                    dialog.OverwritePrompt = true;
                    try
                    {
                        dialog.FileName = Path.GetFileName(PlannerCleanUp.BeforeV2FileName(Loader.Core.CurFileName));
                        var folder = SceneFolder();
                        if (folder != null)
                        {
                            dialog.InitialDirectory = folder;
                        }
                    }
                    catch (Exception)
                    {
                        // The dialog opens in its own default folder.
                    }
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    CopyPath = dialog.FileName;
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // The modeller chose "leave" for this question.
        private bool LeftAsItIs(string questionId)
        {
            string choice;
            return questionId != null && choices.TryGetValue(questionId, out choice) && choice == PlannerCleanUpOption.Leave;
        }

        private static string SceneFolder()
        {
            var scenePath = Loader.Core.CurFilePath;
            var folder = string.IsNullOrEmpty(scenePath) ? null : Path.GetDirectoryName(scenePath);
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
        }

        private static string Count(int count, string one, string many)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);
        }
    }
}
