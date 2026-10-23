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
    // The Activity card: what you've been playing. The numbers as glass cards, your top game called out, then rows of
    // covers: most played (with a bar for its share), played last, games to pick up again, and the ones still waiting.
    // The focused game's art glows behind. A shows the game on Spotlight, X plays it, B goes back.
    public class ActivityScreen : Screen
    {
        private const double Side = 96, TileW = 150, TileH = 200, Pitch = TileW + 22, RowTop = 330, RowPitch = 318;
        private const int PerRow = 9, Visible = 2;
        private readonly List<List<Game>> rows = new List<List<Game>>();
        private readonly List<List<Tile>> tiles = new List<List<Tile>>();
        private readonly List<string> rowNames = new List<string>();
        private readonly Grid page = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly TranslateTransform pageShift = new TranslateTransform();
        private readonly Image hero = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
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

            // header: the title, then the numbers as glass cards
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 56, 0, 0) };
            head.Children.Add(new TextBlock { Text = "Activity", FontSize = 44, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor") });
            head.Children.Add(new TextBlock { Text = Profiles.Active != null ? "What " + Profiles.Active.Name + " has been playing" : "What you've been playing", FontSize = 20, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), Margin = new Thickness(2, 6, 0, 0) });
            var cards = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 28, 0, 0) };
            cards.Children.Add(Card("THIS WEEK", week == 1 ? "1 game" : week + " games", "played in the last 7 days"));
            cards.Children.Add(Card("THIS MONTH", month == 1 ? "1 game" : month + " games", "played in the last 30 days"));
            cards.Children.Add(Card("ALL PLAY TIME", total < 3600 ? GameFacts.Hours(total) : (int)TimeSpan.FromSeconds(total).TotalHours + " h", played.Count + " games played"));
            cards.Children.Add(Card("YOUR GAMES", games.Count.ToString(), installed + " installed · " + neverPlayed + " never played"));
            head.Children.Add(cards);
            Root.Children.Add(head);
            Ui.Rise(head, 40, 14, 320);

            // the top game, called out at the right
            var top = most.FirstOrDefault();
            if (top != null && total > 0)
            {
                int share = (int)Math.Round(100.0 * GameFacts.Seconds(top) / total);
                var call = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, Side, 0) };
                call.Children.Add(new TextBlock { Text = "YOUR TOP GAME", FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Right });
                call.Children.Add(new TextBlock { Text = top.Name, FontSize = 34, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 700, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
                call.Children.Add(new TextBlock { Text = GameFacts.Hours(GameFacts.Seconds(top)) + "  ·  " + share + "% of your play time", FontSize = 18, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.75), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) });
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

        private static Border Card(string label, string value, string sub)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6) });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(0, 2, 0, 0) });
            return new Border { Child = sp, CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 14, 26, 16), Margin = new Thickness(0, 0, 14, 0), MinWidth = 200, Background = Ink.Brush("SeleneGlassColor", 0.55), BorderBrush = Ink.Brush("SeleneInkColor", 0.12), BorderThickness = new Thickness(1) };
        }

        // a row of covers with a caption each; bar: a share of the longest (most played), shown as a line under the cover
        private void AddRow(string title, List<Game> games, Func<Game, string> caption, Func<Game, double> bar)
        {
            if (games.Count == 0) return;
            int r = rows.Count;
            var list = new List<Tile>();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, r * RowPitch, 0, 0) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(2, 0, 0, 14) });
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < games.Count; i++)
            {
                var g = games[i];
                var t = GameFacts.CoverTile(g, TileW, TileH);
                var item = new StackPanel { Width = TileW, Margin = new Thickness(0, 0, Pitch - TileW, 0) };
                item.Children.Add(t.Root);
                if (bar != null)
                {
                    var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneInkColor", 0.18), Margin = new Thickness(2, 10, 2, 0) };
                    track.Child = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneFocusColor"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(6, (TileW - 4) * Math.Max(0.02, Math.Min(1, bar(g)))) };
                    item.Children.Add(track);
                }
                item.Children.Add(new TextBlock { Text = g.Name, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, bar != null ? 8 : 12, 0, 0) });
                item.Children.Add(new TextBlock { Text = caption(g), FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 2, 0, 0) });
                strip.Children.Add(item);
                list.Add(t);
                if (r < Visible) Ui.Rise(item, 140 + r * 60 + i * 35, 14, 300);
            }
            panel.Children.Add(strip);
            page.Children.Add(panel);
            rows.Add(games);
            tiles.Add(list);
            rowNames.Add(title);
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
            if (path == null) { Ui.Fade(hero, 0, 300); return; }
            Ui.LoadInOrder(new List<string> { path }, 1920, (i, pic) =>
            {
                if (closed || path != heroShown) return;
                hero.Source = pic;
                hero.Opacity = 0;
                Ui.Fade(hero, 0.6, 500);
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
                    LoginScene.Finish();
                    HomeNav.ShowGame(g.Id);
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
