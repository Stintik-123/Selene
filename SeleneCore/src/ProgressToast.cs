// Playnite's progress screen (add-ons downloading logos or trailers after a library update, a library update itself...)
// as a notification instead of a full screen: a glass card at the top right, Selene's mark beside the task's words and a
// slim bar in the theme's color that fills as the work goes. Playnite opens that window as a modal one, which blocks every
// other window; the main window is handed back its input, so you keep browsing while the card works. It slides in with a
// sound and slides out when the task is done.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Selene
{
    public static class ProgressToast
    {
        [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hWnd, bool enable);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        private const double CardWidth = 470, Edge = 28, ShadowRoom = 24;

        // false: leave it as Playnite's full screen (no main window to sit on yet, at start-up)
        public static bool Make(Window w)
        {
            var main = Application.Current.MainWindow;
            if (main == null || ReferenceEquals(main, w) || !main.IsVisible || main.WindowState == WindowState.Minimized || main.ActualWidth < 400) return false;
            var vm = w.DataContext;

            // ---- the card ----
            var text = new TextBlock
            {
                FontFamily = Ui.Body, FontSize = 18, Foreground = Ink.Brush("SeleneInkColor"), TextWrapping = TextWrapping.Wrap,
                MaxWidth = CardWidth - 128, TextTrimming = TextTrimming.CharacterEllipsis
            };
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ProgressArgs.Text"));
            var title = new TextBlock { Text = "Selene", FontFamily = Ui.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(0, 0, 0, 3) };
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ink.Brush("SeleneInkColor", 0.14), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
            var fill = new Border
            {
                CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = Ink.Brush("GlyphColor"),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.7, Color = Ink.Of("GlyphColor") }
            };
            var slide = new TranslateTransform();
            fill.RenderTransform = slide;
            track.Child = fill;
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(title);
            words.Children.Add(text);
            words.Children.Add(track);
            UIElement glowUnused;
            var mark = Ui.Mark(40, out glowUnused);
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(0, 0, 18, 0);
            var row = new DockPanel();
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(words);
            var card = new Border
            {
                Width = CardWidth, CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 18, 24, 20),
                Background = Ink.Brush("SeleneGlassColor", 0.94), BorderBrush = Ink.Brush("SeleneInkColor", 0.18), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 30, Opacity = 0.45, Color = Colors.Black },
                Child = row, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(ShadowRoom)
            };
            var root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(card);

            // ---- the window: small, top right, see-through around the card ----
            w.Content = root;
            if (w.AllowsTransparency) w.Background = Brushes.Transparent;
            else { w.Background = Ink.Brush("SeleneGlassColor"); card.Margin = new Thickness(0); card.CornerRadius = new CornerRadius(0); card.Effect = null; }
            w.WindowState = WindowState.Normal;
            w.SizeToContent = SizeToContent.Height;
            w.Width = CardWidth + ShadowRoom * 2;
            w.Left = main.Left + main.ActualWidth - w.Width - Edge + ShadowRoom;
            w.Top = main.Top + Edge - ShadowRoom;
            w.Topmost = true;
            w.ShowInTaskbar = false;

            // ---- in: slides from the right and fades, a sound with it ----
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var move = new TranslateTransform(40, 0);
            card.RenderTransform = move;
            card.Opacity = 0;
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            Ui.NotifySound();

            // ---- the main window gets its input back; the card never takes the focus ----
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */);
            var mainHwnd = new WindowInteropHelper(main).Handle;
            w.Dispatcher.BeginInvoke(new Action(() =>
            {
                EnableWindow(mainHwnd, true);
                main.Activate();
                KeepOnTop(hwnd);
            }), DispatcherPriority.Background);

            // ---- the bar follows the task (polled: Playnite's progress values change on its worker thread) ----
            double shown = -1;
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            bool sweeping = false;
            poll.Tick += (s, e) =>
            {
                try
                {
                    EnableWindow(mainHwnd, true);   // something else may disable it again (Playnite re-arming the modal)
                    KeepOnTop(hwnd);
                    var args = Prop(vm, "ProgressArgs");
                    bool indeterminate = Equals(Prop(args, "IsIndeterminate"), true);
                    double max = ToDouble(Prop(args, "ProgressMaxValue")), value = ToDouble(Prop(args, "CurrentProgressValue"));
                    double width = track.ActualWidth;
                    if (width < 1) return;
                    if (indeterminate || max <= 0)
                    {
                        if (sweeping) return;
                        sweeping = true;
                        fill.BeginAnimation(FrameworkElement.WidthProperty, null);
                        fill.Width = width * 0.32;
                        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-width * 0.32, width, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                        return;
                    }
                    if (sweeping) { sweeping = false; slide.BeginAnimation(TranslateTransform.XProperty, null); slide.X = 0; }
                    double target = width * Math.Max(0, Math.Min(1, value / max));
                    if (Math.Abs(target - shown) < 0.5) return;
                    shown = target;
                    fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                }
                catch { }
            };
            poll.Start();

            // ---- out: slides back to the right and fades, then the window really closes ----
            bool closing = false, closed = false;
            w.Closing += (s, e) =>
            {
                if (closed) return;
                e.Cancel = true;
                if (closing) return;
                closing = true;
                poll.Stop();
                var answer = w.DialogResult;
                var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeIn });
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeIn };
                fade.Completed += (s2, e2) =>
                {
                    closed = true;
                    try { if (answer.HasValue) w.DialogResult = answer; else w.Close(); }
                    catch { try { w.Close(); } catch { } }
                };
                card.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            HomeNav.Log("progress look: notification card");
            return true;
        }

        private static void KeepOnTop(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE NOMOVE NOACTIVATE */);
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static double ToDouble(object o)
        {
            try { return o == null ? 0 : Convert.ToDouble(o); } catch { return 0; }
        }

        // ---- a dev preview: %TEMP%\selene-dev-toast makes a pretend 8 s task a few seconds after start (then the file goes) ----
        public static void DevPreviewLater()
        {
            var flag = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-dev-toast");
            if (!System.IO.File.Exists(flag)) return;
            try { System.IO.File.Delete(flag); } catch { }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var api = HomeNav.Api;
                if (api == null) return;
                HomeNav.Log("dev: notification preview");
                api.Dialogs.ActivateGlobalProgress(a =>
                {
                    a.ProgressMaxValue = 40;
                    for (int i = 0; i <= 40 && !a.CancelToken.IsCancellationRequested; i++)
                    {
                        a.CurrentProgressValue = i;
                        a.Text = "Downloading logos of newly added games… " + i + "/40";
                        System.Threading.Thread.Sleep(200);
                    }
                }, new Playnite.SDK.GlobalProgressOptions("Downloading logos of newly added games…", false) { IsIndeterminate = false });
            };
            t.Start();
        }
    }
}
