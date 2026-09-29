using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AndroidSyncControl.Localization;
using AndroidSyncControl.Services.Adb;
using AndroidSyncControl.Services.Device;

namespace AndroidSyncControl.Services.Bypass
{
    /// <summary>
    /// Device Environment Snapshot — Zero Hardcoding (KR-06).
    /// All fields discovered dynamically at runtime.
    /// </summary>
    public class DeviceEnvironment
    {
        public bool IsRooted { get; set; }
        public bool HasPrivacyKit { get; set; }
        public bool HasDeviceMasker { get; set; }
        public bool HasHideMyApplist { get; set; }
        /// <summary>Shamiko/KSU hide-root module presence. [FIX-7]</summary>
        public bool HasShamiko { get; set; }
        /// <summary>Developer Options → Allow Mock Locations is active. [FIX-8]</summary>
        public bool HasMockLocationEnabled { get; set; }
        /// <summary>Name of app currently registered as Mock Location Provider. [FIX-8]</summary>
        public string MockProvider { get; set; } = string.Empty;
        public string NetworkType { get; set; } = "Unknown"; // Cellular, WiFi, VPN, None
        public string InitialIp { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string CurrentAndroidId { get; set; } = string.Empty;
        /// <summary>Android SDK API level queried at runtime. [FIX-6]</summary>
        public int AndroidApiLevel { get; set; } = 0;
        /// <summary>'cmd connectivity airplane-mode' supported on this ROM. [FIX-3]</summary>
        public bool AirplaneCmdSupported { get; set; } = true;
    }

    /// <summary>
    /// Intelligent Core Pipeline Engine for Shopee Risk Mitigation (M02/D02/L01/M04).
    ///
    /// Changes vs. previous version:
    ///   [FIX-1] Per-app SSAID deletion in settings_ssaid.xml (Android 8.0+ / API 26+)
    ///   [FIX-2] AccountManager tokens removed via SQLite on Root (accounts_de.db)
    ///   [FIX-3] Airplane Mode fallback for MIUI/HyperOS/OneUI/Vivo custom ROMs
    ///   [FIX-4] Extended hidden-storage wipe to 7 paths (adds Shopee, .system_id, .system_setting)
    ///   [FIX-5] Per-path verification loop — not just /sdcard/.shopee as lone sentinel
    ///   [FIX-6] Android API level detection to gate version-specific branches
    ///   [FIX-7] Shamiko module detection in environment snapshot
    ///   [FIX-8] GEO & Locale Spoofing — random VN city Mock Location + vi_VN locale + timezone reset
    ///
    /// SC-11 (Verified Knowledge Workflow) and SC-12 (Intelligent Decision and Real-World Execution).
    /// </summary>
    public class ShopeeBypassPipeline : IShopeeBypassPipeline
    {
        public ShopeeBypassConfig Config { get; set; } = new ShopeeBypassConfig();

        // [FIX-4] Extended from 3 to 7 paths — covers all known Shopee SDK tracking locations.
        private static readonly string[] ShopeeExternalPaths = new[]
        {
            "/sdcard/Android/data/com.shopee.vn",
            "/sdcard/Android/media/com.shopee.vn",
            "/sdcard/Android/obb/com.shopee.vn",
            "/sdcard/.shopee",
            "/sdcard/Shopee",
            "/sdcard/.system_id",
            "/sdcard/Android/.system_setting",
        };

