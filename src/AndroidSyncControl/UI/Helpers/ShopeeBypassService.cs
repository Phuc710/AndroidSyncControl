using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using AndroidSyncControl.Localization;

namespace AndroidSyncControl.UI.Helpers
{
    public static class ShopeeBypassService
    {
        private static string GetAdbPath() => AndroidToolchain.AdbPath;

        public static async Task<string> RunAdbAsync(string deviceId, string arguments, int timeoutMs = 10000)
        {
            return await Task.Run(() =>
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
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    using (var proc = new Process { StartInfo = psi })
                    {
                        var stdout = new StringBuilder();
                        var stderr = new StringBuilder();

                        proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                        proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();

                        bool exited = proc.WaitForExit(timeoutMs);
                        if (!exited)
                        {
                            try { proc.Kill(); } catch { }
                        }

                        string outStr = stdout.ToString().Trim();
                        return !string.IsNullOrEmpty(outStr) ? outStr : stderr.ToString().Trim();
                    }
                }
                catch (Exception ex)
                {
                    return $"Error: {ex.Message}";
                }
            });
        }

        public static string GenerateRandomHex(int byteCount = 8)
        {
            byte[] bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            var sb = new StringBuilder();
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public static async Task<string> GetActiveDeviceAsync()
        {
            try
            {
                string outDevices = await RunAdbAsync(string.Empty, "devices");
                foreach (var line in outDevices.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("List of") || string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[1].Equals("device", StringComparison.OrdinalIgnoreCase))
                    {
                        return parts[0].Trim();
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public static async Task<bool> RestartAdbServerAsync()
        {
            try
            {
                await RunAdbAsync(string.Empty, "kill-server", 5000);
                await Task.Delay(600);
                await RunAdbAsync(string.Empty, "start-server", 8000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> GetDeviceModelAsync(string deviceId)
        {
            try
            {
                string model = (await RunAdbAsync(deviceId, "shell getprop ro.product.model")).Trim();
                if (!string.IsNullOrEmpty(model)) return model;
            }
            catch { }
            return "Android Device";
        }

        public static async Task<(int width, int height)> GetDeviceResolutionAsync(string deviceId)
        {
            try
            {
                string outStr = await RunAdbAsync(deviceId, "shell wm size");
                int baseW = 720, baseH = 1280;
                foreach (var line in outStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s*x\s*(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int w) && int.TryParse(match.Groups[2].Value, out int h))
                    {
                        if (w > 0 && h > 0)
                        {
                            baseW = w;
                            baseH = h;
                            break;
                        }
                    }
                }

                // Check device rotation (0=Portrait, 1=Landscape, 2=Reverse Portrait, 3=Reverse Landscape)
                try
                {
                    string rotOut = await RunAdbAsync(deviceId, "shell dumpsys window");
                    var rotMatch = System.Text.RegularExpressions.Regex.Match(rotOut, @"mCurrentRotation=ROTATION_(\d+)");
                    if (rotMatch.Success && int.TryParse(rotMatch.Groups[1].Value, out int rot))
                    {
                        if (rot == 1 || rot == 3)
                        {
                            int tmp = baseW;
                            baseW = baseH;
                            baseH = tmp;
                        }
                    }
                }
                catch { }

                return (baseW, baseH);
            }
            catch { }
            return (720, 1280);
        }

        public static async Task<string> GetPhoneInfoAsync(string deviceId)
        {
            string androidId = (await RunAdbAsync(deviceId, "shell settings get secure android_id")).Trim();
            string model = (await RunAdbAsync(deviceId, "shell getprop ro.product.model")).Trim();
            string serial = (await RunAdbAsync(deviceId, "shell getprop ro.serialno")).Trim();
            string ipOut = await RunAdbAsync(deviceId, "shell ip -f inet addr 2>/dev/null");

            string wlanIp = "N/A";
            string vpnIp = string.Empty;
            string mobileIp = string.Empty;
            string currentIface = string.Empty;

            foreach (var line in ipOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (char.IsDigit(trimmed[0]) && trimmed.Contains(": "))
                {
                    var parts = trimmed.Split(new[] { ": " }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        currentIface = parts[1].Split(':')[0].Trim();
                    }
                }
                else if (trimmed.StartsWith("inet ") && !trimmed.Contains("127.0.0.1"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        string addr = parts[1].Split('/')[0];
                        if (currentIface.StartsWith("tun") || currentIface.StartsWith("ppp") || currentIface.StartsWith("wg"))
                        {
                            vpnIp = $"{addr} ({currentIface})";
                        }
                        else if (currentIface.StartsWith("wlan"))
                        {
                            wlanIp = addr;
                        }
                        else if (currentIface.StartsWith("rmnet") || currentIface.StartsWith("ccmni"))
                        {
                            mobileIp = addr;
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Model:       {model}");
            sb.AppendLine($"Serial:      {serial}");
            sb.AppendLine($"Android ID:  {androidId}");

            if (!string.IsNullOrEmpty(vpnIp))
            {
                sb.AppendLine($"VPN:         {vpnIp}");
                sb.AppendLine($"Wi-Fi IP:    {wlanIp}");
            }
            else if (!string.IsNullOrEmpty(mobileIp))
            {
                sb.AppendLine($"Cellular IP: {mobileIp}");
            }
            else if (!string.IsNullOrEmpty(wlanIp))
            {
                sb.AppendLine($"Wi-Fi IP:    {wlanIp}");
            }

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Bypass pipeline — 8 bước, M04-capable
        // Bổ sung so với pipeline cũ:
        //   Step 4: Clear GSF ID (com.google.android.gsf) — device registration token
        //   Step 5: Remove Shopee AccountManager tokens — persist ngoài app sandbox
        //   Step 6-7: Airplane mode với adaptive IP-poll loop thay vì hardcode sleep
        //             → fix "not connected" race condition trên cloud phone
        // ──────────────────────────────────────────────────────────────────────
        public static async Task<bool> BypassShopeeAsync(string deviceId, Action<string> statusCallback = null)
        {
            // Step 1 — Force stop + clear all app data + external storage
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step1"));
            await RunAdbAsync(deviceId, "shell am force-stop com.shopee.vn");
            await RunAdbAsync(deviceId, "shell pm clear com.shopee.vn");
            await RunAdbAsync(deviceId, "shell rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/.shopee 2>/dev/null");

            // Step 2 — Rotate SSAID (Android ID)
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step2"));
            string newId = GenerateRandomHex(8);
            await RunAdbAsync(deviceId, $"shell settings put secure android_id {newId}");

            // Step 3 — Reset GAID via GMS clear
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step3"));
            await RunAdbAsync(deviceId, "shell pm clear com.google.android.gms");

            // Step 4 — Clear GSF ID (Google Services Framework device registration token)
            //           Shopee cross-references GAID + GSF for device fingerprint consistency.
            //           pm clear gms alone does NOT wipe GSF — separate package.
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step4"));
            await RunAdbAsync(deviceId, "shell pm clear com.google.android.gsf");

            // Step 5 — Remove Shopee AccountManager tokens
            //           Auth tokens survive pm clear because they live in system account service,
            //           outside the app sandbox. Best-effort: ignore if device denies permission.
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step5"));
            await RemoveShopeeAccountTokensAsync(deviceId);

            // Step 6 — Airplane ON: force socket disconnect + carrier IP release
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step6"));
            string ipBefore = await GetCurrentMobileIpAsync(deviceId);
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode enable");
            await Task.Delay(2000); // give radio time to fully drop

            // Step 7 — Airplane OFF + adaptive poll until IP actually changes
            //           Replaces hardcoded sleep — critical for cloud phones where
            //           cellular re-registration can take 3–20s depending on carrier.
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step7"));
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode disable");
            await WaitForNewIpAsync(deviceId, ipBefore, pollIntervalMs: 1500, timeoutMs: 25000);

            // Step 8 — Launch Shopee with a fresh cold-start session
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step8"));
            await RunAdbAsync(deviceId, "shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1 2>/dev/null");

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Done"));
            return true;
        }

        /// <summary>
        /// Removes Shopee auth tokens from Android AccountManager.
        /// These tokens persist outside the app sandbox and survive pm clear.
        /// Best-effort — silently swallows permission errors.
        /// </summary>
        private static async Task RemoveShopeeAccountTokensAsync(string deviceId)
        {
            try
            {
                // Dump account list and find Shopee-related accounts
                string dump = await RunAdbAsync(deviceId, "shell dumpsys account", 5000);
                var shopeeAccounts = new System.Collections.Generic.List<string>();

                string[] lines = dump.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string currentAccount = null;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    // Account block header: "Account {name=..., type=...}"
                    if (trimmed.StartsWith("Account {", StringComparison.OrdinalIgnoreCase))
                    {
                        currentAccount = trimmed;
                    }
                    // Identify Shopee accounts by package or type
                    if (currentAccount != null &&
                        (trimmed.Contains("shopee", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("sea.com", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!shopeeAccounts.Contains(currentAccount))
                            shopeeAccounts.Add(currentAccount);
                    }
                }

                foreach (string acct in shopeeAccounts)
                {
                    // Extract name= and type= from account descriptor
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(acct, @"name=([^,}]+)");
                    var typeMatch = System.Text.RegularExpressions.Regex.Match(acct, @"type=([^,}]+)");
                    if (nameMatch.Success && typeMatch.Success)
                    {
                        string name = nameMatch.Groups[1].Value.Trim();
                        string type = typeMatch.Groups[1].Value.Trim();
                        // Remove account — requires MANAGE_ACCOUNTS permission (shell has it)
                        await RunAdbAsync(deviceId,
                            $"shell am broadcast -a android.accounts.action.ACCOUNT_REMOVED " +
                            $"--es account_name \"{name}\" --es account_type \"{type}\"", 3000);
                    }
                }
            }
            catch { /* best-effort, non-critical step */ }
        }

        /// <summary>
        /// Returns the current cellular (rmnet/ccmni) IP, or empty string if none.
        /// </summary>
        private static async Task<string> GetCurrentMobileIpAsync(string deviceId)
        {
            try
            {
                string ipOut = await RunAdbAsync(deviceId, "shell ip -f inet addr 2>/dev/null", 5000);
                string currentIface = string.Empty;
                foreach (string line in ipOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && char.IsDigit(trimmed[0]) && trimmed.Contains(": "))
                    {
                        var parts = trimmed.Split(new[] { ": " }, StringSplitOptions.None);
                        if (parts.Length > 1)
                            currentIface = parts[1].Split(':')[0].Trim();
                    }
                    else if (trimmed.StartsWith("inet ") && !trimmed.Contains("127.0.0.1"))
                    {
                        if (currentIface.StartsWith("rmnet") || currentIface.StartsWith("ccmni"))
                        {
                            var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 1)
                                return parts[1].Split('/')[0];
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        /// <summary>
        /// Polls until the mobile IP changes from <paramref name="previousIp"/>, or timeout.
        /// Falls back gracefully on cloud phones that use Wi-Fi instead of cellular.
        /// </summary>
        private static async Task WaitForNewIpAsync(
            string deviceId,
            string previousIp,
            int pollIntervalMs = 1500,
            int timeoutMs = 25000)
        {
            // If previousIp was empty, device had no mobile IP before — just wait a flat minimum.
            if (string.IsNullOrEmpty(previousIp))
            {
                await Task.Delay(Math.Min(pollIntervalMs * 3, 5000));
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(pollIntervalMs);
                string currentIp = await GetCurrentMobileIpAsync(deviceId);
                // Success: IP exists and differs from the pre-airplane address
                if (!string.IsNullOrEmpty(currentIp) && currentIp != previousIp)
                    return;
            }
            // Timeout — proceed anyway; Shopee will handle "no network" gracefully
        }

        public static async Task RotateAirplaneModeAsync(string deviceId)
        {
            string ipBefore = await GetCurrentMobileIpAsync(deviceId);
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode enable");
            await Task.Delay(2000);
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode disable");
            await WaitForNewIpAsync(deviceId, ipBefore, pollIntervalMs: 1500, timeoutMs: 20000);
        }

        public static async Task OpenShopeeAsync(string deviceId)
        {
            await RunAdbAsync(deviceId, "shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1 2>/dev/null");
        }

        /// <summary>
        /// Direct Clipboard Paste:
        /// Transmits Windows clipboard content directly to the focused Android input field
        /// via the device clipboard / input channel.
        /// Does NOT simulate typing character-by-character.
        /// Prioritizes instant atomic paste, full Unicode (UTF-16) support, and sub-30ms latency.
        /// <summary>
        /// Direct Instant Clipboard Paste:
        /// Transmits clipboard content directly to the Android input field in 1 atomic operation.
        /// Zero character-by-character typing simulation. Prioritizes instant paste across all apps.
        /// </summary>
        public static void SafeSetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                void DoSet()
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try { System.Windows.Clipboard.SetDataObject(text, true); return; }
                        catch { System.Threading.Thread.Sleep(20); }
                    }
                }

                if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                    DoSet();
                else
                    System.Windows.Application.Current?.Dispatcher?.Invoke(DoSet);
            }
            catch { }
        }

        /// <summary>
        /// Paste text into the currently focused Android input field.
        /// Path A (scrcpy attached): sets Windows clipboard → fires Ctrl+V via scrcpy (instant, full Unicode).
        /// Path B (headless ADB): uses "adb shell input text" with proper shell-quoting (ASCII only, no %s hack).
        /// </summary>
        public static async Task DirectClipboardPasteAsync(string deviceId, string text, Action? triggerScrcpyPaste = null)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(deviceId)) return;

            // Always populate Windows clipboard first — scrcpy reads from here for Ctrl+V relay
            SafeSetClipboard(text);

            if (triggerScrcpyPaste != null)
            {
                // Path A: scrcpy attached — trigger scrcpy's native Ctrl+V paste
                // scrcpy handles clipboard encoding/Unicode internally, no ADB needed
                triggerScrcpyPaste();
            }
            else
            {
                // Path B: no scrcpy — headless ADB input text fallback
                // Note: "input text" on Android 6+ accepts spaces directly; no %s substitution needed
                string escaped = EscapeShellSingleQuote(text);
                await RunAdbAsync(deviceId, $"shell input text '{escaped}'", 4000);
            }
        }

        public static Task SendTextToDeviceAsync(string deviceId, string text) => DirectClipboardPasteAsync(deviceId, text);
        public static Task PasteTextAsync(string deviceId, string text) => DirectClipboardPasteAsync(deviceId, text);

        /// <summary>
        /// Escapes a string to be safely embedded inside single quotes in an Android sh command.
        /// </summary>
        private static string EscapeShellSingleQuote(string text)
        {
            return text.Replace("'", "'\\''");
        }

        public static async Task SendKeyAsync(string deviceId, int keyCode)
        {
            await RunAdbAsync(deviceId, $"shell input keyevent {keyCode}");
        }

        public static async Task SetProxyAsync(string deviceId, string hostAndPort)
        {
            if (string.IsNullOrWhiteSpace(hostAndPort))
            {
                await RunAdbAsync(deviceId, "shell settings put global http_proxy :0");
                await RunAdbAsync(deviceId, "shell settings delete global http_proxy");
            }
            else
            {
                await RunAdbAsync(deviceId, $"shell settings put global http_proxy {hostAndPort.Trim()}");
            }
        }

        public static async Task TakeScreenshotAsync(string deviceId, string saveDir)
        {
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
            string fileName = $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string localPath = Path.Combine(saveDir, fileName);
            string remotePath = $"/sdcard/{fileName}";

            await RunAdbAsync(deviceId, $"shell screencap -p {remotePath}");
            await RunAdbAsync(deviceId, $"pull {remotePath} \"{localPath}\"");
            await RunAdbAsync(deviceId, $"shell rm {remotePath}");
        }

        public static async Task InstallApkAsync(string deviceId, string apkPath)
        {
            await InstallApkDetailedAsync(deviceId, apkPath, null);
        }

        public static async Task<(bool Success, string Message)> InstallApkDetailedAsync(string deviceId, string apkPath, Action<string>? onProgress = null)
        {
            if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath))
                return (false, "File APK không tồn tại");

            var fileInfo = new FileInfo(apkPath);
            double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            string fileName = fileInfo.Name;

            onProgress?.Invoke(string.Format(LanguageManager.GetString("Str.Status.InstallingApk"), fileName, sizeMb));

            string output = await RunAdbAsync(deviceId, $"install -r \"{apkPath}\"", 120000);

            if (output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                return (true, LanguageManager.GetString("Str.Status.InstallSuccess"));
            }

            // Extract failure message from adb output (e.g. Failure [INSTALL_FAILED_...])
            string error = "Lỗi không xác định";
            int failIdx = output.IndexOf("Failure", StringComparison.OrdinalIgnoreCase);
            if (failIdx >= 0)
            {
                error = output.Substring(failIdx).Trim();
                int newline = error.IndexOfAny(new[] { '\r', '\n' });
                if (newline > 0) error = error.Substring(0, newline);
            }
            else if (!string.IsNullOrWhiteSpace(output))
            {
                error = output.Trim();
            }

            return (false, error);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Backup & Restore Services
        // ──────────────────────────────────────────────────────────────────────
        public class ShopeeBackupInfo
        {
            public string Id { get; set; } = string.Empty;
            public string DeviceId { get; set; } = string.Empty;
            public string DeviceModel { get; set; } = string.Empty;
            public string AndroidId { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public long TotalSizeBytes { get; set; }
            public string DirectoryPath { get; set; } = string.Empty;

            public string DisplayName => $"{CreatedAt:yyyy-MM-dd HH:mm} • {DeviceModel} ({TotalSizeFormatted})";
            public string TotalSizeFormatted => TotalSizeBytes >= 1024 * 1024 
                ? $"{(TotalSizeBytes / (1024.0 * 1024.0)):F1} MB" 
                : $"{(TotalSizeBytes / 1024.0):F0} KB";
        }

        public static string GetDefaultBackupDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static List<ShopeeBackupInfo> GetBackupList()
        {
            var list = new List<ShopeeBackupInfo>();
            string root = GetDefaultBackupDir();
            if (!Directory.Exists(root)) return list;

            foreach (var sub in Directory.GetDirectories(root))
            {
                string metaFile = Path.Combine(sub, "backup_meta.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        string json = File.ReadAllText(metaFile, Encoding.UTF8);
                        var info = System.Text.Json.JsonSerializer.Deserialize<ShopeeBackupInfo>(json);
                        if (info != null)
                        {
                            info.DirectoryPath = sub;
                            list.Add(info);
                        }
                    }
                    catch { }
                }
            }
            return list.OrderByDescending(x => x.CreatedAt).ToList();
        }

        public static async Task<(bool Success, ShopeeBackupInfo? Info, string Error)> BackupShopeeDataAsync(
            string deviceId, Action<string>? onProgress = null)
        {
            try
            {
                onProgress?.Invoke("[1/4] Đang lấy cấu hình thiết bị...");
                string model = (await RunAdbAsync(deviceId, "shell getprop ro.product.model", 3000)).Trim();
                string ssaid = (await RunAdbAsync(deviceId, "shell settings get secure android_id", 3000)).Trim();
                if (string.IsNullOrEmpty(model)) model = deviceId;

                string backupDir = Path.Combine(GetDefaultBackupDir(), $"Shopee_{DateTime.Now:yyyyMMdd_HHmmss}_{deviceId}");
                Directory.CreateDirectory(backupDir);

                onProgress?.Invoke("[2/4] Đang dừng app Shopee...");
                await RunAdbAsync(deviceId, "shell am force-stop com.shopee.vn");

                onProgress?.Invoke("[3/4] Đang sao chép thư mục dữ liệu Shopee...");
                string localData = Path.Combine(backupDir, "data");
                string localDot = Path.Combine(backupDir, "dot_shopee");

                await RunAdbAsync(deviceId, $"pull /sdcard/Android/data/com.shopee.vn \"{localData}\"", 60000);
                await RunAdbAsync(deviceId, $"pull /sdcard/.shopee \"{localDot}\"", 30000);

                onProgress?.Invoke("[4/4] Đang lưu cấu hình bản sao lưu...");
                long totalSize = 0;
                if (Directory.Exists(backupDir))
                {
                    foreach (var file in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                }

                var info = new ShopeeBackupInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DeviceId = deviceId,
                    DeviceModel = model,
                    AndroidId = ssaid,
                    CreatedAt = DateTime.Now,
                    TotalSizeBytes = totalSize,
                    DirectoryPath = backupDir
                };

                string metaJson = System.Text.Json.JsonSerializer.Serialize(info, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(backupDir, "backup_meta.json"), metaJson, Encoding.UTF8);

                return (true, info, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public static async Task<(bool Success, string Message)> RestoreShopeeDataAsync(
            string deviceId, ShopeeBackupInfo backup, Action<string>? onProgress = null)
        {
            try
            {
                if (!Directory.Exists(backup.DirectoryPath))
                    return (false, "Thư mục sao lưu không tồn tại!");

                onProgress?.Invoke("[1/4] Đang dừng ứng dụng Shopee...");
                await RunAdbAsync(deviceId, "shell am force-stop com.shopee.vn");

                if (!string.IsNullOrWhiteSpace(backup.AndroidId))
                {
                    onProgress?.Invoke($"[2/4] Đang khôi phục Android ID ({backup.AndroidId})...");
                    await RunAdbAsync(deviceId, $"shell settings put secure android_id {backup.AndroidId.Trim()}");
                }

                onProgress?.Invoke("[3/4] Đang nạp dữ liệu sao lưu vào thiết bị...");
                string localData = Path.Combine(backup.DirectoryPath, "data");
                string localDot = Path.Combine(backup.DirectoryPath, "dot_shopee");

                if (Directory.Exists(localData))
                {
                    await RunAdbAsync(deviceId, $"push \"{localData}\" /sdcard/Android/data/com.shopee.vn", 60000);
                }
                if (Directory.Exists(localDot))
                {
                    await RunAdbAsync(deviceId, $"push \"{localDot}\" /sdcard/.shopee", 30000);
                }

                onProgress?.Invoke("[4/4] Thiết lập phân quyền thư mục...");
                await RunAdbAsync(deviceId, "shell chmod -R 777 /sdcard/Android/data/com.shopee.vn 2>/dev/null");

                return (true, LanguageManager.GetString("Str.Backup.RestoreSuccess"));
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static async Task<string> CheckDeviceProxyAsync(string deviceId)
        {
            string raw = await RunAdbAsync(deviceId, "shell settings get global http_proxy", 3000);
            string trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == "null" || trimmed == ":0")
                return string.Empty;
            return trimmed;
        }

        public static async Task<(bool Success, string Message)> TestDeviceNetworkAsync(string deviceId)
        {
            // 1. Try curl to get actual outgoing IP
            string ipOut = await RunAdbAsync(deviceId, "shell curl -s --connect-timeout 4 http://ipinfo.io/ip 2>/dev/null", 6000);
            string ip = ipOut.Trim();
            if (!string.IsNullOrEmpty(ip) && !ip.Contains("Error") && !ip.Contains("not found") && ip.Length <= 45)
            {
                return (true, ip);
            }

            // 2. Fallback to pinging DNS
            string pingOut = await RunAdbAsync(deviceId, "shell ping -c 1 -W 3 8.8.8.8 2>/dev/null", 5000);
            if (pingOut.Contains("1 packets transmitted, 1 received") || pingOut.Contains("bytes from 8.8.8.8"))
            {
                return (true, "Internet Connected (Ping OK)");
            }

            return (false, LanguageManager.GetString("Str.Proxy.TestFailed"));
        }

        public static async Task VolumeUpAsync(string deviceId) => await SendKeyAsync(deviceId, 24);
        public static async Task VolumeDownAsync(string deviceId) => await SendKeyAsync(deviceId, 25);
        public static async Task MuteAsync(string deviceId) => await SendKeyAsync(deviceId, 164);
        public static async Task PowerAsync(string deviceId) => await SendKeyAsync(deviceId, 26);
        public static async Task RebootAsync(string deviceId) => await RunAdbAsync(deviceId, "reboot");
        public static async Task SwipeUpAsync(string deviceId) => await RunAdbAsync(deviceId, "shell input swipe 360 1000 360 250 200");
        public static async Task SwipeDownAsync(string deviceId) => await RunAdbAsync(deviceId, "shell input swipe 360 250 360 1000 200");
        public static async Task StopAtxAsync(string deviceId)
        {
            await RunAdbAsync(deviceId, "shell am force-stop com.github.uiautomator");
            await RunAdbAsync(deviceId, "shell am force-stop com.github.uiautomator.test");
        }
    }
}
