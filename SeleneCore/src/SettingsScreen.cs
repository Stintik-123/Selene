using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Selene
{
    // Selene settings, like a console's settings screen: the groups on the left (Start-up, Navigation, Sound & music, Look,
    // Games), the chosen group's switches on the right. Up/Down picks a group; Right or A goes into its switches, where A
    // flips one (saved straight away); Left or B goes back to the groups; B there goes back to the menu.
    public class SettingsScreen : Screen
    {
        private static readonly int[] GroupIcons = { 0xEB9B, 0xEA28, 0xE050, 0xE40A, 0xE338 };

        private readonly List<Border> groupRows = new List<Border>();
        private readonly StackPanel optionList = new StackPanel();
        private readonly TextBlock groupTitle;
        private readonly List<Border> rows = new List<Border>();
        private readonly List<Border> knobs = new List<Border>();
        private readonly List<Border> tracks = new List<Border>();
        private List<Features.Option> options = new List<Features.Option>();
        private int group, index;
        private bool inOptions;

        public override bool Overlay { get { return true; } }

        // a deep, even shade: the settings read clearly over anything
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.94); } }

        public SettingsScreen()
        {
            var layout = new Grid { Margin = new Thickness(120, 110, 120, 110) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // left: title and the groups
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = "Settings", FontSize = 40, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), Margin = new Thickness(18, 0, 0, 6) });
            left.Children.Add(new TextBlock { Text = "Selene", FontSize = 16, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(20, 0, 0, 30) });
            for (int i = 0; i < Features.Groups.Length; i++)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(new TextBlock { Text = Ui.Icon(GroupIcons[i]), FontFamily = Ui.Icons, FontSize = 24, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
                line.Children.Add(new TextBlock { Text = Features.Groups[i], FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
                var row = new Border { Child = line, CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 14, 20, 14), Margin = new Thickness(0, 0, 24, 6) };
                left.Children.Add(row);
                groupRows.Add(row);
            }
            layout.Children.Add(left);

            // right: the chosen group's switches
            var right = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
            groupTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(8, 14, 0, 16) };
            right.Children.Add(groupTitle);
            right.Children.Add(optionList);
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);

            Root.Children.Add(layout);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
        }

        public override void Shown()
        {
            for (int i = 0; i < groupRows.Count; i++) Ui.Rise(groupRows[i], 40 + i * 40, 12, 260);
            Ui.Play("OpenPanel.wav", 0.8);
            ShowGroup(false);
            ShowFocus();
        }

        // the switches of the chosen group, rising in
        private void ShowGroup(bool animate)
        {
            options = Features.All.Where(o => o.Group == Features.Groups[group]).ToList();
            groupTitle.Text = Features.Groups[group].ToUpperInvariant();
            optionList.Children.Clear(); rows.Clear(); knobs.Clear(); tracks.Clear();
            for (int i = 0; i < options.Count; i++)
            {
                var f = options[i];
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = f.Label, FontSize = 21, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor") });
                text.Children.Add(new TextBlock { Text = f.About, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                var knob = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0), Background = Brushes.White, RenderTransform = new TranslateTransform() };
                var track = new Border { Width = 56, Height = 30, CornerRadius = new CornerRadius(15), VerticalAlignment = VerticalAlignment.Center, Child = knob, Margin = new Thickness(24, 0, 0, 0) };
                var line = new DockPanel();
                DockPanel.SetDock(track, Dock.Right);
                line.Children.Add(track);
                line.Children.Add(text);
                var row = new Border { Child = line, CornerRadius = new CornerRadius(16), Padding = new Thickness(26, 18, 24, 18), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Ink.Brush("SeleneGlassColor", 0.7) };
                optionList.Children.Add(row);
                rows.Add(row); knobs.Add(knob); tracks.Add(track);
                ShowSwitch(i, false);
                if (animate) Ui.Rise(row, i * 40, 10, 220);
            }
        }

        private void ShowSwitch(int i, bool animate)
        {
            bool on = Features.On(options[i].Key);
            var slide = new DoubleAnimation(on ? 26 : 0, TimeSpan.FromMilliseconds(animate ? 200 : 0)) { EasingFunction = Ui.Out() };
            ((TranslateTransform)knobs[i].RenderTransform).BeginAnimation(TranslateTransform.XProperty, slide);
            tracks[i].Background = on ? Ink.Brush("GlyphColor") : Ink.Brush("SeleneInkColor", 0.2);
        }

        private void ShowFocus()
        {
            for (int i = 0; i < groupRows.Count; i++)
            {
                bool chosen = i == group;
                groupRows[i].Background = chosen ? (inOptions ? Ink.Brush("SeleneInkColor", 0.1) : Ink.Brush("SeleneFocusColor")) : Brushes.Transparent;
                foreach (var t in ((StackPanel)groupRows[i].Child).Children.OfType<TextBlock>())
                    t.Foreground = chosen && !inOptions ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor", chosen ? 1 : 0.7);
            }
            for (int i = 0; i < rows.Count; i++)
                rows[i].BorderBrush = inOptions && i == index ? Ink.Brush("SeleneFocusColor") : Brushes.Transparent;
        }

        public override void Handle(string b)
        {
            if (!inOptions)
            {
                if (b == "B") { Ui.Back(); LoginScene.Show(new MenuScreen(), -1); return; }
                if ((b == "A" || b == "DPadRight") && options.Count > 0) { inOptions = true; index = 0; Ui.Tick(); ShowFocus(); return; }
                int next = group + (b == "DPadDown" ? 1 : b == "DPadUp" ? -1 : 0);
                if (next == group || next < 0 || next >= groupRows.Count) return;
                group = next;
                Ui.Tick();
                ShowGroup(true);
                ShowFocus();
                return;
            }
            if (b == "B" || b == "DPadLeft") { inOptions = false; Ui.Back(); ShowFocus(); return; }
            if (b == "A")
            {
                var key = options[index].Key;
                Features.Set(key, !Features.On(key));
                Ui.Key();
                ShowSwitch(index, true);
                if (key == "hints") HomeHints.Update();
                if (key == "glow") LoginScene.RefreshBackdrop();
                if (key == "music" && !Features.On(key)) HomeNav.ReleaseMusic();
                HomeNav.Log("settings: " + key + " " + (Features.On(key) ? "on" : "off"));
                return;
            }
            int n = index + (b == "DPadDown" ? 1 : b == "DPadUp" ? -1 : 0);
            if (n == index || n < 0 || n >= rows.Count) return;
            index = n;
            Ui.Tick();
            ShowFocus();
        }
    }
}
