using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the Add layer dialog of the Schedule tab. It asks where the layer goes (Under: the phasing root or a
    /// phase group, <see cref="PlannerAddLayer.Parents"/>, starting on the row chosen in the tab, <see
    /// cref="PlannerAddLayer.StartUnder"/>, with a note when that is not the row itself), a layer's ID (phase, stage and a
    /// sub-stage if it has one) and name, or a phase group's phase and name, and shows, as it is typed, the name the
    /// convention gives it (<see cref="PlannerAddLayer.Compose"/>: &lt;ID&gt;_&lt;Name&gt;, a group _&lt;nn&gt;_&lt;Name&gt;) or why it
    /// cannot be written. It asks for no date: every date comes from the Planner's layer list (D20). Built in code; modal
    /// over the exporter window.
    /// </summary>
    internal sealed class PlannerAddLayerForm : Form
    {
        private const string RootText = "the phasing root";

        private readonly ComboBox underBox = new ComboBox();
        private readonly Label underNote = new Label();
        // The Max layer of each entry of Under; the phasing root's is null.
        private readonly List<string> underLayers = new List<string>();
        private readonly RadioButton layerKind = new RadioButton();
        private readonly RadioButton groupKind = new RadioButton();
        private readonly Label phaseLabel = new Label();
        private readonly NumericUpDown phase = new NumericUpDown();
        private readonly Label stageLabel = new Label();
        private readonly NumericUpDown stage = new NumericUpDown();
        private readonly CheckBox subBox = new CheckBox();
        private readonly NumericUpDown sub = new NumericUpDown();
        private readonly TextBox nameBox = new TextBox();
        private readonly Label nameLabel = new Label();
        private readonly Button okButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Font boldFont;

        /// <param name="parents">The phase groups it can go under besides the phasing root, in the tree's order.</param>
        /// <param name="start">The one Under starts on, or null for the phasing root.</param>
        /// <param name="note">Why Under starts there and not on the row chosen, or null.</param>
        public PlannerAddLayerForm(IList<PlannerAddLayerParent> parents, string start, string note)
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

            // Under: the phasing root and every phase group that can take one, indented as they nest. It starts on the row
            // chosen in the tab (with a note when it cannot), and the phase follows the group chosen here.
            underBox.DropDownStyle = ComboBoxStyle.DropDownList;
            underBox.Width = 320;
            underBox.Margin = new Padding(0, 3, 0, 3);
            underBox.Items.Add(RootText);
            underLayers.Add(null);
            foreach (var parent in parents ?? new List<PlannerAddLayerParent>())
            {
                underBox.Items.Add(new string(' ', 4 * parent.Depth) + parent.Layer);
                underLayers.Add(parent.Layer);
            }
            int startIndex = start != null ? underLayers.FindIndex(l => l != null && string.Equals(l, start, StringComparison.OrdinalIgnoreCase)) : 0;
            underBox.SelectedIndex = Math.Max(0, startIndex);
            underBox.SelectedIndexChanged += (s, e) =>
            {
                // The note was about where Under started.
                underNote.Visible = false;
                int? groupPhase = PhaseOf(UnderLayer);
                if (groupPhase.HasValue)
                {
                    phase.Value = Math.Max(phase.Minimum, Math.Min(phase.Maximum, groupPhase.Value));
                }
            };
            // The list opens as wide as its longest entry.
            underBox.DropDown += (s, e) =>
            {
                int widest = underBox.Width;
                foreach (var item in underBox.Items)
                {
                    widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), underBox.Font).Width + SystemInformation.VerticalScrollBarWidth + 8);
                }
                underBox.DropDownWidth = widest;
            };
            row("Under", underBox);
            if (note != null)
            {
                underNote.Text = note;
                underNote.AutoSize = true;
                underNote.MaximumSize = new Size(520, 0);
                underNote.Margin = new Padding(0, 0, 0, 6);
                underNote.ForeColor = SystemColors.GrayText;
                table.Controls.Add(new Label { AutoSize = true });
                table.Controls.Add(underNote);
            }

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

            // The ID: a layer's phase is always given (one number is no layer on the new names), its stage, and a sub-stage
            // when ticked; a group has its phase only. It starts on the phase of the group Under starts on.
            SetUpNumber(phase, 0, 99, PhaseOf(UnderLayer) ?? 1);
            SetUpNumber(stage, 0, 99, 1);
            SetUpNumber(sub, 1, 99, 1);
            phase.ValueChanged += (s, e) => UpdateName(false);
            stage.ValueChanged += (s, e) => UpdateName(false);
            sub.ValueChanged += (s, e) => UpdateName(false);
            phaseLabel.AutoSize = true;
            phaseLabel.Margin = new Padding(0, 6, 3, 0);
            stageLabel.Text = "Stage";
            stageLabel.AutoSize = true;
            stageLabel.Margin = new Padding(8, 6, 3, 0);
            subBox.Text = "Sub-stage";
            subBox.AutoSize = true;
            subBox.Margin = new Padding(8, 3, 3, 3);
            subBox.CheckedChanged += (s, e) => UpdateName(true);
            row("ID", Line(phaseLabel, phase, stageLabel, stage, subBox, sub));

            nameBox.Width = 320;
            nameBox.TextChanged += (s, e) => UpdateName(false);
            row("Name", nameBox);

            var dates = new Label
            {
                Text = "None set here: the Planner gives every date, and the layer shows TBC until the Planner's layer list has it.",
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Margin = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText
            };
            row("Dates", dates);

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

        /// <summary>The Max layer chosen in Under, or null for the phasing root.</summary>
        public string UnderLayer
        {
            get { return underBox.SelectedIndex > 0 && underBox.SelectedIndex < underLayers.Count ? underLayers[underBox.SelectedIndex] : null; }
        }

        /// <summary>What was asked for: the ID and the name.</summary>
        public PlannerNewLayer Spec
        {
            get
            {
                bool isLayer = layerKind.Checked;
                return new PlannerNewLayer
                {
                    Kind = isLayer ? PlannerLayerKind.Layer : PlannerLayerKind.Group,
                    Phase = (int)phase.Value,
                    Stage = (int)stage.Value,
                    SubStage = isLayer && subBox.Checked ? (int)sub.Value : (int?)null,
                    Name = nameBox.Text
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

        // The phase number of a phase group's name, or null (the phasing root, or a name that has none).
        private static int? PhaseOf(string layer)
        {
            int number;
            return layer != null && PhasingNameFixer.TryReadPhaseGroup(layer, out number) ? number : (int?)null;
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
                phaseLabel.Text = isLayer ? "Phase" : "Phase group";
                stage.Enabled = isLayer;
                subBox.Enabled = isLayer;
                sub.Enabled = isLayer && subBox.Checked;
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
