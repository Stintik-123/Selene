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
            // bronze / silver / gold, PS5-style: the game's own grade (PlayStation), or from how rare it is
            public string Grade
            {
                get
                {
                    var t = (Trophy ?? "").Trim().ToLowerInvariant();
                    if (t == "bronze" || t == "silver" || t == "gold" || t == "platinum") return char.ToUpper(t[0]) + t.Substring(1);
                    if (Rarity.HasValue) return Rarity.Value < 5 ? "Gold" : Rarity.Value < 25 ? "Silver" : "Bronze";
                    return Tier == "Ultra rare" ? "Gold" : Tier == "Rare" || Tier == "Uncommon" ? "Silver" : "Bronze";
                }
            }
        }

        // a trophy of a grade, its metal shading down from light to its colour (as a console's trophies)
        public static System.Windows.Controls.TextBlock TrophyIcon(string grade, double size)
        {
            var c = ((System.Windows.Media.SolidColorBrush)GradeBrush(grade)).Color;
            var light = System.Windows.Media.Color.FromRgb((byte)Math.Min(255, c.R + 55), (byte)Math.Min(255, c.G + 55), (byte)Math.Min(255, c.B + 55));
            var dark = System.Windows.Media.Color.FromRgb((byte)(c.R * 0.72), (byte)(c.G * 0.72), (byte)(c.B * 0.72));
            var metal = new System.Windows.Media.LinearGradientBrush(new System.Windows.Media.GradientStopCollection { new System.Windows.Media.GradientStop(light, 0), new System.Windows.Media.GradientStop(c, 0.5), new System.Windows.Media.GradientStop(dark, 1) }, 90);
            metal.Freeze();
            return new System.Windows.Controls.TextBlock { Text = Ui.Icon(0xea23), FontFamily = Ui.Icons, FontSize = size, Foreground = metal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        }

        // the metals' colours (a trophy's grade is the same in every theme)
        public static System.Windows.Media.Brush GradeBrush(string grade)
        {
            System.Windows.Media.Color c;
            switch (grade)
            {
                case "Platinum": c = System.Windows.Media.Color.FromRgb(0xC9, 0xDD, 0xF2); break;
                case "Gold": c = System.Windows.Media.Color.FromRgb(0xE8, 0xC4, 0x6A); break;
                case "Silver": c = System.Windows.Media.Color.FromRgb(0xC2, 0xC9, 0xD3); break;
                default: c = System.Windows.Media.Color.FromRgb(0xCD, 0x8E, 0x5E); break;
            }
            var b = new System.Windows.Media.SolidColorBrush(c);
            b.Freeze();
            return b;
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
            public bool Platinum { get { return Total > 0 && Unlocked == Total; } }   // every one earned, as a console's platinum
            public int Earned(string grade) { return Items.Count(i => i.Unlocked && i.Grade == grade); }
            public int Count(string grade) { return Items.Count(i => i.Grade == grade); }
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

        // what was read last: the screens open on it at once (reading the add-on's database took a moment each time, a dark
        // screen before the list); a fresh read follows in the background when it's over a minute old
        private static List<GameSet> cache;
        private static DateTime cachedAt;
        private static bool reading;

        // one game, its hidden ones as the add-on shows them; then on the UI thread
        public static void ForGame(Guid id, Action<GameSet> done)
        {
            if (cache != null)
            {
                var known = cache.FirstOrDefault(g => g.GameId == id);
                if (known != null) { done(known); return; }
            }
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
            if (cache != null)
            {
                done(cache);
                if ((DateTime.Now - cachedAt).TotalSeconds > 60 && !reading) Read(null);
                return;
            }
            Read(done);
        }

        // a fresh read whatever the cache's age (the add-on just checked games); nothing when one is under way
        public static void Reread(Action<List<GameSet>> done) { if (!reading) Read(done); }

        // the add-on's check under way, for the hub: how far (done of total) and the game it's on; false when none runs
        public static bool Checking(out int done, out int total, out Guid? game)
        {
            done = total = 0; game = null;
            try
            {
                var p = Plugin();
                var prop = p != null ? p.GetType().GetProperty("RefreshRuntime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
                var rt = prop != null ? prop.GetValue(p, null) : null;
                if (rt == null || !(bool)rt.GetType().GetProperty("IsRebuilding").GetValue(rt, null)) return false;
                var report = rt.GetType().GetMethod("GetLastRebuildProgress", Type.EmptyTypes).Invoke(rt, null);
                if (report != null)
                {
                    var t = report.GetType();
                    done = (int)t.GetProperty("CurrentStep").GetValue(report, null);
                    total = (int)t.GetProperty("TotalSteps").GetValue(report, null);
                    game = t.GetProperty("CurrentGameId").GetValue(report, null) as Guid?;
                }
                return true;
            }
            catch { return false; }
        }

        private static void Read(Action<List<GameSet>> done)
        {
            reading = true;
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
                dispatcher.BeginInvoke(new Action(() =>
                {
                    cache = list;
                    cachedAt = DateTime.Now;
                    reading = false;
                    if (done != null) done(list);
                }));
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

        // ---- the add-on set up without its desktop page ----
        // Playnite Achievements checks nothing on its own until its first-time page (desktop mode only) is gone through; from
        // the couch it never is, so only games played since showed up. Once: that page is marked done and every game it has
        // nothing for is checked in the background (the stores it's signed in to; Steam through Playnite's Steam sign-in),
        // then its own update every few hours keeps the list current. Selene's note of having done it: achievements-setup.txt
        private static string SetupFile { get { return Path.Combine(SelenePaths.DataFolder, "achievements-setup.txt"); } }

        public static void SetUpLater()
        {
            if (!Features.On("achsetup") || (!Features.On("achievements") && !Features.On("cardachievements"))) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            t.Tick += (s, e) => { t.Stop(); SetUp(); };
            t.Start();
        }

        private static void SetUp()
        {
            try
            {
                var p = Plugin();
                if (p == null || File.Exists(SetupFile)) return;
                var settings = p.GetType().GetProperty("Settings").GetValue(p, null);
                var persisted = settings.GetType().GetProperty("Persisted").GetValue(settings, null);
                var done = persisted.GetType().GetProperty("FirstTimeSetupCompleted");
                // (its theme-integration question: nothing for Selene to move over; left unanswered it held the updates too)
                var seen = persisted.GetType().GetProperty("SeenThemeMigration");
                if (done != null && (!(bool)done.GetValue(persisted, null) || (seen != null && !(bool)seen.GetValue(persisted, null))))
                {
                    done.SetValue(persisted, true, null);
                    if (seen != null) seen.SetValue(persisted, true, null);
                    typeof(Playnite.SDK.Plugins.Plugin).GetMethod("SavePluginSettings").MakeGenericMethod(settings.GetType()).Invoke(p, new[] { settings });
                }

                var asm = p.GetType().Assembly;
                var request = Activator.CreateInstance(asm.GetType("PlayniteAchievements.Models.RefreshRequest"));
                request.GetType().GetProperty("Mode").SetValue(request, Enum.Parse(asm.GetType("PlayniteAchievements.Models.RefreshModeType"), "Missing"), null);
                var policy = Activator.CreateInstance(asm.GetType("PlayniteAchievements.Services.Refresh.RefreshExecutionPolicy"));
                var pt = policy.GetType();
                pt.GetProperty("ValidateAuthentication").SetValue(policy, false, null);   // (true asks to sign in, in a desktop window)
                pt.GetProperty("UseProgressWindow").SetValue(policy, false, null);
                pt.GetProperty("SwallowExceptions").SetValue(policy, true, null);
                pt.GetProperty("ErrorLogMessage").SetValue(policy, "Selene: checking every game failed.", null);
                var dispatcher = Dispatcher.CurrentDispatcher;
                Action<bool> finished = ok => dispatcher.BeginInvoke(new Action(() =>
                {
                    HomeNav.Log("achievements: every game checked, " + (ok ? "done" : "with errors"));
                    cachedAt = DateTime.MinValue;   // read again on the next look
                    if (ok) ProgressToast.ShowNote("Achievements are in", "Every game's trophies are on the Achievements card.", 0xea23);
                }));
                pt.GetProperty("OnRefreshCompleted").SetValue(policy, finished, null);
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;   // (the add-on keeps these to itself)
                var entry = p.GetType().GetProperty("RefreshEntryPoint", any).GetValue(p, null);
                entry.GetType().GetMethod("ExecuteAsync", any, null, new[] { request.GetType(), pt }, null).Invoke(entry, new[] { request, policy });
                File.WriteAllText(SetupFile, DateTime.UtcNow.ToString("o"));
                HomeNav.Log("achievements: Playnite Achievements set up, checking every game");
                ProgressToast.ShowNote("Checking your achievements", "This might take a while.", 0xea23);
            }
            catch (Exception ex) { HomeNav.Log("achievements: setting up failed: " + (ex.InnerException ?? ex).Message); }
        }
    }
}
