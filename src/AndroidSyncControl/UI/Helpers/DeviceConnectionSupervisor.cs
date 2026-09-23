using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidSyncControl.UI.Helpers
{
    public enum ConnectionState
    {
        Initializing,
        Searching,
        DeviceDetected,
        Connecting,
        Connected,
        ConnectionLost,
        Reconnecting,
        AdbUnavailable
    }

    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public ConnectionState State { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public int Attempt { get; set; } = 0;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class DeviceConnectionSupervisor : IDisposable
    {
        private static readonly Lazy<DeviceConnectionSupervisor> _instance =
            new Lazy<DeviceConnectionSupervisor>(() => new DeviceConnectionSupervisor());
        public static DeviceConnectionSupervisor Instance => _instance.Value;

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;
        public event EventHandler<(int width, int height)> DeviceResolutionChanged;

        private ConnectionState _currentState = ConnectionState.Initializing;
        public ConnectionState CurrentState => _currentState;

        public string ActiveDeviceId { get; private set; } = string.Empty;
        public string ActiveDeviceModel { get; private set; } = string.Empty;

        public int DeviceScreenWidth { get; private set; } = 720;
        public int DeviceScreenHeight { get; private set; } = 1280;
        public double DeviceAspectRatio => DeviceScreenHeight > 0 ? (double)DeviceScreenWidth / DeviceScreenHeight : 9.0 / 16.0;

        private CancellationTokenSource _cts;
        private Task _supervisorTask;
        private readonly AutoResetEvent _reconnectSignal = new AutoResetEvent(false);

        // Track-devices background listener
        private Process _trackProc;
        private readonly HashSet<string> _attachedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _deviceLock = new object();
        private readonly AutoResetEvent _deviceChangedSignal = new AutoResetEvent(false);

        // Scrcpy process supervisor
        private Process _scrcpyProc;
        private IntPtr _scrcpyHwnd = IntPtr.Zero;
        private readonly object _scrcpyLock = new object();

        // Window Embedding Delegate
        public Func<IntPtr, bool> EmbedScrcpyAction { get; set; }
        public IntPtr ScrcpyHwnd => _scrcpyHwnd;

        private DeviceConnectionSupervisor() { }

        public void Start()
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;

            _cts = new CancellationTokenSource();
            _supervisorTask = Task.Run(() => SupervisorLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _reconnectSignal.Set();
                _deviceChangedSignal.Set();
            }
            catch { }

            StopTrackDevices();
            KillScrcpy();
        }

        public void RequestReconnectNow()
        {
            _reconnectSignal.Set();
        }

        private void Log(string msg)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "debug_scrcpy.log"), $"[{DateTime.Now:HH:mm:ss.fff}] [Supervisor] {msg}\r\n");
            }
            catch { }
        }

        private void ChangeState(ConnectionState newState, string message = "", int attempt = 0)
        {
            Log($"State -> {newState} (Attempt={attempt}, Msg='{message}', Device='{ActiveDeviceId}')");

            if (_currentState == newState && newState == ConnectionState.Connected)
            {
                return; // Deduplicate connected state
            }

            _currentState = newState;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                State = newState,
                DeviceId = ActiveDeviceId,
                DeviceModel = ActiveDeviceModel,
                Attempt = attempt,
                Message = message
            });
        }

        #region Main Supervisor Loop

        private async Task SupervisorLoopAsync(CancellationToken token)
        {
            ChangeState(ConnectionState.Initializing, "Initializing ADB service...");

            // Start track-devices listener
            StartTrackDevices(token);

            int reconnectAttempt = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1. Get current online device from track-devices or fallback probe
                    string detectedDevice = GetFirstOnlineDevice();

                    if (string.IsNullOrEmpty(detectedDevice))
                    {
                        // One-shot fallback probe in case track-devices is still connecting
                        detectedDevice = await ShopeeBypassService.GetActiveDeviceAsync();
                    }

                    if (string.IsNullOrEmpty(detectedDevice))
                    {
                        ActiveDeviceId = string.Empty;
                        ActiveDeviceModel = string.Empty;
                        reconnectAttempt = 0;

                        ChangeState(ConnectionState.Searching, "Searching for device...");

                        // Event-driven wait: Wait for device plug-in signal, manual retry, or timeout
                        WaitHandle.WaitAny(new[] { _deviceChangedSignal, _reconnectSignal, token.WaitHandle }, 2500);
                        continue;
                    }

                    // 2. Device found
                    ActiveDeviceId = detectedDevice;
                    if (string.IsNullOrEmpty(ActiveDeviceModel) || !ActiveDeviceId.Equals(detectedDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        ActiveDeviceModel = await ShopeeBypassService.GetDeviceModelAsync(ActiveDeviceId);
                    }

                    try
                    {
                        var (resW, resH) = await ShopeeBypassService.GetDeviceResolutionAsync(ActiveDeviceId);
                        if (resW > 0 && resH > 0)
                        {
                            DeviceScreenWidth = resW;
                            DeviceScreenHeight = resH;
                            Log($"Device native resolution: {resW}x{resH} (Ratio: {DeviceAspectRatio:F4})");
                        }
                    }
                    catch { }

                    if (reconnectAttempt > 0)
                    {
                        ChangeState(ConnectionState.Reconnecting, $"Reconnecting... (Attempt {reconnectAttempt})", reconnectAttempt);
                    }
                    else
                    {
                        ChangeState(ConnectionState.DeviceDetected, $"Found {ActiveDeviceModel}");
                        await Task.Delay(200, token);
                        ChangeState(ConnectionState.Connecting, $"Connecting to {ActiveDeviceModel}...");
                    }

                    // 3. Launch scrcpy via ScrcpySupervisor
                    DateTime launchTime = DateTime.UtcNow;
                    var scrcpyResult = await LaunchScrcpyAsync(ActiveDeviceId, ActiveDeviceModel, token);

                    if (scrcpyResult.Success)
                    {
                        reconnectAttempt = 0;
                        ChangeState(ConnectionState.Connected, "Connected");

                        // Wait until scrcpy exits OR device is physically removed from track-devices
                        await MonitorActiveSessionAsync(ActiveDeviceId, token);
                    }
                    else
                    {
                        // Scrcpy launch failed or exited immediately
                        reconnectAttempt++;
                        double runSeconds = (DateTime.UtcNow - launchTime).TotalSeconds;

                        if (runSeconds < 4.0)
                        {
                            // Immediate crash -> Downgrade encoder for this device
                            ScrcpyProfile.MarkHardwareEncoderFailed(ActiveDeviceId);
                        }

                        // Check if device is still attached
                        if (IsDeviceOnline(ActiveDeviceId))
                        {
                            // Calculate capped exponential backoff (1s, 2s, 4s, max 5s)
                            int delayMs = (int)Math.Min(1000 * Math.Pow(2, Math.Max(0, reconnectAttempt - 1)), 5000);
                            ChangeState(ConnectionState.Reconnecting, $"Retrying in {delayMs / 1000}s...", reconnectAttempt);

                            // Wait delayMs OR manual retry signal
                            WaitHandle.WaitAny(new[] { _reconnectSignal, token.WaitHandle }, delayMs);
                        }
                        else
                        {
                            ChangeState(ConnectionState.ConnectionLost, "Device unplugged.");
                            reconnectAttempt = 0;
                            await Task.Delay(1000, token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ChangeState(ConnectionState.AdbUnavailable, $"ADB Error: {ex.Message}");
                    WaitHandle.WaitAny(new[] { _reconnectSignal, token.WaitHandle }, 3000);
                }
            }

            KillScrcpy();
            StopTrackDevices();
        }

        private async Task MonitorActiveSessionAsync(string currentDevice, CancellationToken token)
        {
            if (_scrcpyProc == null || _scrcpyProc.HasExited) return;

            var tcs = new TaskCompletionSource<bool>();
            EventHandler onExited = (s, e) => tcs.TrySetResult(true);
            _scrcpyProc.Exited += onExited;

            if (_scrcpyProc.HasExited) return;

            // Ensure current device is acknowledged in attached devices
            lock (_deviceLock)
            {
                _attachedDevices.Add(currentDevice);
            }

            Log($"Active session monitoring started for device '{currentDevice}' (scrcpy PID: {_scrcpyProc.Id})");

            using (token.Register(() => tcs.TrySetCanceled()))
            {
                while (!tcs.Task.IsCompleted && !token.IsCancellationRequested)
                {
                    var delayTask = Task.Delay(1500, token);
                    var completedTask = await Task.WhenAny(tcs.Task, delayTask);

                    if (completedTask == tcs.Task)
                    {
                        Log($"Scrcpy process exited (ExitCode: {_scrcpyProc?.ExitCode})");
                        break;
                    }

                    // Check if track-devices reported device removed
                    if (!IsDeviceOnline(currentDevice))
                    {
                        Log($"Device '{currentDevice}' is no longer reported online by track-devices.");
                        KillScrcpy();
                        break;
                    }

                    if (_scrcpyProc == null || _scrcpyProc.HasExited)
                    {
                        Log("Scrcpy process is no longer running.");
                        break;
                    }
                }
            }

            try { _scrcpyProc.Exited -= onExited; } catch { }
            KillScrcpy();
        }

        #endregion

        #region Scrcpy Supervisor

        private struct LaunchResult
        {
            public bool Success;
            public string ErrorMessage;
        }

        private async Task<LaunchResult> LaunchScrcpyAsync(string deviceId, string deviceModel, CancellationToken token)
        {
            KillScrcpy();

            string scrcpyExe = FindScrcpyExe();
            if (string.IsNullOrEmpty(scrcpyExe))
            {
                return new LaunchResult { Success = false, ErrorMessage = "scrcpy.exe not found" };
            }

            string screenTitle = $"Android_Screen_{deviceId}_{Environment.TickCount}";
            string args = ScrcpyProfile.BuildArguments(deviceId, screenTitle, deviceModel);

            Log($"LaunchScrcpyAsync starting: {scrcpyExe} {args}");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = scrcpyExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(scrcpyExe)
            };

            psi.EnvironmentVariables["ADB"] = AndroidToolchain.AdbPath;

            StringBuilder stderrLog = new StringBuilder();

            void ProcessScrcpyLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                try
                {
                    // Detect scrcpy texture resolution: "INFO: Texture: 720x1280" or "INFO: New texture: 1280x720"
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"(?:Texture|texture|size)\s*:\s*(\d+)x(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int tw) && int.TryParse(match.Groups[2].Value, out int th))
                    {
                        if (tw > 0 && th > 0 && (tw != DeviceScreenWidth || th != DeviceScreenHeight))
                        {
                            DeviceScreenWidth = tw;
                            DeviceScreenHeight = th;
                            Log($"Scrcpy stream texture updated: {tw}x{th} (Ratio: {DeviceAspectRatio:F4})");
                            DeviceResolutionChanged?.Invoke(this, (tw, th));
                        }
                    }
                }
                catch { }
            }

            lock (_scrcpyLock)
            {
                _scrcpyProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _scrcpyProc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        ProcessScrcpyLine(e.Data);
                    }
                };
                _scrcpyProc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stderrLog.AppendLine(e.Data);
                        ProcessScrcpyLine(e.Data);
                    }
                };

                _scrcpyProc.Start();
                _scrcpyProc.BeginOutputReadLine();
                _scrcpyProc.BeginErrorReadLine();
                Log($"scrcpy process started with PID: {_scrcpyProc.Id}");
            }

            // Find SDL2 window handle (SDL_app)
            IntPtr hwnd = IntPtr.Zero;
            for (int i = 0; i < 40; i++)
            {
                if (token.IsCancellationRequested) return new LaunchResult { Success = false };
                await Task.Delay(200, token);

                if (_scrcpyProc == null || _scrcpyProc.HasExited)
                {
                    string err = stderrLog.ToString();
                    Log($"scrcpy exited early! ExitCode={_scrcpyProc?.ExitCode}, Error={err}");
                    if (err.Contains("MediaCodec$CodecException"))
                    {
                        ScrcpyProfile.MarkHardwareEncoderFailed(deviceId);
                    }
                    return new LaunchResult { Success = false, ErrorMessage = err };
                }

                hwnd = FindWindow("SDL_app", screenTitle);
                if (hwnd != IntPtr.Zero)
                {
                    _scrcpyHwnd = hwnd;
                    Log($"Found SDL2 screen HWND: {hwnd}");
                    break;
                }
            }

            if (hwnd == IntPtr.Zero)
            {
                Log("SDL2 screen HWND timeout!");
                return new LaunchResult { Success = false, ErrorMessage = "Window handle timeout" };
            }

            // Invoke embedding action on UI Thread
            bool embedOk = false;
            if (EmbedScrcpyAction != null)
            {
                embedOk = EmbedScrcpyAction(hwnd);
                Log($"EmbedScrcpyAction returned: {embedOk}");
            }

            return new LaunchResult { Success = embedOk };
        }

        private void KillScrcpy()
        {
            lock (_scrcpyLock)
            {
                try
                {
                    if (_scrcpyProc != null && !_scrcpyProc.HasExited)
                    {
                        _scrcpyProc.Kill();
                    }
                }
                catch { }
                finally
                {
                    _scrcpyProc = null;
                    _scrcpyHwnd = IntPtr.Zero;
                }
            }
        }

        private string FindScrcpyExe() => AndroidToolchain.ScrcpyPath;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        #endregion

        #region Event-driven TrackDevices Listener

        private void StartTrackDevices(CancellationToken token)
        {
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string adb = GetAdbPath();
                        Log($"Starting adb track-devices with {adb}");
                        var psi = new ProcessStartInfo
                        {
                            FileName = adb,
                            Arguments = "track-devices",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = false, // Prevents Windows pipe buffer deadlock
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8
                        };

                        _trackProc = new Process { StartInfo = psi };
                        _trackProc.Start();

                        using (var reader = _trackProc.StandardOutput)
                        {
                            string line;
                            while ((line = await reader.ReadLineAsync()) != null && !token.IsCancellationRequested)
                            {
                                ParseTrackDevicesLine(line);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"TrackDevices exception: {ex.Message}");
                    }

                    // Stream ended or crashed — fallback reconnect in 1.5s
                    await Task.Delay(1500, token);
                }
            }, token);
        }

        private void ParseTrackDevicesLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return;

            // ADB track-devices lines typically look like: "0018<serial>\t<state>" or "<serial>\t<state>"
            string clean = rawLine.Trim();
            if (clean.Length >= 4 && int.TryParse(clean.Substring(0, 4), System.Globalization.NumberStyles.HexNumber, null, out int len))
            {
                clean = clean.Substring(4).Trim();
            }

            lock (_deviceLock)
            {
                if (string.IsNullOrEmpty(clean))
                {
                    Log("TrackDevices: empty device list received (all devices disconnected).");
                    _attachedDevices.Clear();
                }
                else
                {
                    // Line may contain multiple devices or single: serial \t state
                    var parts = clean.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string serial = parts[0].Trim();
                        string state = parts[1].Trim();

                        if (state.Equals("device", StringComparison.OrdinalIgnoreCase))
                        {
                            _attachedDevices.Add(serial);
                        }
                        else
                        {
                            _attachedDevices.Remove(serial);
                        }
                        Log($"TrackDevices update: serial='{serial}', state='{state}', totalAttached={_attachedDevices.Count}");
                    }
                }
            }

            _deviceChangedSignal.Set();
        }

        private void StopTrackDevices()
        {
            try
            {
                if (_trackProc != null && !_trackProc.HasExited)
                {
                    _trackProc.Kill();
                }
            }
            catch { }
            finally
            {
                _trackProc = null;
            }
        }

        private string GetFirstOnlineDevice()
        {
            lock (_deviceLock)
            {
                return _attachedDevices.FirstOrDefault();
            }
        }

        private bool IsDeviceOnline(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return false;
            lock (_deviceLock)
            {
                return _attachedDevices.Contains(serial);
            }
        }

        private string GetAdbPath() => AndroidToolchain.AdbPath;

        #endregion

        public void Dispose()
        {
            Stop();
            _reconnectSignal.Dispose();
            _deviceChangedSignal.Dispose();
        }
    }
}
