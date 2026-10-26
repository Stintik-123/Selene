using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Selene
{
    // Shade over the art, set with the home in full view: a card low on the screen with a wide slider; Left / Right move it
    // and the shades behind follow at once, so the depth is picked by eye. A or B goes back to Selene settings, on that row.
    public class ShadePreviewScreen : Screen
    {
        private const double Length = 564;   // the slider's line (the card's inner width)

        private readonly bool fromMenu;
        private readonly Features.Option opt = Features.All.First(o => o.Key == "shade");
        private readonly Border card, fill;
        private readonly TextBlock value;
        private readonly TranslateTransform knobAt = new TranslateTransform();

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Brushes.Transparent; } }   // the home as it is: that's what's being set

        public ShadePreviewScreen(bool fromMenu)
        {
            this.fromMenu = fromMenu;
            var body = new StackPanel();

            // the name and the figure
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = "Shade over the art", FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
            value = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(value);
            body.Children.Add(head);

            // the line: its stops marked, the filled part to the knob
            var line = new Grid { Width = Length, Height = 26, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            line.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ink.Brush("SeleneInkColor", 0.16) });
            fill = new Border { Width = 0, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = Ink.Brush("GlyphColor") };
            line.Children.Add(fill);
            var stops = new Canvas { Height = 26 };
            for (int i = 0; i < opt.Choices.Length; i++)
            {
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Ink.Brush("SeleneInkColor", 0.45) };
                Canvas.SetLeft(dot, Length * i / (opt.Choices.Length - 1) - 3);
                Canvas.SetTop(dot, 10);
                stops.Children.Add(dot);
            }
            line.Children.Add(stops);
            var knob = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left, Background = Ink.Brush("SeleneInkColor"),
                RenderTransform = knobAt, Margin = new Thickness(-11, 0, 0, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.5, Color = Colors.Black }
            };
            line.Children.Add(knob);
            body.Children.Add(line);

            // the ends named
            var ends = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            ends.Children.Add(new TextBlock { Text = (opt.Names ?? opt.Choices)[0], FontSize = 13, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.5) });
            ends.Children.Add(new TextBlock { Text = (opt.Names ?? opt.Choices)[opt.Choices.Length - 1], FontSize = 13, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.5), HorizontalAlignment = HorizontalAlignment.Right });
            body.Children.Add(ends);

            // the buttons, in the card (the home's own hints stay where they are underneath)
            var hints = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            var adjust = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            adjust.Children.Add(Key("◀"));
            adjust.Children.Add(Key("▶"));
            adjust.Children.Add(new TextBlock { Text = "Adjust", FontSize = 19, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.9), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            hints.Children.Add(adjust);
            var done = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            done.Children.Add(Ui.Prompt("A", "Done"));
            done.Children.Add(Ui.Prompt("B", "Back"));
            hints.Children.Add(done);
            body.Children.Add(hints);

            card = new Border
            {
                Child = body, Width = Length + 56, CornerRadius = new CornerRadius(20), Padding = new Thickness(28, 22, 28, 20),
                Background = Ink.Brush("SeleneScrimColor", 0.78), BorderBrush = Ink.Brush("SeleneInkColor", 0.14), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 34, Opacity = 0.5, Color = Colors.Black },
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 120)
            };
            Root.Children.Add(card);
            Show(false);
        }

        // a key cap for the hints: a small rounded square with the arrow
        private static Border Key(string glyph)
        {
            return new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Ink.Brush("SeleneInkColor"), Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = glyph, FontSize = 12, Foreground = Ink.Brush("SeleneScrimColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) }
            };
        }

        public override void Shown()
        {
            if (!SeleneShell.CollectionActive) SeleneShell.ShowHome(true);   // the Spotlight behind (another page, the Store, has no shades to show)
            Ui.Rise(card, 0, 18, 280);
            Contrast.Apply();
        }

        private void Show(bool animate)
        {
            int at = Math.Max(0, Array.IndexOf(opt.Choices, Features.Value(opt.Key)));
            value.Text = Features.ValueName(opt.Key);
            double x = Length * at / (opt.Choices.Length - 1);
            if (!animate) { fill.BeginAnimation(FrameworkElement.WidthProperty, null); fill.Width = x; knobAt.BeginAnimation(TranslateTransform.XProperty, null); knobAt.X = x; return; }
            var ms = TimeSpan.FromMilliseconds(180);
            fill.BeginAnimation(FrameworkElement.WidthProperty, Ui.Fast(new DoubleAnimation(x, ms) { EasingFunction = Ui.Out() }));
            knobAt.BeginAnimation(TranslateTransform.XProperty, Ui.Fast(new DoubleAnimation(x, ms) { EasingFunction = Ui.Out() }));
            Ui.Pop(value);
        }

        public override void Handle(string b)
        {
            if (b == "DPadLeft" || b == "DPadRight")
            {
                int at = Math.Max(0, Array.IndexOf(opt.Choices, Features.Value(opt.Key))) + (b == "DPadLeft" ? -1 : 1);
                if (at < 0 || at >= opt.Choices.Length) return;   // the end of the line
                Features.SetValue(opt.Key, opt.Choices[at]);
                Ui.Tick();
                Show(true);
                Contrast.Apply();   // the shades behind follow at once
                return;
            }
            if (b == "A" || b == "B")
            {
                if (b == "A") Ui.Confirm(); else Ui.Back();
                LoginScene.Show(new SettingsScreen(fromMenu, "Spotlight", "shade"), -1);
            }
        }
    }
}
