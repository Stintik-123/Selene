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

        // a battery lying down, the card's whole word: the case, its nub, four cells inside filled to the level; red when low or empty
        private static UIElement Glyph(int level)
        {
            bool low = level <= 1;
            var ink = Ink.Brush("SeleneInkColor", 0.9);
            Brush fill = low ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : Ink.Brush("SeleneInkColor");
            var g = new Grid { Width = 38, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            int lit = level >= 3 ? 4 : level == 2 ? 3 : level == 1 ? 1 : 0;
            for (int i = 0; i < 4; i++)
                cells.Children.Add(new Border { Width = 5, Height = 8, CornerRadius = new CornerRadius(1), Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), Background = i < lit ? fill : Brushes.Transparent });
            g.Children.Add(new Border { Width = 33, Height = 18, CornerRadius = new CornerRadius(4), BorderBrush = level == 0 ? fill : ink, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = cells });
            g.Children.Add(new Border { Width = 3, Height = 8, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Background = ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(34, 0, 0, 0) });
            return g;
        }
    }
}
