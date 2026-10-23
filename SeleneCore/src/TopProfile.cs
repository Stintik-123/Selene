using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Selene
{
    // The signed-in profile in the top bar (Spotlight and Collection), left of the clock: avatar and name. Follows signing
    // in, switching and editing profiles (Profiles.ApplyLook). Switch: "topprofile".
    public static class TopProfile
    {
        private static Grid slot;

        public static void Attach(Window w)
        {
            slot = w != null ? HomeNav.FindByName(w, "SeleneTopProfile") as Grid : null;
            if (slot == null) { HomeNav.Log("top profile: no slot"); return; }
            Refresh();
        }

        public static void Refresh()
        {
            if (slot == null) return;
            var p = Profiles.Active;
            if (p == null || !Features.On("topprofile")) { slot.Children.Clear(); slot.Visibility = Visibility.Collapsed; return; }
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var avatar = WhoScreen.Avatar(p, 44);
            avatar.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(avatar);
            line.Children.Add(new TextBlock
            {
                Text = p.Name, FontSize = 22, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            });
            slot.Children.Clear();
            slot.Children.Add(line);
            slot.Visibility = Visibility.Visible;
            // a new profile fades in instead of popping
            line.Opacity = 0;
            line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
