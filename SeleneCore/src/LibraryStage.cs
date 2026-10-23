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
        private static UIElement art, wallpaper, baseLayer;   // faded away while the trailer plays (black behind it)
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
            // Collection's own (game details has a PART_ImageBackground of its own, which was the one found before)
            art = HomeNav.FindByName(library, "PART_ImageBackground") as UIElement;
            // Playnite doesn't keep that name on its image: it's the FadeImage sitting right in Collection's own grid
            var grid = library as Panel;
            if (art == null && grid != null)
                foreach (UIElement child in grid.Children)
                    if (child.GetType().Name == "FadeImage") { art = child; break; }
            wallpaper = HomeNav.FindByName(library, "SeleneLibraryWallpaper") as UIElement;
            baseLayer = HomeNav.FindByName(library, "SeleneLibraryBase") as UIElement;
            HomeNav.Log("library stage: art " + (art != null) + ", wallpaper " + (wallpaper != null));
            if (list == null || library == null || host == null) { HomeNav.Log("library stage: parts not found"); list = null; return; }
            arm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };   // like Aniki: the art first, the trailer about 4 s in
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
                    // black cinema bars are measured while it's still hidden, so any crop is in place before it shows
                    CropBars(player, () =>
                    {
                        if (video != player) return;
                        Fade(true);
                        HomeNav.SetMusic("trailer");   // the game's song comes with its trailer
                        if (rsHint != null && videoHasSound) rsHint.Text = "Trailer sound";
                    });
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

        // Many trailers carry black cinema bars inside their 16:9 picture. Measured twice while the trailer is still hidden
        // (0.6 s and 0.9 s in); only when both agree is it zoomed, at once, just enough to push the bars off screen, so the
        // picture reaches the top edge. Then `show`. A trailer starting on black frames just isn't cropped (no zoom later).
        private static void CropBars(MediaElement player, Action show)
        {
            player.RenderTransformOrigin = new Point(0.5, 0.5);
            double first = 0;
            var look = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            look.Tick += (s, e) =>
            {
                if (video != player) { look.Stop(); return; }
                double scale = BarZoom(player);
                if (first == 0) { first = scale; look.Interval = TimeSpan.FromMilliseconds(300); return; }
                look.Stop();
                if (scale > 1.02 && Math.Abs(scale - first) < 0.03)
                {
                    player.RenderTransform = new ScaleTransform(scale, scale);
                    HomeNav.Log("library trailer: black bars, zoomed to " + scale.ToString("0.00"));
                }
                show();
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
                // a real bar ends on a sharp edge (a dark scene fades out instead): the first rows past it hold picture
                Func<int, double> mean = y => { double sum = 0; for (int x = 0; x < w; x++) { int i = (y * w + x) * 4; sum += px[i] + px[i + 1] + px[i + 2]; } return sum / (w * 3); };
                if (mean(Math.Min(h - 1, top + 1)) < 40 || mean(Math.Max(0, h - 2 - bottom)) < 40) return 1;
                return Math.Min(1.45, h / (double)(h - 2 * band));
            }
            catch { return 1; }
        }

        // entering Collection: its art fades in on its own, a moment after the screen (Playnite's image pops in without
        // a fade here, mid-way through the screen's fade-in)
        public static void FadeArtIn()
        {
            if (art == null) return;
            var keys = new DoubleAnimationUsingKeyFrames();
            keys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            keys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
            keys.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            keys.Completed += (s, e) => { if (video == null) art.BeginAnimation(UIElement.OpacityProperty, null); };
            art.BeginAnimation(UIElement.OpacityProperty, keys);
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
            // the game's art and the wallpaper step aside while the trailer plays (dark on the left), and come back after
            foreach (var el in new[] { art, wallpaper, baseLayer })
            {
                if (el == null) continue;
                if (on) el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else
                {
                    // fades back to its own opacity (1 for the art, the theme's for the wallpaper), then lets go
                    var target = el == wallpaper ? 0.6 : 1.0;
                    var back = new DoubleAnimation(target, TimeSpan.FromMilliseconds(300));
                    var who = el;
                    back.Completed += (s, e) => who.BeginAnimation(UIElement.OpacityProperty, null);
                    el.BeginAnimation(UIElement.OpacityProperty, back);
                }
            }
            host.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
