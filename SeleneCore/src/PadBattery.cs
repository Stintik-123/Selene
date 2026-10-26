using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // The controller's battery as a card (the "Controller battery" switch): once when a controller with a charge to read is first
    // seen after Selene starts, and once as it runs low (again at empty); the reading from PadPower (the pad's own report, the
    // Bluetooth figure, Gaming Input, XInput's buckets). A pad on a cable or a 2.4 GHz dongle has nothing to show.
    public static class PadBattery
    {
        private static bool told;           // the start-up card shown this session
        private static bool lowNoted;       // the low card shown, not again until the charge is back up

        public static void Show(PadPower.Reading r)
        {
            try
            {
                if (r == null || !r.Present) { lowNoted = false; return; }
                if (!Features.On("padbattery")) return;
                bool known = r.Percent >= 0 || r.Level >= 0;
                if (!known) return;   // wired, no charge to read
                if (!told)
                {
                    told = true;
                    ProgressToast.ShowPicture(() => Glyph(r));
                    if (r.Low) lowNoted = true;   // (the start-up card said it already)
                    return;
                }
                if (r.Low && !r.Charging && !lowNoted) { lowNoted = true; ProgressToast.ShowPicture(() => Glyph(r)); }
                else if (!r.Low) lowNoted = false;
            }
            catch (Exception ex) { HomeNav.Log("battery: " + ex.Message); }
        }

        // as a console's controller notice: the controller's outline, a battery lying down with three bars lit to the level (red
        // when low), the percentage when the source gives an exact one, a bolt while it charges
        private static UIElement Glyph(PadPower.Reading r)
        {
            var ink = Ink.Brush("SeleneInkColor");
            Brush bar = r.Low && !r.Charging ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : ink;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pad = Ui.Glyph(Ui.Icon(0xea28), Ui.Icons, 30, ink);   // sports_esports: the controller
            pad.VerticalAlignment = VerticalAlignment.Center;
            pad.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(pad);
            var battery = new Grid { Width = 44, Height = 22, VerticalAlignment = VerticalAlignment.Center };
            var bars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            int lit = r.Bars;
            for (int i = 0; i < 3; i++)
                bars.Children.Add(new Border { Width = 8, Height = 12, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), Background = i < lit ? bar : Brushes.Transparent });
            battery.Children.Add(new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(5), BorderBrush = lit == 0 ? bar : ink, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = bars });
            battery.Children.Add(new Border { Width = 3, Height = 10, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Background = ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(41, 0, 0, 0) });
            if (r.Charging)
            {
                var bolt = Ui.Glyph(Ui.Icon(0xea0b), Ui.Icons, 14, Ink.Brush("SeleneScrimColor"));   // bolt, over the bars
                bolt.HorizontalAlignment = HorizontalAlignment.Center; bolt.VerticalAlignment = VerticalAlignment.Center; bolt.Margin = new Thickness(0, 0, 4, 0);
                battery.Children.Add(bolt);
            }
            row.Children.Add(battery);
            if (r.Percent >= 0)
                row.Children.Add(new TextBlock { Text = r.Percent + "%", FontSize = 17, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 1) });
            return row;
        }
    }
}
