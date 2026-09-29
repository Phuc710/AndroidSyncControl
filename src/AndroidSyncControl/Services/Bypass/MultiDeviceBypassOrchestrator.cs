using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidSyncControl.Services.Device;

namespace AndroidSyncControl.Services.Bypass
{
    /// <summary>
    /// Result of a single-device bypass execution within a multi-device run.
    /// </summary>
    public sealed class DeviceBypassResult
    {
        public string DeviceId   { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public bool   Success    { get; init; }
        public string LastStatus { get; init; } = string.Empty;
        public long   ElapsedMs  { get; init; }
        public string? Error     { get; init; }
    }

    /// <summary>
    /// Orchestrates parallel Shopee bypass across multiple connected Android devices.
    ///
    /// Design decisions:
    ///  - Each device gets its own <see cref="ShopeeBypassPipeline"/> instance to avoid
    ///    shared-state races (pipeline holds per-run DeviceEnvironment).
    ///  - MaxParallelDevices is capped at 4 by default; ADB daemon serialises underlying
    ///    USB I/O anyway, so going beyond 4 yields diminishing returns and risks port contention.
    ///  - Status callbacks are device-scoped: UI receives "(Serial) message" strings so it can
    ///    route updates to the correct panel/row without coupling to this orchestrator.
    ///  - CancellationToken propagates to every per-device pipeline — one Cancel() stops all.
    ///
    /// Zero Hardcoding (KR-06): device list is always discovered from live ADB output,
    /// never from a hardcoded serial list.
    /// </summary>
    public sealed class MultiDeviceBypassOrchestrator
    {
        /// <summary>Maximum number of devices that run bypass concurrently.</summary>
        public int MaxParallelDevices { get; set; } = 4;

        /// <summary>Shared config applied to every per-device pipeline.</summary>
        public ShopeeBypassConfig Config { get; set; } = new ShopeeBypassConfig();

        // ── PUBLIC ENTRY POINTS ───────────────────────────────────────────────

        /// <summary>
        /// Runs bypass on every currently connected ADB device in parallel.
        /// Returns one <see cref="DeviceBypassResult"/> per device.
        /// </summary>
        public Task<IReadOnlyList<DeviceBypassResult>> RunOnAllConnectedDevicesAsync(
            Action<string>? globalStatusCallback = null,
            CancellationToken ct = default)
            => RunOnDeviceListAsync(null, globalStatusCallback, ct);

        /// <summary>
        /// Runs bypass on a specific subset of device serials in parallel.
        /// Pass <c>null</c> to target all connected devices (same as <see cref="RunOnAllConnectedDevicesAsync"/>).
        /// </summary>
        public async Task<IReadOnlyList<DeviceBypassResult>> RunOnDeviceListAsync(
            IReadOnlyList<string>? targetSerials,
            Action<string>? globalStatusCallback = null,
            CancellationToken ct = default)
        {
            // Phase 0: Discover live device list (SC-12.2 — Real Data Only)
            List<string> connectedDevices = await DeviceTelemetryService.GetAllConnectedDevicesAsync();

            IEnumerable<string> workList = targetSerials != null
                ? connectedDevices.Intersect(targetSerials)   // honour explicit list but skip offline
                : connectedDevices;

            var devices = workList.ToList();

            if (devices.Count == 0)
            {
                globalStatusCallback?.Invoke("[Multi] Không tìm thấy thiết bị nào đang kết nối.");
                return Array.Empty<DeviceBypassResult>();
            }

            globalStatusCallback?.Invoke(
                $"[Multi] Bắt đầu bypass song song trên {devices.Count} thiết bị " +
                $"(tối đa {MaxParallelDevices} đồng thời)...");

            // Collect results in a thread-safe bag, then order by device serial for stable output.
            var bag = new ConcurrentBag<DeviceBypassResult>();

            // SemaphoreSlim enforces MaxParallelDevices cap.
            using var semaphore = new SemaphoreSlim(MaxParallelDevices, MaxParallelDevices);

            // Resolve friendly model names for all devices upfront (best-effort, non-blocking).
            var modelMap = new ConcurrentDictionary<string, string>();
            await Task.WhenAll(devices.Select(async d =>
            {
                string model = await DeviceTelemetryService.GetDeviceModelAsync(d);
                modelMap[d] = string.IsNullOrWhiteSpace(model) ? d : $"{model} ({d})";
            }));

            // Launch one Task per device — all start immediately but are gated by semaphore.
            var tasks = devices.Select(deviceId => RunSingleDeviceAsync(
                deviceId, modelMap[deviceId], semaphore, bag, globalStatusCallback, ct));

            await Task.WhenAll(tasks);

            // Return in original device discovery order for predictable UI rendering.
            return bag.OrderBy(r => devices.IndexOf(r.DeviceId)).ToList();
        }

        // ── INTERNALS ─────────────────────────────────────────────────────────

        private async Task RunSingleDeviceAsync(
            string deviceId,
            string friendlyName,
            SemaphoreSlim semaphore,
            ConcurrentBag<DeviceBypassResult> bag,
            Action<string>? globalStatusCallback,
            CancellationToken ct)
        {
            await semaphore.WaitAsync(ct);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string lastStatus = string.Empty;

            try
            {
                // Each device gets a fresh pipeline instance — no shared state races.
                var pipeline = new ShopeeBypassPipeline { Config = Config };

                void DeviceCallback(string msg)
                {
                    lastStatus = msg;
                    // Prefix with friendly name so global UI can identify the source device.
                    globalStatusCallback?.Invoke($"[{friendlyName}] {msg}");
                }

                bool success = await pipeline.ExecuteBypassAsync(deviceId, DeviceCallback, ct);
                sw.Stop();

                bag.Add(new DeviceBypassResult
                {
                    DeviceId   = deviceId,
                    DeviceName = friendlyName,
                    Success    = success,
                    LastStatus = lastStatus,
                    ElapsedMs  = sw.ElapsedMilliseconds,
                });
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                bag.Add(new DeviceBypassResult
                {
                    DeviceId   = deviceId,
                    DeviceName = friendlyName,
                    Success    = false,
                    LastStatus = "Đã huỷ (CancellationToken)",
                    ElapsedMs  = sw.ElapsedMilliseconds,
                    Error      = "Cancelled",
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                bag.Add(new DeviceBypassResult
                {
                    DeviceId   = deviceId,
                    DeviceName = friendlyName,
                    Success    = false,
                    LastStatus = $"Lỗi: {ex.Message}",
                    ElapsedMs  = sw.ElapsedMilliseconds,
                    Error      = ex.Message,
                });
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
