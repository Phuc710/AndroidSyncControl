using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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
            sb.AppendLine($"Android ID:     {androidId}");
            sb.AppendLine($"Model:          {model}");
            sb.AppendLine($"Serial:         {serial}");
            sb.AppendLine("────────────────────────────────");

            if (!string.IsNullOrEmpty(vpnIp))
            {
                sb.AppendLine($"VPN:  Đã kết nối [{vpnIp}]");
                sb.AppendLine($"IP Wi-Fi (LAN): {wlanIp}");
            }
            else if (!string.IsNullOrEmpty(mobileIp))
            {
                sb.AppendLine("VPN:  Chưa bật ");
                sb.AppendLine($"IP 4G / LTE:    {mobileIp}");
            }
            else
            {
                sb.AppendLine("VPN:  Chưa bật");
                if (!string.IsNullOrEmpty(wlanIp))
                {
                    sb.AppendLine($"IP Wi-Fi (LAN): {wlanIp}");
                }
            }

            return sb.ToString();
        }

        public static async Task<bool> BypassShopeeAsync(string deviceId, Action<string> statusCallback = null)
        {
            string oldId = (await RunAdbAsync(deviceId, "shell settings get secure android_id")).Trim();
            
            statusCallback?.Invoke("1/6: Dừng & Xóa sạch Session Token Shopee...");
            await RunAdbAsync(deviceId, "shell am force-stop com.shopee.vn");
            await RunAdbAsync(deviceId, "shell pm clear com.shopee.vn");
            // Dọn sạch thư mục external storage của Shopee nếu còn sót
            await RunAdbAsync(deviceId, "shell rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/.shopee 2>/dev/null");

            statusCallback?.Invoke("2/6: Sinh & Nạp Android ID (SSAID) mới...");
            string newId = GenerateRandomHex(8);
            await RunAdbAsync(deviceId, $"shell settings put secure android_id {newId}");
            string verifyId = (await RunAdbAsync(deviceId, "shell settings get secure android_id")).Trim();

            statusCallback?.Invoke("3/6: Reset Google Advertising ID (GAID)...");
            await RunAdbAsync(deviceId, "shell pm clear com.google.android.gms");

            statusCallback?.Invoke("4/6: Đổi IP mạng (Airplane Mode ON)...");
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode enable");
            await Task.Delay(3500);

            statusCallback?.Invoke("5/6: Kết nối mạng mới (Airplane Mode OFF)...");
            await RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode disable");
            await Task.Delay(3500); // Chờ SIM 4G nhận IP mới

            statusCallback?.Invoke("6/6: Khởi chạy Shopee sạch...");
            await RunAdbAsync(deviceId, "shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1 2>/dev/null");

            statusCallback?.Invoke($"Bypass Thành Công! ({oldId.Substring(0, Math.Min(6, oldId.Length))}... -> {newId})");
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

        public static async Task PasteTextAsync(string deviceId, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Method 1: Clipboard broadcast (supports Vietnamese, Unicode, special chars)
            // Encode text as base64 to avoid shell escaping hell
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            string decodeCmd = $"shell eval \"echo '{b64}' | base64 -d | tr -d '\\\\n' | xclip -selection clipboard 2>/dev/null || true\"";

            // Use am broadcast to set clipboard directly — works on Android 7+
            // Escape for adb shell: single-quote wrap, escape single quotes inside
            string shellEscaped = text
                .Replace("\\", "\\\\")
                .Replace("'", "'\\''")
                .Replace("\n", "\\n")
                .Replace("\r", "");

            string broadcastCmd = $"shell am broadcast -a clipper.set -e text '{shellEscaped}' 2>/dev/null";
            string broadcastResult = await RunAdbAsync(deviceId, broadcastCmd, 5000);

            // Fallback Method 2: input text with proper escaping (ASCII-safe text only)
            bool usedBroadcast = broadcastResult != null && broadcastResult.Contains("result=0");
            if (!usedBroadcast)
            {
                // adb shell input text — safe only for ASCII, but best effort fallback
                string escaped = text
                    .Replace("\\", "\\\\")
                    .Replace(" ", "%s")
                    .Replace("&", "\\&")
                    .Replace("<", "\\<")
                    .Replace(">", "\\>")
                    .Replace("\"", "\\\"")
                    .Replace("'", "\\'");
                await RunAdbAsync(deviceId, $"shell input text \"{escaped}\"");
                return;
            }

            // Trigger paste via Ctrl+V (keyevent 279 = KEYCODE_PASTE)
            await Task.Delay(200);
            await RunAdbAsync(deviceId, "shell input keyevent 279");
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
