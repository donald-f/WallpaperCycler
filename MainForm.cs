using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace WallpaperCycler
{
    public class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private PhotoDatabase db;
        private WallpaperService wallpaperService;
        private string? selectedFolder;
        private string? currentPath;
        private int currentOrdinal = -1;
        private FileSystemWatcher? watcher;
        private System.Windows.Forms.Timer cycleTimer;

        public MainForm()
        {
            // Hidden form - we only use tray
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Load += MainForm_Load;

            db = new PhotoDatabase(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "photos.db"));
            wallpaperService = new WallpaperService();

            InitializeTray();
            InitializeTimer();

            // Immediately set up watcher if folder already known
            var lastFolder = db.GetSetting("LastSelectedFolder");
            if (!string.IsNullOrEmpty(lastFolder) && System.IO.Directory.Exists(lastFolder))
            {
                selectedFolder = lastFolder;
                SetupWatcher();
            }

            // Resume last shown wallpaper silently
            var lastShown = db.GetSetting("LastShownPath");
            if (!string.IsNullOrEmpty(lastShown) && File.Exists(lastShown))
            {
                try
                {
                    currentPath = lastShown;
                    currentOrdinal = db.GetSeenOrdinalForPath(lastShown);
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"), db.Settings.ShowDateOnWallpaper);
                    Logger.Log($"Resumed wallpaper: {currentPath}");
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to resume wallpaper: " + ex.Message);
                }
            }

            // Start cycle timer if enabled in settings
            if (db.Settings.CycleMinutes > 0)
            {
                StartCycleTimer(db.Settings.CycleMinutes);
            }

            // Sync autostart flag with actual state
            //bool isAuto = StartupManager.IsAutostartEnabled();
            //db.Settings.Autostart = isAuto;
            //db.SetSetting("Autostart", isAuto ? "true" : "false");
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            this.Visible = false; // ensure app stays alive for tray
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Select Pictures Folder", null, OnSelectFolder);
            var prevItem = new ToolStripMenuItem("Previous Photo", null, OnPrevious) { Enabled = false };
            prevItem.Name = "prev";
            trayMenu.Items.Add(prevItem);
            trayMenu.Items.Add("Next Photo", null, OnNext);
            trayMenu.Items.Add("Delete Current Photo", null, OnDelete);
            trayMenu.Items.Add("Show in File Explorer", null, OnShowInExplorer);
            var locItem = new ToolStripMenuItem("View Photo Location", null, OnViewLocation)
            {
                Name = "location",
                Enabled = false
            };
            trayMenu.Items.Add(locItem);
            trayMenu.Items.Add("Reset Seen Photos", null, OnReset);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Settings", null, OnSettings);
            trayMenu.Items.Add("Exit", null, OnExit);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "WallpaperCycler";
            trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => OnNext(s, e);
        }

        private void InitializeTimer()
        {
            cycleTimer = new System.Windows.Forms.Timer();
            cycleTimer.Tick += (s, e) => OnNext(s, EventArgs.Empty);
        }

        private void OnSelectFolder(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                LoadAndDisplayFirstPhoto(fbd.SelectedPath);
            }
        }


        private void LoadAndDisplayFirstPhoto(string folderPath)
        {
            selectedFolder = folderPath;
            db.SetSetting("LastSelectedFolder", selectedFolder);
            trayIcon.ShowBalloonTip(2000, "Folder selected", selectedFolder, ToolTipIcon.Info);
            Logger.Log($"Folder selected: {selectedFolder}");

            SetupWatcher();

            Task.Run(() =>
            {
                db.DeleteAllPaths();
                db.InitialScan(selectedFolder);

                var next = db.GetRandomUnseen();

                if (next != null)
                {
                    int newOrdinal = db.GetMaxSeenOrdinal() + 1;
                    db.MarkSeen(next.Path, newOrdinal);
                    currentPath = next.Path;
                    currentOrdinal = newOrdinal;
                    db.SetSetting("LastShownPath", currentPath);

                    var fillColor = db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff");
                    wallpaperService.SetWallpaperWithBackground(next.Path, fillColor, db.Settings.ShowDateOnWallpaper);

                    UpdatePrevEnabled();
                    UpdateExplorerEnabled();
                    UpdateLocationEnabled();

                    Logger.Log($"Initial wallpaper set: {next.Path}");
                }
                else
                {
                    var fillColor = db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff");
                    wallpaperService.SetSolidColorBackground(fillColor);

                    currentPath = null;
                    currentOrdinal = -1;

                    UpdatePrevEnabled();
                    UpdateExplorerEnabled();
                    UpdateLocationEnabled();

                    db.SetSetting("LastShownPath", "");

                    trayIcon.ShowBalloonTip(2000, "No images found",
                        "No eligible images in the selected folder.", ToolTipIcon.Warning);

                    Logger.Log($"No images found in folder: {selectedFolder}. Applied solid fill background.");
                }
            });
        }


        private void OnPrevious(object? sender, EventArgs e)
        {
            if (currentOrdinal <= 0) return;

            int checkOrdinal = currentOrdinal - 1;

            while (checkOrdinal >= 0)
            {
                var prev = db.GetBySeenOrdinal(checkOrdinal);
                if (prev == null)
                {
                    checkOrdinal--;
                    continue;
                }

                if (File.Exists(prev.Path))
                {
                    currentPath = prev.Path;
                    currentOrdinal = prev.SeenOrdinal;
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"), db.Settings.ShowDateOnWallpaper);
                    db.SetSetting("LastShownPath", currentPath);
                    UpdatePrevEnabled();
                    UpdateExplorerEnabled();
                    UpdateLocationEnabled();
                    Logger.Log($"Previous wallpaper: {currentPath}");
                    return;
                }

                // Missing file: remove from DB and continue
                db.DeletePath(prev.Path);
                Logger.Log($"Removed missing previous file: {prev.Path}");
                checkOrdinal--;
            }

            trayIcon.ShowBalloonTip(1500, "Previous unavailable", "Could not go back further.", ToolTipIcon.Info);
            UpdatePrevEnabled();
            UpdateExplorerEnabled();
            UpdateLocationEnabled();
        }


        private void OnNext(object? sender, EventArgs e)
        {
            bool needFolderSelection = false;
            int checkOrdinal = currentOrdinal + 1;
            int maxOrdinal = db.GetMaxSeenOrdinal();

            // Try to move forward through already seen images first
            while (checkOrdinal <= maxOrdinal)
            {
                var nextSeen = db.GetBySeenOrdinal(checkOrdinal);
                if (nextSeen == null)
                {
                    checkOrdinal++;
                    continue;
                }

                if (File.Exists(nextSeen.Path))
                {
                    currentPath = nextSeen.Path;
                    currentOrdinal = nextSeen.SeenOrdinal;
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"), db.Settings.ShowDateOnWallpaper);
                    db.SetSetting("LastShownPath", currentPath);
                    UpdatePrevEnabled();
                    UpdateExplorerEnabled();
                    UpdateLocationEnabled();
                    Logger.Log($"Next wallpaper (sequential): {currentPath}");
                    return;
                }

                db.DeletePath(nextSeen.Path);
                Logger.Log($"Removed missing next file: {nextSeen.Path}");
                checkOrdinal++;
            }

            // If no more "seen" images available, get a random unseen one
            Task.Run(() =>
            {
                var next = db.GetRandomUnseen();
                while (next != null && !File.Exists(next.Path))
                {
                    db.DeletePath(next.Path);
                    next = db.GetRandomUnseen();
                }

                if (next == null)
                {
                    db.ResetSeen();
                    next = db.GetRandomUnseen();
                    if (next == null)
                    {
                        needFolderSelection = true;
                        return;
                    }
                }

                int newOrdinal = db.GetMaxSeenOrdinal() + 1;
                db.MarkSeen(next.Path, newOrdinal);
                currentPath = next.Path;
                currentOrdinal = newOrdinal;
                db.SetSetting("LastShownPath", currentPath);

                wallpaperService.SetWallpaperWithBackground(
                    next.Path,
                    db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"),
                    db.Settings.ShowDateOnWallpaper
                );

                UpdatePrevEnabled();
                UpdateExplorerEnabled();
                UpdateLocationEnabled();
                Logger.Log($"Next wallpaper set: {next.Path}");
            })
            .ContinueWith(t =>
            {
                if (needFolderSelection)
                {
                    if (MessageBox.Show(
                        "No images found. Would you like to select a new folder?",
                        "Select New Folder",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    ) == DialogResult.Yes)
                    {
                        using var fbd = new FolderBrowserDialog();
                        if (fbd.ShowDialog() == DialogResult.OK)
                        {
                            LoadAndDisplayFirstPhoto(fbd.SelectedPath);
                        }
                    }
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnDelete(object? sender, EventArgs e)
        {
            if (currentPath == null)
            {
                trayIcon.ShowBalloonTip(2000, "No images selected",
                            "No file is currently selected to delete.", ToolTipIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete this photo and its metadata (if present) and send to Recycle Bin?\n{currentPath}",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirm == DialogResult.Yes)
            {
                try
                {
                    // Delete the main image
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        currentPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                    );
                    Logger.Log($"Deleted photo: {currentPath}");
                    db.DeletePath(currentPath);

                    // Delete the associated JSON file if it exists
                    string jsonPath = currentPath + ".supplemental-metadata.json";
                    if (File.Exists(jsonPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            jsonPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                        );
                        Logger.Log($"Deleted metadata JSON: {jsonPath}");
                    }

                    // Advance to next unseen
                    OnNext(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to delete file: " + ex.Message);
                    Logger.Log("Failed to delete file: " + ex.Message);
                }
            }
        }


        private void OnShowInExplorer(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
            {
                trayIcon.ShowBalloonTip(1500, "File not found", "Current photo no longer exists.", ToolTipIcon.Warning);
                return;
            }

            try
            {
                // Open the folder and highlight the file
                string args = $"/select,\"{currentPath}\"";
                System.Diagnostics.Process.Start("explorer.exe", args);
                Logger.Log($"Opened in Explorer: {currentPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to open Explorer: {ex.Message}");
                trayIcon.ShowBalloonTip(1500, "Error", "Could not open File Explorer.", ToolTipIcon.Error);
            }
        }

        private void OnViewLocation(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath)) return;

            try
            {
                var gpsDir = ImageMetadataReader.ReadMetadata(currentPath)
                    .OfType<MetadataExtractor.Formats.Exif.GpsDirectory>()
                    .FirstOrDefault();

                if (gpsDir == null)
                {
                    trayIcon.ShowBalloonTip(1500, "No location data", "This photo has no GPS metadata.", ToolTipIcon.Info);
                    return;
                }

                double? lat = GetCoordinate(gpsDir, GpsDirectory.TagLatitude, GpsDirectory.TagLatitudeRef);
                double? lon = GetCoordinate(gpsDir, GpsDirectory.TagLongitude, GpsDirectory.TagLongitudeRef);

                if (lat == null || lon == null)
                {
                    trayIcon.ShowBalloonTip(1500, "No location data", "This photo has no GPS coordinates.", ToolTipIcon.Info);
                    return;
                }

                string latStr = lat.Value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                string lonStr = lon.Value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                string url = $"https://www.google.com/maps/search/?api=1&query={latStr}%2C{lonStr}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open location: " + ex.Message);
            }
        }


        private void OnReset(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show("Reset seen photos? This will mark all photos as unseen.", "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm == DialogResult.Yes)
            {
                db.ResetSeen();
                trayIcon.ShowBalloonTip(1500, "Reset", "All photos marked unseen.", ToolTipIcon.Info);
                currentPath = null;
                currentOrdinal = -1;
                UpdatePrevEnabled();
                UpdateExplorerEnabled();
                UpdateLocationEnabled();
                Logger.Log("Reset seen photos");
            }
        }

        private void OnSettings(object? sender, EventArgs e)
        {
            using var s = new SettingsForm(db.Settings);
            if (s.ShowDialog() == DialogResult.OK)
            {
                db.Settings = s.Settings;
                db.SetSetting("FillColor", ColorTranslator.ToHtml(db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff")));
                //db.SetSetting("Autostart", db.Settings.Autostart ? "true" : "false");
                db.SetSetting("CycleMinutes", db.Settings.CycleMinutes.ToString());
                Logger.Log("Settings saved");

                // ✅ Updated autostart handling using new StartupManager
                //StartupManager.SetAutostart(db.Settings.Autostart);

                // Immediately apply new fill color
                if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                {
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"), db.Settings.ShowDateOnWallpaper);
                    Logger.Log("Recomposed current wallpaper with new fill color");
                }

                // Handle cycle timer updates
                if (db.Settings.CycleMinutes > 0)
                {
                    StartCycleTimer(db.Settings.CycleMinutes);
                }
                else
                {
                    StopCycleTimer();
                }

                trayIcon.ShowBalloonTip(1000, "Settings saved", "New settings applied.", ToolTipIcon.Info);
            }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            trayIcon.Visible = false;
            watcher?.Dispose();
            StopCycleTimer();
            Application.Exit();
        }

        private void UpdatePrevEnabled()
        {
            if (trayMenu.InvokeRequired)
            {
                trayMenu.Invoke(new Action(UpdatePrevEnabled));
                return;
            }
            var prev = trayMenu.Items.Find("prev", false).FirstOrDefault() as ToolStripMenuItem;
            if (prev != null)
            {
                prev.Enabled = currentOrdinal > 0;
            }
        }

        private void SetupWatcher()
        {
            watcher?.Dispose();
            if (string.IsNullOrEmpty(selectedFolder)) return;
            watcher = new FileSystemWatcher(selectedFolder)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            watcher.Created += (s, e) => { db.HandleFileCreated(e.FullPath); Logger.Log($"File created: {e.FullPath}"); };
            watcher.Deleted += (s, e) => { db.HandleFileDeleted(e.FullPath); Logger.Log($"File deleted: {e.FullPath}"); };
            watcher.Renamed += (s, e) => { db.HandleFileDeleted(e.OldFullPath); db.HandleFileCreated(e.FullPath); Logger.Log($"File renamed: {e.OldFullPath} -> {e.FullPath}"); };
        }

        private void UpdateExplorerEnabled()
        {
            var showInExplorerItem = trayMenu.Items.Cast<ToolStripItem>()
    .FirstOrDefault(i => !String.IsNullOrEmpty(i.Text) && i.Text.Contains("Explorer")) as ToolStripMenuItem;
            if (showInExplorerItem != null)
                showInExplorerItem.Enabled = !string.IsNullOrEmpty(currentPath) && File.Exists(currentPath);

        }

        private void UpdateLocationEnabled()
        {
            var locItem = trayMenu.Items.Find("location", false).FirstOrDefault() as ToolStripMenuItem;
            if (locItem == null || string.IsNullOrEmpty(currentPath)) return;

            try
            {
                var gpsDir = ImageMetadataReader.ReadMetadata(currentPath)
                    .OfType<MetadataExtractor.Formats.Exif.GpsDirectory>()
                    .FirstOrDefault();

                bool hasGps = gpsDir != null &&
                              gpsDir.GetRationalArray(GpsDirectory.TagLatitude) != null &&
                              gpsDir.GetRationalArray(GpsDirectory.TagLongitude) != null;

                locItem.Enabled = hasGps;
            }
            catch
            {
                locItem.Enabled = false;
            }
        }

        /// <summary>
        /// Converts EXIF GPS data to decimal degrees.
        /// </summary>
        private static double? GetCoordinate(GpsDirectory dir, int coordTag, int refTag)
        {
            var rationalValues = dir.GetRationalArray(coordTag);
            var refValue = dir.GetString(refTag);

            if (rationalValues == null || rationalValues.Length < 3)
                return null;

            double degrees = rationalValues[0].ToDouble();
            double minutes = rationalValues[1].ToDouble();
            double seconds = rationalValues[2].ToDouble();

            double decimalDegrees = degrees + (minutes / 60.0) + (seconds / 3600.0);

            if (refValue == "S" || refValue == "W")
                decimalDegrees *= -1;

            return decimalDegrees;
        }

        private void StartCycleTimer(int minutes)
        {
            cycleTimer.Interval = minutes * 60 * 1000;
            cycleTimer.Start();
            Logger.Log($"Cycle timer started: {minutes} minutes");
        }

        private void StopCycleTimer()
        {
            cycleTimer.Stop();
            Logger.Log("Cycle timer stopped");
        }
    }
}