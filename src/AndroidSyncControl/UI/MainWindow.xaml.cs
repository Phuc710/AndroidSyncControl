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

            // Wire up supervisor callbacks
            DeviceConnectionSupervisor.Instance.EmbedScrcpyAction = EmbedScrcpyWindow;
            DeviceConnectionSupervisor.Instance.StateChanged += OnSupervisorStateChanged;

            // Start single supervisor
            DeviceConnectionSupervisor.Instance.Start();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Log("MainWindow_Closed fired");
            DeviceConnectionSupervisor.Instance.StateChanged -= OnSupervisorStateChanged;
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

        private void ResizeScrcpy()
        {
            if (_scrcpyHwnd != IntPtr.Zero && scrcpyPanel.Width > 50 && scrcpyPanel.Height > 50)
            {
                MoveWindow(_scrcpyHwnd, 0, 0, scrcpyPanel.Width, scrcpyPanel.Height, true);
            }
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

                    int w = scrcpyPanel.Width > 0 ? scrcpyPanel.Width : 420;
                    int h = scrcpyPanel.Height > 0 ? scrcpyPanel.Height : 740;
                    MoveWindow(hwnd, 0, 0, w, h, true);
                    ShowWindow(hwnd, SW_SHOW);

                    success = true;
                }
                catch
                {
                    success = false;
                }
            });

            return success;
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
                        progressBarStatus.Visibility = Visibility.Visible;
                        progressBarStatus.IsIndeterminate = true;
                        progressBarStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                        btnRetry.IsEnabled = false;
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
                        progressBarStatus.Visibility = Visibility.Visible;
                        progressBarStatus.IsIndeterminate = true;
                        progressBarStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                        btnRetry.IsEnabled = true;
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
                        progressBarStatus.Visibility = Visibility.Visible;
                        progressBarStatus.IsIndeterminate = true;
                        progressBarStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                        btnRetry.IsEnabled = false;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        shopeeSidebar.UpdateConnectionStatus(e.DeviceModel, "Connecting...", "#2563EB");
                        break;

                    case ConnectionState.Connecting:
                        txtStatusTitle.Text = "Device detected";
                        txtStatusDetail.Text = $"Connecting to {e.DeviceModel}...";
                        txtStatusState.Text = "Connecting...";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                        progressBarStatus.Visibility = Visibility.Visible;
                        progressBarStatus.IsIndeterminate = true;
                        progressBarStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                        btnRetry.IsEnabled = false;
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
                        ResizeScrcpy();
                        break;

                    case ConnectionState.ConnectionLost:
                        txtStatusTitle.Text = "Connection lost";
                        txtStatusDetail.Text = "Device was disconnected. Connect your phone via USB.";
                        txtStatusState.Text = "Disconnected";
                        statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                        progressBarStatus.Visibility = Visibility.Visible;
                        progressBarStatus.IsIndeterminate = true;
                        progressBarStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                        btnRetry.IsEnabled = true;
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
                        progressBarStatus.Visibility = Visibility.Visible;
                        progressBarStatus.IsIndeterminate = true;
                        progressBarStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                        btnRetry.IsEnabled = true;
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
                        btnRetry.IsEnabled = true;
                        wfHost.Visibility = Visibility.Collapsed;
                        overlayPanel.Visibility = Visibility.Visible;
                        this.Title = "AndroidSyncControl";
                        shopeeSidebar.UpdateConnectionStatus("Offline", "ADB Error", "#EF4444");
                        break;
                }
            });
        }

        private void BtnRetryNow_Click(object sender, RoutedEventArgs e)
        {
            DeviceConnectionSupervisor.Instance.RequestReconnectNow();
        }
    }
}
