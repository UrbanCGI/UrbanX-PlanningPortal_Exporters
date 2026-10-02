using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the dry run of a Planner layers plan. Shows the review list (what needs a look) and every
    /// change the plan would make, and applies nothing unless the modeller presses Apply. Built in code, without
    /// a designer file.
    /// </summary>
    sealed class PlannerReviewDialog : Form
    {
        private readonly PlannerScenePlan plan;
        private readonly CheckBox holdCheck;

        /// <summary>True when the scene should be held (Edit &gt; Hold) before the changes are applied.</summary>
        public bool HoldFirst { get { return holdCheck.Checked; } }

        public PlannerReviewDialog(string title, string intro, PlannerScenePlan plan)
        {
            this.plan = plan;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(980, 600);
            MinimumSize = new Size(640, 400);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var introLabel = new Label { Text = intro, AutoSize = true, MaximumSize = new Size(950, 0), Margin = new Padding(3, 3, 3, 6) };
            var summaryLabel = new Label { Text = plan.Summary(), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 0, 3, 6) };

            var reviewList = NewList();
            reviewList.Columns.Add("Type", 80);
            reviewList.Columns.Add("About", 280);
            reviewList.Columns.Add("Message", 900);
            reviewList.BeginUpdate();
            foreach (var item in PlannerLayersActions.Ordered(plan.Review))
            {
                var row = new ListViewItem(new[] { PlannerLayersActions.SeverityText(item.Severity), item.Subject ?? string.Empty, item.Message });
                if (item.Severity == PlannerReviewSeverity.Error)
                {
                    row.ForeColor = Color.Firebrick;
                }
                else if (item.Severity == PlannerReviewSeverity.Warning)
                {
                    row.ForeColor = Color.DarkOrange;
                }
                else if (item.Severity == PlannerReviewSeverity.Info)
                {
                    row.ForeColor = Color.DimGray;
                }
                reviewList.Items.Add(row);
            }
            reviewList.EndUpdate();

            var changesList = NewList();
            changesList.Columns.Add("#", 50);
            changesList.Columns.Add("Change", 1100);
            changesList.BeginUpdate();
            for (int i = 0; i < plan.Operations.Count; i++)
            {
                changesList.Items.Add(new ListViewItem(new[] { (i + 1).ToString(CultureInfo.InvariantCulture), plan.Operations[i].Description }));
            }
            changesList.EndUpdate();

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var reviewPage = new TabPage(string.Format(CultureInfo.InvariantCulture, "Review: {0} error(s), {1} warning(s), {2} note(s)",
                plan.Count(PlannerReviewSeverity.Error), plan.Count(PlannerReviewSeverity.Warning), plan.Count(PlannerReviewSeverity.Note)));
            reviewPage.Controls.Add(reviewList);
            var changesPage = new TabPage(string.Format(CultureInfo.InvariantCulture, "Changes ({0})", plan.Operations.Count));
            changesPage.Controls.Add(changesList);
            tabs.TabPages.Add(reviewPage);
            tabs.TabPages.Add(changesPage);

            holdCheck = new CheckBox
            {
                Text = "Hold the scene first (Edit > Fetch puts it back as it was)",
                Checked = true,
                AutoSize = true,
                Enabled = plan.HasChanges,
                Margin = new Padding(3, 6, 3, 3)
            };

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            var cancel = new Button { Text = plan.HasChanges ? "Cancel" : "Close", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.Flat };
            var apply = new Button
            {
                Text = string.Format(CultureInfo.InvariantCulture, "Apply {0} change(s)", plan.Operations.Count),
                DialogResult = DialogResult.OK,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Enabled = plan.HasChanges
            };
            var copy = new Button { Text = "Copy list", AutoSize = true, FlatStyle = FlatStyle.Flat };
            copy.Click += (sender, e) =>
            {
                try
                {
                    Clipboard.SetText(PlannerLayersActions.ReviewText(this.plan));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The list could not be copied: " + ex.Message, Text);
                }
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            buttons.Controls.Add(copy);
            AcceptButton = plan.HasChanges ? apply : cancel;
            CancelButton = cancel;

            layout.Controls.Add(introLabel, 0, 0);
            layout.Controls.Add(summaryLabel, 0, 1);
            layout.Controls.Add(tabs, 0, 2);
            layout.Controls.Add(holdCheck, 0, 3);
            layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout);

            // Keep 3ds Max's keyboard shortcuts away while this window has the focus, as the exporter's windows do.
            Activated += (sender, e) => Loader.Global.DisableAccelerators();
            Deactivate += (sender, e) => Loader.Global.EnableAccelerators();
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
