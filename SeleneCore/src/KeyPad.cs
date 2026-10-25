using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Selene
{
    // An on-screen keyboard for the controller (the same keys and look as the profile name step): the D-pad moves (rows
    // wrap at the ends), A presses, X erases, Y types a space, Start presses the last key. Typed text goes to Typed.
    public class KeyPad
    {
        private static readonly string[] Rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL'", "ZXCVBNM,.?" };
        public readonly StackPanel Root = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        public Action<string> Typed;
        public Action Erased, Done;
        private readonly List<List<Tile>> keys = new List<List<Tile>>();
        private readonly List<List<string>> values = new List<List<string>>();
        private readonly List<List<double>> centers = new List<List<double>>();
        private readonly List<TextBlock> letterLabels = new List<TextBlock>();
        private TextBlock doneLabel;
        private int row = 1, col;
        private bool shift, upperNext = true;

        public KeyPad(string doneText, double key = 62, double gap = 9)
        {
            var allRows = Rows.Select(r => r.Select(c => c.ToString()).ToList()).ToList();
            allRows.Add(new List<string> { "shift", "space", "erase", "done" });
            double widest = Rows.Max(r => r.Length) * (key + gap);
            foreach (var r in allRows)
            {
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, gap) };
                var rowKeys = new List<Tile>();
                var rowCenters = new List<double>();
                double x = (widest - r.Sum(v => Width(v, key) + gap)) / 2;
                foreach (var v in r)
                {
                    double w = Width(v, key);
                    var t = new Tile(w, key - 6, 14, 4) { FocusScale = 1.1 };
                    var label = new TextBlock { Text = Label(v, doneText), FontSize = v.Length == 1 ? 22 : 18, FontFamily = v == "shift" || v == "erase" ? Ui.Glyphs : Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    if (v.Length == 1 && char.IsLetter(v[0])) letterLabels.Add(label);
                    if (v == "done") doneLabel = label;
                    t.Face.Child = v == "done" || v == "erase" || v == "space" ? (UIElement)WithBadge(label, v == "done" ? "\u2261" : v == "space" ? "Y" : "X") : label;
                    t.Root.Margin = new Thickness(gap / 2, 0, gap / 2, 0);
                    rowPanel.Children.Add(t.Root);
                    rowKeys.Add(t);
                    rowCenters.Add(x + w / 2);
                    x += w + gap;
                }
                Root.Children.Add(rowPanel);
                keys.Add(rowKeys);
                values.Add(r);
                centers.Add(rowCenters);
            }
            UpdateCase();
        }

        public string DoneText { set { doneLabel.Text = value; } }

        // the case of the next letter: capital to start a sentence (the text so far tells it)
        public void After(string text)
        {
            var t = text.TrimEnd();
            upperNext = t.Length == 0 || t.EndsWith(".") || t.EndsWith("?") || t.EndsWith("!");
            UpdateCase();
        }

        private static Grid WithBadge(UIElement label, string button)
        {
            var g = new Grid();
            g.Children.Add(label);
            var badge = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 5, 0) };
            badge.Children.Add(new Ellipse { Fill = Ink.Brush("SeleneInkColor", 0.9) });
            badge.Children.Add(Ui.Letter(button, 11, FontWeights.Bold, Ink.Brush("SeleneScrimColor")));
            g.Children.Add(badge);
            return g;
        }

        private static double Width(string v, double key) { return v == "space" ? key * 4.4 : v.Length > 1 ? key * 2.1 : key; }

        private static string Label(string v, string doneText)
        {
            switch (v)
            {
                case "shift": return "\uE752";
                case "erase": return "\uE750";
                case "space": return "Space";
                case "done": return doneText;
            }
            return v;
        }

        public void Focus(bool on)
        {
            for (int r = 0; r < keys.Count; r++)
                for (int c = 0; c < keys[r].Count; c++) keys[r][c].Focus(on && r == row && c == col);
        }

        // false: the move went past the top row (the screen decides what's above)
        public bool Move(string dir)
        {
            int r = row, c = col;
            if (dir == "DPadLeft") c = c > 0 ? c - 1 : keys[r].Count - 1;
            else if (dir == "DPadRight") c = c < keys[r].Count - 1 ? c + 1 : 0;
            else
            {
                r += dir == "DPadUp" ? -1 : 1;
                if (r < 0) return false;
                if (r >= keys.Count) return true;
                double x = centers[row][col];
                c = 0;
                for (int i = 1; i < centers[r].Count; i++) if (Math.Abs(centers[r][i] - x) < Math.Abs(centers[r][c] - x)) c = i;
            }
            row = r; col = c;
            Ui.Tick();
            Focus(true);
            return true;
        }

        public void Press(string button)
        {
            if (button == "X") { Erased(); return; }
            if (button == "Y") { Typed(" "); return; }
            if (button == "Start") { Done(); return; }
            if (button != "A") return;
            var v = values[row][col];
            Ui.Pop(keys[row][col].Root);
            if (v == "done") Done();
            else if (v == "erase") Erased();
            else if (v == "shift") { shift = !shift; Ui.Key(); UpdateCase(); }
            else if (v == "space") Typed(" ");
            else
            {
                var s = (upperNext != shift) ? v.ToUpperInvariant() : v.ToLowerInvariant();
                shift = false;
                Typed(s);
            }
        }

        private void UpdateCase()
        {
            bool upper = upperNext != shift;
            foreach (var l in letterLabels) l.Text = upper ? l.Text.ToUpperInvariant() : l.Text.ToLowerInvariant();
        }
    }
}
