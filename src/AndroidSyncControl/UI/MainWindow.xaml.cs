using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using AndroidSyncControl.UI.Helpers;

namespace AndroidSyncControl.UI
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint WM_ACTIVATE = 0x0006;
        private const int WA_ACTIVE = 1;

        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int SW_SHOW = 5;

        private const uint WM_SETFOCUS = 0x0007;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_CHAR = 0x0102;

        private IntPtr _scrcpyHwnd = IntPtr.Zero;
        private IntPtr _mainHwnd = IntPtr.Zero;          // cached once in Loaded — avoids COM interop per FocusScrcpy call
        private uint _attachedScrcpyThreadId = 0;
        private PanelClickFilter? _panelFilter;
        private System.Windows.Threading.DispatcherTimer _resizeTimer; // debounce Win32 MoveWindow floods
        private static readonly string _logPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_scrcpy.log");

        private sealed class PanelClickFilter : System.Windows.Forms.NativeWindow
        {
            private readonly Action _onActivate;
            private const int WM_MOUSEACTIVATE = 0x0021;
            private const int WM_PARENTNOTIFY = 0x0210;
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_RBUTTONDOWN = 0x0204;
            private const int WM_MBUTTONDOWN = 0x0207;

            public PanelClickFilter(IntPtr handle, Action onActivate)
            {
                _onActivate = onActivate;
                AssignHandle(handle);
            }

            protected override void WndProc(ref System.Windows.Forms.Message m)
            {
                if (m.Msg == WM_MOUSEACTIVATE)
                {
                    _onActivate?.Invoke();
                }
                else if (m.Msg == WM_PARENTNOTIFY)
                {
                    int eventId = m.WParam.ToInt32() & 0xFFFF;
                    if (eventId == WM_LBUTTONDOWN || eventId == WM_RBUTTONDOWN || eventId == WM_MBUTTONDOWN)
                    {
                        _onActivate?.Invoke();
                    }
                }
                base.WndProc(ref m);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
        }

        // Fire-and-forget: never block the UI thread on file I/O
        private void Log(string msg) =>
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [MainWindow] {msg}\r\n"); }
                catch { }
            });

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("MainWindow_Loaded fired");

            // Cache the WPF window's HWND once — used by FocusScrcpy on every click.
            // WindowInteropHelper.Handle is cheap after first call but allocation is not.
            _mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            // Resize debounce: coalesce rapid WinForms resize events into a single
            // MoveWindow call 50 ms after the last event fires.
            _resizeTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _resizeTimer.Tick += (_, __) => { _resizeTimer.Stop(); ResizeScrcpy(); };

            this.Title = $"AndroidSyncControl {Controls.ShopeeSidebar.GetDisplayVersion()}";
            this.Closing += (s, ev) => Log($"MainWindow_Closing fired, Cancel={ev.Cancel}");
            scrcpyPanel.Resize += ScrcpyPanel_Resize;
            scrcpyPanel.DoubleClick += (s, ev) => Dispatcher.Invoke(AutoFitWindowToDevice);

            // MouseClick fires after full press+release — safe to focus SDL2 here.
            // MouseDown intentionally omitted: focusing SDL2 while button is held makes
            // SDL2 enter touch-drag mode immediately, causing screen to follow the mouse.
            scrcpyPanel.MouseClick += (s, ev) => FocusScrcpy();

            // Style panel with modern dark slate backdrop
            scrcpyPanel.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);

            // Wire up sidebar fit action, scrcpy focus, and device query delegate
            shopeeSidebar.GetCurrentDeviceId = () => DeviceConnectionSupervisor.Instance?.ActiveDeviceId ?? string.Empty;
            shopeeSidebar.RequestAutoFit = AutoFitWindowToDevice;
            shopeeSidebar.RequestFocusScrcpy = FocusScrcpy;
            shopeeSidebar.RequestPasteScrcpy = TriggerScrcpyPaste;
            shopeeSidebar.RequestPasteToDevice = (txt) => PasteClipboardToDevice(txt);

            // Attach native click filter to capture clicks and route focus to scrcpy
            _panelFilter = new PanelClickFilter(scrcpyPanel.Handle, FocusScrcpy);

            // Global keyboard shortcuts and routing to scrcpy
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            this.PreviewKeyUp += MainWindow_PreviewKeyUp;
            this.PreviewTextInput += MainWindow_PreviewTextInput;

            // Wire up supervisor callbacks
            DeviceConnectionSupervisor.Instance.EmbedScrcpyAction = EmbedScrcpyWindow;
            DeviceConnectionSupervisor.Instance.StateChanged += OnSupervisorStateChanged;
            DeviceConnectionSupervisor.Instance.DeviceResolutionChanged += OnDeviceResolutionChanged;

            Localization.LanguageManager.LanguageChanged += OnLanguageChanged;

            // Start single supervisor
            DeviceConnectionSupervisor.Instance.Start();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Log("MainWindow_Closed fired");
            _panelFilter?.ReleaseHandle();
            DetachScrcpyThread();
            Localization.LanguageManager.LanguageChanged -= OnLanguageChanged;
            DeviceConnectionSupervisor.Instance.StateChanged -= OnSupervisorStateChanged;
            DeviceConnectionSupervisor.Instance.DeviceResolutionChanged -= OnDeviceResolutionChanged;
            DeviceConnectionSupervisor.Instance.Stop();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            Log($"Window_StateChanged: {this.WindowState}");
            ResizeScrcpy();
        }

        private void ScrcpyPanel_Resize(object sender, EventArgs e)
        {
            // Debounce: restart the 50 ms timer on every resize event.
            // Prevents MoveWindow from being called dozens of times per second during drag-resize.
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private bool IsTextInputActive()
        {
            var focused = System.Windows.Input.Keyboard.FocusedElement;
            return focused is System.Windows.Controls.TextBox || focused is System.Windows.Controls.PasswordBox;
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool isCtrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;

            // Shortcut: Ctrl + F or F11 for AutoFit
            if ((e.Key == System.Windows.Input.Key.F && isCtrl) || e.Key == System.Windows.Input.Key.F11)
            {
                AutoFitWindowToDevice();
                e.Handled = true;
                return;
            }

            // Global shortcuts when NOT typing inside a desktop input control (TextBox/PasswordBox)
            if (!IsTextInputActive() && isCtrl)
            {
                // Ctrl + V: Direct paste into phone
                if (e.Key == System.Windows.Input.Key.V)
                {
                    e.Handled = true;
                    PasteClipboardToDevice();
                    return;
                }

                // Ctrl + A: Select All on phone
                if (e.Key == System.Windows.Input.Key.A)
                {
                    e.Handled = true;
                    SelectAllOnDevice();
                    return;
                }

                // Ctrl + C: Copy from phone to PC clipboard
                if (e.Key == System.Windows.Input.Key.C)
                {
                    e.Handled = true;
                    CopyFromDevice();
                    return;
                }

                // Ctrl + X: Cut on phone
                if (e.Key == System.Windows.Input.Key.X)
                {
                    e.Handled = true;
                    CutOnDevice();
                    return;
                }

                // Ctrl + Z: Undo on phone
                if (e.Key == System.Windows.Input.Key.Z)
                {
                    e.Handled = true;
                    TriggerScrcpyKeyCombo(0x5A); // VK_Z
                    return;
                }
            }

            if (IsTextInputActive()) return;

            // Route non-text navigation and control keys directly to scrcpy
            if (_scrcpyHwnd != IntPtr.Zero)
            {
                FocusScrcpy();

                if (e.Key == System.Windows.Input.Key.Back ||
                    e.Key == System.Windows.Input.Key.Enter ||
                    e.Key == System.Windows.Input.Key.Tab ||
                    e.Key == System.Windows.Input.Key.Escape ||
                    e.Key == System.Windows.Input.Key.Delete ||
                    e.Key == System.Windows.Input.Key.Left ||
                    e.Key == System.Windows.Input.Key.Right ||
                    e.Key == System.Windows.Input.Key.Up ||
                    e.Key == System.Windows.Input.Key.Down ||
                    e.Key == System.Windows.Input.Key.Home ||
                    e.Key == System.Windows.Input.Key.End)
                {
                    int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
                    if (vk > 0)
                    {
                        PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)vk, (IntPtr)1);
                        e.Handled = true;
                    }
                }
            }
        }

        private void MainWindow_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (IsTextInputActive()) return;

            if (_scrcpyHwnd != IntPtr.Zero)
            {
                int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
                if (vk > 0)
                {
                    IntPtr lParam = (IntPtr)(1 | (1 << 30) | (1 << 31));
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)vk, lParam);
                }
            }
        }

        private void MainWindow_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (IsTextInputActive()) return;

            if (_scrcpyHwnd != IntPtr.Zero && !string.IsNullOrEmpty(e.Text))
            {
                FocusScrcpy();
                foreach (char c in e.Text)
                {
                    // Filter out non-printable ASCII control characters (< 32) generated by Ctrl-combinations
                    if (c >= 32)
                    {
                        PostMessage(_scrcpyHwnd, WM_CHAR, (IntPtr)c, (IntPtr)1);
                    }
                }
                e.Handled = true;
            }
        }

        private string SafeGetClipboardText()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        return Clipboard.GetText() ?? string.Empty;
                    }
                    return string.Empty;
                }
                catch
                {
                    System.Threading.Thread.Sleep(30);
                }
            }
            return string.Empty;
        }

        public async void PasteClipboardToDevice(string? explicitText = null)
        {
            try
            {
                // Determine text source
                bool fromInputBox = !string.IsNullOrEmpty(explicitText);
                string text = fromInputBox
                    ? explicitText!
                    : SafeGetClipboardText();

                if (string.IsNullOrEmpty(text))
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.EmptyClipboard"));
                    return;
                }

                string activeDevice = DeviceConnectionSupervisor.Instance?.ActiveDeviceId;
                if (string.IsNullOrEmpty(activeDevice))
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.NotConnected"));
                    return;
                }

                // If sourced from Windows clipboard (Ctrl+V), show the text in sidebar input
                if (!fromInputBox)
                    shopeeSidebar.SetInputText(text);

                shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.Pasting"));
                FocusScrcpy();

                await ShopeeBypassService.DirectClipboardPasteAsync(activeDevice, text, TriggerScrcpyPaste);

                shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.Done"));
                FocusScrcpy();
            }
            catch (Exception ex)
            {
                shopeeSidebar.SetStatus($"Error: {ex.Message}");
                Log($"PasteClipboardToDevice error: {ex.Message}");
            }
        }

        public void SelectAllOnDevice()
        {
            try
            {
                string activeDevice = DeviceConnectionSupervisor.Instance?.ActiveDeviceId;
                if (string.IsNullOrEmpty(activeDevice))
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.NotConnected"));
                    return;
                }

                FocusScrcpy();
                // 1. PostMessage Ctrl+A to scrcpy SDL2 window (VK_A = 0x41)
                TriggerScrcpyKeyCombo(0x41);

                // 2. Also send ADB input keyevent fallback for universal support across all devices
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ShopeeBypassService.RunAdbAsync(activeDevice, "shell input keyevent 29 --meta 4096", 1500);
                    }
                    catch { }
                });

                shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.SelectAll"));
            }
            catch (Exception ex)
            {
                Log($"SelectAllOnDevice error: {ex.Message}");
            }
        }

        public async void CopyFromDevice()
        {
            try
            {
                string activeDevice = DeviceConnectionSupervisor.Instance?.ActiveDeviceId;
                if (string.IsNullOrEmpty(activeDevice))
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.NotConnected"));
                    return;
                }

                FocusScrcpy();
                // 1. Send Ctrl+C to device so Android copies active selection to device clipboard
                TriggerScrcpyKeyCombo(0x43); // VK_C

                // 2. Also send ADB fallback: keyevent 31 with meta 4096
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ShopeeBypassService.RunAdbAsync(activeDevice, "shell input keyevent 31 --meta 4096", 1500);
                    }
                    catch { }
                });

                // 3. Send scrcpy shortcut MOD+c (Alt+C) to synchronize device clipboard to computer clipboard
                await Task.Delay(50);
                TriggerScrcpyAltKeyCombo(0x43);

                // 4. Check Windows clipboard after sync and echo into sidebar
                await Task.Delay(120);
                string text = SafeGetClipboardText();
                if (!string.IsNullOrEmpty(text))
                {
                    shopeeSidebar.SetInputText(text);
                }
                shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.Copied"));
            }
            catch (Exception ex)
            {
                Log($"CopyFromDevice error: {ex.Message}");
            }
        }

        public void CutOnDevice()
        {
            try
            {
                string activeDevice = DeviceConnectionSupervisor.Instance?.ActiveDeviceId;
                if (string.IsNullOrEmpty(activeDevice)) return;

                FocusScrcpy();
                TriggerScrcpyKeyCombo(0x58); // VK_X
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ShopeeBypassService.RunAdbAsync(activeDevice, "shell input keyevent 52 --meta 4096", 1500);
                        await Task.Delay(50);
                        await Dispatcher.InvokeAsync(() => TriggerScrcpyAltKeyCombo(0x43));
                        await Task.Delay(100);
                        string text = SafeGetClipboardText();
                        if (!string.IsNullOrEmpty(text))
                            shopeeSidebar.SetInputText(text);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                Log($"CutOnDevice error: {ex.Message}");
            }
        }

        private void OnDeviceResolutionChanged(object sender, (int width, int height) res)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Log($"DeviceResolutionChanged caught in MainWindow: {res.width}x{res.height}");
                AutoFitWindowToDevice();
            });
        }

        public void AutoFitWindowToDevice()
        {
            if (this.WindowState == WindowState.Minimized) return;

            var workArea = SystemParameters.WorkArea;
            double maxWindowHeight = workArea.Height * 0.92;
            double maxWindowWidth = workArea.Width * 0.95;

            // Target height bounded by desktop work area
            double targetH = this.ActualHeight > 400 ? this.ActualHeight : 780;
            if (targetH > maxWindowHeight) targetH = maxWindowHeight;
            if (targetH < 620) targetH = 620;

            // Calculate chrome margins (title bar + window borders)
            double chromeH = 39;
            double chromeW = 16;
            if (this.ActualHeight > 0 && mainGrid.ActualHeight > 0)
            {
                double diffH = this.ActualHeight - mainGrid.ActualHeight;
                if (diffH > 10 && diffH < 80) chromeH = diffH;
            }
            if (this.ActualWidth > 0 && mainGrid.ActualWidth > 0)
            {
                double diffW = this.ActualWidth - mainGrid.ActualWidth;
                if (diffW >= 0 && diffW < 40) chromeW = diffW;
            }

            double contentHeight = targetH - chromeH;
            if (contentHeight < 400) contentHeight = 400;

            double ratio = DeviceConnectionSupervisor.Instance.DeviceAspectRatio;
            if (ratio <= 0.1 || ratio > 10.0) ratio = 9.0 / 16.0;

            // Sidebar width is fixed at 185
            double sidebarW = 185;
            double idealPhoneW = contentHeight * ratio;

            // If wide phone or landscape exceeds screen width, scale height down proportionally
            if (idealPhoneW + sidebarW + chromeW > maxWindowWidth)
            {
                idealPhoneW = maxWindowWidth - sidebarW - chromeW;
                contentHeight = idealPhoneW / ratio;
                targetH = contentHeight + chromeH;
            }

            double targetW = Math.Round(idealPhoneW + sidebarW + chromeW);

            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }

            this.Width = targetW;
            this.Height = Math.Round(targetH);

            // Re-center window if pushed off desktop screen
            if (this.Left + this.Width > workArea.Right)
            {
                this.Left = Math.Max(workArea.Left, workArea.Right - this.Width);
            }
            if (this.Top + this.Height > workArea.Bottom)
            {
                this.Top = Math.Max(workArea.Top, workArea.Bottom - this.Height);
            }

            Dispatcher.InvokeAsync(() =>
            {
                ResizeScrcpy();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ResizeScrcpy()
        {
            if (_scrcpyHwnd == IntPtr.Zero || scrcpyPanel.Width <= 10 || scrcpyPanel.Height <= 10)
                return;

            int panelW = scrcpyPanel.Width;
            int panelH = scrcpyPanel.Height;

            double ratio = DeviceConnectionSupervisor.Instance.DeviceAspectRatio;
            if (ratio <= 0.1 || ratio > 10.0) ratio = 9.0 / 16.0;

            int targetW, targetH;
            double panelRatio = (double)panelW / panelH;

            if (panelRatio > ratio)
            {
                // Panel is wider than phone aspect ratio -> fit to full height
                targetH = panelH;
                targetW = (int)Math.Round(panelH * ratio);
            }
            else
            {
                // Panel is taller than phone aspect ratio -> fit to full width
                targetW = panelW;
                targetH = (int)Math.Round(panelW / ratio);
            }

            if (targetW < 10) targetW = 10;
            if (targetH < 10) targetH = 10;

            // Center scrcpy window perfectly inside panel
            int targetX = (panelW - targetW) / 2;
            int targetY = (panelH - targetH) / 2;

            MoveWindow(_scrcpyHwnd, targetX, targetY, targetW, targetH, true);
        }

        private void AttachScrcpyThread(IntPtr hwnd)
        {
            try
            {
                uint currentThreadId = GetCurrentThreadId();
                uint scrcpyThreadId = GetWindowThreadProcessId(hwnd, out _);
                if (scrcpyThreadId != 0 && scrcpyThreadId != currentThreadId)
                {
                    if (_attachedScrcpyThreadId != 0 && _attachedScrcpyThreadId != scrcpyThreadId)
                    {
                        AttachThreadInput(currentThreadId, _attachedScrcpyThreadId, false);
                    }
                    AttachThreadInput(currentThreadId, scrcpyThreadId, true);
                    _attachedScrcpyThreadId = scrcpyThreadId;
                }
            }
            catch { }
        }

        private void DetachScrcpyThread()
        {
            try
            {
                if (_attachedScrcpyThreadId != 0)
                {
                    AttachThreadInput(GetCurrentThreadId(), _attachedScrcpyThreadId, false);
                    _attachedScrcpyThreadId = 0;
                }
            }
            catch { }
        }

        private bool EmbedScrcpyWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            _scrcpyHwnd = hwnd;

            bool success = false;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    wfHost.Visibility = Visibility.Visible;
                    overlayPanel.Visibility = Visibility.Collapsed;

                    SetParent(hwnd, scrcpyPanel.Handle);
                    int style = GetWindowLong(hwnd, GWL_STYLE);
                    style = (style & ~(unchecked((int)0x80000000) | 0x00C00000 | 0x00040000)) | WS_CHILD | WS_VISIBLE;
                    SetWindowLong(hwnd, GWL_STYLE, style);

                    AttachScrcpyThread(hwnd);
                    ResizeScrcpy();
                    ShowWindow(hwnd, SW_SHOW);
                    FocusScrcpy();

                    success = true;
                }
                catch
                {
                    success = false;
                }
            });

            return success;
        }

        public void FocusScrcpy()
        {
            if (_scrcpyHwnd == IntPtr.Zero) return;
            try
            {
                // scrcpy runs in a separate process; SetForegroundWindow is required
                // before SetFocus can steal focus cross-process on modern Windows.
                // Use cached _mainHwnd — avoids re-allocating WindowInteropHelper wrapper.
                SetForegroundWindow(_mainHwnd);
                SetFocus(_scrcpyHwnd);
            }
            catch { }
        }

        public void TriggerScrcpyKeyCombo(int vk)
        {
            if (_scrcpyHwnd != IntPtr.Zero)
            {
                try
                {
                    FocusScrcpy();
                    const int VK_CONTROL = 0x11;
                    PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, (IntPtr)1);
                    PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)vk, (IntPtr)1);
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)vk, (IntPtr)(1 | (1 << 30) | (1 << 31)));
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)VK_CONTROL, (IntPtr)(1 | (1 << 30) | (1 << 31)));
                }
                catch (Exception ex)
                {
                    Log($"TriggerScrcpyKeyCombo error: {ex.Message}");
                }
            }
        }

        public void TriggerScrcpyAltKeyCombo(int vk)
        {
            if (_scrcpyHwnd != IntPtr.Zero)
            {
                try
                {
                    FocusScrcpy();
                    const int VK_MENU = 0x12;
                    PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)VK_MENU, (IntPtr)1);
                    PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)vk, (IntPtr)1);
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)vk, (IntPtr)(1 | (1 << 30) | (1 << 31)));
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)VK_MENU, (IntPtr)(1 | (1 << 30) | (1 << 31)));
                }
                catch (Exception ex)
                {
                    Log($"TriggerScrcpyAltKeyCombo error: {ex.Message}");
                }
            }
        }

        public void TriggerScrcpyPaste()
        {
            TriggerScrcpyKeyCombo(0x56); // VK_V
        }

        private ConnectionStateChangedEventArgs _lastConnectionState;

        private void OnLanguageChanged()
        {
            if (_lastConnectionState != null)
            {
                OnSupervisorStateChanged(this, _lastConnectionState);
            }
        }

        private void OnSupervisorStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            _lastConnectionState = e;
            Dispatcher.InvokeAsync(() =>
            {
                switch (e.State)
                {
                    case ConnectionState.Initializing:
                        txtStatusTitle.Text = Localization.LanguageManager.GetString("Str.Connect.Title");
                        txtStatusDetail.Text = Localization.LanguageManager.GetString("Str.Connect.InitDetail");
                        txtStatusState.Text = Localization.LanguageManager.GetString("Str.Connect.Initializing");
                        spinnerBrush.Color = Color.FromRgb(0x25, 0x63, 0xEB);
                        progressBarStatus.Visibility = Visibility.Visible;
                        statusDot.Visibility = Visibility.Collapsed;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(null, Localization.LanguageManager.GetString("Str.Connect.Initializing"), "#2563EB", false);
                        break;

                    case ConnectionState.Searching:
                        txtStatusTitle.Text = Localization.LanguageManager.GetString("Str.Connect.Title");
                        txtStatusDetail.Text = Localization.LanguageManager.GetString("Str.Connect.Detail");
                        txtStatusState.Text = Localization.LanguageManager.GetString("Str.Connect.Searching");
                        spinnerBrush.Color = Color.FromRgb(0x25, 0x63, 0xEB);
                        progressBarStatus.Visibility = Visibility.Visible;
                        statusDot.Visibility = Visibility.Collapsed;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.GetCurrentDeviceId = () => string.Empty;
                        shopeeSidebar.UpdateConnectionStatus(null, Localization.LanguageManager.GetString("Str.Connect.Searching"), "#2563EB", false);
                        break;

                    case ConnectionState.DeviceDetected:
                    case ConnectionState.Connecting:
                        txtStatusTitle.Text = string.IsNullOrEmpty(e.DeviceModel) ? "Android Device" : e.DeviceModel;
                        txtStatusDetail.Text = Localization.LanguageManager.GetString("Str.Connect.Connecting");
                        txtStatusState.Text = Localization.LanguageManager.GetString("Str.Connect.Connecting");
                        spinnerBrush.Color = Color.FromRgb(0x10, 0xB9, 0x81);
                        progressBarStatus.Visibility = Visibility.Visible;
                        statusDot.Visibility = Visibility.Collapsed;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(null, Localization.LanguageManager.GetString("Str.Connect.Connecting"), "#2563EB", false);
                        break;

                    case ConnectionState.Connected:
                        wfHost.Visibility = Visibility.Visible;
                        overlayPanel.Visibility = Visibility.Collapsed;
                        shopeeSidebar.GetCurrentDeviceId = () => e.DeviceId;
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, Localization.LanguageManager.GetString("Str.Connect.Connected"), "#10B981", true);
                        Dispatcher.InvokeAsync(() =>
                        {
                            AutoFitWindowToDevice();
                        }, System.Windows.Threading.DispatcherPriority.Loaded);
                        break;

                    case ConnectionState.ConnectionLost:
                        DetachScrcpyThread();
                        _scrcpyHwnd = IntPtr.Zero;
                        txtStatusTitle.Text = Localization.LanguageManager.GetString("Str.Connect.Disconnected");
                        txtStatusDetail.Text = Localization.LanguageManager.GetString("Str.Connect.Detail");
                        txtStatusState.Text = Localization.LanguageManager.GetString("Str.Connect.Disconnected");
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                        statusDot.Visibility = Visibility.Visible;
                        progressBarStatus.Visibility = Visibility.Collapsed;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(null, Localization.LanguageManager.GetString("Str.Connect.Disconnected"), "#94A3B8", false);
                        break;

                    case ConnectionState.Reconnecting:
                        txtStatusTitle.Text = Localization.LanguageManager.GetString("Str.Connect.Reconnecting");
                        txtStatusDetail.Text = Localization.LanguageManager.GetString("Str.Connect.Detail");
                        txtStatusState.Text = Localization.LanguageManager.GetString("Str.Connect.Reconnecting");
                        spinnerBrush.Color = Color.FromRgb(0xF5, 0x9E, 0x0B);
                        progressBarStatus.Visibility = Visibility.Visible;
                        statusDot.Visibility = Visibility.Collapsed;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(null, $"{Localization.LanguageManager.GetString("Str.Connect.Reconnecting")} ({e.Attempt})", "#F59E0B", false);
                        break;

                    case ConnectionState.AdbUnavailable:
                        txtStatusTitle.Text = Localization.LanguageManager.GetString("Str.Connect.AdbUnavailable");
                        txtStatusDetail.Text = string.IsNullOrEmpty(e.Message) ? Localization.LanguageManager.GetString("Str.Connect.AdbError") : e.Message;
                        txtStatusState.Text = Localization.LanguageManager.GetString("Str.Connect.AdbError");
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                        statusDot.Visibility = Visibility.Visible;
                        progressBarStatus.Visibility = Visibility.Collapsed;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(null, Localization.LanguageManager.GetString("Str.Connect.AdbError"), "#EF4444", false);
                        break;
                }
            });
        }

    }
}
