using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidSyncControl.Infrastructure
{
    /// <summary>
    /// Represents the installed application metadata (install.json) placed in the app directory.
    /// </summary>
    public sealed class InstallationMetadata
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [JsonPropertyName("product")]
        public string Product { get; set; } = AppPaths.AppName;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "stable";

        [JsonPropertyName("installPath")]
        public string InstallPath { get; set; } = string.Empty;

        [JsonPropertyName("installedAt")]
        public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

        public static InstallationMetadata? Load(string? customPath = null)
        {
            string path = customPath ?? AppPaths.InstallMetadataFile;
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<InstallationMetadata>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public void Save(string? customPath = null)
        {
            string path = customPath ?? AppPaths.InstallMetadataFile;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
    }
}
