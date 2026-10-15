using System;
using System.Drawing;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the Add layer dialog of the Schedule tab. Asks for a phase group (its phase and name) or a layer (its
    /// phase, stage and sub-stage, its name, its dates or TBC) and shows, as it is typed, the name the convention gives it
    /// (<see cref="PlannerAddLayer.Compose"/>) or why it cannot be written. Built in code; modal over the exporter window.
    /// </summary>
    internal sealed class PlannerAddLayerForm : Form
    {
        private readonly RadioButton layerKind = new RadioButton();
        private readonly RadioButton groupKind = new RadioButton();
        private readonly CheckBox phaseBox = new CheckBox();
        private readonly NumericUpDown phase = new NumericUpDown();
        private readonly NumericUpDown stage = new NumericUpDown();
        private readonly CheckBox subBox = new CheckBox();
        private readonly NumericUpDown sub = new NumericUpDown();
        private readonly TextBox nameBox = new TextBox();
        private readonly CheckBox datesBox = new CheckBox();
        private readonly DateTimePicker start = new DateTimePicker();
        private readonly CheckBox finishBox = new CheckBox();
        private readonly DateTimePicker finish = new DateTimePicker();
        private readonly CheckBox tbcBox = new CheckBox();
        private readonly Label nameLabel = new Label();
        private readonly Button okButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Font boldFont;

        public PlannerAddLayerForm(string parentName, int? parentPhase)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = "Add layer";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            boldFont = new Font(Font, FontStyle.Bold);

            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(10),
                Dock = DockStyle.Fill
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Action<string, Control> row = (text, control) =>
            {
                var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 0) };
                table.Controls.Add(label);
                table.Controls.Add(control);
            };
            var under = new Label { Text = parentName, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0), Font = boldFont };
            row("Under", under);

            layerKind.Text = "Layer (holds work)";
            groupKind.Text = "Phase group (holds layers)";
            foreach (var radio in new[] { layerKind, groupKind })
            {
                radio.AutoSize = true;
                radio.CheckedChanged += (s, e) => UpdateName(true);
            }
            layerKind.Checked = true;
            var kinds = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            kinds.Controls.Add(layerKind);
            kinds.Controls.Add(groupKind);
            row("Kind", kinds);

            SetUpNumber(phase, 0, 99, parentPhase ?? 1);
            phaseBox.Text = "Phase";
            phaseBox.AutoSize = true;
            phaseBox.Checked = true;
            phaseBox.CheckedChanged += (s, e) => UpdateName(true);
            row("ID", Line(phaseBox, phase, new Label { Text = "Stage", AutoSize = true, Margin = new Padding(8, 6, 3, 0) }, stage, subBox, sub));
            SetUpNumber(stage, 0, 99, 1);
            SetUpNumber(sub, 1, 99, 1);
            subBox.Text = "Sub-stage";
            subBox.AutoSize = true;
            subBox.Margin = new Padding(8, 3, 3, 3);
            subBox.CheckedChanged += (s, e) => UpdateName(true);

            nameBox.Width = 320;
            nameBox.TextChanged += (s, e) => UpdateName(false);
            row("Name", nameBox);

            datesBox.Text = "Start";
            datesBox.AutoSize = true;
            datesBox.Checked = true;
            datesBox.CheckedChanged += (s, e) => UpdateName(true);
            finishBox.Text = "Finish";
            finishBox.AutoSize = true;
            finishBox.Checked = true;
            finishBox.Margin = new Padding(8, 3, 3, 3);
            finishBox.CheckedChanged += (s, e) => UpdateName(true);
            foreach (var picker in new[] { start, finish })
            {
                picker.Format = DateTimePickerFormat.Custom;
                picker.CustomFormat = "dd-MM-yyyy";
                picker.Width = 100;
                picker.Value = DateTime.Today;
                picker.ValueChanged += (s, e) => UpdateName(false);
            }
            tbcBox.Text = "TBC";
            tbcBox.AutoSize = true;
            tbcBox.Margin = new Padding(8, 3, 3, 3);
            tbcBox.CheckedChanged += (s, e) => UpdateName(false);
            row("Dates", Line(datesBox, start, finishBox, finish, tbcBox));

            nameLabel.AutoSize = true;
            nameLabel.MaximumSize = new Size(520, 0);
            nameLabel.Margin = new Padding(0, 10, 0, 6);
            row("Will be", nameLabel);

            okButton.Text = "Review\u2026";
            okButton.AutoSize = true;
            okButton.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            cancelButton.Text = "Cancel";
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            table.Controls.Add(new Label());
            table.Controls.Add(buttons);

            Controls.Add(table);
            AcceptButton = okButton;
            CancelButton = cancelButton;
            Activated += (s, e) => Loader.Global.DisableAccelerators();
            Deactivate += (s, e) => Loader.Global.EnableAccelerators();
            ResumeLayout(false);
            PerformLayout();
            UpdateName(true);
        }

        /// <summary>What was asked for.</summary>
        public PlannerNewLayer Spec
        {
            get
            {
                bool isLayer = layerKind.Checked;
                return new PlannerNewLayer
                {
                    Kind = isLayer ? PlannerLayerKind.Layer : PlannerLayerKind.Group,
                    Phase = phaseBox.Checked ? (int)phase.Value : (int?)null,
                    Stage = (int)stage.Value,
                    SubStage = isLayer && subBox.Checked ? (int)sub.Value : (int?)null,
                    Name = nameBox.Text,
                    Start = isLayer && datesBox.Checked ? start.Value.Date : (DateTime?)null,
                    Finish = isLayer && datesBox.Checked && finishBox.Checked ? finish.Value.Date : (DateTime?)null,
                    Tbc = isLayer && tbcBox.Checked
                };
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                boldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private static void SetUpNumber(NumericUpDown number, int min, int max, int value)
        {
            number.Minimum = min;
            number.Maximum = max;
            number.Value = Math.Max(min, Math.Min(max, value));
            number.Width = 48;
        }

        private static FlowLayoutPanel Line(params Control[] controls)
        {
            var line = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
            foreach (var control in controls)
            {
                line.Controls.Add(control);
            }
            return line;
        }

        private void UpdateName(bool fields)
        {
            bool isLayer = layerKind.Checked;
            if (fields)
            {
                phaseBox.Text = isLayer ? "Phase" : "Phase group";
                // A group always has its phase: the Planner makes folders only for phase groups (_<nn>_<name>).
                if (!isLayer && !phaseBox.Checked)
                {
                    phaseBox.Checked = true;
                    return;
                }
                phaseBox.Enabled = isLayer;
                phase.Enabled = phaseBox.Checked;
                stage.Enabled = isLayer;
                subBox.Enabled = isLayer && phaseBox.Checked;
                sub.Enabled = subBox.Enabled && subBox.Checked;
                datesBox.Enabled = isLayer;
                start.Enabled = isLayer && datesBox.Checked;
                finishBox.Enabled = isLayer && datesBox.Checked;
                finish.Enabled = finishBox.Enabled && finishBox.Checked;
                tbcBox.Enabled = isLayer;
            }
            string problem;
            var name = PlannerAddLayer.Compose(Spec, out problem);
            nameLabel.Text = name ?? "Cannot be written yet: " + problem + ".";
            nameLabel.Font = name != null ? boldFont : Font;
            nameLabel.ForeColor = name != null ? SystemColors.ControlText : SystemColors.GrayText;
            okButton.Enabled = name != null;
        }
    }
}
