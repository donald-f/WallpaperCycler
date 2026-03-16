using System;
using System.IO;
using System.Text;

namespace WallpaperCycler
{
    public static class Logger
    {
        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallcycler.log");

        private static readonly long MaxBytes = 1_000_000; // 1 MB
        private static readonly object Lock = new object();

        public static void Init()
        {
            try
            {
                if (!Directory.Exists(AppDomain.CurrentDomain.BaseDirectory))
                    Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory);

                if (!File.Exists(LogPath))
                    File.WriteAllText(LogPath, string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger.Init failed: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    var entry = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, entry, Encoding.UTF8);

                    var fi = new FileInfo(LogPath);
                    if (fi.Length > MaxBytes)
                        TrimLog();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger.Log failed: {ex.Message}");
            }
        }

        // Keeps the newest ~900 KB of the log file.
        private static void TrimLog()
        {
            const int keep = 900_000;
            try
            {
                using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (fs.Length <= keep) return;

                fs.Seek(-keep, SeekOrigin.End);
                var buffer = new byte[keep];
                int read = fs.Read(buffer, 0, keep);
                fs.SetLength(0);
                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(buffer, 0, read);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger.TrimLog failed: {ex.Message}");
            }
        }
    }
}
