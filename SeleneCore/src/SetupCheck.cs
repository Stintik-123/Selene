using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace Selene
{
    public enum SetupState { Ready, Missing, Optional }

    public class SetupItem
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public SetupState State { get; set; }
        // set when picking the card installs a Playnite add-on
        public string AddonId { get; set; }
    }

    // What the first-launch setup checks: games in the library, game folders, add-ons Selene uses, a controller
    public static class SetupCheck
    {
        public const string MetadataLoaderId = "ExtraMetadataLoader_705fdbca-e1fc-4004-b839-1d040b8b4429";

        public static List<SetupItem> Run(IPlayniteAPI api)
        {
            return new List<SetupItem> { Library(api), GameFolders(), Addon(api, "metadata", "Logos & trailers", MetadataLoaderId, false), Controller() };
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
                State = folders > 0 ? SetupState.Ready : SetupState.Optional,
                Detail = folders > 0 ? folders + (folders == 1 ? " folder" : " folders") : "Optional · add your game folders"
            };
        }

        private static SetupItem Addon(IPlayniteAPI api, string key, string title, string addonId, bool required)
        {
            bool installed = api.Addons.Addons.Contains(addonId);
            bool disabled = api.Addons.DisabledAddons.Contains(addonId);
            return new SetupItem
            {
                Key = key,
                Title = title,
                AddonId = installed && !disabled ? null : addonId,
                State = installed && !disabled ? SetupState.Ready : required ? SetupState.Missing : SetupState.Optional,
                Detail = installed ? (disabled ? "Installed but turned off" : "Installed") : "Not installed"
            };
        }

        private static SetupItem Controller()
        {
            bool connected = XInput.AnyConnected();
            return new SetupItem
            {
                Key = "controller",
                Title = "Controller",
                State = connected ? SetupState.Ready : SetupState.Optional,
                Detail = connected ? "Connected" : "Not connected · keyboard works too"
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
