using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace Selene
{
    public enum SetupState { Ready, Missing, Optional, Warning }

    public class SetupItem
    {
        public string Key { get; set; }
        public string Title { get; set; }
        // what it is, in one line, so people know what they're installing
        public string About { get; set; }
        public string Detail { get; set; }
        public SetupState State { get; set; }
        // set when picking the card installs a Playnite add-on
        public string AddonId { get; set; }
    }

    public class AddonInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string About { get; set; }
    }

    // What the first-launch setup checks: games, game folders, the add-ons that come with Selene, a controller,
    // and add-ons that do the same job as Selene Core (they would fight it)
    public static class SetupCheck
    {
        public static readonly AddonInfo[] Included =
        {
            new AddonInfo { Id = "ExtraMetadataLoader_705fdbca-e1fc-4004-b839-1d040b8b4429", Name = "Extra Metadata Loader",
                About = "Game logos and trailers for the home and game details." },
            new AddonInfo { Id = "mtkennerly.ludusavi", Name = "Ludusavi",
                About = "Backs up your game saves every time you close a game, so you never lose progress." },
            new AddonInfo { Id = "playnite-howlongtobeat-plugin", Name = "HowLongToBeat",
                About = "Shows how long a game takes to beat, next to how long you've played." },
            new AddonInfo { Id = "PlayniteAchievements", Name = "Playnite Achievements",
                About = "Your achievements from Steam, Xbox, PlayStation, Epic, GOG and RetroAchievements." },
            new AddonInfo { Id = "felixkmh_DuplicateHider_Plugin", Name = "DuplicateHider",
                About = "Shows each game once, even if you own it on several stores." },
            new AddonInfo { Id = "playnite-gameactivity-plugin", Name = "GameActivity",
                About = "A history of when and how long you played each game." },
            new AddonInfo { Id = "UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890", Name = "UniPlaySong",
                About = "Plays each game's music while you browse." },
            new AddonInfo { Id = "PlayniteAudioSwitcher_708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1", Name = "Audio Switcher",
                About = "Switch between TV speakers and headset with the controller." },
        };

        public static readonly AddonInfo[] Clashes =
        {
            new AddonInfo { Id = "PlayState", Name = "PlayState", About = "pauses games, which Selene already does" },
            new AddonInfo { Id = "SplashScreen_d8c4f435-2bd2-49d8-98f6-87b1d415934a", Name = "Splash Screen", About = "shows a launch screen, which Selene already does" },
            new AddonInfo { Id = "NowPlaying_db4e7ade-57fb-426c-8392-60e2347a0209", Name = "Now Playing", About = "handles the running game, which Selene already does" },
            new AddonInfo { Id = "playnite-backgroundchanger-plugin", Name = "BackgroundChanger", About = "changes backgrounds, which Selene's wallpapers already do" },
        };

        public static List<SetupItem> Run(IPlayniteAPI api)
        {
            var items = new List<SetupItem> { Library(api), GameFolders() };
            items.AddRange(Included.Select(a => IncludedAddon(api, a)));
            if (Enabled(api, "mtkennerly.ludusavi")) items.Add(LudusaviProgram());
            items.Add(Controller());
            items.AddRange(Clashes.Where(c => Enabled(api, c.Id)).Select(c => new SetupItem
            {
                Key = "clash:" + c.Id,
                Title = c.Name,
                State = SetupState.Warning,
                About = c.Name + " " + c.About + ".",
                Detail = "Turn it off in Add-ons so the two don't fight"
            }));
            return items;
        }

        // add-ons that come with Selene and aren't installed (or are turned off) yet
        public static List<AddonInfo> MissingIncluded(IPlayniteAPI api)
        {
            return Included.Where(a => !Enabled(api, a.Id)).ToList();
        }

        private static bool Enabled(IPlayniteAPI api, string addonId)
        {
            return api.Addons.Addons.Contains(addonId) && !api.Addons.DisabledAddons.Contains(addonId);
        }

        private static SetupItem IncludedAddon(IPlayniteAPI api, AddonInfo addon)
        {
            bool installed = api.Addons.Addons.Contains(addon.Id);
            bool on = installed && !api.Addons.DisabledAddons.Contains(addon.Id);
            return new SetupItem
            {
                Key = "addon:" + addon.Id,
                Title = addon.Name,
                About = addon.About,
                AddonId = on ? null : addon.Id,
                State = on ? SetupState.Ready : SetupState.Missing,
                Detail = on ? "Installed" : installed ? "Installed but turned off" : "Comes with Selene · not installed yet"
            };
        }

        private static SetupItem LudusaviProgram()
        {
            bool found = LudusaviApp.Installed;
            return new SetupItem
            {
                Key = "ludusavi-program",
                Title = "Ludusavi program",
                About = "The free program the Ludusavi add-on uses to make the backups. Selene sets it up for you.",
                State = found ? SetupState.Ready : SetupState.Missing,
                Detail = found ? "Ready" : "Not set up yet"
            };
        }

        private static SetupItem Library(IPlayniteAPI api)
        {
            var stores = api.Addons.Plugins.OfType<LibraryPlugin>().ToDictionary(p => p.Id, p => p.Name);
            var games = api.Database.Games.ToList();
            var perStore = games.Where(g => stores.ContainsKey(g.PluginId))
                                .GroupBy(g => stores[g.PluginId])
                                .OrderByDescending(g => g.Count())
                                .Select(g => g.Key + " · " + g.Count() + " games")
                                .ToList();
            var outside = games.Count(g => !stores.ContainsKey(g.PluginId));
            if (outside > 0) perStore.Add("Your folders · " + outside + " games");
            return new SetupItem
            {
                Key = "library",
                Title = "Game library",
                About = "The games Selene shows, from your stores or your own folders.",
                State = games.Count > 0 ? SetupState.Ready : SetupState.Missing,
                Detail = games.Count > 0 ? string.Join(", ", perStore) : "No games yet: connect a store in Playnite's settings"
            };
        }

        private static SetupItem GameFolders()
        {
            var folders = SelenePaths.GameFolders.Length;
            return new SetupItem
            {
                Key = "folders",
                Title = "Games outside a store",
                About = "Folders Selene watches for games no store knows about; new games there are added automatically.",
                State = folders > 0 ? SetupState.Ready : SetupState.Optional,
                Detail = folders > 0 ? folders + (folders == 1 ? " folder" : " folders") : "Optional · add your game folders"
            };
        }

        private static SetupItem Controller()
        {
            bool connected = XInput.AnyConnected();
            return new SetupItem
            {
                Key = "controller",
                Title = "Controller",
                About = "Selene is made for a controller; keyboard works too.",
                State = connected ? SetupState.Ready : SetupState.Optional,
                Detail = connected ? "Connected" : "Not connected"
            };
        }

        // opens Playnite's own add-on installer for that add-on
        public static void Install(string addonId)
        {
            Process.Start("playnite://playnite/installaddon/" + addonId);
        }

        private static class XInput
        {
            [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern int GetState14(int user, byte[] state);
            [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] private static extern int GetState910(int user, byte[] state);

            public static bool AnyConnected()
            {
                for (int user = 0; user < 4; user++)
                {
                    var state = new byte[16];
                    int result;
                    try { result = GetState14(user, state); }
                    catch (DllNotFoundException) { result = GetState910(user, state); }
                    if (result == 0) return true;
                }
                return false;
            }
        }
    }
}
