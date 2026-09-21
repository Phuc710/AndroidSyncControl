using System;
using System.IO;
using AndroidSyncControl.Infrastructure;

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
            // 1. Production Runtime layout: <AppDir>/Runtime/adb/adb.exe or <AppDir>/Runtime/adb.exe
            string runtimeSub = Path.Combine(AppPaths.RuntimeDir, "adb", "adb.exe");
            if (File.Exists(runtimeSub)) return Path.GetFullPath(runtimeSub);

            string runtimeFlat = Path.Combine(AppPaths.RuntimeDir, "adb.exe");
            if (File.Exists(runtimeFlat)) return Path.GetFullPath(runtimeFlat);

            // 2. Base directory layout
            string appDirAdb = Path.Combine(AppPaths.AppDir, "adb.exe");
            if (File.Exists(appDirAdb)) return Path.GetFullPath(appDirAdb);

            // 3. Dev repo structure: walk up to find tools/android/adb/adb.exe
            var dir = new DirectoryInfo(AppPaths.AppDir);
            while (dir != null)
            {
                string devTarget = Path.Combine(dir.FullName, "tools", "android", "adb", "adb.exe");
                if (File.Exists(devTarget)) return Path.GetFullPath(devTarget);

                dir = dir.Parent;
            }

            return "adb.exe";
        }

        private static string ResolveScrcpy()
        {
            // 1. Production Runtime layout: <AppDir>/Runtime/scrcpy/scrcpy.exe or <AppDir>/Runtime/scrcpy.exe
            string runtimeSub = Path.Combine(AppPaths.RuntimeDir, "scrcpy", "scrcpy.exe");
            if (File.Exists(runtimeSub)) return Path.GetFullPath(runtimeSub);

            string runtimeFlat = Path.Combine(AppPaths.RuntimeDir, "scrcpy.exe");
            if (File.Exists(runtimeFlat)) return Path.GetFullPath(runtimeFlat);

            // 2. Base directory layout
            string appDirScrcpy = Path.Combine(AppPaths.AppDir, "scrcpy.exe");
            if (File.Exists(appDirScrcpy)) return Path.GetFullPath(appDirScrcpy);

            // 3. Dev repo structure: walk up to find tools/android/scrcpy/scrcpy.exe
            var dir = new DirectoryInfo(AppPaths.AppDir);
            while (dir != null)
            {
                string devTarget = Path.Combine(dir.FullName, "tools", "android", "scrcpy", "scrcpy.exe");
                if (File.Exists(devTarget)) return Path.GetFullPath(devTarget);

                dir = dir.Parent;
            }

            return "scrcpy.exe";
        }
    }
}
