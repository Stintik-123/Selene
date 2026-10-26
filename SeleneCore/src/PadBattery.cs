using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // The controller's battery beside the clock (the "Controller battery" switch): a small battery drawn into SeleneTopBattery
    // while a wireless controller is connected, from HomeNav's XInput poll ("none" | "wired" | "0".."3"), and a card once as
    // it runs low. XInput only: Xbox-type controllers, others through a wrapper like DS4Windows.
    public static class PadBattery
    {
        private static string drawn;        // what the glyph shows now
        private static int lowNoted = 9;    // the level the low card was shown for (9: none since the battery was up)

        public static void Show(string value)
        {
            try
            {
                int level;
                bool wireless = int.TryParse(value, out level);
                bool on = wireless && Features.On("padbattery");
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                var host = w != null ? HomeNav.FindByName(w, "SeleneTopBattery") as Grid : null;
                if (host != null)
                {
                    string want = on ? value : "off";
                    if (want != drawn || (on && host.Children.Count == 0))
                    {
                        host.Children.Clear();
                        if (on) host.Children.Add(Glyph(level));
                        host.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                        drawn = want;
                    }
                }
                // the card: once as the battery gets low, once more should it reach empty; not again until it has charged up
                if (on && level <= 1 && level < lowNoted) { lowNoted = level; ProgressToast.ShowNote("Controller", level == 0 ? "Battery empty" : "Battery low", 0xe19c); }
                else if (!wireless || level >= 2) lowNoted = 9;
            }
            catch (Exception ex) { HomeNav.Log("battery: " + ex.Message); }
        }

        // a battery lying down: the case, its nub, four cells inside filled to the level; red when low or empty
        private static UIElement Glyph(int level)
        {
            bool low = level <= 1;
            var ink = Ink.Brush("SeleneInkColor", 0.85);
            Brush fill = low ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : Ink.Brush("SeleneInkColor");
            var g = new Grid { Width = 36, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            var cells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            int lit = level >= 3 ? 4 : level == 2 ? 3 : level == 1 ? 1 : 0;
            for (int i = 0; i < 4; i++)
                cells.Children.Add(new Border { Width = 5, Height = 8, CornerRadius = new CornerRadius(1), Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), Background = i < lit ? fill : Brushes.Transparent });
            g.Children.Add(new Border { Width = 32, Height = 16, CornerRadius = new CornerRadius(4), BorderBrush = level == 0 ? fill : ink, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = cells });
            g.Children.Add(new Border { Width = 3, Height = 7, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Background = ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(33, 0, 0, 0) });
            return g;
        }
    }
}
