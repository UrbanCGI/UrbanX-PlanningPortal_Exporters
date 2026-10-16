using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// scrubber previews the scene as the Planner's 4D view shows it at a date (not while an export runs). A Gantt beside
    /// the tree (<see cref="PlannerGanttView"/>) charts the same rows, row for row, folding with the tree's groups; a day
    /// clicked there moves the scrubber. Beside it the tree leaves out its Start, Finish and TBC columns (the Gantt draws
    /// the dates, and the names hold them), so its Layer column keeps the width it had before the Gantt was there. The last
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
        // The Gantt beside the tree, row for row with it. Neither has a bar of its own, so the tree keeps its rows at the
        // smallest window: the Gantt's zoom and scrolling are in its header, the folds in the tree's Layer header and its
        // menu (Expand all, Collapse all, Show the Gantt).
        private readonly SplitContainer ganttSplit = new SplitContainer();
        private readonly PlannerGanttView gantt;
        private readonly ContextMenuStrip treeMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem showGanttItem = new ToolStripMenuItem("Show the Gantt");
        private bool ganttPlaced;
        private bool scrubFromGantt;
        // The tree's width beside the Gantt (placed, or dragged by the modeller), which it gets back as the window grows.
        private int treeWidth = -1;
        private bool treeDragged;
        private bool fittingTree;
        private Size ganttSplitSize;
        // The Layer column's width the tree is placed to give beside the Gantt: the one it had before the Gantt was there
        // (the whole width shared by every column, at the size the window has when the tab first shows), and the font it was
        // worked out for.
        private int layerAim = -1;
        private int layerAimFont;
        // The tree and the Gantt were placed while the tab showed: the window's layout is done, and the aim stays.
        private bool ganttSettled;
        // The tree's indent per level and its fold triangles' size, from the font (FitWidths).
        private int indentStep = 16;
        private int glyphSize = 9;
        // The groups collapsed in the tree (PlannerGanttModel.KeyOf), kept while the window is open; the rows the tree and
        // the Gantt show, and the Gantt's chart of them.
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.Ordinal);
        private List<PlannerScheduleRow> shownRows = new List<PlannerScheduleRow>();
        private PlannerGanttModel ganttModel;
        // The summary above the tree (buttons, list, scene, notes), the side list's buttons and the scrubber's rows.
        private readonly TableLayoutPanel summaryPanel;
        private readonly FlowLayoutPanel sideButtons;
        private readonly TableLayoutPanel scrubPanel;
        // Every note (the summary shows as many lines of them as there is room for, see FitNotes), and what they were
        // last fitted to.
        private List<string> notes = new List<string>();
        private string notesFitted;
        private bool fittingNotes;
        private bool splitPlaced;
        private bool sidePlaced;
        // The side list's width when placed: room for its three buttons on a line (FitWidths).
        private int sideWidth = 240;
        // The tree's header and three rows, the lists' tab strip below it and the side list's header and two rows
        // (FitWidths); the height the lists below the tree are meant to have (placed, or dragged by the modeller), which
        // they get back when the window grows again.
        private int treeMinHeight = 91;
        private int listsMinHeight = 22;
        private int sideRowsMinHeight = 71;
        private int listsHeight = -1;
        private bool fittingSplit;
        private Size splitSize;
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
            // its works start keeps its earlier start, where undated layers' work and installs no dated layer has sit (and
            // where the scrubber starts). By the scrubber, beside the layers at work, so it takes no line of its own.
            var startPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(8, 0, 0, 0)
            };
            projectStartLabel.Text = "Planner project start:";
            projectStartLabel.AutoSize = true;
            projectStartLabel.Margin = new Padding(0, 6, 3, 0);
            projectStartPicker.Format = DateTimePickerFormat.Custom;
            projectStartPicker.CustomFormat = "d MMM yyyy";
            projectStartPicker.ShowCheckBox = true;
            projectStartPicker.Width = 130;
            projectStartPicker.ValueChanged += ProjectStart_Changed;
            // Its width is read off the control once it has a handle (after the control has set itself up: its format is
            // given after the handle's event), and again when the display's scaling changes.
            projectStartPicker.HandleCreated += (s, e) => projectStartPicker.BeginInvoke(new Action(FitPickerWidth));
            projectStartPicker.DpiChangedAfterParent += (s, e) => FitPickerWidth();
            const string startTip = "The Planner workspace's project start: the first day of its Gantt (a new workspace starts on the day it is made). "
                + "Generate from model moves it back to the earliest dated layer, never forward; undated (TBC) layers' work and installs no dated layer has sit on it. "
                + "Tick it and give the date when the workspace starts before its works. Unticked, the tab takes the earliest dated layer. Kept with the scene.";
            toolTip.SetToolTip(projectStartLabel, startTip);
            toolTip.SetToolTip(projectStartPicker, startTip);
            startPanel.Controls.Add(projectStartLabel);
            startPanel.Controls.Add(projectStartPicker);

            summaryPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(6, 6, 6, 4)
            };
            summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            foreach (var label in new[] { listLabel, sceneLabel, notesLabel })
            {
                label.AutoSize = true;
                label.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                label.Margin = new Padding(0, 0, 0, 3);
            }
            listLabel.Font = boldFont;
            notesLabel.ForeColor = SystemColors.GrayText;
            summaryPanel.Controls.Add(buttons, 0, 0);
            summaryPanel.Controls.Add(listLabel, 0, 1);
            summaryPanel.Controls.Add(sceneLabel, 0, 2);
            summaryPanel.Controls.Add(notesLabel, 0, 3);

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
            // TabControl.ItemSize reads 0 until the strip has a handle: its tabs' own height from then (FitWidths takes
            // the font's before).
            bottomTabs.HandleCreated += (s, e) =>
            {
                listsMinHeight = ListsStripHeight();
                FitSplit();
                PerformLayout();
            };

            // The tree's groups fold: a triangle before a row with rows under it (click it, double-click the row, or Left and
            // Right), the triangle in the Layer header folds or opens them all, as do Expand all and Collapse all in the
            // tree's menu; the Gantt shows the same rows.
            grid.CellPainting += Grid_CellPainting;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && RowHasChildren(e.RowIndex))
                {
                    Toggle(e.RowIndex);
                }
            };
            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            grid.KeyDown += Grid_KeyDown;
            treeMenu.Items.Add(new ToolStripMenuItem("Expand all", null, (s, e) => ExpandAll(true)));
            treeMenu.Items.Add(new ToolStripMenuItem("Collapse all", null, (s, e) => ExpandAll(false)));
            treeMenu.Items.Add(new ToolStripSeparator());
            showGanttItem.Checked = true;
            showGanttItem.CheckOnClick = true;
            showGanttItem.CheckedChanged += (s, e) =>
            {
                // Hidden, the tree has the whole width with every column, and its own one-line header.
                fittingTree = true;
                try
                {
                    ganttSplit.Panel2Collapsed = !showGanttItem.Checked;
                }
                finally
                {
                    fittingTree = false;
                }
                ShowTreeColumns();
                FitHeader();
                FitTreeWidth();
                FitSplit();
                PerformLayout();
            };
            treeMenu.Items.Add(showGanttItem);
            treeMenu.Opening += (s, e) =>
            {
                treeMenu.Items[0].Enabled = shownRows.Where((r, i) => RowHasChildren(i)).Any(r => collapsed.Contains(PlannerGanttModel.KeyOf(r)));
                treeMenu.Items[1].Enabled = shownRows.Where((r, i) => RowHasChildren(i)).Any(r => !collapsed.Contains(PlannerGanttModel.KeyOf(r)));
            };
            grid.ContextMenuStrip = treeMenu;
            gantt = new PlannerGanttView(grid) { Dock = DockStyle.Fill };
            gantt.DayClicked += Gantt_DayClicked;
            gantt.HideRequested += () => showGanttItem.Checked = false;
            ganttSplit.Dock = DockStyle.Fill;
            ganttSplit.Orientation = Orientation.Vertical;
            ganttSplit.FixedPanel = FixedPanel.Panel1;
            ganttSplit.Panel1.Controls.Add(grid);
            ganttSplit.Panel2.Controls.Add(gantt);
            ShowTreeColumns();
            // On its own: it is resized while the split above it moves (PlaceSplitter and FitSplit), which must not see it.
            ganttSplit.SizeChanged += (s, e) =>
            {
                ganttSplitSize = ganttSplit.Size;
                FitGanttSplit();
                PlaceGanttSplitter();
                FitTreeWidth();
            };
            // A move while the split keeps its size is the modeller's: the tree keeps that width from then on.
            ganttSplit.SplitterMoved += (s, e) =>
            {
                if (!fittingTree && ganttPlaced && ganttSplit.Size == ganttSplitSize)
                {
                    treeWidth = ganttSplit.SplitterDistance;
                    treeDragged = true;
                }
            };

            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.FixedPanel = FixedPanel.Panel2;
            split.Panel1.Controls.Add(ganttSplit);
            split.Panel2.Controls.Add(bottomTabs);
            // The lists below get a quarter of the height, once the tab has its real size; they then keep their height
            // when the window is resized, except that the tree keeps a few rows (FitSplit).
            split.SizeChanged += (s, e) =>
            {
                splitSize = split.Size;
                PlaceSplitter();
                FitSplit();
            };
            // A move while the split keeps its size is the modeller's (one while it is resized is the split's own).
            split.SplitterMoved += (s, e) =>
            {
                if (!fittingSplit && splitPlaced && split.Size == splitSize)
                {
                    listsHeight = split.Panel2.Height;
                }
            };

            // The side list: the chosen row's IN and RM.
            sideLabel.Dock = DockStyle.Top;
            sideLabel.AutoSize = true;
            sideLabel.Padding = new Padding(4, 3, 4, 2);
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
            sideButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(2, 0, 2, 0) };
            // Short names, so the three fit on one line under the list (the tooltips say what each does).
            SetUpButton(addInButton, "Add to IN\u2026", (s, e) => AddSelected(PlannerTaskType.Install),
                "Give the objects selected in 3ds Max this layer's install tag (their names change; nothing is moved or linked), after a review.");
            SetUpButton(addRmButton, "Add to RM\u2026", (s, e) => AddSelected(PlannerTaskType.Dismantle),
                "Give the objects selected in 3ds Max this layer's removal tag (their names change; nothing is moved or linked), after a review.");
            SetUpButton(removeButton, "Remove from list\u2026", RemoveButton_Click,
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
            scrubPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 2, Padding = new Padding(6, 2, 6, 4) };
            scrubPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scrubPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scrubPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scrubPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            previewBox.Appearance = Appearance.Button;
            previewBox.Text = "Preview in the viewport";
            previewBox.AutoSize = true;
            previewBox.FlatStyle = FlatStyle.Flat;
            previewBox.Anchor = AnchorStyles.Left;
            previewBox.CheckedChanged += PreviewBox_CheckedChanged;
            toolTip.SetToolTip(previewBox, "Hide the objects the Planner's 4D view hides at the scrubber's date. Leaving gives every object its own hidden state back; it is never an undo step.");
            scrubber.Dock = DockStyle.Fill;
            scrubber.TickStyle = TickStyle.None;
            // Its own height (FitWidths), not the 45 px a trackbar takes for its ticks: it has none.
            scrubber.AutoSize = false;
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
            scrubPanel.Controls.Add(previewBox, 0, 0);
            scrubPanel.Controls.Add(scrubber, 1, 0);
            scrubPanel.Controls.Add(endOfDayBox, 2, 0);
            scrubPanel.Controls.Add(selectDayButton, 3, 0);
            scrubPanel.Controls.Add(dateLabel, 0, 1);
            scrubPanel.Controls.Add(activeLabel, 1, 1);
            scrubPanel.Controls.Add(startPanel, 2, 1);
            scrubPanel.SetColumnSpan(startPanel, 2);

            Controls.Add(sideSplit);
            Controls.Add(scrubPanel);
            Controls.Add(summaryPanel);
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
                else if (!ganttSettled)
                {
                    // Shown for the first time: the tree and the Gantt are placed for the window as it opened.
                    FitGanttSplit();
                    PlaceGanttSplitter();
                    FitTreeWidth();
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
                treeMenu.Dispose();
                boldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // Each split is placed once it has its real size, the side list by the width alone (so a window opened small
        // still gets a side list wide enough for its buttons), the Gantt beside the tree in the width the side list leaves
        // (PlaceGanttSplitter), the lists below the tree once there is height for them; the tree keeps its few rows at the
        // smallest sizes (see FitSplit).
        private void PlaceSplitter()
        {
            try
            {
                if (!sidePlaced && sideSplit.Width >= 400)
                {
                    sidePlaced = true;
                    sideSplit.SplitterDistance = Math.Max(sideSplit.Panel1MinSize, sideSplit.Width - sideSplit.SplitterWidth - Math.Max(sideWidth, sideSplit.Width / 4));
                }
                if (!splitPlaced && split.Height >= 200)
                {
                    listsHeight = Math.Max(120, split.Height / 4);
                    fittingSplit = true;
                    split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height - split.SplitterWidth - listsHeight);
                    splitPlaced = true;
                }
            }
            catch (Exception)
            {
                // The splits keep their own division.
            }
            finally
            {
                fittingSplit = false;
            }
        }

        // The lists below the tree keep the height they are meant to have, but at the smallest window sizes they give way
        // (down to their tab strip) so the tree keeps its header and three rows.
        private void FitSplit()
        {
            int room = split.Height - split.SplitterWidth;
            if (fittingSplit || room <= 0)
            {
                return;
            }
            int lists = listsHeight >= 0 ? listsHeight : split.Panel2.Height;
            FitCompact(room, lists);
            int distance = Math.Max(room - lists, Math.Min(treeMinHeight, room - listsMinHeight));
            distance = Math.Max(split.Panel1MinSize, Math.Min(distance, room - split.Panel2MinSize));
            if (distance == split.SplitterDistance)
            {
                return;
            }
            try
            {
                fittingSplit = true;
                split.SplitterDistance = distance;
            }
            catch (Exception)
            {
                // The split keeps its own division.
            }
            finally
            {
                fittingSplit = false;
            }
        }

        // Where the lists below give way so that the tree keeps its least height (its header and three rows), the Gantt's
        // header, and the tree's with it, goes down to one row, and the lists keep the difference: at the smallest window
        // heights they have what they had before the Gantt was beside the tree. It gets its two rows back as the window
        // grows. Decided by the height the split has and the lists are meant to have, not by the notes, which give way to
        // what the tree keeps (SplitMinHeight) and are fitted again on the next layout.
        private void FitCompact(int room, int lists)
        {
            if (gantt == null)
            {
                return;
            }
            bool compact = room < TreeMinHeight(gantt.HeaderHeight(false)) + lists;
            if (compact == gantt.Compact)
            {
                return;
            }
            gantt.Compact = compact;
            FitHeader();
            notesFitted = null;
        }

        // The tree's header holds the Gantt's, so their rows line up: its two rows (months over days), one where the tree
        // is held at its least height (FitCompact), or the tree's own line while the Gantt is hidden. The tree keeps that
        // header and three rows (treeMinHeight).
        private void FitHeader()
        {
            if (gantt != null)
            {
                int header = gantt.HeaderHeight(ganttSplit.Panel2Collapsed || gantt.Compact);
                if (grid.ColumnHeadersHeight != header)
                {
                    grid.ColumnHeadersHeight = header;
                }
            }
            treeMinHeight = TreeMinHeight(grid.ColumnHeadersHeight);
        }

        // The tree's header of this height and three rows.
        private int TreeMinHeight(int header)
        {
            int row = Math.Max(grid.RowTemplate.Height, grid.Font.Height + 9);
            return Math.Max(header, grid.Font.Height + 10) + 3 * row + 4;
        }

        // The lists' tab strip: its tabs and the page's border. TabControl.ItemSize reads 0 until the strip has a handle, so
        // until then a tab is taken to be its font's height and 5 px, as the strip draws it.
        private int ListsStripHeight()
        {
            int tabs = bottomTabs.Font.Height + 5;
            if (bottomTabs.IsHandleCreated)
            {
                tabs = Math.Max(tabs, bottomTabs.ItemSize.Height);
            }
            return tabs + 4;
        }

        // The least the tree and the side list beside it keep while the notes give way: the tree's header and three rows
        // over the lists' tab strip, and the side list's label, header, two rows and buttons.
        private int SplitMinHeight()
        {
            int tree = treeMinHeight + split.SplitterWidth + listsMinHeight;
            int side = sideLabel.Height + sideButtons.Height + sideRowsMinHeight;
            return Math.Max(tree, side);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (FitNotes())
            {
                // The summary above changed height: the lists below take up the difference.
                base.OnLayout(e);
            }
        }

        // The notes give way at the smallest window sizes, after the lists below the tree (FitSplit): as many of them are
        // written out (up to NoteLines) as leave the tree and the side list what SplitMinHeight says, and a last line
        // counts the rest, down to one line saying how many there are. They get their lines back as the window grows, and
        // the tooltip always holds every one. True when the text changed, and the summary's height with it.
        private bool FitNotes()
        {
            if (fittingNotes || summaryPanel == null || scrubPanel == null || sideButtons == null || notes.Count == 0)
            {
                return false;
            }
            int width = 0;
            int room = 0;
            try
            {
                var columns = summaryPanel.GetColumnWidths();
                var rows = summaryPanel.GetRowHeights();
                int notesRow = summaryPanel.GetRow(notesLabel);
                if (columns.Length > 0 && notesRow >= 0 && notesRow < rows.Length && DisplayRectangle.Height > 0)
                {
                    width = columns[0] - notesLabel.Margin.Horizontal;
                    // The tab less the summary without its notes, the scrubber and what the tree and side list keep.
                    room = DisplayRectangle.Height - (summaryPanel.Height - rows[notesRow]) - scrubPanel.Height - SplitMinHeight();
                }
            }
            catch (Exception)
            {
                width = 0;
            }
            // Not laid out yet: every line.
            string fit = width > 0 ? width.ToString(CultureInfo.InvariantCulture) + "|" + room.ToString(CultureInfo.InvariantCulture) : "all";
            if (fit == notesFitted)
            {
                return false;
            }
            notesFitted = fit;
            string before = notesLabel.Text;
            int most = Math.Min(notes.Count, NoteLines);
            string text = NotesText(most);
            fittingNotes = true;
            summaryPanel.SuspendLayout();
            try
            {
                if (width > 0)
                {
                    // The most notes written out that fit; when none do, the shortest text.
                    string shortest = null;
                    int shortestHeight = int.MaxValue;
                    bool fits = false;
                    for (int shown = most; shown >= 0 && !fits; shown--)
                    {
                        var candidate = NotesText(shown);
                        notesLabel.Text = candidate;
                        int height = notesLabel.GetPreferredSize(new Size(width, 0)).Height + notesLabel.Margin.Vertical;
                        if (height <= room)
                        {
                            text = candidate;
                            fits = true;
                        }
                        else if (height < shortestHeight)
                        {
                            shortest = candidate;
                            shortestHeight = height;
                        }
                    }
                    if (!fits)
                    {
                        text = shortest;
                    }
                }
                notesLabel.Text = text;
            }
            finally
            {
                summaryPanel.ResumeLayout(true);
                fittingNotes = false;
            }
            return !string.Equals(before, text, StringComparison.Ordinal);
        }

        // The notes with the first `shown` written out and a last line counting the rest (with none written out, one line
        // saying how many there are).
        private string NotesText(int shown)
        {
            if (shown >= notes.Count)
            {
                return string.Join("\r\n", notes.ToArray());
            }
            if (shown <= 0)
            {
                return notes.Count == 1
                    ? "1 note (hover to read it)."
                    : notes.Count.ToString(CultureInfo.InvariantCulture) + " notes (hover to read them all).";
            }
            return string.Join("\r\n", notes.Take(shown).ToArray()) + "\r\n\u2026 and " + (notes.Count - shown).ToString(CultureInfo.InvariantCulture)
                + " more (hover to read them all).";
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
                scrubber.Height = Math.Max(26, scrubber.Font.Height + 14);
                // The side list's three buttons on one line; the tree's header and three rows; the lists' tab strip; the side
                // list's header and two rows.
                sideWidth = Math.Max(240, new[] { addInButton, addRmButton, removeButton }.Sum(b => b.GetPreferredSize(Size.Empty).Width + b.Margin.Horizontal) + 12);
                FitPickerWidth();
                // The tree's header holds the Gantt's header (FitHeader), so their rows line up; the indent and the fold
                // triangles follow the font, and the Layer header has room for its triangle.
                indentStep = Math.Max(16, grid.Font.Height + 3);
                glyphSize = Math.Max(7, grid.Font.Height * 3 / 5) | 1;
                if (gantt != null)
                {
                    grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                    grid.Columns[0].HeaderCell.Style.Padding = new Padding(glyphSize + 6, 0, 0, 0);
                }
                FitHeader();
                listsMinHeight = ListsStripHeight();
                split.Panel2MinSize = Math.Min(25, listsMinHeight);
                int sideRow = Math.Max(sideGrid.RowTemplate.Height, sideGrid.Font.Height + 9);
                sideRowsMinHeight = Math.Max(sideGrid.ColumnHeadersHeight, sideGrid.Font.Height + 10) + 2 * sideRow + 4;
                int dateColumn = TextRenderer.MeasureText("2026-10-17", grid.Font).Width + 14;
                grid.Columns[1].MinimumWidth = TextRenderer.MeasureText("Group", grid.Font).Width + 14;
                grid.Columns[2].MinimumWidth = dateColumn;
                grid.Columns[3].MinimumWidth = dateColumn;
                grid.Columns[4].MinimumWidth = TextRenderer.MeasureText("TBC", grid.Font).Width + 14;
                // Beside the Gantt the tree keeps its columns' least widths; its width is worked out again for the new
                // font unless the modeller has set it: the Layer column's aim follows the font (not the window's size now),
                // the tree's width the columns' new least widths.
                if (gantt != null)
                {
                    FitGanttSplit();
                    if (ganttPlaced && !treeDragged && layerAim > 0)
                    {
                        if (layerAimFont > 0 && layerAimFont != grid.Font.Height)
                        {
                            layerAim = (int)Math.Round(layerAim * (double)grid.Font.Height / layerAimFont);
                            layerAimFont = grid.Font.Height;
                        }
                        treeWidth = TreeWidthFor(layerAim);
                    }
                    FitTreeWidth();
                }
            }
            catch (Exception)
            {
                // The controls keep the sizes they have.
            }
        }

        // DTM_GETIDEALSIZE: the size a date picker needs to show its text whole, its own parts included.
        private const int DtmGetIdealSize = 0x1000 + 15;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref NativeSize lParam);

        // The project start picker holds its longest date whole. Its own parts (the tick box, the drop-down button and the
        // border) grow with the display's scaling, which its font alone does not tell at every scaling: they are read off
        // the control once it has a handle (the width it asks for its text, less that text), and are never taken as less
        // than the 56 px they need at 96 DPI, scaled to the display's DPI (the control's, or the screen's when larger: the
        // .NET Framework gives 96 for the control unless the host opts in to its DPI handling).
        private void FitPickerWidth()
        {
            if (IsDisposed || projectStartPicker.IsDisposed)
            {
                return;
            }
            try
            {
                var font = projectStartPicker.Font;
                int text = 0;
                for (var day = new DateTime(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
                {
                    text = Math.Max(text, TextRenderer.MeasureText(day.ToString(projectStartPicker.CustomFormat, CultureInfo.CurrentCulture), font).Width);
                }
                float dpi = projectStartPicker.DeviceDpi;
                using (var screen = Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpi = Math.Max(dpi, screen.DpiX);
                }
                int chrome = (int)Math.Ceiling(56 * dpi / 96.0);
                if (projectStartPicker.IsHandleCreated)
                {
                    var ideal = new NativeSize();
                    if (SendMessage(projectStartPicker.Handle, DtmGetIdealSize, IntPtr.Zero, ref ideal) != IntPtr.Zero && ideal.Width > 0)
                    {
                        chrome = Math.Max(chrome, ideal.Width - TextRenderer.MeasureText(projectStartPicker.Text, font).Width);
                    }
                }
                projectStartPicker.Width = text + chrome;
            }
            catch (Exception)
            {
                // The picker keeps the width it has.
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
            // The Gantt charts the same rows, in the same order; a row under a collapsed group is hidden in both. A scene
            // layer the list does not have whose work the Planner reads gets a row after the list's too (it stays in the list
            // of such layers below), so the Gantt charts every layer the scrubber finds at work.
            shownRows = treeRows.Concat(PlannerScheduleRows.UnlistedAtWork(unlistedRows)).Concat(extra).ToList();
            ganttModel = PlannerGanttModel.Build(shownRows, filled ? schedule : null, DateTime.Today);
            var shown = Shown();
            var lines = new List<DataGridViewRow>();
            for (int index = 0; index < shownRows.Count; index++)
            {
                var row = shownRows[index];
                var line = new DataGridViewRow { Tag = row, Visible = shown[index] };
                line.CreateCells(grid, row.Name, row.Kind, row.Start, row.Finish, row.Tbc ? "Yes" : string.Empty, Count(row, true), Count(row, false), row.Status);
                // The tree: each level indented, room for a fold triangle before every name.
                line.Cells[0].Style.Padding = new Padding(TextIndent(row.Depth), 0, 0, 0);
                if (row.Kind == "Group")
                {
                    line.Cells[0].Style.Font = boldFont;
                }
                line.DefaultCellStyle.BackColor = BackOf(row);
                line.Cells[7].ToolTipText = Details(row);
                // The whole name (it holds the dates, which beside the Gantt have no columns of their own).
                line.Cells[0].ToolTipText = row.Name + (row.ListRow != null
                    ? "\r\nPlanner ID " + row.ListRow.PlannerId + (row.ListRow.IdKey != null ? "; layer ID " + row.ListRow.IdKey : string.Empty)
                    : row.LayerName != null && row.LayerName != row.Name ? "\r\n" + row.LayerName : string.Empty);
                lines.Add(line);
            }
            grid.Rows.AddRange(lines.ToArray());
            if (firstRow >= 0 && grid.Rows.Count > 0)
            {
                // The row that was at the top, or the shown row above it.
                int top = Math.Min(firstRow, grid.Rows.Count - 1);
                while (top > 0 && !shown[top])
                {
                    top--;
                }
                try
                {
                    if (shown[top])
                    {
                        grid.FirstDisplayedScrollingRowIndex = top;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The grid has no room to show rows; it starts at the top.
                }
            }
            gantt.Model = ganttModel;

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
                        if (line.Visible)
                        {
                            line.Selected = true;
                        }
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
                    ? " (the earliest dated layer, assumed to be the Planner's project start: give that start in Planner project start, by the scrubber, when the workspace starts earlier)."
                    : schedule.ProjectStart < projectStart.Value
                        ? " (the earliest dated layer, before the Planner's project start: Generate from model moves the start back to it)."
                        : " (the Planner's project start).");
        }

        // The notes: a few lines of them, fewer at the smallest window sizes (FitNotes); all of them in the tooltip.
        private void ShowNotes(List<string> all)
        {
            notes = all;
            notesFitted = null;
            if (all.Count == 0)
            {
                notesLabel.Text = string.Empty;
            }
            notesLabel.Visible = all.Count > 0;
            toolTip.SetToolTip(notesLabel, string.Join("\r\n", all.ToArray()));
            if (FitNotes())
            {
                PerformLayout();
            }
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
                case PlannerScheduleRowSource.Unlisted:
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
            if (row.Source == PlannerScheduleRowSource.Unlisted && row.UnlistedItem != null)
            {
                parts.Add("Not in the Planner's list (" + row.UnlistedItem.What + ")" + (string.IsNullOrEmpty(row.UnlistedItem.Note) ? string.Empty : ": " + row.UnlistedItem.Note)
                    + "; left as it is. The Planner reads its work, so it has a row here and on the Gantt.");
            }
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

        // The tree's folds and the Gantt

        // Which rows show: those under no collapsed group.
        private bool[] Shown()
        {
            return PlannerGanttModel.Shown(shownRows, i => RowHasChildren(i) && collapsed.Contains(PlannerGanttModel.KeyOf(shownRows[i])));
        }

        private bool RowHasChildren(int index)
        {
            return ganttModel != null && index >= 0 && index < ganttModel.Rows.Count && ganttModel.Rows[index].HasChildren;
        }

        // Where a row's name starts: its indent and room for a fold triangle.
        private int TextIndent(int depth)
        {
            return 4 + indentStep * depth + glyphSize + 4;
        }

        private Rectangle GlyphBounds(Rectangle cell, int depth)
        {
            return new Rectangle(cell.X + 4 + indentStep * depth, cell.Y + (cell.Height - glyphSize) / 2, glyphSize, glyphSize);
        }

        // A group is open when any group is (the Layer header's triangle then folds them all).
        private bool AnyOpen()
        {
            for (int i = 0; i < shownRows.Count; i++)
            {
                if (RowHasChildren(i) && !collapsed.Contains(PlannerGanttModel.KeyOf(shownRows[i])))
                {
                    return true;
                }
            }
            return false;
        }

        private bool AnyGroup()
        {
            return ganttModel != null && ganttModel.Rows.Any(r => r.HasChildren);
        }

        // A row with rows under it gets a triangle before its name, pointing right when folded and down when open; the
        // Layer header gets one for all of them.
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex != 0 || (e.RowIndex >= 0 && !RowHasChildren(e.RowIndex)) || (e.RowIndex < 0 && !AnyGroup()))
            {
                return;
            }
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            var box = GlyphBounds(e.CellBounds, e.RowIndex >= 0 ? shownRows[e.RowIndex].Depth : 0);
            bool open = e.RowIndex >= 0 ? !collapsed.Contains(PlannerGanttModel.KeyOf(shownRows[e.RowIndex])) : AnyOpen();
            var points = open
                ? new[] { new Point(box.Left, box.Top + box.Height / 4), new Point(box.Right, box.Top + box.Height / 4), new Point(box.Left + box.Width / 2, box.Bottom - box.Height / 4) }
                : new[] { new Point(box.Left + box.Width / 4, box.Top), new Point(box.Left + box.Width / 4, box.Bottom), new Point(box.Right - box.Width / 4, box.Top + box.Height / 2) };
            bool selected = e.RowIndex >= 0 && (e.State & DataGridViewElementStates.Selected) != 0;
            using (var brush = new SolidBrush(selected ? e.CellStyle.SelectionForeColor : SystemColors.ControlDarkDark))
            {
                var smoothing = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPolygon(brush, points);
                e.Graphics.SmoothingMode = smoothing;
            }
            e.Handled = true;
        }

        // The Layer header's triangle folds every group, or opens them all when none is open.
        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.ColumnIndex == 0 && AnyGroup() && e.X <= 4 + glyphSize + 4)
            {
                ExpandAll(!AnyOpen());
            }
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex != 0 || e.RowIndex < 0 || !RowHasChildren(e.RowIndex))
            {
                return;
            }
            int left = 4 + indentStep * shownRows[e.RowIndex].Depth;
            if (e.X >= left - 3 && e.X <= left + glyphSize + 3)
            {
                Toggle(e.RowIndex);
            }
        }

        // Left folds the chosen group, Right opens it.
        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode != Keys.Left && e.KeyCode != Keys.Right) || e.Modifiers != Keys.None || grid.CurrentCell == null)
            {
                return;
            }
            int index = grid.CurrentCell.RowIndex;
            if (!RowHasChildren(index))
            {
                return;
            }
            bool open = !collapsed.Contains(PlannerGanttModel.KeyOf(shownRows[index]));
            if (open == (e.KeyCode == Keys.Left))
            {
                Toggle(index);
            }
            e.Handled = true;
        }

        private void Toggle(int index)
        {
            var key = PlannerGanttModel.KeyOf(shownRows[index]);
            if (!collapsed.Remove(key))
            {
                collapsed.Add(key);
            }
            ApplyShown();
        }

        private void ExpandAll(bool expand)
        {
            if (expand)
            {
                collapsed.Clear();
            }
            else
            {
                for (int i = 0; i < shownRows.Count; i++)
                {
                    if (RowHasChildren(i))
                    {
                        collapsed.Add(PlannerGanttModel.KeyOf(shownRows[i]));
                    }
                }
            }
            ApplyShown();
        }

        // Shows and hides the tree's rows as the folds say; a chosen row that goes is replaced by the group above it.
        private void ApplyShown()
        {
            if (grid.Rows.Count != shownRows.Count)
            {
                return;
            }
            var shown = Shown();
            var current = grid.CurrentCell != null ? grid.CurrentCell.RowIndex : -1;
            if (current >= 0 && !shown[current])
            {
                int above = current;
                while (above >= 0 && !shown[above])
                {
                    above = ganttModel.Rows[above].Parent;
                }
                try
                {
                    grid.CurrentCell = above >= 0 ? grid.Rows[above].Cells[0] : null;
                    if (above >= 0)
                    {
                        grid.Rows[above].Selected = true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The grid keeps its current cell.
                }
            }
            for (int i = 0; i < shown.Length; i++)
            {
                if ((grid.Rows.GetRowState(i) & DataGridViewElementStates.Visible) != 0 != shown[i])
                {
                    grid.Rows[i].Visible = shown[i];
                }
            }
            grid.InvalidateColumn(0);
            gantt.Invalidate();
        }

        // A day clicked in the Gantt: its row is chosen in the tree, and the scrubber moves there (the preview follows only
        // when it is on).
        private void Gantt_DayClicked(DateTime day, int index)
        {
            if (index >= 0 && index < grid.Rows.Count && (grid.Rows.GetRowState(index) & DataGridViewElementStates.Visible) != 0)
            {
                try
                {
                    grid.CurrentCell = grid.Rows[index].Cells[0];
                    grid.Rows[index].Selected = true;
                }
                catch (InvalidOperationException)
                {
                    // The tree keeps its choice.
                }
            }
            if (!scrubber.Enabled || firstDay == default(DateTime))
            {
                return;
            }
            var target = ganttModel != null ? ganttModel.ClampToScrubber(day) : day.Date;
            int value = Math.Max(scrubber.Minimum, Math.Min(scrubber.Maximum, (int)Math.Round((target - firstDay).TotalDays)));
            scrubFromGantt = true;
            try
            {
                scrubber.Value = value;
            }
            finally
            {
                scrubFromGantt = false;
            }
        }

        // Beside the Gantt the tree gives the room of its Start, Finish and TBC columns to it (ShowTreeColumns), and its
        // Layer column keeps the width it had before the Gantt was there: the whole width shared by every column, at the
        // size the window has when the tab first shows (and never less than about 20 characters of a name after its indent).
        // The Gantt takes the rest, and every width the window gains or loses (until the modeller drags the splitter, which
        // sets the tree's width from then on). Placed again for each width the split gets, the side list's taken out
        // (PlaceSplitter), until the tab shows: the window is still being laid out until then.
        private void PlaceGanttSplitter()
        {
            if (ganttSettled || treeDragged || !sidePlaced || ganttSplit.Width < 300)
            {
                return;
            }
            ganttPlaced = true;
            ganttSettled = Visible;
            var every = grid.Columns.Cast<DataGridViewColumn>().ToList();
            int before = FillWidth(every, ganttSplit.Width - SystemInformation.VerticalScrollBarWidth, 0);
            layerAim = Math.Max(TextIndent(1) + grid.Font.Height * 10, before);
            layerAimFont = grid.Font.Height;
            treeWidth = TreeWidthFor(layerAim);
        }

        // The tree's width beside the Gantt that gives its Layer column `layer` pixels, its vertical scroll bar showing.
        private int TreeWidthFor(int layer)
        {
            var columns = TreeColumns(true);
            int least = columns.Sum(c => c.MinimumWidth);
            int width = least;
            while (width < least + 8000 && FillWidth(columns, width, 0) < layer)
            {
                width += 2;
            }
            return width + SystemInformation.VerticalScrollBarWidth + 4;
        }

        // The tree's columns: beside the Gantt, all but Start, Finish and TBC (the Gantt draws each row's dates, and the names
        // hold them); with the Gantt hidden, every one.
        private List<DataGridViewColumn> TreeColumns(bool besideGantt)
        {
            return grid.Columns.Cast<DataGridViewColumn>().Where(c => !besideGantt || !IsDateColumn(c)).ToList();
        }

        private static bool IsDateColumn(DataGridViewColumn column)
        {
            return column.Index >= 2 && column.Index <= 4;
        }

        // The tree shows its date columns only while the Gantt is hidden.
        private void ShowTreeColumns()
        {
            bool beside = !ganttSplit.Panel2Collapsed;
            foreach (var column in grid.Columns.Cast<DataGridViewColumn>().Where(IsDateColumn))
            {
                if (column.Visible == beside)
                {
                    column.Visible = !beside;
                }
            }
        }

        // The width a column gets when the grid shares out `available` pixels among `columns` as its Fill sizing does, laid
        // out afresh: by fill weight, every column held at its least width when its share would be less.
        private static int FillWidth(List<DataGridViewColumn> columns, int available, int index)
        {
            var held = new bool[columns.Count];
            while (true)
            {
                float weights = 0;
                int rest = available;
                for (int i = 0; i < columns.Count; i++)
                {
                    if (held[i])
                    {
                        rest -= columns[i].MinimumWidth;
                    }
                    else
                    {
                        weights += columns[i].FillWeight;
                    }
                }
                bool changed = false;
                for (int i = 0; i < columns.Count && weights > 0; i++)
                {
                    if (!held[i] && rest * columns[i].FillWeight / weights < columns[i].MinimumWidth)
                    {
                        held[i] = true;
                        changed = true;
                    }
                }
                if (!changed)
                {
                    return held[index] || weights <= 0 ? columns[index].MinimumWidth : (int)(rest * columns[index].FillWeight / weights);
                }
            }
        }

        // The tree at the width it is meant to have, as far as the split allows (the Gantt keeps its least width; at the
        // smallest window the tree gives way down to its columns' least widths), and back to it as the window grows. A grid
        // shares a new width out by the widths its columns had (squeezed to their least widths, it goes on sharing as they
        // were), so after each change it is laid out afresh, by its columns' weights, and once it shows its Layer column is
        // measured: where it falls short of its aim and the split has room, the tree widens until it has it (a grid that
        // does not show lays its columns out only once it does).
        private void FitTreeWidth()
        {
            if (!ganttPlaced || treeWidth < 0 || ganttSplit.Panel2Collapsed || fittingTree)
            {
                return;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                int most = ganttSplit.Width - ganttSplit.SplitterWidth - ganttSplit.Panel2MinSize;
                int distance = Math.Max(ganttSplit.Panel1MinSize, Math.Min(treeWidth, most));
                if (distance <= 0)
                {
                    return;
                }
                if (distance != ganttSplit.SplitterDistance)
                {
                    try
                    {
                        fittingTree = true;
                        ganttSplit.SplitterDistance = distance;
                        ResetFill();
                    }
                    catch (Exception)
                    {
                        // The split keeps its own division.
                        return;
                    }
                    finally
                    {
                        fittingTree = false;
                    }
                }
                int shortBy = layerAim - grid.Columns[0].Width;
                if (treeDragged || layerAim <= 0 || shortBy <= 1 || distance < treeWidth || !grid.Visible || !grid.IsHandleCreated)
                {
                    return;
                }
                float weights = TreeColumns(true).Sum(c => c.FillWeight);
                treeWidth += Math.Max(2, (int)Math.Ceiling(shortBy * weights / grid.Columns[0].FillWeight));
            }
        }

        // The grid lays its columns out afresh, sharing its width by their weights (a weight changed and put back has it
        // forget the widths they had).
        private void ResetFill()
        {
            var column = grid.Columns[0];
            float weight = column.FillWeight;
            column.FillWeight = weight + 1;
            column.FillWeight = weight;
        }

        // The tree keeps room for every column it shows beside the Gantt at its least width (a scroll bar under it would take
        // a row's height at the smallest window), the Gantt for a week or so of days; the tree first when there is not room
        // for both.
        private void FitGanttSplit()
        {
            int tree = TreeColumns(true).Sum(c => c.MinimumWidth) + SystemInformation.VerticalScrollBarWidth + 4;
            int chart = grid.Font.Height * 6;
            // A move of the splitter the new least sizes make is the split's own, not the modeller's.
            bool wasFitting = fittingTree;
            fittingTree = true;
            try
            {
                int room = ganttSplit.Width - ganttSplit.SplitterWidth;
                if (room <= 0)
                {
                    return;
                }
                tree = Math.Min(tree, Math.Max(25, room - chart));
                chart = Math.Min(chart, Math.Max(25, room - tree));
                // The one being made smaller first, so the two never ask for more than there is.
                if (tree < ganttSplit.Panel1MinSize)
                {
                    ganttSplit.Panel1MinSize = tree;
                    ganttSplit.Panel2MinSize = chart;
                }
                else
                {
                    ganttSplit.Panel2MinSize = chart;
                    ganttSplit.Panel1MinSize = tree;
                }
            }
            catch (Exception)
            {
                // The split keeps the least sizes it has.
            }
            finally
            {
                fittingTree = wasFitting;
            }
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
            if (obj != null && obj.MisfiledStage != null)
            {
                parts.Add("a naming error: taken away and put back in " + obj.MisfiledStage + ", it belongs in that stage's layer; correct it in 3ds Max (move it there, or retag it as "
                    + obj.Retagged + ")");
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
                gantt.SetScrub(null, false, false);
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
            // The Gantt's line follows, and scrolls into view unless the day was clicked there.
            gantt.SetScrub(day, endOfDayBox.Checked, !scrubFromGantt);
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
