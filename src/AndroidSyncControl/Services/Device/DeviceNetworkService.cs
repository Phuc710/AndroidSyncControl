using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AndroidSyncControl.Localization;
using AndroidSyncControl.Services.Adb;

namespace AndroidSyncControl.Services.Device
{
    /// <summary>
    /// Service for managing device proxy settings, network state testing, and airplane mode IP rotation.
    /// Incorporates adaptive polling to avoid race conditions during cellular re-registration.
    /// </summary>
    public static class DeviceNetworkService
    {
        public static async Task SetProxyAsync(string deviceId, string hostAndPort)
        {
            if (string.IsNullOrWhiteSpace(hostAndPort))
            {
                await AdbExecutor.RunAdbAsync(deviceId, "shell settings put global http_proxy :0");
                await AdbExecutor.RunAdbAsync(deviceId, "shell settings delete global http_proxy");
            }
            else
            {
                await AdbExecutor.RunAdbAsync(deviceId, $"shell settings put global http_proxy {hostAndPort.Trim()}");
            }
        }

        public static async Task<string> CheckDeviceProxyAsync(string deviceId)
        {
            string raw = await AdbExecutor.RunAdbAsync(deviceId, "shell settings get global http_proxy", 3000);
            string trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == "null" || trimmed == ":0")
                return string.Empty;
            return trimmed;
        }

        public static async Task<(bool Success, string Message)> TestDeviceNetworkAsync(string deviceId)
        {
            // 1. Try curl to get actual outgoing IP
            string ipOut = await AdbExecutor.RunAdbAsync(deviceId, "shell curl -s --connect-timeout 4 http://ipinfo.io/ip 2>/dev/null", 6000);
            string ip = ipOut.Trim();
            if (!string.IsNullOrEmpty(ip) && !ip.Contains("Error") && !ip.Contains("not found") && ip.Length <= 45)
            {
                return (true, ip);
            }

            // 2. Fallback to pinging DNS
            string pingOut = await AdbExecutor.RunAdbAsync(deviceId, "shell ping -c 1 -W 3 8.8.8.8 2>/dev/null", 5000);
            if (pingOut.Contains("1 packets transmitted, 1 received") || pingOut.Contains("bytes from 8.8.8.8"))
            {
                return (true, "Internet Connected (Ping OK)");
            }

            return (false, LanguageManager.GetString("Str.Proxy.TestFailed"));
        }

        public static async Task<string> GetCurrentMobileIpAsync(string deviceId)
        {
            try
            {
                string ipOut = await AdbExecutor.RunAdbAsync(deviceId, "shell ip -f inet addr 2>/dev/null", 5000);
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
                        // [FIX-2] Extended regex covers:
                        //   rmnet*, ccmni*  — Qualcomm older / MediaTek
                        //   pdp_ip*         — Samsung Exynos
                        //   v4-rmnet*, rmnet_data*, rmnet_ipa* — Qualcomm Android 12+
                        //   seth*           — Unisoc / Spreadtrum
                        //   wwan*, data0    — USB modems / SIM dongles / cloud phones
                        //   clat*           — IPv4-in-IPv6 (464XLAT) adapter
                        if (Regex.IsMatch(currentIface,
                            @"^(rmnet|ccmni|pdp_ip|v4-rmnet|rmnet_data|rmnet_ipa|seth|wwan|data|clat)",
                            RegexOptions.IgnoreCase))
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

        public static async Task WaitForNewIpAsync(
            string deviceId,
            string previousIp,
            int pollIntervalMs = 1500,
            int timeoutMs = 25000)
        {
            // If previousIp was empty, device had no mobile IP before — wait flat minimum.
            if (string.IsNullOrEmpty(previousIp))
            {
                await Task.Delay(Math.Min(pollIntervalMs * 3, 5000));
                return;
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(pollIntervalMs);
                string currentIp = await GetCurrentMobileIpAsync(deviceId);
                // Success: IP exists and differs from the pre-airplane address
                if (!string.IsNullOrEmpty(currentIp) && currentIp != previousIp)
                    return;
            }
            // Timeout — proceed anyway; caller will handle verification
        }

        public static async Task RotateAirplaneModeAsync(string deviceId)
        {
            string ipBefore = await GetCurrentMobileIpAsync(deviceId);
            await AdbExecutor.RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode enable");
            await Task.Delay(2000);
            await AdbExecutor.RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode disable");
            await WaitForNewIpAsync(deviceId, ipBefore, pollIntervalMs: 1500, timeoutMs: 20000);
        }
    }
}
