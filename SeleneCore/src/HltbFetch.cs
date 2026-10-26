using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Playnite.SDK.Models;

namespace Selene
{
    // HowLongToBeat only has times for games it was asked about. A game's page asks for its own (once a session, in the
    // background, through the add-on's database), and the page's numbers follow when they come.
    public static class HltbFetch
    {
        private static readonly Guid AddonId = new Guid("e08cd51f-9c9a-4ee3-a094-fde03b55492f");
        private static readonly HashSet<Guid> asked = new HashSet<Guid>();
        private static object database;
        private static bool looked;

        public static void ForPage(Game game)
        {
            if (game == null || !Features.On("hltb") || asked.Contains(game.Id)) return;
            var db = Database();
            if (db == null) return;
            asked.Add(game.Id);
            var type = db.GetType();
            var get = type.GetMethod("Get", new[] { typeof(Guid), typeof(bool), typeof(bool) });
            var themes = type.GetMethod("SetThemesResources", new[] { typeof(Game) });
            if (get == null) return;
            if (HasData(get.Invoke(db, new object[] { game.Id, true, false }))) return;   // already known
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var found = HasData(get.Invoke(db, new object[] { game.Id, false, false }));
                    HomeNav.Log("hltb: " + game.Name + (found ? " fetched" : " not found on HowLongToBeat"));
                    if (!found || themes == null) return;
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var shown = HomeNav.Api.MainView.SelectedGames.FirstOrDefault();
                            if (shown != null && shown.Id == game.Id) themes.Invoke(db, new object[] { game });
                        }
                        catch (Exception ex) { HomeNav.Log("hltb: page refresh failed: " + ex.Message); }
                    }));
                }
                catch (Exception ex) { HomeNav.Log("hltb: fetch failed for " + game.Name + ": " + (ex.InnerException ?? ex).Message); }
            });
        }

        private static bool HasData(object item)
        {
            if (item == null) return false;
            var prop = item.GetType().GetProperty("HasData");
            return prop != null && prop.GetValue(item, null) is bool && (bool)prop.GetValue(item, null);
        }

        // the add-on's database: a static PluginDatabase on its plugin's base class
        private static object Database()
        {
            if (looked) return database;
            looked = true;
            try
            {
                var plugin = HomeNav.Api.Addons.Plugins.FirstOrDefault(p => p.Id == AddonId);
                for (var t = plugin != null ? plugin.GetType() : null; t != null && database == null; t = t.BaseType)
                {
                    var prop = t.GetProperty("PluginDatabase", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (prop != null) database = prop.GetValue(null, null);
                }
                HomeNav.Log("hltb: " + (plugin == null ? "add-on not installed" : database != null ? "database found" : "database not found"));
            }
            catch (Exception ex) { HomeNav.Log("hltb: " + ex.Message); }
            return database;
        }
    }
}
