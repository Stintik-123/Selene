// The login layer (AcceuilSettings in Main.xaml), built in code into LoginCenterShell:
//   backdrop : the profile's wallpaper over the theme's gradient, a breathing accent spotlight, drifting particles
//   first start of Playnite: logo intro (crescent crown rises in, its full moon glows, chime) ->
//   no profiles yet: setup check -> profile steps (name, avatar, theme, background) -> Who's playing?
//   otherwise: Who's playing? (A signs in, Add runs the profile steps, Y edits, X removes; power menu below)
// The menu (Extensions) can bring it back for switching profile, editing the profile or the setup check.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Selene
{
    public static class LoginScene
    {
        private static Window window;
        private static FrameworkElement loginRoot;
        private static Grid scene, stage;
        private static Screen current;
        private static bool keysHooked, introPlayed;
        // what to show the next time the layer opens (set by the menu), instead of Who's playing?
        private static Func<Screen> pending;
        private static readonly Random rnd = new Random(7);

        public static void Attach(Window w, FrameworkElement login)
        {
            try
            {
                if (w == null || login == null) return;
                window = w;
                loginRoot = login;
                // keyboard works too (Enter = A, Esc = B, arrows, typing on the name step): not everyone has a controller
                if (!keysHooked)
                {
                    keysHooked = true;
                    w.PreviewKeyDown += OnKey;
                    w.PreviewTextInput += OnText;
                }
                var shell = HomeNav.FindByName(w, "LoginCenterShell") as Grid;
                if (shell == null) { HomeNav.Log("login: LoginCenterShell not found"); return; }
                if (scene == null)
                {
                    scene = BuildScene();
                    Panel.SetZIndex(scene, -100);
                    shell.Children.Insert(0, scene);
                    HomeNav.Log("login: scene attached");
                }
                login.IsVisibleChanged += (s, e) =>
                {
                    if ((bool)e.NewValue) Open();
                    else if (current != null) { current.Hidden(); stage.Children.Clear(); current = null; }
                };
                // developer switch: %TEMP%\selene-skip-login skips the login (to look at the home while building Selene)
                if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-skip-login")))
                {
                    login.Visibility = Visibility.Collapsed;
                    HomeNav.Log("login: skipped (developer switch)");
                    return;
                }
                if (login.IsVisible) Open();
                DevKeys();
            }
            catch (Exception ex) { HomeNav.Log("login attach failed: " + ex); }
        }

        public static bool IsActive { get { return loginRoot != null && loginRoot.IsVisible && scene != null && current != null; } }

        // controller buttons (from HomeNav's input hook): everything goes to the screen while the login layer is up
        public static bool HandleButton(string pressed)
        {
            if (!IsActive) return false;
            try { current.Handle(Ui.Normalize(pressed)); }
            catch (Exception ex) { HomeNav.Log("login: " + pressed + " failed: " + ex); }
            return true;
        }

        private static void OnKey(object sender, KeyEventArgs e)
        {
            if (!IsActive || e.GetType().Name == "GameControllerInputEventArgs") return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (current.WantsText)
            {
                if (key == Key.Back) { current.Erase(); e.Handled = true; return; }
                if (key == Key.Space) return;   // typed as text
                if (key == Key.Enter) { HandleButton("Start"); e.Handled = true; return; }   // Enter = done
            }
            string b = key == Key.Enter || key == Key.Space ? "A"
                     : key == Key.Escape || key == Key.Back ? "B"
                     : key == Key.Left ? "DPadLeft" : key == Key.Right ? "DPadRight"
                     : key == Key.Up ? "DPadUp" : key == Key.Down ? "DPadDown"
                     : key == Key.Delete ? "X" : key == Key.F2 ? "Y" : key == Key.Tab ? "Start" : null;
            if (b != null) { HandleButton(b); e.Handled = true; }
        }

        private static void OnText(object sender, TextCompositionEventArgs e)
        {
            if (!IsActive || !current.WantsText || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            current.Type(e.Text);
            e.Handled = true;
        }

        // developer switch: %TEMP%\selene-dev-keys holds "<ms> <button>" pairs (e.g. "3000 A 800 DPadRight"), pressed in turn
        // after start, to walk through the screens without a controller
        private static void DevKeys()
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-dev-keys");
            if (!System.IO.File.Exists(file)) return;
            var parts = System.IO.File.ReadAllText(file).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var queue = new Queue<string[]>();
            for (int i = 0; i + 1 < parts.Length; i += 2) queue.Enqueue(new[] { parts[i], parts[i + 1] });
            var timer = new DispatcherTimer();
            Action next = null;
            next = () =>
            {
                if (queue.Count == 0) return;
                var step = queue.Peek();
                int ms;
                timer.Interval = TimeSpan.FromMilliseconds(int.TryParse(step[0], out ms) ? ms : 500);
                timer.Start();
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var step = queue.Dequeue();
                HomeNav.Log("dev key " + step[1]);
                if (step[1].StartsWith("type:") && current != null) current.Type(step[1].Substring(5));
                else HandleButton(step[1]);
                next();
            };
            next();
        }

        // ---------------- flow ----------------
        private static void Open()
        {
            if (scene == null) return;
            var next = pending;
            pending = null;
            if (next != null) Show(next(), 0);
            else if (!introPlayed) { introPlayed = true; Show(new IntroScreen(), 0); }
            else Show(Start(), 0);
        }

        // where the login starts after the intro
        public static Screen Start()
        {
            if (Profiles.LoadError == null && Profiles.FirstRun) return new SetupScreen(true);
            return new WhoScreen(null);
        }

        // switches screens: the old one slides out to one side and fades, the new one comes in from the other
        // (dir 1 = forward, -1 = back, 0 = cross-fade)
        public static void Show(Screen next, int dir)
        {
            var old = current;
            current = next;
            stage.Children.Add(next.Root);
            if (old != null)
            {
                old.Hidden();
                var r = old.Root;
                r.IsHitTestVisible = false;
                Ui.Slide(r, 0, -dir * 90, r.Opacity, 0, 240, () => stage.Children.Remove(r));
            }
            Ui.Slide(next.Root, dir * 90, 0, 0, 1, 320);
            next.Shown();
        }

        // signed in (or carrying on without profiles): the layer fades away to the home
        public static void Finish()
        {
            SeleneShell.HideLogin();
        }

        // menu actions: open the layer on a given screen
        public static void OpenWith(Func<Screen> screen)
        {
            if (scene == null) return;
            if (loginRoot.IsVisible) { Show(screen(), 1); return; }
            pending = screen;
            SeleneShell.ShowLogin();
        }

        // ---------------- backdrop ----------------
        private static Grid BuildScene()
        {
            var g = new Grid { ClipToBounds = true };
            var gradient = new Border();
            gradient.SetResourceReference(Border.BackgroundProperty, "MainBackgourndBrush");
            g.Children.Add(gradient);
            var wallpaper = new Image { Stretch = Stretch.UniformToFill };
            wallpaper.SetResourceReference(Image.SourceProperty, "SeleneWallpaper");
            RenderOptions.SetBitmapScalingMode(wallpaper, BitmapScalingMode.HighQuality);
            g.Children.Add(wallpaper);
            g.Children.Add(new Border { Background = Ink.Brush("SeleneScrimColor", 0.5) });

            // soft spotlight in the theme's accent from the top left, slowly breathing
            var spot = new RadialGradientBrush { Center = new Point(0.16, -0.12), GradientOrigin = new Point(0.16, -0.12), RadiusX = 0.62, RadiusY = 1.15 };
            spot.GradientStops.Add(Ink.Stop("GlyphColor", 0.36, 0));
            spot.GradientStops.Add(Ink.Stop("GlyphColor", 0.12, 0.45));
            spot.GradientStops.Add(Ink.Stop("GlyphColor", 0, 1));
            var spotLayer = new Border { Background = spot };
            spotLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromSeconds(9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            g.Children.Add(spotLayer);

            // hazy floor glow
            var haze = new RadialGradientBrush { Center = new Point(0.5, 1.02), GradientOrigin = new Point(0.5, 1.02), RadiusX = 0.95, RadiusY = 0.34 };
            haze.GradientStops.Add(Ink.Stop("GlyphColor", 0.22, 0));
            haze.GradientStops.Add(Ink.Stop("GlyphColor", 0, 1));
            g.Children.Add(new Border { Background = haze });

            var canvas = new Canvas { IsHitTestVisible = false };
            g.Children.Add(canvas);
            g.SizeChanged += (s, e) => { if (canvas.Children.Count == 0 && e.NewSize.Width > 0) FillParticles(canvas, e.NewSize); };

            stage = new Grid();
            g.Children.Add(stage);
            return g;
        }

        private static void FillParticles(Canvas canvas, Size size)
        {
            for (int i = 0; i < 80; i++)
            {
                bool bokeh = i < 10;
                double d = bokeh ? 14 + rnd.NextDouble() * 34 : 2 + rnd.NextDouble() * 5;
                var key = rnd.NextDouble() < 0.72 ? "GlyphColor" : "SeleneInkColor";
                var fill = new RadialGradientBrush();
                fill.GradientStops.Add(Ink.Stop(key, bokeh ? 0.33 : 0.94, 0));
                fill.GradientStops.Add(Ink.Stop(key, 0, 1));
                var dot = new Ellipse { Width = d, Height = d, Fill = fill };
                Canvas.SetLeft(dot, rnd.NextDouble() * size.Width);
                Canvas.SetTop(dot, size.Height * (0.25 + rnd.NextDouble() * 0.75));
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
    }

    // ---------------- logo intro ----------------
    // about 2.6 s: the crescent crown rises in, the full moon's glow breathes once, a chime, then on to the next screen
    public class IntroScreen : Screen
    {
        private readonly FrameworkElement content;
        private readonly UIElement glow;
        private DispatcherTimer timer;
        private bool done;

        public IntroScreen()
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            UIElement halo;
            col.Children.Add(Ui.Mark(190, out halo));
            glow = halo;
            var word = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 26, 0, 0) };
            foreach (var c in "SELENE")
                word.Children.Add(new TextBlock { Text = c.ToString(), FontSize = 30, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(0, 0, 16, 0) });
            col.Children.Add(word);
            content = col;
            Root.Children.Add(col);
        }

        public override void Shown()
        {
            Ui.Rise(content, 120, 16, 900);
            var breathe = new DoubleAnimationUsingKeyFrames();
            breathe.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            breathe.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            breathe.KeyFrames.Add(new EasingDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            glow.BeginAnimation(UIElement.OpacityProperty, breathe);
            int ticks = 0;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            timer.Tick += (s, e) =>
            {
                ticks++;
                if (ticks == 1) Ui.Chime();
                if (ticks >= 6) Next();
            };
            timer.Start();
        }

        public override void Handle(string button)
        {
            if (button == "A" || button == "B" || button == "Start") Next();
        }

        public override void Hidden() { if (timer != null) timer.Stop(); }

        private void Next()
        {
            if (done) return;
            done = true;
            timer.Stop();
            LoginScene.Show(LoginScene.Start(), 0);
        }
    }

    // ---------------- Who's playing? ----------------
    public class WhoScreen : Screen
    {
        private readonly List<Profile> profiles;
        private readonly List<Tile> tiles = new List<Tile>();
        private readonly List<FrameworkElement> items = new List<FrameworkElement>();
        private readonly Tile power;
        private readonly Border menu;
        private readonly List<Border> menuRows = new List<Border>();
        private readonly TextBlock hint;
        private int index;            // profiles, then Add (or Continue when profiles couldn't load)
        private bool onPower, menuOpen, leaving;
        private int menuIndex;
        private Guid removeArmed = Guid.Empty;
        private DateTime removeArmedAt;

        // glyph (Segoe MDL2 Assets), label, helper command, built-in fallback
        private static readonly string[][] PowerItems = {
            new[] { "\uE708", "Sleep", "power-sleep", "rundll32.exe|powrprof.dll,SetSuspendState 0,1,0" },
            new[] { "\uE777", "Restart", "power-restart", "shutdown.exe|/r /t 0" },
            new[] { "\uE7E8", "Shut down", "power-off", "shutdown.exe|/s /t 0" } };

        public WhoScreen(Guid? focus)
        {
            profiles = Profiles.All.ToList();
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -60, 0, 0) };
            col.Children.Add(Ui.Title("Who's playing?"));
            if (Profiles.LoadError != null)
                col.Children.Add(Ui.Sub("Profiles couldn't be loaded (" + Profiles.LoadError + "). You can still play; nothing is saved this time."));
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) };
            foreach (var p in profiles) AddItem(row, Avatar(p, 170), p.Name, Accent(p));
            if (Profiles.LoadError != null) AddItem(row, Glyph("\uE72A", 54), "Continue", null);
            else AddItem(row, Glyph("\uE710", 60), "Add", null);
            col.Children.Add(row);
            hint = Ui.Text("", 18, 0.6);
            hint.Margin = new Thickness(0, 34, 0, 0);
            col.Children.Add(hint);
            Root.Children.Add(col);

            // power button, bottom centre (Down from the profiles): Sleep / Restart / Shut down
            power = new Tile(64, 64, 32, 5);
            power.Face.Child = new TextBlock { Text = "\uE7E8", FontSize = 24, FontFamily = Ui.Glyphs, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            power.Root.HorizontalAlignment = HorizontalAlignment.Center;
            power.Root.VerticalAlignment = VerticalAlignment.Bottom;
            power.Root.Margin = new Thickness(0, 0, 0, 56);
            Root.Children.Add(power.Root);
            menu = new Border
            {
                CornerRadius = new CornerRadius(16), Background = Ink.Brush("SeleneGlassColor", 0.94), BorderBrush = Ink.Brush("SeleneInkColor", 0.2), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 140),
                Padding = new Thickness(8), Width = 320, Visibility = Visibility.Collapsed
            };
            var menuStack = new StackPanel();
            foreach (var item in PowerItems)
            {
                var r = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 12, 18, 12) };
                var rs = new StackPanel { Orientation = Orientation.Horizontal };
                rs.Children.Add(new TextBlock { Text = item[0], FontSize = 20, FontFamily = Ui.Glyphs, VerticalAlignment = VerticalAlignment.Center, Width = 36 });
                rs.Children.Add(new TextBlock { Text = item[1], FontSize = 22, FontFamily = Ui.Body, VerticalAlignment = VerticalAlignment.Center });
                r.Child = rs;
                menuRows.Add(r);
                menuStack.Children.Add(r);
            }
            menu.Child = menuStack;
            Root.Children.Add(menu);

            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
            if (profiles.Count > 0) Root.Children.Add(Ui.Prompts(false, Ui.Prompt("Y", "Edit profile"), Ui.Prompt("X", "Remove")));

            var active = Profiles.Active;
            var want = focus ?? (active != null ? active.Id : Guid.Empty);
            index = Math.Max(0, profiles.FindIndex(p => p.Id == want));
        }

        private void AddItem(Panel row, FrameworkElement face, string name, Color? accent)
        {
            var tile = new Tile(170, 170, 85, 9) { FocusScale = 1.1 };
            tile.Face.Child = face;
            tile.Face.ClipToBounds = true;
            if (accent.HasValue) tile.Tint(accent.Value);
            var item = new StackPanel { Margin = new Thickness(28, 0, 28, 0) };
            item.Children.Add(tile.Root);
            item.Children.Add(new TextBlock { Text = name, FontSize = 24, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(item);
            tiles.Add(tile);
            items.Add(item);
        }

        // each profile glows in its own theme's accent
        private static Color? Accent(Profile p)
        {
            var theme = ThemePalettes.Find(string.IsNullOrEmpty(p.Theme) ? ThemePalettes.DefaultId : p.Theme);
            if (theme == null || string.IsNullOrEmpty(theme.Accent)) return null;
            try { return (Color)ColorConverter.ConvertFromString(theme.Accent); } catch (FormatException) { return null; }
        }

        public static FrameworkElement Avatar(Profile p, double size)
        {
            var pic = Wallpapers.Load(Profiles.AvatarFile(p), (int)(size * 2));
            if (pic != null) return new Ellipse { Width = size, Height = size, Fill = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } };
            // no picture: the first letter on the profile's accent
            var g = new Grid { Width = size, Height = size };
            var accent = Accent(p);
            g.Children.Add(new Ellipse { Fill = accent.HasValue ? new SolidColorBrush(accent.Value) : (Brush)Ink.Brush("GlyphColor") });
            g.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(p.Name) ? "?" : p.Name.Substring(0, 1).ToUpperInvariant(), FontSize = size * 0.42, FontFamily = Ui.Display, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return g;
        }

        private static FrameworkElement Glyph(string glyph, double size)
        {
            return new TextBlock { Text = glyph, FontSize = size, FontFamily = Ui.Glyphs, Foreground = Ink.Brush("SeleneInkColor", 0.85), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        public override void Shown()
        {
            for (int i = 0; i < items.Count; i++) Ui.Rise(items[i], 160 + i * 55);
            Ui.Rise(power.Root, 200 + items.Count * 55);
            ShowFocus();
        }

        private void ShowFocus()
        {
            for (int i = 0; i < tiles.Count; i++) tiles[i].Focus(!onPower && i == index);
            power.Focus(onPower);
            hint.Text = onPower ? "Sleep, restart or shut down" : "";
        }

        public override void Handle(string b)
        {
            if (leaving) return;
            if (menuOpen) { HandleMenu(b); return; }
            if (b == "DPadDown" && !onPower) { onPower = true; Ui.Tick(); ShowFocus(); return; }
            if (b == "DPadUp" && onPower) { onPower = false; Ui.Tick(); ShowFocus(); return; }
            if (!onPower && (b == "DPadLeft" || b == "DPadRight"))
            {
                int next = index + (b == "DPadLeft" ? -1 : 1);
                if (next < 0 || next >= tiles.Count) return;
                index = next;
                removeArmed = Guid.Empty;
                Ui.Tick();
                ShowFocus();
                return;
            }
            if (b == "A")
            {
                if (onPower) { OpenMenu(); return; }
                if (index < profiles.Count) { SignIn(index); return; }
                Ui.Confirm();
                if (Profiles.LoadError != null) { leaving = true; LoginScene.Finish(); return; }
                LoginScene.Show(new ProfileSteps(null, () => new WhoScreen(null)), 1);
                return;
            }
            if (onPower || index >= profiles.Count) return;
            if (b == "Y") { Ui.Confirm(); LoginScene.Show(new ProfileSteps(profiles[index], () => new WhoScreen(profiles[index].Id)), 1); return; }
            if (b == "X") Remove(profiles[index]);
        }

        private void SignIn(int i)
        {
            leaving = true;
            Ui.Play("LoginSelect.wav", 0.9);
            for (int k = 0; k < items.Count; k++) if (k != i) Ui.Fade(items[k], 0, 220);
            Ui.ScaleTo(tiles[i].Root, 1.22, 380);
            Profiles.SignIn(profiles[i].Id);
            HomeNav.ProfileChanged();
            var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            later.Tick += (s, e) => { later.Stop(); LoginScene.Finish(); };
            later.Start();
        }

        // X twice: removes the profile (its favorites and playtime go with it; the games stay)
        private void Remove(Profile p)
        {
            if (removeArmed != p.Id || (DateTime.Now - removeArmedAt).TotalSeconds > 4)
            {
                removeArmed = p.Id;
                removeArmedAt = DateTime.Now;
                hint.Text = "Press X again to remove " + p.Name + " (favorites and playtime go with it; games stay)";
                Ui.Shake(items[index]);
                Ui.Back();
                return;
            }
            Profiles.Remove(p.Id);
            Ui.Back();
            LoginScene.Show(Profiles.FirstRun ? (Screen)new ProfileSteps(null, () => new WhoScreen(null)) : new WhoScreen(null), 0);
        }

        private void OpenMenu()
        {
            menuOpen = true;
            menuIndex = 0;
            ShowMenu();
            menu.Visibility = Visibility.Visible;
            Ui.Rise(menu, 0, 14, 240);
            Ui.Play("OpenPanel.wav", 0.8);
        }

        private void ShowMenu()
        {
            for (int i = 0; i < menuRows.Count; i++)
            {
                bool on = i == menuIndex;
                menuRows[i].Background = on ? (Brush)Ink.Brush("SeleneFocusColor") : Brushes.Transparent;
                foreach (TextBlock t in ((StackPanel)menuRows[i].Child).Children)
                    t.Foreground = on ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor");
            }
        }

        private void HandleMenu(string b)
        {
            if (b == "B")
            {
                menuOpen = false;
                Ui.Fade(menu, 0, 160, 0, () => { if (!menuOpen) menu.Visibility = Visibility.Collapsed; });
                Ui.Back();
            }
            else if (b == "DPadUp" && menuIndex > 0) { menuIndex--; Ui.Tick(); ShowMenu(); }
            else if (b == "DPadDown" && menuIndex < menuRows.Count - 1) { menuIndex++; Ui.Tick(); ShowMenu(); }
            else if (b == "A") RunPower(menuIndex);
        }

        private void RunPower(int i)
        {
            var item = PowerItems[i];
            HomeNav.Log("login: power menu -> " + item[1]);
            Ui.Confirm();
            menuOpen = false;
            menu.Visibility = Visibility.Collapsed;
            try
            {
                // the optional companion helper does it its own way (e.g. turns the TV off first); else Windows' own commands
                var helper = SelenePaths.Helper;
                var parts = item[3].Split('|');
                var start = !string.IsNullOrEmpty(helper) && System.IO.File.Exists(helper)
                    ? new System.Diagnostics.ProcessStartInfo(helper, item[2])
                    : new System.Diagnostics.ProcessStartInfo(parts[0], parts[1]);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                System.Diagnostics.Process.Start(start);
            }
            catch (Exception ex) { HomeNav.Log("login power failed: " + ex.Message); }
        }
    }
}
