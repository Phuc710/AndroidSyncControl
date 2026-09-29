using System;
using System.IO;
using System.Threading.Tasks;

namespace AndroidSyncControl.Services.Adb
{
    /// <summary>
    /// Service for media operations (screencap, file pull/push, storage cleanup).
    /// </summary>
    public static class AdbMediaService
    {
        public static async Task TakeScreenshotAsync(string deviceId, string saveDir)
        {
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
            string fileName = $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string localPath = Path.Combine(saveDir, fileName);
            string remotePath = $"/sdcard/{fileName}";

            await AdbExecutor.RunAdbAsync(deviceId, $"shell screencap -p {remotePath}");
            await AdbExecutor.RunAdbAsync(deviceId, $"pull {remotePath} \"{localPath}\"");
            await AdbExecutor.RunAdbAsync(deviceId, $"shell rm {remotePath}");
        }

        public static async Task PullAsync(string deviceId, string remotePath, string localPath, int timeoutMs = 60000)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"pull \"{remotePath}\" \"{localPath}\"", timeoutMs);
        }

        public static async Task PushAsync(string deviceId, string localPath, string remotePath, int timeoutMs = 60000)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"push \"{localPath}\" \"{remotePath}\"", timeoutMs);
        }

        public static async Task RemoveRemoteAsync(string deviceId, string remotePath)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"shell rm -rf {remotePath} 2>/dev/null");
        }
    }
}
