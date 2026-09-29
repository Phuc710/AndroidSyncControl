using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AndroidSyncControl.Services.Adb;
using AndroidSyncControl.Services.Backup;
using AndroidSyncControl.Services.Bypass;
using AndroidSyncControl.Services.Device;

namespace AndroidSyncControl.UI.Helpers
{
    /// <summary>
    /// Static Facade class providing backward-compatible API access for UI components.
    /// Delegates internally to specialized modular services in the AndroidSyncControl.Services namespace.
    /// </summary>
    public static class ShopeeBypassService
    {
        private static readonly IShopeeBypassPipeline _pipeline = new ShopeeBypassPipeline();

        // ── ADB Core ────────────────────────────────────────────────────────
        public static Task<string> RunAdbAsync(string deviceId, string arguments, int timeoutMs = 10000)
            => AdbExecutor.RunAdbAsync(deviceId, arguments, timeoutMs);

        public static Task<bool> RestartAdbServerAsync()
            => AdbExecutor.RestartAdbServerAsync();

        // ── Device Identity & Telemetry ─────────────────────────────────────
        public static string GenerateRandomHex(int byteCount = 8)
            => ShopeeBypassPipeline.GenerateRandomHex(byteCount);

        public static Task<string> GetActiveDeviceAsync()
            => DeviceTelemetryService.GetActiveDeviceAsync();

        /// <summary>Returns ALL online ADB device serials for multi-device selection UI.</summary>
        public static Task<List<string>> GetAllConnectedDevicesAsync()
            => DeviceTelemetryService.GetAllConnectedDevicesAsync();

        public static Task<string> GetDeviceModelAsync(string deviceId)
            => DeviceTelemetryService.GetDeviceModelAsync(deviceId);

        public static Task<(int width, int height)> GetDeviceResolutionAsync(string deviceId)
            => DeviceTelemetryService.GetDeviceResolutionAsync(deviceId);

        public static Task<string> GetPhoneInfoAsync(string deviceId)
            => DeviceTelemetryService.GetPhoneInfoAsync(deviceId);

        // ── Network & Proxy ─────────────────────────────────────────────────
        public static Task SetProxyAsync(string deviceId, string hostAndPort)
            => DeviceNetworkService.SetProxyAsync(deviceId, hostAndPort);

        public static Task<string> CheckDeviceProxyAsync(string deviceId)
            => DeviceNetworkService.CheckDeviceProxyAsync(deviceId);

        public static Task<(bool Success, string Message)> TestDeviceNetworkAsync(string deviceId)
            => DeviceNetworkService.TestDeviceNetworkAsync(deviceId);

        public static Task RotateAirplaneModeAsync(string deviceId)
            => DeviceNetworkService.RotateAirplaneModeAsync(deviceId);

        // ── Bypass Pipeline ─────────────────────────────────────────────────
        public static Task<bool> BypassShopeeAsync(string deviceId, Action<string>? statusCallback = null)
            => _pipeline.ExecuteBypassAsync(deviceId, statusCallback);

        // ── Multi-Device Bypass ──────────────────────────────────────────────
        private static readonly MultiDeviceBypassOrchestrator _orchestrator = new MultiDeviceBypassOrchestrator();

        /// <summary>
        /// Runs Shopee bypass simultaneously on ALL currently connected ADB devices.
        /// Returns one DeviceBypassResult per device, ordered by discovery sequence.
        /// </summary>
        public static Task<IReadOnlyList<DeviceBypassResult>> BypassAllConnectedDevicesAsync(
            Action<string>? globalStatusCallback = null,
            System.Threading.CancellationToken ct = default)
            => _orchestrator.RunOnAllConnectedDevicesAsync(globalStatusCallback, ct);

