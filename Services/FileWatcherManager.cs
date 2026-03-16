namespace WallpaperCycler
{
    /// <summary>
    /// Wraps FileSystemWatcher and translates raw file-system events into
    /// meaningful photo lifecycle events that MainForm can react to.
    /// All events are fired on a background thread (FileSystemWatcher's thread pool).
    /// </summary>
    public sealed class FileWatcherManager : IDisposable
    {
        private FileSystemWatcher? _watcher;

        /// <summary>A new valid photo file appeared in the watched folder.</summary>
        public event Action<string>? PhotoCreated;

        /// <summary>A valid photo file was deleted from the watched folder.</summary>
        public event Action<string>? PhotoDeleted;

        /// <summary>
        /// A valid photo file was renamed to another valid photo file name.
        /// Args: (oldPath, newPath).
        /// </summary>
        public event Action<string, string>? PhotoRenamed;

        /// <summary>
        /// A temp/junk file was renamed to a valid photo file name —
        /// i.e., a file download/copy just completed.
        /// The new file should be added to the DB and shown immediately.
        /// </summary>
        public event Action<string>? PhotoFinalized;

        public void Watch(string folder)
        {
            StopWatcher();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter          = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents   = true
            };

            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
        }

        public void Dispose() => StopWatcher();

        // ── Event handlers ───────────────────────────────────────────────────────

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (!IsRealPhotoFile(e.FullPath)) return;
            Logger.Log($"File watcher: created {e.FullPath}");
            PhotoCreated?.Invoke(e.FullPath);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            if (!IsRealPhotoFile(e.FullPath)) return;
            Logger.Log($"File watcher: deleted {e.FullPath}");
            PhotoDeleted?.Invoke(e.FullPath);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            bool oldIsReal = IsRealPhotoFile(e.OldFullPath);
            bool newIsReal = IsRealPhotoFile(e.FullPath);

            if (!oldIsReal && !newIsReal)
                return; // Both junk — irrelevant

            if (oldIsReal && !newIsReal)
                return; // Photo renamed to a junk name (transient temp rename) — ignore

            if (!oldIsReal && newIsReal)
            {
                // A temp/in-progress file was finalised as a real photo
                Logger.Log($"File watcher: finalized {e.FullPath}");
                PhotoFinalized?.Invoke(e.FullPath);
                return;
            }

            // Real → Real rename
            Logger.Log($"File watcher: renamed {e.OldFullPath} -> {e.FullPath}");
            PhotoRenamed?.Invoke(e.OldFullPath, e.FullPath);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsRealPhotoFile(string path)
        {
            if (!AppConstants.ValidPhotoExtensions.Contains(Path.GetExtension(path)))
                return false;

            string fileName = Path.GetFileName(path).ToLowerInvariant();
            foreach (var pattern in AppConstants.IgnoredFilePatterns)
                if (fileName.Contains(pattern))
                    return false;

            return true;
        }

        private void StopWatcher()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnCreated;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
