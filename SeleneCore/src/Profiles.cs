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
        // what's behind the home: "blend" (game art fading into the wallpaper, the default), "art" or "wallpaper"
        public string HomeBackground { get; set; }
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
        // set when profiles.json exists but couldn't be loaded: nothing is saved, so the file on disk is never overwritten
        private static string loadError;

        public static void Init(string dataFolder)
        {
            // developer switch: %TEMP%\selene-dev-profiles keeps profiles in a scratch folder (to try the first launch)
            if (File.Exists(Path.Combine(Path.GetTempPath(), "selene-dev-profiles"))) dataFolder = Path.Combine(Path.GetTempPath(), "selene-dev-profiles-data");
            Directory.CreateDirectory(dataFolder);
            file = Path.Combine(dataFolder, "profiles.json");
            if (!File.Exists(file)) return;
            string json;
            try { json = File.ReadAllText(file); }
            catch (IOException ex) { Fail(ex, "profiles.json is locked or unreadable right now"); return; }
            catch (UnauthorizedAccessException ex) { Fail(ex, "no permission to read profiles.json"); return; }
            try
            {
                store = Serialization.FromJson<ProfileStore>(json) ?? new ProfileStore();
                if (store.Profiles == null) store.Profiles = new List<Profile>();
            }
            catch (Exception ex)
            {
                // damaged file: set it aside for recovery and start fresh
                var kept = file + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Move(file, kept); }
                catch (Exception moveEx) { Fail(moveEx, "profiles.json is damaged and couldn't be set aside"); return; }
                logger.Error(ex, "Selene Core: profiles.json was damaged, moved to " + kept + " and starting fresh");
                store = new ProfileStore();
            }
        }

        private static void Fail(Exception ex, string why)
        {
            loadError = why;
            store = new ProfileStore();
            logger.Error(ex, "Selene Core: " + why + "; profiles are off until the next start and nothing will be saved");
        }

        // why profiles couldn't be loaded this session (null when they loaded fine), for the screens to show
        public static string LoadError { get { return loadError; } }

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

        // saves changes made to a profile's fields (the profile steps in edit mode)
        public static void Update(Profile profile)
        {
            Save();
            if (Active == profile) ApplyLook();
        }

        public static void Remove(Guid id)
        {
            store.Profiles.RemoveAll(p => p.Id == id);
            if (store.ActiveId == id) store.ActiveId = Guid.Empty;
            Save();
        }

        // built-in avatars are kept as "Avatars/<file>" (the theme folder can move); pictures people add are full paths
        public static string AvatarFile(Profile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Avatar)) return null;
            if (Path.IsPathRooted(profile.Avatar)) return profile.Avatar;
            return SelenePaths.ThemeFile(profile.Avatar.Split('/'));
        }

        // a picture someone picked (or their Steam avatar): copied into Selene Core's data so it survives the original moving
        public static string KeepPicture(string source)
        {
            var folder = Path.Combine(SelenePaths.DataFolder, "avatars");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            File.Copy(source, target);
            return target;
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

        public static void SetHomeBackground(string look)
        {
            var p = Active;
            if (p == null) return;
            p.HomeBackground = look;
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
            // developer switch: %TEMP%\selene-dev-theme holding a theme id overrides it (to check every screen in each theme)
            var dev = Path.Combine(Path.GetTempPath(), "selene-dev-theme");
            if (File.Exists(dev) && File.ReadAllText(dev).Trim().Length > 0) theme = File.ReadAllText(dev).Trim();
            ThemePalettes.Apply(theme);
            var wallpaper = p != null && !string.IsNullOrEmpty(p.Wallpaper) ? p.Wallpaper : ThemePalettes.DefaultWallpaper(theme);
            Application.Current.Resources["SeleneWallpaperPath"] = wallpaper;
            Application.Current.Resources["SeleneWallpaper"] = Wallpapers.Load(wallpaper);
            ApplyHomeBackground(p != null ? p.HomeBackground : null);
        }

        public static readonly string[] HomeBackgrounds = { "blend", "art", "wallpaper" };

        // blend uses theme\Constants.xaml's values; the other looks override them
        public static void ApplyHomeBackground(string look)
        {
            var res = Application.Current.Resources;
            foreach (var key in new[] { "SeleneArtOpacity", "SeleneArtMask", "SeleneTintOpacity" }) res.Remove(key);
            if (look == "art")
            {
                res["SeleneArtOpacity"] = 1.0;
                res["SeleneArtMask"] = null;
                res["SeleneTintOpacity"] = 0.0;
            }
            else if (look == "wallpaper") res["SeleneArtOpacity"] = 0.0;
        }

        private static void Save()
        {
            if (loadError != null)
            {
                logger.Error("Selene Core: not saving profiles, they weren't loaded this session (" + loadError + ")");
                return;
            }
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, Serialization.ToJson(store, true));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }
    }
}
