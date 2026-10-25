// Playnite's message boxes (an add-on install, "Restart now?", errors, yes / no questions) dressed as Selene's own cards.
// The theme can only style the card, the title, the text and the buttons (theme\Views\MessageBox.xaml); Playnite lays them
// out itself, flush left like a desktop dialog. When one opens, this rebuilds its inside: an icon in a soft ring at the top,
// the title and text centred, an add-on's name, author and version set out as a little card of their own, the buttons
// centred, a glow of the theme's accent at the top, and the card rises in.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Selene
{
    public static class DialogLook
    {
        public static void Install()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var w = sender as Window;
            if (w == null || w.GetType().Name != "MessageBoxWindow" || !Features.On("dialogs")) return;
            try { Dress(w); }
            catch (Exception ex) { HomeNav.Log("dialog look: " + ex.Message); }
        }

        private static void Dress(Window w)
        {
            var buttons = w.FindName("StackButtons") as StackPanel;
            var panel = buttons != null ? buttons.Parent as StackPanel : null;
            var card = panel != null ? panel.Parent as Border : null;
            if (card == null || panel.Children.Count < 2) return;
            var caption = panel.Children[0] as TextBlock;
            var message = panel.Children[1] as TextBlock;
            if (caption == null || message == null) return;

            string title = caption.Text ?? "", text = message.Text ?? "";
            HomeNav.Log("dialog look: \"" + title + "\"");

            // the card: fixed, roomy width; its padding moves inside so the accent glow can reach its edges
            card.Padding = new Thickness(0);
            card.CornerRadius = new CornerRadius(28);
            card.Width = 680;
            card.MinWidth = 0;
            card.MaxWidth = double.PositiveInfinity;
            card.BorderBrush = Ink.Brush("SeleneInkColor", 0.12);
            card.Child = null;
            var inside = new Grid();
            var wash = new RadialGradientBrush { Center = new Point(0.5, -0.1), GradientOrigin = new Point(0.5, -0.1), RadiusX = 0.75, RadiusY = 0.7 };
            wash.GradientStops.Add(Ink.Stop("GlyphColor", 0.22, 0));
            wash.GradientStops.Add(Ink.Stop("GlyphColor", 0.06, 0.55));
            wash.GradientStops.Add(Ink.Stop("GlyphColor", 0, 1));
            inside.Children.Add(new Border { CornerRadius = new CornerRadius(28), Background = wash, IsHitTestVisible = false });
            panel.Margin = new Thickness(56, 44, 56, 40);
            inside.Children.Add(panel);
            card.Child = inside;

            // the icon in a ring, for what kind of question it is
            panel.Children.Insert(0, IconRing(KindOf(title, text, buttons)));

            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.TextAlignment = TextAlignment.Center;
            caption.FontSize = 32;
            caption.Margin = new Thickness(0, 18, 0, 10);
            caption.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;

            message.TextAlignment = TextAlignment.Center;
            message.HorizontalAlignment = HorizontalAlignment.Center;
            message.MaxWidth = 560;
            message.Opacity = 0.8;

            // "Do you want to install a new extension?" + a block of short lines (name, author, version): the block becomes a
            // card of its own with the name standing out
            string lead, block;
            if (SplitDetails(text, out lead, out block))
            {
                message.Text = lead;
                panel.Children.Insert(panel.Children.IndexOf(message) + 1, DetailsCard(block));
            }

            buttons.HorizontalAlignment = HorizontalAlignment.Center;
            buttons.Margin = new Thickness(0, 30, 0, 0);
            // the pills' right margin (for a left-aligned row) would push a centred row off centre
            foreach (var b in buttons.Children.OfType<FrameworkElement>()) b.Margin = new Thickness(7, 0, 7, 0);

            // in: the card rises and settles, a soft sound with it
            var scale = new ScaleTransform(0.96, 0.96);
            var move = new TranslateTransform(0, 22);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = group;
            card.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var t = TimeSpan.FromMilliseconds(260);
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, t) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, t) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, t) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, t) { EasingFunction = ease });
            if (Features.On("sounds")) Ui.Play("OpenPanel.wav", 0.6);
        }

        private enum Kind { Addon, Restart, Problem, Question, Note }

        private static Kind KindOf(string title, string text, StackPanel buttons)
        {
            var all = (title + " " + text).ToLowerInvariant();
            if (all.Contains("extension") || all.Contains("add-on") || all.Contains("addon") || all.Contains("theme")) return Kind.Addon;
            if (all.Contains("restart")) return Kind.Restart;
            if (all.Contains("error") || all.Contains("fail") || all.Contains("couldn't") || all.Contains("cannot") || all.Contains("can't")) return Kind.Problem;
            // a question: more than one button showing
            if (buttons.Children.OfType<UIElement>().Count(b => b.Visibility == Visibility.Visible) > 1) return Kind.Question;
            return Kind.Note;
        }

        private static FrameworkElement IconRing(Kind kind)
        {
            int code;
            const string tone = "GlyphColor";   // the theme's accent
            switch (kind)
            {
                case Kind.Addon: code = 0xE87B; break;     // extension
                case Kind.Restart: code = 0xE5D5; break;   // refresh
                case Kind.Problem: code = 0xE002; break;   // warning
                case Kind.Question: code = 0xE887; break;  // help
                default: code = 0xE88E; break;             // info
            }
            var halo = new Border
            {
                Width = 92, Height = 92, CornerRadius = new CornerRadius(46), IsHitTestVisible = false,
                Background = new RadialGradientBrush(Ink.Of(tone, 0.28), Ink.Of(tone, 0))
            };
            var ring = new Border
            {
                Width = 68, Height = 68, CornerRadius = new CornerRadius(34),
                Background = Ink.Brush(tone, 0.16), BorderBrush = Ink.Brush(tone, 0.45), BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = Ui.Icon(code), FontFamily = Ui.Icons, FontSize = 34, Foreground = Ink.Brush(tone),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -10) };
            g.Children.Add(halo);
            g.Children.Add(ring);
            return g;
        }

        // text = a sentence, an empty line, then 2-4 short lines (an add-on's name, author, version)
        private static bool SplitDetails(string text, out string lead, out string block)
        {
            lead = text; block = null;
            var cut = text.Replace("\r\n", "\n").LastIndexOf("\n\n", StringComparison.Ordinal);
            if (cut <= 0) return false;
            var norm = text.Replace("\r\n", "\n");
            var lines = norm.Substring(cut + 2).Split('\n').Select(l => l.Trim()).Where(l => l != "").ToArray();
            if (lines.Length < 2 || lines.Length > 4 || lines.Any(l => l.Length > 60)) return false;
            lead = norm.Substring(0, cut).Trim();
            block = string.Join("\n", lines);
            return true;
        }

        private static FrameworkElement DetailsCard(string block)
        {
            var lines = block.Split('\n');
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = lines[0], FontFamily = Ui.Display, FontSize = 24, FontWeight = FontWeights.SemiBold,
                Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("  ·  ", lines.Skip(1)), FontFamily = Ui.Body, FontSize = 17, Margin = new Thickness(0, 6, 0, 0),
                Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            });
            return new Border
            {
                Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(28, 18, 28, 18), CornerRadius = new CornerRadius(18),
                Background = Ink.Brush("SeleneInkColor", 0.06), BorderBrush = Ink.Brush("SeleneInkColor", 0.10), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 360,
                Child = stack
            };
        }
    }
}
