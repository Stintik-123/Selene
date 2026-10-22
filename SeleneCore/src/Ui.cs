using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Selene
{
    // Building blocks of Selene's own screens (login, setup check, profile steps): theme-colored text and tiles, the
    // focus look, motion and sounds, so every screen moves and sounds the same (see CLAUDE.md "Feel").
    public static class Ui
    {
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display");
        public static readonly FontFamily Body = new FontFamily("Segoe UI");
        public static readonly FontFamily Glyphs = new FontFamily("Segoe MDL2 Assets");

        // ---------------- text ----------------
        public static TextBlock Text(string text, double size, double alpha = 1, FontFamily font = null)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, FontFamily = font ?? Body, Foreground = Ink.Brush("SeleneInkColor", alpha),
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        public static TextBlock Title(string text) { return Text(text, 44, 1, Display); }

        public static TextBlock Sub(string text)
        {
            var t = Text(text, 21, 0.7);
            t.Margin = new Thickness(0, 10, 0, 0);
            return t;
        }

        // (A) Next style hint for the bottom corners
        public static StackPanel Prompt(string button, string label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0) };
            var dot = new Grid { Width = 32, Height = 32 };
            dot.Children.Add(new Ellipse { Fill = Ink.Brush("SeleneInkColor") });
            dot.Children.Add(new TextBlock { Text = button, FontSize = button.Length > 1 ? 11 : 16, FontWeight = FontWeights.SemiBold, FontFamily = Body, Foreground = Ink.Brush("SeleneScrimColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(dot);
            sp.Children.Add(new TextBlock { Text = label, FontSize = 21, FontFamily = Body, Foreground = Ink.Brush("SeleneInkColor", 0.9), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            return sp;
        }

        // prompts row, bottom right (or left)
        public static StackPanel Prompts(bool right, params StackPanel[] items)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = right ? new Thickness(0, 0, 64, 48) : new Thickness(46, 0, 0, 48)
            };
            foreach (var i in items) row.Children.Add(i);
            return row;
        }

        // ---------------- motion ----------------
        public static IEasingFunction Out() { return new CubicEase { EasingMode = EasingMode.EaseOut }; }

        public static void Fade(UIElement el, double to, int ms, int delay = 0, Action done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = Out() };
            if (done != null) a.Completed += (s, e) => done();
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        private static TranslateTransform Shift(UIElement el)
        {
            var t = el.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen) { t = new TranslateTransform(); el.RenderTransform = t; }
            return t;
        }

        // appears: rises a few pixels while fading in
        public static void Rise(UIElement el, int delay = 0, double from = 18, int ms = 320)
        {
            el.Opacity = 0;
            Fade(el, 1, ms, delay);
            Shift(el).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = Out() });
        }

        // slides sideways while fading (screen changes)
        public static void Slide(UIElement el, double fromX, double toX, double fromOpacity, double toOpacity, int ms, Action done = null)
        {
            el.Opacity = fromOpacity;
            Fade(el, toOpacity, ms, 0, done);
            Shift(el).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, toX, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out() });
        }

        public static ScaleTransform Scaler(FrameworkElement el)
        {
            var s = el.RenderTransform as ScaleTransform;
            if (s == null || s.IsFrozen) { s = new ScaleTransform(1, 1); el.RenderTransform = s; el.RenderTransformOrigin = new Point(0.5, 0.5); }
            return s;
        }

        public static void ScaleTo(FrameworkElement el, double to, int ms = 200)
        {
            var s = Scaler(el);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out() });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out() });
        }

        // a value or state changed: a small pop
        public static void Pop(FrameworkElement el)
        {
            var s = Scaler(el);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out() });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out() });
        }

        // "can't do that": a short sideways shake
        public static void Shake(UIElement el)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
            foreach (var kv in new[] { new[] { 0.0, 0 }, new[] { 0.18, -9 }, new[] { 0.4, 8 }, new[] { 0.62, -5 }, new[] { 0.82, 3 }, new[] { 1.0, 0 } })
                a.KeyFrames.Add(new LinearDoubleKeyFrame(kv[1], KeyTime.FromPercent(kv[0])));
            Shift(el).BeginAnimation(TranslateTransform.XProperty, a);
        }

        // ---------------- sounds ----------------
        private static readonly Dictionary<string, MediaPlayer> players = new Dictionary<string, MediaPlayer>();

        public static void Play(string file, double volume)
        {
            var path = SelenePaths.ThemeAudio(file);
            if (!System.IO.File.Exists(path)) return;
            try
            {
                MediaPlayer mp;
                if (!players.TryGetValue(file, out mp)) { mp = new MediaPlayer(); mp.Open(new Uri(path)); players[file] = mp; }
                mp.Volume = volume;
                mp.Position = TimeSpan.Zero;
                mp.Play();
            }
            catch (Exception ex) { HomeNav.Log("sound " + file + ": " + ex.Message); }
        }

        public static void Tick() { Play("navigation.wav", 0.7); }
        public static void Confirm() { Play("activation.wav", 0.85); }
        public static void Back() { Play("CloseAdditionalView.wav", 0.8); }
        public static void Chime() { Play("GameReady.wav", 0.85); }
        public static void Boot() { Play("Boot.wav", 0.9); }

        // decodes pictures one after another off the UI thread and hands each over on it (avatar and wallpaper grids)
        public static void LoadInOrder(IList<string> files, int width, Action<int, ImageSource> loaded)
        {
            var dispatcher = Application.Current.Dispatcher;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var pic = Wallpapers.Load(files[i], width);
                    if (pic == null) continue;
                    int at = i;
                    dispatcher.BeginInvoke(new Action(() => loaded(at, pic)), System.Windows.Threading.DispatcherPriority.Background);
                }
            });
        }

        // ---------------- navigation ----------------
        // controller and keyboard both arrive as button names; the left stick moves like the d-pad
        public static string Normalize(string button)
        {
            if (button == null) return "";
            if (button.StartsWith("LeftStick") && button != "LeftStick") return "DPad" + button.Substring("LeftStick".Length);
            return button;
        }

        // moves an index around a grid of `count` items, `cols` wide; returns -1 when it would leave the grid on that side
        public static int GridMove(int index, string dir, int count, int cols)
        {
            int row = index / cols, col = index % cols;
            switch (dir)
            {
                case "DPadLeft": return col > 0 ? index - 1 : -1;
                case "DPadRight": return col < cols - 1 && index + 1 < count ? index + 1 : -1;
                case "DPadUp": return row > 0 ? index - cols : -1;
                case "DPadDown":
                    if (index + cols < count) return index + cols;
                    return row < (count - 1) / cols ? count - 1 : -1;   // short last row: the last item
            }
            return -1;
        }

        // ---------------- the crescent crown ----------------
        // Selene's mark as vectors so it takes the theme's colors: crescent in the text color, full moon glowing in the accent
        public static Grid Mark(double size, out UIElement glow)
        {
            var g = new Grid { Width = size, Height = size };
            var canvas = new Canvas { Width = 72, Height = 72 };
            var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
            var halo = new Ellipse { Width = 64, Height = 64 };
            var haloBrush = new RadialGradientBrush();
            haloBrush.GradientStops.Add(Ink.Stop("GlyphColor", 0.7, 0));
            haloBrush.GradientStops.Add(Ink.Stop("GlyphColor", 0.38, 0.3));
            haloBrush.GradientStops.Add(Ink.Stop("GlyphColor", 0.12, 0.65));
            haloBrush.GradientStops.Add(Ink.Stop("GlyphColor", 0, 1));
            halo.Fill = haloBrush;
            Canvas.SetLeft(halo, 36 - 32); Canvas.SetTop(halo, 18 - 32);
            canvas.Children.Add(halo);
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M 9.109,20.577 A 31,31 0 1 0 62.891,20.577 A 27,27 0 1 1 9.109,20.577 Z"),
                Fill = Ink.Brush("SeleneInkColor")
            });
            var ring = new Ellipse { Width = 18, Height = 18, Stroke = Ink.Brush("SeleneInkColor"), StrokeThickness = 3.2 };
            Canvas.SetLeft(ring, 36 - 9); Canvas.SetTop(ring, 18 - 9);
            canvas.Children.Add(ring);
            var dot = new Ellipse { Width = 6.4, Height = 6.4, Fill = Ink.Brush("SeleneInkColor") };
            Canvas.SetLeft(dot, 36 - 3.2); Canvas.SetTop(dot, 18 - 3.2);
            canvas.Children.Add(dot);
            g.Children.Add(view);
            glow = halo;
            return g;
        }
    }

    // A focusable tile: grows and shows a glowing ring in the theme's focus color when focused
    public class Tile
    {
        public readonly Grid Root;
        public readonly Border Face;
        private readonly Border ring;
        public double FocusScale = 1.07;
        public bool Focused { get; private set; }

        public Tile(double width, double height, double radius, double gap = 6)
        {
            Root = new Grid { Width = width, Height = height };
            Face = new Border
            {
                CornerRadius = new CornerRadius(radius), Background = Ink.Brush("SeleneInkColor", 0.08),
                BorderBrush = Ink.Brush("SeleneInkColor", 0.14), BorderThickness = new Thickness(1)
            };
            ring = new Border
            {
                CornerRadius = new CornerRadius(radius + gap), Margin = new Thickness(-gap), BorderThickness = new Thickness(3),
                BorderBrush = Ink.Brush("SeleneFocusColor"), Opacity = 0, IsHitTestVisible = false,
                Effect = Ink.Track(new DropShadowEffect { ShadowDepth = 0, BlurRadius = 22, Opacity = 0.55 }, DropShadowEffect.ColorProperty, "SeleneFocusColor")
            };
            Root.Children.Add(Face);
            Root.Children.Add(ring);
        }

        // a ring in a fixed color instead of the theme's focus color (each profile glows in its own theme's accent)
        public void Tint(Color color)
        {
            ring.BorderBrush = new SolidColorBrush(color);
            ring.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 26, Opacity = 0.7, Color = color };
        }

        public void Focus(bool on)
        {
            if (on == Focused) return;
            Focused = on;
            Ui.ScaleTo(Root, on ? FocusScale : 1, on ? 200 : 160);
            Ui.Fade(ring, on ? 1 : 0, on ? 180 : 140);
        }
    }

    // A rounded button: glass at rest, filled with the focus color (and its text color) when focused
    public class Pill
    {
        public readonly Border Root;
        private readonly TextBlock label;
        public bool Focused { get; private set; }

        public Pill(string text, double minWidth = 200)
        {
            label = new TextBlock { Text = text, FontSize = 21, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Root = new Border
            {
                Child = label, CornerRadius = new CornerRadius(27), Height = 54, MinWidth = minWidth, Padding = new Thickness(34, 0, 34, 2),
                Background = Ink.Brush("SeleneInkColor", 0.14), BorderBrush = Ink.Brush("SeleneInkColor", 0.22), BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 0, 8, 0)
            };
        }

        public string Text { get { return label.Text; } set { label.Text = value; } }

        public void Focus(bool on)
        {
            if (on == Focused) return;
            Focused = on;
            Root.Background = on ? Ink.Brush("SeleneFocusColor") : Ink.Brush("SeleneInkColor", 0.14);
            Root.BorderBrush = on ? Ink.Brush("SeleneFocusColor") : Ink.Brush("SeleneInkColor", 0.22);
            label.Foreground = on ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor");
            Root.Effect = on ? Ink.Track(new DropShadowEffect { ShadowDepth = 0, BlurRadius = 22, Opacity = 0.4 }, DropShadowEffect.ColorProperty, "SeleneFocusColor") : null;
            Ui.ScaleTo(Root, on ? 1.06 : 1, 160);
        }
    }

    // One screen of the login layer (intro, setup check, profile steps, who's playing)
    public abstract class Screen
    {
        public Grid Root = new Grid();
        // a controller button (normalized: the left stick arrives as DPad*); keyboard keys arrive mapped to these too
        public abstract void Handle(string button);
        // typed text (keyboard), for screens that take it
        public virtual bool WantsText { get { return false; } }
        public virtual void Type(string text) { }
        public virtual void Erase() { }
        public virtual void Shown() { }
        public virtual void Hidden() { }
    }
}
