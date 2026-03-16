using Microsoft.WindowsAPICodePack.Shell;

namespace WallpaperCycler
{
    public static class LivePhotoHelpers
    {
        private static readonly TimeSpan TimestampTolerance       = TimeSpan.FromSeconds(3);
        private const double            MaxDurationSecondsLikelyLive = 8.0;
        private const long              MaxReasonableSizeBytes    = 150L * 1024 * 1024; // 150 MB

        /// <summary>
        /// Returns true when <paramref name="videoPath"/> is very likely the Live Photo
        /// companion video for <paramref name="imagePath"/>, based on filename, timestamps,
        /// file size, and video duration.
        /// </summary>
        public static bool IsLikelyLivePhotoVideo(string imagePath, string videoPath)
        {
            if (!File.Exists(imagePath) || !File.Exists(videoPath))
                return false;

            // Names must match (ignoring extension)
            if (!Path.GetFileNameWithoutExtension(imagePath)
                     .Equals(Path.GetFileNameWithoutExtension(videoPath), StringComparison.OrdinalIgnoreCase))
                return false;

            // Only consider MP4/MOV as live-photo companions
            string ext = Path.GetExtension(videoPath).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".mov")
                return false;

            var imgInfo = new FileInfo(imagePath);
            var vidInfo = new FileInfo(videoPath);

            // Creation timestamps must be close together
            var imgTime = imgInfo.CreationTimeUtc != DateTime.MinValue
                ? imgInfo.CreationTimeUtc : imgInfo.LastWriteTimeUtc;
            var vidTime = vidInfo.CreationTimeUtc != DateTime.MinValue
                ? vidInfo.CreationTimeUtc : vidInfo.LastWriteTimeUtc;

            if ((imgTime - vidTime).Duration() > TimestampTolerance)
                return false;

            // Reject obviously large videos
            if (vidInfo.Length > MaxReasonableSizeBytes)
                return false;

            // Duration must be readable and short
            double duration = GetVideoDurationSeconds(videoPath);
            if (duration <= 0)
            {
                // Cannot confirm short duration — err on the side of caution
                return false;
            }

            return duration <= MaxDurationSecondsLikelyLive;
        }

        private static double GetVideoDurationSeconds(string videoPath)
        {
            try
            {
                using var shell = ShellFile.FromFilePath(videoPath);
                var prop = shell.Properties.System.Media.Duration;
                if (prop?.Value != null)
                {
                    // Shell property stores duration in 100-nanosecond units
                    long ticks = Convert.ToInt64(prop.Value);
                    return ticks / 10_000_000.0;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"LivePhotoHelpers.GetVideoDurationSeconds failed for '{videoPath}': {ex.Message}");
            }
            return 0.0;
        }
    }
}
