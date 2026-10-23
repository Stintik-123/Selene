using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Selene
{
    // Selene settings (from the menu): every feature in Features.All with an on/off switch. Up/Down moves, A flips the
    // switch (saved straight away), B goes back to the menu.
    public class SettingsScreen : Screen
    {
        private readonly List<Border> rows = new List<Border>();
        private readonly List<Border> knobs = new List<Border>();
        private readonly List<Border> tracks = new List<Border>();
        private readonly List<TextBlock> states = new List<TextBlock>();
        private int index;

        public override bool Overlay { get { return true; } }

        public SettingsScreen()
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 980 };
            col.Children.Add(Ui.Title("Selene settings"));
            var sub = Ui.Sub("Turn off anything you don't want. Changes are saved right away.");
            sub.Margin = new Thickness(0, 6, 0, 26);
            col.Children.Add(sub);
            foreach (var f in Features.All)
            {
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = f.Label, FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor") });
                text.Children.Add(new TextBlock { Text = f.About, FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                var state = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.7), VerticalAlignment = VerticalAlignment.Center, Width = 34, Margin = new Thickness(24, 0, 12, 0) };
                var knob = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0), Background = Brushes.White, RenderTransform = new TranslateTransform() };
                var track = new Border { Width = 56, Height = 30, CornerRadius = new CornerRadius(15), VerticalAlignment = VerticalAlignment.Center, Child = knob, BorderThickness = new Thickness(1), BorderBrush = Ink.Brush("SeleneInkColor", 0.25) };
                var line = new DockPanel();
                DockPanel.SetDock(track, Dock.Right);
                DockPanel.SetDock(state, Dock.Right);
                line.Children.Add(track);
                line.Children.Add(state);
                line.Children.Add(text);
                var row = new Border { Child = line, CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 12, 20, 12), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Ink.Brush("SeleneGlassColor", 0.6) };
                col.Children.Add(row);
                rows.Add(row); knobs.Add(knob); tracks.Add(track); states.Add(state);
            }
            Root.Children.Add(col);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Turn on / off")));
        }

        public override void Shown()
        {
            for (int i = 0; i < rows.Count; i++) { Ui.Rise(rows[i], 60 + i * 45, 14, 280); ShowSwitch(i, false); }
            Ui.Play("OpenPanel.wav", 0.8);
            ShowFocus();
        }

        private void ShowSwitch(int i, bool animate)
        {
            bool on = Features.On(Features.All[i].Key);
            var ms = TimeSpan.FromMilliseconds(animate ? 200 : 0);
            var slide = new DoubleAnimation(on ? 26 : 0, ms) { EasingFunction = Ui.Out() };
            ((TranslateTransform)knobs[i].RenderTransform).BeginAnimation(TranslateTransform.XProperty, slide);
            tracks[i].Background = on ? Ink.Brush("GlyphColor") : Ink.Brush("SeleneInkColor", 0.18);
            states[i].Text = on ? "On" : "Off";
        }

        private void ShowFocus()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].BorderBrush = i == index ? Ink.Brush("SeleneFocusColor") : Brushes.Transparent;
                rows[i].Background = i == index ? Ink.Brush("SeleneGlassColor", 0.9) : Ink.Brush("SeleneGlassColor", 0.6);
            }
        }

        public override void Handle(string b)
        {
            if (b == "B") { Ui.Back(); LoginScene.Show(new MenuScreen(), -1); return; }
            if (b == "A")
            {
                var key = Features.All[index].Key;
                Features.Set(key, !Features.On(key));
                Ui.Key();
                ShowSwitch(index, true);
                if (key == "hints") HomeHints.Update();
                if (key == "glow") LoginScene.RefreshBackdrop();
                if (key == "music" && !Features.On(key)) HomeNav.ReleaseMusic();
                HomeNav.Log("settings: " + key + " " + (Features.On(key) ? "on" : "off"));
                return;
            }
            int next = index + (b == "DPadDown" ? 1 : b == "DPadUp" ? -1 : 0);
            if (next == index || next < 0 || next >= rows.Count) return;
            index = next;
            Ui.Tick();
            ShowFocus();
        }
    }
}
