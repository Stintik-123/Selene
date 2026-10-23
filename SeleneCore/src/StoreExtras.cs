using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // Picking the Store's currency: asked the first time the Store opens, and with X in the Store after that
    public class StoreCurrencyScreen : Screen
    {
        private const int Columns = 5;
        private readonly bool first;
        private readonly List<Tile> tiles = new List<Tile>();
        private int index;

        public StoreCurrencyScreen(bool first)
        {
            this.first = first;
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -30, 0, 0) };
            col.Children.Add(Ui.Title("Choose your currency"));
            col.Children.Add(Ui.Sub("The stores show their prices in it. GOG and the other stores' deals stay in dollars. Change it any time with X in the Store."));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Columns, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 0) };
            foreach (var c in StoreData.Currencies)
            {
                var tile = new Tile(220, 112, 16, 5);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = c.Code, FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = c.Name, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                tile.Face.Child = sp;
                tile.Root.Margin = new Thickness(12);
                grid.Children.Add(tile.Root);
                tiles.Add(tile);
            }
            col.Children.Add(grid);
            Root.Children.Add(col);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Use this currency")));
            // this PC's own currency (or the one picked before) starts focused
            index = Math.Max(0, Array.FindIndex(StoreData.Currencies, c => c.Code == StoreData.CurrencyCode));
        }

        public override void Shown()
        {
            for (int i = 0; i < tiles.Count; i++) Ui.Rise(tiles[i].Root, 80 + i * 30, 14, 260);
            ShowFocus();
        }

        private void ShowFocus() { for (int i = 0; i < tiles.Count; i++) tiles[i].Focus(i == index); }

        public override void Handle(string b)
        {
            if (b == "B") { Ui.Back(); if (first) LoginScene.Finish(); else LoginScene.Show(new StorePage(), -1); return; }
            if (b.StartsWith("DPad"))
            {
                int next = Ui.GridMove(index, b, tiles.Count, Columns);
                if (next < 0) return;
                index = next;
                Ui.Tick();
                ShowFocus();
                return;
            }
            if (b == "A")
            {
                Ui.Confirm();
                StoreData.SetCountry(StoreData.Currencies[index].Country);
                LoginScene.Show(new StorePage(), 1);
            }
        }
    }

    // The Store card on Spotlight: a few games as small pictures with their price, this week's free ones first, then
    // the biggest sales
    public static class StoreShowcase
    {
        private static string shownKey;

        public static void Fill()
        {
            var w = Application.Current.MainWindow;
            var panel = w != null ? HomeNav.FindByName(w, "SeleneStoreShowcase") as StackPanel : null;
            if (panel == null) return;
            var picks = StoreData.List("epic:free").Take(2).ToList();
            var sales = StoreData.List("steam:sale").Concat(StoreData.List("epic:sale"))
                .Where(i => i.Discount > 0 && !i.Owned && !string.IsNullOrEmpty(i.Image)).OrderByDescending(i => i.Discount);
            foreach (var s in sales)
            {
                if (picks.Count >= 4) break;   // four big pictures across the card
                if (!picks.Any(p => StoreData.Key(p.Title ?? "") == StoreData.Key(s.Title ?? ""))) picks.Add(s);
            }
            Headline(picks);
            var key = string.Join("|", picks.Select(p => p.Title + p.Price));
            if (key == shownKey) return;
            shownKey = key;
            panel.Children.Clear();
            for (int i = 0; i < picks.Count; i++)
            {
                var card = Card(picks[i]);
                panel.Children.Add(card);
                Ui.Rise(card, i * 50, 12, 280);
            }
        }

        // the card's headline is what's on now (the card's own name already says "Store")
        private static void Headline(List<StoreItem> picks)
        {
            var res = Application.Current.Resources;
            var free = StoreData.List("epic:free");
            var best = StoreData.List("steam:sale").Concat(StoreData.List("epic:sale")).Concat(StoreData.List("gog:sale")).Where(i => i.Discount > 0 && !i.Free).OrderByDescending(i => i.Discount).FirstOrDefault();
            string head, about;
            if (free.Count > 0)
            {
                head = free.Count == 1 ? "Free this week" : free.Count + " free games this week";
                var names = free.Select(i => i.Title).ToList();
                var list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();
                var until = free[0].Note != null && free[0].Note.StartsWith("Free until ") ? free[0].Note.Substring(11) : null;
                about = list + (names.Count == 1 ? " is" : " are") + " free to keep on Epic Games" + (until != null ? " until " + until : "") + ".";
                if (best != null) about += " Up to " + best.Discount + "% off on " + best.StoreName + " right now.";
            }
            else if (best != null)
            {
                head = "Up to " + best.Discount + "% off";
                about = "The biggest sales across your stores right now. You buy in the store's own app.";
            }
            else return;
            res["SeleneStoreHeadline"] = head;
            res["SeleneStoreAbout"] = about;
        }

        // the store apps on this PC as chips with their logos (instead of the plain list)
        public static void FillChips()
        {
            var w = Application.Current.MainWindow;
            var panel = w != null ? HomeNav.FindByName(w, "SeleneStoreChips") as StackPanel : null;
            var names = w != null ? HomeNav.FindByName(w, "SeleneStoreNames") as UIElement : null;
            if (panel == null) return;
            panel.Children.Clear();
            foreach (var store in StoreScreen.Installed())
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = PlatformIcons.For(store);
                if (icon != null)
                    line.Children.Add(new System.Windows.Shapes.Path { Data = icon, Fill = Ink.Brush("SeleneInkColor"), Stretch = Stretch.Uniform, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                line.Children.Add(new TextBlock { Text = store, FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(new Border
                {
                    Child = line, CornerRadius = new CornerRadius(20), Padding = new Thickness(14, 7, 18, 8), Margin = new Thickness(0, 0, 10, 0),
                    Background = Ink.Brush("SeleneInkColor", 0.1), BorderBrush = Ink.Brush("SeleneInkColor", 0.18), BorderThickness = new Thickness(1)
                });
            }
            if (names != null) names.Visibility = panel.Children.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private static FrameworkElement Card(StoreItem item)
        {
            // no name under it: the pictures carry the game's title themselves, so they get the room instead
            var col = new StackPanel { Width = 420, Margin = new Thickness(0, 0, 22, 0) };
            var frame = new Grid { Height = 236 };
            var pic = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
            frame.Children.Add(new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Ink.Brush("SeleneInkColor", 0.08), Child = pic });
            StorePage.Load(pic, StoreData.Sharp(item), 1000);   // sharp at high-DPI too
            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = item.Free ? "FREE" : item.Discount > 0 ? "-" + item.Discount + "%" : null;
            if (badge != null)
                chip.Children.Add(new Border { Background = Ink.Brush("SeleneFocusColor"), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 3), Child = new TextBlock { Text = badge, FontSize = 17, FontWeight = FontWeights.Bold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneOnFocusColor") } });
            var price = item.Free ? (item.Note ?? "") : item.Price ?? "";
            if (price.Length > 0) chip.Children.Add(new TextBlock { Text = price, FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(badge != null ? 8 : 0, 0, 0, 0) });
            if (chip.Children.Count > 0)
                frame.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10), Padding = new Thickness(5, 4, 10, 4), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), Child = chip });
            col.Children.Add(frame);
            return col;
        }
    }
}
