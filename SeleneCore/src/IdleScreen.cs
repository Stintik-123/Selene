using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Selene
{
    // Left alone on Spotlight or Collection for a while (the Idle screen setting), Selene dims and drifts through the games'
    // art: one picture at a time, slowly zooming, crossfading to the next every few seconds, the clock small at the bottom
    // right. Any button wakes it (that press does nothing else). Never over the login, a menu, a running or starting game, or
    // when Playnite isn't in front.
    public static class IdleScreen
    {
        private static DateTime lastTouch = DateTime.Now;
        private static DispatcherTimer watch, turn;
        private static Grid layer;
        private static Image front, back;
        private static TextBlock clock;
        private static List<string> pictures = new List<string>();
        private static int at;
        private static readonly Random random = new Random();
        public static bool Showing { get; private set; }

        public static void Install()
        {
            if (watch != null) return;
            watch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
            watch.Tick += (s, e) => Check();
            watch.Start();
        }

        // a button went down: not idle (and the idle screen, if up, goes away)
        public static void Touched()
        {
            lastTouch = DateTime.Now;
            if (Showing) Hide();
        }

        public static void Restart() { lastTouch = DateTime.Now; if (Showing) Hide(); }

        private static void Check()
        {
            try
            {
                double minutes = Features.Number("idle");
                if (Showing || minutes <= 0 || (DateTime.Now - lastTouch).TotalMinutes < minutes) return;
                if (LoginScene.IsActive || HomeNav.Launching || HomeNav.GameRunning() || ProgressToast.Busy) return;
                var main = Application.Current.MainWindow;
                if (main == null || !main.IsActive || main.WindowState == WindowState.Minimized) return;
                var host = HomeNav.FindByName(main, "PART_MainHost") as Grid;
                if (host == null) return;
                Show(host);
            }
            catch (Exception ex) { HomeNav.Log("idle: " + ex.Message); }
        }

        private static void Show(Grid host)
        {
            var api = HomeNav.Api;
            pictures = api == null ? new List<string>() : api.Database.Games.Select(GameFacts.BackgroundPath).Where(p => p != null).Distinct().OrderBy(p => random.Next()).Take(40).ToList();
            if (pictures.Count == 0) { var w = Wallpapers.Current; if (!string.IsNullOrEmpty(w)) pictures.Add(w); }
            if (pictures.Count == 0) return;
            Showing = true;
            HomeNav.Log("idle: screen up (" + pictures.Count + " pictures)");
            layer = new Grid { Background = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
            Grid.SetRowSpan(layer, 99);
            Grid.SetColumnSpan(layer, 99);
            Panel.SetZIndex(layer, 9000);
            back = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            front = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            foreach (var img in new[] { back, front }) { RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality); img.CacheMode = new BitmapCache(1.0); layer.Children.Add(img); }
            layer.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)) });
            clock = new TextBlock { FontSize = 44, FontFamily = Ui.Display, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 72, 48) };
            layer.Children.Add(clock);
            host.Children.Add(layer);
            at = -1;
            Next();
            layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1600)) { EasingFunction = Ui.Out() });
            turn = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
            turn.Tick += (s, e) => Next();
            turn.Start();
        }

        // the next picture fades in over the last, each drifting a little larger as it shows
        private static void Next()
        {
            if (!Showing || pictures.Count == 0) return;
            clock.Text = DateTime.Now.ToString("h:mm tt");
            at = (at + 1) % pictures.Count;
            var pic = Wallpapers.Load(pictures[at], 1920);
            if (pic == null) { if (pictures.Count > 1) { pictures.RemoveAt(at); at--; Next(); } return; }
            var old = front; front = back; back = old;
            Panel.SetZIndex(front, 2); Panel.SetZIndex(back, 1);
            front.Source = pic;
            var zoom = new ScaleTransform(1.0, 1.0);
            front.RenderTransform = zoom;
            var slow = TimeSpan.FromSeconds(16);
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 1.08, slow));
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 1.08, slow));
            front.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1800)));
            back.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1800)));
        }

        private static void Hide()
        {
            Showing = false;
            if (turn != null) { turn.Stop(); turn = null; }
            var l = layer;
            layer = null;
            if (l == null) return;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)) { EasingFunction = Ui.Out() };
            fade.Completed += (s, e) => { var p = l.Parent as Panel; if (p != null) p.Children.Remove(l); };
            l.BeginAnimation(UIElement.OpacityProperty, fade);
            HomeNav.Log("idle: woke");
        }
    }
}
