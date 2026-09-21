using System;
using System.IO;

namespace AndroidSyncControl.UI.Helpers
{
    public static class AndroidToolchain
    {
        private static string _cachedAdb = string.Empty;
        private static string _cachedScrcpy = string.Empty;
        private static readonly object _lock = new object();

        public static string AdbPath
        {
            get
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_cachedAdb) || !File.Exists(_cachedAdb))
                    {
                        _cachedAdb = ResolveAdb();
                    }
                    return _cachedAdb;
                }
            }
        }

        public static string ScrcpyPath
        {
            get
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_cachedScrcpy) || !File.Exists(_cachedScrcpy))
                    {
                        _cachedScrcpy = ResolveScrcpy();
                    }
                    return _cachedScrcpy;
                }
            }
        }

        private static string ResolveAdb()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var dir = new DirectoryInfo(baseDir);

            while (dir != null)
            {
                string target = Path.Combine(dir.FullName, "tools", "android", "adb", "adb.exe");
                if (File.Exists(target)) return Path.GetFullPath(target);

                dir = dir.Parent;
            }

            // Local fallback
            string localAdb = Path.Combine(baseDir, "adb.exe");
            if (File.Exists(localAdb)) return localAdb;

            return "adb.exe";
        }

        private static string ResolveScrcpy()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var dir = new DirectoryInfo(baseDir);

            while (dir != null)
            {
                string target = Path.Combine(dir.FullName, "tools", "android", "scrcpy", "scrcpy.exe");
                if (File.Exists(target)) return Path.GetFullPath(target);

                dir = dir.Parent;
            }

            // Local fallback
            string localScrcpy = Path.Combine(baseDir, "scrcpy.exe");
            if (File.Exists(localScrcpy)) return localScrcpy;

            return "scrcpy.exe";
        }
    }
}
