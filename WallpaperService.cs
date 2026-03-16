using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WallpaperCycler
{
    public class WallpaperService
    {
        private readonly string _tempFile =
            Path.Combine(Path.GetTempPath(), "wallcycler_current.bmp");

        public void SetWallpaperWithBackground(string imagePath, Color bgColor, bool showDate = false)
        {
            var vs = SystemInformation.VirtualScreen;
            using var canvas = new Bitmap(vs.Width, vs.Height);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(bgColor);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                using var img = Image.FromFile(imagePath);
                RotateImageIfNeeded(img);
                var targetRect = GetCenteredRect(img.Width, img.Height, vs.Width, vs.Height);
                g.DrawImage(img, targetRect);

                if (showDate)
                {
                    var dateTaken = PhotoMetadataHelper.GetLocalizedDateTaken(imagePath);
                    if (dateTaken != null)
                        DrawDateOverlay(g, dateTaken.Value, canvas.Width, canvas.Height);
                }
            }

            canvas.Save(_tempFile, ImageFormat.Bmp);
            ApplyWallpaper(_tempFile);
        }

        public void SetSolidColorBackground(Color color)
        {
            SetBackgroundColor(color);
            // Pass empty string to clear any existing wallpaper image
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, string.Empty, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static void RotateImageIfNeeded(Image img)
        {
            if (!img.PropertyIdList.Contains(AppConstants.ExifOrientationTag))
                return;

            try
            {
                var prop = img.GetPropertyItem(AppConstants.ExifOrientationTag);
                int val = BitConverter.ToUInt16(prop!.Value!, 0);

                switch (val)
                {
                    case 2: img.RotateFlip(RotateFlipType.RotateNoneFlipX);   break;
                    case 3: img.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 4: img.RotateFlip(RotateFlipType.RotateNoneFlipY);   break;
                    case 5: img.RotateFlip(RotateFlipType.Rotate90FlipX);     break;
                    case 6: img.RotateFlip(RotateFlipType.Rotate90FlipNone);  break;
                    case 7: img.RotateFlip(RotateFlipType.Rotate270FlipX);    break;
                    case 8: img.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                    // case 1 = normal — no rotation needed
                }

                img.RemovePropertyItem(AppConstants.ExifOrientationTag);
            }
            catch (Exception ex)
            {
                Logger.Log($"RotateImageIfNeeded failed: {ex.Message}");
            }
        }

        private static Rectangle GetCenteredRect(int imgW, int imgH, int canvasW, int canvasH)
        {
            double scale = Math.Min((double)canvasW / imgW, (double)canvasH / imgH);
            int w = (int)(imgW * scale);
            int h = (int)(imgH * scale);
            int x = (canvasW - w) / 2;
            int y = (canvasH - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        private static void DrawDateOverlay(Graphics g, DateTime dt, int width, int height)
        {
            string text = dt.ToString("MMMM d, yyyy");
            using var font = new Font("Segoe UI", AppConstants.DateOverlayFontSize, FontStyle.Bold);
            var size = g.MeasureString(text, font);

            var rect = new RectangleF(
                width  - size.Width  - AppConstants.DateOverlayMargin,
                height - size.Height - AppConstants.DateOverlayMargin - AppConstants.DateOverlayTaskbarOffset,
                size.Width  + 20,
                size.Height + 10
            );

            using var bgBrush   = new SolidBrush(Color.FromArgb(AppConstants.DateOverlayBackgroundAlpha, 0, 0, 0));
            using var textBrush = new SolidBrush(Color.FromArgb(AppConstants.DateOverlayTextAlpha, 255, 255, 255));
            g.FillRectangle(bgBrush, rect);
            g.DrawString(text, font, textBrush, rect.Left + 10, rect.Top + 5);
        }

        private static void SetBackgroundColor(Color color)
        {
            string rgb = $"{color.R} {color.G} {color.B}";
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors", writable: true);
            key?.SetValue("Background", rgb);
        }

        private static void ApplyWallpaper(string path)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE   = 0x01;
        private const int SPIF_SENDWININICHANGE = 0x02;
    }
}
