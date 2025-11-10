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
        private NumericUpDown rescanNumeric;

        public SettingsForm(SettingsModel settings)
        {
            this.Settings = settings;
            this.Text = "Settings";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 320;
            this.Height = 200;

            var colorLabel = new Label { Text = "Fill color (hex):", Left = 10, Top = 20 };
            colorBox = new TextBox { Left = 120, Top = 18, Width = 150, Text = ColorTranslator.ToHtml(settings.FillColor ?? Color.Blue) };
            var colorBtn = new Button { Text = "Pick", Left = 10, Top = 50, Width = 60 };
            colorBtn.Click += (s, e) =>
            {
                using var dlg = new ColorDialog();
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    colorBox.Text = ColorTranslator.ToHtml(dlg.Color);
                }
            };

            autostartBox = new CheckBox { Text = "Start with Windows", Left = 10, Top = 85, Checked = settings.Autostart };
            var rescanLabel = new Label { Text = "Rescan threshold:", Left = 10, Top = 115 };
            rescanNumeric = new NumericUpDown { Left = 120, Top = 112, Minimum = 1, Maximum = 1000, Value = settings.RescanThreshold };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 120, Top = 140 };
            ok.Click += Ok_Click;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 200, Top = 140 };

            this.Controls.AddRange(new Control[] { colorLabel, colorBox, colorBtn, autostartBox, rescanLabel, rescanNumeric, ok, cancel });
        }

        private void Ok_Click(object? sender, EventArgs e)
        {
            try
            {
                var c = ColorTranslator.FromHtml(colorBox.Text);
                Settings.FillColor = c;
                Settings.Autostart = autostartBox.Checked;
                Settings.RescanThreshold = (int)rescanNumeric.Value;
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