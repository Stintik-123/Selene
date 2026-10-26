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
    // The Achievements card: every game's achievements together (Playnite Achievements' data), like a console's trophy
    // screen. The totals as glass cards and the trophies earned (platinum, gold, silver, bronze) at the top right, the
    // latest unlocks as a row, then every game as a list with its share and trophies. The focused game's art glows behind.
    // A opens that game's list, B goes back.
    public class AchievementsHub : Screen
    {
        private const double Side = 96, TileW = 104, Pitch = TileW + 18, RowTop = 172, ListTop = 430;
        private const int Recent = 12;

        private class Pick { public Guid Game; public AchievementsData.Item Item; }
        private readonly List<Pick> recent = new List<Pick>();
        private readonly List<Tile> recentTiles = new List<Tile>();
        private readonly StackPanel recentStrip = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly TranslateTransform recentShift = new TranslateTransform();
        private readonly List<AchievementsData.GameSet> games = new List<AchievementsData.GameSet>();
        private readonly List<Border> gameRows = new List<Border>();
        private readonly StackPanel list = new StackPanel();
        private readonly ScrollViewer scroller;
        private readonly Image hero = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
        private readonly Image heroBack = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };   // the art going, under the one coming
        private readonly DispatcherTimer heroLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private readonly StackPanel head;
        private readonly Grid summary = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 62, Side, 0) };
        private readonly TextBlock note, recentTitle, gamesTitle;
        private int zone, col, row, keepZone, keepIndex;   // zone 0: the latest unlocks (col), 1: the games (row)
        private bool closed, loaded;
        private string heroShown;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.96); } }

        public AchievementsHub() : this(0, 0) { }

        // (zone, index): where the focus was, when coming back from a game's list
        public AchievementsHub(int zone, int index)
        {
            keepZone = zone; keepIndex = index;
            RenderOptions.SetBitmapScalingMode(hero, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(heroBack, BitmapScalingMode.HighQuality);
            Root.Children.Add(heroBack);
            Root.Children.Add(hero);
            Root.Children.Add(new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0.94, 0), Ink.Stop("SeleneScrimColor", 0.72, 0.5), Ink.Stop("SeleneScrimColor", 0.35, 1) }, 0) });
            Root.Children.Add(new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { Ink.Stop("SeleneScrimColor", 0, 0.25), Ink.Stop("SeleneScrimColor", 0.9, 1) }, 90) });

            head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, 56, 0, 0) };
            head.Children.Add(new TextBlock { Text = "Achievements", FontSize = 44, FontFamily = Ui.Display, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor") });
            Root.Children.Add(head);
            Root.Children.Add(summary);

            // the latest unlocks: one row that moves along with the focus
            recentTitle = RowTitle("Recently unlocked");
            recentTitle.Margin = new Thickness(Side + 2, RowTop, 0, 0);
            Root.Children.Add(recentTitle);
            var recentArea = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, RowTop + 40, 0, 0), ClipToBounds = false };
            recentStrip.Margin = new Thickness(Side, 0, 0, 0);
            recentStrip.HorizontalAlignment = HorizontalAlignment.Left;
            recentStrip.RenderTransform = recentShift;
            recentArea.Children.Add(recentStrip);
            Root.Children.Add(recentArea);

            // every game, as a list
            gamesTitle = RowTitle("Your games");
            gamesTitle.Margin = new Thickness(Side + 2, ListTop, 0, 0);
            Root.Children.Add(gamesTitle);
            scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, CanContentScroll = false };
            var listArea = PeekScroller.Wrap(scroller, list);
            listArea.VerticalAlignment = VerticalAlignment.Top;
            listArea.Margin = new Thickness(Side, ListTop + 44, Side, 0);
            listArea.Height = 1080 - (ListTop + 44) - 110;
            Root.Children.Add(listArea);

            note = new TextBlock { Text = "", FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Side, RowTop + 20, 0, 0) };
            Root.Children.Add(note);
            recentTitle.Visibility = gamesTitle.Visibility = Visibility.Collapsed;
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Open")));
            heroLater.Tick += (s, e) => { heroLater.Stop(); ShowHero(); };
        }

        private static TextBlock RowTitle(string text)
        {
            return new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
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
            int platinums = sets.Count(s => s.Platinum);
            var unlocked = sets.SelectMany(s => s.Items.Where(i => i.Unlocked).Select(i => new Pick { Game = s.GameId, Item = i })).ToList();

            // the trophies at the top right, as a console's trophy screen: platinum, gold, silver, bronze
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            strip.Children.Add(Count(null, earned.ToString(), "EARNED"));
            foreach (var grade in new[] { "Platinum", "Gold", "Silver", "Bronze" })
                strip.Children.Add(Count(grade, grade == "Platinum" ? platinums.ToString() : sets.Sum(s => s.Earned(grade)).ToString(), grade.ToUpperInvariant()));
            summary.Children.Add(strip);
            Ui.Rise(summary, 120, 12, 320);

            // the latest unlocks
            recent.AddRange(unlocked.Where(p => p.Item.When.HasValue).OrderByDescending(p => p.Item.When.Value).Take(Recent));
            BuildRecent();
            // every game: the latest played-for first
            games.AddRange(sets.OrderByDescending(s => s.LastUnlock ?? DateTime.MinValue).ThenByDescending(s => s.Unlocked).ThenBy(s => s.Name));
            BuildGames();

            recentTitle.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            gamesTitle.Visibility = Visibility.Visible;
            zone = recent.Count == 0 ? 1 : Math.Min(1, keepZone);
            if (zone == 0) col = Math.Min(keepIndex, recent.Count - 1); else row = Math.Min(keepIndex, games.Count - 1);
            ShowFocus(false);
        }

        // a trophy count at the top right: the trophy over its number, the grade's name under
        private static StackPanel Count(string grade, string value, string label)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 0, 0, 0), MinWidth = 70 };
            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (grade != null)
            {
                var t = AchievementsData.TrophyIcon(grade, 30);
                t.Margin = new Thickness(0, 0, 8, 0);
                top.Children.Add(t);
            }
            top.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(top);
            sp.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            return sp;
        }

        private static Border Card(string label, string value, string sub)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6) });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(0, 2, 0, 0), MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 14, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(0, 2, 0, 0) });
            return new Border { Child = sp, CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 14, 26, 16), Margin = new Thickness(0, 0, 14, 0), MinWidth = 200, Background = Ink.Brush("SeleneGlassColor", 0.55), BorderBrush = Ink.Brush("SeleneInkColor", 0.12), BorderThickness = new Thickness(1) };
        }

        // ---- the latest unlocks: each one's own picture, its name, the game, its trophy ----
        private void BuildRecent()
        {
            var files = new List<string>();
            var at = new List<int>();
            for (int i = 0; i < recent.Count; i++)
            {
                var p = recent[i];
                var t = new Tile(TileW, TileW, 14);
                t.Face.Child = AchievementsData.TrophyIcon(p.Item.Grade, 36);
                ((TextBlock)t.Face.Child).HorizontalAlignment = HorizontalAlignment.Center;
                if (p.Item.Icon != null) { files.Add(p.Item.Icon); at.Add(i); }
                var game = GameFacts.Find(p.Game);
                var item = new StackPanel { Width = TileW, Margin = new Thickness(0, 0, Pitch - TileW, 0) };
                item.Children.Add(t.Root);
                item.Children.Add(new TextBlock { Text = p.Item.Name, FontSize = 13, FontFamily = Ui.Body, FontWeight = FontWeights.SemiBold, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 9, 0, 0) });
                item.Children.Add(new TextBlock { Text = game != null ? game.Name : "", FontSize = 12, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 1, 0, 0) });
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 0, 0) };
                var trophy = AchievementsData.TrophyIcon(p.Item.Grade, 13);
                trophy.Margin = new Thickness(0, 0, 5, 0);
                line.Children.Add(trophy);
                line.Children.Add(new TextBlock { Text = GameFacts.Ago(p.Item.When), FontSize = 12, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.5), VerticalAlignment = VerticalAlignment.Center });
                item.Children.Add(line);
                recentStrip.Children.Add(item);
                recentTiles.Add(t);
                Ui.Rise(item, 140 + i * 35, 14, 300);
            }
            Ui.LoadInOrder(files, 300, (k, pic) =>
            {
                if (closed || k >= at.Count) return;
                var face = new Border { CornerRadius = new CornerRadius(13), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill }, Opacity = 0 };
                recentTiles[at[k]].Face.Child = face;
                Ui.Fade(face, 1, 240);
            });
        }

        // ---- every game, as a console lists them: picture, name and store, share with a line, trophies ----
        private void BuildGames()
        {
            var files = new List<string>();
            var faces = new List<Border>();
            for (int i = 0; i < games.Count; i++)
            {
                var s = games[i];
                var g = GameFacts.Find(s.GameId);
                var face = new Border { Width = 68, Height = 100, CornerRadius = new CornerRadius(10),   /* a cover's shape: the whole cover shows */ Background = Ink.Brush("SeleneInkColor", 0.08), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
                var cover = g != null ? GameFacts.CoverPath(g) : null;
                if (cover != null) { files.Add(cover); faces.Add(face); }

                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock { Text = g != null ? g.Name : s.Name, FontSize = 25, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), TextTrimming = TextTrimming.CharacterEllipsis });
                var store = g != null && g.Source != null ? g.Source.Name : null;
                if (!string.IsNullOrEmpty(store))
                    words.Children.Add(new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = Ink.Brush("SeleneInkColor", 0.35), BorderThickness = new Thickness(1), Child = new TextBlock { Text = store, FontSize = 14, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.7) } });

                // the share, a short line under it, then the trophies
                var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var share = new StackPanel { Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 26, 0) };
                share.Children.Add(new TextBlock { Text = s.Percent + "%", FontSize = 34, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Right });
                var track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ink.Brush("SeleneInkColor", 0.2), Margin = new Thickness(20, 4, 0, 0) };
                track.Child = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ink.Brush(s.Platinum ? "GlyphColor" : "SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, 126 * s.Unlocked / Math.Max(1.0, s.Total)) };
                share.Children.Add(track);
                side.Children.Add(share);
                foreach (var grade in new[] { "Platinum", "Gold", "Silver", "Bronze" })
                {
                    var cell = new StackPanel { Orientation = Orientation.Horizontal, Width = grade == "Platinum" ? 70 : 96, VerticalAlignment = VerticalAlignment.Center };
                    bool platinum = grade == "Platinum";
                    var icon = AchievementsData.TrophyIcon(grade, 32);
                    icon.Margin = new Thickness(0, 0, 6, 0);
                    if (platinum && !s.Platinum) icon.Opacity = 0.25;
                    cell.Children.Add(icon);
                    if (!platinum) cell.Children.Add(new TextBlock { Text = s.Earned(grade).ToString(), FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.85), VerticalAlignment = VerticalAlignment.Center });
                    side.Children.Add(cell);
                }

                var line = new DockPanel();
                DockPanel.SetDock(face, Dock.Left);
                DockPanel.SetDock(side, Dock.Right);
                line.Children.Add(face);
                line.Children.Add(side);
                line.Children.Add(words);
                var r = new Border { Child = line, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 12, 26, 12), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Ink.Brush("SeleneGlassColor", 0.55) };
                list.Children.Add(r);
                gameRows.Add(r);
                if (i < 5) Ui.Rise(r, 200 + i * 40, 12, 280);
            }
            Ui.LoadInOrder(files, 160, (k, pic) =>
            {
                if (closed || k >= faces.Count) return;
                faces[k].Child = new Border { Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } };
            });
        }

        private Guid? FocusedGame
        {
            get
            {
                if (zone == 0 && col < recent.Count) return recent[col].Game;
                if (zone == 1 && row < games.Count) return games[row].GameId;
                return null;
            }
        }

        private void ShowFocus(bool animate)
        {
            for (int i = 0; i < recentTiles.Count; i++) recentTiles[i].Focus(zone == 0 && i == col);
            for (int i = 0; i < gameRows.Count; i++)
            {
                bool on = zone == 1 && i == row;
                gameRows[i].BorderBrush = on ? Ink.Brush("SeleneFocusColor") : Brushes.Transparent;
                gameRows[i].Background = Ink.Brush("SeleneGlassColor", on ? 0.9 : 0.55);
            }
            // the row of unlocks moves along once the focus goes past its first tiles
            recentShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-Math.Max(0, col - 12) * Pitch, TimeSpan.FromMilliseconds(animate ? 240 : 0)) { EasingFunction = Ui.Out() });
            if (zone == 1 && row < gameRows.Count)
            {
                try
                {
                    scroller.UpdateLayout();
                    var r = gameRows[row];
                    var top = r.TranslatePoint(new Point(0, 0), list).Y;
                    double want = scroller.VerticalOffset, pad = PeekScroller.Band;
                    if (top - pad * 0.6 < scroller.VerticalOffset) want = Math.Max(0, top - pad * 0.6);
                    else if (top + r.ActualHeight > scroller.VerticalOffset + scroller.ViewportHeight - pad) want = top + r.ActualHeight - (scroller.ViewportHeight - pad);
                    if (Math.Abs(want - scroller.VerticalOffset) > 0.5) scroller.ScrollToVerticalOffset(want);
                }
                catch { }
            }
            heroLater.Stop();
            heroLater.Start();
        }

        private void ShowHero()
        {
            var id = FocusedGame;
            var g = id.HasValue ? GameFacts.Find(id.Value) : null;
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

        public override void Handle(string b)
        {
            if (b == "B") { Ui.Back(); LoginScene.Finish(); return; }
            if (!loaded || (recent.Count == 0 && games.Count == 0)) return;
            switch (b)
            {
                case "DPadLeft":
                case "DPadRight":
                {
                    if (zone != 0) return;
                    int next = col + (b == "DPadRight" ? 1 : -1);
                    if (next < 0 || next >= recent.Count) { Ui.Shake(recentTiles[col].Root); return; }
                    col = next;
                    Ui.Tick();
                    ShowFocus(true);
                    return;
                }
                case "DPadUp":
                case "DPadDown":
                {
                    bool down = b == "DPadDown";
                    if (zone == 0)
                    {
                        if (!down || games.Count == 0) return;
                        zone = 1; row = 0;
                    }
                    else
                    {
                        int next = row + (down ? 1 : -1);
                        if (next < 0) { if (recent.Count == 0) return; zone = 0; }
                        else if (next >= games.Count) { Ui.Shake(gameRows[row]); return; }
                        else row = next;
                    }
                    Ui.Tick();
                    ShowFocus(true);
                    return;
                }
                case "A":
                {
                    var id = FocusedGame;
                    if (!id.HasValue) return;
                    Ui.Confirm();
                    int z = zone, at = zone == 0 ? col : row;
                    LoginScene.Show(new AchievementsScreen(id.Value, () => new AchievementsHub(z, at)), 1);
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
                    int done = sets.Count(s => s.Platinum);
                    var res = Application.Current.Resources;
                    res["SeleneStatAchEarned"] = earned + " / " + total;
                    res["SeleneStatAchDone"] = done == 1 ? "1 platinum" : done + " platinums";
                    var latest = sets.SelectMany(s => s.Items.Where(i => i.Unlocked && i.When.HasValue).Select(i => new { s.GameId, Item = i }))
                                     .OrderByDescending(x => x.Item.When.Value).Take(4).ToList();
                    string headline = sets.Count == 0 ? "No achievements yet" : earned == 1 ? "1 achievement earned" : earned + " achievements earned";
                    string about = sets.Count == 0 ? "Your achievements show here as Playnite Achievements fills them in."
                                 : latest.Count > 0 ? "Latest: " + latest[0].Item.Name + ". Down for your newest unlocks." : "Across " + sets.Count + " games.";
                    var entries = latest.Select(x =>
                    {
                        var g = GameFacts.Find(x.GameId);
                        var id = x.GameId;
                        return new CardShowcase.Entry
                        {
                            Image = g != null ? (GameFacts.BackgroundPath(g) ?? GameFacts.CoverPath(g)) : null,
                            Title = x.Item.Name,
                            Sub = (g != null ? g.Name + "  ·  " : "") + x.Item.Grade + "  ·  " + GameFacts.Ago(x.Item.When),
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
