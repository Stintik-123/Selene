using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Selene
{
    public class WallpaperChoice
    {
        public string File { get; set; }
        // the theme it came with, or "Your wallpapers"
        public string Group { get; set; }
    }

    // Every wallpaper the background picker offers: Selene's own, grouped by theme in theme-list order, then the user's folder
    public static class Wallpapers
    {
        public const string YourGroup = "Your wallpapers";
        private static readonly string[] Pictures = { ".jpg", ".jpeg", ".png", ".bmp" };

        public static List<WallpaperChoice> All()
        {
            var list = new List<WallpaperChoice>();
            var shipped = SelenePaths.ThemeFile("Wallpapers");
            if (Directory.Exists(shipped))
            {
                var order = ThemePalettes.All().Select(t => t.Id).ToList();
                var folders = Directory.GetDirectories(shipped)
                    .OrderBy(d => { int i = order.IndexOf(Path.GetFileName(d)); return i < 0 ? int.MaxValue : i; })
                    .ThenBy(d => d);
                foreach (var folder in folders)
                {
                    var theme = ThemePalettes.Find(Path.GetFileName(folder));
                    var group = theme != null ? theme.Name : Path.GetFileName(folder);
                    list.AddRange(PicturesIn(folder, SearchOption.TopDirectoryOnly).Select(f => new WallpaperChoice { File = f, Group = group }));
                }
            }
            var own = SelenePaths.WallpapersFolder;
            if (own != "" && Directory.Exists(own))
                list.AddRange(PicturesIn(own, SearchOption.AllDirectories).Select(f => new WallpaperChoice { File = f, Group = YourGroup }));
            return list;
        }

        private static IEnumerable<string> PicturesIn(string folder, SearchOption depth)
        {
            return Directory.GetFiles(folder, "*", depth)
                .Where(f => Pictures.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
    }
}
