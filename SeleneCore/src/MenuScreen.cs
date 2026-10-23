using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Selene
{
    // Selene's own menu (replaces Playnite's list), like a console's control center: one row of solid icons along the
    // bottom over the lightly shaded home. The focused icon gets a ring and its name above it; Power opens a short list
    // (sleep, restart, shut down, exit) above its icon. Start or the menu button opens it; B or Start closes it.
    public class MenuScreen : Screen
    {
        private class Item
        {
            public string Icon, Label;
            public FrameworkElement Picture;   // instead of an icon (the profile's avatar)
            public Action Run;
        }

        private const double Cell = 96, Ring = 70;
        private readonly List<Item> items = new List<Item>();
        private readonly List<Grid> cells = new List<Grid>();
        private readonly List<Ellipse> rings = new List<Ellipse>();
        private readonly List<TextBlock> labels = new List<TextBlock>();
        private readonly List<ScaleTransform> scales = new List<ScaleTransform>();
        private readonly StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 70) };
        private readonly TextBlock status = new TextBlock { FontSize = 16, FontFamily = Ui.Body, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 30) };
        private Border powerList;
        private readonly List<Border> powerRows = new List<Border>();
        private int index, powerIndex;
        private bool leaving;

        private static readonly object[][] PowerItems =
        {
            new object[] { 0xef44, "Sleep", 0 },
            new object[] { 0xf053, "Restart", 1 },
            new object[] { 0xe8ac, "Shut down", 2 },
            new object[] { 0xe9ba, "Exit Playnite", -1 },
        };

        public override bool Overlay { get { return true; } }

        // the home stays in view: a light shade at the top, deeper toward the icons
        public override Brush OverlayShade
        {
            get
            {
                var c = ((SolidColorBrush)Ink.Brush("SeleneScrimColor")).Color;
                return new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x66, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(0x99, c.R, c.G, c.B), 0.55),
                    new GradientStop(Color.FromArgb(0xEE, c.R, c.G, c.B), 1)
                }, 90);
            }
        }

        public MenuScreen()
        {
            var profile = Profiles.Active;
            items.Add(new Item { Icon = Ui.Icon(0xe853), Label = profile != null ? profile.Name + " · Switch profile" : "Switch profile", Picture = profile != null ? WhoScreen.Avatar(profile, 52) : null, Run = () => LoginScene.Show(new WhoScreen(null), 1) });
            items.Add(new Item { Icon = Ui.Icon(0xe3c9), Label = "Edit profile", Run = EditProfile });
            items.Add(new Item { Icon = Ui.Icon(0xe627), Label = "Update library", Run = UpdateLibrary });
            items.Add(new Item { Icon = Ui.Icon(0xeb40), Label = "Random game", Run = () => Later(() => PlayniteMenu.Main("SelectRandomGameCommand")) });
            items.Add(new Item { Icon = Ui.Icon(0xe429), Label = "Selene settings", Run = () => LoginScene.Show(new SettingsScreen(), 1) });
            items.Add(new Item { Icon = Ui.Icon(0xf0c5), Label = "Setup check", Run = () => LoginScene.Show(new SetupScreen(false), 1) });
            items.Add(new Item { Icon = Ui.Icon(0xe8b8), Label = "Playnite settings", Run = () => Later(PlayniteMenu.OpenSettings) });
            items.Add(new Item { Icon = Ui.Icon(0xe87b), Label = "Extensions", Run = () => Later(PlayniteMenu.OpenExtensions) });
            items.Add(new Item { Icon = Ui.Icon(0xe30c), Label = "Desktop mode", Run = () => Later(() => PlayniteMenu.Main("SwitchToDesktopCommand")) });
            items.Add(new Item { Icon = Ui.Icon(0xe5d3), Label = "More", Run = () => Later(PlayniteMenu.OpenOriginal) });
            items.Add(new Item { Icon = Ui.Icon(0xe8ac), Label = "Power", Run = OpenPower });

            foreach (var item in items)
            {
                var cell = new Grid { Width = Cell, Height = Ring + 44 };
                // the name floats above the icon, wider than the cell
                var label = new TextBlock
                {
                    Text = item.Label, FontSize = 18, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Center,
                    Width = 260, Margin = new Thickness(-82, 0, -82, 0), RenderTransform = new TranslateTransform(0, 6)
                };
                var dot = new Grid { Width = Ring, Height = Ring, VerticalAlignment = VerticalAlignment.Bottom, RenderTransformOrigin = new Point(0.5, 0.5) };
                var scale = new ScaleTransform(1, 1);
                dot.RenderTransform = scale;
                var ring = new Ellipse { StrokeThickness = 3, Stroke = Ink.Brush("SeleneInkColor"), Fill = Ink.Brush("SeleneInkColor", 0.12), Opacity = 0 };
                dot.Children.Add(ring);
                if (item.Picture != null) { item.Picture.HorizontalAlignment = HorizontalAlignment.Center; item.Picture.VerticalAlignment = VerticalAlignment.Center; dot.Children.Add(item.Picture); }
                else dot.Children.Add(new TextBlock { Text = item.Icon, FontFamily = Ui.Icons, FontSize = 34, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                cell.Children.Add(label);
                cell.Children.Add(dot);
                bar.Children.Add(cell);
                cells.Add(cell); rings.Add(ring); labels.Add(label); scales.Add(scale);
            }
            Root.Children.Add(bar);
            status.Foreground = Ink.Brush("SeleneInkColor", 0.75);
            Root.Children.Add(status);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Close")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
        }

        public override void Shown()
        {
            // the icons rise in from the bottom one after another
            for (int i = 0; i < cells.Count; i++) Ui.Rise(cells[i], 40 + i * 25, 24, 260);
            Ui.Play("OpenPanel.wav", 0.8);
            ShowFocus(false);
        }

        private void ShowFocus(bool animate)
        {
            var ms = TimeSpan.FromMilliseconds(animate ? 180 : 0);
            for (int i = 0; i < cells.Count; i++)
            {
                bool on = i == index;
                rings[i].BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, ms));
                labels[i].BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, ms));
                ((TranslateTransform)labels[i].RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? 0 : 6, ms) { EasingFunction = Ui.Out() });
                var grow = new DoubleAnimation(on ? 1.1 : 1, ms) { EasingFunction = Ui.Out() };
                scales[i].BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }

        public override void Handle(string b)
        {
            if (leaving) return;
            if (powerList != null) { HandlePower(b); return; }
            if (b == "B" || b == "Start") { leaving = true; Ui.Back(); LoginScene.Finish(); return; }
            if (b == "A")
            {
                var item = items[index];
                Ui.Confirm();
                try { item.Run(); }
                catch (Exception ex) { HomeNav.Log("menu: " + item.Label + " failed: " + ex); Ui.Shake(cells[index]); }
                return;
            }
            int next = index + (b == "DPadLeft" ? -1 : b == "DPadRight" ? 1 : 0);
            if (next == index || next < 0 || next >= items.Count) return;
            index = next;
            status.Text = "";
            Ui.Tick();
            ShowFocus(true);
        }

        // ---- Power: a short list above its icon ----
        private void OpenPower()
        {
            var list = new StackPanel();
            powerRows.Clear();
            foreach (var p in PowerItems)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(new TextBlock { Text = Ui.Icon((int)p[0]), FontFamily = Ui.Icons, FontSize = 24, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center });
                line.Children.Add(new TextBlock { Text = (string)p[1], FontSize = 18, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 1) });
                var row = new Border { Child = line, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 24, 10), Margin = new Thickness(0, 2, 0, 2) };
                list.Children.Add(row);
                powerRows.Add(row);
            }
            powerList = new Border
            {
                Child = list, CornerRadius = new CornerRadius(18), Padding = new Thickness(8), MinWidth = 240,
                Background = Ink.Brush("SeleneGlassColor", 0.96), BorderBrush = Ink.Brush("SeleneInkColor", 0.16), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 40, Opacity = 0.5, Color = Colors.Black }
            };
            // over the Power icon, its right edge lined up with the icon's
            var at = cells[index].TranslatePoint(new Point(Cell, 0), Root);
            powerList.Margin = new Thickness(Math.Max(20, at.X - 260), 0, 0, Root.ActualHeight - at.Y + 16);
            Root.Children.Add(powerList);
            Ui.Rise(powerList, 0, 12, 200);
            Ui.Play("OpenPanel.wav", 0.8);
            powerIndex = 0;
            ShowPowerFocus();
        }

        private void ShowPowerFocus()
        {
            for (int i = 0; i < powerRows.Count; i++)
            {
                bool on = i == powerIndex;
                powerRows[i].Background = on ? Ink.Brush("SeleneFocusColor") : Brushes.Transparent;
                foreach (var t in ((StackPanel)powerRows[i].Child).Children.OfType<TextBlock>())
                    t.Foreground = on ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor");
            }
        }

        private void HandlePower(string b)
        {
            if (b == "B") { Ui.Back(); ClosePower(); return; }
            if (b == "Start") { leaving = true; Ui.Back(); LoginScene.Finish(); return; }
            if (b == "A")
            {
                Ui.Confirm();
                int what = (int)PowerItems[powerIndex][2];
                try
                {
                    if (what < 0) Later(() => PlayniteMenu.Run("ExitCommand"));
                    else Power.Run(what);
                }
                catch (Exception ex) { HomeNav.Log("menu: power failed: " + ex); Ui.Shake(powerRows[powerIndex]); }
                return;
            }
            int next = powerIndex + (b == "DPadUp" ? -1 : b == "DPadDown" ? 1 : 0);
            if (next == powerIndex || next < 0 || next >= powerRows.Count) return;
            powerIndex = next;
            Ui.Tick();
            ShowPowerFocus();
        }

        private void ClosePower()
        {
            var list = powerList;
            powerList = null;
            Ui.Fade(list, 0, 150, 0, () => Root.Children.Remove(list));
        }

        // Playnite's own windows open once the menu has faded away
        private void Later(Action run)
        {
            leaving = true;
            LoginScene.Finish();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                try { run(); }
                catch (Exception ex) { HomeNav.Log("menu action failed: " + ex); }
            };
            t.Start();
        }

        // starts Playnite's library update in the background (from the hidden Playnite menu it waited behind one already
        // running); says what happens under the icons, then closes
        private void UpdateLibrary()
        {
            string busy;
            bool started = HomeNav.UpdateLibraryNow(out busy);
            status.Text = started ? "Updating your library in the background." : "Already updating: " + busy;
            if (!started) { Ui.Shake(cells[index]); return; }
            leaving = true;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (s, e) => { t.Stop(); LoginScene.Finish(); };
            t.Start();
        }

        private static void EditProfile()
        {
            var p = Profiles.Active;
            if (p == null) { LoginScene.Show(new WhoScreen(null), 1); return; }
            LoginScene.Show(new ProfileSteps(p, null, q => LoginScene.Finish()), 1);
        }
    }

    // Playnite's own actions, reached through its view models (no public API for these)
    public static class PlayniteMenu
    {
        private static object menuModel;
        // Playnite's own "open main menu" (Selene's menu took its place)
        public static ICommand Original;

        public static void OpenOriginal()
        {
            if (Original != null) Original.Execute(null);
            else Main("OpenMainMenuCommand");
        }

        private static object MainModel { get { return Application.Current.MainWindow != null ? Application.Current.MainWindow.DataContext : null; } }

        private static Assembly App { get { var vm = MainModel; return vm != null ? vm.GetType().Assembly : null; } }

        // a command on the main view model (FullscreenAppViewModel)
        public static void Main(string command)
        {
            var vm = MainModel;
            var c = vm != null ? vm.GetType().GetProperty(command).GetValue(vm, null) as ICommand : null;
            if (c == null) throw new InvalidOperationException("Playnite has no " + command);
            c.Execute(null);
        }

        // a command of Playnite's main menu (MainMenuViewModel, made with its real window factory but never shown)
        public static void Run(string command)
        {
            if (menuModel == null)
            {
                var factory = Activator.CreateInstance(App.GetType("Playnite.FullscreenApp.Windows.MainMenuWindowFactory"));
                menuModel = Activator.CreateInstance(App.GetType("Playnite.FullscreenApp.ViewModels.MainMenuViewModel"), factory, MainModel);
            }
            var c = menuModel.GetType().GetProperty(command).GetValue(menuModel, null) as ICommand;
            if (c == null) throw new InvalidOperationException("Playnite's menu has no " + command);
            try { c.Execute(null); }
            catch (NullReferenceException ex) { HomeNav.Log("menu: " + command + " (closing the hidden menu): " + ex.Message); }
        }

        public static void OpenSettings()
        {
            var vmType = App.GetType("Playnite.FullscreenApp.ViewModels.SettingsViewModel");
            var factory = Activator.CreateInstance(App.GetType("Playnite.FullscreenApp.Windows.SettingsWindowFactory"));
            var settings = Activator.CreateInstance(vmType, factory, MainModel);
            vmType.GetMethod("OpenView").Invoke(settings, null);
        }

        public static void OpenExtensions()
        {
            var vmType = App.GetType("Playnite.FullscreenApp.ViewModels.ExtensionsMenuViewModels");
            var factory = Activator.CreateInstance(App.GetType("Playnite.FullscreenApp.Windows.ExtensionsMenuWindowFactory"));
            var menu = Activator.CreateInstance(vmType, factory, MainModel);
            vmType.GetMethod("OpenView").Invoke(menu, null);
        }
    }

    // Sleep / restart / shut down: the optional companion helper does it its own way (e.g. TV off first), else Windows
    public static class Power
    {
        private static readonly string[][] Items = {
            new[] { "power-sleep", "rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0" },
            new[] { "power-restart", "shutdown.exe", "/r /t 0" },
            new[] { "power-off", "shutdown.exe", "/s /t 0" } };

        public static void Run(int i)
        {
            var item = Items[i];
            HomeNav.Log("power: " + item[0]);
            var helper = SelenePaths.Helper;
            var start = !string.IsNullOrEmpty(helper) && System.IO.File.Exists(helper)
                ? new System.Diagnostics.ProcessStartInfo(helper, item[0])
                : new System.Diagnostics.ProcessStartInfo(item[1], item[2]);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            System.Diagnostics.Process.Start(start);
        }
    }
}
