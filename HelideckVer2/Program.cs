using HelideckVer2.Services;
using System.Threading;
using System.Windows.Forms;

namespace HelideckVer2
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Catch unhandled exceptions on the UI thread — log and self-restart instead of showing
            // Windows error dialog and leaving the helideck display blank.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                SystemLogger.LogError("[App] Unhandled UI-thread exception — restarting", e.Exception);
                RestartApp();
            };

            // Catch unhandled exceptions on background/threadpool threads
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    SystemLogger.LogError("[App] Unhandled background exception — restarting", ex);
                else
                    SystemLogger.LogInfo($"[App] Unhandled background exception: {e.ExceptionObject}");
                if (e.IsTerminating)
                    RestartApp();
            };

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        /// <summary>
        /// Launch a fresh instance of this application then exit the current process.
        /// Called by the application watchdog and the unhandled-exception handlers so the
        /// helideck display is never left blank after a crash or freeze.
        /// </summary>
        internal static void RestartApp()
        {
            try
            {
                string exe = Environment.ProcessPath
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                System.Diagnostics.Process.Start(exe);
            }
            catch { }
            Thread.Sleep(500); // give new process time to initialise before we exit
            Environment.Exit(1);
        }
    }
}