using System.Collections.Generic;
using System.Drawing;

namespace WallpaperCycler
{
    internal static class AppConstants
    {
        // Default background fill color
        public static readonly Color DefaultFillColor = ColorTranslator.FromHtml("#0b5fff");

        // Single source of truth for valid photo extensions
        public static readonly HashSet<string> ValidPhotoExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };

        // Temp/junk filename patterns to ignore in file watcher and scans
        public static readonly string[] IgnoredFilePatterns =
        {
            ".tmp",
            "~",
            ".~tmp",
            "~rf",
            "~mg",
        };

        // EXIF orientation property tag
        public const int ExifOrientationTag = 0x0112;

        // Date overlay rendering
        public const float DateOverlayFontSize = 28f;
        public const int DateOverlayMargin = 30;
        public const int DateOverlayTaskbarOffset = 80;
        public const int DateOverlayBackgroundAlpha = 160;
        public const int DateOverlayTextAlpha = 240;
    }
}
