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
