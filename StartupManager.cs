using System;
using System.IO;
using System.Reflection;

namespace WallpaperCycler
{
    internal static class StartupManager
    {
        private static readonly string StartupFolder =
            Environment.GetFolderPath(Environment.SpecialFolder.Startup);

        private static readonly string ShortcutPath =
            Path.Combine(StartupFolder, "WallpaperCycler.lnk");

        public static void SetAutostart(bool enable)
        {
            try
            {
                if (enable)
                {
                    CreateShortcut();
                }
                else
                {
                    if (File.Exists(ShortcutPath))
                        File.Delete(ShortcutPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"StartupManager: {ex}");
            }
        }

        public static bool IsAutostartEnabled() => File.Exists(ShortcutPath);

        private static void CreateShortcut()
        {
            // Uses PowerShell to create the shortcut — avoids COM reference.
            string exePath = Assembly.GetEntryAssembly()!.Location;
            string ps = $@"
$WshShell = New-Object -ComObject WScript.Shell;
$Shortcut = $WshShell.CreateShortcut('{ShortcutPath}');
$Shortcut.TargetPath = '{exePath}';
$Shortcut.WorkingDirectory = '{Path.GetDirectoryName(exePath)}';
$Shortcut.Save();
";
            var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi)!.WaitForExit();
        }
    }
}
