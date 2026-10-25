// Playnite's progress screen (add-ons downloading logos or trailers after a library update, a library update itself...)
// as a notification instead of a full screen: a glass card at the top right, Selene's mark beside the task's words and a
// slim bar in the theme's color that fills as the work goes.
// Playnite opens that screen as a modal window and waits for it to close before carrying on, so it isn't held open: it is
// moved off screen (invisible, the main window handed its input back) and the card is a window of Selene's own that mirrors
// it. The card slides in from beyond the right edge with a sound, stays at least a few seconds so it can be read (the bar
// runs to the end when the task is done), and slides back out. Tasks that come one after another share the card.
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
        private const int MinShownMs = 2600, AfterDoneMs = 1100, InMs = 460, OutMs = 380;

        // the card (one at a time)
        private static Window toast;
        private static Border card, track, fill;
        private static TextBlock words;
        private static TranslateTransform slideIn, sweep;
        private static DispatcherTimer poll, leaveTimer;
        private static DateTime shownAt;
        private static object source;      // the progress window's view model being mirrored (null: its task is done)
        private static bool sweeping, leaving;
        private static double barShown = -1;

        // false: leave it as Playnite's full screen (no main window to sit on yet, at start-up)
        public static bool Make(Window w)
        {
            var main = Application.Current.MainWindow;
            if (main == null || ReferenceEquals(main, w) || !main.IsVisible || main.WindowState == WindowState.Minimized || main.ActualWidth < 400) return false;

            // Playnite's own window: out of sight and out of the way (it closes by itself when the task ends)
            w.WindowState = WindowState.Normal;
            w.SizeToContent = SizeToContent.Manual;
            w.Width = 1; w.Height = 1;
            w.Left = -32000; w.Top = -32000;
            w.Opacity = 0;
            w.ShowInTaskbar = false;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */);
            var mainHwnd = new WindowInteropHelper(main).Handle;
            // the modal window disabled the main one: it gets its input back, so browsing goes on while the task runs
            w.Dispatcher.BeginInvoke(new Action(() => { EnableWindow(mainHwnd, true); main.Activate(); }), DispatcherPriority.Background);
            w.Closed += (s, e) => TaskDone(w.DataContext);

            Show(main, w.DataContext);
            HomeNav.Log("progress look: notification card");
            return true;
        }

        private static void Show(Window main, object vm)
        {
            source = vm;
            if (toast == null) Build();
            if (leaveTimer != null) leaveTimer.Stop();
            barShown = -1;
            Place(main);
            if (!toast.IsVisible || leaving)
            {
                // in: from beyond the right edge of the screen, easing into place, with a sound
                leaving = false;
                shownAt = DateTime.Now;
                if (!toast.IsVisible) toast.Show();
                KeepOnTop();
                slideIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(CardWidth + Edge + ShadowRoom, 0, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(InMs * 0.6)));
                Ui.NotifySound();
            }
            Update();
            poll.Start();
        }

        // the window spans from the card to the screen's right edge, so the card can slide in from beyond it
        private static void Place(Window main)
        {
            toast.Width = CardWidth + ShadowRoom + Edge;
            toast.Left = main.Left + main.ActualWidth - toast.Width;
            toast.Top = main.Top + Edge - ShadowRoom;
        }

        private static void Build()
        {
            words = new TextBlock
            {
                FontFamily = Ui.Body, FontSize = 18, Foreground = Ink.Brush("SeleneInkColor"), TextWrapping = TextWrapping.Wrap,
                MaxWidth = CardWidth - 128, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 52
            };
            var title = new TextBlock { Text = "Selene", FontFamily = Ui.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(0, 0, 0, 3) };
            track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ink.Brush("SeleneInkColor", 0.14), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
            fill = new Border
            {
                CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = Ink.Brush("GlyphColor"),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.7, Color = Ink.Of("GlyphColor") }
            };
            sweep = new TranslateTransform();
            fill.RenderTransform = sweep;
            track.Child = fill;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(words);
            text.Children.Add(track);
            UIElement glow;
            var mark = Ui.Mark(40, out glow);
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(0, 0, 18, 0);
            var row = new DockPanel();
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(text);
            slideIn = new TranslateTransform();
            card = new Border
            {
                Width = CardWidth, CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 18, 24, 20),
                Background = Ink.Brush("SeleneGlassColor", 0.94), BorderBrush = Ink.Brush("SeleneInkColor", 0.18), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 30, Opacity = 0.45, Color = Colors.Black },
                Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(ShadowRoom, ShadowRoom, Edge, ShadowRoom), RenderTransform = slideIn
            };
            var root = new Grid { Background = Brushes.Transparent, ClipToBounds = true, IsHitTestVisible = false };
            root.Children.Add(card);
            toast = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
                Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false, SizeToContent = SizeToContent.Height,
                Title = "SeleneNotification", Content = root
            };
            toast.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(toast).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */ | 0x20 /* TRANSPARENT: clicks go through */);
            };
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            poll.Tick += (s, e) => { KeepOnTop(); Update(); };
        }

        // words and bar from the task (polled: Playnite changes them on its worker thread)
        private static void Update()
        {
            try
            {
                if (source == null) return;
                var args = Prop(source, "ProgressArgs");
                var text = Prop(args, "Text") as string;
                if (!string.IsNullOrEmpty(text) && words.Text != text) words.Text = text;
                bool indeterminate = Equals(Prop(args, "IsIndeterminate"), true);
                double max = ToDouble(Prop(args, "ProgressMaxValue")), value = ToDouble(Prop(args, "CurrentProgressValue"));
                double width = track.ActualWidth;
                if (width < 1) return;
                if (indeterminate || max <= 0) { Sweep(width); return; }
                Bar(width * Math.Max(0, Math.Min(1, value / max)));
            }
            catch { }
        }

        private static void Sweep(double width)
        {
            if (sweeping) return;
            sweeping = true;
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            fill.Width = width * 0.32;
            sweep.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-width * 0.32, width, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        private static void Bar(double target)
        {
            if (sweeping) { sweeping = false; sweep.BeginAnimation(TranslateTransform.XProperty, null); sweep.X = 0; fill.Width = 0; barShown = -1; }
            if (Math.Abs(target - barShown) < 0.5) return;
            barShown = target;
            fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // the task ended: the bar runs to the end, the card stays to be read, then slides out (unless another task takes it)
        private static void TaskDone(object vm)
        {
            if (!ReferenceEquals(vm, source) || toast == null) return;
            source = null;
            poll.Stop();
            if (track.ActualWidth > 1) Bar(track.ActualWidth);
            int wait = Math.Max(AfterDoneMs, MinShownMs - (int)(DateTime.Now - shownAt).TotalMilliseconds);
            leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(wait) };
            leaveTimer.Tick += (s, e) => { leaveTimer.Stop(); Leave(); };
            leaveTimer.Start();
        }

        private static void Leave()
        {
            if (source != null || toast == null) return;
            leaving = true;
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(CardWidth + Edge + ShadowRoom, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = easeIn };
            slide.Completed += (s, e) =>
            {
                if (!leaving) return;   // a new task brought it back meanwhile
                leaving = false;
                toast.Hide();
                sweeping = false;
                sweep.BeginAnimation(TranslateTransform.XProperty, null); sweep.X = 0;
                fill.BeginAnimation(FrameworkElement.WidthProperty, null); fill.Width = 0;
            };
            slideIn.BeginAnimation(TranslateTransform.XProperty, slide);
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(OutMs)) { BeginTime = TimeSpan.FromMilliseconds(OutMs * 0.35), EasingFunction = easeIn });
        }

        private static void KeepOnTop()
        {
            if (toast == null) return;
            var h = new WindowInteropHelper(toast).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE NOMOVE NOACTIVATE */);
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
