using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace Selene
{
    // Selene Core: the companion add-on of the Selene fullscreen theme. Does nothing with any other theme.
    public class SeleneCorePlugin : GenericPlugin
    {
        public const string ThemeId = "Selene_a98e0027-fa6a-4f5e-a087-bad8d69f2443";
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly SeleneSettingsViewModel settings;

        public override Guid Id { get { return Guid.Parse("cd424c29-97f4-4dd0-aa66-4d8e01cc8724"); } }

        public SeleneCorePlugin(IPlayniteAPI api) : base(api)
        {
            settings = new SeleneSettingsViewModel(this);
            SelenePaths.Init(this, settings.Settings);
            Features.Init(settings.Settings, () => SavePluginSettings(settings.Settings));
            Profiles.Init(GetPluginUserDataPath());
            Properties = new GenericPluginProperties { HasSettings = true };
            // Playnite selects its first elements while starting, and each played the move sound: quiet until the
            // login is up (or the user's first press)
            if (Active) HomeNav.QuietFocus(8000);
        }

        private bool Active
        {
            get { return PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen && SelenePaths.ActiveThemeId == ThemeId; }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (!Active) { logger.Info("Selene Core: the Selene theme isn't in use, staying off"); return; }
            HomeNav.Api = PlayniteApi;
            HomeNav.Install();
            try { Profiles.ApplyLook(); }
            catch (Exception ex)
            {
                logger.Error(ex, "Selene Core: couldn't apply the profile's theme");
                PlayniteApi.Notifications.Add(new NotificationMessage("selene-theme", "Selene couldn't apply your theme, using the default look: " + ex.Message, NotificationType.Error));
            }
            SeleneShell.Start();
            if (string.IsNullOrWhiteSpace(settings.Settings.AvatarPath)) AnimatedAvatar.Fetch(SelenePaths.SteamId);
            HomeNav.Log("Selene Core started");
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            if (Active && args.Game != null) HomeNav.GameStarting(args.Game.Id);
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            // a game that runs as administrator gets a no-prompt launcher after it closes (automatic, any game)
            if (Active && args.Game != null) LocalGames.WatchForAdmin(args.Game);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            if (!Active || args.Game == null) return;
            LocalGames.AfterGameStopped(args.Game);
            Profiles.RecordSession(args.Game.Id, args.ElapsedSeconds);
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // store imports mark deleted games installed again: folder gone = not installed
            if (Active) LocalGames.MarkGoneFolders();
        }

        // Extensions menu: back to Who's playing?, the profile steps for the signed-in profile, or the setup check
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (!Active) yield break;
            yield return new MainMenuItem { MenuSection = "@Selene", Description = "Switch profile", Action = a => LoginScene.OpenWith(() => new WhoScreen(null)) };
            if (Profiles.Active != null)
                yield return new MainMenuItem
                {
                    MenuSection = "@Selene", Description = "Edit profile (name, avatar, theme, background)",
                    Action = a => { var p = Profiles.Active; if (p != null) LoginScene.OpenWith(() => new ProfileSteps(p, null, q => LoginScene.Finish())); }
                };
            yield return new MainMenuItem { MenuSection = "@Selene", Description = "Setup check", Action = a => LoginScene.OpenWith(() => new SetupScreen(false)) };
        }

        public override ISettings GetSettings(bool firstRunSettings) { return settings; }

        public override UserControl GetSettingsView(bool firstRunSettings) { return SeleneSettingsView.Build(settings); }
    }

    public class SeleneSettings
    {
        public string MediaFolder { get; set; }
        public List<string> GameFolders { get; set; }
        public string HelperPath { get; set; }
        public string StreamLogsFolder { get; set; }
        public string AvatarPath { get; set; }
        public string DisplayName { get; set; }
        public string StreamClientMatch { get; set; }
        public string StreamCredentialsFile { get; set; }
        public string HelperTask { get; set; }
        public string WallpapersFolder { get; set; }
        // Selene features switched off (Features.All keys); everything else is on
        public List<string> TurnedOff { get; set; }
        // features that start off (Features.All with DefaultOn = false) and were switched on
        public List<string> TurnedOn { get; set; }

        public SeleneSettings()
        {
            MediaFolder = ""; GameFolders = new List<string>(); HelperPath = ""; StreamLogsFolder = ""; AvatarPath = ""; DisplayName = "";
            StreamClientMatch = ""; StreamCredentialsFile = ""; HelperTask = ""; WallpapersFolder = ""; TurnedOff = new List<string>(); TurnedOn = new List<string>();
        }
    }

    public class SeleneSettingsViewModel : ObservableObject, ISettings
    {
        private readonly SeleneCorePlugin plugin;
        private SeleneSettings editing;
        public SeleneSettings Settings { get; private set; }

        public SeleneSettingsViewModel(SeleneCorePlugin plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<SeleneSettings>() ?? new SeleneSettings();
        }

        public void BeginEdit() { editing = Playnite.SDK.Data.Serialization.GetClone(Settings); }

        public void CancelEdit()
        {
            if (editing == null) return;
            Settings.MediaFolder = editing.MediaFolder; Settings.GameFolders = editing.GameFolders;
            Settings.HelperPath = editing.HelperPath; Settings.StreamLogsFolder = editing.StreamLogsFolder;
            Settings.AvatarPath = editing.AvatarPath; Settings.DisplayName = editing.DisplayName;
            Settings.StreamClientMatch = editing.StreamClientMatch; Settings.StreamCredentialsFile = editing.StreamCredentialsFile; Settings.HelperTask = editing.HelperTask;
            Settings.WallpapersFolder = editing.WallpapersFolder; Settings.TurnedOff = editing.TurnedOff; Settings.TurnedOn = editing.TurnedOn;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            SelenePaths.Init(plugin, Settings);
            Features.Init(Settings, () => plugin.SavePluginSettings(Settings));
        }

        public bool VerifySettings(out List<string> errors) { errors = new List<string>(); return true; }
    }

    // settings page, built in code (no XAML compiler needed to build Selene Core)
    public static class SeleneSettingsView
    {
        public static UserControl Build(SeleneSettingsViewModel vm)
        {
            var s = vm.Settings;
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "Features (also in Selene's menu under Selene settings):", FontWeight = FontWeights.SemiBold });
            foreach (var f in Features.All)
            {
                var key = f.Key;
                var box = new CheckBox { Content = f.Label + "  -  " + f.About, IsChecked = Features.On(key), Margin = new Thickness(0, 6, 0, 0) };
                box.Checked += (o, e) => Features.Mark(s, key, true);
                box.Unchecked += (o, e) => Features.Mark(s, key, false);
                panel.Children.Add(box);
            }
            Func<string, string, Action<string>, TextBox> field = (label, value, set) =>
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4) });
                var box = new TextBox { Text = value ?? "" };
                box.TextChanged += (o, e) => set(box.Text);
                panel.Children.Add(box);
                return box;
            };
            field("Media folder (downloaded art, preview clips, screenshots). Empty = Selene Core's own data folder:", s.MediaFolder, v => s.MediaFolder = v);
            var games = field("Folders with games no store knows about (one per line):", string.Join(Environment.NewLine, s.GameFolders ?? new List<string>()),
                v => s.GameFolders = new List<string>(v.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
            games.AcceptsReturn = true; games.MinHeight = 60;
            field("Your wallpapers folder (images in it and its subfolders show up in the background picker). Empty = only Selene's wallpapers:", s.WallpapersFolder, v => s.WallpapersFolder = v);
            field("Login name. Empty = your Steam name:", s.DisplayName, v => s.DisplayName = v);
            field("Login picture (image file). Empty = your Steam avatar:", s.AvatarPath, v => s.AvatarPath = v);
            field("Optional companion helper (.exe) with admin rights. Empty = off:", s.HelperPath, v => s.HelperPath = v);
            field("Optional admin helper scheduled task (focus/close games that run as administrator). Empty = off:", s.HelperTask, v => s.HelperTask = v);
            field("Optional game-streaming host log folder (a streaming client sees every game). Empty = off:", s.StreamLogsFolder, v => s.StreamLogsFolder = v);
            field("Streaming client name to react to (regular expression). Empty = any client:", s.StreamClientMatch, v => s.StreamClientMatch = v);
            field("Streaming host credentials file (disconnect clients before sleep). Empty = off:", s.StreamCredentialsFile, v => s.StreamCredentialsFile = v);
            return new UserControl { Content = panel };
        }
    }

    // Every Selene feature someone might not want, each with an on/off switch (add-on settings page and Selene settings
    // in the menu). A new feature gets a line here and a Features.On("key") check where it starts.
    public static class Features
    {
        public class Option
        {
            public string Key, Label, About;
            public bool DefaultOn = true;
        }

        public static readonly Option[] All =
        {
            new Option { Key = "intro", Label = "Logo intro", About = "The crescent intro when Playnite starts." },
            new Option { Key = "login", Label = "Who's playing? at start", About = "Off: the last profile signs in by itself (the very first start still sets you up)." },
            new Option { Key = "viewswitch", Label = "View button: home and library", About = "The View button switches between the home and the library (hold it to search; hold Start for these settings)." },
            new Option { Key = "sounds", Label = "Interface sounds", About = "Selene's ticks and chimes when you move, select and go back." },
            new Option { Key = "music", Label = "Music control", About = "Keeps game songs from the music add-on off the cards, login and menus, and quiet under trailers." },
            new Option { Key = "hints", Label = "Button hints", About = "What each button does, at the bottom right of the home (off like a console's home).", DefaultOn = false },
            new Option { Key = "glow", Label = "Glow and particles", About = "The breathing glow and drifting specks behind the login when there's no wallpaper." },
            new Option { Key = "launchquiet", Label = "Quiet loading screen", About = "A starting game's intro sounds stay down until the game shows." },
            new Option { Key = "freeze", Label = "Freeze games on Guide", About = "Guide pauses offline games completely; off, games are only muted." },
            new Option { Key = "tray", Label = "Close to tray", About = "Exit hides Playnite in the tray so it comes back instantly (after a restart)." },
            new Option { Key = "admin", Label = "No-prompt launchers", About = "Games that need administrator rights get a launcher that skips the prompt next time." },
        };

        private static SeleneSettings settings;
        private static Action save;

        public static void Init(SeleneSettings s, Action saveAll) { settings = s; save = saveAll; }

        private static bool DefaultOn(string key) { var o = All.FirstOrDefault(f => f.Key == key); return o == null || o.DefaultOn; }

        public static bool On(string key)
        {
            if (DefaultOn(key)) return settings == null || settings.TurnedOff == null || !settings.TurnedOff.Contains(key);
            return settings != null && settings.TurnedOn != null && settings.TurnedOn.Contains(key);
        }

        // records a switch in the given settings (the add-on page edits a copy until it's saved)
        public static void Mark(SeleneSettings s, string key, bool on)
        {
            if (s.TurnedOff == null) s.TurnedOff = new List<string>();
            if (s.TurnedOn == null) s.TurnedOn = new List<string>();
            s.TurnedOff.Remove(key);
            s.TurnedOn.Remove(key);
            if (on && !DefaultOn(key)) s.TurnedOn.Add(key);
            if (!on && DefaultOn(key)) s.TurnedOff.Add(key);
        }

        public static void Set(string key, bool on)
        {
            if (settings == null) return;
            Mark(settings, key, on);
            if (save != null) save();
        }
    }
}
