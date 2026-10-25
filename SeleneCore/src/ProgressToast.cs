// Playnite's progress screen (add-ons downloading logos or trailers after a library update, a library update itself...)
// as a notification instead of a full screen: a glass card at the top right, under the clock, with the task's words.
// Playnite opens that screen as a modal window and waits for it to close before carrying on, so it isn't held open: it is
// moved off screen (invisible, the main window handed its input back) and the card is a window of Selene's own that mirrors
// it. The card slides in from beyond the right edge with a sound, stays at least a few seconds so it can be read (the bar
// runs to the end when the task is done), and slides back out. Tasks that come one after another share the card.
using System;
using System.Linq;
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
        public static bool Busy { get { return source != null || hiddenProgress != null || DateTime.Now < busyUntil || (layer != null && layer.Visibility == Visibility.Visible); } }

        // the card (one at a time)
        // the card's layer inside Playnite's own window (a window of its own flashed a black box as it appeared, and overlapping
        // the full-screen window made Windows present Playnite's frames the slow way: everything stuttered while it showed)
        private static Grid layer;
        private static Border icon;
        private static TextBlock downloadGlyph;
        private static ContentControl titleLine;
        private static StackPanel text;
        private const double SessionWidth = 400, SessionHeight = 182;   // the session card: narrower and taller, the logo big and centred

        // a progress card's shape (wide, one row: icon tile and words) or the session card's (the logo over the words, centred)
        private static void Shape(bool session)
        {
            card.Width = session ? SessionWidth : CardWidth;
            card.Height = session ? SessionHeight : CardHeight;
            card.Padding = session ? new Thickness(24, 16, 24, 18) : new Thickness(16, 0, 22, 0);
            layer.Width = card.Width + ShadowRoom + Edge;
            var centre = session ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            text.HorizontalAlignment = centre;
            titleLine.HorizontalAlignment = centre;
            words.HorizontalAlignment = centre;
            words.TextAlignment = session ? TextAlignment.Center : TextAlignment.Left;
            words.MaxWidth = session ? SessionWidth - 48 : CardWidth - 100;
            titleLine.Margin = session ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 0, 4);
        }
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
                if (ReferenceEquals(w, main) || !w.IsVisible && w.GetType().Name != "ProgressWindow") continue;
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
            if (layer == null) Build();
            icon.Background = Ink.Brush("GlyphColor", 0.18);
            icon.Child = downloadGlyph;
            icon.Visibility = Visibility.Visible;
            titleLine.Visibility = Visibility.Collapsed;
            Shape(false);
            if (leaveTimer != null) leaveTimer.Stop();
            barShown = -1;
            Place(main);
            if (layer.Visibility != Visibility.Visible || leaving)
            {
                leaving = false;
                shownAt = DateTime.Now;
                layer.Visibility = Visibility.Visible;
                // in, as a console does it: a quick fade with a short drift from the right
                slideIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Drift, 0, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                Ui.NotifySound();
            }
            Update();
            poll.Start();
        }

        // the top layer of Playnite's own window, at the top right under the bar (the clock and the profile), not over it
        private static void Place(Window main)
        {
            var host = HomeNav.FindByName(main, "PART_MainHost") as Grid;
            if (host == null || ReferenceEquals(layer.Parent, host)) return;
            var old = layer.Parent as Panel;
            if (old != null) old.Children.Remove(layer);
            Grid.SetRowSpan(layer, 99);
            Grid.SetColumnSpan(layer, 99);
            Panel.SetZIndex(layer, 9998);
            host.Children.Add(layer);
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
            downloadGlyph = new TextBlock { Text = Ui.Icon(0xE2C4 /* file_download */), FontFamily = Ui.Icons, FontSize = 28, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icon = new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(8), Background = Ink.Brush("GlyphColor", 0.18), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0), Child = downloadGlyph, ClipToBounds = true
            };
            titleLine = new ContentControl { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(titleLine);
            text.Children.Add(words);
            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(text);
            slideIn = new TranslateTransform();
            card = new Border
            {
                Width = CardWidth, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 0, 22, 0), Height = CardHeight,
                Background = Ink.Brush("SeleneGlassColor", 0.72), BorderBrush = Ink.Brush("SeleneInkColor", 0.08), BorderThickness = new Thickness(1),   // see-through
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 30, Opacity = 0.45, Color = Colors.Black },
                Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(ShadowRoom, ShadowRoom, Edge, ShadowRoom), RenderTransform = slideIn
            };
            layer = new Grid
            {
                IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Width = CardWidth + ShadowRoom + Edge, Margin = new Thickness(0, 112 - ShadowRoom, 0, 0)
            };
            layer.Children.Add(card);
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            poll.Tick += (s, e) =>
            {
                HideProgress(); Update();
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
            if (!ReferenceEquals(vm, source) || layer == null) return;
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
            if (source != null || layer == null) return;
            leaving = true;
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(Drift, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = easeIn };
            slide.Completed += (s, e) =>
            {
                if (!leaving) return;   // a new task brought it back meanwhile
                leaving = false;
                layer.Visibility = Visibility.Collapsed;
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

        // ---- a game just closed: its cover, its logo (or name) and how long it was played, like a console's "you played" card ----
        public static void ShowSession(Playnite.SDK.Models.Game game, ulong seconds) { ShowSession(game, seconds, false); }

        private static void ShowSession(Playnite.SDK.Models.Game game, ulong seconds, bool fetched)
        {
            var main = Application.Current.MainWindow;
            if (main == null || game == null) return;
            // a Steam game without its logo or cover here: Steam's are fetched first (kept for next time), then the card shows
            var appId = SteamAppId(game);
            if (!fetched && appId != null && (!System.IO.File.Exists(SteamFile(appId, "logo.png")) || !System.IO.File.Exists(SteamFile(appId, "library_600x900.jpg"))))
            {
                var ui = main.Dispatcher;
                System.Threading.Tasks.Task.Factory.StartNew(() =>
                {
                    Fetch(appId, "logo.png");
                    Fetch(appId, "library_600x900.jpg");
                    ui.BeginInvoke(new Action(() => ShowSession(game, seconds, true)));
                });
                return;
            }
            // a progress card up now: this one waits its turn
            if (source != null) { Later(1500, () => ShowSession(game, seconds)); return; }
            if (layer == null) Build();
            Place(main);
            var api = HomeNav.Api;
            // the cover in the icon tile
            ImageSource cover = null;
            try
            {
                var file = !string.IsNullOrEmpty(game.CoverImage) ? api.Database.GetFullFilePath(game.CoverImage) : !string.IsNullOrEmpty(game.Icon) ? api.Database.GetFullFilePath(game.Icon) : null;
                if (file != null && System.IO.File.Exists(file)) cover = Wallpapers.Load(file, 160);
                if (cover == null && appId != null && System.IO.File.Exists(SteamFile(appId, "library_600x900.jpg"))) cover = Wallpapers.Load(SteamFile(appId, "library_600x900.jpg"), 160);
            }
            catch { }
            // just the logo and the line (no cover tile beside them); the tile only stands in when there's no logo at all
            icon.Child = null;
            icon.Background = cover != null ? (Brush)new ImageBrush(cover) { Stretch = Stretch.UniformToFill } : Ink.Brush("GlyphColor", 0.18);
            if (cover == null) icon.Child = new TextBlock { Text = Ui.Icon(0xE338 /* videogame_asset */), FontFamily = Ui.Icons, FontSize = 30, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icon.Visibility = Visibility.Visible;
            // its logo (Extra Metadata's), else its name
            FrameworkElement title = null;
            try
            {
                var logo = System.IO.Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", game.Id.ToString(), "logo.png");
                if (!System.IO.File.Exists(logo) && appId != null) logo = SteamFile(appId, "logo.png");
                var pic = System.IO.File.Exists(logo) ? Wallpapers.Load(logo, 400) : null;
                if (pic != null) title = new Image { Source = pic, MaxHeight = 92, MaxWidth = SessionWidth - 80, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            }
            catch { }
            if (title == null) title = new TextBlock { Text = game.Name, FontFamily = Ui.Display, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = CardWidth - 110 };
            titleLine.Content = title;
            titleLine.Visibility = Visibility.Visible;
            if (title is Image) icon.Visibility = Visibility.Collapsed;
            Shape(title is Image);   // logo: the big centred card; no logo: the usual row with the cover and the name
            var line = "You played " + Duration(seconds);
            if (game.Playtime >= 3600) line += "  \u00B7  " + Duration(game.Playtime) + " in total";
            words.Text = line;
            // in, held for a few seconds, out (no task behind it)
            if (leaveTimer != null) leaveTimer.Stop();
            leaving = false;
            shownAt = DateTime.Now;
            layer.Visibility = Visibility.Visible;
            slideIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Drift, 0, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Ui.NotifySound();
            leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5500) };
            leaveTimer.Tick += (s, e) => { leaveTimer.Stop(); Leave(); };
            leaveTimer.Start();
            HomeNav.Log("session card: " + game.Name + ", " + seconds + " s");
        }

        // the game's Steam app id: a Steam game's own, or the stand-in's (Expedition 33); null for anything else
        private const string SteamPlugin = "cb91dfc9-b977-43bf-8e70-55f46e410fab";
        private static string SteamAppId(Playnite.SDK.Models.Game game)
        {
            if (game.PluginId.ToString().Equals(SteamPlugin, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.GameId)) return game.GameId;
            if (game.Id == Guid.Empty || game.Name == "Clair Obscur: Expedition 33") return game.PluginId == Guid.Empty ? (game.GameId ?? "1903340") : null;
            return null;
        }

        private static string SteamFile(string appId, string name) { return System.IO.Path.Combine(SelenePaths.Media("Steam"), appId + "-" + name); }

        private static void Fetch(string appId, string name)
        {
            var file = SteamFile(appId, name);
            if (System.IO.File.Exists(file)) return;
            try
            {
                try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { }
                using (var web = new System.Net.WebClient()) web.DownloadFile("https://cdn.akamai.steamstatic.com/steam/apps/" + appId + "/" + name, file + ".part");
                if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
                System.IO.File.Move(file + ".part", file);
            }
            catch (Exception ex) { HomeNav.Log("session card: no " + name + " for " + appId + " (" + ex.Message + ")"); try { System.IO.File.Delete(file + ".part"); } catch { } }
        }

        private static string Duration(ulong seconds)
        {
            if (seconds < 60) return "less than a minute";
            ulong h = seconds / 3600, m = seconds % 3600 / 60;
            if (h == 0) return m + (m == 1 ? " minute" : " minutes");
            return h + " h" + (m > 0 ? " " + m + " min" : "");
        }

        private static void Later(int ms, Action act)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); act(); };
            t.Start();
        }

        // dev: %TEMP%\selene-dev-session makes a pretend session card a few seconds after start (a game from the library, else a
        // stand-in), then the file goes
        public static void DevSessionLater()
        {
            var flag = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-dev-session");
            if (!System.IO.File.Exists(flag)) return;
            try { System.IO.File.Delete(flag); } catch { }
            Later(6000, () =>
            {
                var api = HomeNav.Api;
                Playnite.SDK.Models.Game game = null;
                try { game = api != null ? api.Database.Games.OrderByDescending(g => g.Playtime).FirstOrDefault() : null; } catch { }
                if (game == null) game = new Playnite.SDK.Models.Game("Clair Obscur: Expedition 33") { Playtime = 45 * 3600 + 20 * 60, GameId = "1903340" };
                HomeNav.Log("dev: session card preview");
                ShowSession(game, 5040);   // 1 h 24 min
            });
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
