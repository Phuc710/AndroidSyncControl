using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AndroidSyncControl.UI.Helpers
{
    public class ClipboardWatcher : IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private IntPtr _hwnd;
        private HwndSource _source;
        public event Action<string> OnClipboardChanged;

        public void Attach(Window window)
        {
            var helper = new WindowInteropHelper(window);
            _hwnd = helper.Handle;
            if (_hwnd != IntPtr.Zero)
            {
                Hook();
            }
            else
            {
                window.SourceInitialized += (s, e) =>
                {
                    _hwnd = new WindowInteropHelper(window).Handle;
                    Hook();
                };
            }
        }

        private void Hook()
        {
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(WndProc);
            AddClipboardFormatListener(_hwnd);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text))
                        {
                            OnClipboardChanged?.Invoke(text);
                        }
                    }
                }
                catch
                {
                    // Clipboard access might be locked temporarily by another app
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_hwnd);
                _source?.RemoveHook(WndProc);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
