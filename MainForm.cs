using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace WallpaperCycler
{
    /// <summary>
    /// Hidden tray-icon form. Owns the cycle timer, file watcher, tray menu, and
    /// rapid panel. All navigation decisions are delegated to PhotoNavigationService.
    /// Implements IWallpaperController so RapidPanelForm can call actions without
    /// reflection or depending on MainForm internals.
    /// </summary>
    public class MainForm : Form, IWallpaperController
    {
        // ── Fields ───────────────────────────────────────────────────────────────

        private readonly PhotoDatabase          _db;
        private readonly WallpaperService       _wallpaperService;
        private readonly PhotoNavigationService _navService;
        private readonly FileWatcherManager     _watcher;

        private NotifyIcon        _trayIcon  = null!;
        private ContextMenuStrip  _trayMenu  = null!;
        private System.Windows.Forms.Timer _cycleTimer = null!;
        private RapidPanelForm?   _rapidPanel;

        // ── Constructor ──────────────────────────────────────────────────────────

        public MainForm()
        {
            // This form is never visible — it exists only to host the message loop
            ShowInTaskbar = false;
            WindowState   = FormWindowState.Minimized;
            Load         += (_, _) => Visible = false;

            _db               = new PhotoDatabase(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "photos.db"));
            _wallpaperService = new WallpaperService();
            _navService       = new PhotoNavigationService(_db, _wallpaperService);
            _watcher          = new FileWatcherManager();

            _navService.StateChanged += OnNavigationStateChanged;

            InitializeTray();
            InitializeTimer();
            WireFileWatcher();
            RestoreSession();
        }

        // ── IWallpaperController ─────────────────────────────────────────────────

        public bool CanGoPrevious  => _navService.CanGoPrevious;
        public bool HasCurrentPhoto => _navService.HasCurrentPhoto;
        public bool HasGpsLocation  => _navService.HasGpsLocation();

        public void GoToPrevious()
        {
            _cycleTimer.Stop();
            bool wentBack = _navService.GoToPrevious();
            if (!wentBack)
                _trayIcon.ShowBalloonTip(1500, "Previous unavailable", "Could not go back further.", ToolTipIcon.Info);
            ResetCycleTimer();
        }

        public void GoToNext()
        {
            _ = GoToNextAsync(); // fire-and-forget; errors are caught inside
        }

        public void DeleteCurrent()
        {
            _ = DeleteCurrentAsync();
        }

        public void ShowCurrentInExplorer()
        {
            string? path = _navService.CurrentPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _trayIcon.ShowBalloonTip(1500, "File not found", "Current photo no longer exists.", ToolTipIcon.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                Logger.Log($"Opened in Explorer: {path}");
            }
            catch (Exception ex)
            {
                Logger.Log($"ShowCurrentInExplorer failed: {ex.Message}");
                _trayIcon.ShowBalloonTip(1500, "Error", "Could not open File Explorer.", ToolTipIcon.Error);
            }
        }

        public void ViewCurrentLocation()
        {
            string? path = _navService.CurrentPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                var gpsDir = MetadataExtractor.ImageMetadataReader.ReadMetadata(path)
                    .OfType<GpsDirectory>()
                    .FirstOrDefault();

                if (gpsDir == null)
                {
                    _trayIcon.ShowBalloonTip(1500, "No location data", "This photo has no GPS metadata.", ToolTipIcon.Info);
                    return;
                }

                double? lat = GetCoordinate(gpsDir, GpsDirectory.TagLatitude,  GpsDirectory.TagLatitudeRef);
                double? lon = GetCoordinate(gpsDir, GpsDirectory.TagLongitude, GpsDirectory.TagLongitudeRef);

                if (lat == null || lon == null)
                {
                    _trayIcon.ShowBalloonTip(1500, "No location data", "This photo has no GPS coordinates.", ToolTipIcon.Info);
                    return;
                }

                string latStr = lat.Value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                string lonStr = lon.Value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                string url    = $"https://www.google.com/maps/search/?api=1&query={latStr}%2C{lonStr}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"ViewCurrentLocation failed: {ex.Message}");
            }
        }

        // ── Tray menu handlers ───────────────────────────────────────────────────

        private void OnSelectFolder(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
                _ = LoadFolderAsync(fbd.SelectedPath);
        }

        private void OnReset(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Reset seen photos? This will mark all photos as unseen.",
                "Confirm Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _navService.ResetSeen();
                _trayIcon.ShowBalloonTip(1500, "Reset", "All photos marked unseen.", ToolTipIcon.Info);
            }
        }

        private void OnSettings(object? sender, EventArgs e)
        {
            using var form = new SettingsForm(_db.Settings);
            if (form.ShowDialog() != DialogResult.OK) return;

            _db.Settings = form.Settings;
            _db.SetSetting("FillColor",           ColorTranslator.ToHtml(
                               _db.Settings.FillColor ?? AppConstants.DefaultFillColor));
            _db.SetSetting("ShowDateOnWallpaper",  _db.Settings.ShowDateOnWallpaper ? "true" : "false");
            _db.SetSetting("CycleMinutes",         _db.Settings.CycleMinutes.ToString());
            Logger.Log("Settings saved");

            // Re-apply current wallpaper with new settings
            string? path = _navService.CurrentPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _wallpaperService.SetWallpaperWithBackground(
                    path,
                    _db.Settings.FillColor ?? AppConstants.DefaultFillColor,
                    _db.Settings.ShowDateOnWallpaper);
                Logger.Log("Recomposed current wallpaper with updated settings");
            }

            // Update cycle timer
            if (_db.Settings.CycleMinutes > 0)
                StartCycleTimer(_db.Settings.CycleMinutes);
            else
                StopCycleTimer();

            _trayIcon.ShowBalloonTip(1000, "Settings saved", "New settings applied.", ToolTipIcon.Info);
        }

        private void OnOpenRapidPanel(object? sender, EventArgs e)
        {
            if (_rapidPanel == null || _rapidPanel.IsDisposed)
            {
                _rapidPanel = new RapidPanelForm(this);
                _rapidPanel.Show();
            }
            else
            {
                _rapidPanel.Focus();
            }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _cycleTimer.Stop();
            _watcher.Dispose();
            Application.Exit();
        }

        // ── Async helpers (called from IWallpaperController methods) ─────────────

        private async Task GoToNextAsync()
        {
            _cycleTimer.Stop();
            try
            {
                bool needsFolder = await _navService.GoToNextAsync();
                ResetCycleTimer();

                if (needsFolder)
                {
                    if (MessageBox.Show(
                            "No images found. Would you like to select a new folder?",
                            "Select New Folder",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        using var fbd = new FolderBrowserDialog();
                        if (fbd.ShowDialog() == DialogResult.OK)
                            await LoadFolderAsync(fbd.SelectedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"GoToNextAsync failed: {ex.Message}");
                ResetCycleTimer();
            }
        }

        private async Task DeleteCurrentAsync()
        {
            _cycleTimer.Stop();

            string? path = _navService.CurrentPath;
            if (string.IsNullOrEmpty(path))
            {
                _trayIcon.ShowBalloonTip(2000, "No image selected",
                    "No file is currently selected to delete.", ToolTipIcon.Warning);
                ResetCycleTimer();
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete this photo and its metadata and live video (if present) and send to Recycle Bin?\n{path}",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                ResetCycleTimer();
                return;
            }

            try
            {
                DeleteAssociatedFiles(path);

                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                Logger.Log($"Deleted photo: {path}");
                _navService.RemoveFromDatabase(path);

                await GoToNextAsync(); // advances and resets timer
            }
            catch (Exception ex)
            {
                Logger.Log($"DeleteCurrentAsync failed: {ex.Message}");
                MessageBox.Show("Failed to delete file: " + ex.Message);
                ResetCycleTimer();
            }
        }

        private async Task LoadFolderAsync(string folder)
        {
            _trayIcon.ShowBalloonTip(2000, "Folder selected", folder, ToolTipIcon.Info);
            Logger.Log($"Folder selected: {folder}");

            _watcher.Watch(folder);

            bool found = await _navService.LoadFolderAsync(folder);

            if (!found)
                _trayIcon.ShowBalloonTip(2000, "No images found",
                    "No eligible images in the selected folder.", ToolTipIcon.Warning);

            ResetCycleTimer();
        }

        // ── File watcher callbacks (background thread) ───────────────────────────

        private void WireFileWatcher()
        {
            _watcher.PhotoCreated  += path => _db.HandleFileCreated(path);
            _watcher.PhotoDeleted  += path => _db.HandleFileDeleted(path);
            _watcher.PhotoRenamed  += (oldPath, newPath) =>
            {
                _db.HandleFileDeleted(oldPath);
                _db.HandleFileCreated(newPath);
            };
            _watcher.PhotoFinalized += path =>
            {
                // A new photo just landed (e.g. download completed).
                // Add to DB and immediately show it on the wallpaper.
                _db.HandleFileCreated(path);
                _wallpaperService.SetWallpaperWithBackground(
                    path,
                    _db.Settings.FillColor ?? AppConstants.DefaultFillColor,
                    _db.Settings.ShowDateOnWallpaper);
                Logger.Log($"New photo finalized, wallpaper refreshed: {path}");
            };

            // Start watching if a folder was already selected
            string? lastFolder = _db.GetSetting("LastSelectedFolder");
            if (!string.IsNullOrEmpty(lastFolder) && System.IO.Directory.Exists(lastFolder))
                _watcher.Watch(lastFolder);
        }

        // ── State-change handler ─────────────────────────────────────────────────

        private void OnNavigationStateChanged(object? sender, EventArgs e)
        {
            UpdateMenuItemStates();
        }

        private void UpdateMenuItemStates()
        {
            if (_trayMenu.InvokeRequired)
            {
                _trayMenu.BeginInvoke(UpdateMenuItemStates);
                return;
            }

            // "Previous Photo"
            var prev = _trayMenu.Items.Find("prev", false).FirstOrDefault() as ToolStripMenuItem;
            if (prev != null) prev.Enabled = _navService.CanGoPrevious;

            // "Show in File Explorer"
            var explorer = _trayMenu.Items.Cast<ToolStripItem>()
                .FirstOrDefault(i => i.Text?.Contains("Explorer") == true) as ToolStripMenuItem;
            if (explorer != null) explorer.Enabled = _navService.HasCurrentPhoto;

            // "View Photo Location"
            var loc = _trayMenu.Items.Find("location", false).FirstOrDefault() as ToolStripMenuItem;
            if (loc != null) loc.Enabled = _navService.HasGpsLocation();
        }

        // ── Session restore ──────────────────────────────────────────────────────

        private void RestoreSession()
        {
            string? lastShown = _db.GetSetting("LastShownPath");
            if (!string.IsNullOrEmpty(lastShown) && File.Exists(lastShown))
                _navService.RestoreLastShown(lastShown);

            if (_db.Settings.CycleMinutes > 0)
                StartCycleTimer(_db.Settings.CycleMinutes);

            UpdateMenuItemStates();
        }

        // ── Timer ────────────────────────────────────────────────────────────────

        private void InitializeTimer()
        {
            _cycleTimer      = new System.Windows.Forms.Timer();
            _cycleTimer.Tick += (_, _) =>
            {
                Logger.Log("Cycle timer tick");
                GoToNext();
            };
        }

        private void StartCycleTimer(int minutes)
        {
            _cycleTimer.Interval = minutes * 60 * 1000;
            _cycleTimer.Start();
            Logger.Log($"Cycle timer started: {minutes} min");
        }

        private void StopCycleTimer()
        {
            _cycleTimer.Stop();
            Logger.Log("Cycle timer stopped");
        }

        private void ResetCycleTimer()
        {
            if (_db.Settings.CycleMinutes <= 0)
            {
                Logger.Log("ResetCycleTimer: cycle disabled");
                return;
            }
            _cycleTimer.Stop();
            _cycleTimer.Interval = _db.Settings.CycleMinutes * 60 * 1000;
            _cycleTimer.Start();
            Logger.Log($"Cycle timer reset: {_db.Settings.CycleMinutes} min");
        }

        // ── Tray initialisation ──────────────────────────────────────────────────

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();

            _trayMenu.Items.Add(new ToolStripMenuItem("Open Rapid Panel",        null, OnOpenRapidPanel));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Select Pictures Folder",                         null, OnSelectFolder);
            _trayMenu.Items.Add(new ToolStripMenuItem("Previous Photo",           null, (s, e) => GoToPrevious())
                { Name = "prev", Enabled = false });
            _trayMenu.Items.Add("Next Photo",                                     null, (s, e) => GoToNext());
            _trayMenu.Items.Add("Delete Current Photo",                           null, (s, e) => DeleteCurrent());
            _trayMenu.Items.Add("Show in File Explorer",                          null, (s, e) => ShowCurrentInExplorer());
            _trayMenu.Items.Add(new ToolStripMenuItem("View Photo Location",      null, (s, e) => ViewCurrentLocation())
                { Name = "location", Enabled = false });
            _trayMenu.Items.Add("Reset Seen Photos",                              null, OnReset);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Settings",                                       null, OnSettings);
            _trayMenu.Items.Add("Exit",                                           null, OnExit);

            _trayIcon = new NotifyIcon
            {
                Text             = "WallpaperCycler",
                Icon             = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                ContextMenuStrip = _trayMenu,
                Visible          = true
            };
            _trayIcon.DoubleClick += (_, _) => GoToNext();
        }

        // ── Static GPS helper ────────────────────────────────────────────────────

        private static double? GetCoordinate(GpsDirectory dir, int coordTag, int refTag)
        {
            var rationals = dir.GetRationalArray(coordTag);
            var reference = dir.GetString(refTag);

            if (rationals == null || rationals.Length < 3)
                return null;

            double degrees = rationals[0].ToDouble() + rationals[1].ToDouble() / 60.0 + rationals[2].ToDouble() / 3600.0;

            if (reference == "S" || reference == "W")
                degrees *= -1;

            return degrees;
        }

        // ── Delete associated files helper ───────────────────────────────────────

        private static void DeleteAssociatedFiles(string imagePath)
        {
            // Supplemental metadata JSON
            string jsonPath = imagePath + ".supplemental-metadata.json";
            if (File.Exists(jsonPath))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    jsonPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                Logger.Log($"Deleted metadata JSON: {jsonPath}");
            }

            // Live photo companion video (.mp4 or .mov)
            foreach (string ext in new[] { ".mp4", ".mov" })
            {
                string videoPath = Path.ChangeExtension(imagePath, ext);
                if (File.Exists(videoPath) && LivePhotoHelpers.IsLikelyLivePhotoVideo(imagePath, videoPath))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        videoPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    Logger.Log($"Deleted live photo video: {videoPath}");
                    return; // Only one companion video expected
                }
            }

            Logger.Log($"No live video companion found for: {imagePath}");
        }
    }
}
