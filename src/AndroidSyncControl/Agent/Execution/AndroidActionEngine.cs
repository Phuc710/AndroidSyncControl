using System.Diagnostics;
using AndroidSyncControl.Agent.Domain;
using AndroidSyncControl.UI.Helpers;

namespace AndroidSyncControl.Agent.Execution;

/// <summary>
/// Thực thi abstract action steps trên thiết bị Android qua ADB.
/// <para>
/// KR-01: Tất cả ADB calls đều đi qua <see cref="AndroidToolchain.AdbPath"/> —
/// không tạo bản sao binary, không gọi "adb" trần.
/// </para>
/// KR-06: Không hardcode model string. Mọi thứ đi qua DeviceSerial.
/// </summary>
public sealed class AndroidActionEngine
{
    private readonly string _deviceSerial;

    public AndroidActionEngine(string deviceSerial)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial))
            throw new ArgumentException("Device serial must not be empty.", nameof(deviceSerial));
        _deviceSerial = deviceSerial;
    }

    // ── Action dispatcher ──────────────────────────────────────────────────────

    /// <summary>Thực thi một abstract action step và trả về raw ExecutionTrace.</summary>
    public async Task<ExecutionTrace> ExecuteStepAsync(
        string stepName,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken ct)
    {
        parameters ??= new Dictionary<string, string>();
        var sw = Stopwatch.StartNew();
        string uiState = "Unknown";

        try
        {
            string? output = stepName switch
            {
                "launch_app"           => await LaunchAppAsync(parameters, ct),
                "force_stop_app"       => await ForceStopAppAsync(parameters, ct),
                "wait_for_ui_stable"   => await WaitForUiStableAsync(parameters, ct),
                "verify_app_state"     => await VerifyAppStateAsync(parameters, ct),
                "find_element"         => await FindElementAsync(parameters, ct),
                "tap"                  => await TapAsync(parameters, ct),
                "type_text"            => await TypeTextAsync(parameters, ct),
                "swipe"                => await SwipeAsync(parameters, ct),
                "capture_screenshot"   => await CaptureScreenshotAsync(parameters, ct),
                "verify_result"        => await VerifyResultAsync(parameters, ct),
                _                      => throw new NotSupportedException($"Unknown step: {stepName}"),
            };

            sw.Stop();
            return new ExecutionTrace(
                StepName: stepName,
                Input: parameters,
                Output: output,
                Error: null,
                Duration: sw.Elapsed,
                UiState: uiState,
                RecoveryAttempts: []);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecutionTrace(
                StepName: stepName,
                Input: parameters,
                Output: null,
                Error: ex.Message,
                Duration: sw.Elapsed,
                UiState: uiState,
                RecoveryAttempts: []);
        }
    }

    // ── ADB wrappers (all go through AndroidToolchain.AdbPath — KR-01) ─────────

    private async Task<string?> LaunchAppAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("package", out var pkg))
            throw new ArgumentException("launch_app requires 'package' parameter.");
        return await AdbShellAsync($"monkey -p {pkg} -c android.intent.category.LAUNCHER 1", ct);
    }

    private async Task<string?> ForceStopAppAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("package", out var pkg))
            throw new ArgumentException("force_stop_app requires 'package' parameter.");
        return await AdbShellAsync($"am force-stop {pkg}", ct);
    }

    private async Task<string?> WaitForUiStableAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        int waitMs = p.TryGetValue("wait_ms", out var w) && int.TryParse(w, out int ms) ? ms : 1500;
        await Task.Delay(waitMs, ct);
        return $"waited_{waitMs}ms";
    }

    private async Task<string?> VerifyAppStateAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("package", out var pkg))
            throw new ArgumentException("verify_app_state requires 'package' parameter.");
        // Check if app is in foreground via dumpsys
        string output = await AdbShellAsync($"dumpsys activity activities | grep -E 'mResumedActivity'", ct) ?? "";
        return output.Contains(pkg) ? "foreground" : "background";
    }

    private async Task<string?> FindElementAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("resource_id", out var resId))
            throw new ArgumentException("find_element requires 'resource_id' parameter.");
        // uiautomator dump + grep approach (no Appium dependency)
        string dumpPath = $"/sdcard/ui_dump_{Guid.NewGuid():N}.xml";
        await AdbShellAsync($"uiautomator dump {dumpPath}", ct);
        string content = await AdbShellAsync($"cat {dumpPath}", ct) ?? "";
        await AdbShellAsync($"rm {dumpPath}", ct);
        if (!content.Contains(resId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ElementNotFound: resource_id='{resId}' not in UI dump.");
        return $"found:{resId}";
    }

    private async Task<string?> TapAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("x", out var xs) || !p.TryGetValue("y", out var ys))
            throw new ArgumentException("tap requires 'x' and 'y' parameters.");
        return await AdbShellAsync($"input tap {xs} {ys}", ct);
    }

    private async Task<string?> TypeTextAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("text", out var text))
            throw new ArgumentException("type_text requires 'text' parameter.");
        // Escape spaces for ADB shell
        string escaped = text.Replace(" ", "%s");
        return await AdbShellAsync($"input text \"{escaped}\"", ct);
    }

    private async Task<string?> SwipeAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("x1", out var x1) || !p.TryGetValue("y1", out var y1) ||
            !p.TryGetValue("x2", out var x2) || !p.TryGetValue("y2", out var y2))
            throw new ArgumentException("swipe requires x1, y1, x2, y2 parameters.");
        int durationMs = p.TryGetValue("duration_ms", out var d) && int.TryParse(d, out int dur) ? dur : 300;
        return await AdbShellAsync($"input swipe {x1} {y1} {x2} {y2} {durationMs}", ct);
    }

    private async Task<string?> CaptureScreenshotAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        string remotePath = $"/sdcard/screenshot_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.png";
        await AdbShellAsync($"screencap -p {remotePath}", ct);
        if (p.TryGetValue("local_path", out var localPath))
            await AdbPullAsync(remotePath, localPath, ct);
        return remotePath;
    }

    private async Task<string?> VerifyResultAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        // Generic: check if expected text visible on screen via uiautomator dump
        if (!p.TryGetValue("expected_text", out var expected)) return "no_verification";
        string dumpPath = $"/sdcard/verify_dump_{Guid.NewGuid():N}.xml";
        await AdbShellAsync($"uiautomator dump {dumpPath}", ct);
        string content = await AdbShellAsync($"cat {dumpPath}", ct) ?? "";
        await AdbShellAsync($"rm {dumpPath}", ct);
        return content.Contains(expected, StringComparison.OrdinalIgnoreCase) ? "verified" : "not_found";
    }

    // ── ADB process helpers ───────────────────────────────────────────────────

    private async Task<string?> AdbShellAsync(string shellCmd, CancellationToken ct)
    {
        return await RunAdbAsync(["shell", shellCmd], ct);
    }

    private async Task<string?> AdbPullAsync(string remote, string local, CancellationToken ct)
    {
        return await RunAdbAsync(["pull", remote, local], ct);
    }

    private async Task<string?> RunAdbAsync(string[] args, CancellationToken ct)
    {
        string adbPath = AndroidToolchain.AdbPath;
        using var psi = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = adbPath,
                Arguments              = $"-s {_deviceSerial} {string.Join(" ", args)}",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };

        psi.Start();
        string stdout = await psi.StandardOutput.ReadToEndAsync(ct);
        await psi.WaitForExitAsync(ct);
        return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
    }
}
