using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

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
            app.Resources["SeleneIconFont"] = Ui.Icons;   // solid icons for the theme (a font path in the theme's XAML didn't load)
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
            LoginScene.AttachHomeGlow(main);   // the login's glow behind the home's cards
            LibraryStage.Attach(main);         // Collection's background trailer
            TopProfile.Attach(main);
            StoreData.AttachCard();            // the Store card's free games
            Contrast.Apply();                  // the home's shades as deep as the wallpaper needs           // the signed-in profile beside the clock
            Brand.Attach(main);                // Selene's mark on empty screens
            // "last played" as people say it (Today, Yesterday, 3 days ago), for Spotlight and Collection
            var npc = main.DataContext as System.ComponentModel.INotifyPropertyChanged;
            if (npc != null) npc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != "SelectedGame") return;
                HomeNav.GuardSpotlightSelection();   // Spotlight's selection stays Spotlight's (no stray game art)
                UpdateLastPlayed();
            };
            UpdateLastPlayed();
            // no login this start (intro and Who's playing? both off): Collection right away if that's the start screen
            var loginLayer = Part("AcceuilSettings");
            if (loginLayer != null && !loginLayer.IsVisible) LoginScene.AllowBackdropVideo();
            if (loginLayer != null && !loginLayer.IsVisible && Features.On("startcollection")) ShowLibrary();
            // Playnite's "game is running" screen (switching back to Playnite while a game runs): the home instead
            var status = HomeNav.FindByName(main, "PART_ElemGameStatus") as UIElement;
            if (status != null) status.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) HomeNav.GameStatusShown(); };
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
            if (store != null) store.Command = new Relay(() => LoginScene.OpenWith(() => !Features.On("storepage") ? new StoreScreen() : StoreData.ChosenCountry == null ? (Screen)new StoreCurrencyScreen(true) : new StorePage()));
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

        private static void UpdateLastPlayed()
        {
            try
            {
                var vm = main != null ? main.DataContext : null;
                var game = vm != null ? vm.GetType().GetProperty("SelectedGame").GetValue(vm, null) : null;
                var prop = game != null ? game.GetType().GetProperty("LastActivity") : null;
                var when = prop != null ? prop.GetValue(game, null) as DateTime? : null;
                string text;
                if (when == null) text = "Never";
                else
                {
                    int days = (DateTime.Today - when.Value.ToLocalTime().Date).Days;
                    text = days <= 0 ? "Today" : days == 1 ? "Yesterday" : days < 7 ? days + " days ago" : when.Value.ToLocalTime().ToString("MMM d, yyyy");
                }
                Application.Current.Resources["SeleneLastPlayed"] = text;
                // the store's flat logo by "platform"
                var source = game != null ? game.GetType().GetProperty("Source") : null;
                var src = source != null ? source.GetValue(game, null) : null;
                var name = src != null ? src.GetType().GetProperty("Name").GetValue(src, null) as string : null;
                PlatformIcons.Publish(name);
            }
            catch (Exception ex) { HomeNav.Log("last played: " + ex.Message); }
        }

        private static UIElement Part(string name) { return main != null ? HomeNav.FindByName(main, name) as UIElement : null; }

        // Switching between Spotlight and Collection is a crossfade: the screen you leave stays up while the other one gets
        // its picture, so you never see an empty background. (Spotlight sits above Collection in the theme.)
        private static int switchCount;

        // Collection is showing (or being switched to): Spotlight's selection guard stays out of its way
        public static bool CollectionActive { get; private set; }

        public static void ShowLibrary()
        {
            var home = Part("WelcomeHubOverlay");
            var library = Part("SeleneLibrary");
            if (home == null || library == null) return;
            int mine = ++switchCount;
            CollectionActive = true;
            library.BeginAnimation(UIElement.OpacityProperty, null);
            library.Visibility = Visibility.Visible;   // under Spotlight, loading its art meanwhile
            HomeNav.SetMusic("home");   // Collection like Spotlight: the default track, a game's song with its trailer
            HomeNav.ApplyCollectionFilter();   // the same tab as Spotlight's
            var list = Part("PART_ListGameItems") as ListBox;
            if (list != null) main.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
                var item = list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) as UIElement;
                if (item != null) item.Focus(); else list.Focus();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            // a crossfade: a still of Spotlight lies on top and melts away over Collection
            if (Crossfade(home, 260, 160))   // held a moment: Collection's covers load under it
            {
                home.BeginAnimation(UIElement.OpacityProperty, null);
                home.Visibility = Visibility.Collapsed;
                library.Opacity = 1;
                HomeNav.Log("shell: library");
                return;
            }
            // (no still to be had: Spotlight fades to black first, then Collection fades in)
            library.Opacity = 0;
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (s, e) =>
            {
                if (mine != switchCount) return;
                home.Visibility = Visibility.Collapsed;
                home.BeginAnimation(UIElement.OpacityProperty, null);
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                fadeIn.Completed += (s2, e2) => { if (mine == switchCount) { library.BeginAnimation(UIElement.OpacityProperty, null); library.Opacity = 1; } };
                library.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            };
            home.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            HomeNav.Log("shell: library");
        }

        public static void ShowHome()
        {
            var home = Part("WelcomeHubOverlay");
            var library = Part("SeleneLibrary");
            if (home == null) return;
            int mine = ++switchCount;
            CollectionActive = false;
            // a still of Collection lies on top while everything changes under it, then melts away over Spotlight: Collection
            // itself goes at once (setting Spotlight's "no game" made its list jump to its first game: the 007 flash)
            // (held until Spotlight's art is back, at most 700 ms)
            bool still = library != null && library.IsVisible && !LibraryStage.Playing && Crossfade(library, 280, -1);
            if (library != null) { library.BeginAnimation(UIElement.OpacityProperty, null); library.Visibility = Visibility.Collapsed; }
            var held = crossfadeStill;
            HomeNav.SyncSelectionForHome(() => { if (still) ReleaseStill(held); });   // Spotlight's own selection
            if (still)
            {
                var cap = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                cap.Tick += (s, e) => { cap.Stop(); ReleaseStill(held); };
                cap.Start();
            }
            home.Visibility = Visibility.Visible;
            if (still)
            {
                home.BeginAnimation(UIElement.OpacityProperty, null);
                home.Opacity = 1;
                HomeNav.Log("shell: home");
                return;
            }
            // Spotlight fades in over Collection, which goes once it's covered
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += (s, e) =>
            {
                if (mine != switchCount) return;
                home.BeginAnimation(UIElement.OpacityProperty, null);
                if (library != null) library.Visibility = Visibility.Collapsed;
            };
            home.BeginAnimation(UIElement.OpacityProperty, fade);
            HomeNav.Log("shell: home");
        }

        // lays a still picture of the screen being left over everything and fades it out (false when one can't be taken)
        private static Image crossfadeStill;

        private static bool Crossfade(UIElement leaving, int ms, int holdMs)
        {
            var el = leaving as FrameworkElement;
            var host = Part("PART_ViewHost") as Panel;
            if (el == null || host == null || !el.IsVisible || el.ActualWidth < 1 || el.ActualHeight < 1) return false;
            try
            {
                if (crossfadeStill != null) host.Children.Remove(crossfadeStill);
                var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                rtb.Render(el);
                rtb.Freeze();
                var img = new Image { Source = rtb, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Grid.SetRowSpan(img, 99); Grid.SetColumnSpan(img, 99);
                Panel.SetZIndex(img, 1000);
                host.Children.Add(img);
                crossfadeStill = img;
                img.Tag = ms;
                if (holdMs >= 0) ReleaseStill(img, holdMs);   // -1: the caller lets it go (ReleaseStill)
                return true;
            }
            catch (Exception ex) { HomeNav.Log("crossfade still: " + ex.Message); return false; }
        }

        private static void ReleaseStill(Image img, int holdMs = 0)
        {
            if (img == null || !(img.Tag is int)) return;
            int ms = (int)img.Tag;
            img.Tag = null;   // once
            var host = VisualTreeHelper.GetParent(img) as Panel;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(holdMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += (s, e) => { if (host != null) host.Children.Remove(img); if (crossfadeStill == img) crossfadeStill = null; };
            img.BeginAnimation(UIElement.OpacityProperty, fade);
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
            HomeNav.QuietFocus(800);   // focus moves into the login layer: not a move
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
            HomeNav.QuietFocus(1200);   // focus goes back to the home
            if (!LoginScene.KeepHome && Features.On("startcollection")) { ShowLibrary(); HomeNav.Log("start on Collection"); }
            else if (!LoginScene.KeepHome) HomeNav.HoldHomeForIntro();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (s, e) => { login.Visibility = Visibility.Collapsed; login.BeginAnimation(UIElement.OpacityProperty, null); login.Opacity = 1; };
            login.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
