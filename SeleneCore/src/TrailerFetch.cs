using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using Playnite.SDK.Models;

namespace Selene
{
    // Trailers without any setup: a game's Steam trailer (720p, its first minute, with sound) is downloaded into
    // ExtraMetadata\games\<id>\VideoTrailer.mp4, the file Extra Metadata Loader keeps its trailers in, so Spotlight's trailer
    // cards, Collection and that add-on all find it. Steam only streams its trailers now (DASH pieces), so the pieces are
    // fetched and joined (Mp4Join), nothing to install. Setting "Download trailers": the games you look at (and the next few
    // along the row), the whole library in the background, or off. Games not on Steam are looked up there by name.
    public static class TrailerFetch
    {
        private const string SteamPlugin = "cb91dfc9-b977-43bf-8e70-55f46e410fab";
        private const int MaxHeight = 720;
        private const double MaxSeconds = 60;   // the cards play 30 s; Collection loops it

        private static readonly LinkedList<Guid> queue = new LinkedList<Guid>();
        private static readonly HashSet<Guid> tried = new HashSet<Guid>();   // this session: asked once
        private static readonly object gate = new object();
        private static System.Threading.Thread worker;
        private static bool libraryQueued;

        private static string Mode { get { return Features.Value("trailerdownload"); } }

        public static string PathFor(Guid id)
        {
            return System.IO.Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "ExtraMetadata", "games", id.ToString(), "VideoTrailer.mp4");
        }

