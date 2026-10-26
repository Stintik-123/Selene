using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace Selene
{
    // The Activity card: what you've been playing. The numbers in a row under the title, your top game's logo at the right,
    // then rows of covers across the screen (each slides along with the focus): most played (with a bar for its share),
    // played last, games to pick up again, and the ones still waiting. The focused game's art crossfades in behind.
    // A shows the game on Spotlight, X plays it, B goes back.
    public class ActivityScreen : Screen
    {
        private const double Side = 96, TileW = 150, TileH = 200, Pitch = TileW + 22, RowTop = 330, RowPitch = 318;
        private const int PerRow = 20, Visible = 2, Across = 10;   // games a row holds; rows and covers on screen at once
        private readonly List<List<Game>> rows = new List<List<Game>>();
        private readonly List<List<Tile>> tiles = new List<List<Tile>>();
        private readonly List<string> rowNames = new List<string>();
        private readonly List<TranslateTransform> rowShift = new List<TranslateTransform>();
        private readonly Grid page = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly TranslateTransform pageShift = new TranslateTransform();
        private readonly Image hero = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly Image heroBack = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };   // the art going, under the one coming
        private readonly DispatcherTimer heroLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private int zone, col, firstRow;
        private bool closed;
        private string heroShown;

        public ActivityScreen()
        {
            var dark = new Border();
            dark.SetResourceReference(Border.BackgroundProperty, "SeleneScrimBrush");
            Panel.SetZIndex(dark, -10);
            Root.Children.Add(dark);
            RenderOptions.SetBitmapScalingMode(hero, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(heroBack, BitmapScalingMode.HighQuality);
            Panel.SetZIndex(heroBack, -6);
            Root.Children.Add(heroBack);
            Panel.SetZIndex(hero, -5);
            Root.Children.Add(hero);
            // the art stays behind the words: dark on the left, along the bottom and a soft vignette at the edges
            var shade = new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0.94, 0), Ink.Stop("SeleneScrimColor", 0.72, 0.5), Ink.Stop("SeleneScrimColor", 0.3, 1) }, 0) };
            Panel.SetZIndex(shade, -4);
            Root.Children.Add(shade);
            var shade2 = new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0, 0.25), Ink.Stop("SeleneScrimColor", 0.88, 1) }, 90) };
            Panel.SetZIndex(shade2, -4);
            Root.Children.Add(shade2);
            var vignette = new Border { IsHitTestVisible = false, Background = new RadialGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0, 0.55), Ink.Stop("SeleneScrimColor", 0.6, 1) }) { RadiusX = 0.8, RadiusY = 0.9 } };
            Panel.SetZIndex(vignette, -3);
            Root.Children.Add(vignette);

            var games = GameFacts.Games();
            var played = games.Where(g => GameFacts.Seconds(g) > 0).ToList();
            var most = played.OrderByDescending(GameFacts.Seconds).Take(PerRow).ToList();
            var recent = games.Where(g => GameFacts.LastPlayed(g).HasValue).OrderByDescending(g => GameFacts.LastPlayed(g).Value).Take(PerRow).ToList();
            var again = games.Where(g => GameFacts.Seconds(g) >= 3600 && GameFacts.LastPlayed(g).HasValue && GameFacts.LastPlayed(g).Value < DateTime.Now.AddDays(-45))
                .OrderByDescending(GameFacts.Seconds).Take(PerRow).ToList();
            var waiting = games.Where(g => GameFacts.Seconds(g) == 0 && !GameFacts.LastPlayed(g).HasValue).OrderByDescending(g => g.IsInstalled).ThenByDescending(g => g.Added ?? DateTime.MinValue).Take(PerRow).ToList();
            int week = games.Count(g => { var w = GameFacts.LastPlayed(g); return w.HasValue && w.Value > DateTime.Now.AddDays(-7); });
            int month = games.Count(g => { var w = GameFacts.LastPlayed(g); return w.HasValue && w.Value > DateTime.Now.AddDays(-30); });
            ulong total = 0; foreach (var g in games) total += GameFacts.Seconds(g);
            int installed = games.Count(g => g.IsInstalled);
            int neverPlayed = games.Count(g => GameFacts.Seconds(g) == 0 && !GameFacts.LastPlayed(g).HasValue);

            // header: the title, then the numbers in a row
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 56, 0, 0) };
            head.Children.Add(new TextBlock { Text = "Activity", FontSize = 44, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor") });
            var stats = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 34, 0, 0) };
            stats.Children.Add(Stat(week.ToString(), week == 1 ? "GAME THIS WEEK" : "GAMES THIS WEEK", true));
            stats.Children.Add(Stat(month.ToString(), month == 1 ? "GAME THIS MONTH" : "GAMES THIS MONTH", false));
            stats.Children.Add(Stat(total < 3600 ? GameFacts.Hours(total) : (int)TimeSpan.FromSeconds(total).TotalHours + " h", "PLAY TIME", false));
            stats.Children.Add(Stat(played.Count.ToString(), "GAMES PLAYED", false));
            stats.Children.Add(Stat(games.Count.ToString(), installed + " INSTALLED  \u00B7  " + neverPlayed + " NEVER PLAYED", false));
            head.Children.Add(stats);
            Root.Children.Add(head);
            Ui.Rise(head, 40, 14, 320);

            // the top game, called out at the right
            var top = most.FirstOrDefault();
            if (top != null && total > 0)
            {
                int share = (int)Math.Round(100.0 * GameFacts.Seconds(top) / total);
                var call = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, Side, 0) };
                call.Children.Add(new TextBlock { Text = "YOUR TOP GAME", FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Right });
                // its logo (the one Selene shows on its page), else its name
                var logo = HomeNav.Api != null ? System.IO.Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "ExtraMetadata", "games", top.Id.ToString(), "Logo.png") : null;
                if (logo != null && System.IO.File.Exists(logo))
                {
                    var img = new Image { MaxHeight = 110, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Right, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 0) };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Ui.LoadInOrder(new List<string> { logo }, 920, (k, pic) => { if (!closed) img.Source = pic; });
                    call.Children.Add(img);
                }
                else call.Children.Add(new TextBlock { Text = top.Name, FontSize = 34, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 700, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
                // its numbers, as the ones at the left: the time in it, then its share of all with a bar
                var nums = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
                nums.Children.Add(Stat(GameFacts.Hours(GameFacts.Seconds(top)), "PLAYED", true));
                var part = Stat(share + "%", "OF YOUR PLAY TIME", false);
                var shareBar = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ink.Brush("SeleneInkColor", 0.18), Margin = new Thickness(1, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
                shareBar.Child = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ink.Brush("SeleneFocusColor"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, 130 * share / 100.0) };
                ((StackPanel)part.Children[part.Children.Count - 1]).Children.Add(shareBar);
                ((StackPanel)part.Children[part.Children.Count - 1]).Width = 130;
                nums.Children.Add(part);
                call.Children.Add(nums);
                Root.Children.Add(call);
                Ui.Rise(call, 120, 14, 320);
            }

            // the rows, on a page that glides up (two rows at a time)
            var area = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, RowTop - 30, 0, 0), Height = Visible * RowPitch + 60, ClipToBounds = true };
            double h = Visible * RowPitch + 60;
            var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, h) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 30 / h));
            fade.GradientStops.Add(new GradientStop(Colors.Black, (h - 50) / h));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            area.OpacityMask = fade;
            page.RenderTransform = pageShift;
            page.Margin = new Thickness(0, 30, 0, 0);
            area.Children.Add(new Canvas { Children = { page } });
            Root.Children.Add(area);
            ulong maxSecs = most.Count > 0 ? GameFacts.Seconds(most[0]) : 1;
            AddRow("Most played", most, g => GameFacts.Hours(GameFacts.Seconds(g)), g => (double)GameFacts.Seconds(g) / Math.Max(1, maxSecs));
            AddRow("Played last", recent, g => GameFacts.Ago(GameFacts.LastPlayed(g)), null);
            AddRow("Pick up again", again, g => "Last " + GameFacts.Ago(GameFacts.LastPlayed(g)).ToLowerInvariant() + " · " + GameFacts.Hours(GameFacts.Seconds(g)), null);
            AddRow("Still waiting", waiting, g => g.IsInstalled ? "Installed, never played" : "Never played", null);
            if (rows.Count == 0)
                Root.Children.Add(new TextBlock { Text = "Nothing played yet. Your most played and last played games show here.", FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, RowTop + 20, 0, 0) });

            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            if (rows.Count > 0) Root.Children.Add(Ui.Prompts(true, Ui.Prompt("X", "Play"), Ui.Prompt("A", "Show on Spotlight")));
            heroLater.Tick += (s, e) => { heroLater.Stop(); ShowHero(); };
        }

        // a number large with what it counts under it, a thin line before it (but the first)
        private static StackPanel Stat(string value, string label, bool first)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (!first) row.Children.Add(new Border { Width = 1, Background = Ink.Brush("SeleneInkColor", 0.2), Margin = new Thickness(28, 6, 28, 6) });
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = value, FontSize = 34, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor") });
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), Margin = new Thickness(1, 2, 0, 0) });
            row.Children.Add(sp);
            return row;
        }

        // a row of covers with a caption each; bar: a share of the longest (most played), shown as a line under the cover
        private void AddRow(string title, List<Game> games, Func<Game, string> caption, Func<Game, double> bar)
        {
            if (games.Count == 0) return;
            int r = rows.Count;
            var list = new List<Tile>();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, r * RowPitch, 0, 0) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(2, 0, 0, 14) });
            var shift = new TranslateTransform();
            var strip = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = shift };
            for (int i = 0; i < games.Count; i++)
            {
                var g = games[i];
                var t = GameFacts.CoverTile(g, TileW, TileH);
                var item = new StackPanel { Width = TileW, Margin = new Thickness(0, 0, Pitch - TileW, 0) };
                item.Children.Add(t.Root);
                if (bar != null)
                {
                    // (low enough that the focus ring, grown with the cover, stays clear of it)
                    var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneInkColor", 0.18), Margin = new Thickness(2, 22, 2, 0) };
                    track.Child = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneFocusColor"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(6, (TileW - 4) * Math.Max(0.02, Math.Min(1, bar(g)))) };
                    item.Children.Add(track);
                }
                item.Children.Add(new TextBlock { Text = g.Name, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, bar != null ? 8 : 12, 0, 0) });
                item.Children.Add(new TextBlock { Text = caption(g), FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 2, 0, 0) });
                strip.Children.Add(item);
                list.Add(t);
                if (r < Visible && i < Across) Ui.Rise(item, 140 + r * 60 + i * 35, 14, 300);
            }
            // the lane the row slides in: cut off just left of the margin with a soft edge (the covers slid past hung over the
            // screen's edge), room around for the focus ring
            const double Room = 26;
            var lane = new Grid { ClipToBounds = true, Width = 1920 - Side + Room, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-Room, -Room, 0, -Room) };
            var edge = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(8, 0) };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            edge.GradientStops.Add(new GradientStop(Colors.Black, 1));
            lane.OpacityMask = edge;
            // (on a canvas: a row wider than the lane is laid out whole and slides, the lane's edge cutting it)
            strip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            lane.Height = strip.DesiredSize.Height + 2 * Room;
            Canvas.SetLeft(strip, Room);
            Canvas.SetTop(strip, Room);
            lane.Children.Add(new Canvas { Children = { strip } });
            panel.Children.Add(lane);
            page.Children.Add(panel);
            rows.Add(games);
            tiles.Add(list);
            rowNames.Add(title);
            rowShift.Add(shift);
            GameFacts.FillCovers(games, list, 300);
        }

        private Game Focused { get { return zone < rows.Count && col < rows[zone].Count ? rows[zone][col] : null; } }

        private void ShowFocus(bool animate)
        {
            for (int r = 0; r < tiles.Count; r++)
                for (int i = 0; i < tiles[r].Count; i++) tiles[r][i].Focus(r == zone && i == col);
            // the focused row in the top half (the one below peeks in), within the rows' ends
            int first = Math.Max(0, Math.Min(zone, rows.Count - Visible));
            if (first != firstRow || !animate)
            {
                firstRow = first;
                pageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-first * RowPitch, TimeSpan.FromMilliseconds(animate ? 280 : 0)) { EasingFunction = Ui.Out() });
            }
            // the focused row slides along once the focus nears its right end
            if (zone < rowShift.Count)
            {
                int along = Math.Max(0, Math.Min(col - (Across - 2), rows[zone].Count - Across));
                rowShift[zone].BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-along * Pitch, TimeSpan.FromMilliseconds(animate ? 240 : 0)) { EasingFunction = Ui.Out() });
            }
            heroLater.Stop();
            heroLater.Start();
        }

        // the focused game's art behind, once the focus rests on it
        private void ShowHero()
        {
            var g = Focused;
            var path = g != null ? GameFacts.BackgroundPath(g) : null;
            if (path == heroShown) return;
            heroShown = path;
            if (path == null) { Ui.Fade(hero, 0, 400); return; }
            Ui.LoadInOrder(new List<string> { path }, 1920, (i, pic) =>
            {
                if (closed || path != heroShown) return;
                // a crossfade: the art showing goes under, the new one fades in over it, then the old one melts away
                heroBack.BeginAnimation(UIElement.OpacityProperty, null);
                heroBack.Source = hero.Source;
                heroBack.Opacity = hero.Opacity;
                hero.BeginAnimation(UIElement.OpacityProperty, null);
                hero.Source = pic;
                hero.Opacity = 0;
                Ui.Fade(hero, 0.6, 600);
                Ui.Fade(heroBack, 0, 900, 300);
            });
        }

        public override void Shown() { ShowFocus(false); }
        public override void Hidden() { closed = true; heroLater.Stop(); }

        public override void Handle(string b)
        {
            if (b == "B") { Ui.Back(); LoginScene.Finish(); return; }
            var g = Focused;
            switch (b)
            {
                case "DPadLeft":
                case "DPadRight":
                {
                    if (zone >= rows.Count) return;
                    int next = col + (b == "DPadRight" ? 1 : -1);
                    if (next < 0 || next >= rows[zone].Count) { Ui.Shake(tiles[zone][col].Root); return; }
                    col = next;
                    Ui.Tick();
                    ShowFocus(true);
                    return;
                }
                case "DPadUp":
                case "DPadDown":
                {
                    int next = zone + (b == "DPadDown" ? 1 : -1);
                    if (next < 0 || next >= rows.Count) return;
                    zone = next;
                    col = Math.Min(col, rows[zone].Count - 1);
                    Ui.Tick();
                    ShowFocus(true);
                    return;
                }
                case "A":
                    if (g == null) return;
                    Ui.Confirm();
                    LoginScene.ForgetReturnFocus();   // the pick gets the focus on Spotlight
                    var id = g.Id;
                    LoginScene.FinishThen(() => HomeNav.ShowGame(id));
                    return;
                case "X":
                    if (g == null) return;
                    if (!g.IsInstalled) { Ui.Shake(tiles[zone][col].Root); return; }
                    Ui.Confirm();
                    LoginScene.Finish();
                    HomeNav.StartGameNow(g.Id);
                    return;
            }
        }

        // the Activity card on Spotlight: how the week went, and the games played last as pictures (Down goes into them)
        public static void FillCard(List<Game> games, int week, ulong total)
        {
            var recent = games.Where(g => GameFacts.LastPlayed(g).HasValue).OrderByDescending(g => GameFacts.LastPlayed(g).Value).Take(4).ToList();
            var most = games.Where(g => GameFacts.Seconds(g) > 0).OrderByDescending(GameFacts.Seconds).FirstOrDefault();
            string headline = week == 0 ? "Nothing played this week" : week == 1 ? "1 game this week" : week + " games this week";
            string about = total < 3600 ? "Your play time, most played and last played games show here." : (int)TimeSpan.FromSeconds(total).TotalHours + " hours in all";
            if (most != null && total >= 3600) about += ", most of them in " + most.Name + " (" + GameFacts.Hours(GameFacts.Seconds(most)) + ").";
            if (recent.Count > 0) about += " Down for the games you played last.";
            var entries = recent.Select(g =>
            {
                var id = g.Id;
                return new CardShowcase.Entry { Image = GameFacts.BackgroundPath(g) ?? GameFacts.CoverPath(g), Title = g.Name, Sub = GameFacts.Ago(GameFacts.LastPlayed(g)), Glyph = "", Open = () => HomeNav.ShowGame(id) };
            }).ToList();
            CardShowcase.Set("nav:activity", "Activity", headline, about, entries);
        }
    }
}
