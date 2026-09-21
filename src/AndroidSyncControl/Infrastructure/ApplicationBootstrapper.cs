using System;
using System.IO;
using System.Windows;
using AndroidSyncControl.Localization;
using AndroidSyncControl.Themes;
using AndroidSyncControl.Update;

namespace AndroidSyncControl.Infrastructure
{
    public static class ApplicationBootstrapper
    {
        public static bool IsHealthCheckMode { get; private set; }

        public static bool Run(string[] args)
        {
            // Check for --health-check mode from updater
            foreach (var arg in args)
            {
                if (string.Equals(arg, "--health-check", StringComparison.OrdinalIgnoreCase))
                {
                    IsHealthCheckMode = true;
                    break;
                }
            }

            InitializePaths();
            InitializeLogging();

            bool isValid = ValidateInstallation();
            if (IsHealthCheckMode)
            {
                // In health check mode: return validation result and don't load UI
                return isValid;
            }

            InitializeConfiguration();
            return true;
        }

        public static void InitializePaths()
        {
            try
            {
                AppPaths.EnsureDirectories();
            }
            catch (Exception ex)
            {
                // Fallback attempt
                System.Diagnostics.Debug.WriteLine($"[Bootstrapper] EnsureDirectories error: {ex.Message}");
            }
        }

        public static void InitializeLogging()
        {
            string crashLog = AppPaths.GetLogFilePath("crash.log");
            string appLog = AppPaths.GetLogFilePath("app.log");

            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try
                {
                    File.AppendAllText(crashLog,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [App] UnhandledException: {ev.ExceptionObject}\r\n");
                }
                catch { }
            };

            AppDomain.CurrentDomain.ProcessExit += (s, ev) =>
            {
                try
                {
                    File.AppendAllText(appLog,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [App] ProcessExit triggered\r\n");
                }
                catch { }
            };
        }

        public static bool ValidateInstallation()
        {
            try
            {
                // Verify core app directory exists
                if (!Directory.Exists(AppPaths.AppDir)) return false;

                // Ensure data directories in %LOCALAPPDATA% are writable
                string probeFile = Path.Combine(AppPaths.DataDir, ".probe");
                File.WriteAllText(probeFile, "ok");
                File.Delete(probeFile);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void InitializeConfiguration()
        {
            try
            {
                ThemeManager.Apply(ThemeManager.Parse(Singleton.Setting.Setting.Theme));
                LanguageManager.Apply(LanguageManager.Parse(Singleton.Setting.Setting.Language));
            }
            catch { }
        }
    }
}
