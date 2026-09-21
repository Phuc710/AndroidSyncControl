using System;
using System.IO;
using System.Windows;
using AndroidSyncControl.Localization;
using AndroidSyncControl.Themes;

namespace AndroidSyncControl
{
    public partial class App : Application
    {
        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] DispatcherUnhandledException: {e.Exception}\r\n");
            }
            catch { }
            e.Handled = true;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_scrcpy.log"),
                        $"[{DateTime.Now:HH:mm:ss.fff}] [App] UnhandledException: {ev.ExceptionObject}\r\n");
                }
                catch { }
            };

            AppDomain.CurrentDomain.ProcessExit += (s, ev) =>
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_scrcpy.log"),
                        $"[{DateTime.Now:HH:mm:ss.fff}] [App] ProcessExit triggered!\r\n");
                }
                catch { }
            };

            try
            {
                ThemeManager.Apply(ThemeManager.Parse(Singleton.Setting.Setting.Theme));
                LanguageManager.Apply(LanguageManager.Parse(Singleton.Setting.Setting.Language));
            }
            catch { }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_scrcpy.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] [App] Application.OnExit fired! ExitCode={e.ApplicationExitCode}\r\n");
            }
            catch { }
            base.OnExit(e);
        }
    }
}
