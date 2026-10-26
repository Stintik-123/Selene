using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // Shade over the art, set with the home in full view: a small card low on the screen with the slider; Left / Right move it
    // and the shades behind follow at once, so the depth is picked by eye. A or B goes back to Selene settings, on that row.
    public class ShadePreviewScreen : Screen
    {
        private readonly bool fromMenu;
        private readonly SliderBar slider = new SliderBar();
        private readonly Features.Option opt = Features.All.First(o => o.Key == "shade");
        private readonly Border card;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Brushes.Transparent; } }   // the home as it is: that's what's being set

        public ShadePreviewScreen(bool fromMenu)
        {
            this.fromMenu = fromMenu;
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            words.Children.Add(new TextBlock { Text = "Shade over the art", FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor") });
            words.Children.Add(new TextBlock { Text = "Left and Right change it; the screen behind shows how it looks.", FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            var line = new DockPanel();
            DockPanel.SetDock(slider.Root, Dock.Right);
            line.Children.Add(slider.Root);
            line.Children.Add(words);
            card = new Border
            {
                Child = line, Width = 760, CornerRadius = new CornerRadius(16), Padding = new Thickness(26, 18, 24, 18), BorderThickness = new Thickness(2),
                BorderBrush = Ink.Brush("SeleneFocusColor"), Background = Ink.Brush("SeleneGlassColor", 0.92),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 150)
            };
            Root.Children.Add(card);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("A", "Done"), Ui.Prompt("B", "Back")));
            Show(false);
        }

        public override void Shown()
        {
            Ui.Rise(card, 0, 16, 260);
            Contrast.Apply();
        }

        private void Show(bool animate)
        {
            slider.Set(Math.Max(0, Array.IndexOf(opt.Choices, Features.Value(opt.Key))), opt.Choices.Length, Features.ValueName(opt.Key), animate);
        }

        public override void Handle(string b)
        {
            if (b == "DPadLeft" || b == "DPadRight")
            {
                int at = Math.Max(0, Array.IndexOf(opt.Choices, Features.Value(opt.Key))) + (b == "DPadLeft" ? -1 : 1);
                if (at < 0 || at >= opt.Choices.Length) { Ui.Shake(card); return; }   // the end of the line
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
