using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AndroidSyncControl.Infrastructure;
using AndroidSyncControl.Update;

namespace AndroidSyncControl
{
    public partial class App : Application
    {
        public static UpdateService UpdateService { get; } = new();

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.AppendAllText(AppPaths.GetLogFilePath("crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] DispatcherUnhandledException: {e.Exception}\r\n");
            }
            catch { }
            e.Handled = true;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool ok = ApplicationBootstrapper.Run(e.Args);

            if (ApplicationBootstrapper.IsHealthCheckMode)
            {
                // Health check verification mode requested by updater
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            base.OnStartup(e);

            // Trigger non-blocking background update check after UI is up
            _ = Task.Run(async () =>
            {
                try
                {
                    // Delay 3 seconds after startup to keep startup time ultra-fast
                    await Task.Delay(3000);

                    if (!Singleton.Setting.Setting.AutoCheckUpdate) return;

                    string manifestUrl = Singleton.Setting.Setting.UpdateManifestUrl;
                    if (string.IsNullOrWhiteSpace(manifestUrl))
                    {
                        manifestUrl = "https://raw.githubusercontent.com/Phuc710/AndroidSyncControl/main/release/update-manifest.json";
                    }

                    var manifest = await UpdateService.CheckForUpdateAsync(manifestUrl);
                    if (manifest != null)
                    {
                        await Current.Dispatcher.InvokeAsync(() =>
                        {
                            var mainWindow = Current.MainWindow;
                            if (mainWindow != null && mainWindow.IsVisible)
                            {
                                var dlg = new UI.UpdateDialog(manifest, UpdateService)
                                {
                                    Owner = mainWindow
                                };
                                dlg.ShowDialog();
                            }
                        });
                    }
                }
                catch { }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                File.AppendAllText(AppPaths.GetLogFilePath("app.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [App] Application.OnExit fired! ExitCode={e.ApplicationExitCode}\r\n");
            }
            catch { }
            base.OnExit(e);
        }
    }
}
