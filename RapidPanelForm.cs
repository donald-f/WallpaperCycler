namespace WallpaperCycler
{
    /// <summary>
    /// Floating control panel that drives wallpaper actions via the IWallpaperController
    /// interface. No reflection, no access to MainForm internals.
    /// </summary>
    public class RapidPanelForm : Form
    {
        private readonly IWallpaperController _controller;

        private readonly Button _btnPrev;
        private readonly Button _btnNext;
        private readonly Button _btnDelete;
        private readonly Button _btnExplorer;
        private readonly Button _btnLocation;

        public RapidPanelForm(IWallpaperController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));

            Text            = "Rapid Photo Controls";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition   = FormStartPosition.CenterScreen;
            Width           = 300;
            Height          = 260;

            _btnPrev     = MakeButton("Previous",        10);
            _btnNext     = MakeButton("Next",            50);
            _btnDelete   = MakeButton("Delete",          90);
            _btnExplorer = MakeButton("Show In Explorer",130);
            _btnLocation = MakeButton("View Location",   170);

            _btnPrev.Click     += (_, _) => { _controller.GoToPrevious();         RefreshButtonStates(); };
            _btnNext.Click     += (_, _) => { _controller.GoToNext();             ScheduleRefresh(); };
            _btnDelete.Click   += (_, _) => { _controller.DeleteCurrent();        ScheduleRefresh(); };
            _btnExplorer.Click += (_, _) => { _controller.ShowCurrentInExplorer(); };
            _btnLocation.Click += (_, _) => { _controller.ViewCurrentLocation();  };

            Controls.AddRange(new Control[] { _btnPrev, _btnNext, _btnDelete, _btnExplorer, _btnLocation });

            Shown     += (_, _) => RefreshButtonStates();
            Activated += (_, _) => RefreshButtonStates();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void RefreshButtonStates()
        {
            _btnPrev.Enabled     = _controller.CanGoPrevious;
            _btnExplorer.Enabled = _controller.HasCurrentPhoto;
            _btnLocation.Enabled = _controller.HasGpsLocation;
        }

        /// <summary>
        /// Next/Delete are async on the controller side. Wait briefly then refresh,
        /// so the button states reflect the new navigation position.
        /// </summary>
        private void ScheduleRefresh()
        {
            Task.Delay(200).ContinueWith(_ =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(RefreshButtonStates);
            }, TaskScheduler.Default);
        }

        private static Button MakeButton(string text, int top) =>
            new Button { Text = text, Width = 250, Top = top, Left = 10 };
    }
}
