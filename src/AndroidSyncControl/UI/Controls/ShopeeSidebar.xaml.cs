using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AndroidSyncControl.UI.Helpers;
using Microsoft.Win32;

namespace AndroidSyncControl.UI.Controls
{
    public partial class ShopeeSidebar : UserControl
    {
        public Func<string> GetCurrentDeviceId { get; set; }
        public Action RequestAutoFit { get; set; }
        public Action RequestFocusScrcpy { get; set; }
        public Action RequestPasteScrcpy { get; set; }
        public Action<string?> RequestPasteToDevice { get; set; }

        public ShopeeSidebar()
        {
            InitializeComponent();
            ApplyVersionInfo();
            // Show paste button only when input box has text; collapsed/pointer-none when empty
            txt_input.TextChanged += (s, e) =>
            {
                bool hasText = !string.IsNullOrWhiteSpace(txt_input.Text);
                btn_send_text.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        public static string GetDisplayVersion()
        {
            try
            {
                var meta = AndroidSyncControl.Infrastructure.InstallationMetadata.Load();
                if (!string.IsNullOrWhiteSpace(meta?.Version))
                    return $"v{meta.Version}";
            }
            catch { }

            try
            {
                string vFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                if (File.Exists(vFile))
                {
                    string v = File.ReadAllText(vFile).Trim();
                    if (!string.IsNullOrEmpty(v)) return $"v{v}";
                }
            }
            catch { }

            var curVer = Update.UpdateService.CurrentVersion;
            return $"v{curVer.Major}.{curVer.Minor}.{Math.Max(0, curVer.Build)}";
        }

        private void ApplyVersionInfo()
        {
            string ver = GetDisplayVersion();
            if (txt_version != null) txt_version.Text = ver;
            if (txt_update_ver_tag != null) txt_update_ver_tag.Text = ver;
        }

        /// <summary>
        /// Sets the text in the sidebar input box (called when Ctrl+V is pressed to echo clipboard content).
        /// Does NOT fire if the user is actively typing (caret is inside the box).
        /// </summary>
        public void SetInputText(string text)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Only overwrite if the user isn't actively editing the box
                if (!txt_input.IsKeyboardFocusWithin)
                    txt_input.Text = text ?? string.Empty;
            });
        }

        private string ActiveDeviceId => GetCurrentDeviceId?.Invoke() ?? string.Empty;

        public void UpdateConnectionStatus(string? deviceName, string status, string dotColorHex, bool isConnected = false)
        {
            Dispatcher.InvokeAsync(() =>
            {
                bool actuallyConnected = isConnected || 
                    (!string.IsNullOrWhiteSpace(deviceName) && 
                     deviceName != "Offline" && 
                     deviceName != "Android Device" && 
                     (status == "Connected" || status == Localization.LanguageManager.GetString("Str.Connect.Connected")));

                if (actuallyConnected && !string.IsNullOrWhiteSpace(deviceName))
                {
                    panelConnected.Visibility = Visibility.Visible;
                    panelOtherStatus.Visibility = Visibility.Collapsed;
                    txtDeviceName.Text = deviceName;
                    txtConnectedText.Text = status;
                    try
                    {
                        dotConnected.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(dotColorHex);
                    }
                    catch { }
                }
                else
                {
                    panelConnected.Visibility = Visibility.Collapsed;
                    panelOtherStatus.Visibility = Visibility.Visible;
                    txtConnectionState.Text = status;
                    try
                    {
                        dotConnection.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(dotColorHex);
                    }
                    catch { }
                }
            });
        }

