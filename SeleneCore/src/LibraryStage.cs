using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Selene
{
    // The library's stage: rest on a game for a moment and its trailer plays in the card above the row (muted, looping),
    // with the game's song; moving on fades the card away and the default music comes back.
    public static class LibraryStage
    {
        private static ListBox list;
        private static FrameworkElement library;
        private static Border card;
        private static Grid host;
        private static MediaElement video;
        private static DispatcherTimer arm;

        public static void Attach(Window w)
        {
            if (list != null || w == null) return;
            list = HomeNav.FindByName(w, "PART_ListGameItems") as ListBox;
            library = HomeNav.FindByName(w, "SeleneLibrary");
            card = HomeNav.FindByName(w, "SeleneLibraryTrailerCard") as Border;
            host = HomeNav.FindByName(w, "SeleneLibraryTrailerHost") as Grid;
            if (list == null || library == null || card == null || host == null) { HomeNav.Log("library stage: parts not found"); list = null; return; }
            arm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
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
                string path; bool silent;
                if (game == null || !HomeNav.TrailerFor(game, out path, out silent)) return;
                var player = new MediaElement
                {
                    Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.UniformToFill, IsMuted = true, IsHitTestVisible = false   // the game's song plays instead
                };
                player.MediaEnded += (s, e) => { player.Position = TimeSpan.Zero; player.Play(); };
                player.MediaOpened += (s, e) =>
                {
                    if (video != player) return;
                    Show(true);
                    HomeNav.SetMusic("trailer");   // the game's song comes with its trailer
                };
                player.MediaFailed += (s, e) => { HomeNav.Log("library trailer failed: " + e.ErrorException.Message); Stop(); };
                video = player;
                host.Children.Add(player);
                player.Play();
            }
            catch (Exception ex) { HomeNav.Log("library trailer: " + ex.Message); }
        }

        public static void Stop()
        {
            if (arm != null) arm.Stop();
            var old = video;
            video = null;
            if (old != null)
            {
                Show(false, () => { old.Stop(); old.Source = null; host.Children.Remove(old); });
                if (library != null && library.IsVisible) HomeNav.SetMusic("home");
            }
        }

        // the card rises to full size and fades in (or out, then `done`)
        private static void Show(bool on, Action done = null)
        {
            var ms = TimeSpan.FromMilliseconds(on ? 380 : 200);
            var fade = new DoubleAnimation(on ? 1 : 0, ms) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (done != null) fade.Completed += (s, e) => done();
            card.BeginAnimation(UIElement.OpacityProperty, fade);
            var scale = card.RenderTransform as ScaleTransform;
            if (scale != null && !scale.IsFrozen)
            {
                var grow = new DoubleAnimation(on ? 1 : 0.96, ms) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }
    }
}
