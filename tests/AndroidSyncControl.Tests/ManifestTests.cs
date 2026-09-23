using System;
using System.Text.Json;
using AndroidSyncControl.Update;
using Xunit;

namespace AndroidSyncControl.Tests
{
    public class ManifestTests
    {
        [Fact]
        public void UpdateManifest_SerializeAndDeserialize_PreservesSchema()
        {
            var original = new UpdateManifest
            {
                SchemaVersion = 1,
                Product = "AndroidSyncControl",
                Version = "1.2.0",
                Channel = "stable",
                Platform = "win-x64",
                Mandatory = false,
                Package = new PackageInfo
                {
                    Url = "https://github.com/Phuc710/AndroidSyncControl/releases/download/v1.2.0/AndroidSyncControl-1.2.0-win-x64.zip",
                    Size = 45000000,
                    Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                },
                Release = new ReleaseInfo
                {
                    PublishedAt = DateTimeOffset.UtcNow,
                    Changelog = { "Feature 1", "Bugfix 2" }
                },
                MinimumSupportedVersion = "1.0.0"
            };

            string json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
            var deserialized = JsonSerializer.Deserialize<UpdateManifest>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(1, deserialized.SchemaVersion);
            Assert.Equal("AndroidSyncControl", deserialized.Product);
            Assert.Equal("1.2.0", deserialized.Version);
            Assert.Equal("stable", deserialized.Channel);
            Assert.Equal("win-x64", deserialized.Platform);
            Assert.False(deserialized.Mandatory);
            Assert.Equal(45000000, deserialized.Package.Size);
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", deserialized.Package.Sha256);
            Assert.Equal(2, deserialized.Release.Changelog.Count);
        }

        [Theory]
        [InlineData("1.2.0", "1.0.0", true)]
        [InlineData("1.0.1", "1.0.0.0", true)]
        [InlineData("1.2.0-beta", "1.0.0", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.0.0", "1.0.0.0", false)]
        [InlineData("0.9.0", "1.0.0", false)]
        [InlineData("", "1.0.0", false)]
        [InlineData(null, "1.0.0", false)]
        public void UpdateService_IsNewerVersion_EvaluatesCorrectly(string? remoteVer, string currentVerStr, bool expected)
        {
            var currentVer = Version.Parse(currentVerStr);
            bool actual = UpdateService.IsNewerVersion(remoteVer, currentVer);
            Assert.Equal(expected, actual);
        }
    }
}
