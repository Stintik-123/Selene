using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Selene
{
    // Framing your own picture for an avatar: the picture under a circle, the rest dimmed. The stick / d-pad moves it,
    // triggers (or X / Y, or - / + on a keyboard) zoom, A saves exactly what's in the circle, B cancels.
    public class AvatarCrop
    {
        private const double Size = 440;           // the square the circle sits in
        private const int Output = 512;            // saved avatar size (like the built-in ones)
        public readonly Grid Root;
        private readonly Grid frame;
        private readonly Image image;
        private readonly BitmapSource picture;
        private readonly Action<string> done;
        private readonly Action cancelled;
        private double scale, minScale, x, y;      // where the picture is going: size factor and top-left inside the square
        private double shownScale, shownX, shownY; // where it is drawn right now: eases toward the target every frame
        private DateTime lastFrame;
        private bool gliding;

        public static AvatarCrop Open(string file, Action<string> done, Action cancelled)
        {
            BitmapImage bmp;
            try
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 1600;
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();
            }
            catch (Exception ex) { HomeNav.Log("avatar crop: can't open " + file + ": " + ex.Message); return null; }
            return new AvatarCrop(bmp, done, cancelled);
        }

        private AvatarCrop(BitmapSource bmp, Action<string> done, Action cancelled)
        {
            picture = bmp;
            this.done = done;
            this.cancelled = cancelled;
            Root = new Grid { Background = Ink.Brush("SeleneScrimColor", 0.82) };
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(Ui.Title("Frame your picture"));
            col.Children.Add(Ui.Sub("Move it with the stick, zoom with the triggers."));

            frame = new Grid { Width = Size, Height = Size, Margin = new Thickness(0, 36, 0, 0), ClipToBounds = true };
            image = new Image { Source = bmp, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var canvas = new Canvas();
            canvas.Children.Add(image);
            frame.Children.Add(canvas);
            // everything outside the circle dimmed, and a ring on the circle
            var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
            outside.Children.Add(new RectangleGeometry(new Rect(0, 0, Size, Size)));
            outside.Children.Add(new EllipseGeometry(new Rect(0, 0, Size, Size)));
            frame.Children.Add(new System.Windows.Shapes.Path { Data = outside, Fill = Ink.Brush("SeleneScrimColor", 0.7) });
            frame.Children.Add(new Ellipse { Stroke = Ink.Brush("SeleneFocusColor"), StrokeThickness = 3 });
            var border = new Border { Child = frame, CornerRadius = new CornerRadius(18), BorderBrush = Ink.Brush("SeleneInkColor", 0.2), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(border);
            Root.Children.Add(col);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Cancel")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("LT", "Zoom out"), Ui.Prompt("RT", "Zoom in"), Ui.Prompt("A", "Use it")));

            // start: the picture just covers the circle, centred
            minScale = Size / Math.Min(bmp.PixelWidth, bmp.PixelHeight);
            scale = minScale;
            x = (Size - bmp.PixelWidth * scale) / 2;
            y = (Size - bmp.PixelHeight * scale) / 2;
            Place();
            shownScale = scale; shownX = x; shownY = y;
            Draw();
            Ui.Rise(col, 0, 14, 260);
        }

        // keeps the target inside the bounds (the picture always covers the square, so the circle is never empty)
        // and lets the drawn picture glide there
        private void Place()
        {
            double w = picture.PixelWidth * scale, h = picture.PixelHeight * scale;
            x = Math.Min(0, Math.Max(Size - w, x));
            y = Math.Min(0, Math.Max(Size - h, y));
            if (gliding) return;
            gliding = true;
            lastFrame = DateTime.Now;
            CompositionTarget.Rendering += Glide;
        }

        // every frame: a smooth ease toward the target (held stick or trigger = one continuous move)
        private void Glide(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            double dt = Math.Min(0.05, (now - lastFrame).TotalSeconds);
            lastFrame = now;
            double k = 1 - Math.Exp(-dt * 14);
            shownScale += (scale - shownScale) * k;
            shownX += (x - shownX) * k;
            shownY += (y - shownY) * k;
            if (Math.Abs(scale - shownScale) < 0.0005 && Math.Abs(x - shownX) < 0.3 && Math.Abs(y - shownY) < 0.3)
            {
                shownScale = scale; shownX = x; shownY = y;
                Stop();
            }
            Draw();
        }

        private void Stop()
        {
            if (!gliding) return;
            gliding = false;
            CompositionTarget.Rendering -= Glide;
        }

        private void Draw()
        {
            image.Width = picture.PixelWidth * shownScale;
            image.Height = picture.PixelHeight * shownScale;
            Canvas.SetLeft(image, shownX);
            Canvas.SetTop(image, shownY);
        }

        private void Zoom(double factor)
        {
            double next = Math.Max(minScale, Math.Min(minScale * 5, scale * factor));
            // zoom around the centre of the circle
            double cx = (Size / 2 - x) / scale, cy = (Size / 2 - y) / scale;
            scale = next;
            x = Size / 2 - cx * scale;
            y = Size / 2 - cy * scale;
            Place();
        }

        // true = handled
        public void Handle(string b)
        {
            const double step = 46;
            switch (b)
            {
                case "DPadLeft": x += step; Place(); return;
                case "DPadRight": x -= step; Place(); return;
                case "DPadUp": y += step; Place(); return;
                case "DPadDown": y -= step; Place(); return;
                case "TriggerLeft": case "LeftShoulder": case "X": Zoom(1 / 1.15); return;
                case "TriggerRight": case "RightShoulder": case "Y": Zoom(1.15); return;
                case "B": Stop(); Ui.Back(); cancelled(); return;
                case "A": case "Start": Save(); return;
            }
        }

        private void Save()
        {
            try
            {
                double k = Output / Size;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawImage(picture, new Rect(x * k, y * k, picture.PixelWidth * scale * k, picture.PixelHeight * scale * k));
                var target = new RenderTargetBitmap(Output, Output, 96, 96, PixelFormats.Pbgra32);
                target.Render(visual);
                var path = Profiles.NewPicturePath(".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Stop();
                Ui.Confirm();
                done(path);
            }
            catch (Exception ex)
            {
                HomeNav.Log("avatar crop: save failed: " + ex);
                Ui.Shake(frame);
            }
        }
    }
}
