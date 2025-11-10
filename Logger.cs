using System;
using System.IO;
using System.Text;

namespace WallpaperCycler
{
    public static class Logger
    {
        private static readonly string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallcycler.log");
        private static readonly long maxBytes = 1_000_000; // 1 MB
        private static object _lock = new object();

        public static void Init()
        {
            try
            {
                if (!Directory.Exists(AppDomain.CurrentDomain.BaseDirectory)) Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory);
                // ensure file exists
                if (!File.Exists(logPath)) File.WriteAllText(logPath, "");
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    var entry = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
                    File.AppendAllText(logPath, entry, Encoding.UTF8);

                    var fi = new FileInfo(logPath);
                    if (fi.Length > maxBytes)
                    {
                        // trim to keep the newest content: keep last ~900k bytes
                        const int keep = 900_000;
                        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        if (fs.Length > keep)
                        {
                            fs.Seek(-keep, SeekOrigin.End);
                            var buffer = new byte[keep];
                            var read = fs.Read(buffer, 0, keep);
                            fs.SetLength(0);
                            fs.Seek(0, SeekOrigin.Begin);
                            fs.Write(buffer, 0, read);
                        }
                    }
                }
            }
            catch { }
        }
    }
}