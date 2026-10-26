using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows;
using Playnite.SDK.Models;

namespace Selene
{
    // Game songs without any setup. UniPlaySong finds a game's song on YouTube, for which it needs two programs it doesn't
    // bring: yt-dlp (github.com/yt-dlp/yt-dlp, Unlicense) and ffmpeg (gyan.dev's essentials build). Without them it
    // finds nothing, ever: a new install had no game songs at all. Selene downloads both once (about 130 MB) into its own
    // data folder, points UniPlaySong at them, and asks it for a song for every game that has none (UniPlaySong itself
    // does the games added later). Switch: Sound & music > Get game songs.
    public static class SongSetup
    {
        private static readonly Guid UniPlaySongId = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        private static string Tools { get { return Path.Combine(SelenePaths.DataFolder, "Tools"); } }
        private static string YtDlp { get { return Path.Combine(Tools, "yt-dlp.exe"); } }
        private static string Ffmpeg { get { return Path.Combine(Tools, "ffmpeg.exe"); } }
        private static bool started;

        // how far the songs are, for the setting's row: "57 of 358 games have a song" (UniPlaySong keeps each game's song in
        // its own folder under ExtraMetadata / UniPlaySong / Games; a folder with one is a game with a song)
        private static readonly string[] Audio = { ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".flac" };
        private static string SongsRoot { get { return Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "ExtraMetadata", "UniPlaySong", "Games"); } }
        private static bool HasAudio(string dir) { return Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any(f => Audio.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase))); }

        // how many games have a song, of how many
        private static void Count(out int have, out int total)
        {
            have = 0;
            total = HomeNav.Api.Database.Games.Count(g => !g.Hidden);
            var root = SongsRoot;
            if (Directory.Exists(root))
                foreach (var dir in Directory.GetDirectories(root))
                    if (HasAudio(dir)) have++;
        }

        // Selene's downloads are running (its own tools, songs still missing)
        private static bool Fetching { get { return started && File.Exists(YtDlp) && File.Exists(Ffmpeg); } }

        // songs still on their way (for the red dot on the menu and the Sound & music group)
        public static bool Busy
        {
            get
            {
                try { if (!Fetching || HomeNav.Api == null) return false; int have, total; Count(out have, out total); return total > 0 && have < total; }
                catch { return false; }
            }
        }

        public static string Status()
        {
            try
            {
                if (HomeNav.Api == null) return null;
                int have, total;
                Count(out have, out total);
                if (total == 0) return null;
                return have >= total ? "Every game has a song" : have + " of " + total + " games have a song" + (Fetching ? ", more on the way" : "");
            }
            catch { return null; }
        }

        // resting on a game that has no song yet while the songs are still coming: said once a session, short (a new user hears
        // the theme's music where a song would be and might think songs don't work)
        private static bool noted;
        public static void NoteMissing(Guid gameId)
        {
            try
            {
                if (noted || HomeNav.Api == null || !Fetching || !Features.On("songsetup")) return;
                if (HasAudio(Path.Combine(SongsRoot, gameId.ToString()))) return;
                int have, total;
                Count(out have, out total);
                if (total == 0 || have >= total) return;
                noted = true;
                ProgressToast.ShowNote("Game songs", have + " of " + total + " so far", 0xE405);
            }
            catch (Exception ex) { HomeNav.Log("songs: note: " + ex.Message); }
        }

        // a little after start, in the background
        public static void StartLater()
        {
            if (started || !Features.On("songsetup") || HomeNav.Api == null) return;
            started = true;
            var plugin = HomeNav.Api.Addons.Plugins.FirstOrDefault(p => p.Id == UniPlaySongId);
            if (plugin == null) return;
            var service = Field(plugin, "_settingsService");
            var current = service != null ? Prop(service, "Current") : null;
            if (current == null) { HomeNav.Log("songs: UniPlaySong's settings not found (a newer version?)"); return; }
            string yt = Prop(current, "YtDlpPath") as string, ff = Prop(current, "FFmpegPath") as string;
            if (!string.IsNullOrEmpty(yt) && File.Exists(yt) && !string.IsNullOrEmpty(ff) && File.Exists(ff))
            {
                // set up already. By Selene: the songs still missing are asked for again (the list UniPlaySong works through
                // ends with Playnite; games that have a song are skipped). By hand: UniPlaySong's own business
                if (string.Equals(Path.GetFullPath(yt), Path.GetFullPath(YtDlp), StringComparison.OrdinalIgnoreCase))
                {
                    var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    later.Tick += (s, e) => { later.Stop(); Fetch(plugin, false); };
                    later.Start();
                }
                return;
            }

            var dispatcher = Application.Current.Dispatcher;
            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(20000);   // after the start-up's own work
                    bool fresh = !File.Exists(YtDlp) || !File.Exists(Ffmpeg);
                    if (fresh) dispatcher.BeginInvoke(new Action(() => ProgressToast.ShowNote("Game songs", "Setting up", 0xE405)));
                    Download();
                    dispatcher.BeginInvoke(new Action(() => PointAndFetch(plugin, service)));
                }
                catch (Exception ex) { HomeNav.Log("songs: setup failed: " + ex.Message); }
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
            worker.Start();
        }

        private static void Download()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Directory.CreateDirectory(Tools);
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Selene-Core";
                if (!File.Exists(YtDlp))
                {
                    web.DownloadFile("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", YtDlp + ".part");
                    File.Move(YtDlp + ".part", YtDlp);
                    HomeNav.Log("songs: yt-dlp downloaded");
                }
                if (!File.Exists(Ffmpeg))
                {
                    var zip = Path.Combine(Path.GetTempPath(), "selene-ffmpeg.zip");
                    web.DownloadFile("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", zip);
                    using (var archive = ZipFile.OpenRead(zip))
                    {
                        var exe = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                        if (exe == null) throw new FileNotFoundException("ffmpeg.exe isn't in the ffmpeg download");
                        exe.ExtractToFile(Ffmpeg + ".part", true);
                    }
                    File.Move(Ffmpeg + ".part", Ffmpeg);
                    try { File.Delete(zip); } catch { }
                    HomeNav.Log("songs: ffmpeg downloaded");
                }
            }
        }

        // UniPlaySong's settings with the two programs, swapped in through its own settings service (that's what updates its
        // downloader) and saved; then a song for every game without one, the last played first
        private static void PointAndFetch(object plugin, object service)
        {
            try
            {
                var current = Prop(service, "Current");
                var cloneMethod = typeof(Playnite.SDK.Data.Serialization).GetMethods().FirstOrDefault(m => m.Name == "GetClone" && m.IsGenericMethod);
                var next = cloneMethod != null ? cloneMethod.MakeGenericMethod(current.GetType()).Invoke(null, new[] { current }) : null;
                if (next == null) { HomeNav.Log("songs: couldn't copy UniPlaySong's settings"); return; }
                Set(next, "YtDlpPath", YtDlp);
                Set(next, "FFmpegPath", Ffmpeg);
                var update = service.GetType().GetMethod("UpdateSettings");
                if (update == null) { HomeNav.Log("songs: UniPlaySong can't take new settings (a newer version?)"); return; }
                update.Invoke(service, new object[] { next, "Selene" });
                var save = plugin.GetType().GetMethods().FirstOrDefault(m => m.Name == "SavePluginSettings" && m.IsGenericMethod);
                if (save != null) save.MakeGenericMethod(next.GetType()).Invoke(plugin, new[] { next });
                HomeNav.ForgetUps();   // Selene's music control looks the new settings object up
                HomeNav.Log("songs: UniPlaySong set up with yt-dlp and ffmpeg");

                if (!Fetch(plugin, true))
                {
                    // older/newer UniPlaySong: its next library update does every game added since "never"
                    Set(next, "LastAutoLibUpdateAssetsDownload", DateTime.MinValue);
                    if (save != null) save.MakeGenericMethod(next.GetType()).Invoke(plugin, new[] { next });
                    HomeNav.Log("songs: songs come with the next library update");
                }
            }
            catch (Exception ex) { HomeNav.Log("songs: pointing UniPlaySong failed: " + (ex.InnerException ?? ex).Message); }
        }

        // a song for every game without one, the last played first, through UniPlaySong's own automatic download; false when
        // this UniPlaySong has no such method
        private static bool Fetch(object plugin, bool note)
        {
            var fetch = plugin.GetType().GetMethod("AutoDownloadMusicForGamesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fetch == null) return false;
            var games = HomeNav.Api.Database.Games.Where(g => !g.Hidden).OrderByDescending(g => g.LastActivity ?? DateTime.MinValue).ToList();
            // off the UI thread, as UniPlaySong starts it (its search runs a while before it first yields); one game every
            // couple of seconds, the games that have a song skipped
            System.Threading.Tasks.Task.Run(() => { try { fetch.Invoke(plugin, new object[] { games }); } catch (Exception ex) { HomeNav.Log("songs: " + (ex.InnerException ?? ex).Message); } });
            HomeNav.Log("songs: UniPlaySong is getting songs for the games without one (" + games.Count + " games)");
            if (note) ProgressToast.ShowNote("Game songs", "On their way", 0xE405);
            return true;
        }

        private static object Field(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        private static object Prop(object o, string name)
        {
            var p = o != null ? o.GetType().GetProperty(name) : null;
            return p != null ? p.GetValue(o, null) : null;
        }

        private static void Set(object o, string name, object value)
        {
            var p = o.GetType().GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(o, value, null);
        }
    }
}
