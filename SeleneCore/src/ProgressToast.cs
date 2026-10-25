// Playnite's progress screen (add-ons downloading logos or trailers after a library update, a library update itself...)
// as a notification instead of a full screen: a glass card at the top right, under the clock, with the task's words.
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
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        private const double CardWidth = 500, CardHeight = 96, Edge = 28, ShadowRoom = 24, Drift = 26;   // one size for every card
        private const int MinShownMs = 2600, AfterDoneMs = 1100, InMs = 260, OutMs = 220;
        private static Window hiddenProgress;   // Playnite's own window, kept out of sight
        private static DateTime busyUntil = DateTime.MinValue;

        // a progress task is being shown as a card (or just was): focus moving around then is Playnite's own business
        public static bool Busy { get { return source != null || hiddenProgress != null || DateTime.Now < busyUntil || (toast != null && toast.IsVisible); } }

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

        // Playnite's progress window draws its first frame (an opaque dark screen) before it is Loaded: it is made invisible as
        // it activates, which comes first (the black flash before the card)
        public static void Prehide(Window w)
        {
            if (!Features.On("dialogs") || !Features.On("progresstoast")) return;
            var main = Application.Current.MainWindow;
            if (main == null || ReferenceEquals(main, w) || !main.IsVisible) return;
            w.BeginAnimation(UIElement.OpacityProperty, null);
            w.Opacity = 0;
            w.Background = Brushes.Transparent;
            HoldDim(main, true);
        }

        // Playnite's own dim layer (BorderDim in its main window) held hidden while a card shows: switching its flag off was not
        // enough, Playnite sends the "off" about 10 ms late from another thread and the dark layer flashed (the flicker)
        private static UIElement dimLayer;
        private static void HoldDim(Window main, bool hold)
        {
            try
            {
                if (dimLayer == null && main.Template != null) dimLayer = main.Template.FindName("BorderDim", main) as UIElement;
                if (dimLayer == null) return;
                if (hold) dimLayer.Visibility = Visibility.Collapsed;   // a local value outranks Playnite's trigger
                else dimLayer.ClearValue(UIElement.VisibilityProperty);
            }
            catch { }
        }

        // Playnite dims its whole main window while any child window is open (its ChildOpened flag shows an 80% dark layer, and
        // its sounds go quiet). The hidden progress window counts as one: while the card shows, the flag is put back at once,
        // unless a real dialog (a menu, a question) is open too.
        private static bool vmHooked;
        public static void WatchDimming(Window main)
        {
            if (vmHooked || main == null) return;
            var vm = main.DataContext as System.ComponentModel.INotifyPropertyChanged;
            var prop = vm != null ? vm.GetType().GetProperty("ChildOpened") : null;
            if (prop == null || !prop.CanWrite) return;
            vmHooked = true;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != "ChildOpened" || !Equals(prop.GetValue(vm, null), true)) return;
                if (!Features.On("progresstoast") || !OnlyProgressOpen(main)) return;
                prop.SetValue(vm, false, null);
            };
        }

        // the only child windows up are Playnite's progress windows (shown as the card)
        private static bool OnlyProgressOpen(Window main)
        {
            bool any = false;
            foreach (Window w in Application.Current.Windows)
            {
                if (ReferenceEquals(w, main) || ReferenceEquals(w, toast) || !w.IsVisible && w.GetType().Name != "ProgressWindow") continue;
                if (!(w.GetType().Namespace ?? "").StartsWith("Playnite")) continue;   // Selene's own windows don't count
                if (w.GetType().Name != "ProgressWindow") return false;
                any = true;
            }
            return any || Busy;
        }

        // false: leave it as Playnite's full screen (no main window to sit on yet, at start-up)
        public static bool Make(Window w)
        {
            var main = Application.Current.MainWindow;
            if (main == null || ReferenceEquals(main, w) || !main.IsVisible || main.WindowState == WindowState.Minimized || main.ActualWidth < 400) return false;

            // Playnite's own window: out of sight and out of the way (it closes by itself when the task ends). Emptied and hidden the
            // Windows way too, and kept so (Playnite fades / places it after this: a dark veil over the screen stayed otherwise)
            w.BeginAnimation(UIElement.OpacityProperty, null);
            w.Content = null;
            w.Background = Brushes.Transparent;
            hiddenProgress = w;
            w.WindowState = WindowState.Normal;
            w.SizeToContent = SizeToContent.Manual;
            w.Width = 1; w.Height = 1;
            w.Left = -32000; w.Top = -32000;
            w.Opacity = 0;
            w.ShowInTaskbar = false;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */);
            var mainHwnd = new WindowInteropHelper(main).Handle;
            // the main window first gets its input back and the focus, THEN Playnite's window goes: hidden while the main one
            // was still locked by it, Windows handed the focus to another program, and Selene's "a game covers Playnite" guard
            // blanked the home (the black screen, flickering as tasks came and went)
            busyUntil = DateTime.Now.AddSeconds(6);
            EnableWindow(mainHwnd, true);
            main.Activate();
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, 0 /* SW_HIDE */);
            // the modal window disabled the main one: it gets its input back, so browsing goes on while the task runs
            w.Dispatcher.BeginInvoke(new Action(() => { HideProgress(); EnableWindow(mainHwnd, true); main.Activate(); }), DispatcherPriority.Background);
            w.Closed += (s, e) => { if (ReferenceEquals(hiddenProgress, w)) hiddenProgress = null; TaskDone(w.DataContext); };
            WatchDimming(main);
            UndimNow(main);

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
                // owned by Playnite's window: above Playnite only, behind other programs after Alt+Tab, back with Playnite (it
                // stayed over everything as a topmost window)
                if (!toast.IsVisible && !ReferenceEquals(toast.Owner, main)) toast.Owner = main;
                if (!toast.IsVisible) toast.Show();
                // in, as a console does it: a quick fade with a short drift from the right
                slideIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Drift, 0, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
            // under the top bar (the clock and the profile), not over it: the bar is ~100 of the home's 1080 pixels high
            toast.Top = main.Top + main.ActualHeight * 112 / 1080.0 - ShadowRoom;
        }

        private static void Build()
        {
            words = new TextBlock
            {
                FontFamily = Ui.Body, FontSize = 17, Foreground = Ink.Brush("SeleneInkColor", 0.95), TextWrapping = TextWrapping.Wrap, LineHeight = 23,
                MaxWidth = CardWidth - 100, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 52
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
            // like a console's notification: an icon tile on the left, the task's words beside it (the bar is kept, unshown, so
            // the task's progress code has somewhere to go)
            var icon = new Border
            {
                Width = 48, Height = 48, CornerRadius = new CornerRadius(8), Background = Ink.Brush("GlyphColor", 0.18), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
                Child = new TextBlock { Text = Ui.Icon(0xE2C4 /* file_download */), FontFamily = Ui.Icons, FontSize = 28, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            words.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(words);
            slideIn = new TranslateTransform();
            card = new Border
            {
                Width = CardWidth, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 0, 22, 0), Height = CardHeight,
                Background = Ink.Brush("SeleneGlassColor", 0.9), BorderBrush = Ink.Brush("SeleneInkColor", 0.07), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 30, Opacity = 0.45, Color = Colors.Black },
                Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(ShadowRoom, ShadowRoom, Edge, ShadowRoom), RenderTransform = slideIn
            };
            var root = new Grid { Background = Brushes.Transparent, ClipToBounds = true, IsHitTestVisible = false };
            root.Children.Add(card);
            toast = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
                Topmost = false, ShowInTaskbar = false, ShowActivated = false, Focusable = false, SizeToContent = SizeToContent.Height,
                Title = "SeleneNotification", Content = root
            };
            toast.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(toast).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */ | 0x20 /* TRANSPARENT: clicks go through */);
            };
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            poll.Tick += (s, e) =>
            {
                HideProgress(); Update();
                // Playnite hidden (the tray) or minimized: the card goes with it, back if the task still runs when it returns
                var owner = Application.Current.MainWindow;
                if (owner != null && (!owner.IsVisible || owner.WindowState == WindowState.Minimized)) { if (toast.IsVisible) toast.Hide(); }
                else if (owner != null && source != null && !toast.IsVisible && !leaving) toast.Show();
                var mainNow = Application.Current.MainWindow;
                if (mainNow != null) { UndimNow(mainNow); HoldDim(mainNow, OnlyProgressOpen(mainNow)); }   // a real dialog: Playnite's dim back
                // never a blank home behind the card: if it was blanked anyway, it comes back
                var m = Application.Current.MainWindow;
                var r = m != null ? m.Content as UIElement : null;
                if (m != null && m.IsActive && r != null && r.Opacity < 0.01) { HomeNav.FadeInRoot(r); HomeNav.Log("progress look: home was blank, shown again"); }
            };
        }

        // words and bar from the task (polled: Playnite changes them on its worker thread)
        private static void Update()
        {
            try
            {
                if (source == null) return;
                var args = Prop(source, "ProgressArgs");
                var text = Prop(args, "Text") as string;
                if (text != null) text = text.Trim();   // Playnite's texts can start with spaces (they sat indented)
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
            var m0 = Application.Current.MainWindow;
            if (m0 != null) m0.Dispatcher.BeginInvoke(new Action(() => { if (!Busy || source == null) HoldDim(m0, false); }), DispatcherPriority.Background);
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
            var slide = new DoubleAnimation(Drift, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = easeIn };
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
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = easeIn });
        }

        // Playnite's dim layer off now (it was switched on as the progress window loaded, before Selene saw it)
        private static void UndimNow(Window main)
        {
            try
            {
                var vm = main.DataContext;
                var prop = vm != null ? vm.GetType().GetProperty("ChildOpened") : null;
                if (prop != null && prop.CanWrite && Equals(prop.GetValue(vm, null), true) && OnlyProgressOpen(main)) prop.SetValue(vm, false, null);
            }
            catch { }
        }

        // Playnite's progress window back out of sight if it came back (moved over the screen, faded in), the main window usable
        private static void HideProgress()
        {
            var w = hiddenProgress;
            if (w == null) return;
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero && IsWindowVisible(h)) ShowWindow(h, 0 /* SW_HIDE */);
                if (w.Opacity > 0) { w.BeginAnimation(UIElement.OpacityProperty, null); w.Opacity = 0; }
                var main = Application.Current.MainWindow;
                if (main != null) EnableWindow(new WindowInteropHelper(main).Handle, true);
            }
            catch { }
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
