using System;
using System.Threading;
using System.Windows.Forms;

namespace WallpaperCycler
{
    internal static class Program
    {
        private static Mutex? mutex;

        [STAThread]
        static void Main()
        {
            const string mutexName = "WallpaperCycler_SingleInstanceMutex";

            bool createdNew;
            mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                MessageBox.Show("WallpaperCycler is already running.", "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Logger.Init();
            Logger.Log("Application starting");

            var main = new MainForm();
            Application.Run();

            Logger.Log("Application exiting");

            // Release mutex when app exits
            mutex.ReleaseMutex();
        }
    }
}
