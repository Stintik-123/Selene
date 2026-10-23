using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Selene
{
    // The library's background: the selected game's art, and once you rest on a game its trailer takes over the whole
    // background (muted, looping) with the game's song. R3 swaps the song for the trailer's own sound. Moving on fades the
    // trailer away and the default music comes back.
    public static class LibraryStage
    {
        private static ListBox list;
        private static FrameworkElement library;
        private static Grid host;
        private static TextBlock rsHint;
        private static MediaElement video;
        private static bool videoHasSound, soundOn;
        private static DispatcherTimer arm;

        public static bool Playing { get { return video != null; } }

        public static void Attach(Window w)
        {
            if (list != null || w == null) return;
            list = HomeNav.FindByName(w, "PART_ListGameItems") as ListBox;
            library = HomeNav.FindByName(w, "SeleneLibrary");
            host = HomeNav.FindByName(w, "SeleneLibraryTrailerHost") as Grid;
            rsHint = HomeNav.FindByName(w, "SeleneLibraryRsHint") as TextBlock;
            if (list == null || library == null || host == null) { HomeNav.Log("library stage: parts not found"); list = null; return; }
            arm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            arm.Tick += (s, e) => { arm.Stop(); Play(); };
            list.SelectionChanged += (s, e) => Rearm();
            library.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Rearm(); else Stop(); };
        }

        private static void Rearm()
        {
            Stop();
            if (arm == null || !library.IsVisible) return;
            arm.Stop();
            arm.Start();
        }

        private static void Play()
        {
            try
            {
                if (!library.IsVisible || !Features.On("librarytrailer")) return;
                var w = Window.GetWindow(library);
                var details = w != null ? HomeNav.FindByName(w, "PART_ElemGameDetails") : null;
                if (details != null && details.IsVisible) return;
                var game = list.SelectedItem;
                string path; bool hasSound;
                if (game == null || !HomeNav.TrailerFor(game, out path, out hasSound)) return;
                var player = new MediaElement
                {
                    Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.UniformToFill, IsMuted = true, IsHitTestVisible = false   // the game's song plays instead (R3: its own sound)
                };
                player.MediaEnded += (s, e) => { player.Position = TimeSpan.Zero; player.Play(); };
                player.MediaOpened += (s, e) =>
                {
                    if (video != player) return;
                    Fade(true);
                    HomeNav.SetMusic("trailer");   // the game's song comes with its trailer
                    if (rsHint != null && videoHasSound) rsHint.Text = "Trailer sound";
                    CropBars(player);
                };
                player.MediaFailed += (s, e) => { HomeNav.Log("library trailer failed: " + e.ErrorException.Message); Stop(); };
                video = player;
                videoHasSound = hasSound;
                soundOn = false;
                host.Children.Add(player);
                player.Play();
            }
            catch (Exception ex) { HomeNav.Log("library trailer: " + ex.Message); }
        }

        // R3 while a trailer plays: its own sound instead of the game's song (and back)
        public static bool ToggleSound()
        {
            if (video == null || !videoHasSound) return false;
            soundOn = !soundOn;
            video.IsMuted = !soundOn;
            HomeNav.TrailerSound(soundOn);
            if (rsHint != null) rsHint.Text = soundOn ? "Trailer sound off" : "Trailer sound";
            Ui.Key();
            HomeNav.Log("library trailer sound " + (soundOn ? "on" : "off"));
            return true;
        }

        // Many trailers carry black cinema bars inside their 16:9 picture: looked at a few times while it plays (the first
        // seconds are often a fade from black), and if there are bars at the top and bottom the video is zoomed just
        // enough to push them off screen, so the picture reaches the top edge, behind the clock
        private static void CropBars(MediaElement player)
        {
            player.RenderTransformOrigin = new Point(0.5, 0.5);
            var zoom = new ScaleTransform(1, 1);
            player.RenderTransform = zoom;
            int[] at = { 1500, 3000, 6000 };
            int step = 0;
            var look = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(at[0]) };
            look.Tick += (s, e) =>
            {
                look.Stop();
                if (video != player) return;
                double scale = BarZoom(player);
                if (scale > zoom.ScaleX + 0.01)
                {
                    var grow = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                    zoom.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                    zoom.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                    HomeNav.Log("library trailer: black bars, zoomed to " + scale.ToString("0.00"));
                }
                if (++step < at.Length) { look.Interval = TimeSpan.FromMilliseconds(at[step] - at[step - 1]); look.Start(); }
            };
            look.Start();
        }

        // how much zoom hides the bars (1 = none): rows that are black right across, counted from the top and the bottom
        private static double BarZoom(MediaElement player)
        {
            try
            {
                const int w = 160, h = 90;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(player) { Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var px = new byte[w * h * 4];
                rtb.CopyPixels(px, w * 4, 0);
                Func<int, bool> dark = y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = (y * w + x) * 4;
                        if (px[i] > 28 || px[i + 1] > 28 || px[i + 2] > 28) return false;
                    }
                    return true;
                };
                int top = 0; while (top < h && dark(top)) top++;
                if (top >= h) return 1;   // a black frame (fading in): nothing to measure yet
                int bottom = 0; while (bottom < h && dark(h - 1 - bottom)) bottom++;
                int band = Math.Min(top, bottom);
                if (band < h * 0.05 || Math.Abs(top - bottom) > h * 0.05) return 1;   // no bars, or not even ones
                return Math.Min(1.45, h / (double)(h - 2 * band));
            }
            catch { return 1; }
        }

        public static void Stop()
        {
            if (arm != null) arm.Stop();
            var old = video;
            video = null;
            if (rsHint != null) rsHint.Text = "Filter";
            if (soundOn) { soundOn = false; HomeNav.TrailerSound(false); }
            if (old != null)
            {
                Fade(false);
                var gone = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };   // after the fade, whatever came next
                gone.Tick += (s, e) => { gone.Stop(); old.Stop(); old.Source = null; host.Children.Remove(old); };
                gone.Start();
                if (library != null && library.IsVisible) HomeNav.SetMusic("home");
            }
        }

        private static void Fade(bool on)
        {
            var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 700 : 250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            host.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
