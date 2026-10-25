using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // A list that scrolls shows there's more: the rows fade into the dark at the bottom when more is below, and at the top
    // once it has scrolled.
    public static class PeekScroller
    {
        public const double Band = 110;

        public static Grid Wrap(ScrollViewer scroller)
        {
            var host = new Grid { ClipToBounds = true };
            host.Children.Add(scroller);
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            // top: edge, middle of the fade, where the rows are whole; bottom the same mirrored (eased, no hard line)
            var t0 = new GradientStop(Colors.Black, 0); var t1 = new GradientStop(Colors.Black, 0); var t2 = new GradientStop(Colors.Black, 0);
            var b0 = new GradientStop(Colors.Black, 1); var b1 = new GradientStop(Colors.Black, 1); var b2 = new GradientStop(Colors.Black, 1);
            foreach (var s in new[] { t0, t1, t2, b2, b1, b0 }) mask.GradientStops.Add(s);
            scroller.OpacityMask = mask;
            Action update = () =>
            {
                double h = scroller.ViewportHeight;
                if (h < 1) return;
                bool more = scroller.ScrollableHeight - scroller.VerticalOffset > 2;
                bool above = scroller.VerticalOffset > 2;
                double fb = Math.Min(0.45, Band / h), ft = Math.Min(0.3, Band * 0.6 / h);
                t0.Color = above ? Color.FromArgb(0, 0, 0, 0) : Colors.Black;
                t1.Color = above ? Color.FromArgb(0x70, 0, 0, 0) : Colors.Black;
                t1.Offset = above ? ft * 0.45 : 0;
                t2.Offset = above ? ft : 0;
                b0.Color = more ? Color.FromArgb(0, 0, 0, 0) : Colors.Black;
                b1.Color = more ? Color.FromArgb(0x70, 0, 0, 0) : Colors.Black;
                b1.Offset = more ? 1 - fb * 0.45 : 1;
                b2.Offset = more ? 1 - fb : 1;
            };
            scroller.ScrollChanged += (s, e) => update();
            scroller.SizeChanged += (s, e) => update();
            scroller.Loaded += (s, e) => update();
            return host;
        }
    }
}
