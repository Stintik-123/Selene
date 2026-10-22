using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace Selene
{
    public class ThemeInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Mode { get; set; }
        public string Accent { get; set; }
        public string Background { get; set; }
        public string Foreground { get; set; }
        public string Wallpaper { get; set; }
    }

    // Selene's color themes: theme\Palettes\<id>.xaml (made by tools/make_palettes.py) layered over theme\Constants.xaml
    public static class ThemePalettes
    {
        public const string DefaultId = "selene";
        private static readonly ILogger logger = LogManager.GetLogger();
        private static ResourceDictionary applied;

        public static List<ThemeInfo> All()
        {
            var index = SelenePaths.ThemeFile("Palettes", "palettes.json");
            if (!File.Exists(index))
            {
                logger.Error("Selene Core: theme list missing at " + index);
                return new List<ThemeInfo>();
            }
            return Serialization.FromJsonFile<List<ThemeInfo>>(index);
        }

        public static ThemeInfo Find(string id)
        {
            return All().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        // absolute path of a theme's default wallpaper, "" when it has none (Selene's own look uses its gradient)
        public static string DefaultWallpaper(string id)
        {
            var theme = Find(id);
            if (theme == null || string.IsNullOrEmpty(theme.Wallpaper)) return "";
            return SelenePaths.ThemeFile(theme.Wallpaper.Split('/'));
        }

        // swaps the colors of the whole theme; DynamicResource users update at once.
        // Selene's own look is theme\Constants.xaml itself, so it just removes any override.
        public static void Apply(string id)
        {
            if (string.IsNullOrEmpty(id)) id = DefaultId;
            var merged = Application.Current.Resources.MergedDictionaries;
            if (id == DefaultId)
            {
                if (applied != null) merged.Remove(applied);
                applied = null;
                HomeNav.Log("theme: " + id);
                return;
            }
            var file = SelenePaths.ThemeFile("Palettes", id + ".xaml");
            if (!File.Exists(file))
            {
                logger.Error("Selene Core: theme '" + id + "' not found at " + file + ", keeping the current colors");
                HomeNav.Log("theme: missing " + file);
                return;
            }
            ResourceDictionary colors;
            using (var stream = File.OpenRead(file)) colors = (ResourceDictionary)XamlReader.Load(stream);
            if (applied != null) merged.Remove(applied);
            merged.Add(colors);
            applied = colors;
            HomeNav.Log("theme: " + id);
        }
    }
}
