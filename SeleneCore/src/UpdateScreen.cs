using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Selene
{
    // A newer Selene, installed with one press: what's new (the release's notes), then A downloads the theme and the add-on
    // from the GitHub release and hands both to Playnite's own installer (the same one opening the files would use), and
    // Playnite puts them in when it restarts. Settings and profiles stay. In the developer preview everything happens but the
    // handing over.
    public class UpdateScreen : Screen
    {
        private const string Release = "https://github.com/mattxslv/Selene/releases/download/v";
        private static readonly string[] Files = { "Selene.pthm", "SeleneCore.pext" };
        private const double BarW = 620;

        private readonly Func<Screen> back;
        private readonly string version;
        private readonly TextBlock status = new TextBlock { FontSize = 19, FontFamily = Ui.Body, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 760 };
        private readonly Border bar = new Border { Width = BarW, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), Opacity = 0 };
        private readonly Border fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        private readonly Grid prompts = new Grid();
        private int state;   // 0 asking, 1 downloading, 2 ready to restart, 3 failed
        private bool closed;
        private WebClient web;
        private StickScroll stick;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.94); } }

        public UpdateScreen(Func<Screen> back = null)
        {
            this.back = back;
            version = UpdateCheck.Available ?? "";
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 900 };
            var mark = Brand.Mark(84, true);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(mark);
            col.Children.Add(new TextBlock { Text = "Selene " + version, FontSize = 48, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) });
            col.Children.Add(new TextBlock { Text = UpdateCheck.Preview ? "A preview of the update screen (this is what people on an older version see)." : "A new version is ready. You have " + Brand.Version + ".", FontSize = 19, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });

            // what's new: the release notes as plain words, fading out at the foot when long
            var notes = new TextBlock { Text = Plain(UpdateCheck.Notes), FontSize = 17, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.85), TextWrapping = TextWrapping.Wrap, LineHeight = 26 };
            // the right stick scrolls a long one, smoothly
            var notesScroll = new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, CanContentScroll = false };
            stick = new StickScroll(notesScroll, () => !closed);
            notesScroll.OpacityMask = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.85), new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 1) }, 90);
            // a slim bar at the right: there's more, and where you are in it (only when it scrolls)
            var thumb = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneInkColor", 0.6), VerticalAlignment = VerticalAlignment.Top };
            var track = new Grid { Width = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -16, 0), Visibility = Visibility.Collapsed };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneInkColor", 0.12) });
            track.Children.Add(thumb);
            notesScroll.ScrollChanged += (sv, e) =>
            {
                bool more = notesScroll.ScrollableHeight > 1 && notesScroll.ExtentHeight > 0;
                track.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
                if (!more) return;
                double h = notesScroll.ViewportHeight, size = Math.Max(24, h * h / notesScroll.ExtentHeight);
                thumb.Height = size;
                thumb.Margin = new Thickness(0, (h - size) * notesScroll.VerticalOffset / notesScroll.ScrollableHeight, 0, 0);
            };
            var notesArea = new Grid();
            notesArea.Children.Add(notesScroll);
            notesArea.Children.Add(track);
            var notesBox = new Border { MaxHeight = 380, Margin = new Thickness(0, 30, 0, 0), ClipToBounds = true, Child = notesArea, Padding = new Thickness(28, 22, 28, 22), CornerRadius = new CornerRadius(18), Background = Ink.Brush("SeleneGlassColor", 0.7), BorderBrush = Ink.Brush("SeleneInkColor", 0.12), BorderThickness = new Thickness(1) };
            col.Children.Add(notesBox);

            fill.Background = Ink.Brush("SeleneFocusColor");
            bar.Background = Ink.Brush("SeleneInkColor", 0.15);
            bar.Child = fill;
            status.Foreground = Ink.Brush("SeleneInkColor", 0.75);
            status.Margin = new Thickness(0, 26, 0, 0);
            col.Children.Add(status);
            col.Children.Add(bar);
            Root.Children.Add(col);
            Root.Children.Add(prompts);
            ShowState(0);
        }

        // the notes without Markdown's marks (bold, links, headings)
        private static string Plain(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "Fixes and improvements.";
            var s = md.Replace("\r", "");
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "$1");
            s = Regex.Replace(s, @"(?m)^#+\s*", "");
            s = Regex.Replace(s, @"(?m)^\s*[-*]\s+", "• ");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        private void ShowState(int to)
        {
            state = to;
            prompts.Children.Clear();
            switch (to)
            {
                case 0:
                    status.Text = "Settings, profiles and your theme stay as they are.";
                    prompts.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Later")));
                    prompts.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Update now")));
                    break;
                case 1:
                    prompts.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Cancel")));
                    break;
                case 2:
                    status.Text = UpdateCheck.Preview
                        ? "Downloaded. (A preview: nothing was handed to Playnite, so nothing changes.)"
                        : "Ready. Selene " + version + " goes in when Playnite restarts.";
                    prompts.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Later")));
                    prompts.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Restart Playnite")));
                    break;
                case 3:
                    prompts.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Close")));
                    prompts.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Try again")));
                    break;
            }
            Ui.Fade(status, 1, 200);
        }

        private void Progress(double share)
        {
            fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(BarW * Math.Max(0, Math.Min(1, share)), TimeSpan.FromMilliseconds(220)) { EasingFunction = Ui.Out() });
        }

        // both files, one after the other, the bar across both (the theme is nearly all of it)
        private void Download()
        {
            ShowState(1);
            Ui.Fade(bar, 1, 200);
            Progress(0);
            status.Text = "Downloading…";
            var folder = Path.Combine(Path.GetTempPath(), "SeleneUpdate", version);
            try { Directory.CreateDirectory(folder); } catch { }
            int index = 0;
            Action next = null;
            next = () =>
            {
                if (closed || state != 1) return;
                if (index >= Files.Length) { Install(folder); return; }
                var name = Files[index];
                var target = Path.Combine(folder, name);
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                web = new WebClient();
                web.Headers[HttpRequestHeader.UserAgent] = "Selene-Playnite";
                int at = index;
                web.DownloadProgressChanged += (s, e) =>
                {
                    double part = e.TotalBytesToReceive > 0 ? (double)e.BytesReceived / e.TotalBytesToReceive : 0;
                    double share = at == 0 ? part * 0.97 : 0.97 + part * 0.03;   // (the theme first: almost all the size)
                    Progress(share);
                    status.Text = "Downloading…  " + (int)(share * 100) + "%";
                };
                web.DownloadFileCompleted += (s, e) =>
                {
                    if (closed || state != 1) return;
                    if (e.Cancelled) return;
                    if (e.Error != null) { Fail("Couldn't download " + name + ": " + (e.Error.InnerException ?? e.Error).Message); return; }
                    index++;
                    next();
                };
                web.DownloadFileAsync(new Uri(Release + version + "/" + name), target);
            };
            next();
        }

        // both packages to Playnite's install queue (checked by Playnite first); it installs them on its next start
        private void Install(string folder)
        {
            Progress(1);
            if (UpdateCheck.Preview) { HomeNav.Log("update: preview, " + version + " downloaded, not queued"); Done(); return; }
            foreach (var name in Files)
            {
                var file = Path.Combine(folder, name);
                if (!AddonBatch.QueuePackage(file)) { Fail("Playnite didn't accept " + name + "."); return; }
            }
            HomeNav.Log("update: " + version + " queued with Playnite");
            Done();
        }

        private void Done()
        {
            Ui.Play("Success.wav", 0.8);
            ShowState(2);
        }

        private void Fail(string why)
        {
            HomeNav.Log("update: " + why);
            status.Text = why;
            Ui.Fade(bar, 0, 200);
            ShowState(3);
        }

        private void Leave()
        {
            closed = true;
            if (web != null && web.IsBusy) try { web.CancelAsync(); } catch { }
            Ui.Back();
            if (back != null) LoginScene.Show(back(), -1); else LoginScene.Finish();
        }

        public override void Shown() { stick.Start(); }

        public override void Hidden() { closed = true; stick.Stop(); if (web != null && web.IsBusy) try { web.CancelAsync(); } catch { } }

        public override void Handle(string b)
        {
            if (b == "B") { Leave(); return; }
            if (b != "A") return;
            switch (state)
            {
                case 0: case 3: Ui.Confirm(); Download(); return;
                case 2:
                    Ui.Confirm();
                    if (UpdateCheck.Preview) { status.Text = "(A preview: Playnite isn't restarted.)"; return; }
                    if (!AddonBatch.RestartPlaynite()) status.Text = "Restart Playnite to finish the update.";
                    return;
            }
        }
    }
}
