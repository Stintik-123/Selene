// Readable text over any picture, automatically. Every piece of text on the home and in Collection is measured against the
// picture right behind it (the wallpaper or the game art, cropped as the screen shows it): where it reads fine nothing is
// added; where the picture is bright, the text gets a soft dark halo that follows its letters (strong over a bright spot,
// light over a middling one) and quiet grey text comes up toward white. Measured again whenever the background changes.
// Part of the "Readable text" switch; text elsewhere (menus, login) keeps the plain light halo.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Selene
{
    public static class AdaptiveText
    {
        private static readonly List<WeakReference> texts = new List<WeakReference>();
        private static bool hooked;
        private static DispatcherTimer batch;

        // text on the home or in Collection (over the art): true when it is taken care of here
        public static bool Track(TextBlock t)
        {
            if (t == null || !OverTheArt(t)) return false;
            Hook();
            if (!texts.Any(r => ReferenceEquals(r.Target, t)))
            {
                texts.Add(new WeakReference(t));
                t.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Soon(); };
            }
            Soon();
            return true;
        }

        // a block of text (the empty-tab message...): every piece of text in it
        public static void Watch(FrameworkElement block, Func<ImageSource> unused)
        {
            if (block == null) return;
            Hook();
            Action all = () => { foreach (var t in Texts(block)) Track(t); };
            block.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) block.Dispatcher.BeginInvoke(all, DispatcherPriority.Loaded); };
            block.Dispatcher.BeginInvoke(all, DispatcherPriority.Loaded);
        }

        public static void Refresh(FrameworkElement block) { Soon(); }
        public static void RefreshAll() { Soon(); }

        private static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Contrast.Changed += Soon;
            batch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
            batch.Tick += (s, e) => { batch.Stop(); MeasureAll(); };
        }

        // measured in one go a moment later (a background change and many texts showing at once cost one pass)
        private static void Soon()
        {
            if (batch == null) return;
            batch.Stop();
            batch.Start();
        }

        private static void MeasureAll()
        {
            texts.RemoveAll(r => !(r.Target is TextBlock));
            foreach (var r in texts.ToList())
            {
                var t = r.Target as TextBlock;
                if (t != null && t.IsVisible) Measure(t);
            }
        }

        private static void Measure(TextBlock t)
        {
            try
            {
                if (!Features.On("textshadow")) { if (t.Effect is DropShadowEffect) t.Effect = null; return; }
                double bright = BrightnessBehind(t, PictureBehind(t));
                // 0 = reads fine .. 1 = bright behind it
                double k = Math.Max(0, Math.Min(1, (bright - 0.32) / 0.40));
                var q = quiet.GetValue(t, x => { var b = x.Foreground as SolidColorBrush; return new Quiet { Opacity = b != null && b.Opacity < 0.8 ? b.Opacity : -1 }; });
                double was = q.Opacity;
                if (was >= 0) t.Foreground = Ink.Brush("SeleneInkColor", was + (0.95 - was) * k);
                if (k < 0.08) { if (t.Effect is DropShadowEffect) t.Effect = null; return; }   // readable as it is: nothing added
                var halo = t.Effect as DropShadowEffect;
                if (halo == null || halo.IsFrozen) { halo = new DropShadowEffect { ShadowDepth = 0, RenderingBias = RenderingBias.Quality }; t.Effect = halo; }
                halo.Color = Ink.Of("SeleneScrimColor");
                halo.BlurRadius = 10 + 26 * k;         // a soft cloud that hugs the letters, wider over brighter spots
                halo.Opacity = 0.55 + 0.45 * k;
            }
            catch { }
        }

        // each text's own quietness (a grey hint's opacity; -1 for full-strength text), remembered before it is brightened
        private sealed class Quiet { public double Opacity; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, Quiet> quiet = new System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, Quiet>();

        // the picture behind a text: Collection's own (the wallpaper while it has no games), else the home's
        private static ImageSource PictureBehind(TextBlock t)
        {
            for (DependencyObject d = t; d != null; d = VisualTreeHelper.GetParent(d))
            {
                var fe = d as FrameworkElement;
                if (fe == null || fe.Name != "SeleneLibrary") continue;
                if (HomeNav.CollectionEmpty()) return Contrast.WallpaperImage;
                var art = HomeNav.FindByName(fe, "PART_ImageBackground");
                var src = art != null ? art.GetType().GetProperty("Source") : null;
                var img = src != null ? src.GetValue(art, null) as ImageSource : null;
                return img ?? Contrast.WallpaperImage;
            }
            return Contrast.Behind();
        }

        // on the home (Spotlight, its top bar) or in Collection
        private static bool OverTheArt(DependencyObject t)
        {
            for (DependencyObject d = t; d != null; d = VisualTreeHelper.GetParent(d))
            {
                var fe = d as FrameworkElement;
                if (fe == null) continue;
                if (fe.Name == "WelcomeHubOverlay" || fe.Name == "SeleneLibrary") return true;
                if (fe.Name == "AcceuilSettings" || fe is Window) return false;
            }
            return false;
        }

        private static IEnumerable<TextBlock> Texts(DependencyObject root)
        {
            var t = root as TextBlock;
            if (t != null) { yield return t; yield break; }
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                foreach (var c in Texts(VisualTreeHelper.GetChild(root, i))) yield return c;
        }

        // how bright (0..1, the brighter parts weighing most) the picture is right behind `el`, the picture filling the screen
        // the way the theme shows it (UniformToFill: centred, the overflow cut off)
        public static double BrightnessBehind(FrameworkElement el, ImageSource img)
        {
            var src = img as BitmapSource;
            var main = Application.Current.MainWindow;
            var root = main != null ? main.Content as FrameworkElement : null;
            if (src == null || root == null || root.ActualWidth < 1 || el.ActualWidth < 1) return 0;
            if (!el.IsDescendantOf(root)) return 0;
            var r = el.TransformToVisual(root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            double W = root.ActualWidth, H = root.ActualHeight;
            double ia = (double)src.PixelWidth / src.PixelHeight, sa = W / H;
            double fx = 1, fy = 1, ox = 0, oy = 0;
            if (ia > sa) { fx = sa / ia; ox = (1 - fx) / 2; } else { fy = ia / sa; oy = (1 - fy) / 2; }
            double u0 = ox + Math.Max(0, r.Left / W) * fx, u1 = ox + Math.Min(1, r.Right / W) * fx;
            double v0 = oy + Math.Max(0, r.Top / H) * fy, v1 = oy + Math.Min(1, r.Bottom / H) * fy;
            if (u1 <= u0 || v1 <= v0) return 0;
            int x = (int)(u0 * src.PixelWidth), y = (int)(v0 * src.PixelHeight);
            int w = Math.Max(1, (int)((u1 - u0) * src.PixelWidth)), h = Math.Max(1, (int)((v1 - v0) * src.PixelHeight));
            if (x + w > src.PixelWidth) w = src.PixelWidth - x;
            if (y + h > src.PixelHeight) h = src.PixelHeight - y;
            if (w < 1 || h < 1) return 0;
            BitmapSource part = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
            double scale = Math.Min(1, 40.0 / Math.Max(w, h));
            if (scale < 1) part = new TransformedBitmap(part, new ScaleTransform(scale, scale));
            part = new FormatConvertedBitmap(part, PixelFormats.Bgra32, null, 0);
            int pw = part.PixelWidth, ph = part.PixelHeight;
            var px = new byte[pw * ph * 4];
            part.CopyPixels(px, pw * 4, 0);
            var lums = new List<double>(pw * ph);
            for (int i = 0; i < px.Length; i += 4) lums.Add((0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2]) / 255.0);
            lums.Sort();
            return lums[(int)(lums.Count * 0.8)];
        }
    }
}
