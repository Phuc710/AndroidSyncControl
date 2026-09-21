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

            // ─── Approach 1: ADBKeyboard app (best Unicode/Vietnamese support) ───
            // Optional: install https://github.com/senzhk/ADBKeyBoard on device for best results.
            // Broadcasts ADB_INPUT_TEXT action — result=-1 means not installed, skip.
            string adbKbResult = await RunAdbAsync(deviceId,
                $"shell am broadcast -a ADB_INPUT_TEXT --es msg '{EscapeShellSingleQuote(text)}' 2>/dev/null", 5000);
            if (adbKbResult != null && adbKbResult.Contains("result=0"))
                return;

            // ─── Approach 2: Stdin piping — bypasses ALL shell argument escaping ───
            // We write the shell command `input text <text>` to the process stdin of `adb shell`.
            // This completely avoids argv escaping on the Windows side.
            // On the Android side, the shell receives the command via its stdin (not as an argv
            // argument to adb), so the only escaping needed is within the Android sh context.
            // We use a base64-decoded here-string trick to avoid any sh quoting issues entirely.
            bool stdinOk = await RunAdbInputTextViaStdinAsync(deviceId, text);
            if (stdinOk) return;

            // ─── Approach 3: Push text to /sdcard temp file, read back and input ───
            // Used when the above two fail. Works on all Android versions.
            bool fileOk = await PushAndTypeTextAsync(deviceId, text);
            if (fileOk) return;

            // ─── Approach 4: Fallback — BuildInputTextArg (ASCII-safe, best-effort for Unicode) ───
            string escaped = BuildInputTextArg(text);
            await RunAdbAsync(deviceId, $"shell input text {escaped}", 8000);
        }

        /// <summary>
        /// Pipes the input text command through the adb shell process stdin.
        /// This avoids all Windows-side argument escaping — text goes through binary-clean
        /// to the Android shell. We encode via base64 so no quoting is needed in the shell cmd.
        /// </summary>
        private static async Task<bool> RunAdbInputTextViaStdinAsync(string deviceId, string text)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string adb = GetAdbPath();
                    string devPart = string.IsNullOrEmpty(deviceId) ? "" : $"-s {deviceId} ";

                    // Encode text to base64 so the sh command has zero quoting problems
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

                    // Shell one-liner written to stdin:
                    //   1. Decode b64 to a shell variable $T
                    //   2. Call `input text "$T"` — $T is never shell-expanded because we use
                    //      a variable, so only the leading/trailing " need to be safe,
                    //      and $T is set from b64 decode which is guaranteed safe.
                    // NOTE: `input text` on Android reads its argument as raw UTF-8 bytes,
                    // so Vietnamese characters in $T go through intact.
                    string cmd = $"T=$(echo '{b64}' | base64 -d) && input text \"$T\"\n";
                    byte[] cmdBytes = Encoding.UTF8.GetBytes(cmd);

                    var psi = new ProcessStartInfo
                    {
                        FileName = adb,
                        Arguments = $"{devPart}shell",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using (var proc = new Process { StartInfo = psi })
                    {
                        proc.Start();

                        // Write command to stdin and close it so the shell exits
                        proc.StandardInput.BaseStream.Write(cmdBytes, 0, cmdBytes.Length);
                        proc.StandardInput.Close();

                        bool exited = proc.WaitForExit(8000);
                        if (!exited) { try { proc.Kill(); } catch { } return false; }
                        return proc.ExitCode == 0;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Push text to /sdcard/asc_paste.tmp, then run `input text "$(cat /sdcard/asc_paste.tmp)"`.
        /// Works on Android 5+ with no third-party apps. Handles full Unicode via file push.
        /// </summary>
        private static async Task<bool> PushAndTypeTextAsync(string deviceId, string text)
        {
            try
            {
                // Write text to a local temp file
                string tmpLocal = Path.Combine(Path.GetTempPath(), "asc_paste.tmp");
                File.WriteAllText(tmpLocal, text, new UTF8Encoding(false)); // UTF-8 no BOM

                // Push to device
                string pushResult = await RunAdbAsync(deviceId, $"push \"{tmpLocal}\" /sdcard/asc_paste.tmp", 5000);
                if (pushResult == null || pushResult.Contains("error")) return false;

                // Read file content into a variable and call input text
                // This avoids any argument escaping — the file content is the raw UTF-8 text
                string runResult = await RunAdbAsync(deviceId,
                    "shell T=$(cat /sdcard/asc_paste.tmp) && input text \"$T\"", 8000);

                // Cleanup
                await RunAdbAsync(deviceId, "shell rm /sdcard/asc_paste.tmp 2>/dev/null", 3000);
                try { File.Delete(tmpLocal); } catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds a properly-escaped argument string for `adb shell input text`.
        /// Used as last-resort fallback. Vietnamese/Unicode chars pass through as raw UTF-8.
        /// Shell metacharacters are backslash-escaped; spaces become %s (input text convention).
        /// </summary>
        private static string BuildInputTextArg(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                switch (c)
                {
                    case ' ':  sb.Append("%s"); break;
                    case '\t': sb.Append("%t"); break;
                    case '\n': sb.Append("%n"); break;
                    case '\r': break;
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\'': sb.Append("\\'"); break;
                    case '`':  sb.Append("\\`"); break;
                    case '$':  sb.Append("\\$"); break;
                    case '&':  sb.Append("\\&"); break;
                    case '|':  sb.Append("\\|"); break;
                    case ';':  sb.Append("\\;"); break;
                    case '<':  sb.Append("\\<"); break;
                    case '>':  sb.Append("\\>"); break;
                    case '(':  sb.Append("\\("); break;
                    case ')':  sb.Append("\\)"); break;
                    default:   sb.Append(c); break;
                }
            }
            return $"\"{sb}\"";
        }

        /// <summary>
        /// Escapes a string to be safely embedded inside single quotes in an Android sh command.
        /// Rule: end the single-quote region, insert \', reopen single-quote region.
        /// Example: it's → 'it'\''s'
        /// </summary>
        private static string EscapeShellSingleQuote(string text)
        {
            // 'it'\''s' pattern: end quote, literal \', reopen quote
            return text.Replace("'", "'\\'' ");
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
