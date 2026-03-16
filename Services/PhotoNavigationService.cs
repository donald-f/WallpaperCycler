using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace WallpaperCycler
{
    /// <summary>
    /// Owns all photo navigation state (current path, current ordinal) and the logic for
    /// moving forward, backward, loading a new folder, and resetting the seen list.
    /// MainForm delegates all navigation decisions here.
    /// </summary>
    public class PhotoNavigationService
    {
        private readonly PhotoDatabase    _db;
        private readonly WallpaperService _wallpaperService;

        public string? CurrentPath    { get; private set; }
        public int     CurrentOrdinal { get; private set; } = -1;

        /// <summary>Fired on the calling thread after any navigation that changes state.</summary>
        public event EventHandler? StateChanged;

        public bool CanGoPrevious  => CurrentOrdinal > 0;
        public bool HasCurrentPhoto => !string.IsNullOrEmpty(CurrentPath) && File.Exists(CurrentPath);

        public PhotoNavigationService(PhotoDatabase db, WallpaperService wallpaperService)
        {
            _db               = db;
            _wallpaperService = wallpaperService;
        }

        // ── Startup resume ───────────────────────────────────────────────────────

        /// <summary>
        /// Called on startup to restore the last-shown wallpaper without scanning.
        /// </summary>
        public void RestoreLastShown(string path)
        {
            try
            {
                int ordinal = _db.GetSeenOrdinalForPath(path);
                CurrentPath    = path;
                CurrentOrdinal = ordinal;
                LogOrdinalProgress("Resumed wallpaper");
                _wallpaperService.SetWallpaperWithBackground(
                    path,
                    _db.Settings.FillColor ?? AppConstants.DefaultFillColor,
                    _db.Settings.ShowDateOnWallpaper);
                Logger.Log($"Resumed wallpaper: {path}");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Log($"RestoreLastShown failed: {ex.Message}");
            }
        }

        // ── Folder loading ───────────────────────────────────────────────────────

        /// <summary>
        /// Scans a new folder, picks the first random unseen photo, and sets it as wallpaper.
        /// Returns true if at least one photo was found, false if the folder was empty.
        /// Heavy DB work runs on a background thread; state update happens on the caller's thread.
        /// </summary>
        public async Task<bool> LoadFolderAsync(string folder)
        {
            _db.SetSetting("LastSelectedFolder", folder);

            var (foundPath, foundOrdinal) = await Task.Run(() =>
            {
                _db.DeleteAllPaths();
                _db.InitialScan(folder);

                var next = _db.GetRandomUnseen();
                if (next == null)
                    return ((string?)null, -1);

                int ordinal = _db.GetMaxSeenOrdinal() + 1;
                _db.MarkSeen(next.Path, ordinal);

                // Set wallpaper on the background thread (no UI interaction needed)
                _wallpaperService.SetWallpaperWithBackground(
                    next.Path,
                    _db.Settings.FillColor ?? AppConstants.DefaultFillColor,
                    _db.Settings.ShowDateOnWallpaper);

                Logger.Log($"Initial wallpaper set: {next.Path}");
                return (next.Path, ordinal);
            });

            if (foundPath == null)
            {
                // No photos found — apply solid fill
                _wallpaperService.SetSolidColorBackground(
                    _db.Settings.FillColor ?? AppConstants.DefaultFillColor);
                CurrentPath    = null;
                CurrentOrdinal = -1;
                _db.SetSetting("LastShownPath", string.Empty);
                Logger.Log($"No images found in folder: {folder}");
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            // Back on UI thread — update navigation state
            CurrentPath    = foundPath;
            CurrentOrdinal = foundOrdinal;
            _db.SetSetting("LastShownPath", foundPath);
            LogOrdinalProgress("Initial wallpaper set");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // ── Forward navigation ───────────────────────────────────────────────────

        /// <summary>
        /// Advances to the next photo.
        /// First replays already-seen photos in order; once exhausted, picks a random unseen one.
        /// When the pool is fully exhausted the seen list is reset and a new random photo is picked.
        /// Returns true if the caller should prompt for a new folder (no photos at all).
        /// </summary>
        public async Task<bool> GoToNextAsync()
        {
            // ── Sequential pass: replay already-seen photos ──────────────────────
            int checkOrdinal = CurrentOrdinal + 1;
            int maxOrdinal   = _db.GetMaxSeenOrdinal();

            while (checkOrdinal <= maxOrdinal)
            {
                var seen = _db.GetBySeenOrdinal(checkOrdinal);
                if (seen == null) { checkOrdinal++; continue; }

                if (File.Exists(seen.Path))
                {
                    ApplyPhoto(seen.Path, seen.SeenOrdinal);
                    Logger.Log($"Next wallpaper (sequential): {seen.Path}");
                    return false;
                }

                // Missing file — purge and keep searching
                _db.DeletePath(seen.Path);
                Logger.Log($"Removed missing sequential file: {seen.Path}");
                checkOrdinal++;
            }

            // ── Random unseen pass ───────────────────────────────────────────────
            var (foundPath, foundOrdinal, needsFolder) = await Task.Run(() => FindAndMarkRandomUnseen());

            if (needsFolder)
                return true;

            // Back on UI thread — update state (wallpaper was already set in the background)
            CurrentPath    = foundPath!;
            CurrentOrdinal = foundOrdinal;
            _db.SetSetting("LastShownPath", foundPath!);
            LogOrdinalProgress("Next wallpaper (random)");
            StateChanged?.Invoke(this, EventArgs.Empty);
            Logger.Log($"Next wallpaper (random): {foundPath}");
            return false;
        }

        // Runs on background thread. Sets the wallpaper there too (no UI objects touched).
        private (string? path, int ordinal, bool needsFolder) FindAndMarkRandomUnseen()
        {
            var next = _db.GetRandomUnseen();

            // Skip/purge any files that no longer exist on disk
            while (next != null && !File.Exists(next.Path))
            {
                _db.DeletePath(next.Path);
                Logger.Log($"Removed missing unseen file: {next.Path}");
                next = _db.GetRandomUnseen();
            }

            if (next == null)
            {
                // Pool exhausted — reset and try once more
                _db.ResetSeen();
                next = _db.GetRandomUnseen();
                if (next == null)
                    return (null, -1, true); // Truly no photos
            }

            int ordinal = _db.GetMaxSeenOrdinal() + 1;
            _db.MarkSeen(next.Path, ordinal);

            _wallpaperService.SetWallpaperWithBackground(
                next.Path,
                _db.Settings.FillColor ?? AppConstants.DefaultFillColor,
                _db.Settings.ShowDateOnWallpaper);

            return (next.Path, ordinal, false);
        }

        // ── Backward navigation ──────────────────────────────────────────────────

        /// <summary>
        /// Moves to the most recent previously-seen photo.
        /// Returns true if navigation succeeded, false if there is nothing to go back to.
        /// Fires StateChanged regardless so menu items are refreshed.
        /// </summary>
        public bool GoToPrevious()
        {
            if (CurrentOrdinal <= 0)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            int checkOrdinal = CurrentOrdinal - 1;
            while (checkOrdinal >= 0)
            {
                var prev = _db.GetBySeenOrdinal(checkOrdinal);
                if (prev == null) { checkOrdinal--; continue; }

                if (File.Exists(prev.Path))
                {
                    ApplyPhoto(prev.Path, prev.SeenOrdinal);
                    Logger.Log($"Previous wallpaper: {prev.Path}");
                    return true;
                }

                _db.DeletePath(prev.Path);
                Logger.Log($"Removed missing previous file: {prev.Path}");
                checkOrdinal--;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        // ── Reset ────────────────────────────────────────────────────────────────

        /// <summary>Marks all photos as unseen and clears current navigation state.</summary>
        public void ResetSeen()
        {
            _db.ResetSeen();
            CurrentPath    = null;
            CurrentOrdinal = -1;
            StateChanged?.Invoke(this, EventArgs.Empty);
            Logger.Log("Reset seen photos");
        }

        // ── Delete support ───────────────────────────────────────────────────────

        /// <summary>Removes a path from the database (call after physically deleting the file).</summary>
        public void RemoveFromDatabase(string path)
        {
            _db.DeletePath(path);
        }

        // ── GPS / location ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the current photo has readable GPS coordinates.
        /// Reads EXIF metadata — only call when needed (not on every tick).
        /// </summary>
        public bool HasGpsLocation()
        {
            if (string.IsNullOrEmpty(CurrentPath) || !File.Exists(CurrentPath))
                return false;

            try
            {
                var gpsDir = ImageMetadataReader.ReadMetadata(CurrentPath)
                    .OfType<GpsDirectory>()
                    .FirstOrDefault();

                return gpsDir != null
                    && gpsDir.GetRationalArray(GpsDirectory.TagLatitude)  != null
                    && gpsDir.GetRationalArray(GpsDirectory.TagLongitude) != null;
            }
            catch (Exception ex)
            {
                Logger.Log($"HasGpsLocation failed for '{CurrentPath}': {ex.Message}");
                return false;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void ApplyPhoto(string path, int ordinal)
        {
            CurrentPath    = path;
            CurrentOrdinal = ordinal;
            _db.SetSetting("LastShownPath", path);
            LogOrdinalProgress("Setting wallpaper");
            _wallpaperService.SetWallpaperWithBackground(
                path,
                _db.Settings.FillColor ?? AppConstants.DefaultFillColor,
                _db.Settings.ShowDateOnWallpaper);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LogOrdinalProgress(string context)
        {
            try
            {
                int total        = _db.GetTotalPhotoCount();
                int humanOrdinal = CurrentOrdinal >= 0 ? CurrentOrdinal + 1 : 0;
                Logger.Log($"{context} — ordinal {humanOrdinal} of {total}");
            }
            catch (Exception ex)
            {
                Logger.Log($"LogOrdinalProgress failed: {ex.Message}");
            }
        }
    }
}
