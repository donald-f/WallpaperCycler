using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperCycler
{
    public class WallpaperService
    {
        private readonly string tempFile = Path.Combine(Path.GetTempPath(), "wallcycler_current.bmp");

        public void SetWallpaperWithBackground(string imagePath, Color bgColor)
        {
            var vs = SystemInformation.VirtualScreen;
            using var canvas = new Bitmap(vs.Width, vs.Height);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(bgColor);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                using var img = Image.FromFile(imagePath);
                // compute scaled size to fit without stretching
                var targetRect = GetCenteredRect(img.Width, img.Height, vs.Width, vs.Height);
                g.DrawImage(img, targetRect);
            }

            // save as BMP (Windows-friendly)
            canvas.Save(tempFile, ImageFormat.Bmp);

            // set as wallpaper
            SetWallpaper(tempFile);
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
    }
}