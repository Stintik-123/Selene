using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Selene
{
    // Selene's own menu (replaces Playnite's list): who's signed in and the time, then rows of tiles for Selene,
    // Playnite and power. Opens over the dimmed home from the top bar's menu button or Start; B or Start closes it.
    public class MenuScreen : Screen
    {
        private class Item
        {
            public string Glyph, Label, About;
            public Action Run;
        }

        private readonly List<List<Item>> rows = new List<List<Item>>();
        private readonly List<List<Tile>> tiles = new List<List<Tile>>();
        private readonly List<FrameworkElement> rowPanels = new List<FrameworkElement>();
        private readonly TextBlock about, clock;
        private readonly DispatcherTimer clockTimer;
        private int row, col;
        private bool leaving;

        public override bool Overlay { get { return true; } }

        public MenuScreen()
        {
            var profile = Profiles.Active;
            rows.Add(new List<Item>
            {
                new Item { Glyph = "\uE77B", Label = "Switch profile", About = "Back to Who's playing?", Run = () => LoginScene.Show(new WhoScreen(null), 1) },
                new Item { Glyph = "\uE70F", Label = "Edit profile", About = "Your name, avatar, theme and background.", Run = EditProfile },
                new Item { Glyph = "\uE72C", Label = "Update library", About = "Look for new games in your stores and folders.", Run = () => Later(() => PlayniteMenu.Run("UpdateGamesCommand")) },
                new Item { Glyph = "\uE8B1", Label = "Random game", About = "Let Selene pick something to play.", Run = () => Later(() => PlayniteMenu.Main("SelectRandomGameCommand")) },
                new Item { Glyph = "\uE771", Label = "Selene settings", About = "Turn Selene's features on or off: intro, login, sounds and more.", Run = () => LoginScene.Show(new SettingsScreen(), 1) },
                new Item { Glyph = "\uE90F", Label = "Setup check", About = "Add-ons, save backups and what Selene needs.", Run = () => LoginScene.Show(new SetupScreen(false), 1) },
            });
            rows.Add(new List<Item>
            {
                new Item { Glyph = "\uE713", Label = "Settings", About = "Playnite's settings: libraries, controller, audio and more.", Run = () => Later(PlayniteMenu.OpenSettings) },
                new Item { Glyph = "\uEA86", Label = "Extensions", About = "What your add-ons offer from here.", Run = () => Later(PlayniteMenu.OpenExtensions) },
                new Item { Glyph = "\uE7F4", Label = "Desktop mode", About = "Switch Playnite to its desktop window.", Run = () => Later(() => PlayniteMenu.Main("SwitchToDesktopCommand")) },
                new Item { Glyph = "\uE712", Label = "More", About = "Playnite's own menu: tools, store clients and the rest.", Run = () => Later(PlayniteMenu.OpenOriginal) },
            });
            rows.Add(new List<Item>
            {
                new Item { Glyph = "\uE708", Label = "Sleep", About = "Put the PC to sleep.", Run = () => Power.Run(0) },
                new Item { Glyph = "\uE777", Label = "Restart", About = "Restart the PC.", Run = () => Power.Run(1) },
                new Item { Glyph = "\uE7E8", Label = "Shut down", About = "Turn the PC off.", Run = () => Power.Run(2) },
                new Item { Glyph = "\uE711", Label = "Exit Playnite", About = "Close Playnite.", Run = () => Later(() => PlayniteMenu.Run("ExitCommand")) },
            });

            // header: who's signed in on the left, the time on the right
            var header = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(96, 70, 96, 0) };
            var who = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            if (profile != null)
            {
                var face = WhoScreen.Avatar(profile, 64);
                face.Margin = new Thickness(0, 0, 18, 0);
                who.Children.Add(face);
            }
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = profile != null ? profile.Name : "Selene", FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor") });
            names.Children.Add(new TextBlock { Text = profile != null ? "Signed in" : "No profile", FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6) });
            who.Children.Add(names);
            header.Children.Add(who);
            clock = new TextBlock { FontSize = 40, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Root.Children.Add(header);
            clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            clockTimer.Tick += (s, e) => clock.Text = DateTime.Now.ToString("t");
            clock.Text = DateTime.Now.ToString("t");

            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            var titles = new[] { "Selene", "Playnite", "Power" };
            for (int r = 0; r < rows.Count; r++)
            {
                var section = new StackPanel { Margin = new Thickness(0, r == 0 ? 0 : 30, 0, 0) };
                section.Children.Add(new TextBlock { Text = titles[r].ToUpperInvariant(), FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.55), Margin = new Thickness(10, 0, 0, 12) });
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                var rowTiles = new List<Tile>();
                foreach (var item in rows[r])
                {
                    var t = new Tile(196, 128, 20, 6) { FocusScale = 1.06 };
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    sp.Children.Add(new TextBlock { Text = item.Glyph, FontFamily = Ui.Glyphs, FontSize = 30, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Center });
                    sp.Children.Add(new TextBlock { Text = item.Label, FontSize = 18, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
                    t.Face.Child = sp;
                    t.Root.Margin = new Thickness(10, 0, 10, 0);
                    line.Children.Add(t.Root);
                    rowTiles.Add(t);
                }
                section.Children.Add(line);
                body.Children.Add(section);
                tiles.Add(rowTiles);
                rowPanels.Add(section);
            }
            about = Ui.Text("", 19, 0.75);
            about.Margin = new Thickness(0, 34, 0, 0);
            body.Children.Add(about);
            Root.Children.Add(body);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Close")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
        }

        public override void Shown()
        {
            for (int r = 0; r < rowPanels.Count; r++) Ui.Rise(rowPanels[r], 80 + r * 60, 18, 300);
            clockTimer.Start();
            Ui.Play("OpenPanel.wav", 0.8);
            ShowFocus();
        }

        public override void Hidden() { clockTimer.Stop(); }

        private void ShowFocus()
        {
            for (int r = 0; r < tiles.Count; r++)
                for (int c = 0; c < tiles[r].Count; c++) tiles[r][c].Focus(r == row && c == col);
            about.Text = rows[row][col].About;
        }

        public override void Handle(string b)
        {
            if (leaving) return;
            if (b == "B" || b == "Start") { leaving = true; Ui.Back(); LoginScene.Finish(); return; }
            if (b == "A")
            {
                var item = rows[row][col];
                Ui.Confirm();
                try { item.Run(); }
                catch (Exception ex) { HomeNav.Log("menu: " + item.Label + " failed: " + ex); Ui.Shake(tiles[row][col].Root); }
                return;
            }
            int r = row, c = col;
            if (b == "DPadLeft") c--;
            else if (b == "DPadRight") c++;
            else if (b == "DPadUp") r--;
            else if (b == "DPadDown") r++;
            else return;
            if (r < 0 || r >= rows.Count) return;
            if (r != row) c = Math.Min(c, rows[r].Count - 1);
            if (c < 0 || c >= rows[r].Count) return;
            row = r; col = c;
            Ui.Tick();
            ShowFocus();
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