        /// <summary>
        /// Runs Shopee bypass on a specific subset of device serials in parallel.
        /// Devices not currently connected are skipped silently.
        /// </summary>
        public static Task<IReadOnlyList<DeviceBypassResult>> BypassDeviceListAsync(
            IReadOnlyList<string> serials,
            Action<string>? globalStatusCallback = null,
            System.Threading.CancellationToken ct = default)
            => _orchestrator.RunOnDeviceListAsync(serials, globalStatusCallback, ct);

        public static Task OpenShopeeAsync(string deviceId)
            => AdbPackageService.LaunchAppAsync(deviceId, "com.shopee.vn");

        // ── Input & Clipboard ───────────────────────────────────────────────
        public static void SafeSetClipboard(string text)
            => AdbInputService.SafeSetClipboard(text);

        public static Task DirectClipboardPasteAsync(string deviceId, string text, Action? triggerScrcpyPaste = null)
            => AdbInputService.DirectClipboardPasteAsync(deviceId, text, triggerScrcpyPaste);

        public static Task SendTextToDeviceAsync(string deviceId, string text)
            => AdbInputService.DirectClipboardPasteAsync(deviceId, text);

        public static Task PasteTextAsync(string deviceId, string text)
            => AdbInputService.DirectClipboardPasteAsync(deviceId, text);

        public static Task SendKeyAsync(string deviceId, int keyCode)
            => AdbInputService.SendKeyAsync(deviceId, keyCode);

        public static Task VolumeUpAsync(string deviceId) => AdbInputService.VolumeUpAsync(deviceId);
        public static Task VolumeDownAsync(string deviceId) => AdbInputService.VolumeDownAsync(deviceId);
        public static Task MuteAsync(string deviceId) => AdbInputService.MuteAsync(deviceId);
        public static Task PowerAsync(string deviceId) => AdbInputService.PowerAsync(deviceId);
        public static Task RebootAsync(string deviceId) => AdbInputService.RebootAsync(deviceId);
        public static Task SwipeUpAsync(string deviceId) => AdbInputService.SwipeUpAsync(deviceId);
        public static Task SwipeDownAsync(string deviceId) => AdbInputService.SwipeDownAsync(deviceId);

        // ── Package & Media ─────────────────────────────────────────────────
        public static Task StopAtxAsync(string deviceId)
            => AdbPackageService.StopAtxAsync(deviceId);

        public static Task TakeScreenshotAsync(string deviceId, string saveDir)
            => AdbMediaService.TakeScreenshotAsync(deviceId, saveDir);

        public static Task InstallApkAsync(string deviceId, string apkPath)
            => AdbPackageService.InstallApkDetailedAsync(deviceId, apkPath);

        public static Task<(bool Success, string Message)> InstallApkDetailedAsync(
            string deviceId, string apkPath, Action<string>? onProgress = null)
            => AdbPackageService.InstallApkDetailedAsync(deviceId, apkPath, onProgress);

        // ── Backup & Restore ────────────────────────────────────────────────
        // Type alias for backward compatibility with dialogs
        public class ShopeeBackupInfo : AndroidSyncControl.Services.Backup.ShopeeBackupInfo { }

        public static string GetDefaultBackupDir()
            => ShopeeBackupService.GetDefaultBackupDir();

        public static List<AndroidSyncControl.Services.Backup.ShopeeBackupInfo> GetBackupList()
            => ShopeeBackupService.GetBackupList();

        public static Task<(bool Success, AndroidSyncControl.Services.Backup.ShopeeBackupInfo? Info, string Error)> BackupShopeeDataAsync(
            string deviceId, Action<string>? onProgress = null)
            => ShopeeBackupService.BackupShopeeDataAsync(deviceId, onProgress);

        public static Task<(bool Success, string Message)> RestoreShopeeDataAsync(
            string deviceId, AndroidSyncControl.Services.Backup.ShopeeBackupInfo backup, Action<string>? onProgress = null)
            => ShopeeBackupService.RestoreShopeeDataAsync(deviceId, backup, onProgress);
    }
}
