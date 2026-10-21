using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private readonly ReviewGrid grid = new ReviewGrid();
        private readonly TableLayoutPanel bottom;
        private readonly ToolTip tips = new ToolTip();
        // What the last paste did, in the bar at the bottom.
        private readonly Label pasteLabel = new Label();
        private readonly ContextMenuStrip answerMenu = new ContextMenuStrip();
        private AnswerPicker picker;
        // The answer last copied from an Answer cell, and its choice: pasted into another row it takes the same choice, as
        // an answer that names the object's own layer reads differently on each row.
        private string copiedText;
        private string copiedChoice;
        // The copied answer's own layer ID: "at the end of its own layer, 02_02_8" is the same answer as "..., 06_05_1" on
        // a row in 06_05_1, but "Refer to 01_26" is not "Refer to 01_14".
        private string copiedOwnId;
        // The row the answer menu was opened on.
        private int menuRow = -1;
        // When an answer was last chosen in the search list: a click on the grid just after it is the second click of a
        // double-click on the list, not a click to open another row's list.
        private int chosenAt;
        private bool chosenByClick;
        // When the grid's last mouse button press happened (GetMessageTime), however late it is handled.
        private int pressedAt;

        [DllImport("user32.dll")]
        private static extern int GetMessageTime();

        // Whether the clipboard holds text, asked without opening it (CF_UNICODETEXT).
        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        private const uint UnicodeTextFormat = 13;
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

        // A question's line: the question, its answers in order, and the choice each answer stands for.
        private sealed class QuestionLine
        {
            public PlannerCleanUpQuestion Question;
            public readonly List<string> Answers = new List<string>();
            public readonly Dictionary<string, string> ChoiceOf = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // The review list: Enter, F4, Alt+Down, Ctrl+C and Ctrl+V reach the Answer cells' handling (AnswerKey) before the
        // grid's own (Enter would move down a row; Ctrl+C still copies the cells as usual).
        private sealed class ReviewGrid : DataGridView
        {
            public Func<Keys, bool> AnswerKey;

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                return (AnswerKey != null && AnswerKey(keyData)) || base.ProcessCmdKey(ref msg, keyData);
            }
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
            // No cell is edited in place: an answer is chosen in the search list (AnswerPicker).
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
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
            grid.DataError += (s, e) => e.ThrowException = false;
            // A listed object's answers open in a search list: a click on its Answer, Enter, F4 or Alt+Down, or a key typed
            // there (which starts the search). Ctrl+C copies an answer and Ctrl+V pastes it into the rows of the cells
            // chosen; the right-click menu does the same.
            grid.AnswerKey = AnswerKey;
            grid.CellMouseClick += Grid_CellMouseClick;
            // The second press of a double-click on an Answer closes the list the first one opened: it opens again.
            grid.CellMouseDoubleClick += Grid_CellMouseClick;
            grid.MouseDown += (s, e) => pressedAt = GetMessageTime();
            grid.KeyPress += (s, e) =>
            {
                var cell = grid.CurrentCell;
                if (!char.IsControl(e.KeyChar) && cell != null && cell.ColumnIndex == AnswerColumn && LineAt(cell.RowIndex) != null)
                {
                    e.Handled = true;
                    OpenPicker(cell.RowIndex, e.KeyChar.ToString());
                }
            };
            // Search and Copy act on the row right-clicked; Paste on the rows chosen.
            answerMenu.Items.Add("Search answers\u2026", null, (s, e) => OpenPicker(menuRow, null));
            answerMenu.Items.Add("Copy answer", null, (s, e) => CopyAnswer(menuRow));
            answerMenu.Items.Add("Paste answer", null, (s, e) => PasteAnswer());

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
            // Anchored, not docked: a docked label grows the bar's row as soon as it has text.
            pasteLabel.AutoSize = false;
            pasteLabel.AutoEllipsis = true;
            pasteLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            pasteLabel.TextAlign = ContentAlignment.MiddleLeft;
            pasteLabel.ForeColor = SystemColors.GrayText;
            bottom.Controls.Add(pasteLabel, 3, 0);
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
                answerMenu.Dispose();
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
            // Asked of two phasing roots that share layer IDs, not of an object: its answer renumbers one root's layers.
            bool roots = question.Case == PlannerCleanUpCase.SharedIds;
            string status, why;
            Color back;
            if (open && LeftAsItIs(question.Id))
            {
                status = "Left";
                why = question.Why + (roots ? "; left for now: they still share them, and the export check reports it" : "; left as it is for now: it keeps its old name");
                back = LeftBack;
            }
            else if (open)
            {
                status = "Choose";
                why = question.Why + (roots ? "; nothing is renumbered until answered" : "; it keeps its name until answered");
                back = ChooseBack;
            }
            else if (roots)
            {
                // Answered here, or no longer asked: another answer gave the two roots different IDs.
                string given;
                var kept = choices.TryGetValue(question.Id, out given) && given != null && given.StartsWith(PlannerCleanUpOption.KeepPrefix, StringComparison.Ordinal);
                status = kept ? "Answered" : "Check";
                why = question.Why + (kept ? string.Empty : "; no longer needed: another answer has given them different IDs");
                back = kept ? AnsweredBack : ProblemBack;
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
            string chosenRoot;
            var keeps = roots && choices.TryGetValue(question.Id, out chosenRoot) ? question.OptionFor(chosenRoot) : null;
            row.CreateCells(grid, status, roots ? "Phasing roots" : "Object", question.LayerName, question.CurrentName,
                roots ? (keeps != null ? keeps.Name : question.CurrentName) : rename != null ? rename.NewName : question.CurrentName, why, string.Empty);
            row.DefaultCellStyle.BackColor = back;
            row.Cells[StatusColumn].Style.Font = boldFont;
            row.Cells[WhyColumn].ToolTipText = PlannerTipText.Wrap(why);

            // The answers: leave, each option, and each layer for an option that needs one (not its own: that is "at the end").
            var items = line.Answers;
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
            // Drawn as a list, but chosen in the search list: the cell holds only the answer it shows.
            var cell = new DataGridViewComboBoxCell
            {
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            };
            cell.Items.Add(value);
            row.Cells[AnswerColumn] = cell;
            cell.Value = value;
            // Chosen in the search list, not edited in the cell.
            cell.ReadOnly = true;
            cell.ToolTipText = PlannerTipText.Wrap("Choose what Clean up does with " + question.CurrentName + ": click, press Enter or start typing to "
                + "search the answers. Ctrl+C copies this answer; Ctrl+V pastes a copied answer into the rows chosen.");
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

        // Answers: chosen in the search list, copied and pasted

        private QuestionLine LineAt(int rowIndex)
        {
            return rowIndex >= 0 && rowIndex < grid.Rows.Count ? grid.Rows[rowIndex].Tag as QuestionLine : null;
        }

        // A listed object's answer counts at once: every answer plans again.
        private void SetChoice(PlannerCleanUpQuestion question, string choice)
        {
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
        }

        private void SetPasteNote(string text)
        {
            pasteLabel.Text = text ?? string.Empty;
            tips.SetToolTip(pasteLabel, string.IsNullOrEmpty(text) ? null : PlannerTipText.Wrap(text));
        }

        private bool AnswerKey(Keys keyData)
        {
            var cell = grid.CurrentCell;
            var onAnswer = cell != null && cell.ColumnIndex == AnswerColumn && LineAt(cell.RowIndex) != null;
            switch (keyData)
            {
                case Keys.Enter:
                case Keys.F4:
                case Keys.Alt | Keys.Down:
                    if (onAnswer)
                    {
                        OpenPicker(cell.RowIndex, null);
                    }
                    return onAnswer;
                case Keys.Control | Keys.C:
                case Keys.Control | Keys.Insert:
                    // An answer is copied with the clipboard guarded; other cells as the grid copies them.
                    if (onAnswer)
                    {
                        CopyAnswer(cell.RowIndex);
                    }
                    return onAnswer;
                case Keys.Control | Keys.V:
                case Keys.Shift | Keys.Insert:
                    PasteAnswer();
                    return true;
            }
            return false;
        }

        private void Grid_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                // Paste acts on the cells chosen, or on the one clicked when it is not among them; Search and Copy on the
                // row clicked.
                if (!grid[e.ColumnIndex, e.RowIndex].Selected)
                {
                    grid.CurrentCell = grid[e.ColumnIndex, e.RowIndex];
                }
                menuRow = e.RowIndex;
                var onAnswer = e.ColumnIndex == AnswerColumn && LineAt(e.RowIndex) != null;
                answerMenu.Items[0].Enabled = onAnswer;
                answerMenu.Items[1].Enabled = onAnswer;
                bool busy;
                answerMenu.Items[2].Enabled = (ClipboardText(out busy) != null || busy) && TargetLines().Count > 0;
                answerMenu.Show(Cursor.Position);
                return;
            }
            // Ctrl+ and Shift+click choose several cells (to paste into) without opening the list; so does the second click
            // of a double-click on an answer in the list, which lands on the grid once the list has closed: pressed within
            // the double-click time of that choice, however long the re-plan took.
            var secondOfDouble = chosenByClick && unchecked(pressedAt - chosenAt) >= 0 && unchecked(pressedAt - chosenAt) <= SystemInformation.DoubleClickTime;
            chosenByClick = false;
            if (e.Button == MouseButtons.Left && e.ColumnIndex == AnswerColumn && ModifierKeys == Keys.None && LineAt(e.RowIndex) != null && !secondOfDouble)
            {
                OpenPicker(e.RowIndex, null);
            }
        }

        private void OpenPicker(int rowIndex, string typed)
        {
            var line = LineAt(rowIndex);
            if (line == null)
            {
                return;
            }
            if (picker != null && !picker.IsDisposed)
            {
                picker.Close();
            }
            var cell = grid.Rows[rowIndex].Cells[AnswerColumn];
            if (grid.CurrentCell != cell)
            {
                grid.CurrentCell = cell;
            }
            // Opened by a key on a row scrolled out of view: the row is brought into view, so the list sits under it.
            var shown = grid.GetCellDisplayRectangle(AnswerColumn, rowIndex, false);
            if (shown.Height < cell.Size.Height)
            {
                try
                {
                    grid.FirstDisplayedScrollingRowIndex = rowIndex;
                }
                catch (InvalidOperationException)
                {
                    // The grid has no room to show rows.
                }
                shown = grid.GetCellDisplayRectangle(AnswerColumn, rowIndex, false);
            }
            var bounds = grid.RectangleToScreen(shown.IsEmpty ? grid.ClientRectangle : shown);
            picker = new AnswerPicker(line.Answers, cell.Value as string, typed, grid.Font, bounds, grid.RectangleToScreen(grid.ClientRectangle), () =>
            {
                bool busy;
                var text = ClipboardText(out busy);
                return text != null ? AnswerFor(line, text) : null;
            }, (text, byClick) =>
            {
                string choice;
                if (!line.ChoiceOf.TryGetValue(text, out choice))
                {
                    return;
                }
                chosenByClick = byClick;
                chosenAt = GetMessageTime();
                SetPasteNote(null);
                SetChoice(line.Question, choice);
                BeginInvoke(new Action(Fill));
            });
            picker.Show(this);
        }

        private void NoteCopied(QuestionLine line, string text)
        {
            string choice;
            if (line == null || text == null || !line.ChoiceOf.TryGetValue(text, out choice))
            {
                return;
            }
            copiedText = text;
            copiedChoice = choice;
            copiedOwnId = OwnLayerId(line);
        }

        // The layer ID of the layer a listed object sits in, as answers write it, or null.
        private static string OwnLayerId(QuestionLine line)
        {
            var layer = PlannerNaming.ParseLayerNameV2(line.Question.LayerName);
            return layer != null ? PlannerNaming.LayerIdText(layer.Id) : null;
        }

        private void CopyAnswer(int rowIndex)
        {
            var line = LineAt(rowIndex);
            var text = line != null ? grid.Rows[rowIndex].Cells[AnswerColumn].Value as string : null;
            if (text == null)
            {
                return;
            }
            try
            {
                // A few short tries, not the default second, while another program holds the clipboard.
                Clipboard.SetDataObject(text, true, 3, 50);
            }
            catch (Exception)
            {
                // Another program holds the clipboard: Ctrl+V would paste what it held before.
                SetPasteNote("The clipboard is busy: press Ctrl+C again.");
                return;
            }
            NoteCopied(line, text);
            SetPasteNote("Answer copied: choose the rows to give it to, and press Ctrl+V.");
        }

        // The clipboard's first line, or null; busy when another program held the clipboard.
        private static string ClipboardText(out bool busy)
        {
            busy = false;
            try
            {
                if (!Clipboard.ContainsText())
                {
                    // Text that cannot be read is held by another program.
                    busy = IsClipboardFormatAvailable(UnicodeTextFormat);
                    return null;
                }
                var text = Clipboard.GetText().Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Text that reads as nothing while text is there is held by another program.
                    busy = IsClipboardFormatAvailable(UnicodeTextFormat);
                    return null;
                }
                return text.Trim();
            }
            catch (Exception)
            {
                busy = true;
                return null;
            }
        }

        // The listed objects of the rows whose cells are chosen (any column), or of the current cell's row when none is.
        private List<QuestionLine> TargetLines()
        {
            var rows = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).ToList();
            if (rows.Count == 0 && grid.CurrentCell != null)
            {
                rows.Add(grid.CurrentCell.RowIndex);
            }
            return rows.Distinct().OrderBy(r => r).Select(LineAt).Where(l => l != null).ToList();
        }

        // The answer of this row that a copied or pasted text stands for, or null. The answer last copied here gives this
        // row's answer for the same choice when that reads the same but for its layer IDs ("Removed at the end of its own
        // layer, 02_02_8" is "..., 06_05_1" here); not when the same choice reads otherwise (taking out the second RM_ of
        // RM_RM_ is not taking out the RM_ of IN_RM_; a reference the Planner's list has renumbered). Any other text gives
        // the answer that reads the same (spaces and case aside, and the " (default)" the list adds).
        private string AnswerFor(QuestionLine line, string text)
        {
            var plain = Plain(text);
            if (copiedText != null && copiedChoice != null && string.Equals(Plain(copiedText), plain, StringComparison.OrdinalIgnoreCase))
            {
                var copied = WithoutOwnId(copiedText, copiedOwnId);
                var ownId = OwnLayerId(line);
                var same = line.ChoiceOf.Where(p => p.Value == copiedChoice).Select(p => p.Key)
                    .FirstOrDefault(a => string.Equals(WithoutOwnId(a, ownId), copied, StringComparison.OrdinalIgnoreCase));
                if (same != null)
                {
                    return same;
                }
            }
            return line.Answers.FirstOrDefault(a => string.Equals(Plain(a), plain, StringComparison.OrdinalIgnoreCase));
        }

        // An answer as Plain gives it, with its row's own layer ID (written whole: not 02_02 inside 02_02_8) as "#".
        private static string WithoutOwnId(string answer, string ownId)
        {
            var text = Plain(answer);
            return string.IsNullOrEmpty(ownId)
                ? text
                : System.Text.RegularExpressions.Regex.Replace(text, "(?<![0-9A-Za-z_])" + System.Text.RegularExpressions.Regex.Escape(ownId) + "(?![0-9A-Za-z]|_[0-9])", "#");
        }

        // Pastes the clipboard's answer into every row chosen that has an answer for it (AnswerFor).
        private void PasteAnswer()
        {
            bool busy;
            var text = ClipboardText(out busy);
            var lines = TargetLines();
            if (text == null)
            {
                SetPasteNote(busy ? "The clipboard is busy: press Ctrl+V again." : "Nothing to paste: copy an answer first.");
                return;
            }
            if (lines.Count == 0)
            {
                SetPasteNote("Choose the Answer cells (or rows) to paste into.");
                return;
            }
            int pasted = 0;
            var missed = new List<string>();
            foreach (var line in lines)
            {
                var answer = AnswerFor(line, text);
                string choice;
                if (answer == null || !line.ChoiceOf.TryGetValue(answer, out choice))
                {
                    missed.Add(line.Question.CurrentName);
                    continue;
                }
                SetChoice(line.Question, choice);
                pasted++;
            }
            SetPasteNote((pasted > 0 ? "Answer pasted into " + Count(pasted, "row", "rows") : "Answer not pasted")
                + (missed.Count > 0 ? "; " + Count(missed.Count, "row has", "rows have") + " no answer that matches it: " + string.Join(", ", missed.Take(3).ToArray())
                    + (missed.Count > 3 ? ", \u2026" : string.Empty) : string.Empty)
                + ".");
            if (pasted > 0)
            {
                Fill();
            }
        }

        // An answer with its spaces evened out and without the " (default)" its list adds to the default one.
        private static string Plain(string answer)
        {
            const string mark = " (default)";
            var text = string.Join(" ", answer.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return text.EndsWith(mark, StringComparison.OrdinalIgnoreCase) ? text.Substring(0, text.Length - mark.Length) : text;
        }

        // One question's answers, searched as they are typed, in a small window under its Answer cell. Every word typed must
        // be in an answer, in any order and any case; the best matches come first (Score: whole words, then word starts,
        // and an answer that begins with what was typed before all), and the first is chosen while words are typed.
        // Enter or a click chooses;
        // Esc, or a click anywhere else, closes it unchanged; Up, Down, Page Up and Page Down move. Ctrl+V chooses the
        // answer a copied one stands for on this row (or, when none does, pastes the text into the search).
        private sealed class AnswerPicker : Form
        {
            private const int EmSetCueBanner = 0x1501;
            private const int VisibleRows = 14;
            private readonly TextBox search = new TextBox();
            private readonly Label none = new Label();
            private readonly ListBox list = new ListBox();
            private readonly List<string> answers;
            private readonly string current;
            private readonly Rectangle cell;
            private readonly Rectangle room;
            private readonly Func<string> pasted;
            private readonly Action<string, bool> choose;
            private bool done;

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

            /// <param name="pasted">The answer of this row the clipboard stands for, or null.</param>
            /// <param name="cell">The Answer cell, on the screen.</param>
            /// <param name="room">The review list, on the screen: the list stays inside it.</param>
            public AnswerPicker(List<string> answers, string current, string typed, Font font, Rectangle cell, Rectangle room, Func<string> pasted, Action<string, bool> choose)
            {
                this.answers = answers;
                this.current = current;
                this.cell = cell;
                this.room = room;
                this.pasted = pasted;
                this.choose = choose;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Text = "Answers";
                Font = font;
                // A one-pixel frame round the box and the list.
                BackColor = SystemColors.ControlDark;
                Padding = new Padding(1);
                search.Dock = DockStyle.Top;
                search.BorderStyle = BorderStyle.FixedSingle;
                none.Dock = DockStyle.Top;
                none.AutoSize = false;
                none.Height = font.Height + 6;
                none.TextAlign = ContentAlignment.MiddleLeft;
                none.BackColor = SystemColors.Window;
                none.ForeColor = SystemColors.GrayText;
                none.Text = "No answer has every word typed.";
                none.Visible = false;
                list.Dock = DockStyle.Fill;
                list.IntegralHeight = false;
                // The box keeps the focus (Tab included), so typing always searches and Enter always chooses.
                list.TabStop = false;
                // A press on the list gives it the focus; the box takes it back on the release, after the list's click has
                // chosen (or not: a press on its empty part, or released off an item), so typing goes on searching.
                list.MouseUp += (s, e) =>
                {
                    if (!done && !IsDisposed)
                    {
                        search.Focus();
                    }
                };
                list.BorderStyle = BorderStyle.None;
                // Docking runs from the last control added: the box on top, the note under it, the list below.
                Controls.Add(list);
                Controls.Add(none);
                Controls.Add(search);
                search.TextChanged += (s, e) => Filter();
                search.KeyDown += Search_KeyDown;
                list.MouseClick += (s, e) =>
                {
                    var index = list.IndexFromPoint(e.Location);
                    if (index >= 0)
                    {
                        Choose(list.Items[index] as string, true);
                    }
                };
                // As the other windows do: keys typed here are not 3ds Max shortcuts. Leaving it closes it.
                Activated += (s, e) => Loader.Global.DisableAccelerators();
                Deactivate += (s, e) =>
                {
                    Loader.Global.EnableAccelerators();
                    if (!done && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed)
                            {
                                Close();
                            }
                        }));
                    }
                };
                Filter();
                if (!string.IsNullOrEmpty(typed))
                {
                    search.Text = typed;
                }
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                // Measured now the list has its handle (before it, its row height is a default, not its font's): as wide as
                // the cell or the longest answer, and as many rows as fit up to VisibleRows, under the cell or above it,
                // inside the review list (so a double-click's second press never lands on the buttons below it) and the
                // screen; on the side with more room, with fewer rows, when neither side has room for them all.
                var area = Screen.FromRectangle(cell).WorkingArea;
                var inside = Rectangle.Intersect(area, room);
                if (inside.Height <= 0)
                {
                    inside = area;
                }
                int widest = answers.Count > 0 ? answers.Max(a => TextRenderer.MeasureText(a, list.Font).Width) : 0;
                int width = Math.Min(area.Width - 8, Math.Max(cell.Width, widest + SystemInformation.VerticalScrollBarWidth + 16));
                int frame = search.Height + Padding.Vertical + 4;
                int height = frame + list.ItemHeight * Math.Max(1, Math.Min(VisibleRows, answers.Count));
                int below = inside.Bottom - cell.Bottom;
                int above = cell.Top - inside.Top;
                int y;
                if (height <= below)
                {
                    y = cell.Bottom;
                }
                else if (height <= above)
                {
                    y = cell.Top - height;
                }
                else
                {
                    var space = Math.Max(below, above);
                    height = Math.Max(frame + list.ItemHeight, frame + (space - frame) / list.ItemHeight * list.ItemHeight);
                    y = below >= above ? cell.Bottom : cell.Top - height;
                }
                int x = Math.Max(area.Left, Math.Min(cell.Left, area.Right - width));
                Bounds = new Rectangle(x, y, width, height);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    SendMessage(search.Handle, EmSetCueBanner, (IntPtr)1, "Type to search");
                }
                catch (Exception)
                {
                    // The box shows no hint.
                }
                search.Focus();
                search.SelectionStart = search.Text.Length;
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
                {
                    var answer = pasted != null ? pasted() : null;
                    if (answer != null)
                    {
                        Choose(answer, false);
                        return true;
                    }
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (keyData == Keys.Escape)
                {
                    done = true;
                    Close();
                    return true;
                }
                return base.ProcessDialogKey(keyData);
            }

            // How well an answer matches the words typed, or -1 when one of them is not in it: each word counts 2 as a whole
            // word, 1 as the start of a word and 0 inside one ("no" in "North"), and an answer that begins with all that was
            // typed comes before the rest ("no removal" finds "No removal: ..." first).
            private static int Score(string answer, string[] words)
            {
                int score = 0;
                foreach (var word in words)
                {
                    int best = -1;
                    for (int at = answer.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0 && best < 2;
                        at = at + 1 < answer.Length ? answer.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase) : -1)
                    {
                        bool starts = at == 0 || !char.IsLetterOrDigit(answer[at - 1]);
                        bool ends = at + word.Length == answer.Length || !char.IsLetterOrDigit(answer[at + word.Length]);
                        best = Math.Max(best, starts ? (ends ? 2 : 1) : 0);
                    }
                    if (best < 0)
                    {
                        return -1;
                    }
                    score += best;
                }
                if (words.Length > 0 && answer.StartsWith(string.Join(" ", words), StringComparison.OrdinalIgnoreCase))
                {
                    score += 2 * words.Length + 1;
                }
                return score;
            }

            // The answers that hold every word typed, best first. With nothing typed the row's own answer is chosen; with
            // words typed, the best match.
            private void Filter()
            {
                var words = search.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var shown = answers.Select((answer, order) => new { answer, order, score = Score(answer, words) })
                    .Where(a => a.score >= 0).OrderByDescending(a => a.score).ThenBy(a => a.order).Select(a => a.answer).ToList();
                list.BeginUpdate();
                list.Items.Clear();
                list.Items.AddRange(shown.Cast<object>().ToArray());
                list.EndUpdate();
                none.Visible = shown.Count == 0;
                var index = words.Length == 0 && current != null ? list.Items.IndexOf(current) : -1;
                list.SelectedIndex = shown.Count == 0 ? -1 : Math.Max(0, index);
            }

            private void Search_KeyDown(object sender, KeyEventArgs e)
            {
                int step;
                switch (e.KeyCode)
                {
                    case Keys.Down:
                        step = 1;
                        break;
                    case Keys.Up:
                        step = -1;
                        break;
                    case Keys.PageDown:
                        step = VisibleRows - 1;
                        break;
                    case Keys.PageUp:
                        step = 1 - VisibleRows;
                        break;
                    case Keys.Enter:
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        Choose(list.SelectedItem as string, false);
                        return;
                    default:
                        return;
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (list.Items.Count > 0)
                {
                    list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + step));
                }
            }

            private void Choose(string answer, bool byClick)
            {
                if (answer == null || done)
                {
                    return;
                }
                done = true;
                Close();
                choose(answer, byClick);
            }
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
