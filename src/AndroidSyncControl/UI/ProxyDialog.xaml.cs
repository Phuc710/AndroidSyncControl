using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AndroidSyncControl.Localization;
using AndroidSyncControl.UI.Helpers;

namespace AndroidSyncControl.UI
{
    public partial class ProxyDialog : Window
    {
        private readonly string _deviceId;

        public ProxyDialog(string deviceId, Window? owner = null)
        {
            InitializeComponent();
            _deviceId = deviceId;
            if (owner != null) Owner = owner;

            Loaded += async (s, e) => await LoadCurrentProxyAsync();
        }

        private async Task LoadCurrentProxyAsync()
        {
            SetBusy(true, "Đang kiểm tra cấu hình proxy...");
            try
            {
                string proxy = await ShopeeBypassService.CheckDeviceProxyAsync(_deviceId);
                UpdateProxyStateUI(proxy);
            }
            catch (Exception ex)
            {
                txtFeedback.Text = $"Lỗi: {ex.Message}";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateProxyStateUI(string proxy)
        {
            if (string.IsNullOrEmpty(proxy))
            {
                txtCurrentProxy.Text = LanguageManager.GetString("Str.Proxy.Direct");
                txtCurrentProxy.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
                dotProxyStatus.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                bdrStatusCard.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                bdrStatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            }
            else
            {
                txtCurrentProxy.Text = string.Format(LanguageManager.GetString("Str.Proxy.Active"), proxy);
                txtCurrentProxy.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
                dotProxyStatus.Fill = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                bdrStatusCard.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
                bdrStatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE));

                // Prepopulate inputs if empty
                if (string.IsNullOrWhiteSpace(txtHost.Text))
                {
                    var parts = proxy.Split(':');
                    if (parts.Length > 0) txtHost.Text = parts[0];
                    if (parts.Length > 1) txtPort.Text = parts[1];
                }
            }
        }

        private void txtHost_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string text = txtHost.Text.Trim();
            if (text.Contains(':'))
            {
                var parts = text.Split(':');
                txtHost.Text = parts[0].Trim();
                if (parts.Length > 1) txtPort.Text = parts[1].Trim();
                txtHost.CaretIndex = txtHost.Text.Length;
            }
        }

        private async void btnRefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await LoadCurrentProxyAsync();
        }

        private async void btnApplyProxy_Click(object sender, RoutedEventArgs e)
        {
            string host = txtHost.Text.Trim();
            string port = txtPort.Text.Trim();

            if (string.IsNullOrEmpty(host))
            {
                txtFeedback.Text = "Vui lòng nhập địa chỉ Host/IP";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                txtHost.Focus();
                return;
            }

            if (string.IsNullOrEmpty(port) || !int.TryParse(port, out int p) || p < 1 || p > 65535)
            {
                txtFeedback.Text = "Cổng (Port) không hợp lệ (1 - 65535)";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                txtPort.Focus();
                return;
            }

            string proxyTarget = $"{host}:{port}";
            SetBusy(true, $"Đang áp dụng proxy {proxyTarget}...");

            try
            {
                await ShopeeBypassService.SetProxyAsync(_deviceId, proxyTarget);
                await LoadCurrentProxyAsync();
                txtFeedback.Text = $"Đã áp dụng proxy {proxyTarget} thành công ✓";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            }
            catch (Exception ex)
            {
                txtFeedback.Text = $"Lỗi: {ex.Message}";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void btnDisableProxy_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true, "Đang tắt proxy...");
            try
            {
                await ShopeeBypassService.SetProxyAsync(_deviceId, string.Empty);
                txtHost.Text = string.Empty;
                txtPort.Text = string.Empty;
                await LoadCurrentProxyAsync();
                txtFeedback.Text = LanguageManager.GetString("Str.Status.ProxyCleared");
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            }
            catch (Exception ex)
            {
                txtFeedback.Text = $"Lỗi: {ex.Message}";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void btnTestIp_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true, LanguageManager.GetString("Str.Proxy.Testing"));
            try
            {
                var result = await ShopeeBypassService.TestDeviceNetworkAsync(_deviceId);
                if (result.Success)
                {
                    txtFeedback.Text = string.Format(LanguageManager.GetString("Str.Proxy.TestSuccess"), result.Message);
                    txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                }
                else
                {
                    txtFeedback.Text = result.Message;
                    txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                }
            }
            catch (Exception ex)
            {
                txtFeedback.Text = $"Lỗi kiểm tra mạng: {ex.Message}";
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy, string message = "")
        {
            pbProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            btnApplyProxy.IsEnabled = !busy;
            btnDisableProxy.IsEnabled = !busy;
            btnTestIp.IsEnabled = !busy;
            btnRefreshStatus.IsEnabled = !busy;

            if (!string.IsNullOrEmpty(message))
            {
                txtFeedback.Text = message;
                txtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }
        }
    }
}
