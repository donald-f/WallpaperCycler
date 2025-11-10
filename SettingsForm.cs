using System;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperCycler
{
    public class SettingsForm : Form
    {
        public SettingsModel Settings { get; private set; }
        private TextBox colorBox;
        private CheckBox autostartBox;
        private ComboBox cycleCombo;

        public SettingsForm(SettingsModel settings)
        {
            this.Settings = settings;
            this.Text = "Settings";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 360;
            this.Height = 220;

            var colorLabel = new Label { Text = "Fill color (hex):", Left = 10, Top = 20 };
            colorBox = new TextBox { Left = 140, Top = 18, Width = 170, Text = ColorTranslator.ToHtml(settings.FillColor ?? Color.Blue) };
            var colorBtn = new Button { Text = "Pick", Left = 320, Top = 16, Width = 24 };
            colorBtn.Click += (s, e) =>
            {
                using var dlg = new ColorDialog();
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    colorBox.Text = ColorTranslator.ToHtml(dlg.Color);
                }
            };

            autostartBox = new CheckBox { Text = "Start with Windows", Left = 10, Top = 60, Checked = settings.Autostart };
            var cycleLabel = new Label { Text = "Cycle interval:", Left = 10, Top = 95 };
            cycleCombo = new ComboBox { Left = 140, Top = 92, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
            cycleCombo.Items.AddRange(new object[] { "Off", "10 minutes", "20 minutes", "30 minutes", "60 minutes" });
            cycleCombo.SelectedIndex = IndexForMinutes(settings.CycleMinutes);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 140, Top = 140 };
            ok.Click += Ok_Click;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 220, Top = 140 };

            this.Controls.AddRange(new Control[] { colorLabel, colorBox, colorBtn, autostartBox, cycleLabel, cycleCombo, ok, cancel });
        }

        private int IndexForMinutes(int minutes)
        {
            return minutes switch
            {
                10 => 1,
                20 => 2,
                30 => 3,
                60 => 4,
                _ => 0
            };
        }

        private int MinutesForIndex(int idx)
        {
            return idx switch
            {
                1 => 10,
                2 => 20,
                3 => 30,
                4 => 60,
                _ => 0
            };
        }

        private void Ok_Click(object? sender, EventArgs e)
        {
            try
            {   var c = ColorTranslator.FromHtml(colorBox.Text);
                Settings.FillColor = c;
                Settings.Autostart = autostartBox.Checked;
                Settings.CycleMinutes = MinutesForIndex(cycleCombo.SelectedIndex);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch
            {
                MessageBox.Show("Enter a valid hex color like #112233");
            }
        }
    }
}