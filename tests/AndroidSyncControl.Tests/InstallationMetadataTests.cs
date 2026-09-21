using System;
using System.IO;
using AndroidSyncControl.Infrastructure;
using Xunit;

namespace AndroidSyncControl.Tests
{
    public class InstallationMetadataTests
    {
        [Fact]
        public void InstallationMetadata_SaveAndLoad_RoundtripWorks()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"install_test_{Guid.NewGuid():N}.json");

            try
            {
                var meta = new InstallationMetadata
                {
                    Product = "AndroidSyncControl",
                    Version = "1.0.0",
                    Channel = "stable",
                    InstallPath = @"C:\Program Files\AndroidSyncControl",
                    InstalledAt = DateTimeOffset.UtcNow
                };

                meta.Save(tempFile);
                Assert.True(File.Exists(tempFile));

                var loaded = InstallationMetadata.Load(tempFile);
                Assert.NotNull(loaded);
                Assert.Equal("AndroidSyncControl", loaded.Product);
                Assert.Equal("1.0.0", loaded.Version);
                Assert.Equal("stable", loaded.Channel);
                Assert.Equal(@"C:\Program Files\AndroidSyncControl", loaded.InstallPath);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
