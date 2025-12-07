using System;
using System.IO;
using Microsoft.WindowsAPICodePack.Shell;

public static class LivePhotoHelpers
{
    // tolerance settings (tweakable)
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(3);
    private const double MaxDurationSecondsLikelyLive = 8.0;   // <= 8s is strong indicator
    private const double MaxDurationSecondsMaybeLive = 12.0;   // <= 12s could be ok
    private const long MaxReasonableSizeBytes = 150 * 1024 * 1024; // 150 MB: reject huge files

    public static bool IsLikelyLivePhotoVideo(string imagePath, string videoPath)
    {
        if (!File.Exists(imagePath) || !File.Exists(videoPath))
            return false;

        var imageBase = Path.GetFileNameWithoutExtension(imagePath);
        var videoBase = Path.GetFileNameWithoutExtension(videoPath);
        if (!imageBase.Equals(videoBase, StringComparison.OrdinalIgnoreCase))
            return false;

        // Accept common extensions: mp4, mov (case-insensitive)
        var ext = Path.GetExtension(videoPath)?.ToLowerInvariant();
        if (ext != ".mp4" && ext != ".mov")
            return false;

        var imgInfo = new FileInfo(imagePath);
        var vidInfo = new FileInfo(videoPath);

        // Timestamp closeness (creation or last write)
        var imgTime = imgInfo.CreationTimeUtc != DateTime.MinValue ? imgInfo.CreationTimeUtc : imgInfo.LastWriteTimeUtc;
        var vidTime = vidInfo.CreationTimeUtc != DateTime.MinValue ? vidInfo.CreationTimeUtc : vidInfo.LastWriteTimeUtc;
        if ((imgTime - vidTime).Duration() > TimestampTolerance)
            return false;

        // Avoid obviously huge videos unrelated to live photo
        if (vidInfo.Length > MaxReasonableSizeBytes)
            return false;

        // Try to read video duration (in seconds). Use ShellFile property as an example.
        double durationSeconds = GetVideoDurationSeconds(videoPath);
        if (durationSeconds <= 0)
        {
            // If we can't read duration, fall back to more cautious rules:
            // prefer not to delete unless name+timestamp+json flag exist. For now return false.
            return false;
        }

        // Strong indicator: short video (<= MaxDurationSecondsLikelyLive)
        if (durationSeconds <= MaxDurationSecondsLikelyLive)
            return true;

        /*
        // Weak/possible indicator: slightly longer but still plausible
        if (durationSeconds <= MaxDurationSecondsMaybeLive)
        {
            // If video is <= 12s, check that video file size isn't huge relative to typical phone videos.
            // Accept if size is less than, say, 80 MB
            if (vidInfo.Length < 80L * 1024 * 1024)
                return true;
        }
        */

        // Otherwise it's likely not a live-photo video
        return false;
    }

    private static double GetVideoDurationSeconds(string videoPath)
    {
        try
        {
            using (var shell = ShellFile.FromFilePath(videoPath))
            {
                // System.Media.Duration is in 100-nanosecond units, per Shell property format
                var prop = shell.Properties.System.Media.Duration;
                if (prop != null && prop.Value != null)
                {
                    // prop.Value is a ulong ticks (100ns). Convert to seconds.
                    var ticks = Convert.ToInt64(prop.Value);
                    var seconds = ticks / 10_000_000.0;
                    return seconds;
                }
            }
        }
        catch
        {
            // ignore and return 0
        }
        return 0.0;
    }
}
