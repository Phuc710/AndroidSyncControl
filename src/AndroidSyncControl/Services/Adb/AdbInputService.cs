using System;
using System.Threading.Tasks;
using System.Windows;

namespace AndroidSyncControl.Services.Adb
{
    /// <summary>
    /// Service for injecting inputs, key events, swipes, and clipboard text into Android devices.
    /// </summary>
    public static class AdbInputService
    {
        public static void SafeSetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                void DoSet()
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try { Clipboard.SetDataObject(text, true); return; }
                        catch { System.Threading.Thread.Sleep(20); }
                    }
                }

                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                    DoSet();
                else
                    Application.Current?.Dispatcher?.Invoke(DoSet);
            }
            catch { }
        }

        public static async Task DirectClipboardPasteAsync(string deviceId, string text, Action? triggerScrcpyPaste = null)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(deviceId)) return;

            // 1. Always synchronize Windows clipboard so the text is preserved on PC
            SafeSetClipboard(text);

            // 2. Direct ADB text injection from PC to Mobile (Zero reliance on mobile clipboard)
            // Batched in a single shell command to eliminate multi-line input lag
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var commands = new System.Collections.Generic.List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!string.IsNullOrEmpty(line))
                {
                    string formatted = FormatForAdbInputText(line);
                    commands.Add($"input text {formatted}");
                }

                if (i < lines.Length - 1)
                {
                    commands.Add("input keyevent 66"); // KEYCODE_ENTER
                }
            }

            if (commands.Count > 0)
            {
                await AdbExecutor.RunAdbShellBatchAsync(deviceId, commands, 10000);
            }
        }

        public static string FormatForAdbInputText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            // In Android's input command, spaces are represented by %s
            string withSpaces = text.Replace(" ", "%s");

            // Escape single quotes for POSIX sh: ' -> '\''
            string escaped = withSpaces.Replace("'", "'\\''");

            return $"'{escaped}'";
        }

        public static string EscapeShellSingleQuote(string text)
        {
            return text.Replace("'", "'\\''");
        }

        public static async Task SendKeyAsync(string deviceId, int keyCode)
        {
            await AdbExecutor.RunAdbAsync(deviceId, $"shell input keyevent {keyCode}");
        }

        public static async Task VolumeUpAsync(string deviceId) => await SendKeyAsync(deviceId, 24);
        public static async Task VolumeDownAsync(string deviceId) => await SendKeyAsync(deviceId, 25);
        public static async Task MuteAsync(string deviceId) => await SendKeyAsync(deviceId, 164);
        public static async Task PowerAsync(string deviceId) => await SendKeyAsync(deviceId, 26);
        public static async Task RebootAsync(string deviceId) => await AdbExecutor.RunAdbAsync(deviceId, "reboot");
        public static async Task SwipeUpAsync(string deviceId) => await AdbExecutor.RunAdbAsync(deviceId, "shell input swipe 360 1000 360 250 200");
        public static async Task SwipeDownAsync(string deviceId) => await AdbExecutor.RunAdbAsync(deviceId, "shell input swipe 360 250 360 1000 200");
    }
}
