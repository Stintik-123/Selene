// console-style login, built in code on top of the old theme's login (inserted as the bottom layer of LoginCenterShell, so the old theme's
// clock stays and its own background is covered):
//   scene : dark-blue blurry glow intro (first start only) -> drifting gold particles, soft spotlight from the top left,
//           hazy floor glow
//   stage 0 "Press the A button on your controller." with a breathing ring around an A button
//   stage 1 "Welcome back, Matthew / Who's playing?" with "+ Add User" and the user's avatar (gold ring); A on the
//           avatar lets the press through to the old theme (which unlocks and plays LoginConfirm.wav), B goes back.
// Sounds (the theme's Audio folder): LoginPress.wav on "Press A", LoginSelect.wav when the profile is picked.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Selene
{
    public static class LoginScene
    {
        private static Grid scene, pressLayer, whoLayer;
        private static FrameworkElement loginRoot;
        private static int stage;            // 0 press, 1 who's playing
        private static int whoIndex = 1;     // 0 add user, 1 avatar
        private static Border addTile, avatarTile;
        private static Ellipse avatarRing, addRing;
        private static bool bootSoundPlayed, keysHooked;
        private static MediaPlayer bootPlayer, blipPlayer;
        private static readonly Random rnd = new Random(7);

        public static void Attach(Window w, FrameworkElement login)
        {
            try
            {
                if (w == null || login == null) return;
                loginRoot = login;
                // keyboard works too (Enter = A, Esc = B, arrows): not everyone has a controller plugged in
                if (!keysHooked)
                {
                    keysHooked = true;
                    w.PreviewKeyDown += (s, e) =>
                    {
                        if (!IsActive) return;
                        string b = e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space ? "A"
                                 : e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Back ? "B"
                                 : e.Key == System.Windows.Input.Key.Left ? "DPadLeft" : e.Key == System.Windows.Input.Key.Right ? "DPadRight"
                                 : e.Key == System.Windows.Input.Key.Up ? "DPadUp" : e.Key == System.Windows.Input.Key.Down ? "DPadDown" : null;
                        if (b != null) { HandleButton(b); e.Handled = true; }
                    };
                }
                // developer switch: %TEMP%\selene-skip-login skips the login (to look at the home while building Selene)
                if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-skip-login")))
                {
                    login.Visibility = Visibility.Collapsed;
                    HomeNav.Log("login: skipped (developer switch)");
                    return;
                }
                var shell = HomeNav.FindByName(w, "LoginCenterShell") as Grid;
                if (shell == null) { HomeNav.Log("login: LoginCenterShell not found"); return; }
                if (scene == null)
                {
                    scene = BuildScene(w, shell);
                    Panel.SetZIndex(scene, -100);
                    shell.Children.Insert(0, scene);
                    // the old theme's / the older Solaris-style login layout is replaced by ours
                    foreach (var n in new[] { "SeleneSolarisLogin", "LoginHeaderBlock", "LoginProfileBlock" })
                    {
                        var el = HomeNav.FindByName(w, n) as UIElement;
                        if (el != null) el.Visibility = Visibility.Collapsed;
                    }
                    HomeNav.Log("login: scene attached");
                }
                login.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) ResetToPress(); };
                if (login.IsVisible) { ResetToPress(); PlayBoot(); }
            }
            catch (Exception ex) { HomeNav.Log("login attach failed: " + ex.Message); }
        }

        public static bool IsActive { get { return loginRoot != null && loginRoot.IsVisible && scene != null; } }

        // true = swallow the button (handled here); false = let it through (the final A unlocks the old theme's login)
        public static bool HandleButton(string pressed)
        {
            if (!IsActive) return false;
            if (stage == 0)
            {
                if (pressed == "A" || pressed == "Start") { ShowWho(); return true; }
                return true;  // nothing else on the press screen
            }
            bool up = pressed == "DPadUp" || pressed == "LeftStickUp", down = pressed == "DPadDown" || pressed == "LeftStickDown";
            if (menuOpen)
            {
                if (pressed == "B") { ClosePowerMenu(); return true; }
                if (up && menuIndex > 0) { menuIndex--; ShowMenuSelection(); Blip(); }
                else if (down && menuIndex < PowerItems.Length - 1) { menuIndex++; ShowMenuSelection(); Blip(); }
                else if (pressed == "A") RunPower(menuIndex);
                return true;
            }
            if (pressed == "B") { ResetToPress(); Blip(); return true; }
            if (down && whoIndex != 2) { Select(2); return true; }
            if (up && whoIndex == 2) { Select(1); return true; }
            if (whoIndex != 2 && (pressed == "DPadRight" || pressed == "LeftStickRight")) { Select(1); return true; }
            if (pressed == "A")
            {
                if (whoIndex == 2) { OpenPowerMenu(); return true; }
                if (whoIndex == 0) { Shake(addTile); return true; }  // adding users isn't a thing here
                FadeOut(whoLayer, 260);
                PlaySound(SelenePaths.ThemeAudio("LoginSelect.wav"), 0.9);  // profile picked: the theme's own sign-in sound
                stage = 2;
                SeleneShell.HideLogin();  // the login layer is Selene's own now
                return true;
            }
            return true;
        }

        // ---------------- scene ----------------
        private static Grid BuildScene(Window w, Grid shell)
        {
            var g = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x2A, 0x5C), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0x17, 0x3A), 0.6));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x03, 0x08, 0x18), 1));
            g.Children.Add(new Border { Background = bg });

            // soft spotlight from the top left, slowly breathing
            var spot = new RadialGradientBrush { Center = new Point(0.16, -0.12), GradientOrigin = new Point(0.16, -0.12), RadiusX = 0.62, RadiusY = 1.15 };
            spot.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0xB8, 0xD8, 0xFF), 0));
            spot.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0x7F, 0xB2, 0xFF), 0.45));
            spot.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x7F, 0xB2, 0xFF), 1));
            var spotLayer = new Border { Background = spot };
            spotLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromSeconds(9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            g.Children.Add(spotLayer);

            // hazy floor glow
            var haze = new RadialGradientBrush { Center = new Point(0.5, 1.02), GradientOrigin = new Point(0.5, 1.02), RadiusX = 0.95, RadiusY = 0.34 };
            haze.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0x4A, 0x8C, 0xE8), 0));
            haze.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x4A, 0x8C, 0xE8), 1));
            g.Children.Add(new Border { Background = haze });

            // drifting particles (gold + a few white), and a handful of big soft bokeh
            var canvas = new Canvas();
            g.Children.Add(canvas);
            g.SizeChanged += (s, e) => { if (canvas.Children.Count == 0 && e.NewSize.Width > 0) FillParticles(canvas, e.NewSize); };

            pressLayer = BuildPress();
            g.Children.Add(pressLayer);
            whoLayer = BuildWho(w);
            g.Children.Add(whoLayer);

            // first start: dark blue blurry glow fading into the scene (console boot)
            if (!bootSoundPlayed)
            {
                var intro = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x05, 0x0D, 0x15)), IsHitTestVisible = false };
                foreach (var b in new[] { new double[] { 0.30, 0.45, 0.55 }, new double[] { 0.62, 0.40, 0.70 }, new double[] { 0.80, 0.70, 0.45 }, new double[] { 0.15, 0.80, 0.40 } })
                {
                    var br = new RadialGradientBrush { Center = new Point(b[0], b[1]), GradientOrigin = new Point(b[0], b[1]), RadiusX = b[2] * 0.6, RadiusY = b[2] };
                    br.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, 0x1E, 0x4A, 0x68), 0));
                    br.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x1E, 0x4A, 0x68), 1));
                    intro.Children.Add(new Border { Background = br });
                }
                g.Children.Add(intro);
                intro.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(2400)) { BeginTime = TimeSpan.FromMilliseconds(900), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            }
            return g;
        }

        private static void FillParticles(Canvas canvas, Size size)
        {
            for (int i = 0; i < 90; i++)
            {
                bool bokeh = i < 12;
                double d = bokeh ? 14 + rnd.NextDouble() * 34 : 2 + rnd.NextDouble() * 5;
                bool gold = rnd.NextDouble() < 0.72;
                var c = gold ? Color.FromRgb(0x9C, 0xCB, 0xFF) : Color.FromRgb(0xFF, 0xFF, 0xFF);  // light blue + white
                var fill = new RadialGradientBrush();
                fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(bokeh ? 0x55 : 0xF0), c.R, c.G, c.B), 0));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
                var dot = new Ellipse { Width = d, Height = d, Fill = fill };
                // mostly in the lower half and along the spotlight, like a console
                double x = rnd.NextDouble() * size.Width;
                double y = size.Height * (0.25 + rnd.NextDouble() * 0.75);
                Canvas.SetLeft(dot, x); Canvas.SetTop(dot, y);
                var tr = new TranslateTransform();
                dot.RenderTransform = tr;
                double dur = 16 + rnd.NextDouble() * 18;
                var begin = TimeSpan.FromSeconds(-rnd.NextDouble() * dur);
                tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -size.Height * (0.10 + rnd.NextDouble() * 0.18), TimeSpan.FromSeconds(dur)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
                tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 14 + rnd.NextDouble() * 20, TimeSpan.FromSeconds(5 + rnd.NextDouble() * 6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                double peak = bokeh ? 0.25 + rnd.NextDouble() * 0.3 : 0.4 + rnd.NextDouble() * 0.6;
                var op = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(dur), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
                op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                op.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromPercent(0.18)));
                op.KeyFrames.Add(new LinearDoubleKeyFrame(peak * 0.7, KeyTime.FromPercent(0.6)));
                op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                dot.BeginAnimation(UIElement.OpacityProperty, op);
                canvas.Children.Add(dot);
            }
        }

        // ---------------- stage 0: press A ----------------
        private static Grid BuildPress()
        {
            var g = new Grid();
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            stack.Children.Add(new TextBlock { Text = "Press the A button on your controller.", FontSize = 26, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center });
            var ringHost = new Grid { Width = 220, Height = 220, Margin = new Thickness(0, 46, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 2; i++)
            {
                var ring = new Ellipse { Width = 74, Height = 74, Stroke = Brushes.White, StrokeThickness = 2, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
                var sc = new ScaleTransform(1, 1);
                ring.RenderTransform = sc;
                var begin = TimeSpan.FromMilliseconds(i * 1100);
                var dur = TimeSpan.FromMilliseconds(2200);
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 2.4, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 2.4, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.85, 0, dur) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever });
                ringHost.Children.Add(ring);
            }
            var button = new Grid { Width = 74, Height = 74 };
            button.Children.Add(new Ellipse { Fill = Brushes.White, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 24, Color = Colors.White, Opacity = 0.45 } });
            button.Children.Add(new TextBlock { Text = "A", FontSize = 36, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x20)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) });
            ringHost.Children.Add(button);
            stack.Children.Add(ringHost);
            g.Children.Add(stack);
            return g;
        }

        // ---------------- stage 1: who's playing ----------------
        private static Grid BuildWho(Window w)
        {
            var g = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -30, 0, 0) };
            col.Children.Add(new TextBlock { Text = "Welcome back", FontSize = 40, FontFamily = new FontFamily("Segoe UI Variable Display"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            col.Children.Add(new TextBlock { Text = "Who's using this controller?", FontSize = 22, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) };
            // + Add User
            var addCol = new StackPanel { Margin = new Thickness(0, 0, 70, 0) };
            var addGrid = new Grid { Width = 170, Height = 170 };
            addTile = new Border { CornerRadius = new CornerRadius(85), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            addTile.Child = new TextBlock { Text = "+", FontSize = 70, FontWeight = FontWeights.Light, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -8, 0, 0) };
            addRing = new Ellipse { Stroke = Brushes.White, StrokeThickness = 5, Margin = new Thickness(-8), Opacity = 0 };
            addGrid.Children.Add(addTile); addGrid.Children.Add(addRing);
            addCol.Children.Add(addGrid);
            addCol.Children.Add(new TextBlock { Text = "Add User", FontSize = 22, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) });
            // (no "Add User": Playnite has a single profile - a button that did nothing; user chose to remove it)

            // the user's avatar (the old theme's animated-avatar template, same picture as its login)
            var avCol = new StackPanel { Margin = new Thickness(0, -46, 0, 0) };
            var badge = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
            badge.Children.Add(new TextBlock { Text = "1", FontSize = 13, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center });
            badge.Children.Add(new TextBlock { Text = "\uE7FC", FontSize = 22, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center });
            avCol.Children.Add(badge);
            var avGrid = new Grid { Width = 210, Height = 210 };
            avatarTile = new Border { CornerRadius = new CornerRadius(105), ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1.08, 1.08) };
            try
            {
                // the user's picture: Selene Core's setting, else the Steam account's avatar
                var pic = SelenePaths.AvatarImage;
                if (pic != null && System.IO.File.Exists(pic))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 420; bmp.UriSource = new Uri(pic); bmp.EndInit(); bmp.Freeze();
                    avatarTile.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill, Width = 210, Height = 210, Clip = new EllipseGeometry(new Point(105, 105), 105, 105) };
                }
            }
            catch (Exception ex) { HomeNav.Log("login avatar: " + ex.Message); }
            avatarRing = new Ellipse { Stroke = Brushes.White, StrokeThickness = 5, Margin = new Thickness(-10), Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 22, Color = Color.FromRgb(0x6F, 0xB4, 0xFF), Opacity = 0.8 } };
            avGrid.Children.Add(avatarTile); avGrid.Children.Add(avatarRing);
            avCol.Children.Add(avGrid);
            var userName = SelenePaths.UserName;
            avCol.Children.Add(new TextBlock { Text = userName, FontSize = 26, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) });
            row.Children.Add(avCol);
            col.Children.Add(row);
            g.Children.Add(col);

            // bottom centre: power button (Down from the profiles), A opens Sleep / Restart / Shut down
            var powerGrid = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 56) };
            powerTile = new Border { CornerRadius = new CornerRadius(32), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            powerTile.Child = new TextBlock { Text = "\uE7E8", FontSize = 24, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            powerRing = new Ellipse { Stroke = Brushes.White, StrokeThickness = 3, Margin = new Thickness(-6), Opacity = 0 };
            powerGrid.Children.Add(powerTile); powerGrid.Children.Add(powerRing);
            g.Children.Add(powerGrid);

            powerMenu = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x22, 0x44)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 140), Padding = new Thickness(8), Width = 320, Visibility = Visibility.Collapsed, Opacity = 0 };
            var menuStack = new StackPanel();
            menuRows.Clear();
            foreach (var item in PowerItems)
            {
                var rowB = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 12, 18, 12) };
                var rowS = new StackPanel { Orientation = Orientation.Horizontal };
                rowS.Children.Add(new TextBlock { Text = item[0], FontSize = 20, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Width = 36 });
                rowS.Children.Add(new TextBlock { Text = item[1], FontSize = 22, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
                rowB.Child = rowS;
                menuRows.Add(rowB);
                menuStack.Children.Add(rowB);
            }
            powerMenu.Child = menuStack;
            g.Children.Add(powerMenu);

            // bottom right: (A) Select
            var hint = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 50) };
            var aDot = new Grid { Width = 34, Height = 34 };
            aDot.Children.Add(new Ellipse { Fill = Brushes.White });
            aDot.Children.Add(new TextBlock { Text = "A", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x20)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            hint.Children.Add(aDot);
            hint.Children.Add(new TextBlock { Text = "Select", FontSize = 22, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            g.Children.Add(hint);
            return g;
        }

        // ---------------- transitions ----------------
        private static Border powerTile, powerMenu;
        private static Ellipse powerRing;
        private static readonly List<Border> menuRows = new List<Border>();
        private static bool menuOpen;
        private static int menuIndex;
        // glyph (Segoe MDL2 Assets), label, ConsoleHelper command
        private static readonly string[][] PowerItems = {
            new[] { "\uE708", "Sleep", "power-sleep" },
            new[] { "\uE777", "Restart", "power-restart" },
            new[] { "\uE7E8", "Shut Down", "power-off" } };

        private static string ConsoleName()
        {
            var n = (Environment.MachineName ?? "").ToLowerInvariant();
            return n.Length == 0 ? "Console" : char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        private static void OpenPowerMenu()
        {
            menuOpen = true; menuIndex = 0;
            ShowMenuSelection();
            powerMenu.Visibility = Visibility.Visible;
            FadeIn(powerMenu, 200, 0);
            var rise = new TranslateTransform(0, 14);
            powerMenu.RenderTransform = rise;
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            PlaySound(ThemeAudio("OpenPanel.wav"), 0.8);
        }

        private static void ClosePowerMenu()
        {
            if (!menuOpen) return;
            menuOpen = false;
            FadeOut(powerMenu, 160);
            PlaySound(ThemeAudio("CloseAdditionalView.wav"), 0.8);
        }

        private static void ShowMenuSelection()
        {
            for (int i = 0; i < menuRows.Count; i++)
                menuRows[i].Background = i == menuIndex ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        }

        private static void RunPower(int i)
        {
            var cmd = PowerItems[i][2];
            HomeNav.Log("login: power menu -> " + PowerItems[i][1]);
            ClosePowerMenu();
            try
            {
                // the helper also turns the TV off first (like rest mode)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SelenePaths.Helper, cmd) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) { HomeNav.Log("login power failed: " + ex.Message); }
        }

        private static void ResetToPress()
        {
            stage = 0; whoIndex = 1;
            menuOpen = false;
            if (powerMenu != null) { powerMenu.BeginAnimation(UIElement.OpacityProperty, null); powerMenu.Opacity = 0; powerMenu.Visibility = Visibility.Collapsed; }
            if (whoLayer != null) { whoLayer.BeginAnimation(UIElement.OpacityProperty, null); whoLayer.Opacity = 0; whoLayer.Visibility = Visibility.Collapsed; }
            if (pressLayer != null) { pressLayer.Visibility = Visibility.Visible; FadeIn(pressLayer, 450, 0); }
        }

        private static void ShowWho()
        {
            stage = 1;
            PlaySound(SelenePaths.ThemeAudio("LoginPress.wav"), 0.9);  // Press A: the old theme's original login sound (user's choice)
            FadeOut(pressLayer, 300);
            whoLayer.Visibility = Visibility.Visible;
            Select(1);
            FadeIn(whoLayer, 520, 220);
            var rise = new TranslateTransform(0, 24);
            whoLayer.RenderTransform = rise;
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(220), EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
        }

        private static void Select(int i)
        {
            if (avatarTile == null) return;
            if (i != whoIndex) Blip();
            whoIndex = i;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Action<Border, Ellipse, bool> look = (tile, ring, on) =>
            {
                var sc = tile.RenderTransform as ScaleTransform;
                if (sc != null)
                {
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1.08 : 0.94, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.08 : 0.94, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                }
                ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(200)));
            };
            look(addTile, addRing, i == 0);
            look(avatarTile, avatarRing, i == 1);
            if (powerTile != null) look(powerTile, powerRing, i == 2);
        }

        private static void Shake(FrameworkElement el)
        {
            var tr = new TranslateTransform();
            var group = new TransformGroup();
            if (el.RenderTransform != null && el.RenderTransform != Transform.Identity) group.Children.Add(el.RenderTransform);
            group.Children.Add(tr);
            el.RenderTransform = group;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
            foreach (var kv in new[] { new[] { 0.0, 0 }, new[] { 0.15, -10 }, new[] { 0.35, 9 }, new[] { 0.55, -6 }, new[] { 0.75, 4 }, new[] { 1.0, 0 } })
                a.KeyFrames.Add(new LinearDoubleKeyFrame(kv[1], KeyTime.FromPercent(kv[0])));
            tr.BeginAnimation(TranslateTransform.XProperty, a);
        }

        private static void FadeIn(UIElement el, int ms, int delay)
        {
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private static void FadeOut(UIElement el, int ms)
        {
            if (el == null) return;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (s, e) => { if (el.Opacity < 0.01) el.Visibility = Visibility.Collapsed; };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        // ---------------- sounds ----------------
        private static void PlayBoot()
        {
            if (bootSoundPlayed) return;
            bootSoundPlayed = true;
            string path = SelenePaths.ThemeAudio("Boot.wav");
            if (!System.IO.File.Exists(path)) return;
            try { bootPlayer = new MediaPlayer(); bootPlayer.Open(new Uri(path)); bootPlayer.Volume = 0.9; bootPlayer.Play(); } catch { }
        }

        private static readonly List<MediaPlayer> playing = new List<MediaPlayer>();

        private static void PlaySound(string path, double volume)
        {
            if (!System.IO.File.Exists(path)) return;
            try
            {
                var mp = new MediaPlayer();
                playing.Add(mp);  // kept alive until it finishes
                mp.MediaEnded += (s, e) => { mp.Close(); playing.Remove(mp); };
                mp.Open(new Uri(path)); mp.Volume = volume; mp.Play();
            }
            catch { }
        }

        private static string ThemeAudio(string file)
        {
            return SelenePaths.ThemeAudio(file);
        }

        private static void Blip()
        {
            var path = ThemeAudio("navigation.wav");
            if (!System.IO.File.Exists(path)) return;
            try
            {
                if (blipPlayer == null) { blipPlayer = new MediaPlayer(); blipPlayer.Open(new Uri(path)); }
                blipPlayer.Position = TimeSpan.Zero; blipPlayer.Volume = 0.7; blipPlayer.Play();
            }
            catch { }
        }
    }
}
