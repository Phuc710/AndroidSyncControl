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
            SyncClipboardToInput();
        }

        private void SetStatus(string msg)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txt_status.Text = msg;
            }));
        }

        public void SyncClipboardToInput()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText().Trim();
                    if (!string.IsNullOrEmpty(clip) && string.IsNullOrEmpty(txt_input.Text))
                    {
                        txt_input.Text = clip;
                    }
                }
            }
            catch { }
        }

        private void txt_input_GotFocus(object sender, RoutedEventArgs e)
        {
            SyncClipboardToInput();
            txt_input.SelectAll();
        }

        private async void btn_info_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Đang đọc thông số...");
            string info = await ShopeeBypassService.GetPhoneInfoAsync(ActiveDeviceId);
            MessageBox.Show(info, "Thông Tin Thiết Bị", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("Ready");
        }

        private void btn_fit_screen_Click(object sender, RoutedEventArgs e)
        {
            RequestAutoFit?.Invoke();
            SetStatus("Fit window ✓");
        }

        private async void btn_power_Click(object sender, RoutedEventArgs e)
        {
            await ShopeeBypassService.PowerAsync(ActiveDeviceId);
            SetStatus("Power ✓");
        }

        private async void btn_reboot_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Khởi động lại điện thoại?", "Xác nhận Reboot", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SetStatus("Rebooting device...");
                await ShopeeBypassService.RebootAsync(ActiveDeviceId);
            }
        }

        private async void btn_screenshot_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Đang chụp màn hình...");
            string shotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            await ShopeeBypassService.TakeScreenshotAsync(ActiveDeviceId, shotDir);
            SetStatus("Ảnh đã lưu vào screenshots/ ✓");
            try { Process.Start(new ProcessStartInfo("explorer.exe", shotDir) { UseShellExecute = true }); } catch { }
        }


        private async void btn_send_text_Click(object sender, RoutedEventArgs e)
        {
            string text = txt_input.Text.Trim();
            if (string.IsNullOrEmpty(text) && Clipboard.ContainsText())
            {
                text = Clipboard.GetText().Trim();
                txt_input.Text = text;
            }

            if (!string.IsNullOrEmpty(text))
            {
                SetStatus("Đang gửi text vào máy...");
                await ShopeeBypassService.PasteTextAsync(ActiveDeviceId, text);
                string preview = text.Length > 15 ? text.Substring(0, 15) + "..." : text;
                SetStatus($"Đã gõ: \"{preview}\" ✓");
            }
            else
            {
                SetStatus("Chưa có chữ để gửi!");
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

        private async void btn_paste_direct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = txt_input.Text.Trim();
                if (string.IsNullOrEmpty(text) && Clipboard.ContainsText())
                {
                    text = Clipboard.GetText().Trim();
                    txt_input.Text = text;
                }

                if (!string.IsNullOrEmpty(text))
                {
                    SetStatus("Đang dán vào điện thoại...");
                    await ShopeeBypassService.PasteTextAsync(ActiveDeviceId, text);
                    string preview = text.Length > 15 ? text.Substring(0, 15) + "..." : text;
                    SetStatus($"Đã dán: \"{preview}\" ✓");
                    return;
                }
                SetStatus("Clipboard & Ô nhập đang trống!");
            }
            catch (Exception ex)
            {
                SetStatus($"Lỗi dán: {ex.Message}");
            }
        }

        private async void btn_install_apk_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Android APK (*.apk)|*.apk|All files (*.*)|*.*",
                Title = "Chọn file APK để cài đặt vào điện thoại"
            };
            if (dlg.ShowDialog() == true)
            {
                SetStatus("Đang cài đặt APK...");
                await ShopeeBypassService.InstallApkAsync(ActiveDeviceId, dlg.FileName);
                SetStatus("Cài đặt APK hoàn tất!");
            }
        }

        private void btn_backup_restore_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Backup/Restore Profiles Shopee và Account Storage đã sẵn sàng!", "Backup / Restore", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void btn_proxy_Click(object sender, RoutedEventArgs e)
        {
            string current = await ShopeeBypassService.RunAdbAsync(ActiveDeviceId, "shell settings get global http_proxy");
            string prompt = string.IsNullOrWhiteSpace(current) || current == "null" || current == ":0"
                ? "Nhập Proxy dạng host:port (Để trống để gỡ proxy):"
                : $"Proxy hiện tại: {current}\nNhập host:port mới hoặc để trống để gỡ:";

            var dlg = new Window
            {
                Title = "Fake Proxy Manager",
                Width = 380,
                Height = 170,
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
                Content = "Áp Dụng",
                Width = 96,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Style = (Style)Application.Current.FindResource("PrimaryButton"),
                IsEnabled = !string.IsNullOrWhiteSpace(txt.Text)
            };

            // Dynamic Enable/Disable: Disabled when empty, enabled when proxy text is entered
            txt.TextChanged += (s, ev) =>
            {
                btnOk.IsEnabled = !string.IsNullOrWhiteSpace(txt.Text);
            };

            btnOk.Click += async (s, ev) =>
            {
                string input = txt.Text.Trim();
                await ShopeeBypassService.SetProxyAsync(ActiveDeviceId, input);
                dlg.Close();
                SetStatus(string.IsNullOrEmpty(input) ? "Đã gỡ Proxy!" : $"Proxy: {input}");
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
                    Arguments = $"/k \"title ADB Shell [{ActiveDeviceId}] && echo ======================================== && echo  ADB SHELL CONNECTED TO: {ActiveDeviceId} && echo ======================================== && adb {devArg}shell\"",
                    WorkingDirectory = baseDir,
                    UseShellExecute = true
                });
                SetStatus("Đã mở ADB Shell ✓");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở ADB: {ex.Message}");
            }
        }

        private async void btn_open_shopee_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Đang mở Shopee...");
            await ShopeeBypassService.OpenShopeeAsync(ActiveDeviceId);
            SetStatus("Đã mở Shopee ✓");
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
                SetStatus($"Lỗi: {ex.Message}");
            }
            finally
            {
                btn_bypass.IsEnabled = true;
            }
        }

        private async void btn_rotate_ip_Click(object sender, RoutedEventArgs e)
        {
            btn_rotate_ip.IsEnabled = false;
            SetStatus("Đang xoay IP 4G...");
            await ShopeeBypassService.RotateAirplaneModeAsync(ActiveDeviceId);
            SetStatus("IP 4G rotated ✓");
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
    }
}
