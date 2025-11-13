using Microsoft.Win32;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GeoTimeZone;

namespace WallpaperCycler
{
    public class WallpaperService
    {
        private readonly string tempFile = Path.Combine(Path.GetTempPath(), "wallcycler_current.bmp");

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
                    {
                        DrawDateOverlay(g, dateTaken.Value, canvas.Width, canvas.Height);
                    }
                }
            }

            canvas.Save(tempFile, ImageFormat.Bmp);
            SetWallpaper(tempFile);
        }


        private void RotateImageIfNeeded(Image img)
        {
            const int ExifOrientationId = 0x0112; // Property tag for orientation
            if (!img.PropertyIdList.Contains(ExifOrientationId))
                return;

            try
            {
                var prop = img.GetPropertyItem(ExifOrientationId);
                int val = BitConverter.ToUInt16(prop.Value, 0);

                switch (val)
                {
                    case 1: // normal
                        break;
                    case 2: // flip horizontal
                        img.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        break;
                    case 3: // 180
                        img.RotateFlip(RotateFlipType.Rotate180FlipNone);
                        break;
                    case 4: // flip vertical
                        img.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        break;
                    case 5: // transpose
                        img.RotateFlip(RotateFlipType.Rotate90FlipX);
                        break;
                    case 6: // rotate 90
                        img.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        break;
                    case 7: // transverse
                        img.RotateFlip(RotateFlipType.Rotate270FlipX);
                        break;
                    case 8: // rotate 270
                        img.RotateFlip(RotateFlipType.Rotate270FlipNone);
                        break;
                }

                // Remove the EXIF orientation tag so it won’t be reapplied or cause confusion later
                img.RemovePropertyItem(ExifOrientationId);
            }
            catch
            {
                // ignore if property missing or unreadable
            }
        }

        public void SetSolidColorBackground(Color color)
        {
            // Change the Windows desktop background color
            SetBackgroundColor(color);

            // Remove any existing wallpaper by setting it to an empty string
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, "", SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }

        private void SetBackgroundColor(Color color)
        {
            string rgbValue = $"{color.R} {color.G} {color.B}";

            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors", true))
            {
                key?.SetValue("Background", rgbValue);
            }

            // Notify Windows to update background color
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null!, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }

        private Rectangle GetCenteredRect(int imgW, int imgH, int canvasW, int canvasH)
        {
            double scale = Math.Min((double)canvasW / imgW, (double)canvasH / imgH);
            int w = (int)(imgW * scale);
            int h = (int)(imgH * scale);
            int x = (canvasW - w) / 2;
            int y = (canvasH - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDWININICHANGE = 0x02;

        private void SetWallpaper(string path)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }

        private void DrawDateOverlay(Graphics g, DateTime dt, int width, int height)
        {
            string text = dt.ToString("MMMM d, yyyy");
            using var font = new Font("Segoe UI", 28, FontStyle.Bold);
            var size = g.MeasureString(text, font);

            const int margin = 30;
            const int taskbarOffset = 80; // ← Move text up this much

            var rect = new RectangleF(
                width - size.Width - margin,
                height - size.Height - margin - taskbarOffset,
                size.Width + 20,
                size.Height + 10
            );

            using var bgBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            g.FillRectangle(bgBrush, rect);
            using var textBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
            g.DrawString(text, font, textBrush, rect.Left + 10, rect.Top + 5);
        }


    }
}