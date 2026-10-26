using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Selene
{
    // A list that scrolls shows there's more, as the details page's lower rows do: the rows at the bottom edge (when more is
    // below) and the top edge (once it has scrolled) are blurred and fade into the dark.
    public static class PeekScroller
    {
        public const double Band = 110;

        public static Grid Wrap(ScrollViewer scroller, Panel rows)
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
                foreach (UIElement row in rows.Children)
                {
                    var fe = row as FrameworkElement;
                    if (fe == null || fe.ActualHeight < 1) continue;
                    double top = fe.TranslatePoint(new Point(0, 0), scroller).Y, bottom = top + fe.ActualHeight;
                    // only a row across an edge: the ones wholly past it are out of sight (blurring every row below the list,
                    // hundreds in a long one, made it slow)
                    bool soft = (more && bottom > h - Band * 0.5 && top < h) || (above && top < Band * 0.3 && bottom > 0);
                    Soften(fe, soft);
                }
            };
            updates.Add(scroller, update);
            scroller.ScrollChanged += (s, e) => update();
            scroller.SizeChanged += (s, e) => update();
            scroller.Loaded += (s, e) => update();
            rows.SizeChanged += (s, e) => update();
            return host;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, Action> updates = new System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, Action>();

        // the rows rearranged (an order change): which are at an edge, worked out again once they're laid out in their new
        // places (measured before, the old places left the wrong rows blurred)
        public static void Refresh(ScrollViewer scroller)
        {
            Action update;
            if (!updates.TryGetValue(scroller, out update)) return;
            scroller.UpdateLayout();
            update();
        }

        // which rows are blurred now (kept here: a row's Tag belongs to its screen)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> softened = new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object>();

        // in and out of the edge at once, as the list itself moves (fading either way flickered: each step a row went sharp
        // as the next one faded in); out of it the effect goes entirely (at radius 0 it still drew the words through it)
        private static void Soften(FrameworkElement row, bool on)
        {
            object state;
            bool now = softened.TryGetValue(row, out state);
            if (on == now) return;
            if (on)
            {
                softened.Add(row, true);
                row.Effect = new BlurEffect { Radius = 5, RenderingBias = RenderingBias.Performance };
            }
            else
            {
                softened.Remove(row);
                if (row.Effect is BlurEffect) row.Effect = null;
            }
        }
    }
}
