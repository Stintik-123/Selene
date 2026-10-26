using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Selene
{
    // Achievements from the Playnite Achievements add-on, read through its own data service (no files of its own touched).
    // Read off the UI thread; the screens get plain lists.
    public static class AchievementsData
    {
        public class Item
        {
            public string Name, Description, Icon, Tier, Trophy;
            public bool Unlocked, Hidden;
            public DateTime? When;          // local time it was unlocked
            public double? Rarity;          // % of players who have it
            public int? ProgressNum, ProgressDenom;
            public int Order;
        }

        public class GameSet
        {
            public Guid GameId;
            public string Name;
            public int Unlocked, Total;
            public bool Completed;
            public List<Item> Items = new List<Item>();
            public DateTime? LastUnlock { get { var w = Items.Where(i => i.Unlocked && i.When.HasValue).Select(i => i.When.Value).ToList(); return w.Count > 0 ? w.Max() : (DateTime?)null; } }
            public int Percent { get { return Total > 0 ? (int)Math.Floor(100.0 * Unlocked / Total) : 0; } }
        }

        private static object plugin;
        private static bool looked;
        private static string dataFolder;

        private static object Plugin()
        {
            if (looked) return plugin;
            looked = true;
            try
            {
                var addon = HomeNav.Api != null ? HomeNav.Api.Addons.Plugins.FirstOrDefault(p => p.GetType().FullName == "PlayniteAchievements.PlayniteAchievementsPlugin") : null;
                plugin = addon;
                if (addon != null) dataFolder = ((Playnite.SDK.Plugins.Plugin)addon).GetPluginUserDataPath();
                HomeNav.Log("achievements: " + (addon != null ? "Playnite Achievements found" : "Playnite Achievements not installed"));
            }
            catch (Exception ex) { HomeNav.Log("achievements: " + ex.Message); }
            return plugin;
        }

        public static bool Available() { return Plugin() != null; }

        private static object Service()
        {
            var p = Plugin();
            var prop = p != null ? p.GetType().GetProperty("AchievementDataService") : null;
            return prop != null ? prop.GetValue(p, null) : null;
        }

        // one game, its hidden ones as the add-on shows them; then on the UI thread
        public static void ForGame(Guid id, Action<GameSet> done)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            Task.Factory.StartNew(() =>
            {
                GameSet set = null;
                try
                {
                    var svc = Service();
                    var m = svc != null ? svc.GetType().GetMethod("GetVisibleGameAchievementData", new[] { typeof(Guid) }) : null;
                    set = m != null ? ToSet(m.Invoke(svc, new object[] { id })) : null;
                }
                catch (Exception ex) { HomeNav.Log("achievements: reading a game failed: " + (ex.InnerException ?? ex).Message); }
                dispatcher.BeginInvoke(new Action(() => done(set)));
            });
        }

        // every game with achievements
        public static void All(Action<List<GameSet>> done)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            Task.Factory.StartNew(() =>
            {
                var list = new List<GameSet>();
                try
                {
                    var svc = Service();
                    var m = svc != null ? svc.GetType().GetMethod("GetAllVisibleGameAchievementDataForTheme", Type.EmptyTypes) : null;
                    var all = m != null ? m.Invoke(svc, null) as IEnumerable : null;
                    if (all != null) foreach (var g in all) { var set = ToSet(g); if (set != null && set.Total > 0) list.Add(set); }
                }
                catch (Exception ex) { HomeNav.Log("achievements: reading all failed: " + (ex.InnerException ?? ex).Message); }
                dispatcher.BeginInvoke(new Action(() => done(list)));
            });
        }

        private static GameSet ToSet(object data)
        {
            if (data == null) return null;
            var set = new GameSet();
            var gid = Get(data, "PlayniteGameId");
            if (gid is Guid) set.GameId = (Guid)gid;
            set.Name = Get(data, "GameName") as string ?? "";
            set.Completed = Equals(Get(data, "IsCompleted"), true);
            var items = Get(data, "Achievements") as IEnumerable;
            if (items != null)
                foreach (var a in items)
                {
                    var it = new Item
                    {
                        Name = (Get(a, "DisplayName") as string) ?? (Get(a, "Name") as string) ?? "",
                        Description = Get(a, "Description") as string ?? "",
                        Unlocked = Equals(Get(a, "Unlocked"), true),
                        Hidden = Equals(Get(a, "Hidden"), true),
                        Trophy = Get(a, "TrophyType") as string,
                        Tier = TierName(Get(a, "Rarity")),
                        Order = Get(a, "DefaultOrderIndex") is int ? (int)Get(a, "DefaultOrderIndex") : 0,
                    };
                    var when = Get(a, "UnlockTimeUtc") as DateTime?;
                    if (when.HasValue) it.When = DateTime.SpecifyKind(when.Value, DateTimeKind.Utc).ToLocalTime();
                    it.ProgressNum = ToInt(Get(a, "ProgressNum"));
                    it.ProgressDenom = ToInt(Get(a, "ProgressDenom"));
                    it.Rarity = ToDouble(Get(a, "GlobalPercentUnlocked"));
                    it.Icon = IconFile(Get(a, it.Unlocked ? "UnlockedIconDisplay" : "LockedIconDisplay") as string)
                           ?? IconFile(Get(a, "IconDisplay") as string)
                           ?? IconFile(Get(a, it.Unlocked ? "UnlockedIconPath" : "LockedIconPath") as string)
                           ?? IconFile(Get(a, "UnlockedIconPath") as string);
                    set.Items.Add(it);
                }
            set.Total = set.Items.Count;
            set.Unlocked = set.Items.Count(i => i.Unlocked);
            return set;
        }

        private static string TierName(object tier)
        {
            var s = tier != null ? tier.ToString() : "";
            switch (s)
            {
                case "UltraRare": return "Ultra rare";
                case "Rare": return "Rare";
                case "Uncommon": return "Uncommon";
                case "Common": return "Common";
            }
            return "";
        }

        // an icon as a file on this PC (the add-on keeps them in its folder; paths there can be relative to it)
        private static string IconFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                var full = Path.IsPathRooted(path) ? path : (dataFolder != null ? Path.Combine(dataFolder, path) : null);
                return full != null && File.Exists(full) ? full : null;
            }
            catch { return null; }
        }

        private static int? ToInt(object o) { try { return o == null ? (int?)null : System.Convert.ToInt32(o); } catch { return null; } }
        private static double? ToDouble(object o) { try { return o == null ? (double?)null : System.Convert.ToDouble(o); } catch { return null; } }

        private static object Get(object o, string name)
        {
            if (o == null) return null;
            try { var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance); return p != null ? p.GetValue(o, null) : null; }
            catch { return null; }
        }

        // ---- words ----
        public static string RarityLine(Item i)
        {
            if (!i.Rarity.HasValue) return i.Tier;
            var pct = i.Rarity.Value < 1 ? i.Rarity.Value.ToString("0.0") : Math.Round(i.Rarity.Value).ToString("0");
            return (i.Tier.Length > 0 ? i.Tier + "  ·  " : "") + pct + "%";
        }
    }
}
