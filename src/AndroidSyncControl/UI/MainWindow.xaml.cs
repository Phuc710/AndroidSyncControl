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
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int SW_SHOW = 5;

        private IntPtr _scrcpyHwnd = IntPtr.Zero;

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

            // Click vào màn hình → route focus vào SDL2 HWND để gõ phím được
            scrcpyPanel.MouseClick += (s, ev) => FocusScrcpy();
            scrcpyPanel.MouseDown  += (s, ev) => FocusScrcpy();

            // Style panel with modern dark slate backdrop
            scrcpyPanel.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);

            // Wire up sidebar fit action and keyboard shortcuts
            shopeeSidebar.RequestAutoFit = AutoFitWindowToDevice;
            this.KeyDown += MainWindow_KeyDown;

            // Wire up supervisor callbacks
            DeviceConnectionSupervisor.Instance.EmbedScrcpyAction = EmbedScrcpyWindow;
            DeviceConnectionSupervisor.Instance.StateChanged += OnSupervisorStateChanged;
            DeviceConnectionSupervisor.Instance.DeviceResolutionChanged += OnDeviceResolutionChanged;

            // Start single supervisor
            DeviceConnectionSupervisor.Instance.Start();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Log("MainWindow_Closed fired");
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

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if ((e.Key == System.Windows.Input.Key.F && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control) 
                || e.Key == System.Windows.Input.Key.F11)
            {
                AutoFitWindowToDevice();
                e.Handled = true;
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
                    // Strip WS_POPUP (0x80000000), WS_CAPTION (0x00C00000), WS_THICKFRAME (0x00040000)
                    style = (style & ~(unchecked((int)0x80000000) | 0x00C00000 | 0x00040000)) | WS_CHILD | WS_VISIBLE;
                    SetWindowLong(hwnd, GWL_STYLE, style);

                    ResizeScrcpy();
                    ShowWindow(hwnd, SW_SHOW);
                    FocusScrcpy(); // Route keyboard focus into SDL2 HWND

                    success = true;
                }
                catch
                {
                    success = false;
                }
            });

            return success;
        }

        // SDL2 không tự nhận focus khi là WS_CHILD — phải gọi SetFocus() trực tiếp vào HWND của nó
        private void FocusScrcpy()
        {
            if (_scrcpyHwnd == IntPtr.Zero) return;
            SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            SetFocus(_scrcpyHwnd);
        }

        private void OnSupervisorStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                switch (e.State)
                {
                    case ConnectionState.Initializing:
                        txtStatusTitle.Text = "Connect your Android device";
                        txtStatusDetail.Text = "Initializing ADB service...";
                        txtStatusState.Text = "Initializing...";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                        spinnerBrush.Color = Color.FromRgb(0x25, 0x63, 0xEB);
                        progressBarStatus.Visibility = Visibility.Visible;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.UpdateConnectionStatus("Android Device", "Initializing...", "#2563EB");
                        break;

                    case ConnectionState.Searching:
                        txtStatusTitle.Text = "Connect your Android device";
                        txtStatusDetail.Text = "Connect your phone via USB and make sure\nUSB debugging is enabled.";
                        txtStatusState.Text = "Searching for device...";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                        spinnerBrush.Color = Color.FromRgb(0x25, 0x63, 0xEB);
                        progressBarStatus.Visibility = Visibility.Visible;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.GetCurrentDeviceId = () => string.Empty;
                        shopeeSidebar.UpdateConnectionStatus("Android Device", "Searching...", "#2563EB");
                        break;

                    case ConnectionState.DeviceDetected:
                        txtStatusTitle.Text = "Device detected";
                        txtStatusDetail.Text = $"Device: {e.DeviceModel}";
                        txtStatusState.Text = "Preparing screen stream...";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                        spinnerBrush.Color = Color.FromRgb(0x10, 0xB9, 0x81);
                        progressBarStatus.Visibility = Visibility.Visible;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, "Connecting...", "#2563EB");
                        break;

                    case ConnectionState.Connecting:
                        txtStatusTitle.Text = "Device detected";
                        txtStatusDetail.Text = $"Connecting to {e.DeviceModel}...";
                        txtStatusState.Text = "Connecting...";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                        spinnerBrush.Color = Color.FromRgb(0x10, 0xB9, 0x81);
                        progressBarStatus.Visibility = Visibility.Visible;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, "Connecting...", "#2563EB");
                        break;

                    case ConnectionState.Connected:
                        wfHost.Visibility = Visibility.Visible;
                        overlayPanel.Visibility = Visibility.Collapsed;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.GetCurrentDeviceId = () => e.DeviceId;
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, "Connected", "#10B981");
                        Dispatcher.InvokeAsync(() =>
                        {
                            AutoFitWindowToDevice();
                        }, System.Windows.Threading.DispatcherPriority.Loaded);
                        break;

                    case ConnectionState.ConnectionLost:
                        txtStatusTitle.Text = "Connection lost";
                        txtStatusDetail.Text = "Device was disconnected. Connect your phone via USB.";
                        txtStatusState.Text = "Disconnected";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                        spinnerBrush.Color = Color.FromRgb(0x94, 0xA3, 0xB8);
                        progressBarStatus.Visibility = Visibility.Visible;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.UpdateConnectionStatus("Offline", "Disconnected", "#94A3B8");
                        break;

                    case ConnectionState.Reconnecting:
                        txtStatusTitle.Text = "Reconnecting";
                        txtStatusDetail.Text = "Waiting for device to respond...";
                        txtStatusState.Text = string.IsNullOrEmpty(e.Message) ? "Reconnecting..." : e.Message;
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber
                        spinnerBrush.Color = Color.FromRgb(0xF5, 0x9E, 0x0B);
                        progressBarStatus.Visibility = Visibility.Visible;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.UpdateConnectionStatus("Offline", $"Reconnecting ({e.Attempt})", "#F59E0B");
                        break;

                    case ConnectionState.AdbUnavailable:
                        txtStatusTitle.Text = "ADB unavailable";
                        txtStatusDetail.Text = string.IsNullOrEmpty(e.Message) ? "Could not communicate with ADB server." : e.Message;
                        txtStatusState.Text = "ADB service error";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                        progressBarStatus.Visibility = Visibility.Hidden;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.UpdateConnectionStatus("Offline", "ADB Error", "#EF4444");
                        break;
                }
            });
        }

    }
}
