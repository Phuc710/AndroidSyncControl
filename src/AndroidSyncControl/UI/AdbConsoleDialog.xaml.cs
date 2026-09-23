using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AndroidSyncControl.Localization;
using AndroidSyncControl.UI.Helpers;

namespace AndroidSyncControl.UI
{
    public partial class AdbConsoleDialog : Window
    {
        private readonly string _deviceId;
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;

        public AdbConsoleDialog(string deviceId, Window? owner = null)
        {
            InitializeComponent();
            _deviceId = deviceId;
            if (owner != null) Owner = owner;

            txtDeviceSerial.Text = $"Device: {_deviceId}";
            Loaded += AdbConsoleDialog_Loaded;
        }

        private void AdbConsoleDialog_Loaded(object sender, RoutedEventArgs e)
        {
            AppendLog("==========================================================================");
            AppendLog($"  ADB CONSOLE - DEVICE: {_deviceId}");
            AppendLog($"  Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Ready for execution.");
            AppendLog("==========================================================================");
            txtCommand.Focus();
        }

        private async void btnRun_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCurrentCommandAsync();
        }

        private async void txtCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !txtCommand.Text.Contains('\n')))
            {
                e.Handled = true;
                await ExecuteCurrentCommandAsync();
            }
            else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None && txtCommand.CaretIndex == 0)
            {
                if (_commandHistory.Count > 0 && _historyIndex > 0)
                {
                    _historyIndex--;
                    txtCommand.Text = _commandHistory[_historyIndex];
                    txtCommand.CaretIndex = txtCommand.Text.Length;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None && txtCommand.CaretIndex == txtCommand.Text.Length)
            {
                if (_commandHistory.Count > 0 && _historyIndex < _commandHistory.Count - 1)
                {
                    _historyIndex++;
                    txtCommand.Text = _commandHistory[_historyIndex];
                    txtCommand.CaretIndex = txtCommand.Text.Length;
                    e.Handled = true;
                }
            }
        }

        private async Task ExecuteCurrentCommandAsync()
        {
            string rawCmd = txtCommand.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(rawCmd)) return;

            if (string.IsNullOrEmpty(_deviceId))
            {
                MessageBox.Show("Chưa kết nối thiết bị Android!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save to history
            if (_commandHistory.Count == 0 || _commandHistory[_commandHistory.Count - 1] != rawCmd)
            {
                _commandHistory.Add(rawCmd);
            }
            _historyIndex = _commandHistory.Count;

            SetBusy(true);

            // Normalize command string: handle adb prefix and direct adb verbs
            string targetCmd = rawCmd;
            if (targetCmd.StartsWith("adb -s ", StringComparison.OrdinalIgnoreCase))
            {
                int nextSpace = targetCmd.IndexOf(' ', 7);
                if (nextSpace > 0)
                {
                    targetCmd = targetCmd.Substring(nextSpace + 1).Trim();
                }
            }
            else if (targetCmd.StartsWith("adb ", StringComparison.OrdinalIgnoreCase))
            {
                targetCmd = targetCmd.Substring(4).Trim();
            }

            string adbArg;
            string[] directAdbVerbs = new[] { "devices", "push", "pull", "install", "uninstall", "reboot", "remount", "root", "unroot", "tcpip", "forward", "reverse", "logcat", "bugreport" };

            bool isDirectAdb = false;
            foreach (var verb in directAdbVerbs)
            {
                if (targetCmd.Equals(verb, StringComparison.OrdinalIgnoreCase) || 
                    targetCmd.StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase))
                {
                    isDirectAdb = true;
                    break;
                }
            }

            if (isDirectAdb)
            {
                adbArg = targetCmd;
                AppendLog($"\n> [{DateTime.Now:HH:mm:ss}] adb: {targetCmd}");
            }
            else
            {
                if (targetCmd.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
                {
                    targetCmd = targetCmd.Substring(6).Trim();
                }

                // Normalize multi-line scripts to chained commands with ';'
                var lines = new List<string>();
                foreach (var line in targetCmd.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                    {
                        lines.Add(trimmed);
                    }
                }
                string script = string.Join(" ; ", lines);

                string escaped = script.Replace("\"", "\\\"");
                adbArg = $"shell \"{escaped}\"";
                AppendLog($"\n> [{DateTime.Now:HH:mm:ss}] shell: {script}");
            }

            var sw = Stopwatch.StartNew();

            try
            {
                string output = await ShopeeBypassService.RunAdbAsync(_deviceId, adbArg, 180000);
                sw.Stop();

                if (string.IsNullOrWhiteSpace(output))
                {
                    AppendLog("(Không có output trả về - lệnh đã thực thi thành công)");
                }
                else
                {
                    AppendLog(output);
                }

                txtStatus.Text = $"Lệnh hoàn tất trong {sw.ElapsedMilliseconds} ms ✓";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppendLog($"[LỖI] {ex.Message}");
                txtStatus.Text = $"Lỗi thực thi ({sw.ElapsedMilliseconds} ms)";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void AppendLog(string text)
        {
            txtConsole.AppendText(text + Environment.NewLine);
            txtConsole.ScrollToEnd();
        }

        private void btnClearInput_Click(object sender, RoutedEventArgs e)
        {
            txtCommand.Text = string.Empty;
            txtCommand.Focus();
        }

        private void btnClearOutput_Click(object sender, RoutedEventArgs e)
        {
            txtConsole.Text = string.Empty;
            AppendLog($"[Terminal cleared at {DateTime.Now:HH:mm:ss}]");
        }

        private void btnCopyOutput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(txtConsole.Text))
                {
                    Clipboard.SetText(txtConsole.Text);
                    txtStatus.Text = "Đã sao chép output vào clipboard ✓";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                }
            }
            catch { }
        }

        private void btnOpenCmd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string adbExe = AndroidToolchain.AdbPath;
                string devArg = !string.IsNullOrEmpty(_deviceId) ? $"-s {_deviceId} " : "";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"title [ROOT_Shopee] ADB Shell [{_deviceId}] && echo ======================================== && echo [ROOT_Shopee] ADB Shell - Device: {_deviceId} && echo ======================================== && \"{adbExe}\" {devArg}shell\"",
                    WorkingDirectory = baseDir,
                    UseShellExecute = true
                });

                txtStatus.Text = "Đã mở cửa sổ CMD shell ngoài ✓";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi mở CMD: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetBusy(bool busy)
        {
            pbRunning.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            btnRun.IsEnabled = !busy;

            if (busy)
            {
                txtStatus.Text = LanguageManager.GetString("Str.Adb.Running");
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
            }
        }
    }
}
