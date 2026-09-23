using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AndroidSyncControl.Localization;
using AndroidSyncControl.UI.Helpers;

namespace AndroidSyncControl.UI
{
    public partial class BackupRestoreDialog : Window
    {
        private readonly string _deviceId;

        public BackupRestoreDialog(string deviceId, Window? owner = null)
        {
            InitializeComponent();
            _deviceId = deviceId;
            if (owner != null) Owner = owner;

            Loaded += (s, e) => RefreshBackups();
        }

        private void RefreshBackups()
        {
            try
            {
                var list = ShopeeBypassService.GetBackupList();
                lstBackups.ItemsSource = list;
                txtEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Lỗi đọc danh sách: {ex.Message}";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
        }

        private async void btnCreateBackup_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_deviceId))
            {
                MessageBox.Show("Chưa kết nối thiết bị Android!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true, LanguageManager.GetString("Str.Backup.BackingUp"));

            try
            {
                var result = await ShopeeBypassService.BackupShopeeDataAsync(_deviceId, status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = status;
                        txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                    });
                });

                if (result.Success && result.Info != null)
                {
                    txtStatus.Text = $"Sao lưu thành công! ({result.Info.TotalSizeFormatted}) ✓";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                    RefreshBackups();
                }
                else
                {
                    txtStatus.Text = $"Sao lưu thất bại: {result.Error}";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Lỗi: {ex.Message}";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void btnItemRestore_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_deviceId))
            {
                MessageBox.Show("Chưa kết nối thiết bị Android!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if ((sender as FrameworkElement)?.Tag is not ShopeeBypassService.ShopeeBackupInfo backup) return;

            string confirm = LanguageManager.GetString("Str.Backup.ConfirmRestore");
            if (MessageBox.Show(confirm, LanguageManager.GetString("Str.Backup.RestoreBtn"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SetBusy(true, LanguageManager.GetString("Str.Backup.Restoring"));

            try
            {
                var result = await ShopeeBypassService.RestoreShopeeDataAsync(_deviceId, backup, status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = status;
                        txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                    });
                });

                if (result.Success)
                {
                    txtStatus.Text = result.Message;
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                }
                else
                {
                    txtStatus.Text = $"Khôi phục thất bại: {result.Message}";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Lỗi: {ex.Message}";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void btnItemDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ShopeeBypassService.ShopeeBackupInfo backup) return;

            string confirm = LanguageManager.GetString("Str.Backup.ConfirmDelete");
            if (MessageBox.Show(confirm, LanguageManager.GetString("Str.Backup.DeleteBtn"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    if (Directory.Exists(backup.DirectoryPath))
                    {
                        Directory.Delete(backup.DirectoryPath, true);
                    }
                    RefreshBackups();
                    txtStatus.Text = "Đã xóa bản sao lưu.";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                }
                catch (Exception ex)
                {
                    txtStatus.Text = $"Lỗi xóa: {ex.Message}";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                }
            }
        }

        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = ShopeeBypassService.GetDefaultBackupDir();
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Lỗi mở thư mục: {ex.Message}";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshBackups();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetBusy(bool busy, string message = "")
        {
            pbProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            btnCreateBackup.IsEnabled = !busy;
            lstBackups.IsEnabled = !busy;

            if (!string.IsNullOrEmpty(message))
            {
                txtStatus.Text = message;
            }
        }
    }
}
