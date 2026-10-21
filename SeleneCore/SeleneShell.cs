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
        private static System.Windows.Threading.DispatcherTimer attachTimer;

        public static void Start()
        {
            var app = Application.Current;
            if (app == null) return;
            // the theme reads the media folder from here (tile squares, hero art)
            app.Resources["SeleneMediaRoot"] = System.IO.Path.GetDirectoryName(SelenePaths.Media("Hero"));
            if (!app.Resources.Contains("Ps5RowMode")) app.Resources["Ps5RowMode"] = "tv";
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
            var library = HomeNav.FindByName(main, "Ps5Nav_library") as ButtonBase;
            var hub = HomeNav.FindByName(main, "Ps5Nav_hub") as ButtonBase;
            if (library == null || hub == null) return false;
            library.Command = new Relay(ShowLibrary);
            hub.Command = new Relay(ShowHome);
            HomeNav.Log("shell: home/library wired");
            return true;
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
