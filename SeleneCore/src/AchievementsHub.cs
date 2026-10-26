using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace Selene
{
    // The Achievements card: every game's achievements together (Playnite Achievements' data). The totals as glass cards,
    // then rows: the latest unlocks, games close to done, and every game with its progress. The focused game's art glows
    // behind. A opens that game's list, B goes back.
    public class AchievementsHub : Screen
    {
        private const double Side = 96, TileW = 150, TileH = 200, Pitch = TileW + 22, RowTop = 330, RowPitch = 318;
        private const int PerRow = 12, Visible = 2;

        private class Pick { public Guid Game; public AchievementsData.Item Item; }
        private readonly List<List<Pick>> rows = new List<List<Pick>>();
        private readonly List<List<Tile>> tiles = new List<List<Tile>>();
        private readonly Grid page = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly TranslateTransform pageShift = new TranslateTransform();
        private readonly Image hero = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly DispatcherTimer heroLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private readonly StackPanel head;
        private readonly TextBlock note;
        private int zone, col, firstRow, keepZone, keepCol;
        private bool closed, loaded;
        private string heroShown;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.96); } }

        public AchievementsHub() : this(0, 0) { }

        // (zone, col): where the focus was, when coming back from a game's list
        public AchievementsHub(int zone, int col)
        {
            keepZone = zone; keepCol = col;
            RenderOptions.SetBitmapScalingMode(hero, BitmapScalingMode.HighQuality);
            Root.Children.Add(hero);
            Root.Children.Add(new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0.94, 0), Ink.Stop("SeleneScrimColor", 0.72, 0.5), Ink.Stop("SeleneScrimColor", 0.3, 1) }, 0) });
            Root.Children.Add(new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0, 0.25), Ink.Stop("SeleneScrimColor", 0.88, 1) }, 90) });

            head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 56, 0, 0) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = Ui.Icon(0xea23), FontFamily = Ui.Icons, FontSize = 44, Foreground = Ink.Brush("GlyphColor"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            title.Children.Add(new TextBlock { Text = "Achievements", FontSize = 44, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(title);
            head.Children.Add(new TextBlock { Text = Profiles.Active != null ? Profiles.Active.Name + "'s achievements, every game together" : "Your achievements, every game together", FontSize = 20, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), Margin = new Thickness(2, 6, 0, 0) });
            Root.Children.Add(head);

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

            note = new TextBlock { Text = "Getting your achievements...", FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, RowTop + 20, 0, 0) };
            Root.Children.Add(note);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Open")));
            heroLater.Tick += (s, e) => { heroLater.Stop(); ShowHero(); };
        }

        public override void Shown()
        {
            Ui.Play("OpenPanel.wav", 0.8);
            Ui.Rise(head, 40, 14, 320);
            AchievementsData.All(sets =>
            {
                if (closed) return;
                loaded = true;
                Build(sets);
            });
        }

        public override void Hidden() { closed = true; heroLater.Stop(); }

        private void Build(List<AchievementsData.GameSet> sets)
        {
            if (sets.Count == 0)
            {
                note.Text = AchievementsData.Available()
                    ? "No achievements yet. Playnite Achievements fills them in as it refreshes your games."
                    : "Achievements need the Playnite Achievements add-on.";
                return;
            }
            note.Visibility = Visibility.Collapsed;
            int earned = sets.Sum(s => s.Unlocked), total = sets.Sum(s => s.Total);
            int done = sets.Count(s => s.Total > 0 && s.Unlocked == s.Total);
            var unlocked = sets.SelectMany(s => s.Items.Where(i => i.Unlocked).Select(i => new Pick { Game = s.GameId, Item = i })).ToList();
            var rarest = unlocked.Where(p => p.Item.Rarity.HasValue).OrderBy(p => p.Item.Rarity.Value).FirstOrDefault();

            var cards = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 28, 0, 0) };
            cards.Children.Add(Card("EARNED", earned.ToString(), "of " + total + " in " + sets.Count + (sets.Count == 1 ? " game" : " games")));
            cards.Children.Add(Trophies(sets, done));
            cards.Children.Add(Card("OVERALL", (total > 0 ? (int)Math.Floor(100.0 * earned / total) : 0) + "%", "of every achievement"));
            if (rarest != null) cards.Children.Add(Card("RAREST", rarest.Item.Name, (rarest.Item.Rarity.Value < 1 ? rarest.Item.Rarity.Value.ToString("0.0") : Math.Round(rarest.Item.Rarity.Value).ToString("0")) + "% of players have it"));
            head.Children.Add(cards);
            Ui.Rise(cards, 80, 14, 320);

            // the rows
            var recent = unlocked.Where(p => p.Item.When.HasValue).OrderByDescending(p => p.Item.When.Value).Take(PerRow).ToList();
            AddUnlockRow("Recently unlocked", recent);
            var close = sets.Where(s => s.Total > 0 && s.Unlocked < s.Total && s.Unlocked * 2 >= s.Total).OrderByDescending(s => (double)s.Unlocked / s.Total).Take(PerRow).ToList();
            AddGameRow("Almost there", close);
            var games = sets.OrderByDescending(s => s.LastUnlock ?? DateTime.MinValue).ThenByDescending(s => s.Unlocked).Take(40).ToList();
            AddGameRow("Your games", games);
            zone = Math.Min(keepZone, Math.Max(0, rows.Count - 1));
            col = rows.Count > 0 ? Math.Min(keepCol, rows[zone].Count - 1) : 0;
            ShowFocus(false);
        }

        // the trophies: platinums (games completed) large, then gold, silver and bronze earned
        private static Border Trophies(List<AchievementsData.GameSet> sets, int platinums)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "TROPHIES", FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6) });
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            top.Children.Add(new TextBlock { Text = Ui.Icon(0xea23), FontFamily = Ui.Icons, FontSize = 30, Foreground = AchievementsData.GradeBrush("Platinum"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            top.Children.Add(new TextBlock { Text = platinums + (platinums == 1 ? " platinum" : " platinums"), FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(top);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var grade in new[] { "Gold", "Silver", "Bronze" })
            {
                row.Children.Add(new TextBlock { Text = Ui.Icon(0xea23), FontFamily = Ui.Icons, FontSize = 15, Foreground = AchievementsData.GradeBrush(grade), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                row.Children.Add(new TextBlock { Text = sets.Sum(s => s.Earned(grade)).ToString(), FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.75), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            }
            sp.Children.Add(row);
            return new Border { Child = sp, CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 14, 26, 16), Margin = new Thickness(0, 0, 14, 0), MinWidth = 200, Background = Ink.Brush("SeleneGlassColor", 0.55), BorderBrush = Ink.Brush("SeleneInkColor", 0.12), BorderThickness = new Thickness(1) };
        }

        private static Border Card(string label, string value, string sub)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6) });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(0, 2, 0, 0), MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(0, 2, 0, 0) });
            return new Border { Child = sp, CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 14, 26, 16), Margin = new Thickness(0, 0, 14, 0), MinWidth = 200, Background = Ink.Brush("SeleneGlassColor", 0.55), BorderBrush = Ink.Brush("SeleneInkColor", 0.12), BorderThickness = new Thickness(1) };
        }

        // the latest unlocks: the achievement's own picture, its name and the game under it
        private void AddUnlockRow(string title, List<Pick> picks)
        {
            if (picks.Count == 0) return;
            int r = rows.Count;
            var list = new List<Tile>();
            var strip = StartRow(title, r);
            var files = new List<string>();
            var at = new List<int>();
            for (int i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                var t = new Tile(TileW, TileW, 18);
                t.Face.Child = new TextBlock { Text = Ui.Icon(0xea23), FontFamily = Ui.Icons, FontSize = 48, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (p.Item.Icon != null) { files.Add(p.Item.Icon); at.Add(i); }
                var game = GameFacts.Find(p.Game);
                var item = new StackPanel { Width = TileW, Margin = new Thickness(0, 0, Pitch - TileW, 0) };
                item.Children.Add(t.Root);
                item.Children.Add(new TextBlock { Text = p.Item.Name, FontSize = 15, FontFamily = Ui.Body, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 12, 0, 0) });
                item.Children.Add(new TextBlock { Text = (game != null ? game.Name : "") , FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 2, 0, 0) });
                var when = new TextBlock { FontSize = 13, FontFamily = Ui.Body, Margin = new Thickness(2, 1, 0, 0) };
                when.Inlines.Add(new System.Windows.Documents.Run(p.Item.Grade) { Foreground = AchievementsData.GradeBrush(p.Item.Grade), FontWeight = FontWeights.SemiBold });
                when.Inlines.Add(new System.Windows.Documents.Run("  \u00B7  " + GameFacts.Ago(p.Item.When)) { Foreground = Ink.Brush("SeleneInkColor", 0.45) });
                item.Children.Add(when);
                strip.Children.Add(item);
                list.Add(t);
                if (r < Visible) Ui.Rise(item, 140 + r * 60 + i * 35, 14, 300);
            }
            Ui.LoadInOrder(files, 300, (k, pic) =>
            {
                if (closed || k >= at.Count) return;
                var face = new Border { CornerRadius = new CornerRadius(17), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill }, Opacity = 0 };
                list[at[k]].Face.Child = face;
                Ui.Fade(face, 1, 240);
            });
            rows.Add(picks);
            tiles.Add(list);
        }

        // games: the cover, a bar for how far, the count
        private void AddGameRow(string title, List<AchievementsData.GameSet> sets)
        {
            var games = sets.Select(s => GameFacts.Find(s.GameId)).ToList();
            var keep = sets.Where((s, i) => games[i] != null).ToList();
            games = games.Where(g => g != null).ToList();
            if (keep.Count == 0) return;
            int r = rows.Count;
            var list = new List<Tile>();
            var strip = StartRow(title, r);
            for (int i = 0; i < keep.Count; i++)
            {
                var s = keep[i];
                var t = GameFacts.CoverTile(games[i], TileW, TileH);
                var item = new StackPanel { Width = TileW, Margin = new Thickness(0, 0, Pitch - TileW, 0) };
                item.Children.Add(t.Root);
                var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush("SeleneInkColor", 0.18), Margin = new Thickness(2, 10, 2, 0) };
                track.Child = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ink.Brush(s.Unlocked == s.Total ? "GlyphColor" : "SeleneFocusColor"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, (TileW - 4) * (s.Total > 0 ? (double)s.Unlocked / s.Total : 0)) };
                item.Children.Add(track);
                item.Children.Add(new TextBlock { Text = games[i].Name, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 8, 0, 0) });
                if (s.Platinum)
                {
                    var plat = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 0) };
                    plat.Children.Add(new TextBlock { Text = Ui.Icon(0xea23), FontFamily = Ui.Icons, FontSize = 15, Foreground = AchievementsData.GradeBrush("Platinum"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                    plat.Children.Add(new TextBlock { Text = "Platinum", FontSize = 14, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = AchievementsData.GradeBrush("Platinum"), VerticalAlignment = VerticalAlignment.Center });
                    item.Children.Add(plat);
                }
                else item.Children.Add(new TextBlock { Text = s.Unlocked + " / " + s.Total + "  \u00B7  " + s.Percent + "%", FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), Margin = new Thickness(2, 2, 0, 0) });
                strip.Children.Add(item);
                list.Add(t);
                if (r < Visible) Ui.Rise(item, 140 + r * 60 + i * 35, 14, 300);
            }
            GameFacts.FillCovers(games, list, 300);
            rows.Add(keep.Select(s => new Pick { Game = s.GameId }).ToList());
            tiles.Add(list);
        }

        private StackPanel StartRow(string title, int r)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, r * RowPitch, 0, 0) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(2, 0, 0, 14) });
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(strip);
            page.Children.Add(panel);
            return strip;
        }

        private Pick Focused { get { return zone < rows.Count && col < rows[zone].Count ? rows[zone][col] : null; } }

        private void ShowFocus(bool animate)
        {
            for (int r = 0; r < tiles.Count; r++)
                for (int i = 0; i < tiles[r].Count; i++) tiles[r][i].Focus(r == zone && i == col);
            int first = Math.Max(0, Math.Min(zone, rows.Count - Visible));
            if (first != firstRow || !animate)
            {
                firstRow = first;
                pageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-first * RowPitch, TimeSpan.FromMilliseconds(animate ? 280 : 0)) { EasingFunction = Ui.Out() });
            }
            // a row longer than the screen moves along with the focus
            if (zone < tiles.Count && tiles[zone].Count > 0)
            {
                var strip = VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(tiles[zone][0].Root)) as StackPanel;
                if (strip != null)
                {
                    var shift = strip.RenderTransform as TranslateTransform;
                    if (shift == null) { shift = new TranslateTransform(); strip.RenderTransform = shift; }
                    double x = -Math.Max(0, col - 8) * Pitch;
                    shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(animate ? 240 : 0)) { EasingFunction = Ui.Out() });
                }
            }
            heroLater.Stop();
            heroLater.Start();
        }

        private void ShowHero()
        {
            var p = Focused;
            var g = p != null ? GameFacts.Find(p.Game) : null;
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

        public override void Handle(string b)
        {
            if (b == "B") { Ui.Back(); LoginScene.Finish(); return; }
            if (!loaded || rows.Count == 0) return;
            switch (b)
            {
                case "DPadLeft":
                case "DPadRight":
                {
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
                {
                    var p = Focused;
                    if (p == null) return;
                    Ui.Confirm();
                    int z = zone, c = col;
                    LoginScene.Show(new AchievementsScreen(p.Game, () => new AchievementsHub(z, c)), 1);
                    return;
                }
            }
        }

        // ---- the card on Spotlight: the totals and the latest unlocks as pictures (Down goes into them) ----
        public static void FillCard()
        {
            if (!AchievementsData.Available()) return;
            AchievementsData.All(sets =>
            {
                try
                {
                    int earned = sets.Sum(s => s.Unlocked), total = sets.Sum(s => s.Total);
                    int done = sets.Count(s => s.Total > 0 && s.Unlocked == s.Total);
                    var res = Application.Current.Resources;
                    res["SeleneStatAchEarned"] = earned + " / " + total;
                    res["SeleneStatAchDone"] = done == 1 ? "1 game" : done + " games";
                    var recent = sets.SelectMany(s => s.Items.Where(i => i.Unlocked && i.When.HasValue).Select(i => new { s.GameId, Item = i }))
                                     .OrderByDescending(x => x.Item.When.Value).Take(4).ToList();
                    string headline = sets.Count == 0 ? "No achievements yet" : earned == 1 ? "1 achievement earned" : earned + " achievements earned";
                    string about = sets.Count == 0 ? "Your achievements show here as Playnite Achievements fills them in."
                                 : recent.Count > 0 ? "Latest: " + recent[0].Item.Name + ". Down for your newest unlocks." : "Across " + sets.Count + " games.";
                    var entries = recent.Select(x =>
                    {
                        var g = GameFacts.Find(x.GameId);
                        var id = x.GameId;
                        return new CardShowcase.Entry
                        {
                            Image = g != null ? (GameFacts.BackgroundPath(g) ?? GameFacts.CoverPath(g)) : null,
                            Title = x.Item.Name,
                            Sub = (g != null ? g.Name + "  ·  " : "") + GameFacts.Ago(x.Item.When),
                            Glyph = "",
                            Open = () => LoginScene.OpenWith(() => new AchievementsScreen(id, null))
                        };
                    }).ToList();
                    CardShowcase.Set("nav:achievements", "Achievements", headline, about, entries);
                }
                catch (Exception ex) { HomeNav.Log("achievements card: " + ex.Message); }
            });
        }
    }
}
