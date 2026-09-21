using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AndroidSyncControl.Infrastructure;

namespace AndroidSyncControl.Update
{
    public sealed class UpdateService
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public static Version CurrentVersion { get; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

        public event Action<UpdateManifest>? UpdateAvailable;
        public event Action<int>? DownloadProgressChanged;
        public event Action<string>? UpdateFailed;

        /// <summary>
        /// Check for update manifest asynchronously in the background.
        /// Never blocks UI and never throws uncaught exceptions.
        /// </summary>
        public async Task<UpdateManifest?> CheckForUpdateAsync(string manifestUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl)) return null;

            try
            {
                using var response = await HttpClient.GetAsync(manifestUrl, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
                if (manifest == null) return null;

                if (Version.TryParse(manifest.Version, out var remoteVer))
                {
                    if (remoteVer > CurrentVersion)
                    {
                        UpdateAvailable?.Invoke(manifest);
                        return manifest;
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateFailed?.Invoke($"Update check failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Download the update package to %LOCALAPPDATA%\AndroidSyncControl\updates\
        /// </summary>
        public async Task<string?> DownloadPackageAsync(UpdateManifest manifest, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                AppPaths.EnsureDirectories();
                string targetZip = Path.Combine(AppPaths.UpdatesDir, $"{AppPaths.AppName}-{manifest.Version}-win-x64.zip");

                if (File.Exists(targetZip))
                {
                    // Check if existing file is already valid
                    if (VerifyChecksum(targetZip, manifest.Package.Sha256))
                    {
                        progress?.Report(100);
                        return targetZip;
                    }
                    File.Delete(targetZip);
                }

                using var response = await HttpClient.GetAsync(manifest.Package.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? manifest.Package.Size;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = new FileStream(targetZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    totalRead += read;

                    if (totalBytes > 0)
                    {
                        int percent = (int)((totalRead * 100) / totalBytes);
                        progress?.Report(percent);
                        DownloadProgressChanged?.Invoke(percent);
                    }
                }

                fileStream.Flush();
                fileStream.Close();

                // Verify SHA-256
                if (!VerifyChecksum(targetZip, manifest.Package.Sha256))
                {
                    UpdateFailed?.Invoke("Downloaded package checksum mismatch.");
                    if (File.Exists(targetZip)) File.Delete(targetZip);
                    return null;
                }

                return targetZip;
            }
            catch (Exception ex)
            {
                UpdateFailed?.Invoke($"Download failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Spawns AndroidSyncControl.Updater.exe and closes the current application.
        /// </summary>
        public bool LaunchUpdaterAndExit(string packagePath, UpdateManifest manifest)
        {
            try
            {
                string updaterExe = AppPaths.UpdaterExePath;
                if (!File.Exists(updaterExe))
                {
                    UpdateFailed?.Invoke($"Updater executable not found at: {updaterExe}");
                    return false;
                }

                int currentPid = Process.GetCurrentProcess().Id;
                string currentAppDir = AppPaths.AppDir;
                string restartExe = Path.Combine(currentAppDir, "AndroidSyncControl.exe");

                string args = $"--wait-pid {currentPid} " +
                              $"--target-dir \"{currentAppDir}\" " +
                              $"--package-path \"{packagePath}\" " +
                              $"--expected-sha256 \"{manifest.Package.Sha256}\" " +
                              $"--current-version \"{CurrentVersion}\" " +
                              $"--new-version \"{manifest.Version}\" " +
                              $"--restart-exe \"{restartExe}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = currentAppDir
                };

                Process.Start(psi);

                // Gracefully shutdown WPF app
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown(0);
                });

                return true;
            }
            catch (Exception ex)
            {
                UpdateFailed?.Invoke($"Failed to launch updater: {ex.Message}");
                return false;
            }
        }

        public static bool VerifyChecksum(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(filePath)) return false;

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            string actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            return string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
