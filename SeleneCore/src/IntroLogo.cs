using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Selene
{
    // The intro's logo, drawn and animated on its own thread (a HostVisual): Playnite builds its library, the home row and
    // decodes pictures on the main thread right at start-up, and the logo stuttered whenever that thread was busy. Here it
    // rises in, then its moon's halo keeps breathing and swelling, whatever the main thread is doing.
    public class IntroLogo : FrameworkElement
    {
        private readonly HostVisual host = new HostVisual();
        private readonly double size;
        private readonly Color ink, glyph;
        private Dispatcher logoThread;

        // the mark, and under it the name in the logo's lettering (theme\Images\wordmark.png, painted in the theme's ink)
        private const double LetterWidth = 0.92, LetterGap = 0.06;   // narrower than the mark: wider, the S fell outside the logo's box and was cut off
        private const double LetterRatio = 117.0 / 900.0;
        public static double HeightFor(double size) { return size * (1 + LetterGap + LetterWidth * LetterRatio); }

        public IntroLogo(double size)
        {
            this.size = size;
            Width = size;
            Height = HeightFor(size);
            ink = ColorOf("SeleneInkColor", Colors.White);
            glyph = ColorOf("GlyphColor", Color.FromRgb(0x9C, 0xCB, 0xFF));
            AddVisualChild(host);
        }

        private static Color ColorOf(string key, Color fallback)
        {
            var b = Ink.Brush(key) as SolidColorBrush;
            return b != null ? b.Color : fallback;
        }

        protected override int VisualChildrenCount { get { return 1; } }
        protected override Visual GetVisualChild(int index) { return host; }

        // builds the logo on its thread and starts the rise (after `delayMs`) and the glow
        public void Start(int delayMs)
        {
            if (logoThread != null) return;
            var ready = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                // never let anything here take Playnite down: an exception on this thread would end the whole app
                try
                {
                    var target = new VisualTarget(host);
                    var root = Build(delayMs);
                    root.Measure(new Size(size, HeightFor(size)));
                    root.Arrange(new Rect(0, 0, size, HeightFor(size)));
                    target.RootVisual = root;
                    logoThread = Dispatcher.CurrentDispatcher;
                    Dispatcher.CurrentDispatcher.UnhandledException += (s, e) => { e.Handled = true; };
                    ready.Set();
                    Dispatcher.Run();
                    target.Dispose();
                }
                catch (Exception ex) { ready.Set(); HomeNav.Log("intro logo thread: " + ex.Message); }
            }) { IsBackground = true, Name = "Selene intro logo" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            ready.WaitOne(2000);
        }

        private Grid rootEl;
        private TranslateTransform liftTr;

        // the way out: the logo fades as it lifts a little (on its own thread, like the rest of it)
        public void FadeOut(int ms)
        {
            var d = logoThread;
            if (d == null) return;
            d.BeginInvoke(new Action(() =>
            {
                if (rootEl == null) return;
                var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
                rootEl.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                if (liftTr != null) liftTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            }));
        }

        public void Stop()
        {
            var d = logoThread;
            logoThread = null;
            if (d != null) d.BeginInvokeShutdown(DispatcherPriority.Normal);
        }

        // the same drawing as Ui.Mark (72 x 72 units): the halo, the crescent, the ringed full moon
        private Grid Build(int delayMs)
        {
            var root = new Grid { Width = size, Height = HeightFor(size), Opacity = 0 };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootEl = root;
            var canvas = new Canvas { Width = 72, Height = 72 };
            var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
            var haloBrush = new RadialGradientBrush();
            haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB3, glyph.R, glyph.G, glyph.B), 0));
            haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x61, glyph.R, glyph.G, glyph.B), 0.3));
            haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1F, glyph.R, glyph.G, glyph.B), 0.65));
            haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, glyph.R, glyph.G, glyph.B), 1));
            var halo = new Ellipse { Width = 64, Height = 64, Fill = haloBrush, RenderTransformOrigin = new Point(0.5, 0.5) };
            Canvas.SetLeft(halo, 4); Canvas.SetTop(halo, -14);
            canvas.Children.Add(halo);
            var inkBrush = new SolidColorBrush(ink);
            canvas.Children.Add(new Path { Data = Geometry.Parse("M 9.109,20.577 A 31,31 0 1 0 62.891,20.577 A 27,27 0 1 1 9.109,20.577 Z"), Fill = inkBrush });
            var ring = new Ellipse { Width = 18, Height = 18, Stroke = inkBrush, StrokeThickness = 3.2 };
            Canvas.SetLeft(ring, 27); Canvas.SetTop(ring, 9);
            canvas.Children.Add(ring);
            var dot = new Ellipse { Width = 6.4, Height = 6.4, Fill = inkBrush };
            Canvas.SetLeft(dot, 32.8); Canvas.SetTop(dot, 14.8);
            canvas.Children.Add(dot);
            root.Children.Add(view);
            var letters = Lettering(inkBrush);
            if (letters != null) { Grid.SetRow(letters, 1); root.Children.Add(letters); }

            // rise in: fade and lift
            var lift = new TranslateTransform(0, 16);
            root.RenderTransform = lift;
            liftTr = lift;
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { BeginTime = begin, EasingFunction = ease });
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(900)) { BeginTime = begin, EasingFunction = ease });

            // the halo brightens, then keeps breathing and swelling
            var breathe = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            breathe.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            breathe.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            breathe.KeyFrames.Add(new EasingDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2100)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            breathe.Completed += (s, e) => halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(1100)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            halo.Opacity = 0.4;
            halo.BeginAnimation(UIElement.OpacityProperty, breathe);
            var swell = new ScaleTransform(1, 1);
            halo.RenderTransform = swell;
            var grow = new DoubleAnimation(0.94, 1.08, TimeSpan.FromMilliseconds(1100)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, BeginTime = begin };
            swell.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            swell.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            return root;
        }

        // the name under the mark: the ink through the lettering's picture, so it's the theme's color like the crescent
        private FrameworkElement Lettering(Brush inkBrush)
        {
            try
            {
                var file = SelenePaths.ThemeFile("Images", "wordmark.png");
                if (!System.IO.File.Exists(file)) return null;
                var pic = new System.Windows.Media.Imaging.BitmapImage();
                pic.BeginInit();
                pic.UriSource = new Uri(file);
                pic.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                pic.EndInit();
                pic.Freeze();
                double w = size * LetterWidth;
                return new Rectangle
                {
                    Width = w, Height = w * LetterRatio, Fill = inkBrush, Margin = new Thickness(0, size * LetterGap, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    OpacityMask = new ImageBrush(pic) { Stretch = Stretch.Fill }
                };
            }
            catch (Exception ex) { HomeNav.Log("intro lettering: " + ex.Message); return null; }
        }
    }
}
