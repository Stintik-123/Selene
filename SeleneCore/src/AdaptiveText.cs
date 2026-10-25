// Readable text over any picture: for a block of text on the home or in Collection (the empty-tab message...), how bright
// the picture right behind it is gets measured (the wallpaper or the game art, cropped as the screen shows it), and the block
// gets a soft dark glow behind it and brighter words to match: nothing over a dark spot, a deep glow over a bright one.
// Measured again whenever the background changes. Part of the "Readable text" switch.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Selene
{
    public static class AdaptiveText
    {
        private static readonly List<KeyValuePair<FrameworkElement, Func<ImageSource>>> watched = new List<KeyValuePair<FrameworkElement, Func<ImageSource>>>();
        private static bool hooked;

        // keeps `block` readable over the picture `behind` returns (measured now, and again on every background change)
        public static void Watch(FrameworkElement block, Func<ImageSource> behind)
        {
            if (block == null) return;
            if (!watched.Any(p => ReferenceEquals(p.Key, block)))
            {
                watched.Add(new KeyValuePair<FrameworkElement, Func<ImageSource>>(block, behind));
                block.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Later(block, behind); };   // once per block
            }
            if (!hooked) { hooked = true; Contrast.Changed += RefreshAll; }
            Later(block, behind);
        }

        public static void RefreshAll()
        {
            foreach (var p in watched.ToList()) Later(p.Key, p.Value);
        }

        public static void Refresh(FrameworkElement block)
        {
            foreach (var p in watched) if (ReferenceEquals(p.Key, block)) Later(p.Key, p.Value);
        }

        // once laid out (its place on screen is known by then)
        private static void Later(FrameworkElement block, Func<ImageSource> behind)
        {
            block.Dispatcher.BeginInvoke(new Action(() => Apply(block, behind)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static void Apply(FrameworkElement block, Func<ImageSource> behind)
        {
            try
            {
                if (!block.IsVisible) return;
                double bright = Features.On("textshadow") ? BrightnessBehind(block, behind()) : 0;
                // 0 over a dark spot .. 1 over a bright one
                double k = Math.Max(0, Math.Min(1, (bright - 0.28) / 0.45));
                var scrim = Ink.Of("SeleneScrimColor");
                double a = 0.8 * k;
                var glow = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.62, RadiusY = 0.8 };
                glow.GradientStops.Add(new GradientStop(WithAlpha(scrim, a), 0));
                glow.GradientStops.Add(new GradientStop(WithAlpha(scrim, a * 0.72), 0.55));
                glow.GradientStops.Add(new GradientStop(WithAlpha(scrim, 0), 1));
                SetBackground(block, glow);
                // the quieter words (the hint under a title) come up toward full white over brighter spots
                foreach (var t in Texts(block))
                {
                    var tag = t.Tag as string;
                    if (tag == null) { var b = t.Foreground as SolidColorBrush; tag = b != null && b.Opacity < 0.8 ? "quiet" : "main"; t.Tag = tag; }
                    if (tag == "quiet") t.Foreground = Ink.Brush("SeleneInkColor", 0.6 + 0.35 * k);
                }
                HomeNav.Log("readable: " + block.Name + " over brightness " + bright.ToString("0.00") + " -> glow " + a.ToString("0.00"));
            }
            catch (Exception ex) { HomeNav.Log("readable: " + ex.Message); }
        }

        private static void SetBackground(FrameworkElement el, Brush b)
        {
            var panel = el as Panel;
            if (panel != null) { panel.Background = b; return; }
            var border = el as Border;
            if (border != null) { border.Background = b; return; }
            var text = el as TextBlock;
            if (text != null) text.Background = b;
        }

        private static IEnumerable<TextBlock> Texts(DependencyObject root)
        {
            var t = root as TextBlock;
            if (t != null) { yield return t; yield break; }
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                foreach (var c in Texts(VisualTreeHelper.GetChild(root, i))) yield return c;
        }

        private static Color WithAlpha(Color c, double a) { c.A = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))); return c; }

        // how bright (0..1, the brighter parts weighing most) the picture is right behind `el`, the picture filling the screen
        // the way the theme shows it (UniformToFill: centred, the overflow cut off)
        public static double BrightnessBehind(FrameworkElement el, ImageSource img)
        {
            var src = img as BitmapSource;
            var main = Application.Current.MainWindow;
            var root = main != null ? main.Content as FrameworkElement : null;
            if (src == null || root == null || root.ActualWidth < 1 || el.ActualWidth < 1) return 0;
            var r = el.TransformToVisual(root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            double W = root.ActualWidth, H = root.ActualHeight;
            double ia = (double)src.PixelWidth / src.PixelHeight, sa = W / H;
            double fx = 1, fy = 1, ox = 0, oy = 0;   // the part of the picture on screen, as fractions of it
            if (ia > sa) { fx = sa / ia; ox = (1 - fx) / 2; } else { fy = ia / sa; oy = (1 - fy) / 2; }
            double u0 = ox + Math.Max(0, r.Left / W) * fx, u1 = ox + Math.Min(1, r.Right / W) * fx;
            double v0 = oy + Math.Max(0, r.Top / H) * fy, v1 = oy + Math.Min(1, r.Bottom / H) * fy;
            int x = (int)(u0 * src.PixelWidth), y = (int)(v0 * src.PixelHeight);
            int w = Math.Max(1, (int)((u1 - u0) * src.PixelWidth)), h = Math.Max(1, (int)((v1 - v0) * src.PixelHeight));
            if (x + w > src.PixelWidth) w = src.PixelWidth - x;
            if (y + h > src.PixelHeight) h = src.PixelHeight - y;
            if (w < 1 || h < 1) return 0;
            BitmapSource part = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
            double scale = Math.Min(1, 48.0 / Math.Max(w, h));
            if (scale < 1) part = new TransformedBitmap(part, new ScaleTransform(scale, scale));
            part = new FormatConvertedBitmap(part, PixelFormats.Bgra32, null, 0);
            int pw = part.PixelWidth, ph = part.PixelHeight;
            var px = new byte[pw * ph * 4];
            part.CopyPixels(px, pw * 4, 0);
            var lums = new List<double>(pw * ph);
            for (int i = 0; i < px.Length; i += 4) lums.Add((0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2]) / 255.0);
            lums.Sort();
            // the brighter fifth counts: a bright sun behind half the words makes them unreadable however dark the rest is
            return lums[(int)(lums.Count * 0.8)];
        }
    }
}
