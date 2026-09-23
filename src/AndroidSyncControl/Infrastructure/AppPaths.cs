using System;
using System.IO;

namespace AndroidSyncControl.Infrastructure
{
    /// <summary>
    /// Centralized paths management enforcing strict 3-tier data isolation:
    /// 1. Application Binaries: Read-only Program Files (or portable app folder).
    /// 2. User Data: %LOCALAPPDATA%\AndroidSyncControl\ (config, logs, cache, updates).
    /// 3. Agent Knowledge: %LOCALAPPDATA%\AndroidSyncControl\agent-data\ (playbooks, experiences).
    /// </summary>
    public static class AppPaths
    {
        public const string AppName = "AndroidSyncControl";

        private static readonly Lazy<string> _appDir = new(() =>
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));

        private static readonly Lazy<string> _dataDir = new(() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName));

        public static string AppDir => _appDir.Value;

        // ── Single Source of Truth for Repository & OTA Manifest ──────────
        public const string DefaultRepoSlug = "Phuc710/AndroidSyncControl";
        public const string DefaultManifestUrl = "https://raw.githubusercontent.com/" + DefaultRepoSlug + "/main/release/update-manifest.json";

        public static string RuntimeDir => Path.Combine(AppDir, "Runtime");
        public static string InstallMetadataFile => Path.Combine(AppDir, "install.json");
        public static string UpdaterExePath
        {
            get
            {
                string primary = Path.Combine(AppDir, "AndroidSyncControl.Updater.exe");
                if (File.Exists(primary)) return primary;

                // Look in sibling/dev directories
                string[] candidates = new[]
                {
                    Path.Combine(AppDir, "..", "..", "..", "AndroidSyncControl.Updater", "bin", "Release", "net8.0-windows", "win-x64", "AndroidSyncControl.Updater.exe"),
                    Path.Combine(AppDir, "..", "..", "..", "AndroidSyncControl.Updater", "bin", "Release", "net8.0-windows", "AndroidSyncControl.Updater.exe"),
                    Path.Combine(AppDir, "..", "..", "..", "AndroidSyncControl.Updater", "bin", "x64", "Release", "net8.0-windows", "win-x64", "AndroidSyncControl.Updater.exe"),
                    Path.Combine(AppDir, "..", "..", "..", "AndroidSyncControl.Updater", "bin", "x64", "Debug", "net8.0-windows", "win-x64", "AndroidSyncControl.Updater.exe"),
                    Path.Combine(AppDir, "..", "..", "..", "AndroidSyncControl.Updater", "bin", "Debug", "net8.0-windows", "AndroidSyncControl.Updater.exe")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }

                // Dynamically find any staged updater in dist/ folder without hardcoded version
                try
                {
                    string distDir = Path.GetFullPath(Path.Combine(AppDir, "..", "..", "dist"));
                    if (Directory.Exists(distDir))
                    {
                        foreach (var subDir in Directory.GetDirectories(distDir, "AndroidSyncControl-*-win-x64"))
                        {
                            string updaterInDist = Path.Combine(subDir, "AndroidSyncControl.Updater.exe");
                            if (File.Exists(updaterInDist)) return Path.GetFullPath(updaterInDist);
                        }
                    }
                }
                catch { }

                return primary;
            }
        }

        // ── User Mutable Data (%LOCALAPPDATA%\AndroidSyncControl\) ──────────
        public static string DataDir => _dataDir.Value;
        public static string ConfigDir => Path.Combine(DataDir, "config");
        public static string LogsDir => Path.Combine(DataDir, "logs");
        public static string CacheDir => Path.Combine(DataDir, "cache");
        public static string UpdatesDir => Path.Combine(DataDir, "updates");

        // ── Agent Persistent Knowledge (%LOCALAPPDATA%\AndroidSyncControl\agent-data\) ──
        public static string AgentDataDir => Path.Combine(DataDir, "agent-data");
        public static string AgentPlaybooksDir => Path.Combine(AgentDataDir, "playbooks");
        public static string AgentExperiencesDir => Path.Combine(AgentDataDir, "experiences");
        public static string AgentLessonsDir => Path.Combine(AgentDataDir, "lessons");
        public static string AgentEvaluationsDir => Path.Combine(AgentDataDir, "evaluations");

        /// <summary>
        /// Ensures all mutable data directories exist in %LOCALAPPDATA%.
        /// Must be called during ApplicationBootstrapper before any file I/O.
        /// </summary>
        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(CacheDir);
            Directory.CreateDirectory(UpdatesDir);

            Directory.CreateDirectory(AgentDataDir);
            Directory.CreateDirectory(AgentPlaybooksDir);
            Directory.CreateDirectory(AgentExperiencesDir);
            Directory.CreateDirectory(AgentLessonsDir);
            Directory.CreateDirectory(AgentEvaluationsDir);
        }

        /// <summary>
        /// Log file path in %LOCALAPPDATA%\AndroidSyncControl\logs\
        /// </summary>
        public static string GetLogFilePath(string logName = "app.log")
        {
            return Path.Combine(LogsDir, logName);
        }
    }
}
