using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // The controller's battery as a card (the "Controller battery" switch): once when a wireless controller is first seen after
    // Selene starts (its level), and once as it runs low (again at empty), from HomeNav's XInput poll ("none" | "wired" |
    // "0".."3"). XInput only: Xbox-type controllers, others through a wrapper like DS4Windows.
    public static class PadBattery
    {
        private static bool told;           // the start-up card shown this session
        private static int lowNoted = 9;    // the level the low card was shown for (9: none since the battery was up)

        public static void Show(string value)
        {
            try
            {
                int level;
                bool wireless = int.TryParse(value, out level);
                if (!wireless || !Features.On("padbattery")) { if (!wireless) lowNoted = 9; return; }
                if (!told)
                {
                    told = true;
                    ProgressToast.ShowPicture(() => Glyph(level));
                    if (level <= 1) lowNoted = level;   // (the start-up card said it already)
                    return;
                }
                // the card: once as the battery gets low, once more should it reach empty; not again until it has charged up
                if (level <= 1 && level < lowNoted) { lowNoted = level; ProgressToast.ShowPicture(() => Glyph(level)); }
                else if (level >= 2) lowNoted = 9;
            }
            catch (Exception ex) { HomeNav.Log("battery: " + ex.Message); }
        }

        // as a console's controller notice: the controller's outline, then a battery lying down with three bars inside, lit to the
        // level (XInput's four levels onto three bars: full 3, medium 2, low 1 in red, empty none with a red case)
        private static UIElement Glyph(int level)
        {
            bool low = level <= 1;
            var ink = Ink.Brush("SeleneInkColor");
            Brush bar = low ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : ink;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pad = Ui.Glyph(Ui.Icon(0xea28), Ui.Icons, 30, ink);   // sports_esports: the controller
            pad.VerticalAlignment = VerticalAlignment.Center;
            pad.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(pad);
            var battery = new Grid { Width = 44, Height = 22, VerticalAlignment = VerticalAlignment.Center };
            var bars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            int lit = level >= 3 ? 3 : level == 2 ? 2 : level == 1 ? 1 : 0;
            for (int i = 0; i < 3; i++)
                bars.Children.Add(new Border { Width = 8, Height = 12, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), Background = i < lit ? bar : Brushes.Transparent });
            battery.Children.Add(new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(5), BorderBrush = level == 0 ? bar : ink, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = bars });
            battery.Children.Add(new Border { Width = 3, Height = 10, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Background = ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(41, 0, 0, 0) });
            row.Children.Add(battery);
            return row;
        }
    }
}
