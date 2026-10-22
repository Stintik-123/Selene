using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Selene
{
    // The parts of the main screen Selene Core owns: home <-> library, the login layer, values the theme binds to.
    public static class SeleneShell
    {
        private class Relay : ICommand
        {
            private readonly Action run;
            public Relay(Action run) { this.run = run; }
            public bool CanExecute(object parameter) { return true; }
            public void Execute(object parameter) { run(); }
            public event EventHandler CanExecuteChanged { add { } remove { } }
        }

        private static Window main;
        private static ICommand seleneMenu;
        private static System.Windows.Threading.DispatcherTimer attachTimer;

        public static void Start()
        {
            var app = Application.Current;
            if (app == null) return;
            // the theme reads the media folder from here (tile squares, hero art)
            app.Resources["SeleneMediaRoot"] = System.IO.Path.GetDirectoryName(SelenePaths.Media("Hero"));
            if (!app.Resources.Contains("SeleneRowMode")) app.Resources["SeleneRowMode"] = "tv";
            // the main screen's template is applied a moment after start: retry until its parts exist
            attachTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            int tries = 0;
            attachTimer.Tick += (s, e) => { if (TryAttach() || ++tries > 120) attachTimer.Stop(); };
            attachTimer.Start();
        }

        private static bool TryAttach()
        {
            main = Application.Current.MainWindow;
            if (main == null) return false;
            var library = HomeNav.FindByName(main, "SeleneNav_library") as ButtonBase;
            var hub = HomeNav.FindByName(main, "SeleneNav_hub") as ButtonBase;
            if (library == null || hub == null) return false;
            library.Command = new Relay(ShowLibrary);
            hub.Command = new Relay(ShowHome);
            var menuButton = HomeNav.FindByName(main, "PART_ButtonMainMenu") as ButtonBase;
            if (menuButton != null) menuButton.Command = new Relay(OpenMenu);
            // every way Playnite opens its main menu (Start, keys, the button) opens Selene's; Playnite's own stays reachable
            // from it under More
            var vm = main.DataContext;
            var openMenu = vm != null ? vm.GetType().GetProperty("OpenMainMenuCommand") : null;
            try
            {
                var current = openMenu != null ? openMenu.GetValue(vm, null) as ICommand : null;
                if (openMenu != null && openMenu.CanWrite && current != null && current != seleneMenu)
                {
                    PlayniteMenu.Original = current;
                    // Playnite's own command type (the property only takes that)
                    seleneMenu = new Playnite.SDK.RelayCommand<object>(o => OpenMenu());
                    openMenu.SetValue(vm, seleneMenu, null);
                    HomeNav.Log("shell: main menu is Selene's");
                }
            }
            catch (Exception ex) { HomeNav.Log("shell: couldn't take over the main menu: " + ex.Message); }
            var store = HomeNav.FindByName(main, "SeleneNav_store") as ButtonBase;
            if (store != null) store.Command = new Relay(() => LoginScene.OpenWith(() => new StoreScreen()));
            HomeNav.Log("shell: home/library wired");
            DevView();
            return true;
        }

        // developer switch: %TEMP%\selene-dev-view holding "details", "library" or "menu" opens that screen a few seconds
        // after start (to look at each screen while building Selene, without a controller)
        private static void DevView()
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-dev-view");
            if (!System.IO.File.Exists(file)) return;
            var view = System.IO.File.ReadAllText(file).Trim().ToLowerInvariant();
            var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            later.Tick += (s, e) =>
            {
                later.Stop();
                try
                {
                    var vm = main.DataContext;
                    if (view == "library") ShowLibrary();
                    else if (view == "details" && vm != null) vm.GetType().GetProperty("GameDetailsVisible").SetValue(vm, true, null);
                    else if (view == "menu" && vm != null) { var c = vm.GetType().GetProperty("OpenMainMenuCommand").GetValue(vm, null) as ICommand; if (c != null) c.Execute(null); }
                    HomeNav.Log("shell: developer view " + view);
                }
                catch (Exception ex) { HomeNav.Log("shell: developer view failed " + ex.Message); }
            };
            later.Start();
        }

        private static UIElement Part(string name) { return main != null ? HomeNav.FindByName(main, name) as UIElement : null; }

        public static void ShowLibrary()
        {
            var home = Part("WelcomeHubOverlay");
            var library = Part("SeleneLibrary");
            if (home == null || library == null) return;
            library.Visibility = Visibility.Visible;
            library.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            home.Visibility = Visibility.Collapsed;
            var list = Part("PART_ListGameItems") as ListBox;
            if (list != null) main.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
                var item = list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) as UIElement;
                if (item != null) item.Focus(); else list.Focus();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            HomeNav.Log("shell: library");
        }

        public static void ShowHome()
        {
            var home = Part("WelcomeHubOverlay");
            var library = Part("SeleneLibrary");
            if (home == null) return;
            if (library != null) library.Visibility = Visibility.Collapsed;
            home.Visibility = Visibility.Visible;
            home.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            HomeNav.Log("shell: home");
        }

        public static void OpenMenu()
        {
            if (LoginScene.IsActive) return;
            LoginScene.OpenWith(() => new MenuScreen());
        }

        // brings the login layer back (switch profile, setup check, edit profile from the menu)
        public static void ShowLogin()
        {
            var login = Part("AcceuilSettings");
            if (login == null) return;
            login.BeginAnimation(UIElement.OpacityProperty, null);
            login.Opacity = 0;
            login.Visibility = Visibility.Visible;
            login.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // the login layer goes when the profile is picked (it was the theme plugin's job before)
        public static void HideLogin()
        {
            var login = Part("AcceuilSettings");
            if (login == null) return;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (s, e) => { login.Visibility = Visibility.Collapsed; login.BeginAnimation(UIElement.OpacityProperty, null); login.Opacity = 1; };
            login.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
