using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperCycler
{
    public class RapidPanelForm : Form
    {
        private readonly MainForm main;
        private readonly Button btnPrev;
        private readonly Button btnNext;
        private readonly Button btnDelete;
        private readonly Button btnExplorer;
        private readonly Button btnLocation;

        public RapidPanelForm(MainForm main)
        {
            this.main = main ?? throw new ArgumentNullException(nameof(main));

            Text = "Rapid Photo Controls";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            Width = 300;
            Height = 260;

            btnPrev = new Button { Text = "Previous", Width = 250, Top = 10, Left = 10 };
            btnNext = new Button { Text = "Next", Width = 250, Top = 50, Left = 10 };
            btnDelete = new Button { Text = "Delete", Width = 250, Top = 90, Left = 10 };
            btnExplorer = new Button { Text = "Show In Explorer", Width = 250, Top = 130, Left = 10 };
            btnLocation = new Button { Text = "View Location", Width = 250, Top = 170, Left = 10 };

            btnPrev.Click += (s, e) => InvokeMainAction("OnPrevious");
            btnNext.Click += (s, e) => InvokeMainAction("OnNext");
            btnDelete.Click += (s, e) => InvokeMainAction("OnDelete");
            btnExplorer.Click += (s, e) => InvokeMainAction("OnShowInExplorer");
            btnLocation.Click += (s, e) => InvokeMainAction("OnViewLocation");

            Controls.Add(btnPrev);
            Controls.Add(btnNext);
            Controls.Add(btnDelete);
            Controls.Add(btnExplorer);
            Controls.Add(btnLocation);

            // Refresh button states when the panel is shown or activated
            this.Shown += (s, e) => RefreshButtonStates();
            this.Activated += (s, e) => RefreshButtonStates();
        }

        private void InvokeMainAction(string methodName)
        {
            // Call the private MainForm method safely (on MainForm's thread if needed)
            void Call()
            {
                var mi = main.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
                mi?.Invoke(main, new object?[] { null, EventArgs.Empty });
            }

            // Use BeginInvoke on the main form if it requires marshaling; otherwise call directly.
            try
            {
                if (main.IsHandleCreated && main.InvokeRequired)
                {
                    main.BeginInvoke((Action)Call);
                }
                else
                {
                    Call();
                }
            }
            catch
            {
                // If invoking fails for any reason, attempt a direct call as a fallback.
                try { Call(); } catch { /* swallow to avoid crashing the panel */ }
            }

            // Give the main form a moment to update its state, then refresh the panel buttons.
            // Use a short Task delay so the UI has time to update currentPath / DB state.
            Task.Delay(150).ContinueWith(_ => {
                if (!this.IsDisposed && !this.Disposing)
                    this.BeginInvoke((Action)RefreshButtonStates);
            }, TaskScheduler.Default);
        }

        private void RefreshButtonStates()
        {
            try
            {
                // Query the main form for whether View Location is available.
                bool canViewLocation = false;
                try
                {
                    canViewLocation = main.IsViewLocationAvailable();
                }
                catch
                {
                    canViewLocation = false;
                }

                btnLocation.Enabled = canViewLocation;

                // Also enable/disable explorer button based on whether a current path exists.
                bool canShowExplorer = false;
                try
                {
                    // Use the same pattern: call a small public check on main if you have one,
                    // otherwise check via reflection in a safe manner.
                    var prop = main.GetType().GetProperty("currentPath", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var val = prop.GetValue(main) as string;
                        canShowExplorer = !string.IsNullOrEmpty(val) && System.IO.File.Exists(val);
                    }
                }
                catch
                {
                    canShowExplorer = false;
                }

                btnExplorer.Enabled = true; //canShowExplorer;
            }
            catch
            {
                // ignore any refresh errors to keep the panel stable
            }
        }
    }
}