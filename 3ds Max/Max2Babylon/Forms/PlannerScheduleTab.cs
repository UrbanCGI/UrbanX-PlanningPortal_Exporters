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
    /// the list does not have, and links the objects to their helpers after a review, in one undo step. Clean up scene graph
    /// brings the scene's names to the new naming and, with a list imported, its layers and helpers in line with the list
    /// (D22: there is no separate Build), in one undo step, after a review that asks what it cannot convert alone
    /// (<see cref="PlannerCleanUpForm"/>). The list is read without dates (<see cref="PlannerCleanUp.WithoutDates"/>), so no
    /// name the tab shows or Clean up gives carries one. Fill IN / RM from names reads every object as the Planner's Generate from model would
    /// and gives each row its IN and RM (the side list shows them; selecting there selects in 3ds Max); Add to IN / RM and
    /// Remove write the new names' IN_ / RM_ tags after a review (<see cref="PlannerTagEdit"/>, a layer only the Planner's list
    /// has included); Add layer makes a layer or phase group with its helper. The date
    /// scrubber previews the scene as the Planner's 4D view shows it at a date (not while an export runs). A Gantt beside
    /// the tree (<see cref="PlannerGanttView"/>) charts the same rows, row for row, folding with the tree's groups; a day
    /// clicked there moves the scrubber. Beside it the tree leaves out its Start, Finish and TBC columns (the Gantt draws
    /// the dates), so its Layer column keeps the width it had before the Gantt was there. A row whose dates the Planner has
    /// not settled is highlighted TBC in its Layer cell (D21): one the imported list marks TBC, and a layer of the scene the
    /// list does not have. The tab works on the new names only (D23): on a scene that reads as the old Ph/St names Fill, the
    /// scrubber, the Gantt, Add to IN / RM and Add layer wait until Clean up scene graph makes it read as the new names (the
    /// Gantt, the scrubber and the side list say so). The last imported list, the layers added in Max and the Planner's project start are kept with
    /// the scene and shown again when the window opens. Built in code; the window holds it in its Schedule page.
    /// </summary>
    internal sealed class PlannerScheduleTab : UserControl
    {
        private const int MessageLines = 25;
        private const int NoteLines = 6;

        private const string CleanUpTip = "Bring the scene's names to the new naming in one batch, after a review (hold the scene first: Edit > Fetch is the sure way back): "
            + "dates and TBC out of the layer and helper names, "
            + "old Ph/St tags converted, objects linked under their layers' helpers and clashing names numbered. With the Planner's layer list "
            + "imported, the layers and helpers are also created, renamed and nested as the list has them, never with dates; nothing is deleted. "
            + "What it cannot convert safely is listed with answers to choose. A scene on the old names needs it before the rest of this tab works. "
            + "On a live project, run it only when the planning team says to go ahead: the Planner reads the new names only from its next release.";
        // What the tab says on a scene on the old names (D23) when a button that waits for Clean up is used.
        private const string OldNamesWait = "This scene uses the old Ph/St names: run Clean up scene graph first.";
        private static readonly Color NewBack = Color.FromArgb(226, 243, 226);
        private static readonly Color ChangeBack = Color.FromArgb(255, 243, 205);
        private static readonly Color ProblemBack = Color.FromArgb(248, 215, 218);
        private static readonly Color AddedBack = Color.FromArgb(222, 235, 250);
        private static readonly Color ExtraBack = Color.FromArgb(240, 240, 240);
        private static readonly Color TbcZoneBack = Color.FromArgb(255, 236, 214);
        // A row highlighted TBC (D21): its Layer cell in the TBC colour of the scrubber's zone and the Gantt's header, with a
        // stripe of the Planner's accent at its left edge, which shows on a chosen row too.
        private static readonly Color TbcRowBack = Color.FromArgb(255, 236, 214);
        private static readonly Color TbcStripe = Color.FromArgb(247, 146, 29);
        // Preview in the viewport while it is on.
        private static readonly Color PreviewOnBack = Color.FromArgb(204, 228, 247);
        private static readonly Color PreviewOnBorder = Color.FromArgb(0, 120, 215);

        // The scene changes the tab follows: it shows the new scene's own list.
        private static readonly Autodesk.Max.SystemNotificationCode[] SceneChanges =
        {
            Autodesk.Max.SystemNotificationCode.FilePostOpen,
            Autodesk.Max.SystemNotificationCode.SystemPostNew,
            Autodesk.Max.SystemNotificationCode.SystemPostReset
        };
        // Edit > Undo and Redo: the tab reads the same scene again (its naming, statuses and added layers may have changed:
        // a clean-up taken back is on the old names again).
        private static readonly Autodesk.Max.SystemNotificationCode[] SceneEdits =
        {
            Autodesk.Max.SystemNotificationCode.SceneUndo,
            Autodesk.Max.SystemNotificationCode.SceneRedo
        };

        private readonly Button importButton = new Button();
        private readonly Button refreshButton = new Button();
        private readonly Button cleanUpButton = new Button();
        private readonly Button linkButton = new Button();
        private readonly Button fillButton = new Button();
        private readonly Button addLayerButton = new Button();
        private readonly Label listLabel = new Label();
        private readonly Label sceneLabel = new Label();
        private readonly Label notesLabel = new Label();
        // The tree and the lists below it choose no row by themselves (ListGrid): a row is chosen by the modeller's click or key.
        private readonly DataGridView grid = new ListGrid();
        private readonly DataGridView unlistedGrid = new ListGrid();
        private readonly DataGridView notScheduledGrid = new ListGrid();
        private readonly TabControl bottomTabs = new TabControl();
        private readonly TabPage unlistedPage = new TabPage();
        private readonly TabPage notScheduledPage = new TabPage();
        private readonly Label unlistedLabel = new Label();
        private readonly Label sideLabel = new Label();
        // The chosen row's ID, TBC notes and Notes from the Planner's list, under its heading (ShowInfo).
        private readonly TextBox sideInfo = new TextBox();
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
        // The Planner workspace's project start, when the modeller gives it or the list does (kept with the scene); null:
        // today.
        private DateTime? projectStart;
        private bool updatingStart;
        private PlannerLayerPlan plan;
        private PlannerSceneModel sceneModel;
        // The scene's naming (spec 2.3), read on every reading of the scene: the new names fill from the Planner's dates on
        // the new engine. A scene that reads as the old names needs Clean up scene graph first (D23): Fill, the scrubber, the
        // Gantt, Add to IN / RM and Add layer wait. A scene with nothing that tells (no tagged object, no layer node, no dated
        // layer name: a new scene) is taken as the new names, as nothing in it needs Clean up.
        private PlannerNamingKind sceneNaming = PlannerNamingKind.New;
        private PlannerSchedule schedule;
        private bool filled;
        // Filled, or asked to fill, while the scene read as the old names: filled as soon as it reads as the new names (Replan).
        private bool fillWaiting;
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
        private Autodesk.Max.GlobalDelegates.Delegate5 sceneEdited;
        // A reading after Edit > Undo or Redo is waiting: several undo steps in a row are read once.
        private bool editPending;

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
            SetUpButton(refreshButton, "Read the scene", (s, e) => Replan(), "Work out each row's status (and, once filled, its IN and RM) in the scene as it is now.");
            SetUpButton(cleanUpButton, "Clean up scene graph\u2026", CleanUpButton_Click, CleanUpTip);
            SetUpButton(linkButton, "Link objects to their helpers\u2026", LinkButton_Click,
                "Link every object on a phasing layer under that layer's helper, after a review.");
            SetUpButton(fillButton, "Fill IN / RM from names", FillButton_Click,
                "Read every object as the Planner's Generate from model would: each row's IN and RM are the objects whose install or removal its layer holds, "
                + "by their IN_ / RM_ tags and the layer IDs the tags name. The dates only time them. Waits for a scene on the old names to be cleaned up.");
            SetUpButton(addLayerButton, "Add layer\u2026", AddLayerButton_Click,
                "Add a layer (its ID and name) or a phase group, with its helper, under a phasing root or a phase group, chosen in the dialog's Under: it "
                + "starts on the chosen row's phase group or root, or on the (first) phasing root with no row chosen (Ctrl+click the chosen row to choose none). No date: the "
                + "Planner gives every date, and the layer shows TBC until its list has it. It reaches the Planner with the next GLB. Waits for a scene on the "
                + "old names to be cleaned up.");
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(refreshButton);
            buttons.Controls.Add(cleanUpButton);
            buttons.Controls.Add(linkButton);
            buttons.Controls.Add(fillButton);
            buttons.Controls.Add(addLayerButton);

            // The Planner workspace's project start: it never moves on the new names, and layers with no dates are parked one
            // day each from it (where the scrubber starts). By the scrubber, beside the layers at work, so it takes no line of
            // its own.
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
            const string startTip = "The Planner workspace's project start: the first day of its Gantt. It never moves; layers with no dates in the Planner "
                + "are parked one day each from it (unticked, from today). Importing a layer list that gives a project start sets it. Kept with the scene.";
            toolTip.SetToolTip(projectStartLabel, PlannerTipText.Wrap(startTip));
            toolTip.SetToolTip(projectStartPicker, PlannerTipText.Wrap(startTip));
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
            // The lists keep headers as tall as their font needs (at any display scaling); the tree's is fitted to the Gantt's.
            unlistedGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
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
            notScheduledGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
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

            // The side list: the chosen row's IN and RM. Its heading (the row's name, then its counts or what to do first)
            // wraps to the list's width, so a long layer name does not cut off the rest (FitSideLabel).
            sideLabel.Dock = DockStyle.Top;
            sideLabel.AutoSize = true;
            sideLabel.Padding = new Padding(4, 3, 4, 2);
            sideLabel.Font = boldFont;
            // Under the heading, what the Planner's folder editor holds for the row: read-only text, so an ID or a note can be
            // selected and copied; as tall as its lines up to six, then it scrolls; hidden for a row with none (FitSideInfo).
            sideInfo.Dock = DockStyle.Top;
            sideInfo.Multiline = true;
            sideInfo.ReadOnly = true;
            sideInfo.WordWrap = true;
            sideInfo.ScrollBars = ScrollBars.Vertical;
            sideInfo.BorderStyle = BorderStyle.None;
            sideInfo.Margin = new Padding(4, 0, 4, 3);
            sideInfo.Visible = false;
            SetUpGrid(sideGrid);
            sideGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
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
                "Install the objects selected in 3ds Max in this layer, after a review: IN_ at the start of the name for their own layer (_IN at the end when "
                + "the start holds RM_), _IN_<ID> at the end for another layer, one only the Planner's list has included. Only the names change; nothing is moved or linked.");
            SetUpButton(addRmButton, "Add to RM\u2026", (s, e) => AddSelected(PlannerTaskType.Dismantle),
                "Remove the objects selected in 3ds Max in this layer, after a review: RM_ at the start of the name for their own layer (_RM at the end when "
                + "the start holds IN_), _RM_<ID> at the end for another layer, one only the Planner's list has included. Only the names change; nothing is moved or linked.");
            SetUpButton(removeButton, "Remove from list\u2026", RemoveButton_Click,
                "Take the objects chosen in this list out of it, after a review: their tag of that column comes out of their names where it is, and the other "
                + "tag stays. An object left with no tag is no longer scheduled.");
            sideButtons.Controls.Add(addInButton);
            sideButtons.Controls.Add(addRmButton);
            sideButtons.Controls.Add(removeButton);
            sideSplit.Dock = DockStyle.Fill;
            sideSplit.Orientation = Orientation.Vertical;
            sideSplit.FixedPanel = FixedPanel.Panel2;
            sideSplit.Panel1.Controls.Add(split);
            // Docked last to first: the heading at the top, the Planner's ID and notes under it, the buttons at the bottom,
            // the list in what is left.
            sideSplit.Panel2.Controls.Add(sideGrid);
            sideSplit.Panel2.Controls.Add(sideButtons);
            sideSplit.Panel2.Controls.Add(sideInfo);
            sideSplit.Panel2.Controls.Add(sideLabel);
            sideSplit.SizeChanged += (s, e) => PlaceSplitter();
            sideSplit.Panel2.ClientSizeChanged += (s, e) =>
            {
                FitSideLabel();
                FitSideInfo();
            };

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
            // On, it is filled and edged in blue, so it reads as pressed; off, a plain flat button like the others.
            previewBox.FlatAppearance.CheckedBackColor = PreviewOnBack;
            previewBox.CheckedChanged += (s, e) => previewBox.FlatAppearance.BorderColor = previewBox.Checked ? PreviewOnBorder : Color.Empty;
            previewBox.CheckedChanged += PreviewBox_CheckedChanged;
            toolTip.SetToolTip(previewBox, PlannerTipText.Wrap("Hide the objects the Planner's 4D view hides at the scrubber's date. Leaving gives every object its own hidden state back; it is never an undo step."));
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
            toolTip.SetToolTip(endOfDayBox, PlannerTipText.Wrap("Show the day once its work is done. Unticked, the start of the day, as the Planner's 4D view shows a day."));
            SetUpButton(selectDayButton, SelectDayBase, SelectDayButton_Click, "Select in 3ds Max the objects installed or removed on this day.");
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

        // The side list's heading wraps at the side list's width (it is docked at its top, sized to its text).
        private void FitSideLabel()
        {
            int width = sideSplit.Panel2.ClientSize.Width;
            if (width > 0 && sideLabel.MaximumSize.Width != width)
            {
                sideLabel.MaximumSize = new Size(width, 0);
            }
        }

        // The chosen row's ID, TBC notes and Notes from the Planner's list (PlannerLayerRow.PlannerInfoText), under the
        // heading; hidden when it has none.
        private void ShowInfo(PlannerScheduleRow row)
        {
            var text = row != null && row.ListRow != null ? row.ListRow.PlannerInfoText() : string.Empty;
            sideInfo.Text = text;
            sideInfo.Visible = text.Length > 0;
            FitSideInfo();
        }

        // The ID and notes box as tall as its text wraps to at the side list's width, up to six lines (then it scrolls).
        private void FitSideInfo()
        {
            if (!sideInfo.Visible)
            {
                return;
            }
            int width = Math.Max(40, sideSplit.Panel2.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            int text = TextRenderer.MeasureText(sideInfo.Text, sideInfo.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            sideInfo.Height = Math.Min(text, sideInfo.Font.Height * 6) + 6;
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
        // over the lists' tab strip, and the side list's heading, header, two rows and buttons. The heading counts one line
        // whatever row is chosen (a long name wraps it), so the notes do not give way or come back as rows are chosen.
        private int SplitMinHeight()
        {
            int tree = treeMinHeight + split.SplitterWidth + listsMinHeight;
            int side = sideLabel.Font.Height + sideLabel.Padding.Vertical + sideButtons.Height + sideRowsMinHeight;
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

        private const string SelectDayBase = "Select changing this day";

        private static string SelectDayText(int count)
        {
            return SelectDayBase + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";
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
                // The IN and RM columns, and the side list's marker column, hold "RM" whole at any display scaling.
                grid.Columns[5].MinimumWidth = grid.Columns[6].MinimumWidth = Math.Max(30, TextRenderer.MeasureText("RM", grid.Font).Width + 14);
                unlistedGrid.Columns[2].MinimumWidth = unlistedGrid.Columns[3].MinimumWidth = Math.Max(30, TextRenderer.MeasureText("RM", unlistedGrid.Font).Width + 14);
                sideGrid.Columns[0].MinimumWidth = Math.Max(30, TextRenderer.MeasureText("RM", sideGrid.Font).Width + 14);
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
            toolTip.SetToolTip(button, PlannerTipText.Wrap(tip));
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
                    sceneEdited = new Autodesk.Max.GlobalDelegates.Delegate5(OnSceneEdited);
                    foreach (var code in SceneEdits)
                    {
                        Autodesk.Max.GlobalInterface.Instance.RegisterNotification(sceneEdited, null, code);
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
                    var edited = sceneEdited;
                    sceneEdited = null;
                    if (edited != null)
                    {
                        foreach (var code in SceneEdits)
                        {
                            Autodesk.Max.GlobalInterface.Instance.UnRegisterNotification(edited, null, code);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Without the notifications the tab still reads the scene again on Read the scene and before changes.
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
                    fillWaiting = false;
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

        // Edit > Undo or Redo in 3ds Max: the same scene, read again once the steps are done (the list stays, and the fill
        // waits while the scene reads as the old names), so the buttons, the Gantt, the scrubber and the added layers follow a
        // clean-up taken back or put back. A tab not shown
        // reads the scene when it is shown; while an export runs it waits for the next reading.
        private void OnSceneEdited(IntPtr param, Autodesk.Max.INotifyInfo info)
        {
            if (IsDisposed || !IsHandleCreated || editPending)
            {
                return;
            }
            try
            {
                editPending = true;
                BeginInvoke(new Action(() =>
                {
                    editPending = false;
                    if (IsDisposed || !Visible || !SceneFree())
                    {
                        stale = true;
                        return;
                    }
                    EndPreview();
                    Replan();
                }));
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
                editPending = false;
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
                    // Read as Clean up builds from it: a list exported before the new names gives no dated layer names (D22).
                    list = PlannerCleanUp.WithoutDates(PlannerLayerList.Parse(text));
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
                ImportFile(dialog.FileName);
            }
        }

        // Reads the list from the file chosen, keeps it with the scene, and takes its project start.
        private void ImportFile(string fileName)
        {
            PlannerLayerList read;
            string text;
            try
            {
                string note;
                text = PlannerLayerList.Decode(File.ReadAllBytes(fileName), out note);
                read = PlannerLayerList.Parse(text);
                if (note != null)
                {
                    read.Lead(note);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file could not be read: " + ex.Message, "Import layer list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!read.Usable)
            {
                MessageBox.Show(this, Path.GetFileName(fileName) + " is not a layer list the Planner exported, so nothing was imported.\r\n\r\n"
                    + string.Join("\r\n", read.Errors.ToArray()), "Import layer list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            list = PlannerCleanUp.WithoutDates(read);
            listText = text;
            listFile = fileName;
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
            // The list's Project start column sets the Planner project start (spec 5.4), ready for the new names on a scene still
            // to be cleaned up too; a list made before the Planner wrote the column leaves the start as it was.
            if (list.ProjectStart.HasValue && list.ProjectStart != projectStart)
            {
                projectStart = list.ProjectStart;
                try
                {
                    PlannerScheduleScene.SaveProjectStart(projectStart);
                }
                catch (Exception)
                {
                    // Used for now; the scene keeps what it had.
                }
                ShowProjectStart();
                list.Notes.Add("Planner project start set to " + projectStart.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ", from the list.");
                Replan();
            }
        }

        // The statuses

        // The options Generate from model is run with: the Planner's project start (which never moves on the new names) and
        // its folders with their dates, as the list gives them (spec 5.4, 5.6). No date comes from 3ds Max (D20).
        private PlannerScheduleOptions Options()
        {
            return new PlannerScheduleOptions
            {
                Zone = TimeZoneInfo.Local,
                Today = DateTime.Today,
                WorkspaceStart = projectStart,
                ProjectStart = projectStart,
                Folders = PlannerScheduleRows.PlannerFolders(list, plan)
            };
        }

        // A scene that reads as the old names (D23): only Import, Read the scene, Clean up and Link work on it.
        private bool OldNames
        {
            get { return sceneNaming == PlannerNamingKind.Old; }
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
                // The scene as the export hands it to the Planner, and its naming: on the new names (or a scene with nothing
                // that tells) the new names' engine; a scene on the old names waits for Clean up (D23), unfilled.
                var exported = new PlannerScheduleModel(snapshot);
                var naming = PlannerSchedule.NamingOfScene(exported);
                sceneNaming = naming != null && naming.Naming == PlannerNamingKind.Old ? PlannerNamingKind.Old : PlannerNamingKind.New;
                if (list != null && list.Usable)
                {
                    plan = PlannerLayerPlanner.Plan(list, sceneModel);
                }
                PruneAdded();
                if (OldNames)
                {
                    // The fill waits while the scene reads as the old names (Edit > Undo of a clean-up) and comes back
                    // when it reads as the new names again (Edit > Redo).
                    fillWaiting = fillWaiting || filled;
                    filled = false;
                    schedule = null;
                }
                else
                {
                    if (fillWaiting)
                    {
                        filled = true;
                        fillWaiting = false;
                    }
                    if (filled)
                    {
                        schedule = PlannerSchedule.BuildV2(exported, Options(), naming);
                    }
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
        // or one the Planner's list now has (the Planner's from then on), is not shown as an added layer, and one Clean up
        // renamed follows its layer back when the clean-up is undone (and forward again on Redo). The records are read from
        // the scene first, as Clean up scene graph run from MAXScript may have renamed them. Only a record the Planner's list
        // now has is taken out of the records kept with the scene (unless an export is running: then on the next reading);
        // one whose layer is gone stays there, so an Edit > Redo that brings the layer back finds its record again.
        private void PruneAdded()
        {
            if (sceneModel == null)
            {
                return;
            }
            var stored = added;
            if (SceneFree())
            {
                try
                {
                    stored = PlannerScheduleScene.LoadAdded();
                }
                catch (Exception)
                {
                    // The records in memory are used.
                }
            }
            var kept = plan != null ? PlannerAddedLayerStore.StillAdded(stored, plan) : stored;
            added = PlannerAddedLayerStore.Present(kept, sceneModel);
            if (kept.Count == stored.Count || !SceneFree())
            {
                return;
            }
            try
            {
                PlannerScheduleScene.SaveAdded(kept);
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
            RowChosen(unlistedGrid.SelectedRows.Count > 0 ? unlistedGrid : grid);
            ShowScrubber();
        }

        private void FillLists()
        {
            int firstRow = grid.Rows.Count > 0 ? grid.FirstDisplayedScrollingRowIndex : -1;
            var chosen = currentRow != null ? currentRow.LayerName ?? currentRow.Name : null;
            bool chosenUnlisted = currentRow != null && unlistedRows.Contains(currentRow);
            grid.Rows.Clear();
            unlistedGrid.Rows.Clear();
            notScheduledGrid.Rows.Clear();
            currentRow = null;

            var notes = new List<string>();
            if (list == null)
            {
                listLabel.Text = "No layer list imported yet: the scene's own phasing tree is shown.";
                sceneLabel.Text = readError ?? "Export the list from the Planner (Layer list for 3ds Max (CSV)) and import it here: Clean up scene graph then makes "
                    + "one layer and one helper of the same name for each row, nested as in the Planner.";
                unlistedLabel.Text = "Phasing layers in the scene not in the Planner's list are listed here once a list is imported.";
            }
            else
            {
                var source = listFile != null ? Path.GetFileName(listFile) : "The imported list";
                listLabel.Text = source + (listImported.HasValue ? ", imported " + listImported.Value.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture) : string.Empty)
                    + ": " + list.Describe();
                notes.AddRange(list.Notes);
                notes.AddRange(list.DateNotes);
                if (plan != null)
                {
                    sceneLabel.Text = "In the scene: " + plan.Summary();
                    notes.AddRange(plan.Notes);
                }
                else
                {
                    sceneLabel.Text = readError ?? (stale ? "Not compared with the scene yet: press Read the scene." : "In the scene: not known.");
                }
            }
            if (filled && schedule != null)
            {
                sceneLabel.Text += "\r\n" + FillSummary();
                if (schedule.Usable)
                {
                    notes.AddRange(schedule.LayerIssues.Select(i => "Layer name: " + i));
                    if (schedule.V2 != null)
                    {
                        notes.AddRange(schedule.NotesV2());
                    }
                }
            }
            else if (!OldNames && readError == null)
            {
                notes.Add(PlannerScheduleRows.NewNamesFillNote(list));
            }
            ShowNotes(notes);

            treeRows = PlannerScheduleRows.Tree(list, plan, sceneModel, added, filled ? schedule : null, sceneNaming);
            unlistedRows = PlannerScheduleRows.Unlisted(plan, added, filled ? schedule : null, sceneNaming, sceneModel);
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
                // TBC (D21): what the list marks TBC, and a layer of the scene the list does not have.
                var tbc = PlannerScheduleRows.TbcText(row.TbcMark);
                line.CreateCells(grid, row.Name, row.Kind, row.Start, row.Finish, tbc != null ? "Yes" : string.Empty, Count(row, true), Count(row, false), row.Status);
                // The tree: each level indented, room for a fold triangle before every name.
                line.Cells[0].Style.Padding = new Padding(TextIndent(row.Depth), 0, 0, 0);
                // Groups and phasing roots in bold.
                if (row.IsGroup)
                {
                    line.Cells[0].Style.Font = boldFont;
                }
                line.DefaultCellStyle.BackColor = BackOf(row);
                line.Cells[7].ToolTipText = PlannerTipText.Wrap(Details(row));
                // The whole name, its IDs, why it is TBC, and the Planner's ID, TBC notes and Notes for it.
                line.Cells[0].ToolTipText = PlannerTipText.Wrap(row.Name + (row.ListRow != null
                    ? "\r\nPlanner ID " + row.ListRow.PlannerId + (row.ListRow.IdKey != null ? "; layer ID " + row.ListRow.IdKey : string.Empty)
                    : row.LayerName != null && row.LayerName != row.Name ? "\r\n" + row.LayerName : string.Empty)
                    + (tbc != null ? "\r\n" + tbc : string.Empty)
                    + (row.ListRow != null && row.ListRow.HasPlannerInfo ? "\r\n" + row.ListRow.PlannerInfoText() : string.Empty));
                if (tbc != null)
                {
                    line.Cells[0].Style.BackColor = TbcRowBack;
                    line.Cells[4].ToolTipText = PlannerTipText.Wrap(tbc);
                }
                lines.Add(line);
            }
            grid.Rows.AddRange(lines.ToArray());
            // On the old names the Gantt waits (D23); the tree keeps its model for its folds.
            gantt.Model = OldNames ? null : ganttModel;
            gantt.Message = OldNames ? "The Gantt shows once the scene reads as the new names: run Clean up scene graph\u2026 first." : null;

            if (plan == null)
            {
                unlistedLabel.Text = list == null ? unlistedLabel.Text : "In the scene but not in the Planner's list: not known until the scene is read.";
            }
            else
            {
                // The layers added in Max that the list does not have yet show in the tree, not here; the summary above counts them.
                unlistedLabel.Text = PlannerScheduleRows.UnlistedText(unlistedRows.Count, Math.Max(0, plan.NotInList.Count - (unlistedRows.Count - plan.UnlistedRoots.Count)));
                unlistedGrid.Rows.AddRange(unlistedRows.Select(r =>
                {
                    var line = new DataGridViewRow { Tag = r };
                    line.CreateCells(unlistedGrid, r.Name, r.UnlistedItem != null ? r.UnlistedItem.What : string.Empty, Count(r, true), Count(r, false), r.Status);
                    // A layer the list does not have is TBC (D21), as in the tree.
                    var tbc = PlannerScheduleRows.TbcText(r.TbcMark);
                    if (tbc != null)
                    {
                        line.Cells[0].Style.BackColor = TbcRowBack;
                        line.Cells[0].ToolTipText = PlannerTipText.Wrap(r.Name + "\r\n" + tbc);
                    }
                    return line;
                }).ToArray());
            }
            unlistedPage.Text = "Not in the Planner's list" + (plan != null ? " (" + unlistedRows.Count.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
            // On the new names the page also lists the objects the Planner schedules but reads only in part, counted apart.
            int inPart = filled && schedule != null ? schedule.NotScheduled.Count(n => n.InPart) : 0;
            notScheduledGrid.Columns[2].HeaderText = inPart > 0 ? "Why the Planner does not schedule it, or reads it only in part" : "Why the Planner does not schedule it";
            if (filled && schedule != null)
            {
                notScheduledGrid.Rows.AddRange(schedule.NotScheduled.Select(n =>
                {
                    var line = new DataGridViewRow { Tag = n };
                    line.CreateCells(notScheduledGrid, n.Name, n.LayerName ?? string.Empty, n.Reason);
                    line.Cells[2].ToolTipText = PlannerTipText.Wrap(n.Reason);
                    return line;
                }).ToArray());
                notScheduledPage.Text = "Not scheduled (" + (schedule.NotScheduled.Count - inPart).ToString(CultureInfo.InvariantCulture) + ")"
                    + (inPart > 0 ? ", read in part (" + inPart.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
            }
            else
            {
                notScheduledPage.Text = "Not scheduled";
            }
            linkButton.Enabled = true;
            // On the old names Fill and Add layer wait for Clean up (D23).
            fillButton.Enabled = !OldNames;
            addLayerButton.Enabled = !OldNames;

            // The chosen row again, if it is still there; otherwise no row chosen (a grid makes its first row current, and
            // chooses it, by itself as rows are added: not the modeller's choice). Then the tree scrolled back to where it was.
            ChooseAgain(chosenUnlisted, chosen);
            ClearChoice(notScheduledGrid);
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
        }

        // The new names' summary (spec 5.6): what the preview scheduled, the layers it parked, and where its project start comes from.
        private string FillSummary()
        {
            return schedule.Usable ? schedule.SummaryV2(projectStart.HasValue) : "IN / RM: " + schedule.Message;
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
            toolTip.SetToolTip(notesLabel, PlannerTipText.Wrap(string.Join("\r\n", all.ToArray())));
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
            if (row.Layer != null && row.Layer.Id != null)
            {
                // The Planner's folder for the layer ID, and where its dates come from.
                var layer = row.Layer;
                var days = layer.Start.ToString("d MMM yyyy", CultureInfo.CurrentCulture) + (layer.End != layer.Start ? " to " + layer.End.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : string.Empty);
                parts.Add("The Planner files its work in the folder " + layer.Label + " (layer " + PlannerNaming.LayerIdText(layer.Id) + "), "
                    + (layer.ParkedOn.HasValue ? "parked on " + days + ": it has no dates in the Planner yet"
                        : layer.DateSource == PlannerDateSource.Planner ? days + ", from the Planner's list"
                        : "undated, with no work to park")
                    + (layer.Tbc ? ", TBC" : string.Empty) + (layer.InScene ? "." : "; the scene has no layer of its ID yet."));
            }
            if (string.IsNullOrEmpty(row.Status) == false && parts.Count == 0)
            {
                parts.Add(row.Status);
            }
            var tbc = PlannerScheduleRows.TbcText(row.TbcMark);
            if (tbc != null)
            {
                parts.Add(tbc);
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
        // Layer header gets one for all of them. A row highlighted TBC (D21) gets a stripe of the accent along the left edge
        // of its Layer cell, which shows on a chosen row too.
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex != 0)
            {
                return;
            }
            bool glyph = e.RowIndex >= 0 ? RowHasChildren(e.RowIndex) : AnyGroup();
            bool tbc = e.RowIndex >= 0 && e.RowIndex < shownRows.Count && shownRows[e.RowIndex].TbcMark != PlannerRowTbc.None;
            if (!glyph && !tbc)
            {
                return;
            }
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            if (tbc)
            {
                using (var stripe = new SolidBrush(TbcStripe))
                {
                    e.Graphics.FillRectangle(stripe, e.CellBounds.Left, e.CellBounds.Top, Math.Max(3, glyphSize / 3), Math.Max(0, e.CellBounds.Height - 1));
                }
            }
            if (!glyph)
            {
                e.Handled = true;
                return;
            }
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

        // Shows and hides the tree's rows as the folds say; a chosen row that goes is replaced by the group above it. The
        // current cell of a row not chosen (a Ctrl+click took the choice away) goes with its row, and nothing is chosen.
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
                bool chosen = grid.Rows[current].Selected;
                int above = current;
                while (above >= 0 && !shown[above])
                {
                    above = ganttModel.Rows[above].Parent;
                }
                try
                {
                    grid.CurrentCell = chosen && above >= 0 ? grid.Rows[above].Cells[0] : null;
                    if (chosen && above >= 0)
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

        // A row chosen before a reading rebuilt the lists, chosen again by its layer's name, with its current cell: in the
        // list it was chosen in, else in the other (a layer at work that the Planner's list does not have has a row in both).
        // None chosen, or that row gone, leaves neither list with a row chosen or a current cell. False when the row named
        // is not there any more.
        private bool ChooseAgain(bool unlisted, string name)
        {
            ClearChoice(grid);
            ClearChoice(unlistedGrid);
            if (name == null)
            {
                return true;
            }
            foreach (var source in unlisted ? new[] { unlistedGrid, grid } : new[] { grid, unlistedGrid })
            {
                foreach (DataGridViewRow line in source.Rows)
                {
                    var row = (PlannerScheduleRow)line.Tag;
                    if (line.Visible && string.Equals(row.LayerName ?? row.Name, name, StringComparison.Ordinal))
                    {
                        try
                        {
                            source.CurrentCell = line.Cells[0];
                            line.Selected = true;
                            return true;
                        }
                        catch (InvalidOperationException)
                        {
                            ClearChoice(source);
                            return false;
                        }
                    }
                }
            }
            return false;
        }

        // No row chosen in a list and no current cell, which a grid sets on its first row by itself once filled.
        private static void ClearChoice(DataGridView view)
        {
            view.ClearSelection();
            if (view.CurrentCell != null)
            {
                try
                {
                    view.CurrentCell = null;
                }
                catch (InvalidOperationException)
                {
                    // The grid keeps its current cell; no row is chosen.
                }
            }
        }

        /// <summary>
        /// A list that chooses no row by itself. A DataGridView makes its first row current, and with full-row selection
        /// chooses it, when its handle is made (the tab or its page first shown), when it takes the focus and when a header is
        /// clicked, with no cell current: the tab would take that row for the modeller's choice (Add layer under it, the side
        /// list, a selection in 3ds Max). That choice is taken back here before anything hears of it, so a row is chosen only
        /// by the modeller's click on it or key (an arrow key with none current chooses the first row in view). Rows added choose one
        /// too: FillLists clears it.
        /// </summary>
        private sealed class ListGrid : DataGridView
        {
            private bool unchoosing;

            protected override void OnHandleCreated(EventArgs e)
            {
                bool held = Hold();
                try
                {
                    base.OnHandleCreated(e);
                    Unchoose(held);
                }
                finally
                {
                    Release(held);
                }
            }

            protected override void OnEnter(EventArgs e)
            {
                bool held = Hold();
                try
                {
                    base.OnEnter(e);
                    Unchoose(held);
                }
                finally
                {
                    Release(held);
                }
            }

            protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
            {
                // A click on a header; one on a row chooses that row.
                bool held = e.RowIndex < 0 && Hold();
                try
                {
                    base.OnCellMouseDown(e);
                    Unchoose(held);
                }
                finally
                {
                    Release(held);
                }
            }

            protected override void OnSelectionChanged(EventArgs e)
            {
                if (!unchoosing)
                {
                    base.OnSelectionChanged(e);
                }
            }

            // True when no cell is current (and no step of the list's own is already held): the step about to run may make
            // the first row current, which is then taken back unheard.
            private bool Hold()
            {
                if (CurrentCell != null || unchoosing)
                {
                    return false;
                }
                unchoosing = true;
                return true;
            }

            private void Unchoose(bool held)
            {
                if (held)
                {
                    ClearChoice(this);
                }
            }

            private void Release(bool held)
            {
                if (held)
                {
                    unchoosing = false;
                }
            }
        }

        private void ShowSide()
        {
            updatingSide = true;
            try
            {
                sideGrid.Rows.Clear();
                var row = currentRow;
                ShowInfo(row);
                if (row == null)
                {
                    sideLabel.Text = OldNames ? "IN and RM wait for Clean up scene graph." : "Choose a row to see its IN and RM.";
                }
                else if (OldNames)
                {
                    sideLabel.Text = row.Name + ": IN and RM wait for Clean up scene graph.";
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
                                line.CreateCells(sideGrid, pair.Key, name, SideNote(schedule, row, activity, obj));
                                line.Cells[2].ToolTipText = PlannerTipText.Wrap(activity.Name + ": " + Times(activity));
                                lines.Add(line);
                            }
                        }
                    }
                    sideGrid.Rows.AddRange(lines.ToArray());
                    sideGrid.ClearSelection();
                }
                // Add to IN / RM and Remove write the new names' tags (PlannerTagEdit) for a layer row of the preview, one only the
                // Planner's list has included; on the old names they wait for Clean up (D23).
                bool editable = !OldNames && row != null && row.Editable && filled && schedule != null && schedule.Usable;
                addInButton.Enabled = editable;
                addRmButton.Enabled = editable;
                removeButton.Enabled = editable;
            }
            finally
            {
                updatingSide = false;
            }
        }

        private static string SideNote(PlannerSchedule schedule, PlannerScheduleRow row, PlannerScheduleActivity activity, PlannerScheduleObject obj)
        {
            var parts = new List<string>();
            if (obj != null && obj.MaxName != null && obj.MaxName != obj.Name)
            {
                parts.Add("called " + obj.MaxName + " in 3ds Max: another node the export writes before it has that name, so the export renames it and the Planner reads it as an object of its own");
            }
            if (obj != null && obj.FiledIn != null && obj.FiledIn != row.Layer)
            {
                parts.Add("sits in " + (obj.FiledIn.Id != null ? obj.FiledIn.Label : obj.FiledIn.Raw));
            }
            // Where the event is placed, and a reference to a layer nobody has.
            if (activity.Unresolved)
            {
                var id = PlannerNaming.LayerIdText(activity.EventLayer);
                parts.Add((id == PlannerNaming.DateUnknownLayer ? "for layer " + id + ", work whose date is not known yet" : "names layer " + id + ", which neither the scene nor the list has")
                    + (activity.Type == PlannerTaskType.Install ? ": at the project start" : ": parked after " + schedule.WorksEndText));
            }
            else if (activity.Group == 3)
            {
                parts.Add("at the end of the layer");
            }
            else if (activity.Group == 2)
            {
                parts.Add("named for this layer by its tag");
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
            if (OldNames && readError == null)
            {
                // Read again, the scene is on the old names (D23): Replan left it unfilled.
                MessageBox.Show(this, OldNamesWait, "Fill IN / RM from names", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (schedule != null && !schedule.Usable)
            {
                MessageBox.Show(this, schedule.Message, "Fill IN / RM from names", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AddSelected(PlannerTaskType type)
        {
            var row = currentRow;
            if (OldNames)
            {
                MessageBox.Show(this, OldNamesWait, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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
            if (OldNames)
            {
                MessageBox.Show(this, OldNamesWait, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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

        // A layer (its ID and name, no date: D20) or a phase group under a phasing root or a phase group, chosen in the
        // dialog's Under, which starts on the chosen row's phase group or root (the first root with no row chosen), after a
        // review, in one undo record. On a scene on the old names it waits for Clean up (D23).
        private void AddLayerButton_Click(object sender, EventArgs e)
        {
            if (!MayChange())
            {
                return;
            }
            // The row chosen (none: the first phasing root), by its layer's name: the reading below rebuilds both lists and chooses
            // it again (FillLists).
            var chosenName = currentRow != null ? currentRow.LayerName ?? currentRow.Name : null;
            // The scene as it is now (an Edit > Undo since the last reading may have put it back on the old names), as Fill does.
            Replan();
            if (OldNames)
            {
                MessageBox.Show(this, OldNamesWait, "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var row = currentRow;
            if (chosenName != null && (row == null || !string.Equals(row.LayerName ?? row.Name, chosenName, StringComparison.Ordinal)))
            {
                MessageBox.Show(this, chosenName + " is not there any more (the scene has changed), so no row is chosen now: press Add layer again to add under "
                    + "a phasing root or a phase group, or choose a row first.", "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string chosenLayer = null;
            if (row != null)
            {
                if (row.Source == PlannerScheduleRowSource.Outside)
                {
                    chosenLayer = row.LayerName;
                }
                else if (row.Root != null)
                {
                    // A phasing root's row stands for the root's Max layer, or, while the root has no Max layer of its name, for
                    // the root by its name: Add layer makes the root's missing Max layer, as it does for the first root.
                    chosenLayer = row.Root.Layer != null ? row.Root.Layer.Name : row.Root.Name;
                }
                else if (row.LayerName == null || row.Unit == null || row.Unit.Layer == null)
                {
                    MessageBox.Show(this, row.Name + " has no Max layer in the scene yet: run Clean up scene graph with the list imported first, or choose another row "
                        + "(or none: Ctrl+click the chosen row).", "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                else
                {
                    chosenLayer = row.Unit.Layer.Name;
                }
            }
            // Under: the phasing roots and the phase groups that can take a layer (the Planner files a layer only in the phase
            // group above it, or at the top), starting on the chosen row's, with a note when that is not the row itself.
            var parents = PlannerAddLayer.Parents(sceneModel);
            string note;
            var start = PlannerAddLayer.StartUnder(sceneModel, parents, chosenLayer, out note);
            PlannerNewLayer spec;
            string parent;
            using (var dialog = new PlannerAddLayerForm(parents, start, note))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                spec = dialog.Spec;
                parent = dialog.UnderLayer;
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
                try
                {
                    // Added to the records the scene keeps (read again: MAXScript or another reading may have changed them), a
                    // record of the same layer name dropped (a layer taken back and added again is recorded once).
                    PlannerScheduleScene.SaveAdded(PlannerAddedLayerStore.WithAdded(PlannerScheduleScene.LoadAdded(), fresh.Added));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The layer was added but could not be noted with the scene: " + ex.Message, "Add layer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Replan();
            MessageBox.Show(this, Shorten(result.Describe(held)), "Add layer", MessageBoxButtons.OK, result.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
                dateLabel.BackColor = SystemColors.Control;
                // No day to count while the scrubber is off.
                selectDayButton.Text = SelectDayBase;
                activeLabel.Text = OldNames ? "The scrubber works once the scene reads as the new names. " + OldNamesWait
                    : filled && schedule != null ? schedule.Message : "Fill IN / RM from names to scrub through the schedule.";
                toolTip.SetToolTip(activeLabel, PlannerTipText.Wrap(activeLabel.Text));
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
                parts.Add("TBC zone: removals for a layer nobody has are parked here, the day after " + schedule.WorksEndText + ", as the Planner parks them");
            }
            if (active.Count > 0)
            {
                parts.Add("At work: " + string.Join(", ", active.Select(l => l.Label + (l.ParkedOn.HasValue ? " (parked: no dates yet)" : l.Tbc ? " (TBC)" : string.Empty)).ToArray()));
            }
            else if (!tbcZone)
            {
                parts.Add("No layer at work");
            }
            int changing = schedule.ChangingOn(day).Count;
            selectDayButton.Text = SelectDayText(changing);
            activeLabel.Text = string.Join(". ", parts.ToArray()) + ".";
            toolTip.SetToolTip(activeLabel, PlannerTipText.Wrap(activeLabel.Text));
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

        // Clean up scene graph (naming v2, spec 5.1): plans on the scene (with the imported layer list, D22: the layers and
        // helpers brought in line with it too, as Build layers and helpers did), shows the review (answers, word fixes, a copy
        // and a hold first), checks the scene has not changed, saves the copy, holds, runs one batch in one undo record, renames
        // the Add layer records with their layers, and reads the scene again, now on the new names.
        private void CleanUpButton_Click(object sender, EventArgs e)
        {
            const string title = PlannerCleanUp.UndoLabel;
            if (!MayChange())
            {
                return;
            }
            EndPreview();
            // The list as the tab shows it (read without dates); a list that cannot be used is no list.
            var cleanUpList = list != null && list.Usable ? list : null;
            List<PhasingWordFix> wordFixes;
            bool wordFixesSaved;
            try
            {
                wordFixesSaved = PhasingScene.HasSavedWordFixes();
                wordFixes = PhasingScene.LoadWordFixes();
            }
            catch (Exception)
            {
                wordFixesSaved = false;
                wordFixes = PhasingNameFixer.DefaultWordFixes();
            }
            PlannerCleanUpPlan reviewed;
            try
            {
                reviewed = PlannerCleanUp.Plan(PlannerScheduleScene.Read(), wordFixes, cleanUpList);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (reviewed.Message != null)
            {
                MessageBox.Show(this, reviewed.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PlannerCleanUpResult planned;
            Dictionary<string, string> choices;
            bool hold;
            string copy;
            using (var review = new PlannerCleanUpForm(reviewed, wordFixes, wordFixesSaved,
                fixes => PlannerCleanUp.Plan(PlannerScheduleScene.Read(), fixes, cleanUpList), SaveWordFixes))
            {
                var answer = review.ShowDialog(this);
                wordFixes = review.WordFixes;
                if (answer != DialogResult.OK)
                {
                    return;
                }
                planned = review.Result;
                choices = review.Choices;
                hold = review.HoldFirst;
                copy = review.CopyPath;
            }

            // The window does not stop anyone working in the scene (or starting an export), so it is checked again first.
            if (!MayChange())
            {
                return;
            }
            PlannerCleanUpPlan fresh;
            PlannerCleanUpResult resolved;
            PlannerSceneSnapshot before;
            try
            {
                before = PlannerScheduleScene.Read();
                fresh = PlannerCleanUp.Plan(before, wordFixes, cleanUpList);
                resolved = fresh.Resolve(choices);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The scene could not be read: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (fresh.Message != null || resolved.Fingerprint() != planned.Fingerprint())
            {
                Replan();
                MessageBox.Show(this, "The scene has changed since the list was reviewed, so nothing was changed. Press Clean up scene graph\u2026 again to see the new list.",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PlannerCleanUpOutcome outcome;
            bool held = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (copy != null)
                {
                    var copyError = PlannerScheduleScene.SaveCopy(copy);
                    if (copyError != null)
                    {
                        Cursor = Cursors.Default;
                        MessageBox.Show(this, "Nothing was changed: the copy of the scene could not be saved as:\r\n" + PathLines(copy) + "\r\n\r\n" + copyError, title,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                if (hold)
                {
                    Loader.Core.FileHold();
                    held = true;
                }
                outcome = PlannerScheduleScene.ApplyCleanUp(fresh, resolved);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                Replan();
                MessageBox.Show(this, "The scene could not be cleaned up: " + ex.Message + (held ? "\r\n\r\nEdit > Fetch puts the scene back as it was before." : string.Empty),
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;
            // The scene as the batch left it. The layers added in Max follow the Max layers it shows renamed (one that kept
            // its name keeps its record as it is), with their names from before, so Edit > Undo takes the records back too.
            // The records are the scene's own, as the scene named their layers before the batch (MAXScript's Clean up may have
            // renamed them since the tab last read them).
            PlannerSceneSnapshot cleaned = null;
            PlannerCleanUpPlan after = null;
            try
            {
                cleaned = PlannerScheduleScene.Read();
                after = PlannerCleanUp.Plan(cleaned, wordFixes, cleanUpList);
            }
            catch (Exception)
            {
                // The report goes without the second reading, and the records stay as they are.
            }
            if (cleaned != null)
            {
                try
                {
                    var stored = PlannerScheduleScene.LoadAdded();
                    if (stored.Count > 0)
                    {
                        PlannerScheduleScene.SaveAdded(resolved.RenameAddedLayers(PlannerAddedLayerStore.ForScene(stored, new PlannerSceneModel(before)), new PlannerSceneModel(cleaned)));
                    }
                }
                catch (Exception)
                {
                    // The scene keeps its records as they were.
                }
            }
            // The scene is read again: on the new names it fills from the Planner's dates on the new engine.
            Replan();
            // The copy's lines come after the report is shortened, so they are always shown whole.
            var report = Shorten(outcome.Describe(resolved, after, held));
            if (copy != null)
            {
                report += "\r\nThe scene as it was before is saved as:\r\n" + PathLines(copy);
            }
            MessageBox.Show(this, report, title, MessageBoxButtons.OK, outcome.HasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // A file's name and its folder on lines of their own, the folder broken after a backslash where it is long, so a
        // message box does not break the path in the middle of a word (only a single folder name too long for a line still is).
        private static string PathLines(string path)
        {
            const int width = 60;
            try
            {
                var lines = new List<string>();
                var line = "in ";
                var parts = Path.GetDirectoryName(path).Split('\\');
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i] + (i < parts.Length - 1 ? "\\" : string.Empty);
                    if (line.Length > 3 && line.Length + part.Length > width)
                    {
                        lines.Add(line);
                        line = "   ";
                    }
                    line += part;
                }
                lines.Add(line);
                return Path.GetFileName(path) + "\r\n" + string.Join("\r\n", lines.ToArray());
            }
            catch (Exception)
            {
                return path;
            }
        }

        // Keeps the word fixes edited in Clean up's window with the scene: null when saved, else why not.
        private static string SaveWordFixes(List<PhasingWordFix> fixes)
        {
            try
            {
                PhasingScene.SaveWordFixes(fixes);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
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