        private void ShopeeSidebar_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyVersionInfo();
        }

        public void SetStatus(string msg)
        {
            SetProcessing(false, msg);
        }

        public void SetProcessing(bool isProcessing, string msg, bool isError = false)
        {
            if (Dispatcher.CheckAccess())
            {
                ApplyProcessingUI(isProcessing, msg, isError);
            }
            else
            {
                Dispatcher.InvokeAsync(() => ApplyProcessingUI(isProcessing, msg, isError),
                    System.Windows.Threading.DispatcherPriority.DataBind);
            }
        }

        private void ApplyProcessingUI(bool isProcessing, string msg, bool isError)
        {
            if (pb_sidebar != null)
            {
                pb_sidebar.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;
            }

            if (txt_status != null && txt_status.Text != msg)
            {
                txt_status.Text = msg;
            }

            var color = isError 
                ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44))
                : isProcessing 
                    ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB))
                    : (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));

            if (txt_status != null) txt_status.Foreground = color;
            if (dot_status != null) dot_status.Fill = color;
        }

        private async void btn_info_Click(object sender, RoutedEventArgs e)
        {
            SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.Ready"));
            string info = await ShopeeBypassService.GetPhoneInfoAsync(ActiveDeviceId);
            MessageBox.Show(info, Localization.LanguageManager.GetString("Str.Action.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btn_fit_screen_Click(object sender, RoutedEventArgs e)
        {
            RequestAutoFit?.Invoke();
            SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.Done"));
        }

        private async void btn_power_Click(object sender, RoutedEventArgs e)
        {
            await ShopeeBypassService.PowerAsync(ActiveDeviceId);
            SetProcessing(false, Localization.LanguageManager.GetString("Str.Action.Power") + " ✓");
        }

        private async void btn_reboot_Click(object sender, RoutedEventArgs e)
        {
            string confirmMsg = Localization.LanguageManager.GetString("Str.Dialog.RebootConfirm");
            string title = Localization.LanguageManager.GetString("Str.Action.Reboot");

            if (MessageBox.Show(confirmMsg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SetProcessing(true, Localization.LanguageManager.GetString("Str.Status.Rebooting"));
                await ShopeeBypassService.RebootAsync(ActiveDeviceId);
            }
        }

        private async void btn_screenshot_Click(object sender, RoutedEventArgs e)
        {
            SetProcessing(true, Localization.LanguageManager.GetString("Str.Status.Capturing"));
            string shotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            await ShopeeBypassService.TakeScreenshotAsync(ActiveDeviceId, shotDir);
            SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.Done"));
            try { Process.Start(new ProcessStartInfo("explorer.exe", shotDir) { UseShellExecute = true }); } catch { }
        }

        private void btn_send_text_Click(object sender, RoutedEventArgs e)
        {
            SendTextFromInput();
        }

        private void txt_input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SendTextFromInput();
            }
        }

        /// <summary>
        /// Sends the typed text from the input box to the device.
        /// Preserves the text in the input box so the user can reuse, edit, or delete as desired.
        /// Empty input → no-op (never falls back to Windows clipboard).
        /// </summary>
        private void SendTextFromInput()
        {
            string text = txt_input.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            // Do NOT clear — keep the text intact for user reuse/editing
            RequestPasteToDevice?.Invoke(text);
        }

        private async void btn_install_apk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Filter = "Android APK (*.apk)|*.apk|All files (*.*)|*.*",
                Title = Localization.LanguageManager.GetString("Str.Action.InstallApk")
            };
            if (dlg.ShowDialog() == true)
            {
                btn_install_apk.IsEnabled = false;
                try
                {
                    string apkName = Path.GetFileName(dlg.FileName) ?? "APK";
                    SetProcessing(true, $"Đang cài {apkName}...");

                    var result = await ShopeeBypassService.InstallApkDetailedAsync(ActiveDeviceId, dlg.FileName, s =>
                    {
                        SetProcessing(true, s);
                    });

                    if (result.Success)
                    {
                        SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.InstallSuccess"));
                    }
                    else
                    {
                        SetProcessing(false, string.Format(Localization.LanguageManager.GetString("Str.Status.InstallFailed"), result.Message), isError: true);
                    }
                }
                catch (Exception ex)
                {
                    SetProcessing(false, $"Lỗi: {ex.Message}", isError: true);
                }
                finally
                {
                    btn_install_apk.IsEnabled = true;
                }
            }
        }

        private void btn_backup_restore_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            var dlg = new BackupRestoreDialog(ActiveDeviceId, Window.GetWindow(this));
            dlg.ShowDialog();
        }

        private void btn_proxy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            var dlg = new ProxyDialog(ActiveDeviceId, Window.GetWindow(this));
            dlg.ShowDialog();
        }

        private void btn_adb_cmd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            var dlg = new AdbConsoleDialog(ActiveDeviceId, Window.GetWindow(this));
            dlg.ShowDialog();
        }

        private async void btn_open_shopee_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            SetProcessing(true, Localization.LanguageManager.GetString("Str.Status.OpeningShopee"));
            await ShopeeBypassService.OpenShopeeAsync(ActiveDeviceId);
            SetProcessing(false, "Shopee ✓");
        }

        private async void btn_bypass_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            btn_bypass.IsEnabled = false;
            try
            {
                SetProcessing(true, Localization.LanguageManager.GetString("Str.Bypass.Step1"));
                await ShopeeBypassService.BypassShopeeAsync(ActiveDeviceId, s => SetProcessing(true, s));
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Bypass.Done"));
            }
            catch (Exception ex)
            {
                SetProcessing(false, $"Error: {ex.Message}", isError: true);
            }
            finally
            {
                btn_bypass.IsEnabled = true;
            }
        }

        private async void btn_rotate_ip_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ActiveDeviceId))
            {
                SetProcessing(false, Localization.LanguageManager.GetString("Str.Status.NotConnected"), isError: true);
                return;
            }

            btn_rotate_ip.IsEnabled = false;
            try
            {
                SetProcessing(true, Localization.LanguageManager.GetString("Str.Status.RotatingIp"));
                await ShopeeBypassService.RotateAirplaneModeAsync(ActiveDeviceId);
                SetProcessing(false, "IP ✓");
            }
            catch (Exception ex)
            {
                SetProcessing(false, $"Lỗi: {ex.Message}", isError: true);
            }
            finally
            {
                btn_rotate_ip.IsEnabled = true;
            }
        }

        private async void btn_nav_menu_Click(object sender, RoutedEventArgs e)
        {
            await ShopeeBypassService.SendKeyAsync(ActiveDeviceId, 187); // KEYCODE_APP_SWITCH
        }

        private async void btn_nav_home_Click(object sender, RoutedEventArgs e)
        {
            await ShopeeBypassService.SendKeyAsync(ActiveDeviceId, 3); // KEYCODE_HOME
        }

        private async void btn_nav_back_Click(object sender, RoutedEventArgs e)
        {
            await ShopeeBypassService.SendKeyAsync(ActiveDeviceId, 4); // KEYCODE_BACK
        }

        private async void btn_check_update_Click(object sender, RoutedEventArgs e)
        {
            btn_check_update.IsEnabled = false;
            SetStatus(Localization.LanguageManager.GetString("Str.Update.Checking"));

            try
            {
                string url = Singleton.Setting.Setting.UpdateManifestUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    url = AndroidSyncControl.Infrastructure.AppPaths.DefaultManifestUrl;
                }

                var manifest = await App.UpdateService.CheckForUpdateAsync(url);
                if (manifest != null)
                {
                    SetStatus($"v{manifest.Version} ✓");

                    var dlg = new UpdateDialog(manifest, App.UpdateService)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    dlg.ShowDialog();
                }
                else
                {
                    string currentVer = Update.UpdateService.CurrentVersion.ToString(3);
                    string upToDateText = Localization.LanguageManager.GetString("Str.Update.UpToDate");
                    SetStatus($"{upToDateText} ({currentVer}) ✓");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Update: {ex.Message}");
            }
            finally
            {
                btn_check_update.IsEnabled = true;
            }
        }
    }
}