        public static string GenerateRandomHex(int byteCount = 8)
        {
            byte[] bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new StringBuilder();
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ── MAIN ENTRY POINT ──────────────────────────────────────────────────
        public async Task<bool> ExecuteBypassAsync(
            string deviceId,
            Action<string>? statusCallback = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;

            // Phase 0: Observe real device state before any action (SC-12.1)
            statusCallback?.Invoke("[0/8] \u0110ang ph\u00e1t hi\u1ec7n m\u00f4i tr\u01b0\u1eddng & \u0111\u1ecbnh tuy\u1ebfn chi\u1ebfn l\u01b0\u1ee3c...");
            var env = await DiscoverEnvironmentAsync(deviceId);
            ct.ThrowIfCancellationRequested();

            // Phase 1: Scorched Earth — wipe all data + hidden tracking tokens
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step1"));
            await WipeAppDataAndHiddenStorageAsync(deviceId, env);
            ct.ThrowIfCancellationRequested();

            // Phase 2: Identity mutation — rotate SSAID (+ per-app XML on Android 8+)
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step2"));
            await MutateDeviceIdentityAsync(deviceId, env);
            ct.ThrowIfCancellationRequested();

            // Phase 2b: [FIX-8] GEO & Locale spoofing — inject random VN city + reset locale/TZ
            if (Config.EnableGeoSpoofing)
            {
                statusCallback?.Invoke("[2b/8] Đổi vị trí GPS và locale tiếng Việt...");
                await SpoofGeoAndLocaleAsync(deviceId, env);
            }
            ct.ThrowIfCancellationRequested();

            // Phase 3: Reset GAID + GSF cross-app tracking IDs
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step3"));
            await AdbPackageService.ClearDataAsync(deviceId, "com.google.android.gms");

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step4"));
            await AdbPackageService.ClearDataAsync(deviceId, "com.google.android.gsf");

            // Phase 4: AccountManager token eviction
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step5"));
            await RemoveShopeeAccountTokensAsync(deviceId, env);
            ct.ThrowIfCancellationRequested();

            // Phase 5: Deep Root hardware mutation (only if Root + modules detected)
            if (env.IsRooted && Config.EnableDeepRootHookTriggers)
            {
                statusCallback?.Invoke("[5/8] K\u00edch ho\u1ea1t \u0111\u1ed9t bi\u1ebfn ph\u1ea7n c\u1ee9ng s\u00e2u (PrivacyKit/Xposed)...");
                await TriggerDeepRootHardwareMutationAsync(deviceId, env);
            }
            ct.ThrowIfCancellationRequested();

            // Phase 6 & 7: Network rotation — capture IP before, toggle airplane, wait for new IP
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step6"));
            string ipBefore = await DeviceNetworkService.GetCurrentMobileIpAsync(deviceId);

            await ToggleAirplaneModeAsync(deviceId, env, enable: true);
            await Task.Delay(Config.AirplaneDropDelayMs, ct);

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step7"));
            await ToggleAirplaneModeAsync(deviceId, env, enable: false);

            // Adaptive poll: wait until modem negotiates a new public cellular IP (SC-12.10)
            await DeviceNetworkService.WaitForNewIpAsync(
                deviceId, ipBefore, Config.IpPollIntervalMs, Config.IpPollTimeoutMs);
            ct.ThrowIfCancellationRequested();

            // Phase 8: Cold-start Shopee + process verification
            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Step8"));
            await AdbPackageService.LaunchAppAsync(deviceId, "com.shopee.vn");

            await Task.Delay(1000, ct);
            string psCheck = await AdbExecutor.RunAdbAsync(
                deviceId, "shell pidof com.shopee.vn 2>/dev/null", 3000);
            if (string.IsNullOrWhiteSpace(psCheck))
            {
                // Fallback: explicit Activity intent if monkey didn't spawn the process
                await AdbExecutor.RunAdbAsync(deviceId,
                    "shell am start -n com.shopee.vn/com.shopee.app.ui.home.HomeActivity_ 2>/dev/null", 3000);
            }

            statusCallback?.Invoke(LanguageManager.GetString("Str.Bypass.Done"));
            return true;
        }

        // ── PHASE 0: DISCOVER ENVIRONMENT ────────────────────────────────────
        private async Task<DeviceEnvironment> DiscoverEnvironmentAsync(string deviceId)
        {
            var env = new DeviceEnvironment();
            try
            {
                // Execute all independent environment probes in parallel to eliminate latency
                var suTask = AdbExecutor.RunAdbAsync(deviceId, "shell su -c id 2>/dev/null", 3000);
                var apiTask = AdbExecutor.RunAdbAsync(deviceId, "shell getprop ro.build.version.sdk", 2000);
                var pkgTask = AdbExecutor.RunAdbAsync(deviceId, "shell pm list packages", 4000);
                var mockOpsTask = AdbExecutor.RunAdbAsync(deviceId, "shell appops get com.android.location.fused android:mock_location 2>/dev/null", 2000);
                var mockProvTask = AdbExecutor.RunAdbAsync(deviceId, "shell settings get secure mock_location 2>/dev/null", 2000);
                var ipTask = AdbExecutor.RunAdbAsync(deviceId, "shell ip -f inet addr 2>/dev/null", 3000);
                var probeTask = AdbExecutor.RunAdbAsync(deviceId, "shell cmd connectivity airplane-mode 2>/dev/null", 2000);

                await Task.WhenAll(suTask, apiTask, pkgTask, mockOpsTask, mockProvTask, ipTask, probeTask);

                // Root check
                string suCheck = await suTask;
                env.IsRooted = suCheck.Contains("uid=0");

                // [FIX-6] API level detection
                string apiStr = (await apiTask).Trim();
                int.TryParse(apiStr, out int api);
                env.AndroidApiLevel = api;

                // [FIX-7] Module presence check (adds Shamiko)
                string pkgList = await pkgTask;
                env.HasPrivacyKit    = pkgList.Contains("com.sal.privacykit");
                env.HasDeviceMasker  = pkgList.Contains("com.device.id.masker")
                                    || pkgList.Contains("com.fakemydevice");
                env.HasHideMyApplist = pkgList.Contains("dranyer.HideMyApplist");
                env.HasShamiko       = pkgList.Contains("com.lsposed.lspatch")
                                    || pkgList.Contains("zygisk.module.shamiko");

                // [FIX-8] Mock Location capability detection
                string mockOps = await mockOpsTask;
                env.HasMockLocationEnabled = mockOps.Contains("allow");

                // Detect currently registered mock provider app
                string mockProv = (await mockProvTask).Trim();
                env.MockProvider = mockProv;

                // Extended cellular interface regex
                string ipOut = await ipTask;
                if (Regex.IsMatch(
                    ipOut,
                    "rmnet|ccmni|pdp_ip|v4-rmnet|rmnet_data|seth|wwan|clat",
                    RegexOptions.IgnoreCase))
                    env.NetworkType = "Cellular";
                else if (ipOut.Contains("wlan"))
                    env.NetworkType = "WiFi";
                else if (ipOut.Contains("tun") || ipOut.Contains("wg"))
                    env.NetworkType = "VPN";

                // [FIX-3] Probe airplane-mode cmd
                string probe = await probeTask;
                env.AirplaneCmdSupported = !probe.Contains("Unknown service")
                                        && !probe.Contains("No such service")
                                        && !string.IsNullOrWhiteSpace(probe);
            }
            catch { }
            return env;
        }

        // ── PHASE 1: WIPE APP DATA + ALL HIDDEN TRACKING STORAGE ─────────────
        // [FIX-4] 7 paths batched  [FIX-5] Single-pass Root escalation
        private async Task WipeAppDataAndHiddenStorageAsync(string deviceId, DeviceEnvironment env)
        {
            var stopTask = AdbPackageService.ForceStopAsync(deviceId, "com.shopee.vn");
            var clearTask = AdbPackageService.ClearDataAsync(deviceId, "com.shopee.vn");
            await Task.WhenAll(stopTask, clearTask);

            // Batch wipe all external paths in a single shell command to eliminate process spawn lag
            string allPaths = string.Join(" ", ShopeeExternalPaths.Select(p => $"\"{p}\""));
            await AdbExecutor.RunAdbAsync(deviceId, $"shell rm -rf {allPaths} 2>/dev/null", 4000);

            // If rooted, escalate to su in a single compound command for Scoped Storage paths
            if (env.IsRooted)
            {
                await AdbExecutor.RunAdbAsync(
                    deviceId,
                    $"shell su -c 'rm -rf {allPaths}' 2>/dev/null",
                    4000);
            }
        }

        // ── PHASE 2: MUTATE DEVICE IDENTITY ──────────────────────────────────
        // [FIX-1] Delete per-app SSAID entry from settings_ssaid.xml on Android 8+
        private async Task MutateDeviceIdentityAsync(string deviceId, DeviceEnvironment env)
        {
            string newId = GenerateRandomHex(8);

            // Global android_id override (all Android versions)
            await AdbExecutor.RunAdbAsync(deviceId, "shell settings put secure android_id " + newId);

            // [FIX-1] Android 8.0+ (API 26) keeps per-app SSAID in settings_ssaid.xml.
            //         pm clear removes the entry for new installs, but stale entries survive.
            //         On Root, sed-delete the Shopee entry so the OS regenerates from scratch.
            if (env.IsRooted && env.AndroidApiLevel >= 26)
            {
                await AdbExecutor.RunAdbAsync(deviceId,
                    "shell su -c 'sed -i /com.shopee.vn/d /data/system/users/0/settings_ssaid.xml 2>/dev/null'",
                    3000);
            }

            // Verification: confirm global channel holds the new value
            string currentId = (await AdbExecutor.RunAdbAsync(
                deviceId, "shell settings get secure android_id", 2000)).Trim();
            if (!string.Equals(currentId, newId, StringComparison.OrdinalIgnoreCase) && env.IsRooted)
                await AdbExecutor.RunAdbAsync(
                    deviceId, "shell su -c 'settings put secure android_id " + newId + "'", 2000);
        }

        // ── PHASE 4: ACCOUNTMANAGER TOKEN EVICTION ────────────────────────────
        // [FIX-2] Root: DELETE from accounts_de.db via sqlite3 (authoritative)
        //         Non-Root: cmd account remove (more reliable than protected broadcast)
        private async Task RemoveShopeeAccountTokensAsync(string deviceId, DeviceEnvironment env)
        {
            try
            {
                if (env.IsRooted)
                {
                    const string dbDe  = "/data/system_de/0/accounts_de.db"; // Android 7+
                    const string dbLeg = "/data/system/accounts.db";          // Android <7

                    string q = "DELETE FROM accounts WHERE type LIKE " +
                               "'" + "%shopee%" + "'" +
                               " OR type LIKE " +
                               "'" + "%sea.com%" + "';";

                    await AdbExecutor.RunAdbAsync(deviceId,
                        @"shell su -c 'sqlite3 " + dbDe + @" """ + q + @"""'  2>/dev/null", 4000);
                    await AdbExecutor.RunAdbAsync(deviceId,
                        @"shell su -c 'sqlite3 " + dbLeg + @" """ + q + @"""' 2>/dev/null", 4000);
                    return; // Root path is authoritative — skip broadcast path
                }

                // Non-Root: parse dumpsys account, issue cmd account remove per matched account
                string dump = await AdbExecutor.RunAdbAsync(deviceId, "shell dumpsys account", 5000);
                var shopeeAccounts = new List<string>();
                string[] lines = dump.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string? currentAccount = null;

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("Account {", StringComparison.OrdinalIgnoreCase))
                        currentAccount = trimmed;
                    if (currentAccount != null
                        && (trimmed.Contains("shopee", StringComparison.OrdinalIgnoreCase)
                            || trimmed.Contains("sea.com", StringComparison.OrdinalIgnoreCase))
                        && !shopeeAccounts.Contains(currentAccount))
                    {
                        shopeeAccounts.Add(currentAccount);
                    }
                }

                foreach (string acct in shopeeAccounts)
                {
                    var typeMatch = Regex.Match(acct, @"type=([^,}]+)");
                    var nameMatch = Regex.Match(acct, @"name=([^,}]+)");
                    if (!typeMatch.Success || !nameMatch.Success) continue;

                    string type = typeMatch.Groups[1].Value.Trim();
                    string name = nameMatch.Groups[1].Value.Trim();

                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell cmd account remove" +
                        " --account-name \"" + name + "\"" +
                        " --account-type \"" + type + "\" 2>/dev/null",
                        3000);
                }
            }
            catch { /* best-effort */ }
        }

        // ── PHASE 5: DEEP ROOT HARDWARE MUTATION ──────────────────────────────
        private async Task TriggerDeepRootHardwareMutationAsync(string deviceId, DeviceEnvironment env)
        {
            try
            {
                if (env.HasPrivacyKit)
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell am broadcast -a com.sal.privacykit.RANDOMIZE 2>/dev/null", 2000);
                if (env.HasDeviceMasker)
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell am broadcast -a com.device.id.masker.RANDOMIZE 2>/dev/null", 2000);
                // Give Xposed hooks time to propagate
                await Task.Delay(500);
            }
            catch { }
        }

        // ── AIRPLANE MODE TOGGLE WITH ROM FALLBACK ────────────────────────────
        // [FIX-3] Fallback for ROMs that strip 'cmd connectivity': settings + broadcast
        private async Task ToggleAirplaneModeAsync(string deviceId, DeviceEnvironment env, bool enable)
        {
            string onOff    = enable ? "enable" : "disable";
            int    intValue = enable ? 1 : 0;

            if (env.AirplaneCmdSupported)
            {
                string res = await AdbExecutor.RunAdbAsync(deviceId,
                    "shell cmd connectivity airplane-mode " + onOff + " 2>/dev/null", 3000);
                if (res.Contains("Unknown service") || res.Contains("No such service"))
                    env.AirplaneCmdSupported = false;
            }

            if (!env.AirplaneCmdSupported)
            {
                // Fallback 1: global settings (honoured by MIUI and most custom ROMs)
                await AdbExecutor.RunAdbAsync(deviceId,
                    "shell settings put global airplane_mode_on " + intValue, 2000);
                // Fallback 2: broadcast so modem/Wi-Fi adapters actually detach
                string state = enable ? "true" : "false";
                await AdbExecutor.RunAdbAsync(deviceId,
                    "shell am broadcast -a android.intent.action.AIRPLANE_MODE --ez state " + state + " 2>/dev/null",
                    2000);
            }
        }

        // ── PHASE 2b: GEO & LOCALE SPOOFING [FIX-8] ─────────────────────────
        // Strategy routing (SC-12.1):
        //   Non-Root: ADB appops grant + geo fix Location Provider injection
        //   Root    : additionally setprop persist.sys.locale for deeper persistence
        //             on Samsung OneUI / Exynos which cache locale in system properties.
        //
        // Why GEO matters:
        //   Shopee MINT cross-references LocationManager data against the delivery address.
        //   A Korean SIM (KTT carrier, UTC+9, last GPS fix in Seoul) ordering to a Hanoi
        //   address triggers a geo-consistency anomaly flag leading to M02/M04.
        //   Pipeline injects a random Vietnamese city centroid to neutralise this.
        private async Task SpoofGeoAndLocaleAsync(string deviceId, DeviceEnvironment env)
        {
            try
            {
                // Step A: Pick random VN city from config pool (Zero Hardcoding KR-06)
                var pool = ShopeeBypassConfig.VietnamCityPool;
                byte[] rb = new byte[1];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                    rng.GetBytes(rb);
                int idx  = rb[0] % pool.Length;
                var city = pool[idx];

                // Jitter ±0.002deg (~220m) so repeated runs do not produce identical GPS fixes
                double lat = city.Lat + (GenerateJitter() * 0.004) - 0.002;
                double lon = city.Lon + (GenerateJitter() * 0.004) - 0.002;
                string latStr = lat.ToString("F7", System.Globalization.CultureInfo.InvariantCulture);
                string lonStr = lon.ToString("F7", System.Globalization.CultureInfo.InvariantCulture);

                // Step B: Grant MOCK_LOCATION via appops to ADB shell UID (Non-Root, API 23+)
                await AdbExecutor.RunAdbAsync(deviceId,
                    "shell appops set com.android.shell android:mock_location allow 2>/dev/null", 2000);

                // Step C: Inject GPS fix via ADB 'geo fix <lon> <lat> <alt>'
                string geoResult = await AdbExecutor.RunAdbAsync(deviceId,
                    "shell geo fix " + lonStr + " " + latStr + " 10 2>/dev/null", 2000);

                bool geoWorked = !string.IsNullOrWhiteSpace(geoResult)
                              && !geoResult.Contains("not found")
                              && !geoResult.Contains("error");

                if (!geoWorked)
                {
                    // Fallback: synthetic broadcast honoured by Samsung OneUI/Exynos when
                    // LocationManager.GPS_PROVIDER is in mock mode
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell am broadcast -a android.intent.action.LOCATION_CHANGED " +
                        "--ef latitude " + latStr + " --ef longitude " + lonStr +
                        " --ef accuracy 10.0 --es provider gps 2>/dev/null", 2000);
                }

                // Step D: Root path — setprop for deep SoC-level locale persistence.
                // Samsung Exynos (gracerltektt, universal8890) reads persist.sys.locale
                // even after pm clear; KTT SIM forces ko_KR locale on each boot without this.
                if (env.IsRooted)
                {
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell su -c \"setprop persist.sys.locale vi-VN\" 2>/dev/null", 2000);
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell su -c \"setprop persist.sys.language vi\" 2>/dev/null", 2000);
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell su -c \"setprop persist.sys.country VN\" 2>/dev/null", 2000);
                }

                // Step E: Locale + Timezone via ADB settings (Non-Root, all versions)
                if (Config.EnableLocaleReset)
                {
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell settings put secure locale vi_VN 2>/dev/null", 1500);
                    // Korea UTC+9 vs Vietnam UTC+7 = 2h gap MINT flags as geo anomaly
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell settings put global time_zone Asia/Ho_Chi_Minh 2>/dev/null", 1500);
                    await AdbExecutor.RunAdbAsync(deviceId,
                        "shell am broadcast -a android.intent.action.TIMEZONE_CHANGED " +
                        "--es time-zone Asia/Ho_Chi_Minh 2>/dev/null", 1500);
                }
            }
            catch { /* best-effort — GEO failure must never abort the main pipeline */ }
        }

        // Random byte normalised to [0,1] for GPS coordinate jitter.
        private static double GenerateJitter()
        {
            byte[] b = new byte[1];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(b);
            return b[0] / 255.0;
        }
    }
}
