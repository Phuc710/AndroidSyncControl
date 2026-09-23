using System;
using System.Diagnostics;
using System.IO;
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

        public static async Task<bool> BypassShopeeAsync(string deviceId, Action<string> statusCallback = null)
        {
            string oldId = (await RunAdbAsync(deviceId, "shell settings get secure android_id")).Trim();
            
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step1"));
            await RunAdbAsync(deviceId, "shell am force-stop com.shopee.vn");
            await RunAdbAsync(deviceId, "shell pm clear com.shopee.vn");
            // Dọn sạch thư mục external storage của Shopee nếu còn sót
            await RunAdbAsync(deviceId, "shell rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/.shopee 2>/dev/null");

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step2"));
            string newId = GenerateRandomHex(8);
            await RunAdbAsync(deviceId, $"shell settings put secure android_id {newId}");
            string verifyId = (await RunAdbAsync(deviceId, "shell settings get secure android_id")).Trim();

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step3"));
            await RunAdbAsync(deviceId, "shell pm clear com.google.android.gms");

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step4"));
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode enable");
            await Task.Delay(3500);

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step5"));
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode disable");
            await Task.Delay(3500); // Chờ SIM 4G nhận IP mới

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step6"));
            await RunAdbAsync(deviceId, "shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1 2>/dev/null");

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Done"));
            return true;
        }

        public static async Task RotateAirplaneModeAsync(string deviceId)
        {
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode enable");
            await Task.Delay(3000);
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode disable");
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
        public static async Task DirectClipboardPasteAsync(string deviceId, string text)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(deviceId)) return;

            // 1. Ensure Windows Clipboard holds the exact text with retry
            try
            {
                void SafeSetClipboard(string val)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            if (System.Windows.Clipboard.GetText() != val)
                            {
                                System.Windows.Clipboard.SetText(val);
                            }
                            break;
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(20);
                        }
                    }
                }

                if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    SafeSetClipboard(text);
                }
                else
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try { SafeSetClipboard(text); } catch { }
                    });
                }
            }
            catch { }

            // 2. Direct Android Device Clipboard Channel (API 29+ Android 10+)
            string clipCmd = $"shell cmd clipboard set-text '{EscapeShellSingleQuote(text)}' 2>/dev/null";
            _ = RunAdbAsync(deviceId, clipCmd, 1200);

            // 3. Short grace period for scrcpy socket sync
            await Task.Delay(50);

            // 4. Trigger Instant Atomic Paste via KEYCODE_PASTE (279)
            await RunAdbAsync(deviceId, "shell input keyevent 279", 3000);
        }

        // Aliases for compatibility
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
            if (File.Exists(apkPath))
            {
                await RunAdbAsync(deviceId, $"install -r \"{apkPath}\"", 60000);
            }
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
