using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AndroidSyncControl.Localization;
using AndroidSyncControl.Update;

namespace AndroidSyncControl.UI
{
    public partial class UpdateDialog : Window
    {
        private readonly UpdateManifest _manifest;
        private readonly UpdateService _updateService;
        private CancellationTokenSource? _cts;

        public UpdateDialog(UpdateManifest manifest, UpdateService updateService)
        {
            InitializeComponent();
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

            LoadManifestData();
        }

        private void LoadManifestData()
        {
            txt_product_version.Text = $"{_manifest.Product} {_manifest.Version}";

            string currentVer = UpdateService.CurrentVersion.ToString(3);
            double sizeMb = Math.Round((double)_manifest.Package.Size / (1024 * 1024), 1);
            txt_version_info.Text = $"{currentVer} → {_manifest.Version} • {sizeMb} MB";

            if (_manifest.Release.Changelog.Count > 0)
            {
                ic_changelog.ItemsSource = _manifest.Release.Changelog;
            }
            else
            {
                ic_changelog.ItemsSource = new[] { "Bug fixes and performance improvements." };
            }
        }

        private void btn_later_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        private async void btn_update_Click(object sender, RoutedEventArgs e)
        {
            btn_update.IsEnabled = false;
            btn_later.IsEnabled = false;
            panel_progress.Visibility = Visibility.Visible;
            pb_download.Value = 0;
            
            string downloadingText = LanguageManager.GetString("Str.Update.Downloading");
            txt_progress_status.Text = $"{downloadingText} 0%";
            txt_progress_status.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

            _cts = new CancellationTokenSource();

            var progress = new Progress<int>(percent =>
            {
                pb_download.Value = percent;
                txt_progress_status.Text = $"{downloadingText} {percent}%";
            });

            try
            {
                string? packagePath = await _updateService.DownloadPackageAsync(_manifest, progress, _cts.Token);

                if (string.IsNullOrEmpty(packagePath))
                {
                    txt_progress_status.Text = LanguageManager.GetString("Str.Update.Failed");
                    txt_progress_status.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    btn_update.IsEnabled = true;
                    btn_update.Content = "Thử lại";
                    btn_later.IsEnabled = true;
                    return;
                }

                txt_progress_status.Text = LanguageManager.GetString("Str.Update.Installing");
                txt_progress_status.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

                await Task.Delay(300);

                bool launched = _updateService.LaunchUpdaterAndExit(packagePath, _manifest);
                if (!launched)
                {
                    txt_progress_status.Text = LanguageManager.GetString("Str.Update.Failed");
                    txt_progress_status.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    btn_update.IsEnabled = true;
                    btn_later.IsEnabled = true;
                }
            }
            catch (Exception)
            {
                txt_progress_status.Text = LanguageManager.GetString("Str.Update.Failed");
                txt_progress_status.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                btn_update.IsEnabled = true;
                btn_update.Content = "Thử lại";
                btn_later.IsEnabled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnClosed(e);
        }
    }
}
