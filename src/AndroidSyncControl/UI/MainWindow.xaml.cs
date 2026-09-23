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
        private uint _attachedScrcpyThreadId = 0;
        private PanelClickFilter? _panelFilter;

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

        private void Log(string msg)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "debug_scrcpy.log"), $"[{DateTime.Now:HH:mm:ss.fff}] [MainWindow] {msg}\r\n");
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("MainWindow_Loaded fired");
            this.Closing += (s, ev) => Log($"MainWindow_Closing fired, Cancel={ev.Cancel}");
            scrcpyPanel.Resize += ScrcpyPanel_Resize;
            scrcpyPanel.DoubleClick += (s, ev) => Dispatcher.Invoke(AutoFitWindowToDevice);

            scrcpyPanel.MouseClick += (s, ev) => FocusScrcpy();
            scrcpyPanel.MouseDown  += (s, ev) => FocusScrcpy();
            scrcpyPanel.GotFocus   += (s, ev) => FocusScrcpy();

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
            Log($"ScrcpyPanel_Resize: {scrcpyPanel.Width}x{scrcpyPanel.Height}");
            ResizeScrcpy();
        }

        private bool IsTextInputActive()
        {
            var focused = System.Windows.Input.Keyboard.FocusedElement;
            return focused is System.Windows.Controls.TextBox || focused is System.Windows.Controls.PasswordBox;
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Shortcut: Ctrl + F or F11 for AutoFit
            if ((e.Key == System.Windows.Input.Key.F && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control) 
                || e.Key == System.Windows.Input.Key.F11)
            {
                AutoFitWindowToDevice();
                e.Handled = true;
                return;
            }

            // Shortcut: Ctrl + V for direct paste into active phone field
            if (e.Key == System.Windows.Input.Key.V && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (!IsTextInputActive())
                {
                    e.Handled = true;
                    PasteClipboardToDevice();
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
                    PostMessage(_scrcpyHwnd, WM_CHAR, (IntPtr)c, (IntPtr)1);
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
                string text = explicitText;
                if (string.IsNullOrEmpty(text))
                {
                    text = SafeGetClipboardText();
                }

                if (string.IsNullOrEmpty(text))
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.EmptyClipboard"));
                    return;
                }

                // Ensure Windows Clipboard and sidebar textbox stay synchronized
                ShopeeBypassService.SafeSetClipboard(text);
                shopeeSidebar.SetInputText(text);

                string activeDevice = DeviceConnectionSupervisor.Instance?.ActiveDeviceId;
                if (!string.IsNullOrEmpty(activeDevice))
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.Pasting"));
                    await ShopeeBypassService.DirectClipboardPasteAsync(activeDevice, text, TriggerScrcpyPaste);
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.Done"));
                    FocusScrcpy();
                }
                else
                {
                    shopeeSidebar.SetStatus(Localization.LanguageManager.GetString("Str.Status.NotConnected"));
                }
            }
            catch (Exception ex)
            {
                shopeeSidebar.SetStatus($"Error: {ex.Message}");
                Log($"PasteClipboardToDevice error: {ex.Message}");
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
            if (_scrcpyHwnd != IntPtr.Zero)
            {
                try
                {
                    IntPtr mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    SetForegroundWindow(mainHwnd);
                    SetFocus(_scrcpyHwnd);
                    SendMessage(_scrcpyHwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
                    SendMessage(_scrcpyHwnd, WM_SETFOCUS, mainHwnd, IntPtr.Zero);
                }
                catch { }
            }
        }

        public void TriggerScrcpyPaste()
        {
            if (_scrcpyHwnd != IntPtr.Zero)
            {
                try
                {
                    FocusScrcpy();
                    const int VK_CONTROL = 0x11;
                    const int VK_V = 0x56;
                    PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, (IntPtr)1);
                    PostMessage(_scrcpyHwnd, WM_KEYDOWN, (IntPtr)VK_V, (IntPtr)1);
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)VK_V, (IntPtr)(1 | (1 << 30) | (1 << 31)));
                    PostMessage(_scrcpyHwnd, WM_KEYUP, (IntPtr)VK_CONTROL, (IntPtr)(1 | (1 << 30) | (1 << 31)));
                }
                catch (Exception ex)
                {
                    Log($"TriggerScrcpyPaste error: {ex.Message}");
                }
            }
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
                        shopeeSidebar.UpdateConnectionStatus("Android Device", Localization.LanguageManager.GetString("Str.Connect.Initializing"), "#2563EB");
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
                        shopeeSidebar.UpdateConnectionStatus("Android Device", Localization.LanguageManager.GetString("Str.Connect.Searching"), "#2563EB");
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
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, Localization.LanguageManager.GetString("Str.Connect.Connecting"), "#2563EB");
                        break;

                    case ConnectionState.Connected:
                        wfHost.Visibility = Visibility.Visible;
                        overlayPanel.Visibility = Visibility.Collapsed;
                        shopeeSidebar.GetCurrentDeviceId = () => e.DeviceId;
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, Localization.LanguageManager.GetString("Str.Connect.Connected"), "#10B981");
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
                        shopeeSidebar.UpdateConnectionStatus("Offline", Localization.LanguageManager.GetString("Str.Connect.Disconnected"), "#94A3B8");
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
                        shopeeSidebar.UpdateConnectionStatus("Offline", $"{Localization.LanguageManager.GetString("Str.Connect.Reconnecting")} ({e.Attempt})", "#F59E0B");
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
                        shopeeSidebar.UpdateConnectionStatus("Offline", Localization.LanguageManager.GetString("Str.Connect.AdbError"), "#EF4444");
                        break;
                }
            });
        }

    }
}
