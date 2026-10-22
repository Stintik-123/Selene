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
        private static FontFamily cinzel;

        // Cinzel, the logo's lettering (theme\Fonts\Cinzel.ttf), for the SELENE word; Segoe when it's missing
        public static FontFamily Wordmark
        {
            get
            {
                if (cinzel != null) return cinzel;
                var folder = SelenePaths.ThemeFile("Fonts");
                cinzel = System.IO.File.Exists(System.IO.Path.Combine(folder, "Cinzel.ttf"))
                    ? new FontFamily(new Uri(folder.TrimEnd('\\') + "\\"), "./#Cinzel") : Display;
                return cinzel;
            }
        }

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
        public static void Chime() { Play("Success.wav", 0.85); }   // something finished: a profile made, a setup step done
        public static void Boot() { Play("Boot.wav", 0.9); }
        public static void Alert() { Play("Alert.wav", 0.85); }   // asking before something can't be undone

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

        // a button-like pill: the controller badge inside, then what it does (the main one filled with the focus color)
        public static Border ActionPill(string button, string label, bool main)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Grid { Width = 26, Height = 26 };
            dot.Children.Add(new Ellipse { Fill = main ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor", 0.9) });
            dot.Children.Add(new TextBlock { Text = button, FontSize = 13, FontWeight = FontWeights.Bold, FontFamily = Body, Foreground = main ? Ink.Brush("SeleneFocusColor") : Ink.Brush("SeleneScrimColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = label, FontSize = 17, FontWeight = FontWeights.SemiBold, FontFamily = Body, Foreground = main ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 1) });
            return new Border
            {
                Child = row, CornerRadius = new CornerRadius(20), Padding = new Thickness(6, 6, 16, 6), Margin = new Thickness(0, 0, 10, 0),
                Background = main ? Ink.Brush("SeleneFocusColor") : Ink.Brush("SeleneInkColor", 0.12),
                BorderBrush = main ? Ink.Brush("SeleneFocusColor") : Ink.Brush("SeleneInkColor", 0.2), BorderThickness = new Thickness(1)
            };
        }

        // ---------------- navigation ----------------
        // controller and keyboard both arrive as button names; the left stick moves like the d-pad
        public static string Normalize(string button)
        {
            if (button == null) return "";
            if (button.StartsWith("LeftStick") && button != "LeftStick") return "DPad" + button.Substring("LeftStick".Length);
            return button;
        }

        // true when the same direction came again within a moment (a held stick or d-pad repeating), so a move that
        // leaves one group for another (cards -> buttons) can wait for a fresh press
        private static string lastDirection;
        private static DateTime lastDirectionAt;
        public static bool Repeated(string button)
        {
            return button == lastDirection && (DateTime.Now - lastDirectionAt).TotalMilliseconds < 300;
        }

        public static void Pressed(string button)
        {
            if (button.StartsWith("DPad")) { lastDirection = button; lastDirectionAt = DateTime.Now; }
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

    // A choice between a few options, changed with one button: a label, the options in a glass pill (the chosen one
    // filled with the focus color), and the button badge
    public class Segmented
    {
        public readonly StackPanel Root = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly List<Border> segments = new List<Border>();
        private readonly List<TextBlock> labels = new List<TextBlock>();

        public Segmented(string title, string[] options, string button)
        {
            Root.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in options)
            {
                var label = new TextBlock { Text = o, FontSize = 17, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.8) };
                var seg = new Border { CornerRadius = new CornerRadius(19), Padding = new Thickness(18, 7, 18, 8), Child = label };
                row.Children.Add(seg);
                segments.Add(seg);
                labels.Add(label);
            }
            Root.Children.Add(new Border
            {
                Child = row, CornerRadius = new CornerRadius(23), Padding = new Thickness(4),
                Background = Ink.Brush("SeleneInkColor", 0.1), BorderBrush = Ink.Brush("SeleneInkColor", 0.18), BorderThickness = new Thickness(1)
            });
            var badge = new Grid { Width = 28, Height = 28, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.Children.Add(new Ellipse { Fill = Ink.Brush("SeleneInkColor", 0.9) });
            badge.Children.Add(new TextBlock { Text = button, FontSize = 14, FontWeight = FontWeights.Bold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneScrimColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            Root.Children.Add(badge);
        }

        public void Select(int index, bool animate)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                bool on = i == index;
                segments[i].Background = on ? (Brush)Ink.Brush("SeleneFocusColor") : Brushes.Transparent;
                labels[i].Foreground = on ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor", 0.8);
                labels[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (animate) Ui.Pop(segments[index]);
        }
    }

    public enum Tone { Accent, Success, Warning, Muted }

    // The panel that explains whatever is focused: its icon in a soft tinted circle, its name with a status chip,
    // one line about it, and what the buttons do as pills (badge inside) with a small note beside them
    public class DetailPanel
    {
        public readonly StackPanel Root = new StackPanel();
        private readonly Ellipse circle;
        private readonly TextBlock glyph, title, chipText, about, note;
        private readonly Border chip;
        private readonly StackPanel actions;

        // everything centred: icon, name and chip on one line, the line about it, then the action pills and the note
        public DetailPanel(double width)
        {
            Root.Width = width;
            Root.HorizontalAlignment = HorizontalAlignment.Center;
            var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var badge = new Grid { Width = 52, Height = 52, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            circle = new Ellipse();
            glyph = new TextBlock { FontFamily = Ui.Glyphs, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badge.Children.Add(circle);
            badge.Children.Add(glyph);
            head.Children.Add(badge);
            title = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center };
            chipText = new TextBlock { FontSize = 13, FontWeight = FontWeights.Bold, FontFamily = Ui.Body };
            chip = new Border { Child = chipText, CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 3, 11, 4), Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(title);
            head.Children.Add(chip);
            Root.Children.Add(head);
            about = new TextBlock { FontSize = 19, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.82), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 52, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            Root.Children.Add(about);
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            note = new TextBlock { FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = width - 260 };
            foot.Children.Add(actions);
            foot.Children.Add(note);
            Root.Children.Add(foot);
        }

        // actions: pairs of button + label, e.g. "A", "Install"; the first is drawn as the main one
        public void Show(string icon, Tone tone, string name, string status, string text, string noteText, params string[] buttons)
        {
            var key = tone == Tone.Success ? "SeleneSuccessColor" : tone == Tone.Muted ? "SeleneInkColor" : "GlyphColor";
            if (tone == Tone.Warning)
            {
                circle.Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x6B, 0x6B));
                glyph.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                chipText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                chip.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0x6B, 0x6B));
            }
            else
            {
                circle.Fill = Ink.Brush(key, 0.16);
                glyph.Foreground = Ink.Brush(key);
                chipText.Foreground = Ink.Brush(key);
                chip.Background = Ink.Brush(key, 0.16);
            }
            glyph.Text = icon;
            title.Text = name;
            chipText.Text = (status ?? "").ToUpperInvariant();
            chip.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
            about.Text = text;
            note.Text = noteText ?? "";
            note.Margin = new Thickness(buttons.Length > 0 ? 8 : 0, 0, 0, 0);
            actions.Children.Clear();
            for (int i = 0; i + 1 < buttons.Length; i += 2) actions.Children.Add(Ui.ActionPill(buttons[i], buttons[i + 1], i == 0));
        }
    }

    // A centred question over a dimmed screen: icon, title, line, and two buttons (the safe one chosen first).
    // Left/right picks, A answers, B is the safe answer; the caller sends it buttons while it's open.
    public class Dialog
    {
        public readonly Grid Root;
        private readonly Pill yes, no;
        private readonly Action onYes, onNo;
        private bool onYesButton, done;
        private readonly FrameworkElement panel;

        public Dialog(string icon, string title, string text, string yesLabel, string noLabel, Action onYes, Action onNo)
        {
            this.onYes = onYes;
            this.onNo = onNo;
            Root = new Grid { Background = Ink.Brush("SeleneScrimColor", 0.62) };
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var badge = new Grid { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center };
            badge.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x6B, 0x6B)) });
            var glyph = new TextBlock { Text = icon, FontFamily = Ui.Glyphs, FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            badge.Children.Add(glyph);
            col.Children.Add(badge);
            col.Children.Add(new TextBlock { Text = title, FontSize = 34, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) });
            col.Children.Add(new TextBlock { Text = text, FontSize = 19, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.75), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 10, 0, 0) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 34, 0, 0) };
            no = new Pill(noLabel, 190);
            yes = new Pill(yesLabel, 190);
            buttons.Children.Add(no.Root);
            buttons.Children.Add(yes.Root);
            col.Children.Add(buttons);
            panel = new Border
            {
                Child = col, Width = 640, CornerRadius = new CornerRadius(28), Padding = new Thickness(40, 40, 40, 36),
                Background = Ink.Brush("SeleneGlassColor", 0.96), BorderBrush = Ink.Brush("SeleneInkColor", 0.16), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 60, Opacity = 0.55, Color = Colors.Black }
            };
            Root.Children.Add(panel);
            ShowFocus();
        }

        public void Open(Panel host)
        {
            host.Children.Add(Root);
            Root.Opacity = 0;
            Ui.Fade(Root, 1, 200);
            panel.RenderTransformOrigin = new Point(0.5, 0.5);
            var s = Ui.Scaler(panel);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ui.Out() });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ui.Out() });
            Ui.Alert();
        }

        private void ShowFocus()
        {
            no.Focus(!onYesButton);
            yes.Focus(onYesButton);
            // the destructive answer turns red when chosen
            if (onYesButton) { yes.Root.SetResourceReference(Border.BackgroundProperty, "WarningBrush"); yes.Root.SetResourceReference(Border.BorderBrushProperty, "WarningBrush"); }
        }

        public void Handle(string b)
        {
            if (done) return;
            if ((b == "DPadLeft" && onYesButton) || (b == "DPadRight" && !onYesButton)) { onYesButton = !onYesButton; Ui.Tick(); ShowFocus(); return; }
            if (b == "A") Answer(onYesButton);
            else if (b == "B") Answer(false);
        }

        public void Answer(bool sure)
        {
            if (done) return;
            done = true;
            var host = Root.Parent as Panel;
            Ui.Fade(Root, 0, 160, 0, () => { if (host != null) host.Children.Remove(Root); });
            if (sure) onYes(); else { Ui.Back(); onNo(); }
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
        // true = shown over the dimmed home (menus) instead of on the login backdrop
        public virtual bool Overlay { get { return false; } }
    }
}