        // a game was selected: its trailer first, then the next ones along (they're likely next)
        public static void Want(object game, System.Collections.IList neighbours)
        {
            if (HomeNav.Api == null || Mode == "off") return;
            if (Mode == "all") QueueLibrary();
            var list = new List<Guid>();
            var g = AsGame(game);
            if (g != null) list.Add(g.Id);
            if (neighbours != null) foreach (var o in neighbours) { var n = AsGame(o); if (n != null) list.Add(n.Id); }
            lock (gate)
            {
                // to the front in this order (the selected game first)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var id = list[i];
                    if (tried.Contains(id)) continue;
                    queue.Remove(id);
                    queue.AddFirst(id);
                }
            }
            Start();
        }

        // "Whole library": every game, the last played first, behind the ones being looked at
        private static void QueueLibrary()
        {
            if (libraryQueued) return;
            libraryQueued = true;
            var ids = HomeNav.Api.Database.Games.Where(x => !x.Hidden).OrderByDescending(x => x.LastActivity ?? DateTime.MinValue).Select(x => x.Id).ToList();
            lock (gate) foreach (var id in ids) if (!queue.Contains(id)) queue.AddLast(id);
            Start();
        }

        private static void Start()
        {
            lock (gate)
            {
                if (worker != null || queue.Count == 0) return;
                worker = new System.Threading.Thread(Work) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
                worker.Start();
            }
        }

        private static void Work()
        {
            int made = 0;
            while (true)
            {
                Guid id;
                lock (gate)
                {
                    if (queue.Count == 0 || Mode == "off") { worker = null; break; }
                    id = queue.First.Value;
                    queue.RemoveFirst();
                    if (!tried.Add(id)) continue;
                }
                try { if (Fetch(id)) made++; }
                catch (Exception ex) { HomeNav.Log("trailer download: " + id + " failed: " + ex.Message); }
            }
            if (made > 0) HomeNav.Log("trailer download: " + made + " new");
        }

        // true when a trailer was made
        private static bool Fetch(Guid id)
        {
            var g = HomeNav.Api.Database.Games.Get(id);
            if (g == null) return false;
            var file = PathFor(id);
            if (System.IO.File.Exists(file)) return false;
            // nothing on Steam: asked again after a week
            var none = System.IO.Path.Combine(SelenePaths.Media("Trailers"), id + ".none");
            if (System.IO.File.Exists(none) && (DateTime.Now - System.IO.File.GetLastWriteTime(none)).TotalDays < 7) return false;

            var appId = g.PluginId.ToString() == SteamPlugin ? g.GameId : GameMedia.SteamAppIdByName(g.Name);
            var mpd = string.IsNullOrEmpty(appId) ? null : ManifestUrl(appId);
            if (mpd == null) { System.IO.File.WriteAllText(none, g.Name); return false; }

            var started = DateTime.Now;
            var dir = System.IO.Path.GetDirectoryName(file);
            System.IO.Directory.CreateDirectory(dir);
            var part = file + ".part";
            try
            {
                Download(mpd, part);
                if (System.IO.File.Exists(file)) { System.IO.File.Delete(part); return false; }   // Extra Metadata Loader got there first
                System.IO.File.Move(part, file);
            }
            finally { try { if (System.IO.File.Exists(part)) System.IO.File.Delete(part); } catch { } }
            HomeNav.Log("trailer download: " + g.Name + " (" + (new System.IO.FileInfo(file).Length / 1048576.0).ToString("0.0") + " MB, " + (DateTime.Now - started).TotalSeconds.ToString("0.0") + " s)");
            Application.Current.Dispatcher.BeginInvoke(new Action(() => { HomeNav.TrailerReady(id); LibraryStage.TrailerReady(id); }));
            return true;
        }

        // the store's highlighted trailer (else its first), as H.264 DASH
        private static string ManifestUrl(string appId)
        {
            var json = Text("https://store.steampowered.com/api/appdetails?filters=movies&appids=" + appId);
            var root = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            object app, data, movies;
            if (root == null || !root.TryGetValue(appId, out app) || !(app is Dictionary<string, object>)) return null;
            if (!((Dictionary<string, object>)app).TryGetValue("data", out data) || !(data is Dictionary<string, object>)) return null;
            if (!((Dictionary<string, object>)data).TryGetValue("movies", out movies) || !(movies is object[])) return null;
            var list = ((object[])movies).OfType<Dictionary<string, object>>().ToList();
            var movie = list.FirstOrDefault(m => m.ContainsKey("highlight") && true.Equals(m["highlight"])) ?? list.FirstOrDefault();
            object url;
            return movie != null && movie.TryGetValue("dash_h264", out url) ? url as string : null;
        }

        // the video (720p, or the nearest below) and the audio, their pieces for the first minute, joined into one file
        private static void Download(string mpdUrl, string target)
        {
            var mpd = XDocument.Parse(Text(mpdUrl));
            XNamespace ns = mpd.Root.Name.Namespace;
            double length = Seconds((string)mpd.Root.Attribute("mediaPresentationDuration"));
            var sets = mpd.Descendants(ns + "AdaptationSet").ToList();
            var videoSet = sets.FirstOrDefault(s => (string)s.Attribute("contentType") == "video");
            var audioSet = sets.FirstOrDefault(s => (string)s.Attribute("contentType") == "audio");
            if (videoSet == null) throw new Exception("no video in the trailer");
            var videos = videoSet.Elements(ns + "Representation").OrderByDescending(r => (int?)r.Attribute("height") ?? 0).ToList();
            var video = videos.FirstOrDefault(r => ((int?)r.Attribute("height") ?? 0) <= MaxHeight) ?? videos.Last();
            var audio = audioSet != null ? audioSet.Elements(ns + "Representation").FirstOrDefault() : null;

            var baseUrl = mpdUrl.Substring(0, mpdUrl.LastIndexOf('/') + 1);
            byte[] vInit; List<byte[]> vChunks;
            Pieces(baseUrl, ns, video, length, out vInit, out vChunks);
            byte[] aInit = null; List<byte[]> aChunks = null;
            if (audio != null) Pieces(baseUrl, ns, audio, length, out aInit, out aChunks);
            if (vChunks.Count == 0) throw new Exception("no video pieces");
            Mp4Join.Write(target, vInit, vChunks, aInit, aChunks);
        }

        private static void Pieces(string baseUrl, XNamespace ns, XElement rep, double length, out byte[] init, out List<byte[]> chunks)
        {
            var tpl = rep.Element(ns + "SegmentTemplate") ?? rep.Parent.Element(ns + "SegmentTemplate");
            if (tpl == null) throw new Exception("no segment template");
            var repId = (string)rep.Attribute("id");
            double scale = (double?)tpl.Attribute("timescale") ?? 1, each = ((double?)tpl.Attribute("duration") ?? 0) / scale;
            int first = (int?)tpl.Attribute("startNumber") ?? 1;
            int count = each > 0 ? (int)Math.Ceiling(Math.Min(length > 0 ? length : MaxSeconds, MaxSeconds) / each) : 0;
            using (var web = Client())
            {
                init = web.DownloadData(baseUrl + Fill((string)tpl.Attribute("initialization"), repId, 0));
                chunks = new List<byte[]>();
                for (int n = first; n < first + count; n++)
                {
                    try { chunks.Add(web.DownloadData(baseUrl + Fill((string)tpl.Attribute("media"), repId, n))); }
                    catch (WebException) { break; }   // past the end
                }
            }
        }

        // $RepresentationID$, $Number$ / $Number%05d$
        private static string Fill(string tpl, string repId, int number)
        {
            var s = tpl.Replace("$RepresentationID$", repId);
            return Regex.Replace(s, @"\$Number(%0(\d+)d)?\$", m => m.Groups[2].Success ? number.ToString().PadLeft(int.Parse(m.Groups[2].Value), '0') : number.ToString());
        }

        // ISO 8601 "PT1M32.6S"
        private static double Seconds(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return 0;
            try { return System.Xml.XmlConvert.ToTimeSpan(iso).TotalSeconds; } catch { return 0; }
        }

        private static WebClient Client()
        {
            var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
            return wc;
        }

        private static string Text(string url) { using (var wc = Client()) return wc.DownloadString(url); }

        private static Game AsGame(object o)
        {
            var g = o as Game;
            if (g != null || o == null) return g;
            var prop = o.GetType().GetProperty("Game");
            return prop != null ? prop.GetValue(o, null) as Game : null;
        }
    }
}
