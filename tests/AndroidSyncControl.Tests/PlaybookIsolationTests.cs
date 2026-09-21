using System;
using System.IO;
using System.Threading.Tasks;
using AndroidSyncControl.Agent.Domain;
using AndroidSyncControl.Agent.Repository;
using Xunit;

namespace AndroidSyncControl.Tests
{
    public class PlaybookIsolationTests
    {
        [Fact]
        public async Task PlaybookRepository_SaveAndGet_MaintainsVersionHistory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"agent_test_{Guid.NewGuid():N}");

            try
            {
                var repo = new JsonPlaybookRepository(tempDir);

                var pb1 = Playbook.CreateDraft("pb_test", "test intent", new[] { "browser", "setup" });
                await repo.SaveAsync(pb1);

                var loaded = await repo.GetAsync("pb_test", 1);
                Assert.NotNull(loaded);
                Assert.Equal(1, loaded.Version);
                Assert.Equal("pb_test", loaded.Id);

                // Invariant: Cannot overwrite existing version (SC-11)
                await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveAsync(pb1));

                // Save v2 using BumpVersion
                var pb2 = pb1.BumpVersion(PlaybookState.Verified);
                await repo.SaveAsync(pb2);

                var all = await repo.GetAllAsync();
                Assert.Equal(2, all.Count);
                Assert.Equal("pb_test_v1", pb2.ParentVersion);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
