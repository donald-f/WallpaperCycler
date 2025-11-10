using System;
using System.Windows.Forms;

namespace WallpaperCycler
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var main = new MainForm();
            Application.Run();
        }
    }
}