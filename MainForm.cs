using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

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
            if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
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
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"));
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
                    wallpaperService.SetWallpaperWithBackground(next.Path, fillColor);

                    UpdatePrevEnabled();
                    UpdateExplorerEnabled();

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
            var prev = db.GetBySeenOrdinal(currentOrdinal - 1);
            if (prev != null && File.Exists(prev.Path))
            {
                currentPath = prev.Path;
                currentOrdinal = prev.SeenOrdinal;
                wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"));
                db.SetSetting("LastShownPath", currentPath);
                UpdatePrevEnabled();
                Logger.Log($"Previous wallpaper: {currentPath}");
            }
            else
            {
                // out of sync, attempt cleanup
                db.DeleteMissingPaths();
                trayIcon.ShowBalloonTip(1500, "Previous unavailable", "Could not go back further.", ToolTipIcon.Info);
                UpdatePrevEnabled();
                UpdateExplorerEnabled();
            }
        }

        private void OnNext(object? sender, EventArgs e)
        {
            bool needFolderSelection = false;

            var possibleNext = db.GetBySeenOrdinal(currentOrdinal + 1);

            while (possibleNext != null)
            {
                if (File.Exists(possibleNext.Path))
                {
                    currentPath = possibleNext.Path;
                    currentOrdinal = possibleNext.SeenOrdinal;
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"));
                    db.SetSetting("LastShownPath", currentPath);
                    UpdatePrevEnabled();
                    UpdateExplorerEnabled();
                    Logger.Log($"Next wallpaper (sequential): {currentPath}");
                    return;
                }
                else
                {
                    // Missing file, remove from DB and continue searching
                    db.DeletePath(possibleNext.Path);
                }
                possibleNext = db.GetBySeenOrdinal(currentOrdinal + 1);
            }

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
                    db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff")
                );

                UpdatePrevEnabled();
                UpdateExplorerEnabled();
                Logger.Log($"Next wallpaper set: {next.Path}");
            })
            .ContinueWith(t =>
            {
                // Back on UI thread
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
            }, TaskScheduler.FromCurrentSynchronizationContext());  // ensures UI thread
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
                    wallpaperService.SetWallpaperWithBackground(currentPath, db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff"));
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