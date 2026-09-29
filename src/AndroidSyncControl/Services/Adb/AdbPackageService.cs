using System;
using System.IO;
using System.Threading.Tasks;
using AndroidSyncControl.Localization;

namespace AndroidSyncControl.Services.Adb
{
    /// <summary>
    /// Service for managing Android packages, APK installation, force-stopping, and clearing app state.
    /// </summary>
    public static class AdbPackageService
    {
        public static async Task ForceStopAsync(string deviceId, string packageName)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"shell am force-stop {packageName}");
        }

        public static async Task ClearDataAsync(string deviceId, string packageName)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"shell pm clear {packageName}");
        }

        public static async Task LaunchAppAsync(string deviceId, string packageName)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1 2>/dev/null");
        }

        public static async Task StopAtxAsync(string deviceId)
        {
            await ForceStopAsync(deviceId, "com.github.uiautomator");
            await ForceStopAsync(deviceId, "com.github.uiautomator.test");
        }

        public static async Task<(bool Success, string Message)> InstallApkDetailedAsync(
            string deviceId, string apkPath, Action<string>? onProgress = null)
        {
            if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath))
                return (false, "File APK không tồn tại");

            var fileInfo = new FileInfo(apkPath);
            double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            string fileName = fileInfo.Name;

            onProgress?.Invoke(string.Format(LanguageManager.GetString("Str.Status.InstallingApk"), fileName, sizeMb));

            string output = await AdbExecutor.RunAdbAsync(deviceId, $"install -r \"{apkPath}\"", 120000);

            if (output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                return (true, LanguageManager.GetString("Str.Status.InstallSuccess"));
            }

            // Extract failure message from adb output (e.g. Failure [INSTALL_FAILED_...])
            string error = "Lỗi không xác định";
            int failIdx = output.IndexOf("Failure", StringComparison.OrdinalIgnoreCase);
            if (failIdx >= 0)
            {
                error = output.Substring(failIdx).Trim();
                int newline = error.IndexOfAny(new[] { '\r', '\n' });
                if (newline > 0) error = error.Substring(0, newline);
            }
            else if (!string.IsNullOrWhiteSpace(output))
            {
                error = output.Trim();
            }

            return (false, error);
        }
    }
}
