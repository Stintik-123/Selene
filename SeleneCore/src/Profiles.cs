using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace Selene
{
    public class Profile
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Avatar { get; set; }
        public string Theme { get; set; }
        // null = the theme's default wallpaper, so switching themes changes it until the user picks one themselves
        public string Wallpaper { get; set; }
        public DateTime Created { get; set; }
        public List<Guid> Favorites { get; set; }
        public Dictionary<Guid, ulong> Playtime { get; set; }
        public Dictionary<Guid, DateTime> LastPlayed { get; set; }

        public Profile()
        {
            Favorites = new List<Guid>();
            Playtime = new Dictionary<Guid, ulong>();
            LastPlayed = new Dictionary<Guid, DateTime>();
        }
    }

    public class ProfileStore
    {
        public List<Profile> Profiles { get; set; }
        public Guid ActiveId { get; set; }

        public ProfileStore() { Profiles = new List<Profile>(); }
    }

    // Selene's own profiles (Playnite has a single user): look, favorites, playtime and last played per person.
    // The game library and store accounts stay shared. Saved in Selene Core's data folder as profiles.json.
    public static class Profiles
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static string file;
        private static ProfileStore store = new ProfileStore();

        public static void Init(string dataFolder)
        {
            Directory.CreateDirectory(dataFolder);
            file = Path.Combine(dataFolder, "profiles.json");
            if (!File.Exists(file)) return;
            try
            {
                store = Serialization.FromJsonFile<ProfileStore>(file) ?? new ProfileStore();
                if (store.Profiles == null) store.Profiles = new List<Profile>();
            }
            catch (Exception ex)
            {
                // keep the unreadable file for recovery instead of overwriting it on the next save
                var kept = file + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(file, kept);
                logger.Error(ex, "Selene Core: profiles.json could not be read, moved to " + kept + " and starting fresh");
                store = new ProfileStore();
            }
        }

        public static IReadOnlyList<Profile> All { get { return store.Profiles; } }

        public static bool FirstRun { get { return store.Profiles.Count == 0; } }

        public static Profile Active { get { return store.Profiles.FirstOrDefault(p => p.Id == store.ActiveId); } }

        // the avatars shipped with the theme (theme\Avatars)
        public static string[] BuiltInAvatars()
        {
            var folder = SelenePaths.ThemeFile("Avatars");
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jpg").OrderBy(f => f).ToArray() : new string[0];
        }

        public static Profile Create(string name, string avatar, string theme, string wallpaper)
        {
            var profile = new Profile { Id = Guid.NewGuid(), Name = name, Avatar = avatar, Theme = theme, Wallpaper = wallpaper, Created = DateTime.Now };
            store.Profiles.Add(profile);
            store.ActiveId = profile.Id;
            Save();
            return profile;
        }

        public static void SignIn(Guid id)
        {
            if (store.Profiles.All(p => p.Id != id)) throw new ArgumentException("no Selene profile with id " + id);
            store.ActiveId = id;
            Save();
            ApplyLook();
        }

        public static void SetTheme(string theme)
        {
            var p = Active;
            if (p == null) return;
            p.Theme = theme;
            Save();
            ApplyLook();
        }

        // null puts the theme's default wallpaper back
        public static void SetWallpaper(string wallpaper)
        {
            var p = Active;
            if (p == null) return;
            p.Wallpaper = wallpaper;
            Save();
            ApplyLook();
        }

        public static bool ToggleFavorite(Guid gameId)
        {
            var p = Active;
            if (p == null) return false;
            bool now = !p.Favorites.Remove(gameId);
            if (now) p.Favorites.Add(gameId);
            Save();
            return now;
        }

        public static void RecordSession(Guid gameId, ulong seconds)
        {
            var p = Active;
            if (p == null) return;
            ulong total;
            p.Playtime.TryGetValue(gameId, out total);
            p.Playtime[gameId] = total + seconds;
            p.LastPlayed[gameId] = DateTime.Now;
            Save();
        }

        // colors and wallpaper of the signed-in profile (Selene's own look before anyone signs in)
        public static void ApplyLook()
        {
            var p = Active;
            var theme = p != null && !string.IsNullOrEmpty(p.Theme) ? p.Theme : ThemePalettes.DefaultId;
            ThemePalettes.Apply(theme);
            var wallpaper = p != null && !string.IsNullOrEmpty(p.Wallpaper) ? p.Wallpaper : ThemePalettes.DefaultWallpaper(theme);
            Application.Current.Resources["SeleneWallpaper"] = wallpaper;
        }

        private static void Save()
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, Serialization.ToJson(store, true));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }
    }
}
