using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AndroidSyncControl.Services.Adb;

namespace AndroidSyncControl.Services.Device
{
    /// <summary>
    /// Service for reading and querying Android device telemetry, identity, display resolution, and network interfaces.
    /// Follows KR-06: Zero Hardcoding - all properties queried dynamically at runtime.
    /// </summary>
    public static class DeviceTelemetryService
    {
        public static async Task<string> GetActiveDeviceAsync()
        {
            try
            {
                string outDevices = await AdbExecutor.RunAdbAsync(string.Empty, "devices", 3000);
                foreach (var line in outDevices.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("*") || line.StartsWith("List of") || string.IsNullOrWhiteSpace(line)) continue;
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

        /// <summary>
        /// Returns ALL currently online ADB devices (not just the first one).
        /// Used by MultiDeviceBypassOrchestrator for parallel multi-phone bypass.
        /// </summary>
        public static async Task<List<string>> GetAllConnectedDevicesAsync()
        {
            var result = new List<string>();
            try
            {
                string outDevices = await AdbExecutor.RunAdbAsync(string.Empty, "devices", 3000);
                foreach (var line in outDevices.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("*") || line.StartsWith("List of") || string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[1].Equals("device", StringComparison.OrdinalIgnoreCase))
                        result.Add(parts[0].Trim());
                }
            }
            catch { }
            return result;
        }

        public static async Task<string> GetDeviceModelAsync(string deviceId)
        {
            try
            {
                string model = (await AdbExecutor.RunAdbAsync(deviceId, "shell getprop ro.product.model", 3000)).Trim();
                if (!string.IsNullOrEmpty(model)) return model;
            }
            catch { }
            return "Android Device";
        }

        public static async Task<(int width, int height)> GetDeviceResolutionAsync(string deviceId)
        {
            try
            {
                string outStr = await AdbExecutor.RunAdbAsync(deviceId, "shell wm size", 3000);
                int baseW = 720, baseH = 1280;
                foreach (var line in outStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = Regex.Match(line, @"(\d+)\s*x\s*(\d+)");
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
                    string rotOut = await AdbExecutor.RunAdbAsync(deviceId, "shell dumpsys window", 3000);
                    var rotMatch = Regex.Match(rotOut, @"mCurrentRotation=ROTATION_(\d+)");
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
            // Execute all 4 queries in parallel to eliminate sequential process spawn latency
            var androidIdTask = AdbExecutor.RunAdbAsync(deviceId, "shell settings get secure android_id", 3000);
            var modelTask = AdbExecutor.RunAdbAsync(deviceId, "shell getprop ro.product.model", 3000);
            var serialTask = AdbExecutor.RunAdbAsync(deviceId, "shell getprop ro.serialno", 3000);
            var ipTask = AdbExecutor.RunAdbAsync(deviceId, "shell ip -f inet addr 2>/dev/null", 3000);

            await Task.WhenAll(androidIdTask, modelTask, serialTask, ipTask);

            string androidId = (await androidIdTask).Trim();
            string model = (await modelTask).Trim();
            string serial = (await serialTask).Trim();
            string ipOut = await ipTask;

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
                        currentIface = parts[1].Split(':')[0].Trim();
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
                        // Extended multi-chipset cellular detection (matches DeviceNetworkService):
                        // Qualcomm (A02s Snapdragon): rmnet_data, v4-rmnet, rmnet
                        // Samsung Exynos (J3 Pro, Note FE): ccmni, pdp_ip
                        // Unisoc/Spreadtrum: seth
                        // USB modem / dongle: wwan, data0
                        // IPv6 translation: clat
                        else if (System.Text.RegularExpressions.Regex.IsMatch(
                                     currentIface,
                                     @"rmnet|ccmni|pdp_ip|v4-rmnet|rmnet_data|seth|wwan|data\d|clat",
                                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
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
    }
}
