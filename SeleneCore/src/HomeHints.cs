using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Selene
{
    // The home's button hints, bottom right: what A / X / Y / R3 / Start do for whatever is focused right now
    public static class HomeHints
    {
        private static Grid host;
        private static StackPanel bar;
        private static string shown = "";

        public static void Attach(Window w)
        {
            var hub = HomeNav.FindByName(w, "WelcomeHubOverlay") as Grid;
            if (hub == null || host == hub) return;
            host = hub;
            bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 64, 40), IsHitTestVisible = false };
            Panel.SetZIndex(bar, 60);
            Grid.SetRowSpan(bar, 99);
            hub.Children.Add(bar);
            hub.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => Update()), true);
            Update();
        }

        // what's focused decides the hints; the bar crossfades when they change
        public static void Update()
        {
            if (bar == null) return;
            var focused = Keyboard.FocusedElement as FrameworkElement;
            string key;
            string[] hints;
            var item = focused as ListBoxItem;
            if (HomeNav.Moving) { key = "move"; hints = new[] { "A", "Drop", "B", "Cancel" }; }
            else if (item != null && HomeNav.InRow(item))
            {
                if (item.DataContext is string) { key = "card"; hints = new[] { "A", "Open", "Y", "Move", "≡", "Menu" }; }
                else if (HomeNav.HasTrailer(item.DataContext)) { key = "game+"; hints = new[] { "A", "Play", "X", "Favorite", "Y", "Move", "R3", "Trailer sound", "≡", "Menu" }; }
                else { key = "game"; hints = new[] { "A", "Play", "X", "Favorite", "Y", "Move", "≡", "Menu" }; }
            }
            else if (focused != null && focused.Name.StartsWith("SeleneShotTile")) { key = "shot"; hints = new[] { "A", "View", "≡", "Menu" }; }
            else if (focused != null && (focused.Name == "SelenePlayBtn" || focused.Name == "SeleneDetailsBtn" || focused.Name == "SeleneFavBtn")) { key = "pill"; hints = new[] { "A", "Select", "≡", "Menu" }; }
            else { key = "other"; hints = new[] { "≡", "Menu" }; }
            if (key == shown) return;
            shown = key;
            bar.Children.Clear();
            for (int i = 0; i + 1 < hints.Length; i += 2) bar.Children.Add(Ui.Prompt(hints[i], hints[i + 1]));
            bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ui.Out() });
        }
    }
}
