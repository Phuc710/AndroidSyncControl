using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AndroidSyncControl.Update
{
    public sealed class UpdateManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("product")]
        public string Product { get; set; } = "AndroidSyncControl";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "stable";

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = "win-x64";

        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; set; } = false;

        [JsonPropertyName("package")]
        public PackageInfo Package { get; set; } = new();

        [JsonPropertyName("release")]
        public ReleaseInfo Release { get; set; } = new();

        [JsonPropertyName("minimumSupportedVersion")]
        public string MinimumSupportedVersion { get; set; } = "1.0.0";
    }

    public sealed class PackageInfo
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; } = 0;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }

    public sealed class ReleaseInfo
    {
        [JsonPropertyName("publishedAt")]
        public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("changelog")]
        public List<string> Changelog { get; set; } = new();
    }
}
