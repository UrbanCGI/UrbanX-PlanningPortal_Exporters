using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the "Planner layers" window. Read-only view of the Work_Phasing layers with the dates of
    /// the last loaded Planner schedule, plus the actions that keep the layers in step with the Planner: load
    /// a schedule, update the layers from it, adopt old dated layers. The Planner owns hierarchy, names, codes,
    /// dates and TBC; nothing here edits them. Modeless and parented to the 3ds Max window like the exporter's
    /// other windows; built in code, without a designer file.
    /// </summary>
    sealed class PlannerLayersForm : Form
    {
        private static PlannerLayersForm instance;
        private static NativeWindow parentWindow;

        private readonly Label projectLabel = new Label { AutoSize = true, Font = new Font(Control.DefaultFont, FontStyle.Bold) };
        private readonly Label exportedLabel = new Label { AutoSize = true };
        private readonly Label zoneLabel = new Label { AutoSize = true };
        private readonly Label sourceLabel = new Label { AutoSize = true, ForeColor = Color.DimGray };
        private readonly ListView layersList = NewList();
        private readonly ListView objectsList = NewList();
        private readonly Label objectsTitle = new Label { AutoSize = true, Font = new Font(Control.DefaultFont, FontStyle.Bold), Margin = new Padding(3, 4, 3, 4) };
        private readonly Label statusLabel = new Label { AutoSize = true, Margin = new Padding(3, 4, 3, 2) };

        private readonly ToolTip tips = new ToolTip();
        private Font boldFont;

        private PlannerLoadedSchedule loaded;
        private PlannerPanelModel model;
        private bool objectsPending;
        /// <summary>The lower list shows the objects outside the Planner layers until a layer is selected again.</summary>
        private bool showingOutside;
        /// <summary>Set while the code itself changes the layer selection, so the change is not taken as the modeller's.</summary>
        private bool suppressSelection;

        private GlobalDelegates.Delegate5 sceneChangedDelegate;
        private static readonly SystemNotificationCode[] SceneChangeCodes =
        {
            SystemNotificationCode.FilePostOpen, SystemNotificationCode.SystemPostReset, SystemNotificationCode.SystemPostNew
        };

        /// <summary>Opens the window (or brings it to the front), parented to the 3ds Max window.</summary>
        public static void ShowWindow()
        {
            if (Loader.Class_ID == null)
            {
                Loader.AssemblyMain();
            }
            if (instance == null || instance.IsDisposed)
            {
                instance = new PlannerLayersForm();
            }
            if (parentWindow == null)
            {
                parentWindow = new NativeWindow();
            }
            if (parentWindow.Handle == IntPtr.Zero)
            {
                parentWindow.AssignHandle(Loader.Core.MAXHWnd);
            }
            if (!instance.Visible)
            {
                instance.Show(parentWindow);
            }
            instance.WindowState = FormWindowState.Normal;
            instance.BringToFront();
        }

        private PlannerLayersForm()
        {
            Text = "Planner Exporters - Planner layers";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(760, 480);

            var header = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            header.Controls.AddRange(new Control[] { projectLabel, exportedLabel, zoneLabel, sourceLabel });

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
            buttons.Controls.Add(NewButton("Load schedule...", "Read the schedule file the Planner exports (<project>.planner-schedule.json) and keep it with this scene.", LoadSchedule_Click));
            buttons.Controls.Add(NewButton("Update layers", "Match the Work_Phasing layers to the loaded schedule: create, rename and nest layers and helpers, link objects. Shows the list first.", UpdateLayers_Click));
            buttons.Controls.Add(NewButton("Adopt existing layers...", "One-off: turn the old dated layers into coded layers under Work_Phasing, with their helpers. Shows the list first.", AdoptLayers_Click));
            buttons.Controls.Add(NewButton("Refresh", "Read the scene's layers again.", (sender, e) => RefreshScene()));
            buttons.Controls.Add(NewButton("Select objects", "Select the listed objects in 3ds Max (only the highlighted ones, when some are highlighted).", SelectObjects_Click));
            buttons.Controls.Add(NewButton("Show objects outside Planner layers", "List the objects outside Work_Phasing: context, which is not phased.", ShowOutside_Click));

            layersList.Columns.Add("Code", 90);
            layersList.Columns.Add("Name", 330);
            layersList.Columns.Add("Start", 110);
            layersList.Columns.Add("Finish", 110);
            layersList.Columns.Add("TBC", 45);
            layersList.Columns.Add("Objects", 60, HorizontalAlignment.Right);
            layersList.Columns.Add("Dates from", 100);
            layersList.Columns.Add("Note", 400);
            layersList.SelectedIndexChanged += (sender, e) =>
            {
                if (suppressSelection)
                {
                    return;
                }
                if (layersList.SelectedItems.Count > 0)
                {
                    showingOutside = false;
                }
                QueueObjects();
            };

            objectsList.Columns.Add("Object", 280);
            objectsList.Columns.Add("Type", 70);
            objectsList.Columns.Add("Tag", 70);
            objectsList.Columns.Add("Removed in", 80);
            objectsList.Columns.Add("Activity", 240);
            objectsList.Columns.Add("Start", 110);
            objectsList.Columns.Add("Finish", 110);
            objectsList.Columns.Add("TBC", 45);
            objectsList.Columns.Add("Linked to", 220);
            objectsList.Columns.Add("Layer", 220);
            objectsList.Columns.Add("Note", 400);

            var objectsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            objectsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            objectsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            objectsPanel.Controls.Add(objectsTitle, 0, 0);
            objectsPanel.Controls.Add(objectsList, 0, 1);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(layersList);
            split.Panel2.Controls.Add(objectsPanel);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.Controls.Add(split, 0, 2);
            layout.Controls.Add(statusLabel, 0, 3);
            Controls.Add(layout);

            Load += (sender, e) =>
            {
                try
                {
                    split.SplitterDistance = Math.Max(120, split.Height / 2);
                }
                catch (InvalidOperationException)
                {
                    // A window too small for the split keeps the default.
                }
                ReloadAll();
            };
            Activated += (sender, e) => Loader.Global.DisableAccelerators();
            Deactivate += (sender, e) => Loader.Global.EnableAccelerators();
            FormClosed += (sender, e) =>
            {
                Loader.Global.EnableAccelerators();
                UnregisterSceneChanges();
                tips.Dispose();
                if (boldFont != null)
                {
                    boldFont.Dispose();
                }
            };
            RegisterSceneChanges();
        }

        // ---- scene changes (open, reset, new) ----------------------------------------------------------------------

        private void RegisterSceneChanges()
        {
            sceneChangedDelegate = new GlobalDelegates.Delegate5(OnSceneChanged);
            foreach (var code in SceneChangeCodes)
            {
                GlobalInterface.Instance.RegisterNotification(sceneChangedDelegate, null, code);
            }
        }

        private void UnregisterSceneChanges()
        {
            if (sceneChangedDelegate == null)
            {
                return;
            }
            foreach (var code in SceneChangeCodes)
            {
                GlobalInterface.Instance.UnRegisterNotification(sceneChangedDelegate, null, code);
            }
            sceneChangedDelegate = null;
        }

        private void OnSceneChanged(IntPtr param0, IntPtr param1)
        {
            OnSceneChanged();
        }

        private void OnSceneChanged(IntPtr param0, INotifyInfo param1)
        {
            OnSceneChanged();
        }

        private void OnSceneChanged()
        {
            if (IsDisposed)
            {
                return;
            }
            Guard(ReloadAll);
        }

        // ---- reading -------------------------------------------------------------------------------------------

        /// <summary>Reads the schedule stored in the scene and the scene's layers.</summary>
        private void ReloadAll()
        {
            loaded = PlannerLayersActions.Stored();
            RefreshScene();
        }

        private PlannerSchedule Schedule
        {
            get { return loaded != null ? loaded.Schedule : null; }
        }

        private void RefreshScene()
        {
            Guard(() =>
            {
                var selected = new HashSet<string>(SelectedRows().Select(r => r.LayerName), PlannerScene.LayerNameComparer);
                model = PlannerPanelModel.Build(PlannerMaxScene.Read(), Schedule);
                ShowHeader();
                ShowLayers(selected);
                ShowObjects();
            });
        }

        private void ShowHeader()
        {
            var lines = PlannerPanelModel.HeaderLines(Schedule, TimeZoneInfo.Local);
            projectLabel.Text = lines[0];
            exportedLabel.Text = lines.Length > 1 ? lines[1] : string.Empty;
            zoneLabel.Text = lines.Length > 2 ? lines[2] : string.Empty;
            exportedLabel.Visible = exportedLabel.Text.Length > 0;
            zoneLabel.Visible = zoneLabel.Text.Length > 0;
            if (loaded != null && loaded.Schedule == null)
            {
                projectLabel.Text = "The schedule kept with this scene could not be read: " + string.Join(" ", loaded.Errors.ToArray());
            }
            var source = loaded != null && loaded.SourcePath != null ? "Loaded from " + loaded.SourcePath : string.Empty;
            if (loaded != null && loaded.LoadedAt != null)
            {
                DateTime at;
                source += (source.Length > 0 ? ", " : "Loaded ")
                          + (DateTime.TryParseExact(loaded.LoadedAt, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out at)
                              ? at.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                              : loaded.LoadedAt);
            }
            sourceLabel.Text = source;
            sourceLabel.Visible = source.Length > 0;
        }

        private void ShowLayers(HashSet<string> keepSelected)
        {
            suppressSelection = true;
            try
            {
                FillLayers(keepSelected);
            }
            finally
            {
                suppressSelection = false;
            }

            if (model.RootLayerName == null)
            {
                SetStatus("This scene has no Work_Phasing layer yet. Use Adopt existing layers to convert the old dated layers, or Update layers to build the layers from a Planner schedule.");
            }
            else
            {
                SetStatus(string.Format(CultureInfo.InvariantCulture, "{0} Planner layer(s) under '{1}'. Select layers to list their objects.", model.Rows.Count - 1, model.RootLayerName));
            }
        }

        private void FillLayers(HashSet<string> keepSelected)
        {
            layersList.BeginUpdate();
            layersList.Items.Clear();
            foreach (var row in model.Rows)
            {
                var item = new ListViewItem(new[]
                {
                    row.Code,
                    new string(' ', 4 * row.Depth) + (row.IsRoot ? row.LayerName : row.Name),
                    row.Start,
                    row.Finish,
                    row.Tbc ? "TBC" : string.Empty,
                    row.ObjectCount.ToString(CultureInfo.InvariantCulture),
                    row.DatesFrom,
                    row.Note
                })
                {
                    Tag = row,
                    ToolTipText = row.LayerName
                };
                if (row.IsRoot)
                {
                    item.Font = boldFont ?? (boldFont = new Font(layersList.Font, FontStyle.Bold));
                }
                if (row.Note.Length > 0)
                {
                    item.ForeColor = Color.DarkOrange;
                }
                layersList.Items.Add(item);
                item.Selected = keepSelected.Contains(row.LayerName);
            }
            layersList.ShowItemToolTips = true;
            layersList.EndUpdate();
        }

        private IEnumerable<PlannerPanelRow> SelectedRows()
        {
            return layersList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as PlannerPanelRow).Where(r => r != null);
        }

        /// <summary>Selection changes arrive one row at a time; list the objects once they have settled.</summary>
        private void QueueObjects()
        {
            if (objectsPending || !IsHandleCreated)
            {
                return;
            }
            objectsPending = true;
            BeginInvoke((Action)(() =>
            {
                objectsPending = false;
                Guard(ShowObjects);
            }));
        }

        private void ShowObjects()
        {
            if (model == null)
            {
                return;
            }
            if (showingOutside)
            {
                ShowOutsideObjects();
                return;
            }
            var rows = SelectedRows().ToList();
            if (rows.Count == 0)
            {
                objectsTitle.Text = "Objects: select one or more layers above";
                FillObjects(new List<PlannerPanelObject>());
                return;
            }
            objectsTitle.Text = rows.Count == 1
                ? "Objects on " + rows[0].LayerName
                : string.Format(CultureInfo.InvariantCulture, "Objects on {0} layers", rows.Count);
            FillObjects(model.ObjectsOn(rows.Select(r => r.LayerName)));
        }

        private void FillObjects(List<PlannerPanelObject> objects)
        {
            objectsList.BeginUpdate();
            objectsList.Items.Clear();
            foreach (var item in objects)
            {
                var activities = item.Activities.Count > 0 ? item.Activities : new List<PlannerPanelActivity> { null };
                foreach (var activity in activities)
                {
                    var row = new ListViewItem(new[]
                    {
                        item.Name,
                        (activity != null ? activity.Type : item.Type) == PlannerTaskType.Dismantle ? "Dismantle" : "Install",
                        item.TagCode,
                        item.RemovalCode,
                        activity != null ? activity.Name : string.Empty,
                        activity != null ? activity.Start : string.Empty,
                        activity != null ? activity.Finish : string.Empty,
                        activity != null && activity.Tbc ? "TBC" : string.Empty,
                        item.LinkedTo,
                        item.LayerName,
                        item.Note
                    })
                    {
                        Tag = item
                    };
                    if (item.Note.Length > 0)
                    {
                        row.ForeColor = item.FiledUnderOwnLayer ? Color.DarkOrange : Color.Firebrick;
                    }
                    objectsList.Items.Add(row);
                }
            }
            objectsList.EndUpdate();
        }

        // ---- actions -------------------------------------------------------------------------------------------

        private void LoadSchedule_Click(object sender, EventArgs e)
        {
            Guard(() =>
            {
                string path;
                using (var dialog = new OpenFileDialog
                {
                    Title = "Load a Planner schedule",
                    Filter = "Planner schedule (*.planner-schedule.json)|*.planner-schedule.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                    CheckFileExists = true
                })
                {
                    if (loaded != null && loaded.SourcePath != null)
                    {
                        try
                        {
                            var folder = Path.GetDirectoryName(loaded.SourcePath);
                            if (Directory.Exists(folder))
                            {
                                dialog.InitialDirectory = folder;
                            }
                        }
                        catch (Exception)
                        {
                            // A stored path that no longer parses just leaves the dialog where it opens.
                        }
                    }
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    path = dialog.FileName;
                }

                var read = PlannerLayersActions.ReadFile(path);
                if (read.Schedule == null)
                {
                    MessageBox.Show(this, "This schedule cannot be used:\r\n\r\n" + string.Join("\r\n", read.Errors.ToArray()), "Load schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var previous = Schedule;
                if (previous != null && previous.ProjectId != null && read.Schedule.ProjectId != null && previous.ProjectId != read.Schedule.ProjectId)
                {
                    var answer = MessageBox.Show(this,
                        string.Format("This schedule is for the Planner project '{0}', but the schedule kept with this scene is for '{1}'. Load it anyway?",
                            read.Schedule.ProjectName ?? read.Schedule.ProjectId, previous.ProjectName ?? previous.ProjectId),
                        "Load schedule", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }
                }

                PlannerLayersActions.Store(read);
                var stored = PlannerLayersActions.Stored();
                loaded = stored != null && stored.Schedule != null ? stored : read;
                RefreshScene();

                var message = string.Format(CultureInfo.InvariantCulture, "Loaded {0} folder(s) and {1} activity(ies) for {2}.",
                    read.Schedule.Folders.Count, read.Schedule.Activities.Count, read.Schedule.ProjectName ?? "the project");
                if (stored == null || stored.Schedule == null)
                {
                    message += "\r\n\r\nThe schedule could not be kept with the scene, so it will need loading again after the scene is reopened.";
                }
                if (read.Warnings.Count > 0)
                {
                    message += string.Format(CultureInfo.InvariantCulture, "\r\n\r\n{0} thing(s) in the file were skipped or could not be read:\r\n", read.Warnings.Count)
                               + string.Join("\r\n", read.Warnings.Take(10).ToArray())
                               + (read.Warnings.Count > 10 ? "\r\n..." : string.Empty);
                }
                message += "\r\n\r\nUpdate the layers from it now? The list of changes is shown first.";
                if (MessageBox.Show(this, message, "Load schedule", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    RunUpdate();
                }
            });
        }

        private void UpdateLayers_Click(object sender, EventArgs e)
        {
            Guard(RunUpdate);
        }

        private void RunUpdate()
        {
            var schedule = Schedule;
            if (schedule == null)
            {
                MessageBox.Show(this, "Load a Planner schedule first (Load schedule...).", "Update layers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var plan = PlannerLayersActions.PlanUpdate(schedule);
            Review("Update layers",
                "These are the changes that bring the Work_Phasing layers in line with the Planner schedule, and what needs a look. Nothing has changed yet. Layers are never deleted: layers the schedule no longer has are listed so they can be tidied by hand.",
                plan, () => PlannerLayersActions.PlanUpdate(schedule));
        }

        private void AdoptLayers_Click(object sender, EventArgs e)
        {
            Guard(() =>
            {
                var plan = PlannerLayersActions.PlanAdopt();
                Review("Adopt existing layers",
                    "This turns the old dated layers into coded layers under Work_Phasing: each layer is renamed <code>_<name>, gets a helper of the same name, and its loose objects are linked to that helper. The dates in the old names are kept on the helpers until a Planner schedule is applied. Nothing has changed yet, and nothing is deleted.",
                    plan, PlannerLayersActions.PlanAdopt);
            });
        }

        private void Review(string title, string intro, PlannerScenePlan plan, Func<PlannerScenePlan> replan)
        {
            bool hold;
            using (var dialog = new PlannerReviewDialog(title, intro, plan))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                hold = dialog.HoldFirst;
            }
            Cursor = Cursors.WaitCursor;
            string result;
            bool succeeded;
            try
            {
                result = PlannerLayersActions.Apply(plan, hold, replan, out succeeded);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            RefreshScene();
            MessageBox.Show(this, result, title, MessageBoxButtons.OK, succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void SelectObjects_Click(object sender, EventArgs e)
        {
            Guard(() =>
            {
                var items = objectsList.SelectedItems.Count > 0 ? objectsList.SelectedItems.Cast<ListViewItem>() : objectsList.Items.Cast<ListViewItem>();
                var ids = items.Select(i => i.Tag as PlannerPanelObject).Where(o => o != null).Select(o => o.NodeId).ToList();
                if (ids.Count == 0)
                {
                    SetStatus("No objects listed: select layers above, or show the objects outside the Planner layers.");
                    return;
                }
                var count = PlannerMaxScene.Select(ids);
                SetStatus(string.Format(CultureInfo.InvariantCulture, "Selected {0} object(s) in 3ds Max.", count));
            });
        }

        private void ShowOutside_Click(object sender, EventArgs e)
        {
            Guard(() =>
            {
                showingOutside = true;
                suppressSelection = true;
                try
                {
                    layersList.SelectedItems.Clear();
                }
                finally
                {
                    suppressSelection = false;
                }
                ShowObjects();
            });
        }

        private void ShowOutsideObjects()
        {
            var outside = model.ObjectsOutside();
            objectsTitle.Text = model.RootLayerName != null
                ? string.Format(CultureInfo.InvariantCulture, "Objects outside '{0}' (context, not phased): {1}", model.RootLayerName, outside.Count)
                : string.Format(CultureInfo.InvariantCulture, "Objects (the scene has no Work_Phasing layer, so none are phased): {0}", outside.Count);
            FillObjects(outside);
            var tagged = outside.Count(o => o.Tagged);
            SetStatus(tagged > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0} of them carry a phasing tag and are listed first: they were probably meant to sit on a Planner layer.", tagged)
                : "None of them carry a phasing tag.");
        }

        // ---- helpers -------------------------------------------------------------------------------------------

        private void SetStatus(string text)
        {
            statusLabel.Text = text;
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Something went wrong in the Planner layers window:\r\n\r\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Button NewButton(string text, string tip, EventHandler click)
        {
            var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 6, 0) };
            button.Click += click;
            tips.SetToolTip(button, tip);
            return button;
        }

        private static ListView NewList()
        {
            return new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true
            };
        }
    }
}
