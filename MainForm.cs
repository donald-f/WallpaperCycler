using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private int rescanThreshold = 20; // rescans after this many "next" operations
        private int nextCounter = 0;
        private FileSystemWatcher? watcher;

        public MainForm()
        {
            // Hidden form - we only use tray
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Load += MainForm_Load;

            db = new PhotoDatabase(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "photos.db"));
            wallpaperService = new WallpaperService();

            InitializeTray();
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            // ensure app stays alive for tray
            this.Visible = false;
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
            trayMenu.Items.Add("Reset Seen Photos", null, OnReset);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Settings", null, OnSettings);
            trayMenu.Items.Add("Exit", null, OnExit);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "WallpaperCycler";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => OnNext(s, e);
        }

        private void OnSelectFolder(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                selectedFolder = fbd.SelectedPath;
                trayIcon.ShowBalloonTip(2000, "Folder selected", selectedFolder, ToolTipIcon.Info);
                SetupWatcher();
                Task.Run(() => db.InitialScan(selectedFolder));
                // Immediately pick a random unseen and display
                Task.Run(() =>
                {
                    var next = db.GetRandomUnseen();
                    if (next != null)
                    {
                        currentPath = next.Path;
                        currentOrdinal = 0;
                        db.MarkSeen(next.Path, 0);
                        ShowWallpaper(next.Path);
                        UpdatePrevEnabled();
                    }
                    else
                    {
                        trayIcon.ShowBalloonTip(2000, "No images found", "No eligible images in the selected folder.", ToolTipIcon.Warning);
                    }
                });
            }
        }

        private void OnPrevious(object? sender, EventArgs e)
        {
            if (currentOrdinal <= 0) return;
            var prev = db.GetBySeenOrdinal(currentOrdinal - 1);
            if (prev != null && File.Exists(prev.Path))
            {
                currentPath = prev.Path;
                currentOrdinal = prev.SeenOrdinal;
                ShowWallpaper(currentPath);
                UpdatePrevEnabled();
            }
            else
            {
                // out of sync, attempt cleanup
                db.DeleteMissingPaths();
                trayIcon.ShowBalloonTip(1500, "Previous unavailable", "Could not go back further.", ToolTipIcon.Info);
                UpdatePrevEnabled();
            }
        }

        private void OnNext(object? sender, EventArgs e)
        {
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
                    trayIcon.ShowBalloonTip(1500, "No unseen photos", "You've seen all photos. Use Reset to start over.", ToolTipIcon.Info);
                    return;
                }

                int newOrdinal = db.GetMaxSeenOrdinal() + 1;
                db.MarkSeen(next.Path, newOrdinal);
                currentPath = next.Path;
                currentOrdinal = newOrdinal;
                ShowWallpaper(next.Path);
                nextCounter++;
                UpdatePrevEnabled();

                if (nextCounter >= rescanThreshold)
                {
                    nextCounter = 0;
                    db.Rescan(selectedFolder);
                }
            });
        }

        private void OnDelete(object? sender, EventArgs e)
        {
            if (currentPath == null) return;
            var confirm = MessageBox.Show($"Delete this photo and send to Recycle Bin?\n{currentPath}", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm == DialogResult.Yes)
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(currentPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    db.DeletePath(currentPath);
                    trayIcon.ShowBalloonTip(1500, "Deleted", Path.GetFileName(currentPath), ToolTipIcon.Info);

                    // advance to next unseen
                    OnNext(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to delete file: " + ex.Message);
                }
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
            }
        }

        private void OnSettings(object? sender, EventArgs e)
        {
            using var s = new SettingsForm(db.Settings);
            if (s.ShowDialog() == DialogResult.OK)
            {
                db.Settings = s.Settings;
                trayIcon.ShowBalloonTip(1000, "Settings saved", "New settings applied.", ToolTipIcon.Info);
            }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            trayIcon.Visible = false;
            watcher?.Dispose();
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

        private void ShowWallpaper(string path)
        {
            try
            {
                var color = db.Settings.FillColor ?? ColorTranslator.FromHtml("#0b5fff");
                wallpaperService.SetWallpaperWithBackground(path, color);
                trayIcon.ShowBalloonTip(1000, "Wallpaper set", Path.GetFileName(path), ToolTipIcon.None);
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(2000, "Failed to set wallpaper", ex.Message, ToolTipIcon.Error);
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
            watcher.Created += (s, e) => db.HandleFileCreated(e.FullPath);
            watcher.Deleted += (s, e) => db.HandleFileDeleted(e.FullPath);
            watcher.Renamed += (s, e) => { db.HandleFileDeleted(e.OldFullPath); db.HandleFileCreated(e.FullPath); };
        }
    }
}