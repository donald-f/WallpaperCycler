namespace WallpaperCycler
{
    public class SettingsForm : Form
    {
        public SettingsModel Settings { get; private set; }

        private readonly TextBox  _colorBox;
        private readonly CheckBox _dateBox;
        private readonly ComboBox _cycleCombo;

        public SettingsForm(SettingsModel settings)
        {
            Settings = settings;
            Text            = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            Width           = 400;
            Height          = 230;

            // Fill colour
            var colorLabel = new Label { Text = "Fill color (hex):", Left = 10, Top = 20, AutoSize = true };
            _colorBox      = new TextBox
            {
                Left  = 140,
                Top   = 18,
                Width = 170,
                Text  = ColorTranslator.ToHtml(settings.FillColor ?? AppConstants.DefaultFillColor)
            };
            var colorBtn = new Button { Text = "Pick", Left = 320, Top = 16, Width = 40 };
            colorBtn.Click += (_, _) =>
            {
                using var dlg = new ColorDialog();
                if (dlg.ShowDialog() == DialogResult.OK)
                    _colorBox.Text = ColorTranslator.ToHtml(dlg.Color);
            };

            // Show date on wallpaper
            _dateBox = new CheckBox
            {
                Text    = "Include photo date on wallpaper",
                Left    = 10,
                Top     = 60,
                Width   = 300,
                Checked = settings.ShowDateOnWallpaper
            };

            // Cycle interval
            var cycleLabel = new Label { Text = "Cycle interval:", Left = 10, Top = 95, AutoSize = true };
            _cycleCombo = new ComboBox
            {
                Left          = 140,
                Top           = 92,
                Width         = 170,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cycleCombo.Items.AddRange(["Off", "5 minutes", "10 minutes", "20 minutes", "30 minutes", "60 minutes"]);
            _cycleCombo.SelectedIndex = MinutesToIndex(settings.CycleMinutes);

            // Buttons
            var ok     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Left = 140, Top = 150 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 220, Top = 150 };
            ok.Click += Ok_Click;

            Controls.AddRange([colorLabel, _colorBox, colorBtn, _dateBox, cycleLabel, _cycleCombo, ok, cancel]);
        }

        private void Ok_Click(object? sender, EventArgs e)
        {
            Color parsed;
            try
            {
                parsed = ColorTranslator.FromHtml(_colorBox.Text.Trim());
            }
            catch
            {
                MessageBox.Show("Enter a valid hex color, e.g. #112233", "Invalid color",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Settings.FillColor           = parsed;
            Settings.ShowDateOnWallpaper = _dateBox.Checked;
            Settings.CycleMinutes        = IndexToMinutes(_cycleCombo.SelectedIndex);

            DialogResult = DialogResult.OK;
            Close();
        }

        private static int MinutesToIndex(int minutes) => minutes switch
        {
            5  => 1,
            10 => 2,
            20 => 3,
            30 => 4,
            60 => 5,
            _  => 0
        };

        private static int IndexToMinutes(int index) => index switch
        {
            1 => 5,
            2 => 10,
            3 => 20,
            4 => 30,
            5 => 60,
            _ => 0
        };
    }
}
