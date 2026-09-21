using System;
using System.IO;
using AndroidSyncControl.Infrastructure;
using Xunit;

namespace AndroidSyncControl.Tests
{
    public class AppPathsTests
    {
        [Fact]
        public void AppPaths_DataDir_MustBeInLocalAppData()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.StartsWith(localAppData, AppPaths.DataDir, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AppPaths_AgentDataDir_MustBeSubdirectoryOfDataDir()
        {
            Assert.StartsWith(AppPaths.DataDir, AppPaths.AgentDataDir, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("agent-data", AppPaths.AgentDataDir, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AppPaths_LogsDir_MustBeSubdirectoryOfDataDir()
        {
            Assert.StartsWith(AppPaths.DataDir, AppPaths.LogsDir, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("logs", AppPaths.LogsDir, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AppPaths_EnsureDirectories_CreatesAllRequiredDirectories()
        {
            AppPaths.EnsureDirectories();

            Assert.True(Directory.Exists(AppPaths.DataDir));
            Assert.True(Directory.Exists(AppPaths.ConfigDir));
            Assert.True(Directory.Exists(AppPaths.LogsDir));
            Assert.True(Directory.Exists(AppPaths.CacheDir));
            Assert.True(Directory.Exists(AppPaths.UpdatesDir));
            Assert.True(Directory.Exists(AppPaths.AgentDataDir));
            Assert.True(Directory.Exists(AppPaths.AgentPlaybooksDir));
            Assert.True(Directory.Exists(AppPaths.AgentExperiencesDir));
            Assert.True(Directory.Exists(AppPaths.AgentLessonsDir));
            Assert.True(Directory.Exists(AppPaths.AgentEvaluationsDir));
        }

        [Fact]
        public void AppPaths_GetLogFilePath_ReturnsCorrectPath()
        {
            string logPath = AppPaths.GetLogFilePath("test.log");
            Assert.StartsWith(AppPaths.LogsDir, logPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("test.log", logPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
