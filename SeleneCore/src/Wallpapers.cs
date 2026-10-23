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
        private static readonly Playnite.SDK.ILogger logger = Playnite.SDK.LogManager.GetLogger();
        private static readonly string[] Pictures = { ".jpg", ".jpeg", ".png", ".bmp" };

        // a wallpaper as a picture for the screens (null when there's none or it can't be read)
        public static System.Windows.Media.ImageSource Load(string file, int width = 1920)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = width;
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                logger.Warn("Selene Core: can't load wallpaper " + file + ": " + ex.Message);
                return null;
            }
        }

        // puts a wallpaper behind Selene; the one already showing is left alone (a reloaded copy flickered)
        private static string shown;

        public static void Show(string file)
        {
            var res = System.Windows.Application.Current.Resources;
            if (shown != null && string.Equals(shown, file ?? "", StringComparison.OrdinalIgnoreCase)) return;
            shown = file ?? "";
            res["SeleneWallpaperPath"] = file;
            var pic = Load(file);
            res["SeleneWallpaper"] = pic;
            Contrast.Wallpaper(pic);   // the home's shades as deep as this picture needs
        }

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
            var found = new List<string>();
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                try
                {
                    found.AddRange(Directory.GetFiles(current).Where(f => Pictures.Contains(Path.GetExtension(f).ToLowerInvariant())));
                    if (depth == SearchOption.AllDirectories)
                        foreach (var sub in Directory.GetDirectories(current)) pending.Push(sub);
                }
                catch (UnauthorizedAccessException) { logger.Warn("Selene Core: no access to " + current + ", skipping it for wallpapers"); }
                catch (PathTooLongException) { logger.Warn("Selene Core: path too long, skipping " + current + " for wallpapers"); }
                catch (IOException ex) { logger.Warn("Selene Core: can't read " + current + " for wallpapers: " + ex.Message); }
            }
            return found.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
    }
}
