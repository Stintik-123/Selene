// Diagnostic for the dark veil seen behind the progress notification: logs every window of Playnite's process (WPF's list
// and Windows' own, with size, visibility and transparency), the home's own opacity, and how bright the home really draws
// (a small picture of it, averaged) before and during the notification. Read %TEMP%\selene.log after a run.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Selene
{
    public static class DimProbe
    {
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

        // the home's brightness right now (0-255), from a small picture of the main window's content
        public static double HomeBrightness()
        {
            try
            {
                var main = Application.Current.MainWindow;
                var root = main != null ? main.Content as FrameworkElement : null;
                if (root == null || root.ActualWidth < 10) return -1;
                const int w = 96, h = 54;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var px = new byte[w * h * 4];
                rtb.CopyPixels(px, w * 4, 0);
                double sum = 0;
                for (int i = 0; i < px.Length; i += 4) sum += 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
                return sum / (w * h);
            }
            catch { return -1; }
        }

        public static void Log(string when)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("dim probe (" + when + "): home brightness " + HomeBrightness().ToString("0.0"));
                var main = Application.Current.MainWindow;
                var root = main != null ? main.Content as UIElement : null;
                if (main != null)
                    sb.Append(", main opacity " + main.Opacity.ToString("0.00") + ", content opacity " + (root != null ? root.Opacity.ToString("0.00") : "?")
                              + ", main enabled " + main.IsEnabled + " / win32 " + IsWindowEnabled(new WindowInteropHelper(main).Handle) + ", active " + main.IsActive);
                foreach (var name in new[] { "PART_ViewHost", "WelcomeHubOverlay", "AcceuilSettings", "SeleneLibrary" })
                {
                    var el = main != null ? HomeNav.FindByName(main, name) as UIElement : null;
                    if (el != null) sb.Append(" | " + name + " vis=" + el.Visibility + " op=" + el.Opacity.ToString("0.00"));
                }
                HomeNav.Log(sb.ToString());
                foreach (Window w in Application.Current.Windows)
                    HomeNav.Log("  wpf window " + w.GetType().Name + " \"" + w.Title + "\" visible=" + w.IsVisible + " opacity=" + w.Opacity.ToString("0.00")
                                + " at " + (int)w.Left + "," + (int)w.Top + " " + (int)w.ActualWidth + "x" + (int)w.ActualHeight + " state=" + w.WindowState
                                + " bg=" + (w.Background != null ? w.Background.ToString() : "null") + " topmost=" + w.Topmost);
                uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                EnumWindows((h, l) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != me || !IsWindowVisible(h)) return true;
                    RECT r; GetWindowRect(h, out r);
                    var cls = new StringBuilder(128); GetClassName(h, cls, 128);
                    var title = new StringBuilder(128); GetWindowText(h, title, 128);
                    int ex = GetWindowLong(h, -20);
                    uint key, flags; byte alpha = 255;
                    bool layered = (ex & 0x80000) != 0 && GetLayeredWindowAttributes(h, out key, out alpha, out flags);
                    HomeNav.Log("  win32 window " + cls + " \"" + title + "\" " + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T)
                                + " exstyle=0x" + ex.ToString("X") + (layered ? " alpha=" + alpha : ""));
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { HomeNav.Log("dim probe failed: " + ex.Message); }
        }

        // before, and twice during, a notification
        public static void AroundNotification()
        {
            Log("card appearing");
            foreach (var ms in new[] { 700, 2000 })
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                string label = "card up " + ms + " ms";
                t.Tick += (s, e) => { t.Stop(); Log(label); };
                t.Start();
            }
        }
    }
}
