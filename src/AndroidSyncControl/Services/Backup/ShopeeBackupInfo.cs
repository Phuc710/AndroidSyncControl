using System;

namespace AndroidSyncControl.Services.Backup
{
    /// <summary>
    /// Metadata descriptor for a saved Shopee account / data backup snapshot.
    /// </summary>
    public class ShopeeBackupInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string AndroidId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public long TotalSizeBytes { get; set; }
        public string DirectoryPath { get; set; } = string.Empty;

        public string DisplayName => $"{CreatedAt:yyyy-MM-dd HH:mm} • {DeviceModel} ({TotalSizeFormatted})";
        public string TotalSizeFormatted => TotalSizeBytes >= 1024 * 1024 
            ? $"{(TotalSizeBytes / (1024.0 * 1024.0)):F1} MB" 
            : $"{(TotalSizeBytes / 1024.0):F0} KB";
    }
}
