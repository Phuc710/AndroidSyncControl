using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AndroidSyncControl.UI.Helpers;

namespace AndroidSyncControl.Services.Adb
{
    /// <summary>
    /// Core executor for ADB CLI commands with process management, timeout, and buffer handling.
    /// Follows KR-01: Centralized Toolchain isolation using tools/android/adb/adb.exe.
    /// Optimized for high-throughput, low latency, and zero daemon noise.
    /// </summary>
    public static class AdbExecutor
    {
        private static string GetAdbPath() => AndroidToolchain.AdbPath;

        /// <summary>
        /// Runs an ADB command asynchronously with strict timeout and clean output filtering.
        /// </summary>
        public static async Task<string> RunAdbAsync(string deviceId, string arguments, int timeoutMs = 10000)
        {
            try
            {
                string adb = GetAdbPath();
                string fullArgs = string.IsNullOrEmpty(deviceId) ? arguments : $"-s {deviceId} {arguments}";
                var psi = new ProcessStartInfo
                {
                    FileName = adb,
                    Arguments = fullArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = psi };
                using var cts = new CancellationTokenSource(timeoutMs);

                proc.Start();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(); } catch { }
                    return "Error: Command timed out";
                }

                string stdout = SanitizeOutput(await stdoutTask);
                string stderr = SanitizeOutput(await stderrTask);

                if (!string.IsNullOrEmpty(stdout)) return stdout;
                return stderr;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Executes multiple shell commands in a single roundtrip ADB process to eliminate process spawn lag.
        /// Commands are joined with '; ' so subsequent commands continue even if one returns non-zero.
        /// </summary>
        public static async Task<string> RunAdbShellBatchAsync(string deviceId, IEnumerable<string> shellCommands, int timeoutMs = 15000)
        {
            var cmdList = shellCommands?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
            if (cmdList == null || cmdList.Count == 0) return string.Empty;

            if (cmdList.Count == 1)
            {
                return await RunAdbAsync(deviceId, $"shell {cmdList[0]}", timeoutMs);
            }

            // Chain multiple commands with semicolon in a single shell session
            string compound = string.Join("; ", cmdList);
            return await RunAdbAsync(deviceId, $"shell \"{compound}\"", timeoutMs);
        }

        /// <summary>
        /// Checks if a device is online and responsive via 'get-state' in under 1.5 seconds.
        /// </summary>
        public static async Task<bool> IsDeviceReadyAsync(string deviceId, int timeoutMs = 1500)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                string state = (await RunAdbAsync(deviceId, "get-state", timeoutMs)).Trim();
                return state.Equals("device", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Strips ADB daemon messages and warnings from raw stdout/stderr.
        /// </summary>
        private static string SanitizeOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var sb = new StringBuilder();
            using var reader = new StringReader(raw);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                // Filter out daemon startup and listener noise
                if (trimmed.StartsWith("* daemon not running", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("* daemon started", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("* daemon listening", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("adb server version", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                sb.AppendLine(line);
            }
            return sb.ToString().Trim();
        }

        public static async Task<bool> RestartAdbServerAsync()
        {
            try
            {
                await RunAdbAsync(string.Empty, "kill-server", 5000);
                await Task.Delay(500);
                await RunAdbAsync(string.Empty, "start-server", 8000);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

