using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace Selene
{
    // The Activity card: what you've been playing. The week's and all-time numbers up top, then your most played games
    // and the ones you played last, each as a cover with its hours or when you last played it. The focused game's art
    // glows behind. A shows the game on Spotlight, X plays it, B goes back.
    public class ActivityScreen : Screen
    {
        private const double Side = 96, TileW = 150, TileH = 200, Pitch = TileW + 22;
        private const int PerRow = 9;
        private readonly List<List<Game>> rows = new List<List<Game>>();
        private readonly List<List<Tile>> tiles = new List<List<Tile>>();
        private readonly List<StackPanel> rowPanels = new List<StackPanel>();
        private readonly Image hero = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly DispatcherTimer heroLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private readonly TextBlock note;
        private int zone, col;
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
            // the art stays behind the words: dark on the left and along the bottom
            var shade = new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0.94, 0), Ink.Stop("SeleneScrimColor", 0.75, 0.5), Ink.Stop("SeleneScrimColor", 0.35, 1) }, 0) };
            Panel.SetZIndex(shade, -4);
            Root.Children.Add(shade);
            var shade2 = new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0, 0.3), Ink.Stop("SeleneScrimColor", 0.85, 1) }, 90) };
            Panel.SetZIndex(shade2, -4);
            Root.Children.Add(shade2);

            var games = GameFacts.Games();
            var played = games.Where(g => GameFacts.Seconds(g) > 0).ToList();
            var most = played.OrderByDescending(GameFacts.Seconds).Take(PerRow).ToList();
            var recent = games.Where(g => GameFacts.LastPlayed(g).HasValue).OrderByDescending(g => GameFacts.LastPlayed(g).Value).Take(PerRow).ToList();

            // header and the numbers
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 56, 0, 0) };
            head.Children.Add(new TextBlock { Text = "Activity", FontSize = 44, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor") });
            head.Children.Add(new TextBlock { Text = Profiles.Active != null ? "What " + Profiles.Active.Name + " has been playing" : "What you've been playing", FontSize = 20, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), Margin = new Thickness(2, 6, 0, 0) });
            int week = games.Count(g => { var w = GameFacts.LastPlayed(g); return w.HasValue && w.Value > DateTime.Now.AddDays(-7); });
            int month = games.Count(g => { var w = GameFacts.LastPlayed(g); return w.HasValue && w.Value > DateTime.Now.AddDays(-30); });
            ulong total = 0; foreach (var g in games) total += GameFacts.Seconds(g);
            int installed = games.Count(g => g.IsInstalled);
            var stats = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 34, 0, 0) };
            stats.Children.Add(Stat("PLAYED THIS WEEK", week == 1 ? "1 game" : week + " games"));
            stats.Children.Add(Stat("LAST 30 DAYS", month == 1 ? "1 game" : month + " games"));
            stats.Children.Add(Stat("ALL PLAY TIME", total < 3600 ? GameFacts.Hours(total) : (int)TimeSpan.FromSeconds(total).TotalHours + " hours"));
            stats.Children.Add(Stat("YOUR GAMES", games.Count + " · " + installed + " installed"));
            head.Children.Add(stats);
            Root.Children.Add(head);
            Ui.Rise(head, 40, 14, 320);

            AddRow("Most played", most, g => GameFacts.Hours(GameFacts.Seconds(g)), 330);
            AddRow("Recently played", recent, g => GameFacts.Ago(GameFacts.LastPlayed(g)), 660);

            note = new TextBlock
            {
                Text = played.Count == 0 && recent.Count == 0 ? "Nothing played yet. Your most played and last played games show here." : "",
                FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 380, 0, 0)
            };
            Root.Children.Add(note);

            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            if (rows.Any(r => r.Count > 0)) Root.Children.Add(Ui.Prompts(true, Ui.Prompt("X", "Play"), Ui.Prompt("A", "Show on Spotlight")));
            // the first row that has games starts focused
            zone = rows.FindIndex(r => r.Count > 0);
            if (zone < 0) zone = 0;
            heroLater.Tick += (s, e) => { heroLater.Stop(); ShowHero(); };
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
                return new CardShowcase.Entry { Image = GameFacts.BackgroundPath(g) ?? GameFacts.CoverPath(g), Title = g.Name, Sub = GameFacts.Ago(GameFacts.LastPlayed(g)), Glyph = "\uE7FC", Open = () => HomeNav.ShowGame(id) };
            }).ToList();
            CardShowcase.Set("nav:activity", "Activity", headline, about, entries);
        }

        private static StackPanel Stat(string label, string value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 44, 0) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6) });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(0, 3, 0, 0) });
            return sp;
        }

        private void AddRow(string title, List<Game> games, Func<Game, string> caption, double top)
        {
            var list = new List<Tile>();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, top, 0, 0) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(2, 0, 0, 14) });
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < games.Count; i++)
            {
                var g = games[i];
                var t = GameFacts.CoverTile(g, TileW, TileH);
                var item = new StackPanel { Width = TileW, Margin = new Thickness(0, 0, Pitch - TileW, 0) };
                item.Children.Add(t.Root);
                item.Children.Add(new TextBlock { Text = g.Name, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 12, 0, 0) });
                item.Children.Add(new TextBlock { Text = caption(g), FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), Margin = new Thickness(2, 2, 0, 0) });
                strip.Children.Add(item);
                list.Add(t);
                Ui.Rise(item, 120 + (int)(top / 10) + i * 40, 14, 300);
            }
            if (games.Count == 0) panel.Visibility = Visibility.Collapsed;
            panel.Children.Add(strip);
            Root.Children.Add(panel);
            rows.Add(games);
            tiles.Add(list);
            rowPanels.Add(panel);
            GameFacts.FillCovers(games, list, 300);
        }

        private Game Focused { get { return zone < rows.Count && col < rows[zone].Count ? rows[zone][col] : null; } }

        private void ShowFocus()
        {
            for (int r = 0; r < tiles.Count; r++)
                for (int i = 0; i < tiles[r].Count; i++) tiles[r][i].Focus(r == zone && i == col);
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

        public override void Shown() { ShowFocus(); }
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
                    if (next < 0 || next >= rows[zone].Count) { if (rows[zone].Count > 0) Ui.Shake(tiles[zone][col].Root); return; }
                    col = next;
                    Ui.Tick();
                    ShowFocus();
                    return;
                }
                case "DPadUp":
                case "DPadDown":
                {
                    int next = zone + (b == "DPadDown" ? 1 : -1);
                    if (next < 0 || next >= rows.Count || rows[next].Count == 0) return;
                    zone = next;
                    col = Math.Min(col, rows[zone].Count - 1);
                    Ui.Tick();
                    ShowFocus();
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
    }
}
