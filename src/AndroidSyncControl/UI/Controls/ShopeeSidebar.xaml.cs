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

        public ShopeeSidebar()
        {
            InitializeComponent();
        }

        private string ActiveDeviceId => GetCurrentDeviceId?.Invoke() ?? string.Empty;

        public void UpdateConnectionStatus(string deviceName, string status, string dotColorHex)
        {
            Dispatcher.InvokeAsync(() =>
            {
                txtDeviceName.Text = string.IsNullOrEmpty(deviceName) ? "Offline" : deviceName;
                txtConnectionState.Text = status;
                try
                {
                    dotConnection.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(dotColorHex);
                }
                catch { }
            });
        }

        private void ShopeeSidebar_Loaded(object sender, RoutedEventArgs e)
        {
        }

        public void SetStatus(string msg)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txt_status.Text = msg;
            }));
        }

        private async void btn_info_Click(object sender, RoutedEventArgs e)
        {
            SetStatus(Localization.LanguageManager.GetString("Str.Status.Ready"));
            string info = await ShopeeBypassService.GetPhoneInfoAsync(ActiveDeviceId);
            MessageBox.Show(info, Localization.LanguageManager.GetString("Str.Action.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btn_fit_screen_Click(object sender, RoutedEventArgs e)
        {
            RequestAutoFit?.Invoke();
            SetStatus(Localization.LanguageManager.GetString("Str.Status.Done"));
        }

        private async void btn_power_Click(object sender, RoutedEventArgs e)
        {
            await ShopeeBypassService.PowerAsync(ActiveDeviceId);
            SetStatus(Localization.LanguageManager.GetString("Str.Action.Power") + " ✓");
        }

        private async void btn_reboot_Click(object sender, RoutedEventArgs e)
        {
            string confirmMsg = Localization.LanguageManager.GetString("Str.Dialog.RebootConfirm");
            string title = Localization.LanguageManager.GetString("Str.Action.Reboot");

            if (MessageBox.Show(confirmMsg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SetStatus(Localization.LanguageManager.GetString("Str.Status.Rebooting"));
                await ShopeeBypassService.RebootAsync(ActiveDeviceId);
            }
        }

        private async void btn_screenshot_Click(object sender, RoutedEventArgs e)
        {
            SetStatus(Localization.LanguageManager.GetString("Str.Status.Capturing"));
            string shotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            await ShopeeBypassService.TakeScreenshotAsync(ActiveDeviceId, shotDir);
            SetStatus(Localization.LanguageManager.GetString("Str.Status.Done"));
            try { Process.Start(new ProcessStartInfo("explorer.exe", shotDir) { UseShellExecute = true }); } catch { }
        }

        private async void btn_send_text_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = txt_input.Text;
                if (string.IsNullOrEmpty(text) && Clipboard.ContainsText())
                {
                    text = Clipboard.GetText() ?? string.Empty;
                    txt_input.Text = text;
                }

                if (string.IsNullOrEmpty(text))
                {
                    SetStatus(Localization.LanguageManager.GetString("Str.Status.EmptyClipboard"));
                    return;
                }

                string deviceId = ActiveDeviceId;
                if (string.IsNullOrEmpty(deviceId))
                {
                    SetStatus(Localization.LanguageManager.GetString("Str.Status.NotConnected"));
                    return;
                }

                SetStatus(Localization.LanguageManager.GetString("Str.Status.Pasting"));
                await ShopeeBypassService.DirectClipboardPasteAsync(deviceId, text, RequestPasteScrcpy);
                SetStatus(Localization.LanguageManager.GetString("Str.Status.Done"));
                RequestFocusScrcpy?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
        }

        private void txt_input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                btn_send_text_Click(sender, e);
            }
        }

        private async void btn_install_apk_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Android APK (*.apk)|*.apk|All files (*.*)|*.*",
                Title = Localization.LanguageManager.GetString("Str.Action.InstallApk")
            };
            if (dlg.ShowDialog() == true)
            {
                SetStatus(Localization.LanguageManager.GetString("Str.Status.Installing"));
                await ShopeeBypassService.InstallApkAsync(ActiveDeviceId, dlg.FileName);
                SetStatus(Localization.LanguageManager.GetString("Str.Status.Done"));
            }
        }

        private void btn_backup_restore_Click(object sender, RoutedEventArgs e)
        {
            SetStatus(Localization.LanguageManager.GetString("Str.Status.Ready"));
        }

        private async void btn_proxy_Click(object sender, RoutedEventArgs e)
        {
            string current = await ShopeeBypassService.RunAdbAsync(ActiveDeviceId, "shell settings get global http_proxy");
            string prompt = string.IsNullOrWhiteSpace(current) || current == "null" || current == ":0"
                ? Localization.LanguageManager.GetString("Str.Dialog.ProxyEmptyPrompt")
                : string.Format(Localization.LanguageManager.GetString("Str.Dialog.ProxyCurrentPrompt"), current);

            var dlg = new Window
            {
                Title = Localization.LanguageManager.GetString("Str.Action.Proxy"),
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            var lbl = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#334155")
            };
            var txt = new TextBox { Height = 32, Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
            var btnOk = new Button
            {
                Content = Localization.LanguageManager.GetString("Str.Dialog.Apply"),
                Width = 96,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Style = (Style)Application.Current.FindResource("PrimaryButton"),
                IsEnabled = !string.IsNullOrWhiteSpace(txt.Text)
            };

            txt.TextChanged += (s, ev) =>
            {
                btnOk.IsEnabled = !string.IsNullOrWhiteSpace(txt.Text);
            };

            btnOk.Click += async (s, ev) =>
            {
                string input = txt.Text.Trim();
                await ShopeeBypassService.SetProxyAsync(ActiveDeviceId, input);
                dlg.Close();
                SetStatus(string.IsNullOrEmpty(input) 
                    ? Localization.LanguageManager.GetString("Str.Status.ProxyCleared") 
                    : $"Proxy: {input} ✓");
            };

            sp.Children.Add(lbl);
            sp.Children.Add(txt);
            sp.Children.Add(btnOk);
            dlg.Content = sp;
            txt.Focus();
            dlg.ShowDialog();
        }

        private void btn_adb_cmd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string devArg = !string.IsNullOrEmpty(ActiveDeviceId) ? $"-s {ActiveDeviceId} " : "";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"title ADB Shell [{ActiveDeviceId}] && echo ADB Shell [{ActiveDeviceId}] && adb {devArg}shell\"",
                    WorkingDirectory = baseDir,
                    UseShellExecute = true
                });
                SetStatus("ADB Shell ✓");
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
        }

        private async void btn_open_shopee_Click(object sender, RoutedEventArgs e)
        {
            SetStatus(Localization.LanguageManager.GetString("Str.Status.OpeningShopee"));
            await ShopeeBypassService.OpenShopeeAsync(ActiveDeviceId);
            SetStatus("Shopee ✓");
        }

        private async void btn_bypass_Click(object sender, RoutedEventArgs e)
        {
            btn_bypass.IsEnabled = false;
            try
            {
                await ShopeeBypassService.BypassShopeeAsync(ActiveDeviceId, s => SetStatus(s));
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                btn_bypass.IsEnabled = true;
            }
        }

        private async void btn_rotate_ip_Click(object sender, RoutedEventArgs e)
        {
            btn_rotate_ip.IsEnabled = false;
            SetStatus(Localization.LanguageManager.GetString("Str.Status.RotatingIp"));
            await ShopeeBypassService.RotateAirplaneModeAsync(ActiveDeviceId);
            SetStatus("IP ✓");
            btn_rotate_ip.IsEnabled = true;
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
                    url = "https://raw.githubusercontent.com/Phuc710/AndroidSyncControl/main/release/update-manifest.json";
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
