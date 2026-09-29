using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AndroidSyncControl.Localization;
using AndroidSyncControl.Services.Adb;

namespace AndroidSyncControl.Services.Backup
{
    /// <summary>
    /// Service for backing up and restoring Shopee account sessions and storage caches.
    /// Manages /sdcard/Android/data/com.shopee.vn, /sdcard/.shopee, and Android ID.
    /// </summary>
    public static class ShopeeBackupService
    {
        public static string GetDefaultBackupDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static List<ShopeeBackupInfo> GetBackupList()
        {
            var list = new List<ShopeeBackupInfo>();
            string root = GetDefaultBackupDir();
            if (!Directory.Exists(root)) return list;

            foreach (var sub in Directory.GetDirectories(root))
            {
                string metaFile = Path.Combine(sub, "backup_meta.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        string json = File.ReadAllText(metaFile, Encoding.UTF8);
                        var info = JsonSerializer.Deserialize<ShopeeBackupInfo>(json);
                        if (info != null)
                        {
                            info.DirectoryPath = sub;
                            list.Add(info);
                        }
                    }
                    catch { }
                }
            }
            return list.OrderByDescending(x => x.CreatedAt).ToList();
        }

        public static async Task<(bool Success, ShopeeBackupInfo? Info, string Error)> BackupShopeeDataAsync(
            string deviceId, Action<string>? onProgress = null)
        {
            try
            {
                onProgress?.Invoke("[1/4] Đang lấy cấu hình thiết bị...");
                string model = (await AdbExecutor.RunAdbAsync(deviceId, "shell getprop ro.product.model", 3000)).Trim();
                string ssaid = (await AdbExecutor.RunAdbAsync(deviceId, "shell settings get secure android_id", 3000)).Trim();
                if (string.IsNullOrEmpty(model)) model = deviceId;

                string backupDir = Path.Combine(GetDefaultBackupDir(), $"Shopee_{DateTime.Now:yyyyMMdd_HHmmss}_{deviceId}");
                Directory.CreateDirectory(backupDir);

                onProgress?.Invoke("[2/4] Đang dừng app Shopee...");
                await AdbPackageService.ForceStopAsync(deviceId, "com.shopee.vn");

                onProgress?.Invoke("[3/4] Đang sao chép thư mục dữ liệu Shopee...");
                string localData = Path.Combine(backupDir, "data");
                string localDot = Path.Combine(backupDir, "dot_shopee");

                await AdbMediaService.PullAsync(deviceId, "/sdcard/Android/data/com.shopee.vn", localData, 60000);
                await AdbMediaService.PullAsync(deviceId, "/sdcard/.shopee", localDot, 30000);

                onProgress?.Invoke("[4/4] Đang lưu cấu hình bản sao lưu...");
                long totalSize = 0;
                if (Directory.Exists(backupDir))
                {
                    foreach (var file in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                }

                var info = new ShopeeBackupInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DeviceId = deviceId,
                    DeviceModel = model,
                    AndroidId = ssaid,
                    CreatedAt = DateTime.Now,
                    TotalSizeBytes = totalSize,
                    DirectoryPath = backupDir
                };

                string metaJson = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(backupDir, "backup_meta.json"), metaJson, Encoding.UTF8);

                return (true, info, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public static async Task<(bool Success, string Message)> RestoreShopeeDataAsync(
            string deviceId, ShopeeBackupInfo backup, Action<string>? onProgress = null)
        {
            try
            {
                if (!Directory.Exists(backup.DirectoryPath))
                    return (false, "Thư mục sao lưu không tồn tại!");

                onProgress?.Invoke("[1/4] Đang dừng ứng dụng Shopee...");
                await AdbPackageService.ForceStopAsync(deviceId, "com.shopee.vn");

                if (!string.IsNullOrWhiteSpace(backup.AndroidId))
                {
                    onProgress?.Invoke($"[2/4] Đang khôi phục Android ID ({backup.AndroidId})...");
                    await AdbExecutor.RunAdbAsync(deviceId, $"shell settings put secure android_id {backup.AndroidId.Trim()}");
                }

                onProgress?.Invoke("[3/4] Đang nạp dữ liệu sao lưu vào thiết bị...");
                string localData = Path.Combine(backup.DirectoryPath, "data");
                string localDot = Path.Combine(backup.DirectoryPath, "dot_shopee");

                if (Directory.Exists(localData))
                {
                    await AdbMediaService.PushAsync(deviceId, localData, "/sdcard/Android/data/com.shopee.vn", 60000);
                }
                if (Directory.Exists(localDot))
                {
                    await AdbMediaService.PushAsync(deviceId, localDot, "/sdcard/.shopee", 30000);
                }

                onProgress?.Invoke("[4/4] Thiết lập phân quyền thư mục...");
                await AdbExecutor.RunAdbAsync(deviceId, "shell chmod -R 777 /sdcard/Android/data/com.shopee.vn 2>/dev/null");

                return (true, LanguageManager.GetString("Str.Backup.RestoreSuccess"));
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
