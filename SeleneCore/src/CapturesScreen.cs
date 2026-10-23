using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Selene
{
    // The Captures card: your screenshots and clips, newest first, from Steam's screenshot folders, Xbox Game Bar's
    // Videos\Captures, the NVIDIA app's Videos\NVIDIA and any folders added in Selene Core's settings. A opens one full
    // screen (clips play), Left / Right go through them there, B comes back to the grid, then to the home.
    public class CapturesScreen : Screen
    {
        public class Capture
        {
            public string Path, Game;
            public DateTime When;
            public bool IsVideo;
        }

        private const double Side = 96, TileW = 330, TileH = 186, PitchX = TileW + 22, PitchY = TileH + 64, Top = 190;
        private const int Columns = 5, Visible = 3, MaxItems = 150;
        private static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".bmp" };
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".mkv", ".avi", ".wmv" };
        private static readonly Regex DateTail = new Regex(@"\s*[\d]{4}[.\-_][\d]{2}[.\-_][\d]{2}.*$");

        private static readonly string[] Filters = { "All", "Screenshots", "Clips" };
        private static int filter;   // remembered while Playnite runs
        private readonly List<Capture> all = new List<Capture>();
        private readonly List<Border> tabs = new List<Border>();
        private readonly List<Capture> items = new List<Capture>();
        private readonly List<Tile> tiles = new List<Tile>();
        private readonly Grid page = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly TranslateTransform pageShift = new TranslateTransform();
        private readonly TextBlock note, caption;
        private readonly Grid viewer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
        private readonly Image picture = new Image { Stretch = Stretch.Uniform };
        private readonly TextBlock viewerName, viewerDate, timeText;
        private readonly Grid controls = new Grid { IsHitTestVisible = false };   // the words, the bar and the hints over a clip (hide after a while)
        private readonly Border progress = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        private readonly Border progressTrack;
        private readonly StackPanel clipHints, imageHints;
        private readonly DispatcherTimer hideControls = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer playerClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private MediaElement video;
        private WebTrailer clip;   // the browser playing a clip Playnite's own player couldn't (AV1, VP9)
        private int index, firstRow;
        private bool viewing, closed, paused, controlsShown;
        private double clipAt, clipLength;
        private readonly string openPath;   // opened from the card's pictures: straight to this one
        private readonly Image hero = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly DispatcherTimer heroLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        private string heroShown;

        public CapturesScreen(string openPath = null)
        {
            this.openPath = openPath;
            var dark = new Border();
            dark.SetResourceReference(Border.BackgroundProperty, "SeleneScrimBrush");
            Panel.SetZIndex(dark, -10);
            Root.Children.Add(dark);
            // the focused capture's picture, soft and faint, behind everything; a vignette keeps the edges dark
            RenderOptions.SetBitmapScalingMode(hero, BitmapScalingMode.LowQuality);
            hero.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 40, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
            Panel.SetZIndex(hero, -6);
            Root.Children.Add(hero);
            var vignette = new Border { IsHitTestVisible = false, Background = new RadialGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0.35, 0), Ink.Stop("SeleneScrimColor", 0.6, 0.6), Ink.Stop("SeleneScrimColor", 0.92, 1) }) { RadiusX = 0.78, RadiusY = 0.85 } };
            Panel.SetZIndex(vignette, -5);
            Root.Children.Add(vignette);
            heroLater.Tick += (s, e) => { heroLater.Stop(); ShowHero(); };

            // all / screenshots / clips: pills up top (LB / RB)
            var tabBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 36, 0, 0) };
            tabBar.Children.Add(Ui.Prompt("LB", ""));
            foreach (var f in Filters)
            {
                var pill = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 6, 18, 8), Margin = new Thickness(6, 0, 6, 0), Child = new TextBlock { Text = f, FontSize = 19, FontFamily = Ui.Body } };
                tabs.Add(pill);
                tabBar.Children.Add(pill);
            }
            tabBar.Children.Add(Ui.Prompt("RB", ""));
            Root.Children.Add(tabBar);
            ShowTabs();

            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 56, 0, 0) };
            head.Children.Add(new TextBlock { Text = "Captures", FontSize = 44, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor") });
            caption = new TextBlock { Text = "Looking for your screenshots and clips…", FontSize = 20, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), Margin = new Thickness(2, 6, 0, 0) };
            head.Children.Add(caption);
            Root.Children.Add(head);
            Ui.Rise(head, 40, 14, 320);

            // the grid on a clipped area: the focused row glides into view
            var area = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Top - 24, 0, 0), Height = Visible * PitchY + 24, ClipToBounds = true };
            double h = Visible * PitchY + 24;
            var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, h) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 24 / h));
            fade.GradientStops.Add(new GradientStop(Colors.Black, (h - 70) / h));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            area.OpacityMask = fade;
            page.RenderTransform = pageShift;
            page.Margin = new Thickness(Side, 24, 0, 0);
            area.Children.Add(new Canvas { Children = { page } });
            Root.Children.Add(area);

            note = new TextBlock { FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 240, 0, 0), MaxWidth = 1100, TextWrapping = TextWrapping.Wrap };
            Root.Children.Add(note);

            // the viewer: one capture on the whole screen; over it, the game and date at the bottom left, a clip's bar with its
            // time, and the hints, all fading away after a moment (any press brings them back)
            viewer.SetResourceReference(Panel.BackgroundProperty, "SeleneScrimBrush");
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            viewer.Children.Add(picture);
            var shade = new Border { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Bottom, Height = 300, Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xB8, 0, 0, 0), 1) }, 90) };
            controls.Children.Add(shade);
            var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Side, 0, 0, 132) };
            viewerName = new TextBlock { FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Brushes.White, MaxWidth = 1200, TextTrimming = TextTrimming.CharacterEllipsis };
            viewerDate = new TextBlock { FontSize = 17, FontFamily = Ui.Body, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(1, 4, 0, 0) };
            words.Children.Add(viewerName);
            words.Children.Add(viewerDate);
            controls.Children.Add(words);
            // the bar: where the clip is, with the time at its right end
            progressTrack = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Side, 0, 0, 104), Width = 1920 - 2 * Side - 150 };
            progress.Background = Ink.Brush("SeleneFocusColor");
            progressTrack.Child = progress;
            controls.Children.Add(progressTrack);
            timeText = new TextBlock { FontSize = 17, FontFamily = Ui.Body, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, Side, 96) };
            controls.Children.Add(timeText);
            clipHints = Ui.Prompts(true, Ui.Prompt("LB RB", "Previous / next"), Ui.Prompt("\u25C0 \u25B6", "10 seconds"), Ui.Prompt("A", "Pause"));
            imageHints = Ui.Prompts(true, Ui.Prompt("\u25C0 \u25B6", "Previous / next"));
            controls.Children.Add(clipHints);
            controls.Children.Add(imageHints);
            controls.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            viewer.Children.Add(controls);
            Panel.SetZIndex(viewer, 20);
            Root.Children.Add(viewer);
            hideControls.Tick += (s, e) => { hideControls.Stop(); if (!paused) ShowControls(false); };
            playerClock.Tick += (s, e) => PollPlayer();

            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back"), Ui.Prompt("LB RB", "Filter")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Open")));
        }

        public override void Shown()
        {
            var dispatcher = Root.Dispatcher;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                var found = Scan(MaxItems);
                dispatcher.BeginInvoke(new Action(() => { if (closed) return; all.Clear(); all.AddRange(found); Apply(); }), DispatcherPriority.Background);
            });
        }

        public override void Hidden() { closed = true; StopVideo(); hideControls.Stop(); playerClock.Stop(); heroLater.Stop(); }

        // ---- the words, bar and hints over the viewer ----
        private void ShowControls(bool on)
        {
            controlsShown = on;
            Ui.Fade(controls, on ? 1 : 0, on ? 160 : 400);
            hideControls.Stop();
            if (on) hideControls.Start();
        }

        private static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        private void ShowProgress(double at, double length, bool isPaused)
        {
            clipAt = at; clipLength = length;
            if (paused != isPaused) { paused = isPaused; ShowControls(true); }
            double w = Math.Max(0, progressTrack.Width);
            progress.Width = length > 0 ? Math.Max(0, Math.Min(w, w * at / length)) : 0;
            timeText.Text = Clock(at) + (length > 0 ? " / " + Clock(length) : "");
        }

        // Playnite's own player has no events for where it is: asked a few times a second
        private void PollPlayer()
        {
            var me = video;
            if (me == null) return;
            try { ShowProgress(me.Position.TotalSeconds, me.NaturalDuration.HasTimeSpan ? me.NaturalDuration.TimeSpan.TotalSeconds : 0, paused); } catch { }
        }

        private void TogglePause()
        {
            if (clip != null) { clip.TogglePause(); return; }
            var me = video;
            if (me == null) return;
            try { if (paused) me.Play(); else me.Pause(); } catch { }
            paused = !paused;
            ShowControls(true);
        }

        private void Seek(double seconds)
        {
            if (clip != null) { clip.Seek(seconds); return; }
            var me = video;
            if (me == null) return;
            try
            {
                double length = me.NaturalDuration.HasTimeSpan ? me.NaturalDuration.TimeSpan.TotalSeconds : 0;
                double to = Math.Max(0, me.Position.TotalSeconds + seconds);
                if (length > 0) to = Math.Min(length - 0.3, to);
                me.Position = TimeSpan.FromSeconds(to);
                ShowProgress(to, length, paused);
            }
            catch { }
        }

        private void ShowTabs()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = i == filter;
                tabs[i].Background = on ? (Brush)Ink.Brush("SeleneFocusColor", 0.95) : Brushes.Transparent;
                var label = (TextBlock)tabs[i].Child;
                label.Foreground = on ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor", 0.7);
                label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // the grid as the filter wants it
        private void Apply()
        {
            Fill(filter == 1 ? all.Where(c => !c.IsVideo).ToList() : filter == 2 ? all.Where(c => c.IsVideo).ToList() : all.ToList());
        }

        private void Fill(List<Capture> found)
        {
            items.Clear();
            items.AddRange(found);
            page.Children.Clear();
            tiles.Clear();
            int shots = items.Count(c => !c.IsVideo), clips = items.Count - shots;
            caption.Text = items.Count == 0 ? "Screenshots and clips from your games" : Count(shots, "screenshot") + (clips > 0 ? " · " + Count(clips, "clip") : "") + ", newest first";
            if (items.Count == 0)
            {
                note.Text = all.Count > 0 ? (filter == 1 ? "No screenshots here. LB / RB show the clips, or everything." : "No clips here. LB / RB show the screenshots, or everything.")
                    : "No screenshots or clips found. Selene looks in Steam's screenshot folders, Videos\\Captures (Xbox Game Bar), Videos\\NVIDIA (the NVIDIA app) and the folders you add under Captures in Selene Core's settings.";
                return;
            }
            note.Text = "";
            var files = new List<string>();
            var at = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                var c = items[i];
                var t = new Tile(TileW, TileH, 12, 5) { FocusScale = 1.05 };
                var item = new StackPanel { Width = TileW, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness((i % Columns) * PitchX, (i / Columns) * PitchY, 0, 0) };
                item.Children.Add(t.Root);
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 0) };
                if (c.IsVideo) line.Children.Add(new TextBlock { Text = "\uE714", FontFamily = Ui.Glyphs, FontSize = 13, Foreground = Ink.Brush("SeleneInkColor", 0.8), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
                line.Children.Add(new TextBlock { Text = c.Game, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = TileW - 90 });
                line.Children.Add(new TextBlock { Text = "  " + GameFacts.Ago(c.When), FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
                item.Children.Add(line);
                if (c.IsVideo)
                {
                    // a clip: a film mark on the glass face until its still is made (Windows' thumbnail, or ffmpeg's)
                    var g = new Grid();
                    g.Children.Add(new TextBlock { Text = "\uE768", FontFamily = Ui.Glyphs, FontSize = 40, Foreground = Ink.Brush("SeleneInkColor", 0.85), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    g.Children.Add(new TextBlock { Text = System.IO.Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant() + " clip", FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14) });
                    t.Face.Child = g;
                    var tile = t;
                    Thumbnails.Request(c.Path, still =>
                    {
                        if (closed || still == null) return;
                        Ui.LoadInOrder(new List<string> { still }, 420, (k, pic) =>
                        {
                            if (closed) return;
                            var face = new Grid();
                            face.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } });
                            face.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Padding = new Thickness(7, 3, 7, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0), Child = new TextBlock { Text = "\uE768", FontFamily = Ui.Glyphs, FontSize = 13, Foreground = Brushes.White } });
                            face.Opacity = 0;
                            tile.Face.Child = face;
                            Ui.Fade(face, 1, 240);
                        });
                    });
                }
                else { files.Add(c.Path); at.Add(i); }
                page.Children.Add(item);
                tiles.Add(t);
                if (i < Columns * Visible) Ui.Rise(item, 60 + i * 35, 14, 300);
            }
            Ui.LoadInOrder(files, 420, (i, pic) =>
            {
                if (closed || at[i] >= tiles.Count) return;
                var fill = new Border { CornerRadius = new CornerRadius(11), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill }, Opacity = 0 };
                tiles[at[i]].Face.Child = fill;
                Ui.Fade(fill, 1, 240);
            });
            index = 0;
            if (openPath != null) { int at0 = items.FindIndex(c => string.Equals(c.Path, openPath, StringComparison.OrdinalIgnoreCase)); if (at0 >= 0) index = at0; }
            ShowFocus(false);
            if (openPath != null && index < items.Count && string.Equals(items[index].Path, openPath, StringComparison.OrdinalIgnoreCase)) Open(index);
        }

        private static string Count(int n, string what) { return n + " " + what + (n == 1 ? "" : "s"); }

        // the focused capture behind, once the focus rests on it (a still for a clip, the picture itself for a screenshot)
        private void ShowHero()
        {
            if (index >= items.Count || viewing) return;
            var c = items[index];
            var path = c.IsVideo ? Thumbnails.Cached(c.Path) : c.Path;
            if (path == heroShown) return;
            heroShown = path;
            if (path == null) { Ui.Fade(hero, 0, 300); return; }
            Ui.LoadInOrder(new List<string> { path }, 480, (k, pic) =>
            {
                if (closed || path != heroShown) return;
                hero.Source = pic;
                hero.Opacity = 0;
                Ui.Fade(hero, 0.55, 600);
            });
        }

        private void ShowFocus(bool animate)
        {
            heroLater.Stop();
            heroLater.Start();
            for (int i = 0; i < tiles.Count; i++) tiles[i].Focus(i == index);
            int row = index / Columns, rows = (tiles.Count + Columns - 1) / Columns;
            // the focused row stays in the middle of the three on screen, within the grid's ends
            int first = Math.Max(0, Math.Min(row - 1, rows - Visible));
            if (first == firstRow && animate) return;
            firstRow = first;
            pageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-first * PitchY, TimeSpan.FromMilliseconds(animate ? 260 : 0)) { EasingFunction = Ui.Out() });
        }

        // ---- the viewer ----
        private void Open(int i)
        {
            if (i < 0 || i >= items.Count) return;
            index = i;
            var c = items[i];
            StopVideo();
            picture.Source = null;
            viewerName.Text = c.Game;
            viewerDate.Text = c.When.ToString("MMMM d, yyyy  ·  HH:mm");
            paused = false;
            clipAt = 0; clipLength = 0;
            progress.Width = 0;
            timeText.Text = "";
            progressTrack.Visibility = timeText.Visibility = clipHints.Visibility = c.IsVideo ? Visibility.Visible : Visibility.Collapsed;
            imageHints.Visibility = c.IsVideo ? Visibility.Collapsed : Visibility.Visible;
            ShowControls(true);
            if (c.IsVideo)
            {
                var ext = System.IO.Path.GetExtension(c.Path).ToLowerInvariant();
                if (ext == ".mkv" || ext == ".avi")
                {
                    // Playnite's player can't show these: the PC's own player, Selene waits here
                    try { System.Diagnostics.Process.Start(c.Path); } catch (Exception ex) { HomeNav.Log("captures: open failed: " + ex.Message); }
                    return;
                }
                // Playnite's own player first (H.264 from Xbox Game Bar); a clip it can't decode (AV1 from the NVIDIA app) goes to
                // the browser Playnite ships with, as the Store trailers do
                var me = new MediaElement { Source = new Uri(c.Path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, Volume = 0.8 };
                video = me;
                me.MediaEnded += (s, e) => { try { me.Position = TimeSpan.Zero; me.Play(); } catch { } };
                me.MediaFailed += (s, e) =>
                {
                    HomeNav.Log("captures: clip failed in the player (" + e.ErrorException.Message + "): the browser");
                    if (!ReferenceEquals(video, me) || !viewing) return;
                    StopVideo();
                    var path = c.Path;
                    clip = WebTrailer.CreateFile(path, null);
                    if (clip == null) { viewerDate.Text = "This clip can't be played here."; return; }
                    clip.Progress = (at, length, isPaused) => { if (viewing && clip != null) ShowProgress(at, length, isPaused); };
                    viewer.Children.Insert(1, clip.View);
                };
                viewer.Children.Insert(1, me);
                me.Play();
                playerClock.Start();
            }
            else
            {
                Ui.LoadInOrder(new List<string> { c.Path }, 1920, (k, pic) => { if (viewing && index == i) picture.Source = pic; });
            }
            if (!viewing)
            {
                viewing = true;
                viewer.Visibility = Visibility.Visible;
                Ui.Fade(viewer, 1, 220);
                for (int k = 0; k < tiles.Count; k++) tiles[k].Focus(false);
            }
        }

        private void CloseViewer()
        {
            viewing = false;
            StopVideo();
            Ui.Fade(viewer, 0, 200, 0, () => { viewer.Visibility = Visibility.Collapsed; picture.Source = null; });
            ShowFocus(true);
        }

        private void StopVideo()
        {
            playerClock.Stop();
            if (clip != null) { clip.Stop(); clip = null; }
            if (video == null) return;
            try { video.Stop(); video.Close(); } catch { }
            viewer.Children.Remove(video);
            video = null;
        }

        public override void Handle(string b)
        {
            if (viewing)
            {
                if (b == "B") { Ui.Back(); CloseViewer(); return; }
                bool isClip = index < items.Count && items[index].IsVideo;
                if (!controlsShown) ShowControls(true); else hideControls.Stop();
                hideControls.Start();
                if (b == "A" && isClip) { Ui.Tick(); TogglePause(); return; }
                if ((b == "DPadLeft" || b == "DPadRight") && isClip) { Ui.Tick(); Seek(b == "DPadRight" ? 10 : -10); return; }
                if (b == "DPadLeft" || b == "DPadRight" || b == "LeftShoulder" || b == "RightShoulder")
                {
                    int next = index + (b == "DPadRight" || b == "RightShoulder" ? 1 : -1);
                    if (next < 0 || next >= items.Count) return;
                    Ui.Tick();
                    Open(next);
                }
                return;
            }
            if (b == "B") { Ui.Back(); LoginScene.Finish(); return; }
            if (b == "LeftShoulder" || b == "RightShoulder")
            {
                filter = (filter + (b == "RightShoulder" ? 1 : Filters.Length - 1)) % Filters.Length;
                Ui.Tick();
                ShowTabs();
                Ui.Pop(tabs[filter]);
                firstRow = -1;
                Apply();
                page.Opacity = 0;
                Ui.Fade(page, 1, 220);
                return;
            }
            if (tiles.Count == 0) return;
            if (b == "A") { Ui.Confirm(); Open(index); return; }
            if (!b.StartsWith("DPad")) return;
            int to = Ui.GridMove(index, b, tiles.Count, Columns);
            if (to < 0) { Ui.Shake(tiles[index].Root); return; }
            index = to;
            Ui.Tick();
            ShowFocus(true);
        }

        // ---------------- finding them ----------------
        private const string SteamPlugin = "cb91dfc9-b977-43bf-8e70-55f46e410fab";

        // the last full scan (the card's count makes one at start): what the details page shows for a game
        public static List<Capture> Known = new List<Capture>();

        public static List<Capture> Scan(int max)
        {
            var list = new List<Capture>();
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            try
            {
                // Steam: userdata\<account>\760\remote\<app>\screenshots
                var steamNames = SteamNames();
                var userdata = SelenePaths.SteamUserdata;
                if (Directory.Exists(userdata))
                    foreach (var account in Directory.GetDirectories(userdata))
                    {
                        var remote = Path.Combine(account, "760", "remote");
                        if (!Directory.Exists(remote)) continue;
                        foreach (var app in Directory.GetDirectories(remote))
                        {
                            var shots = Path.Combine(app, "screenshots");
                            if (!Directory.Exists(shots)) continue;
                            string name;
                            if (!steamNames.TryGetValue(Path.GetFileName(app), out name)) name = "Steam";
                            Add(list, Directory.GetFiles(shots), name);
                        }
                    }
            }
            catch (Exception ex) { HomeNav.Log("captures: steam: " + ex.Message); }
            // Xbox Game Bar: Videos\Captures ("Game Name 2026-10-23 15-39-06.png")
            Folder(list, Path.Combine(videos, "Captures"), null, false);
            // the NVIDIA app: Videos\NVIDIA\<game>\, and the older Videos\<game>\
            Folder(list, Path.Combine(videos, "NVIDIA"), null, true);
            try
            {
                if (Directory.Exists(videos))
                    foreach (var dir in Directory.GetDirectories(videos))
                    {
                        var n = Path.GetFileName(dir);
                        if (n.Equals("Captures", StringComparison.OrdinalIgnoreCase) || n.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)) continue;
                        Add(list, Directory.GetFiles(dir), n);
                    }
            }
            catch (Exception ex) { HomeNav.Log("captures: videos: " + ex.Message); }
            // folders added in Selene Core's settings (files in them and one level of subfolders)
            foreach (var extra in SelenePaths.CapturesFolders) Folder(list, extra, null, true);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return list.Where(c => seen.Add(c.Path)).OrderByDescending(c => c.When).Take(max).ToList();
        }

        private static void Folder(List<Capture> list, string folder, string name, bool subfolders)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
                Add(list, Directory.GetFiles(folder), name);
                if (!subfolders) return;
                foreach (var dir in Directory.GetDirectories(folder)) Add(list, Directory.GetFiles(dir), Path.GetFileName(dir));
            }
            catch (Exception ex) { HomeNav.Log("captures: " + folder + ": " + ex.Message); }
        }

        private static void Add(List<Capture> list, string[] files, string name)
        {
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                bool video = VideoExt.Contains(ext);
                if (!video && !ImageExt.Contains(ext)) continue;
                var stem = Path.GetFileNameWithoutExtension(f);
                // "Game Name 2026-10-23 15-39-06" / "Desktop 2026.10.23 - 15.39.06.47": the game is the part before the date
                var game = name ?? DateTail.Replace(stem, "").Trim();
                if (string.IsNullOrEmpty(game)) game = "Capture";
                DateTime when;
                try { when = File.GetLastWriteTime(f); } catch { when = DateTime.MinValue; }
                list.Add(new Capture { Path = f, Game = game, When = when, IsVideo = video });
            }
        }

        // Steam app ids -> names, from the games Playnite knows
        private static Dictionary<string, string> SteamNames()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (HomeNav.Api == null) return d;
                foreach (var g in HomeNav.Api.Database.Games)
                    if (g.PluginId.ToString() == SteamPlugin && !string.IsNullOrEmpty(g.GameId) && !d.ContainsKey(g.GameId)) d[g.GameId] = g.Name;
            }
            catch { }
            return d;
        }

        // the Captures card: the numbers ("128 screenshots · 12 clips"), what's newest, and the latest four as pictures
        // (Down goes into them, A opens one); found off the UI thread
        public static void CountInBackground()
        {
            var dispatcher = Application.Current.Dispatcher;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                string text, headline, about;
                var latest = new List<Capture>();
                try
                {
                    var all = Scan(int.MaxValue);
                    Known = all;
                    int clips = all.Count(c => c.IsVideo), shots = all.Count - clips;
                    text = all.Count == 0 ? "None found yet" : Count(shots, "screenshot") + (clips > 0 ? " · " + Count(clips, "clip") : "");
                    latest = all.Take(4).ToList();
                    foreach (var c in latest) if (c.IsVideo) Thumbnails.Get(c.Path, 12000);   // stills for the card's clips (made here, off the UI thread)
                    if (all.Count == 0)
                    {
                        headline = "No captures yet";
                        about = "Screenshots and clips from Steam, Xbox Game Bar, the NVIDIA app and your own folders show here.";
                    }
                    else
                    {
                        // the newest capture from a game (desktop recordings and unnamed ones aside)
                        var fromGame = all.FirstOrDefault(c => !c.Game.Equals("Desktop", StringComparison.OrdinalIgnoreCase) && c.Game != "Capture");
                        headline = fromGame != null ? "New from " + fromGame.Game : "Your captures";
                        about = text + ", newest first. Down for the latest ones.";
                    }
                }
                catch { text = ""; headline = "Captures"; about = ""; }
                dispatcher.BeginInvoke(new Action(() =>
                {
                    Application.Current.Resources["SeleneStatCaptures"] = text;
                    var entries = latest.Select(c =>
                    {
                        var path = c.Path;
                        return new CardShowcase.Entry { Image = c.IsVideo ? Thumbnails.Cached(c.Path) : c.Path, Title = c.Game, Sub = GameFacts.Ago(c.When), Glyph = "\uE714", Open = () => LoginScene.OpenWith(() => new CapturesScreen(path)) };
                    }).ToList();
                    CardShowcase.Set("nav:captures", "Captures", headline, about, entries);
                }));
            });
        }
    }
}
