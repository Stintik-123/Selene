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
                if (picks.Count >= 5) break;
                if (!picks.Any(p => StoreData.Key(p.Title ?? "") == StoreData.Key(s.Title ?? ""))) picks.Add(s);
            }
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

        private static FrameworkElement Card(StoreItem item)
        {
            var col = new StackPanel { Width = 232, Margin = new Thickness(0, 0, 16, 0) };
            var frame = new Grid { Height = 109 };
            var pic = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
            frame.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Ink.Brush("SeleneInkColor", 0.08), Child = pic });
            StorePage.Load(pic, item.Image, 470);
            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = item.Free ? "FREE" : item.Discount > 0 ? "-" + item.Discount + "%" : null;
            if (badge != null)
                chip.Children.Add(new Border { Background = Ink.Brush("SeleneFocusColor"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), Child = new TextBlock { Text = badge, FontSize = 13, FontWeight = FontWeights.Bold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneOnFocusColor") } });
            var price = item.Free ? (item.Note ?? "") : item.Price ?? "";
            if (price.Length > 0) chip.Children.Add(new TextBlock { Text = price, FontSize = 14, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(badge != null ? 6 : 0, 0, 0, 0) });
            if (chip.Children.Count > 0)
                frame.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6), Padding = new Thickness(4, 3, 8, 3), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), Child = chip });
            col.Children.Add(frame);
            col.Children.Add(new TextBlock { Text = item.Title, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.85), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 6, 0, 0) });
            return col;
        }
    }
}
