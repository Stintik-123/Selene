using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;

namespace Selene
{
    // The Ludusavi add-on needs the Ludusavi program itself (github.com/mtkennerly/ludusavi, MIT).
    // Selene Core downloads it into its own data folder and points the add-on at it, so save backups just work.
    public static class LudusaviApp
    {
        public static readonly Guid PluginId = Guid.Parse("72e2de43-d859-44d8-914e-4277741c8208");
        private static readonly ILogger logger = LogManager.GetLogger();

        private class Release
        {
            public string tag_name { get; set; }
            public List<Asset> assets { get; set; }
        }

        private class Asset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
        }

        public static string Exe { get { return Path.Combine(SelenePaths.DataFolder, "Ludusavi", "ludusavi.exe"); } }

        public static bool Installed { get { return File.Exists(Exe) || OnPath(); } }

        private static bool OnPath()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            return path.Split(Path.PathSeparator).Any(dir =>
            {
                try { return File.Exists(Path.Combine(dir.Trim(), "ludusavi.exe")); }
                catch (ArgumentException) { return false; }
            });
        }

        // downloads the latest Windows build if needed, then tells the add-on where it is; throws when either step fails
        public static void Setup(IPlayniteAPI api)
        {
            if (!File.Exists(Exe)) Download();
            PointAddon(api);
        }

        private static void Download()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Selene-Core";
                var release = Serialization.FromJson<Release>(web.DownloadString("https://api.github.com/repos/mtkennerly/ludusavi/releases/latest"));
                var zip = release.assets.FirstOrDefault(a => a.name.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase));
                if (zip == null) throw new InvalidOperationException("Ludusavi " + release.tag_name + " has no Windows 64-bit download");
                if (!zip.browser_download_url.StartsWith("https://github.com/mtkennerly/ludusavi/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("unexpected Ludusavi download location " + zip.browser_download_url);

                var folder = Path.GetDirectoryName(Exe);
                var temp = Path.Combine(Path.GetTempPath(), zip.name);
                web.DownloadFile(zip.browser_download_url, temp);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                ZipFile.ExtractToDirectory(temp, folder);
                File.Delete(temp);
                if (!File.Exists(Exe)) throw new FileNotFoundException("ludusavi.exe not found in " + zip.name, Exe);
                logger.Info("Selene Core: installed Ludusavi " + release.tag_name + " to " + folder);
            }
        }

        private static void PointAddon(IPlayniteAPI api)
        {
            var addon = api.Addons.Plugins.FirstOrDefault(p => p.Id == PluginId);
            if (addon != null)
            {
                // running add-on: change its live settings so it saves our path instead of overwriting it later
                var settings = addon.GetSettings(false);
                var property = settings != null ? settings.GetType().GetProperty("ExecutablePath") : null;
                if (property == null) throw new InvalidOperationException("the Ludusavi add-on no longer has an ExecutablePath setting");
                settings.BeginEdit();
                property.SetValue(settings, Exe, null);
                settings.EndEdit();
                return;
            }
            // just installed, loads after the next restart: leave the path in its settings file
            var folder = Path.Combine(api.Paths.ExtensionsDataPath, PluginId.ToString());
            var file = Path.Combine(folder, "config.json");
            Directory.CreateDirectory(folder);
            var config = File.Exists(file) ? Serialization.FromJsonFile<Dictionary<string, object>>(file) : new Dictionary<string, object>();
            config["ExecutablePath"] = Exe;
            File.WriteAllText(file, Serialization.ToJson(config, true));
        }
    }
}
