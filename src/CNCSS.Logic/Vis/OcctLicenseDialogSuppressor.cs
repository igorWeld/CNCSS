using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Occt;

namespace CNCSS.Vis
{
    /// <summary>
    /// TianTeng Occt.NET shows a modal license reminder ("Occt.NET!") on STEP/IGES use without <c>Occt.lic</c>.
    /// Import still works; this helper auto-dismisses that native dialog so WPF is not blocked.
    /// </summary>
    internal sealed class OcctLicenseDialogSuppressor : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _thread;

        private OcctLicenseDialogSuppressor()
        {
            _thread = new Thread(DismissLoop)
            {
                Name = "CNCSS Occt dialog dismiss",
                IsBackground = true
            };
            _thread.Start();
        }

        public static OcctLicenseDialogSuppressor Start() => new();

        public void Dispose()
        {
            _cts.Cancel();
            TryDismissOcctDialogs();
        }

        private void DismissLoop()
        {
            CancellationToken token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                TryDismissOcctDialogs();
                Thread.Sleep(50);
            }
        }

        private static void TryDismissOcctDialogs()
        {
            EnumWindows(static (hwnd, _) =>
            {
                if (!IsOcctLicenseDialog(hwnd))
                {
                    return true;
                }

                IntPtr ok = FindWindowEx(hwnd, IntPtr.Zero, "Button", null);
                if (ok != IntPtr.Zero)
                {
                    PostMessage(ok, BmClick, IntPtr.Zero, IntPtr.Zero);
                }
                else
                {
                    PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                }

                return true;
            }, IntPtr.Zero);
        }

        private static bool IsOcctLicenseDialog(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd))
            {
                return false;
            }

            var className = new StringBuilder(64);
            if (GetClassName(hwnd, className, className.Capacity) == 0)
            {
                return false;
            }

            if (!className.ToString().Equals("#32770", StringComparison.Ordinal))
            {
                return false;
            }

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            string titleText = title.ToString();
            if (titleText.Contains("Occt.NET", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return WindowContainsNeedle(hwnd, "tengxuekeji")
                   || WindowContainsNeedle(hwnd, "MachineCode");
        }

        private static bool WindowContainsNeedle(IntPtr hwnd, string needle)
        {
            bool found = false;
            EnumChildWindows(hwnd, (child, _) =>
            {
                var text = new StringBuilder(512);
                GetWindowText(child, text, text.Capacity);
                if (text.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int WmClose = 0x0010;
        private const int BmClick = 0x00F5;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
