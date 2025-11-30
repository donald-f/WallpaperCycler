using System;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperCycler
{
    public class SettingsForm : Form
    {
        public SettingsModel Settings { get; private set; }
        private TextBox colorBox;
        //private CheckBox autostartBox;
        private ComboBox cycleCombo;

        public SettingsForm(SettingsModel settings)
        {
            this.Settings = settings;
            this.Text = "Settings";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 400;
            this.Height = 230;

            var colorLabel = new Label { Text = "Fill color (hex):", Left = 10, Top = 20 };
            colorBox = new TextBox { Left = 140, Top = 18, Width = 170, Text = ColorTranslator.ToHtml(settings.FillColor ?? Color.Blue) };
            var colorBtn = new Button { Text = "Pick", Left = 320, Top = 16, Width = 40 };
            colorBtn.Click += (s, e) =>
            {
                using var dlg = new ColorDialog();
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    colorBox.Text = ColorTranslator.ToHtml(dlg.Color);
                }
            };

            var dateBox = new CheckBox { Text = "Include photo date on wallpaper", Left = 10, Top = 60, Width = 300, Checked = settings.ShowDateOnWallpaper };

            var cycleLabel = new Label { Text = "Cycle interval:", Left = 10, Top = 95 };
            cycleCombo = new ComboBox { Left = 140, Top = 92, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
            cycleCombo.Items.AddRange(new object[] { "Off", "5 minutes", "10 minutes", "20 minutes", "30 minutes", "60 minutes" });
            cycleCombo.SelectedIndex = IndexForMinutes(settings.CycleMinutes);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 140, Top = 150 };
            ok.Click += Ok_Click;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 220, Top = 150 };

            this.Controls.AddRange(new Control[] { colorLabel, colorBox, colorBtn, dateBox, cycleLabel, cycleCombo, ok, cancel });
        }


        private int IndexForMinutes(int minutes)
        {
            return minutes switch
            {
                5 => 1,
                10 => 2,
                20 => 3,
                30 => 4,
                60 => 5,
                _ => 0
            };
        }

        private int MinutesForIndex(int idx)
        {
            return idx switch
            {
                1 => 5,
                2 => 10,
                3 => 20,
                4 => 30,
                5 => 60,
                _ => 0
            };
        }

        private void Ok_Click(object? sender, EventArgs e)
        {
            try
            {   var c = ColorTranslator.FromHtml(colorBox.Text);
                Settings.FillColor = c;
                //Settings.Autostart = autostartBox.Checked;
                Settings.CycleMinutes = MinutesForIndex(cycleCombo.SelectedIndex);
                Settings.ShowDateOnWallpaper = this.Controls.OfType<CheckBox>().FirstOrDefault(c => c.Text.Contains("Include"))?.Checked ?? false;
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