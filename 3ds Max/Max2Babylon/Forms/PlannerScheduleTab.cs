using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the Schedule tab of the exporter window. Imports the Planner's layer list (CSV), shows it as a tree
    /// with each row's status in the scene (with no list, the scene's own phasing tree), lists the scene's phasing layers
    /// the list does not have, and builds the layers and helpers and links the objects to their helpers, each after a
    /// review and in one undo step. Fill IN / RM from names reads every object as the Planner's Generate from model would
    /// and gives each row its IN and RM (the side list shows them; selecting there selects in 3ds Max); Add to IN / RM and
    /// Remove rename the objects' tags after a review; Add layer makes a layer or phase group with its helper. The date
    /// scrubber previews the scene as the Planner's 4D view shows it at a date (not while an export runs). The last
    /// imported list, the layers added in Max and the Planner's project start are kept with the scene and shown again
    /// when the window opens. Built in code; the window holds it in its Schedule page.
    /// </summary>
    internal sealed class PlannerScheduleTab : UserControl
    {
        private const int MessageLines = 25;
        private const int NoteLines = 6;
        private static readonly Color NewBack = Color.FromArgb(226, 243, 226);
        private static readonly Color ChangeBack = Color.FromArgb(255, 243, 205);
        private static readonly Color ProblemBack = Color.FromArgb(248, 215, 218);
        private static readonly Color AddedBack = Color.FromArgb(222, 235, 250);
        private static readonly Color ExtraBack = Color.FromArgb(240, 240, 240);
        private static readonly Color TbcZoneBack = Color.FromArgb(255, 236, 214);

        // The scene changes the tab follows: it shows the new scene's own list.
        private static readonly Autodesk.Max.SystemNotificationCode[] SceneChanges =
        {
            Autodesk.Max.SystemNotificationCode.FilePostOpen,
            Autodesk.Max.SystemNotificationCode.SystemPostNew,
            Autodesk.Max.SystemNotificationCode.SystemPostReset
        };

        private readonly Button importButton = new Button();
        private readonly Button refreshButton = new Button();
        private readonly Button buildButton = new Button();
        private readonly Button linkButton = new Button();
        private readonly Button fillButton = new Button();
        private readonly Button addLayerButton = new Button();
        private readonly Label listLabel = new Label();
        private readonly Label sceneLabel = new Label();
        private readonly Label notesLabel = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly DataGridView unlistedGrid = new DataGridView();
        private readonly DataGridView notScheduledGrid = new DataGridView();
        private readonly TabControl bottomTabs = new TabControl();
        private readonly TabPage unlistedPage = new TabPage();
        private readonly TabPage notScheduledPage = new TabPage();
        private readonly Label unlistedLabel = new Label();
        private readonly Label sideLabel = new Label();
        private readonly DataGridView sideGrid = new DataGridView();
        private readonly Button addInButton = new Button();
        private readonly Button addRmButton = new Button();
        private readonly Button removeButton = new Button();
        private readonly CheckBox previewBox = new CheckBox();
        private readonly TrackBar scrubber = new TrackBar();
        private readonly CheckBox endOfDayBox = new CheckBox();
        private readonly Label dateLabel = new Label();
        private readonly Label activeLabel = new Label();
        private readonly Button selectDayButton = new Button();
        private readonly Label projectStartLabel = new Label();
        private readonly DateTimePicker projectStartPicker = new DateTimePicker();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly SplitContainer split = new SplitContainer();
        private readonly SplitContainer sideSplit = new SplitContainer();
        private bool splitPlaced;
        private readonly Font boldFont;
        private readonly PlannerPreviewController preview = new PlannerPreviewController();

        private PlannerLayerList list;
        private string listText;
        private string listFile;
        private DateTime? listImported;
        private List<PlannerAddedLayer> added = new List<PlannerAddedLayer>();
        // The Planner workspace's project start, when the modeller gives it (kept with the scene); null: the earliest
        // dated layer is assumed.
        private DateTime? projectStart;
        private bool updatingStart;
        private PlannerLayerPlan plan;
        private PlannerSceneModel sceneModel;
        private PlannerSchedule schedule;
        private bool filled;
        private List<PlannerScheduleRow> treeRows = new List<PlannerScheduleRow>();
        private List<PlannerScheduleRow> unlistedRows = new List<PlannerScheduleRow>();
        private PlannerScheduleRow currentRow;
        private bool updatingSide;
        // The lists are being filled: their selections are not the modeller's.
        private bool updatingLists;
        private bool updatingPreview;
        private string readError;
        private bool stale = true;
        // Another scene was opened (or File > New, Reset) before the tab was ever shown.
        private bool sceneSwitched;
        // Kept in a field so it lives as long as the notifications it is registered for.
        private Autodesk.Max.GlobalDelegates.Delegate5 sceneChanged;

        /// <summary>Asked before a change to the scene: false while an export runs (the exporter window says so).</summary>
        public Func<bool> CanChangeScene;

        public PlannerScheduleTab()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Inherit;
            boldFont = new Font(Font, FontStyle.Bold);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 4)
            };
            SetUpButton(importButton, "Import layer list\u2026", ImportButton_Click,
                "Read the layer list the Planner exports (its Layer list for 3ds Max (CSV) export). It is kept with the scene.");
            SetUpButton(refreshButton, "Read the scene again", (s, e) => Replan(), "Work out each row's status (and, once filled, its IN and RM) in the scene as it is now.");
            SetUpButton(buildButton, "Build layers and helpers\u2026", BuildButton_Click,
                "Create, rename and move the layers and their helpers to match the list, after a review. Nothing is deleted.");
            SetUpButton(linkButton, "Link objects to their helpers\u2026", LinkButton_Click,
                "Link every object on a phasing layer under that layer's helper, after a review.");
            SetUpButton(fillButton, "Fill IN / RM from names", FillButton_Click,
                "Read every object as the Planner's Generate from model would: each row's IN and RM are the objects whose install and removal its dates place.");
            SetUpButton(addLayerButton, "Add layer\u2026", AddLayerButton_Click,
                "Add a layer or a group, with its helper, under the selected row (or the phasing root). It reaches the Planner with the next GLB.");
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(refreshButton);
            buttons.Controls.Add(buildButton);
            buttons.Controls.Add(linkButton);
            buttons.Controls.Add(fillButton);
            buttons.Controls.Add(addLayerButton);

            // The Planner workspace's project start: Generate from model only moves it back, so a workspace made before
            // its works start keeps its earlier start, where undated layers' work and installs no dated layer has sit.
            var startPanel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
            projectStartLabel.Text = "Planner project start:";
            projectStartLabel.AutoSize = true;
            projectStartLabel.Margin = new Padding(0, 6, 3, 0);
            projectStartPicker.Format = DateTimePickerFormat.Custom;
            projectStartPicker.CustomFormat = "d MMM yyyy";
            projectStartPicker.ShowCheckBox = true;
            projectStartPicker.Width = 130;
            projectStartPicker.ValueChanged += ProjectStart_Changed;
            const string startTip = "The Planner workspace's project start: the first day of its Gantt (a new workspace starts on the day it is made). "
                + "Generate from model moves it back to the earliest dated layer, never forward; undated (TBC) layers' work and installs no dated layer has sit on it. "
                + "Tick it and give the date when the workspace starts before its works. Unticked, the tab takes the earliest dated layer. Kept with the scene.";
            toolTip.SetToolTip(projectStartLabel, startTip);
            toolTip.SetToolTip(projectStartPicker, startTip);
            startPanel.Controls.Add(projectStartLabel);
            startPanel.Controls.Add(projectStartPicker);
            buttons.Controls.Add(startPanel);

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(6, 6, 6, 4)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            foreach (var label in new[] { listLabel, sceneLabel, notesLabel })
            {
                label.AutoSize = true;
                label.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                label.Margin = new Padding(0, 0, 0, 3);
            }
            listLabel.Font = boldFont;
            notesLabel.ForeColor = SystemColors.GrayText;
            top.Controls.Add(buttons, 0, 0);
            top.Controls.Add(listLabel, 0, 1);
            top.Controls.Add(sceneLabel, 0, 2);
            top.Controls.Add(notesLabel, 0, 3);

            SetUpGrid(grid);
            AddColumn(grid, "Layer", 40);
            AddColumn(grid, "Kind", 6);
            AddColumn(grid, "Start", 8);
            AddColumn(grid, "Finish", 8);
            AddColumn(grid, "TBC", 4);
            AddColumn(grid, "IN", 4);
            AddColumn(grid, "RM", 4);
            AddColumn(grid, "Status in the scene", 18);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.SelectionChanged += (s, e) =>
            {
                if (!updatingLists)
                {
                    RowChosen(grid);
                }
            };

            SetUpGrid(unlistedGrid);
            AddColumn(unlistedGrid, "Name", 40);
            AddColumn(unlistedGrid, "What", 14);
            AddColumn(unlistedGrid, "IN", 4);
            AddColumn(unlistedGrid, "RM", 4);
            AddColumn(unlistedGrid, "Note", 38);
            unlistedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            unlistedGrid.MultiSelect = false;
            unlistedGrid.SelectionChanged += (s, e) =>
            {
                if (!updatingLists)
                {
                    RowChosen(unlistedGrid);
                }
            };
            unlistedLabel.Dock = DockStyle.Top;
            unlistedLabel.AutoSize = true;
            unlistedLabel.Padding = new Padding(0, 4, 0, 4);
            unlistedLabel.Font = boldFont;
            unlistedPage.Controls.Add(unlistedGrid);
            unlistedPage.Controls.Add(unlistedLabel);
            unlistedPage.Text = "Not in the Planner's list";

            SetUpGrid(notScheduledGrid);
            AddColumn(notScheduledGrid, "Object", 36);
            AddColumn(notScheduledGrid, "Max layer", 24);
            AddColumn(notScheduledGrid, "Why the Planner does not schedule it", 40);
            notScheduledGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            notScheduledGrid.SelectionChanged += (s, e) =>
            {
                if (!updatingLists)
                {
                    SelectInMax(notScheduledGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<PlannerNotScheduled>().Select(n => n.Key));
                }
            };
            notScheduledPage.Controls.Add(notScheduledGrid);
            notScheduledPage.Text = "Not scheduled";
            bottomTabs.Dock = DockStyle.Fill;
            bottomTabs.TabPages.Add(unlistedPage);
            bottomTabs.TabPages.Add(notScheduledPage);

            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.FixedPanel = FixedPanel.Panel2;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(bottomTabs);
            // The lists below get a quarter of the height, once the tab has its real size; they then keep their height
            // when the window is resized.
            split.SizeChanged += (s, e) => PlaceSplitter();

            // The side list: the chosen row's IN and RM.
            sideLabel.Dock = DockStyle.Top;
            sideLabel.AutoSize = true;
            sideLabel.Padding = new Padding(4, 4, 4, 4);
            sideLabel.Font = boldFont;
            SetUpGrid(sideGrid);
            AddColumn(sideGrid, "", 8);
            AddColumn(sideGrid, "Object", 56);
            AddColumn(sideGrid, "Note", 36);
            sideGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            sideGrid.MultiSelect = true;
            sideGrid.SelectionChanged += (s, e) =>
            {
                if (!updatingSide)
                {
                    SelectInMax(sideGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<SideEntry>().Select(x => x.Key));
                }
            };
            var sideButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(2) };
            SetUpButton(addInButton, "Add selected to IN\u2026", (s, e) => AddSelected(PlannerTaskType.Install),
                "Give the objects selected in 3ds Max this layer's install tag (their names change; nothing is moved or linked), after a review.");
            SetUpButton(addRmButton, "Add selected to RM\u2026", (s, e) => AddSelected(PlannerTaskType.Dismantle),
                "Give the objects selected in 3ds Max this layer's removal tag (their names change; nothing is moved or linked), after a review.");
            SetUpButton(removeButton, "Remove from the list\u2026", RemoveButton_Click,
                "Take the objects chosen in this list out of it: their tag of that column is taken out of their names, after a review.");
            sideButtons.Controls.Add(addInButton);
            sideButtons.Controls.Add(addRmButton);
            sideButtons.Controls.Add(removeButton);
            sideSplit.Dock = DockStyle.Fill;
            sideSplit.Orientation = Orientation.Vertical;
            sideSplit.FixedPanel = FixedPanel.Panel2;
            sideSplit.Panel1.Controls.Add(split);
            sideSplit.Panel2.Controls.Add(sideGrid);
            sideSplit.Panel2.Controls.Add(sideButtons);
            sideSplit.Panel2.Controls.Add(sideLabel);
            sideSplit.SizeChanged += (s, e) => PlaceSplitter();

            // The date scrubber. Nothing in its row changes size as it moves (the date and the count have room for their
            // longest text, the layers at work get two lines and the rest in the tooltip), so the slider stays put under the
            // pointer while it is dragged; see FitWidths.
            var scrub = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 2, Padding = new Padding(6, 2, 6, 4) };
            scrub.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scrub.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scrub.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scrub.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            previewBox.Appearance = Appearance.Button;
            previewBox.Text = "Preview in the viewport";
            previewBox.AutoSize = true;
            previewBox.FlatStyle = FlatStyle.Flat;
            previewBox.Anchor = AnchorStyles.Left;
            previewBox.CheckedChanged += PreviewBox_CheckedChanged;
            toolTip.SetToolTip(previewBox, "Hide the objects the Planner's 4D view hides at the scrubber's date. Leaving gives every object its own hidden state back; it is never an undo step.");
            scrubber.Dock = DockStyle.Fill;
            scrubber.TickStyle = TickStyle.None;
            scrubber.Minimum = 0;
            scrubber.Maximum = 0;
            scrubber.ValueChanged += (s, e) => ScrubTo();
            endOfDayBox.Text = "End of the day";
            endOfDayBox.AutoSize = true;
            endOfDayBox.Anchor = AnchorStyles.Left;
            endOfDayBox.CheckedChanged += (s, e) => ScrubTo();
            toolTip.SetToolTip(endOfDayBox, "Show the day once its work is done. Unticked, the start of the day, as the Planner's 4D view shows a day.");
            SetUpButton(selectDayButton, "Select changing this day", SelectDayButton_Click, "Select in 3ds Max the objects installed or removed on this day.");
            selectDayButton.AutoSize = false;
            selectDayButton.Anchor = AnchorStyles.Left;
            dateLabel.AutoSize = false;
            dateLabel.Font = boldFont;
            dateLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            dateLabel.TextAlign = ContentAlignment.TopLeft;
            activeLabel.AutoSize = false;
            activeLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            activeLabel.AutoEllipsis = true;
            scrub.Controls.Add(previewBox, 0, 0);
            scrub.Controls.Add(scrubber, 1, 0);
            scrub.Controls.Add(endOfDayBox, 2, 0);
            scrub.Controls.Add(selectDayButton, 3, 0);
            scrub.Controls.Add(dateLabel, 0, 1);
            scrub.Controls.Add(activeLabel, 1, 1);
            scrub.SetColumnSpan(activeLabel, 3);

            Controls.Add(sideSplit);
            Controls.Add(scrub);
            Controls.Add(top);
            FitWidths();
            ResumeLayout(false);
            PerformLayout();

            preview.Ended += () =>
            {
                if (IsDisposed)
                {
                    return;
                }
                updatingPreview = true;
                previewBox.Checked = false;
                updatingPreview = false;
            };
            VisibleChanged += (s, e) =>
            {
                if (!Visible)
                {
                    EndPreview();
                }
            };

            LoadSavedList();
            ShowList();
            WatchScene(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                preview.Dispose();
                WatchScene(false);
                toolTip.Dispose();
                boldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void PlaceSplitter()
        {
            if (splitPlaced || split.Height < 200 || sideSplit.Width < 400)
            {
                return;
            }
            splitPlaced = true;
            try
            {
                split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height - Math.Max(120, split.Height / 4));
                sideSplit.SplitterDistance = Math.Max(sideSplit.Panel1MinSize, sideSplit.Width - Math.Max(240, sideSplit.Width / 4));
            }
            catch (Exception)
            {
                // The splits keep their own division.
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitWidths();
        }

        private static string DateText(DateTime day, bool endOfDay)
        {
            return day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture) + (endOfDay ? ", end of the day" : ", start of the day");
        }

        private static string SelectDayText(int count)
        {
            return "Select changing this day (" + count.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // Sizes measured once from the longest text each control can hold, so nothing in the scrubber's row moves as it
        // moves (the date of any day, bold; the day's count up to 9999; two lines of layers at work, the rest in the
        // tooltip), and the tree's dates and kinds are never cut short.
        private void FitWidths()
        {
            try
            {
                var bold = dateLabel.Font;
                int dateWidth = 0;
                for (var day = new DateTime(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
                {
                    dateWidth = Math.Max(dateWidth, TextRenderer.MeasureText(DateText(day, false), bold).Width);
                }
                dateLabel.Size = new Size(dateWidth + 8, bold.Height + 6);
                selectDayButton.Size = new Size(TextRenderer.MeasureText(SelectDayText(9999), selectDayButton.Font).Width + 24, Math.Max(23, selectDayButton.Font.Height + 10));
                activeLabel.Height = activeLabel.Font.Height * 2 + 4;
                int dateColumn = TextRenderer.MeasureText("2026-10-17", grid.Font).Width + 14;
                grid.Columns[1].MinimumWidth = TextRenderer.MeasureText("Group", grid.Font).Width + 14;
                grid.Columns[2].MinimumWidth = dateColumn;
                grid.Columns[3].MinimumWidth = dateColumn;
                grid.Columns[4].MinimumWidth = TextRenderer.MeasureText("TBC", grid.Font).Width + 14;
            }
            catch (Exception)
            {
                // The controls keep the sizes they have.
            }
        }

        /// <summary>Called when the Schedule tab is shown: works out the statuses, as the scene may have changed.</summary>
        public void OnShown()
        {
            if (sceneSwitched)
            {
                sceneSwitched = false;
                LoadSavedList();
            }
            Replan();
        }

        /// <summary>Called when the window shows another tab: the preview ends, giving every object its own state back.</summary>
        public void OnHidden()
        {
            EndPreview();
        }

        private void SetUpButton(Button button, string text, EventHandler click, string tip)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(80, 23);
            button.UseVisualStyleBackColor = true;
            button.FlatStyle = FlatStyle.Flat;
            button.Click += click;
            toolTip.SetToolTip(button, tip);
        }

        private static void SetUpGrid(DataGridView view)
        {
            view.Dock = DockStyle.Fill;
            view.AllowUserToAddRows = false;
            view.AllowUserToDeleteRows = false;
            view.AllowUserToResizeRows = false;
            view.ReadOnly = true;
            view.RowHeadersVisible = false;
            view.SelectionMode = DataGridViewSelectionMode.CellSelect;
            view.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            view.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            view.BackgroundColor = SystemColors.Window;
            view.BorderStyle = BorderStyle.None;
            view.ShowCellToolTips = true;
        }

        private static void AddColumn(DataGridView view, string header, float weight)
        {
            view.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MinimumWidth = 30
            });
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
                // Without the notifications the tab still reads the scene again on Read the scene again and before changes.
            }
        }

        private void OnSceneChanged(IntPtr param, Autodesk.Max.INotifyInfo info)
        {
            if (IsDisposed)
            {
                return;
            }
            if (!IsHandleCreated)
            {
                // Not shown yet: the new scene's list is read when the tab is.
                sceneSwitched = true;
                return;
            }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed)
                    {
                        return;
                    }
                    EndPreview();
                    LoadSavedList();
                    plan = null;
                    schedule = null;
                    filled = false;
                    stale = true;
                    if (Visible)
                    {
                        Replan();
                    }
                    else
                    {
                        ShowList();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
            }
        }

        // The list

        private void LoadSavedList()
        {
            list = null;
            listText = null;
            listFile = null;
            listImported = null;
            added = new List<PlannerAddedLayer>();
            projectStart = null;
            try
            {
                string text, file;
                DateTime? imported;
                if (PlannerScheduleScene.TryLoadList(out text, out file, out imported))
                {
                    listText = text;
                    listFile = file;
                    listImported = imported;
                    list = PlannerLayerList.Parse(text);
                }
                added = PlannerScheduleScene.LoadAdded();
                projectStart = PlannerScheduleScene.LoadProjectStart();
            }
            catch (Exception)
            {
                list = null;
            }
            ShowProjectStart();
        }

        private void ShowProjectStart()
        {
            updatingStart = true;
            try
            {
                // The value first: setting it ticks the box.
                projectStartPicker.Value = projectStart ?? DateTime.Today;
                projectStartPicker.Checked = projectStart.HasValue;
            }
            catch (Exception)
            {
                // The picker shows what it had.
            }
            finally
            {
                updatingStart = false;
            }
        }

        private void ProjectStart_Changed(object sender, EventArgs e)
        {
            if (updatingStart)
            {
                return;
            }
            DateTime? value = projectStartPicker.Checked ? projectStartPicker.Value.Date : (DateTime?)null;
            if (value == projectStart)
            {
                return;
            }
            projectStart = value;
            try
            {
                PlannerScheduleScene.SaveProjectStart(projectStart);
            }
            catch (Exception)
            {
                // Used for now; the scene keeps what it had.
            }
            if (filled)
            {
                Replan();
            }
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Import the Planner's layer list";
                dialog.Filter = "Layer lists (*.csv)|*.csv|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                try
                {
                    var folder = !string.IsNullOrEmpty(listFile) ? Path.GetDirectoryName(listFile) : null;
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    {
                        var scenePath = Loader.Core.CurFilePath;
                        folder = string.IsNullOrEmpty(scenePath) ? null : Path.GetDirectoryName(scenePath);
                    }
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
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
                PlannerLayerList read;
                string text;
                try
                {
                    string note;
                    text = PlannerLayerList.Decode(File.ReadAllBytes(dialog.FileName), out note);
                    read = PlannerLayerList.Parse(text);
                    if (note != null)
                    {
                        read.Notes.Insert(0, note);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The file could not be read: " + ex.Message, "Import layer list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!read.Usable)
                {
                    MessageBox.Show(this, Path.GetFileName(dialog.FileName) + " is not a layer list the Planner exported, so nothing was imported.\r\n\r\n"
                        + string.Join("\r\n", read.Errors.ToArray()), "Import layer list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                list = read;
                listText = text;
                listFile = dialog.FileName;
                listImported = DateTime.UtcNow;
                try
                {
                    PlannerScheduleScene.SaveList(listText, listFile, listImported.Value);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The list is shown but could not be saved with the scene: " + ex.Message, "Import layer list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                // A layer added in Max that the Planner's list now has is the Planner's from then on (Replan).
                Replan();
            }
        }

        // The statuses

        private PlannerScheduleOptions Options()
        {
            return new PlannerScheduleOptions { Zone = TimeZoneInfo.Local, Today = DateTime.Today, WorkspaceStart = projectStart };
        }

        private PlannerSceneSnapshot ReadScene()
        {
            return PlannerScheduleScene.Read(preview.Active ? preview.HiddenKeys : null);
        }

        private void Replan()
        {
            readError = null;
            plan = null;
            sceneModel = null;
            try
            {
                var snapshot = ReadScene();
                sceneModel = new PlannerSceneModel(snapshot);
                if (list != null && list.Usable)
                {
                    plan = PlannerLayerPlanner.Plan(list, sceneModel);
                }
                PruneAdded();
                if (filled)
                {
                    schedule = PlannerSchedule.Build(snapshot, Options());
                }
            }
            catch (Exception ex)
            {
                readError = "The scene could not be read: " + ex.Message;
            }
            stale = false;
            ShowList();
            if (preview.Active && schedule != null)
            {
                ScrubTo();
            }
        }

        // The layers added in Max as the scene has them: one taken back with Edit > Undo, deleted or renamed by hand since,
        // or one the Planner's list now has (the Planner's from then on), is no longer an added layer, and the record kept
        // with the scene says so too (unless an export is running: then on the next reading).
        private void PruneAdded()
        {
            if (sceneModel == null || added.Count == 0)
            {
                return;
            }
            var kept = PlannerAddedLayerStore.Prune(added, sceneModel, plan);
            if (kept.Count == added.Count)
            {
                return;
            }
            added = kept;
            if (!SceneFree())
            {
                return;
            }
            try
            {
                PlannerScheduleScene.SaveAdded(added);
            }
            catch (Exception)
            {
                // The scene keeps the old record; it is pruned again on the next reading.
            }
        }

        private void ShowList()
        {
            updatingLists = true;
            try
            {
                FillLists();
            }
            finally
            {
                updatingLists = false;
            }
            RowChosen(grid);
            ShowScrubber();
        }

        private void FillLists()
        {
            int firstRow = grid.Rows.Count > 0 ? grid.FirstDisplayedScrollingRowIndex : -1;
            var chosen = currentRow != null ? currentRow.LayerName ?? currentRow.Name : null;
            grid.Rows.Clear();
            unlistedGrid.Rows.Clear();
            notScheduledGrid.Rows.Clear();
            currentRow = null;

            var notes = new List<string>();
            if (list == null)
            {
                listLabel.Text = "No layer list imported yet: the scene's own phasing tree is shown.";
                sceneLabel.Text = readError ?? "Export the list from the Planner (Layer list for 3ds Max (CSV)) and import it here: Build layers and helpers then makes "
                    + "one layer and one helper of the same name for each row, nested as in the Planner.";
                unlistedLabel.Text = "Phasing layers in the scene not in the Planner's list are listed here once a list is imported.";
                buildButton.Enabled = false;
            }
            else
            {
                var source = listFile != null ? Path.GetFileName(listFile) : "The imported list";
                listLabel.Text = source + (listImported.HasValue ? ", imported " + listImported.Value.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture) : string.Empty)
                    + ": " + list.Describe();
                notes.AddRange(list.Notes);
                if (plan != null)
                {
                    sceneLabel.Text = "In the scene: " + plan.Summary();
                    notes.AddRange(plan.Notes);
                }
                else
                {
                    sceneLabel.Text = readError ?? (stale ? "Not compared with the scene yet: Read the scene again." : "In the scene: not known.");
                }
                buildButton.Enabled = list.Usable;
            }
            if (filled && schedule != null)
            {
                sceneLabel.Text += "\r\n" + FillSummary();
                if (schedule.Usable)
                {
                    notes.AddRange(schedule.LayerIssues.Select(i => "Layer name: " + i));
                }
            }
            ShowNotes(notes);

            treeRows = PlannerScheduleRows.Tree(list, plan, sceneModel, added, filled ? schedule : null);
            unlistedRows = PlannerScheduleRows.Unlisted(plan, added, filled ? schedule : null);
            var extra = filled ? PlannerScheduleRows.Extra(treeRows.Concat(unlistedRows), schedule) : new List<PlannerScheduleRow>();
            var lines = new List<DataGridViewRow>();
            foreach (var row in treeRows.Concat(extra))
            {
                var line = new DataGridViewRow { Tag = row };
                line.CreateCells(grid, row.Name, row.Kind, row.Start, row.Finish, row.Tbc ? "Yes" : string.Empty, Count(row, true), Count(row, false), row.Status);
                // The tree: each level indented.
                line.Cells[0].Style.Padding = new Padding(4 + 16 * row.Depth, 0, 0, 0);
                if (row.Kind == "Group")
                {
                    line.Cells[0].Style.Font = boldFont;
                }
                line.DefaultCellStyle.BackColor = BackOf(row);
                line.Cells[7].ToolTipText = Details(row);
                line.Cells[0].ToolTipText = row.ListRow != null
                    ? "Planner ID " + row.ListRow.PlannerId + (row.ListRow.IdKey != null ? "; layer ID " + row.ListRow.IdKey : string.Empty)
                    : row.LayerName ?? string.Empty;
                lines.Add(line);
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

            if (plan == null)
            {
                unlistedLabel.Text = list == null ? unlistedLabel.Text : "In the scene but not in the Planner's list: not known until the scene is read.";
            }
            else
            {
                unlistedLabel.Text = unlistedRows.Count == 0
                    ? "Every phasing layer in the scene is in the Planner's list."
                    : "In the scene but not in the Planner's list \u2014 left as they are (" + unlistedRows.Count.ToString(CultureInfo.InvariantCulture) + "):";
                unlistedGrid.Rows.AddRange(unlistedRows.Select(r =>
                {
                    var line = new DataGridViewRow { Tag = r };
                    line.CreateCells(unlistedGrid, r.Name, r.UnlistedItem != null ? r.UnlistedItem.What : string.Empty, Count(r, true), Count(r, false), r.Status);
                    return line;
                }).ToArray());
            }
            unlistedPage.Text = "Not in the Planner's list" + (plan != null ? " (" + unlistedRows.Count.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
            if (filled && schedule != null)
            {
                notScheduledGrid.Rows.AddRange(schedule.NotScheduled.Select(n =>
                {
                    var line = new DataGridViewRow { Tag = n };
                    line.CreateCells(notScheduledGrid, n.Name, n.LayerName ?? string.Empty, n.Reason);
                    line.Cells[2].ToolTipText = n.Reason;
                    return line;
                }).ToArray());
                notScheduledPage.Text = "Not scheduled (" + schedule.NotScheduled.Count.ToString(CultureInfo.InvariantCulture) + ")";
            }
            else
            {
                notScheduledPage.Text = "Not scheduled";
            }
            linkButton.Enabled = true;
            addLayerButton.Enabled = true;

            // The chosen row again, if it is still there.
            if (chosen != null)
            {
                foreach (DataGridViewRow line in grid.Rows)
                {
                    var row = (PlannerScheduleRow)line.Tag;
                    if (string.Equals(row.LayerName ?? row.Name, chosen, StringComparison.Ordinal))
                    {
                        line.Selected = true;
                        break;
                    }
                }
            }
            if (grid.SelectedRows.Count == 0)
            {
                grid.ClearSelection();
            }
            unlistedGrid.ClearSelection();
            notScheduledGrid.ClearSelection();
        }

        private string FillSummary()
        {
            if (!schedule.Usable)
            {
                return "IN / RM: " + schedule.Message;
            }
            int installs = schedule.Activities.Where(a => a.Type == PlannerTaskType.Install).Sum(a => a.Members.Count);
            int removals = schedule.Activities.Where(a => a.Type == PlannerTaskType.Dismantle).Sum(a => a.Members.Count);
            return "IN / RM from names: " + PhasingFixScript.Count(schedule.Objects.Count, "object", "objects") + " scheduled in "
                + PhasingFixScript.Count(schedule.Layers.Count, "layer", "layers") + " (" + installs.ToString(CultureInfo.InvariantCulture) + " IN, "
                + removals.ToString(CultureInfo.InvariantCulture) + " RM), " + PhasingFixScript.Count(schedule.NotScheduled.Count, "not scheduled", "not scheduled")
                + "; project start " + schedule.ProjectStart.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                + (!projectStart.HasValue
                    ? " (the earliest dated layer, assumed to be the Planner's project start: give that start above when the workspace starts earlier)."
                    : schedule.ProjectStart < projectStart.Value
                        ? " (the earliest dated layer, before the Planner's project start: Generate from model moves the start back to it)."
                        : " (the Planner's project start).");
        }

        // The notes, a few lines of them; all of them in the tooltip.
        private void ShowNotes(List<string> notes)
        {
            var shown = notes.Take(NoteLines).ToList();
            if (notes.Count > NoteLines)
            {
                shown.Add("\u2026 and " + (notes.Count - NoteLines).ToString(CultureInfo.InvariantCulture) + " more (hover to read them all).");
            }
            notesLabel.Text = string.Join("\r\n", shown.ToArray());
            notesLabel.Visible = notes.Count > 0;
            toolTip.SetToolTip(notesLabel, string.Join("\r\n", notes.ToArray()));
        }

        private string Count(PlannerScheduleRow row, bool install)
        {
            if (!filled || schedule == null || !schedule.Usable)
            {
                return string.Empty;
            }
            int count = install ? row.InCount : row.RmCount;
            return count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static Color BackOf(PlannerScheduleRow row)
        {
            switch (row.Source)
            {
                case PlannerScheduleRowSource.Added:
                    return AddedBack;
                case PlannerScheduleRowSource.Outside:
                case PlannerScheduleRowSource.ByRule:
                    return ExtraBack;
            }
            if (row.State == PlannerRowState.New)
            {
                return NewBack;
            }
            if (row.State == PlannerRowState.Change)
            {
                return ChangeBack;
            }
            return row.State == PlannerRowState.Problem ? ProblemBack : SystemColors.Window;
        }

        private static string Details(PlannerScheduleRow row)
        {
            var parts = new List<string>();
            var rowPlan = row.RowPlan;
            if (rowPlan != null && rowPlan.Unit != null)
            {
                parts.Add("In the scene as " + rowPlan.Unit.Name + " (" + rowPlan.Unit.What + (rowPlan.FromOutsideRoot ? ", outside the phasing root" : string.Empty)
                    + "), " + PlannerLayerPlanner.FoundBy(rowPlan.MatchedBy) + ".");
            }
            if (row.ListRow != null)
            {
                parts.AddRange(row.ListRow.Problems.Select(p => "Problem: " + p + "."));
                parts.AddRange(row.ListRow.Notes.Select(n => "Note: " + n + "."));
            }
            if (rowPlan != null)
            {
                parts.AddRange(rowPlan.Notes.Select(n => "Note: " + n + "."));
            }
            if (row.Added != null)
            {
                var reach = PlannerAddLayer.ReachesThePlanner(row.Added.Kind);
                parts.Add(char.ToUpperInvariant(reach[0]) + reach.Substring(1) + "; the next list from the Planner then has it.");
            }
            if (row.Layer != null)
            {
                parts.Add("The Planner reads it as the layer " + row.Layer.Label + (row.Layer.Dated ? ", " + row.Layer.Start.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                    + (row.Layer.End != row.Layer.Start ? " to " + row.Layer.End.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : string.Empty) : ", undated (at the project start)")
                    + (row.Layer.Tbc ? ", TBC" : string.Empty) + (row.Layer.Phase.HasValue ? ", phase " + row.Layer.Phase.Value.ToString(CultureInfo.InvariantCulture) : string.Empty) + ".");
            }
            if (string.IsNullOrEmpty(row.Status) == false && parts.Count == 0)
            {
                parts.Add(row.Status);
            }
            return string.Join("\r\n", parts.ToArray());
        }

        // The side list: the chosen row's IN and RM

        private sealed class SideEntry
        {
            public long Key;
            public string Name;
            public PlannerTaskType Type;
        }

        private void RowChosen(DataGridView source)
        {
            if (source.SelectedRows.Count > 0)
            {
                if (source == grid)
                {
                    unlistedGrid.ClearSelection();
                }
                else
                {
                    grid.ClearSelection();
                }
                currentRow = source.SelectedRows[0].Tag as PlannerScheduleRow;
            }
            else if (grid.SelectedRows.Count == 0 && unlistedGrid.SelectedRows.Count == 0)
            {
                currentRow = null;
            }
            ShowSide();
        }

        private void ShowSide()
        {
            updatingSide = true;
            try
            {
                sideGrid.Rows.Clear();
                var row = currentRow;
                if (row == null)
                {
                    sideLabel.Text = "Choose a row to see its IN and RM.";
                }
                else if (!filled || schedule == null)
                {
                    sideLabel.Text = row.Name + ": press Fill IN / RM from names to see its IN and RM.";
                }
                else if (!schedule.Usable)
                {
                    sideLabel.Text = schedule.Message;
                }
                else
                {
                    sideLabel.Text = row.Name + ": IN " + row.InCount.ToString(CultureInfo.InvariantCulture) + ", RM " + row.RmCount.ToString(CultureInfo.InvariantCulture);
                    var lines = new List<DataGridViewRow>();
                    foreach (var pair in new[] { new KeyValuePair<string, List<PlannerScheduleActivity>>("IN", row.In), new KeyValuePair<string, List<PlannerScheduleActivity>>("RM", row.Rm) })
                    {
                        foreach (var activity in pair.Value)
                        {
                            foreach (var name in activity.Members)
                            {
                                PlannerScheduleObject obj;
                                schedule.Objects.TryGetValue(name, out obj);
                                var line = new DataGridViewRow { Tag = new SideEntry { Key = obj != null ? obj.Key : 0, Name = name, Type = activity.Type } };
                                line.CreateCells(sideGrid, pair.Key, name, SideNote(row, activity, obj));
                                line.Cells[2].ToolTipText = activity.Name + ": " + Times(activity);
                                lines.Add(line);
                            }
                        }
                    }
                    sideGrid.Rows.AddRange(lines.ToArray());
                    sideGrid.ClearSelection();
                }
                bool editable = row != null && row.Editable && filled && schedule != null && schedule.Usable;
                addInButton.Enabled = editable;
                addRmButton.Enabled = editable;
                removeButton.Enabled = editable;
            }
            finally
            {
                updatingSide = false;
            }
        }

        private static string SideNote(PlannerScheduleRow row, PlannerScheduleActivity activity, PlannerScheduleObject obj)
        {
            var parts = new List<string>();
            if (obj != null && obj.MaxName != null && obj.MaxName != obj.Name)
            {
                parts.Add("called " + obj.MaxName + " in 3ds Max: another node the export writes before it has that name, so the export renames it and the Planner reads it as an object of its own");
            }
            if (obj != null && obj.FiledIn != null && obj.FiledIn != row.Layer)
            {
                parts.Add("sits in " + obj.FiledIn.Raw);
            }
            if (activity.Unresolved)
            {
                parts.Add("no dated layer has " + activity.StageText + (activity.Type == PlannerTaskType.Install ? ": at the project start" : ": after the end of the works"));
            }
            if (activity.Tbc)
            {
                parts.Add("TBC");
            }
            return string.Join("; ", parts.ToArray());
        }

        private string Times(PlannerScheduleActivity activity)
        {
            var from = schedule.LocalTime(activity.StartMs);
            var to = schedule.LocalTime(activity.EndMs);
            return from.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture) + " to " + to.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)
                + (activity.Type == PlannerTaskType.Install ? " (shows from its start)" : " (hidden from its finish)");
        }

        private void SelectInMax(IEnumerable<long> keys)
        {
            var wanted = keys.Where(k => k > 0).Distinct().ToList();
            // While an export runs the selection stays as it is (an export of the selected objects reads it as it goes).
            if (wanted.Count == 0 || !SceneFree())
            {
                return;
            }
            try
            {
                PlannerScheduleScene.Select(wanted);
            }
            catch (Exception)
            {
                // The selection stays as it was.
            }
        }

        // Fill and the IN / RM edits

        private void FillButton_Click(object sender, EventArgs e)
        {
            filled = true;
            Cursor = Cursors.WaitCursor;
            try
            {
                Replan();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            if (schedule != null && !schedule.Usable)
            {
                MessageBox.Show(this, schedule.Message, "Fill IN / RM from names", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AddSelected(PlannerTaskType type)
        {
            var row = currentRow;
            if (row == null || !row.Editable || schedule == null)
            {
                return;
            }
            var keys = PlannerScheduleScene.SelectedKeys();
            if (keys.Count == 0)
            {
                MessageBox.Show(this, "Select the objects in 3ds Max first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // The layer as the Planner reads it there (its helper's name), which may have drifted from the Max layer's.
            var target = PlannerTagTarget.For(row, schedule);
            if (target == null)
            {
                return;
            }
            var requests = keys.Select(k => new PlannerTagEditRequest { Key = k, Type = type, Kind = PlannerTagEditKind.Add, Target = target }).ToList();
            ApplyEdit(requests, (type == PlannerTaskType.Install ? "Add to IN of " : "Add to RM of ") + target.LayerName);
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            var row = currentRow;
            if (row == null || !row.Editable || schedule == null)
            {
                return;
            }
            var entries = sideGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<SideEntry>().Where(x => x.Key > 0).ToList();
            if (entries.Count == 0)
            {
                MessageBox.Show(this, "Choose the objects to take out in the list first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var target = PlannerTagTarget.For(row, schedule);
            if (target == null)
            {
                return;
            }
            var requests = entries.Select(x => new PlannerTagEditRequest { Key = x.Key, Type = x.Type, Kind = PlannerTagEditKind.Remove, Target = target }).ToList();
            ApplyEdit(requests, "Remove from " + target.LayerName);
        }

        // Plans the renames, shows the review, checks the scene has not changed, renames in one undo record, fills again.
        private void ApplyEdit(List<PlannerTagEditRequest> requests, string title)
        {
            if (!MayChange())
            {
                return;
            }
            EndPreview();
            PlannerTagEditPlan reviewed;
            try
            {
                reviewed = PlannerTagEdit.Plan(PlannerScheduleScene.Read(), Options(), requests);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!reviewed.HasChanges && reviewed.ProblemCount == 0)
            {
                MessageBox.Show(this, "There is nothing to rename: " + reviewed.Summary(), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool hold;
            using (var review = new PlannerReviewForm(title, reviewed.Summary(), reviewed.Notes, reviewed.Review, reviewed.HasChanges, "Apply", "planner_in_rm"))
            {
                if (review.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                hold = review.HoldFirst;
            }
            PlannerTagEditPlan fresh;
            try
            {
                fresh = PlannerTagEdit.Plan(PlannerScheduleScene.Read(), Options(), requests);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (fresh.Fingerprint() != reviewed.Fingerprint())
            {
                MessageBox.Show(this, "The scene has changed since the list was reviewed. Try again to see the new list.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int renamed;
            List<string> failures;
            bool completed;
            string aborted;
            bool held = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (hold)
                {
                    Loader.Core.FileHold();
                    held = true;
                }
                PlannerScheduleScene.ApplyRenames(fresh, out renamed, out failures, out completed, out aborted);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                Replan();
                MessageBox.Show(this, "The objects could not be renamed: " + ex.Message + (held ? "\r\n\r\nEdit > Fetch puts the scene back as it was before." : string.Empty),
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;
            filled = true;
            Replan();
            var lines = new List<string>();
            if (!completed)
            {
                lines.Add("3ds Max did not report back, so it is not known how far the renaming got.");
            }
            if (aborted != null)
            {
                lines.Add("The renaming stopped part-way: " + aborted);
            }
            lines.Add(renamed == 0 ? "Nothing was renamed." : "Done: " + PhasingFixScript.Count(renamed, "object renamed", "objects renamed") + "; the lists are filled again from the names.");
            if (failures.Count > 0)
            {
                lines.Add(PhasingFixScript.Count(failures.Count, "rename was not made:", "renames were not made:"));
                lines.AddRange(failures.Select(f => "- " + f));
            }
            lines.Add(held ? "Edit > Undo takes it back in one step; Edit > Fetch puts the scene back as it was before." : "Edit > Undo takes it back in one step.");
            MessageBox.Show(this, Shorten(string.Join("\r\n", lines.ToArray())), title, MessageBoxButtons.OK,
                failures.Count > 0 || aborted != null || !completed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // Add layer

        private void AddLayerButton_Click(object sender, EventArgs e)
        {
            if (!MayChange())
            {
                return;
            }
            var row = currentRow;
            string parent = null;
            if (row != null && row.Source != PlannerScheduleRowSource.ByRule && row.Source != PlannerScheduleRowSource.Outside)
            {
                if (row.LayerName == null || row.Unit == null || row.Unit.Layer == null)
                {
                    MessageBox.Show(this, row.Name + " has no Max layer in the scene yet: build the layers and helpers first, or choose another row (or none, for the phasing root).",
                        "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                parent = row.Unit.Layer.Name;
                // The Planner files a layer only in the phase group above it (or at the top).
                var parentProblem = PlannerAddLayer.ParentProblem(parent, PlannerAddLayer.PhaseGroupAbove(sceneModel, parent));
                if (parentProblem != null)
                {
                    MessageBox.Show(this, "Nothing can be added under " + parent + ": " + parentProblem + ".", "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            int phase;
            int? parentPhase = parent != null && PhasingNameFixer.TryReadPhaseGroup(parent, out phase) ? phase : (int?)null;
            if (!parentPhase.HasValue && parent != null)
            {
                var parsed = PlannerNaming.ParseLayerName(parent);
                parentPhase = parsed != null ? parsed.Phase : null;
            }
            PlannerNewLayer spec;
            using (var dialog = new PlannerAddLayerForm(parent ?? "the phasing root", parentPhase))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                spec = dialog.Spec;
            }
            EndPreview();
            PlannerAddLayerPlan reviewed;
            try
            {
                reviewed = PlannerAddLayer.Plan(PlannerScheduleScene.Read(), parent, spec, list, added);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!reviewed.CanApply)
            {
                MessageBox.Show(this, (reviewed.LayerName ?? "The layer") + " cannot be added: " + reviewed.Problem + ".", "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool hold;
            using (var review = new PlannerReviewForm("Add layer", "1 layer and its helper to add: " + reviewed.LayerName, reviewed.Build.Notes, reviewed.Review, true, "Apply", "planner_add_layer"))
            {
                if (review.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                hold = review.HoldFirst;
            }
            PlannerAddLayerPlan fresh;
            try
            {
                fresh = PlannerAddLayer.Plan(PlannerScheduleScene.Read(), parent, spec, list, added);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!fresh.CanApply || fresh.Build.Fingerprint() != reviewed.Build.Fingerprint())
            {
                MessageBox.Show(this, "The scene has changed since the layer was reviewed" + (fresh.Problem != null ? " (" + fresh.Problem + ")" : string.Empty) + ". Try again.",
                    "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PlannerLayerResult result;
            bool held = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (hold)
                {
                    Loader.Core.FileHold();
                    held = true;
                }
                result = PlannerScheduleScene.ApplyAddLayer(fresh);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                Replan();
                MessageBox.Show(this, "The layer could not be added: " + ex.Message + (held ? "\r\n\r\nEdit > Fetch puts the scene back as it was before." : string.Empty),
                    "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;
            if (result.LayersCreated > 0)
            {
                added.Add(fresh.Added);
                try
                {
                    PlannerScheduleScene.SaveAdded(added);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The layer was added but could not be noted with the scene: " + ex.Message, "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Replan();
            MessageBox.Show(this, Shorten(result.Describe(null, held)), "Add layer", MessageBoxButtons.OK, result.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // The date scrubber

        private DateTime firstDay;
        private DateTime lastDay;

        private void ShowScrubber()
        {
            bool usable = filled && schedule != null && schedule.Usable;
            scrubber.Enabled = usable;
            previewBox.Enabled = usable;
            endOfDayBox.Enabled = usable;
            selectDayButton.Enabled = usable;
            if (!usable)
            {
                EndPreview();
                dateLabel.Text = "Date";
                activeLabel.Text = filled && schedule != null ? schedule.Message : "Fill IN / RM from names to scrub through the schedule.";
                return;
            }
            DateTime first, last;
            schedule.Span(out first, out last);
            int days = (int)(last - first).TotalDays;
            var keep = firstDay != default(DateTime) ? firstDay.AddDays(scrubber.Value) : first;
            firstDay = first;
            lastDay = last;
            scrubber.Maximum = Math.Max(0, days);
            scrubber.Value = Math.Max(0, Math.Min(scrubber.Maximum, (int)(keep - first).TotalDays));
            ScrubTo();
        }

        private DateTime ScrubDay
        {
            get { return firstDay.AddDays(scrubber.Value); }
        }

        private void ScrubTo()
        {
            if (!filled || schedule == null || !schedule.Usable || firstDay == default(DateTime))
            {
                return;
            }
            var day = ScrubDay;
            bool tbcZone = schedule.WorksEnd.HasValue && day > schedule.WorksEnd.Value;
            dateLabel.Text = DateText(day, endOfDayBox.Checked);
            dateLabel.BackColor = tbcZone ? TbcZoneBack : SystemColors.Control;
            var active = schedule.ActiveOn(day);
            var parts = new List<string>();
            if (tbcZone)
            {
                parts.Add("TBC zone: removals no dated layer has are parked here, the day after the works end, as the Planner parks them");
            }
            if (active.Count > 0)
            {
                parts.Add("At work: " + string.Join(", ", active.Select(l => l.Raw + (l.Dated ? string.Empty : " (undated: at the project start)")).ToArray()));
            }
            else if (!tbcZone)
            {
                parts.Add("No layer at work");
            }
            int changing = schedule.ChangingOn(day).Count;
            selectDayButton.Text = SelectDayText(changing);
            activeLabel.Text = string.Join(". ", parts.ToArray()) + ".";
            toolTip.SetToolTip(activeLabel, activeLabel.Text);
            if (preview.Active && !SceneFree())
            {
                // An export runs: it would leave the objects the preview hides out of the GLB.
                EndPreview();
            }
            else if (preview.Active)
            {
                try
                {
                    preview.Show(schedule, Instant());
                }
                catch (Exception ex)
                {
                    EndPreview();
                    MessageBox.Show(this, "The preview stopped: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private long Instant()
        {
            return schedule.Midnight(endOfDayBox.Checked ? ScrubDay.AddDays(1) : ScrubDay);
        }

        private void PreviewBox_CheckedChanged(object sender, EventArgs e)
        {
            if (updatingPreview)
            {
                return;
            }
            if (!previewBox.Checked)
            {
                EndPreview();
                return;
            }
            if (!filled || schedule == null || !schedule.Usable || !MayChange())
            {
                updatingPreview = true;
                previewBox.Checked = false;
                updatingPreview = false;
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                preview.Show(schedule, Instant());
                if (!preview.Active)
                {
                    updatingPreview = true;
                    previewBox.Checked = false;
                    updatingPreview = false;
                }
            }
            catch (Exception ex)
            {
                EndPreview();
                MessageBox.Show(this, "The preview could not start: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void EndPreview()
        {
            try
            {
                preview.End();
            }
            catch (Exception)
            {
                // Nothing more can be shown again.
            }
            if (previewBox.Checked)
            {
                updatingPreview = true;
                previewBox.Checked = false;
                updatingPreview = false;
            }
        }

        private void SelectDayButton_Click(object sender, EventArgs e)
        {
            if (!filled || schedule == null || !schedule.Usable || !MayChange())
            {
                return;
            }
            var keys = schedule.ChangingOn(ScrubDay).Select(o => o.Key).ToList();
            if (keys.Count == 0)
            {
                MessageBox.Show(this, "Nothing is installed or removed on " + ScrubDay.ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ".", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SelectInMax(keys);
        }

        // Changing the scene

        private bool MayChange()
        {
            if (!SceneFree())
            {
                MessageBox.Show(this, "An export is running. Try again once it has finished.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        // No export is running (from this window or from MAXScript): the scene, its hidden objects and its selection may
        // change. An export reads them node by node as it goes, so the preview and the selection wait for it too.
        private bool SceneFree()
        {
            return (CanChangeScene == null || CanChangeScene()) && !ExporterForm.ExportRunning;
        }

        private string Title
        {
            get
            {
                var form = FindForm();
                return form != null ? form.Text : "Planner";
            }
        }

        private void BuildButton_Click(object sender, EventArgs e)
        {
            if (list == null || !list.Usable || !MayChange())
            {
                return;
            }
            EndPreview();
            PlannerLayerPlan reviewed;
            try
            {
                reviewed = PlannerLayerPlanner.Plan(list, PlannerScheduleScene.Read());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            plan = reviewed;
            stale = false;
            ShowList();
            if (!reviewed.HasChanges)
            {
                MessageBox.Show(this, "There is nothing to build: " + reviewed.Summary(), "Build layers and helpers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool hold;
            using (var review = new PlannerReviewForm("Build layers and helpers", reviewed.Summary(), reviewed.Notes, reviewed.Review, true, "Apply", "planner_layers_build"))
            {
                if (review.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                hold = review.HoldFirst;
            }

            // The window does not stop anyone working in the scene, so the list is checked against it first.
            PlannerLayerPlan fresh;
            try
            {
                fresh = PlannerLayerPlanner.Plan(list, PlannerScheduleScene.Read());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (fresh.Fingerprint() != reviewed.Fingerprint())
            {
                plan = fresh;
                ShowList();
                MessageBox.Show(this, "The scene has changed since the list was reviewed. The statuses have been read again: check them, then build again.",
                    "Build layers and helpers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PlannerLayerResult result;
            bool held = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (hold)
                {
                    Loader.Core.FileHold();
                    held = true;
                }
                result = PlannerScheduleScene.ApplyBuild(fresh);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                Replan();
                MessageBox.Show(this, "The layers and helpers could not be built: " + ex.Message + (held ? "\r\n\r\nEdit > Fetch puts the scene back as it was before." : string.Empty),
                    "Build layers and helpers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;
            Replan();
            MessageBox.Show(this, Shorten(result.Describe(plan, held)), "Build layers and helpers", MessageBoxButtons.OK,
                result.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void LinkButton_Click(object sender, EventArgs e)
        {
            if (!MayChange())
            {
                return;
            }
            EndPreview();
            PlannerLinkPlan reviewed;
            try
            {
                reviewed = PlannerLinkPlanner.Plan(PlannerScheduleScene.Read());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (reviewed.Message != null)
            {
                MessageBox.Show(this, reviewed.Message, "Link objects to their helpers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!reviewed.HasChanges && reviewed.ProblemCount == 0)
            {
                MessageBox.Show(this, "There is nothing to link: " + reviewed.Summary(), "Link objects to their helpers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool hold;
            using (var review = new PlannerReviewForm("Link objects to their helpers", reviewed.Summary(), reviewed.Notes, reviewed.Review, reviewed.HasChanges, "Apply", "planner_object_links"))
            {
                if (review.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                hold = review.HoldFirst;
            }

            PlannerLinkPlan fresh;
            try
            {
                fresh = PlannerLinkPlanner.Plan(PlannerScheduleScene.Read());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (fresh.Fingerprint() != reviewed.Fingerprint())
            {
                MessageBox.Show(this, "The scene has changed since the list was reviewed. Press Link objects to their helpers again to see the new list.",
                    "Link objects to their helpers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PlannerLinkResult result;
            bool held = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (hold)
                {
                    Loader.Core.FileHold();
                    held = true;
                }
                result = PlannerScheduleScene.ApplyLinks(fresh);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, "The objects could not be linked: " + ex.Message + (held ? "\r\n\r\nEdit > Fetch puts the scene back as it was before." : string.Empty),
                    "Link objects to their helpers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;
            PlannerLinkPlan after = null;
            try
            {
                after = PlannerLinkPlanner.Plan(PlannerScheduleScene.Read());
            }
            catch (Exception)
            {
                // The report goes without the second reading.
            }
            Replan();
            MessageBox.Show(this, Shorten(result.Describe(after, held)), "Link objects to their helpers", MessageBoxButtons.OK,
                result.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
            kept.Add("- and " + (lines.Length - MessageLines + 2).ToString(CultureInfo.InvariantCulture) + " more lines; the Schedule tab shows what is still to change.");
            kept.Add(lines[lines.Length - 1]);
            return string.Join("\r\n", kept.ToArray());
        }
    }
}
