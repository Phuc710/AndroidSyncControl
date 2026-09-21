using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace AndroidSyncControl.Updater
{
    public sealed class UpdateOptions
    {
        public int WaitPid { get; set; } = 0;
        public string TargetDir { get; set; } = string.Empty;
        public string PackagePath { get; set; } = string.Empty;
        public string ExpectedSha256 { get; set; } = string.Empty;
        public string RestartExe { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;
        public bool Silent { get; set; } = false;
    }

    public static class UpdateEngine
    {
        private static string _logFile = string.Empty;

        public static void SetLogFile(string path)
        {
            _logFile = path;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public static void Log(string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            try
            {
                if (!string.IsNullOrEmpty(_logFile))
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine);
                }
            }
            catch { }
            Console.WriteLine(line);
        }

        public static bool Run(UpdateOptions options)
        {
            Log("==================================================");
            Log($"Starting AndroidSyncControl Update Process");
            Log($"Target Directory: {options.TargetDir}");
            Log($"Package Path:     {options.PackagePath}");
            Log($"New Version:      {options.NewVersion}");
            Log("==================================================");

            // Step 1: Wait for running instance to exit
            if (options.WaitPid > 0)
            {
                Log($"Waiting for parent process (PID={options.WaitPid}) to terminate...");
                try
                {
                    var proc = Process.GetProcessById(options.WaitPid);
                    if (!proc.WaitForExit(15000))
                    {
                        Log("Process did not exit in 15s. Forcing termination...");
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                    Log("Parent process terminated.");
                }
                catch (ArgumentException)
                {
                    Log("Parent process already exited.");
                }
                catch (Exception ex)
                {
                    Log($"Warning while waiting for PID {options.WaitPid}: {ex.Message}");
                }
            }

            // Small delay to release file locks
            Thread.Sleep(500);

            // Step 2: Validate Package Integrity (SHA-256)
            if (!File.Exists(options.PackagePath))
            {
                Log($"[ERROR] Package zip not found: {options.PackagePath}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.ExpectedSha256))
            {
                Log("Verifying package SHA-256 checksum...");
                string actualSha = ComputeSha256(options.PackagePath);
                if (!string.Equals(actualSha, options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[ERROR] SHA-256 mismatch! Expected: {options.ExpectedSha256}, Actual: {actualSha}");
                    return false;
                }
                Log("Package SHA-256 verified successfully.");
            }

            // Step 3: Backup current state to .backup/
            string backupDir = Path.Combine(options.TargetDir, ".backup");
            try
            {
                Log($"Creating backup in: {backupDir}");
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, recursive: true);
                }
                Directory.CreateDirectory(backupDir);

                CopyDirectoryExcluding(options.TargetDir, backupDir, excludeDirName: ".backup");
                Log("Backup completed.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to create backup: {ex.Message}");
                return false;
            }

            // Step 4: Extract package into target directory (atomic replace)
            bool extractSuccess = false;
            try
            {
                Log("Extracting new package files...");
                ZipFile.ExtractToDirectory(options.PackagePath, options.TargetDir, overwriteFiles: true);
                extractSuccess = true;
                Log("Package extraction finished.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Extraction failed: {ex.Message}");
            }

            string exePath = !string.IsNullOrEmpty(options.RestartExe)
                ? options.RestartExe
                : Path.Combine(options.TargetDir, "AndroidSyncControl.exe");

            // Step 5 & 6: Verify Installation & Run Health Check
            bool healthCheckPassed = false;
            if (extractSuccess && File.Exists(exePath))
            {
                Log($"Running health check on updated binary: {exePath} --health-check");
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "--health-check",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var healthProc = Process.Start(startInfo);
                    if (healthProc != null)
                    {
                        if (healthProc.WaitForExit(10000))
                        {
                            if (healthProc.ExitCode == 0)
                            {
                                healthCheckPassed = true;
                                Log("Health check PASSED with exit code 0.");
                            }
                            else
                            {
                                Log($"[ERROR] Health check failed with exit code: {healthProc.ExitCode}");
                            }
                        }
                        else
                        {
                            Log("[ERROR] Health check timed out after 10s.");
                            try { healthProc.Kill(); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] Failed to start health check process: {ex.Message}");
                }
            }

            // Step 7: Handle Result - Rollback on failure
            if (!healthCheckPassed)
            {
                Log("==================================================");
                Log("[CRITICAL] Health check FAILED. Initiating ROLLBACK...");
                Log("==================================================");
                try
                {
                    CopyDirectoryExcluding(backupDir, options.TargetDir, excludeDirName: null);
                    Log("Rollback completed. Restored previous version.");
                }
                catch (Exception rbEx)
                {
                    Log($"[FATAL] Rollback failed: {rbEx.Message}");
                }

                // Clean backup
                try { Directory.Delete(backupDir, true); } catch { }

                // Restart restored version
                if (File.Exists(exePath))
                {
                    Log($"Restarting restored application: {exePath}");
                    Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
                }
                return false;
            }

            // SUCCESS PATH
            Log("Update verified successfully.");

            // Update install.json metadata
            try
            {
                string metadataPath = Path.Combine(options.TargetDir, "install.json");
                UpdateInstallMetadata(metadataPath, options.NewVersion, options.TargetDir);
            }
            catch (Exception metaEx)
            {
                Log($"Warning: could not update install.json: {metaEx.Message}");
            }

            // Clean backup
            try
            {
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                    Log("Temporary backup removed.");
                }
            }
            catch { }

            // Step 8: Restart updated app
            Log($"Restarting application: {exePath}");
            try
            {
                Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
                Log("Application restarted. Updater exiting.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to restart application: {ex.Message}");
            }

            return true;
        }

        private static void CopyDirectoryExcluding(string sourceDir, string targetDir, string? excludeDirName)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                if (!string.IsNullOrEmpty(excludeDirName) && string.Equals(dirName, excludeDirName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string destSub = Path.Combine(targetDir, dirName);
                CopyDirectoryExcluding(subDir, destSub, excludeDirName);
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static void UpdateInstallMetadata(string path, string version, string installPath)
        {
            var meta = new
            {
                product = "AndroidSyncControl",
                version = version,
                channel = "stable",
                installPath = installPath,
                installedAt = DateTimeOffset.UtcNow
            };

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(meta, opts));
        }
    }
}
