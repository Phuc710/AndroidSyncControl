using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using AndroidSyncControl.Services.Adb;
using AndroidSyncControl.Services.Device;
using AndroidSyncControl.UI.Helpers;
using AndroidSyncControl.UI.ViewModels;

namespace AndroidSyncControl.UI
{
    /// <summary>
    /// Master Dashboard Window for Multi-Device Android Management and Automation.
    /// Clean, light-themed admin control center with vector icons, animated radar scanning empty-state,
    /// batch operations, and single-device screen mirroring window launcher.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<DeviceItemViewModel> _deviceList = new();
        private readonly List<DeviceItemViewModel> _allDevices = new();
        private DeviceControlWindow? _controlWindow;
        private bool _isRefreshing = false;
        private bool _isBatchOperating = false;

        public MainWindow()
        {
            InitializeComponent();
            dgDevices.ItemsSource = _deviceList;

            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;

            // Double click row to open control window directly
            dgDevices.MouseDoubleClick += (s, e) =>
            {
                if (dgDevices.SelectedItem is DeviceItemViewModel item && !string.IsNullOrEmpty(item.Serial))
                {
                    OpenControlForDevice(item.Serial);
                }
            };
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DeviceConnectionSupervisor.Instance.AttachedDevicesChanged += OnSupervisorAttachedDevicesChanged;
            DeviceConnectionSupervisor.Instance.Start();

            await RefreshDeviceListAsync();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            DeviceConnectionSupervisor.Instance.AttachedDevicesChanged -= OnSupervisorAttachedDevicesChanged;

            if (_controlWindow != null && _controlWindow.IsLoaded)
            {
                _controlWindow.Close();
                _controlWindow = null;
            }
        }

        private void OnSupervisorAttachedDevicesChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await RefreshDeviceListAsync();
            });
        }

        /// <summary>
        /// Scans all online ADB devices, queries model and network info, and populates the dashboard.
        /// </summary>
        public async Task RefreshDeviceListAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                txtFooterSummary.Text = "Đang quét thiết bị kết nối qua ADB...";
                var serials = await ShopeeBypassService.GetAllConnectedDevicesAsync();

                // Fetch details for all devices concurrently
                var tasks = serials.Select(async s =>
                {
                    string model = await ShopeeBypassService.GetDeviceModelAsync(s);
                    string mobileIp = await DeviceNetworkService.GetCurrentMobileIpAsync(s);
                    bool hasMobile = !string.IsNullOrEmpty(mobileIp);
                    string netType = hasMobile ? "4G Cellular" : "WiFi / LAN";
                    string ipAddr = hasMobile ? mobileIp : "Local IP";

                    var item = new DeviceItemViewModel
                    {
                        IsSelected = true,
                        Serial = s,
                        Model = model,
                        NetworkType = netType,
                        IpAddress = ipAddr,
                        Status = "Sẵn sàng",
                        StatusColor = "#10B981",
                        Progress = 0
                    };

                    item.PropertyChanged += OnDeviceItemPropertyChanged;
                    return item;
                }).ToList();

                var deviceItems = await Task.WhenAll(tasks);

                _allDevices.Clear();
                _allDevices.AddRange(deviceItems);

                ApplySearchFilter();

                // Update Stats Ribbon
                int total = deviceItems.Length;
                int mobileIpCount = deviceItems.Count(d => d.NetworkType.Contains("4G"));

                txtStatTotalDevices.Text = total.ToString();
                txtStatReadyDevices.Text = total.ToString();
                txtStatMobileIpDevices.Text = mobileIpCount.ToString();

                txtFooterSummary.Text = total > 0
                    ? $"Đang kết nối {total} thiết bị ({mobileIpCount} máy 4G)"
                    : "Sẵn sàng • Chưa có thiết bị kết nối";
            }
            catch (Exception ex)
            {
                txtFooterSummary.Text = $"Lỗi: {ex.Message}";
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void OnDeviceItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceItemViewModel.IsSelected))
            {
                UpdateSelectedBadge();
            }
        }

        private void UpdateSelectedBadge()
        {
            if (txtSelectedCountBadge == null || _deviceList == null) return;
            int selected = _deviceList.Count(d => d.IsSelected);
            int total = _deviceList.Count;
            txtSelectedCountBadge.Text = $"{selected}/{total} máy";
        }

        private void ApplySearchFilter()
        {
            if (txtSearchFilter == null || panelEmptyState == null || dgDevices == null) return;
            string query = txtSearchFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            _deviceList.Clear();
            foreach (var item in _allDevices)
            {
                if (string.IsNullOrEmpty(query) ||
                    item.Model.ToLowerInvariant().Contains(query) ||
                    item.Serial.ToLowerInvariant().Contains(query) ||
                    item.IpAddress.ToLowerInvariant().Contains(query))
                {
                    _deviceList.Add(item);
                }
            }

            // Toggle empty state vs active DataGrid
            if (_deviceList.Count == 0)
            {
                panelEmptyState.Visibility = Visibility.Visible;
                dgDevices.Visibility = Visibility.Collapsed;
            }
            else
            {
                panelEmptyState.Visibility = Visibility.Collapsed;
                dgDevices.Visibility = Visibility.Visible;
            }

            UpdateSelectedBadge();
        }

        /// <summary>
        /// Opens or brings to focus the single-device control window (Ảnh 2).
        /// </summary>
        public void OpenControlForDevice(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return;

            if (_controlWindow == null || !_controlWindow.IsLoaded)
            {
                _controlWindow = new DeviceControlWindow(serial);
                _controlWindow.Closed += (s, e) => _controlWindow = null;
                _controlWindow.Show();
            }
            else
            {
                _controlWindow.SwitchDevice(serial);
                if (_controlWindow.WindowState == WindowState.Minimized)
                {
                    _controlWindow.WindowState = WindowState.Normal;
                }
                _controlWindow.Activate();
            }
        }

        // ── Single Device Operation Helpers ─────────────────────────────────

        private async Task RunSingleDeviceBypassAsync(DeviceItemViewModel dev)
        {
            dev.IsBusy = true;
            dev.Status = "Bắt đầu bypass...";
            dev.StatusColor = "#F59E0B";
            dev.Progress = 15;

            bool success = await ShopeeBypassService.BypassShopeeAsync(dev.Serial, step =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    dev.Status = step;
                    if (dev.Progress < 85) dev.Progress += 15;
                });
            });

            dev.IsBusy = false;
            dev.Progress = 100;
            if (success)
            {
                dev.Status = "Bypass thành công";
                dev.StatusColor = "#10B981";
            }
            else
            {
                dev.Status = "Bypass thất bại";
                dev.StatusColor = "#EF4444";
            }
        }

        private async Task RotateSingleDeviceIpAsync(DeviceItemViewModel dev)
        {
            dev.IsBusy = true;
            dev.Status = "Đang gạt Airplane mode...";
            dev.StatusColor = "#F59E0B";

            await ShopeeBypassService.RotateAirplaneModeAsync(dev.Serial);

            string newIp = await DeviceNetworkService.GetCurrentMobileIpAsync(dev.Serial);
            dev.IsBusy = false;
            if (!string.IsNullOrEmpty(newIp))
            {
                dev.IpAddress = newIp;
                dev.NetworkType = "4G Cellular";
                dev.Status = "Đổi IP thành công";
                dev.StatusColor = "#10B981";
            }
            else
            {
                dev.Status = "Đã gạt Airplane";
                dev.StatusColor = "#2563EB";
            }
        }

        // ── Toolbar & Event Handlers ────────────────────────────────────────

        private async void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceListAsync();
        }

        private void txtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplySearchFilter();
        }

        private void chkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _deviceList == null) return;
            foreach (var item in _deviceList)
            {
                item.IsSelected = true;
            }
            UpdateSelectedBadge();
        }

        private void chkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _deviceList == null) return;
            foreach (var item in _deviceList)
            {
                item.IsSelected = false;
            }
            UpdateSelectedBadge();
        }

        private void btnRowOpenControl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string serial && !string.IsNullOrEmpty(serial))
            {
                OpenControlForDevice(serial);
            }
        }

        private async void btnRowQuickBypass_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string serial && !string.IsNullOrEmpty(serial))
            {
                var dev = _deviceList.FirstOrDefault(d => d.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
                if (dev != null)
                {
                    await RunSingleDeviceBypassAsync(dev);
                }
            }
        }

        private async void btnBatchBypass_Click(object sender, RoutedEventArgs e)
        {
            if (_isBatchOperating) return;

            var selected = _deviceList.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng tick chọn ít nhất 1 thiết bị trong danh sách.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isBatchOperating = true;
            btnBatchBypass.IsEnabled = false;
            btnBatchRotateIp.IsEnabled = false;
            btnBatchOpenShopee.IsEnabled = false;
            btnBatchClearShopee.IsEnabled = false;
            btnHeaderBypassAll.IsEnabled = false;

            txtFooterSummary.Text = $"Đang thực hiện Bypass Shopee trên {selected.Count} thiết bị đồng thời...";

            try
            {
                var tasks = selected.Select(async dev =>
                {
                    await RunSingleDeviceBypassAsync(dev);
                }).ToList();

                await Task.WhenAll(tasks);
                txtFooterSummary.Text = $"Hoàn tất Bypass cho {selected.Count} thiết bị.";
            }
            catch (Exception ex)
            {
                txtFooterSummary.Text = $"Lỗi khi Bypass: {ex.Message}";
            }
            finally
            {
                _isBatchOperating = false;
                btnBatchBypass.IsEnabled = true;
                btnBatchRotateIp.IsEnabled = true;
                btnBatchOpenShopee.IsEnabled = true;
                btnBatchClearShopee.IsEnabled = true;
                btnHeaderBypassAll.IsEnabled = true;
            }
        }

        private async void btnBatchRotateIp_Click(object sender, RoutedEventArgs e)
        {
            if (_isBatchOperating) return;

            var selected = _deviceList.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng tick chọn ít nhất 1 thiết bị.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isBatchOperating = true;
            btnBatchRotateIp.IsEnabled = false;
            txtFooterSummary.Text = $"Đang xoay IP 4G trên {selected.Count} thiết bị...";

            try
            {
                var tasks = selected.Select(async dev =>
                {
                    await RotateSingleDeviceIpAsync(dev);
                }).ToList();

                await Task.WhenAll(tasks);
                txtFooterSummary.Text = $"Đã hoàn tất xoay IP trên {selected.Count} thiết bị.";
            }
            catch (Exception ex)
            {
                txtFooterSummary.Text = $"Lỗi xoay IP: {ex.Message}";
            }
            finally
            {
                _isBatchOperating = false;
                btnBatchRotateIp.IsEnabled = true;
            }
        }

        private async void btnBatchOpenShopee_Click(object sender, RoutedEventArgs e)
        {
            var selected = _deviceList.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng tick chọn ít nhất 1 thiết bị.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            txtFooterSummary.Text = $"Đang mở Shopee trên {selected.Count} thiết bị...";
            var tasks = selected.Select(async dev =>
            {
                await ShopeeBypassService.OpenShopeeAsync(dev.Serial);
                dev.Status = "Đã mở Shopee";
                dev.StatusColor = "#10B981";
            }).ToList();

            await Task.WhenAll(tasks);
            txtFooterSummary.Text = $"Đã mở Shopee trên {selected.Count} thiết bị.";
        }

        private async void btnBatchClearShopee_Click(object sender, RoutedEventArgs e)
        {
            var selected = _deviceList.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng tick chọn ít nhất 1 thiết bị.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            txtFooterSummary.Text = $"Đang dọn sạch cache Shopee trên {selected.Count} thiết bị...";
            var tasks = selected.Select(async dev =>
            {
                dev.Status = "Đang dọn cache...";
                dev.StatusColor = "#F59E0B";
                await AdbPackageService.ForceStopAsync(dev.Serial, "com.shopee.vn");
                await AdbPackageService.ClearDataAsync(dev.Serial, "com.shopee.vn");
                dev.Status = "Đã dọn sạch Shopee";
                dev.StatusColor = "#10B981";
            }).ToList();

            await Task.WhenAll(tasks);
            txtFooterSummary.Text = $"Đã dọn sạch cache Shopee trên {selected.Count} thiết bị.";
        }

        private async void btnBatchInstallApk_Click(object sender, RoutedEventArgs e)
        {
            var selected = _deviceList.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng tick chọn ít nhất 1 thiết bị.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Filter = "Android Package (*.apk)|*.apk|All Files (*.*)|*.*",
                Title = "Chọn tệp tin APK để cài đặt hàng loạt"
            };

            if (dlg.ShowDialog() == true)
            {
                string apkPath = dlg.FileName;
                txtFooterSummary.Text = $"Đang cài đặt APK trên {selected.Count} thiết bị...";

                var tasks = selected.Select(async dev =>
                {
                    dev.Status = "Đang cài đặt APK...";
                    dev.StatusColor = "#F59E0B";
                    var (ok, _) = await ShopeeBypassService.InstallApkDetailedAsync(dev.Serial, apkPath);
                    dev.Status = ok ? "Cài APK thành công" : "Cài APK thất bại";
                    dev.StatusColor = ok ? "#10B981" : "#EF4444";
                }).ToList();

                await Task.WhenAll(tasks);
                txtFooterSummary.Text = $"Hoàn tất cài đặt APK trên {selected.Count} thiết bị.";
            }
        }

        // ── DataGrid Context Menu Handlers ──────────────────────────────────

        private void mnuRowOpenControl_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item && !string.IsNullOrEmpty(item.Serial))
            {
                OpenControlForDevice(item.Serial);
            }
        }

        private async void mnuRowQuickBypass_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item)
            {
                await RunSingleDeviceBypassAsync(item);
            }
        }

        private async void mnuRowRotateIp_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item)
            {
                await RotateSingleDeviceIpAsync(item);
            }
        }

        private async void mnuRowOpenShopee_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item)
            {
                await ShopeeBypassService.OpenShopeeAsync(item.Serial);
                item.Status = "Đã mở Shopee";
                item.StatusColor = "#10B981";
            }
        }

        private async void mnuRowClearCache_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item)
            {
                item.Status = "Đang dọn cache...";
                item.StatusColor = "#F59E0B";
                await AdbPackageService.ForceStopAsync(item.Serial, "com.shopee.vn");
                await AdbPackageService.ClearDataAsync(item.Serial, "com.shopee.vn");
                item.Status = "Đã dọn sạch Shopee";
                item.StatusColor = "#10B981";
            }
        }

        private async void mnuRowInstallApk_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item)
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "Android Package (*.apk)|*.apk|All Files (*.*)|*.*",
                    Title = $"Cài đặt APK cho {item.Model}"
                };

                if (dlg.ShowDialog() == true)
                {
                    item.Status = "Đang cài APK...";
                    item.StatusColor = "#F59E0B";
                    var (ok, _) = await ShopeeBypassService.InstallApkDetailedAsync(item.Serial, dlg.FileName);
                    item.Status = ok ? "Cài APK thành công" : "Cài APK thất bại";
                    item.StatusColor = ok ? "#10B981" : "#EF4444";
                }
            }
        }

        private async void mnuRowReboot_Click(object sender, RoutedEventArgs e)
        {
            if (dgDevices.SelectedItem is DeviceItemViewModel item)
            {
                var result = MessageBox.Show($"Bạn có chắc chắn muốn khởi động lại thiết bị {item.Model} ({item.Serial})?", 
                    "Xác Nhận Khởi Động Lại", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    item.Status = "Đang khởi động lại...";
                    item.StatusColor = "#EF4444";
                    await ShopeeBypassService.RebootAsync(item.Serial);
                }
            }
        }
    }
}
