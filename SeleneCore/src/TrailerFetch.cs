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
        private static readonly HashSet<Guid> browsed = new HashSet<Guid>();   // looked at (their trailer is wanted)
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
            if (HomeNav.Api == null || (Mode == "off" && !Features.On("artdownload"))) return;
            QueueLibrary();   // art (light) for every game; trailers too with "Whole library"
            var list = new List<Guid>();
            var g = AsGame(game);
            if (g != null) list.Add(g.Id);
            if (neighbours != null) foreach (var o in neighbours) { var n = AsGame(o); if (n != null) list.Add(n.Id); }
            lock (gate)
            {
                // to the front in this order (the selected game first); a game the library pass already did (its art)
                // comes again the first time it's looked at, for its trailer
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var id = list[i];
                    if (browsed.Add(id)) tried.Remove(id);
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
                    if (queue.Count == 0 || (Mode == "off" && !Features.On("artdownload"))) { worker = null; break; }
                    id = queue.First.Value;
                    queue.RemoveFirst();
                    if (!tried.Add(id)) continue;
                }
                bool looked;
                lock (gate) looked = browsed.Contains(id);
                asked = false;
                try { if (Fetch(id)) made++; }
                catch (Exception ex)
                {
                    HomeNav.Log("trailer download: " + id + " failed: " + ex.Message);
                    // Steam saying "too many requests": that game goes to the back, and a minute's rest
                    var web = ex as WebException;
                    var status = web != null && web.Response is HttpWebResponse ? (int)((HttpWebResponse)web.Response).StatusCode : 0;
                    if (status == 429) { lock (gate) { tried.Remove(id); queue.AddLast(id); } System.Threading.Thread.Sleep(60000); }
                }
                // the library pass goes gently (Steam limits how often it's asked); the games being looked at don't wait
                if (!looked && asked) System.Threading.Thread.Sleep(1500);
            }
            if (made > 0) HomeNav.Log("trailer download: " + made + " new");
        }

        // true when a trailer was made
        private static bool Fetch(Guid id)
        {
            var g = HomeNav.Api.Database.Games.Get(id);
            if (g == null) return false;
            string appId = null;
            bool appIdLooked = false;
            Func<string> steamId = () =>
            {
                if (!appIdLooked)
                {
                    appIdLooked = true;
                    if (g.PluginId.ToString() == SteamPlugin) appId = g.GameId;
                    else { asked = true; appId = GameMedia.SteamAppIdByName(g.Name); }   // (a search on Steam's store)
                }
                return appId;
            };
            if (Features.On("artdownload")) { try { FetchArt(g, steamId); } catch (Exception ex) { HomeNav.Log("art download: " + g.Name + " failed: " + ex.Message); } }

            // the trailer: a game looked at, or every game with "Whole library"
            bool wanted;
            lock (gate) wanted = Mode == "all" || (Mode == "browse" && browsed.Contains(id));
            if (!wanted) return false;
            var file = PathFor(id);
            if (System.IO.File.Exists(file)) return false;
            // nothing on Steam: asked again after a week
            var none = System.IO.Path.Combine(SelenePaths.Media("Trailers"), id + ".none");
            if (System.IO.File.Exists(none) && (DateTime.Now - System.IO.File.GetLastWriteTime(none)).TotalDays < 7) return false;

            var mpd = string.IsNullOrEmpty(steamId()) ? null : ManifestUrl(steamId());
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

        // ---- art: the logo (ExtraMetadata\games\<id>\logo.png, where Extra Metadata Loader keeps it) and the wide background
        // (the media folder's Hero\<id>.jpg) from Steam, for the games that have none: Spotlight, Collection and a game's page
        // show them over the plain store background. Asked again after a week when Steam had nothing.
        private const string SteamAssets = "https://shared.akamai.steamstatic.com/store_item_assets/";

        private static void FetchArt(Game g, Func<string> steamId)
        {
            var logo = System.IO.Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "ExtraMetadata", "games", g.Id.ToString(), "logo.png");
            var hero = System.IO.Path.Combine(SelenePaths.Media("Hero"), g.Id + ".jpg");
            bool needLogo = !System.IO.File.Exists(logo), needHero = !System.IO.File.Exists(hero);
            if (!needLogo && !needHero) return;
            var none = System.IO.Path.Combine(SelenePaths.Media("Trailers"), g.Id + ".noart");
            if (System.IO.File.Exists(none) && (DateTime.Now - System.IO.File.GetLastWriteTime(none)).TotalDays < 7) return;
            var id = steamId();
            if (string.IsNullOrEmpty(id)) { System.IO.File.WriteAllText(none, g.Name); return; }

            // the file names (newer games have them in hashed folders) from Steam's store service
            string format = "steam/apps/" + id + "/${FILENAME}", heroName = "library_hero.jpg";
            try
            {
                var q = "{\"ids\":[{\"appid\":" + id + "}],\"context\":{\"language\":\"english\",\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}";
                var root = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(Text("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(q))) as Dictionary<string, object>;
                var items = root != null && root.ContainsKey("response") ? ((Dictionary<string, object>)root["response"])["store_items"] as object[] : null;
                var assets = items != null && items.Length > 0 && ((Dictionary<string, object>)items[0]).ContainsKey("assets") ? ((Dictionary<string, object>)items[0])["assets"] as Dictionary<string, object> : null;
                if (assets != null)
                {
                    if (assets.ContainsKey("asset_url_format")) format = (string)assets["asset_url_format"];
                    if (assets.ContainsKey("library_hero")) heroName = (string)assets["library_hero"];
                }
            }
            catch (Exception ex) { HomeNav.Log("art download: " + g.Name + ": store service: " + ex.Message); }
            Func<string, string> url = name => SteamAssets + format.Replace("${FILENAME}", name);

            bool got = false;
            if (needLogo)
                foreach (var name in new[] { "logo_2x.png", "logo.png" })
                    if (Save(url(name), logo, true)) { got = true; HomeNav.Log("art download: " + g.Name + " logo"); break; }
            if (needHero && Save(url(heroName), hero, false))
            {
                got = true;
                HomeNav.Log("art download: " + g.Name + " background");
                // the theme only looks in the media folder once something is there (SeleneShell.ApplyArtRoots)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if ("".Equals(Application.Current.Resources["SeleneHeroRoot"])) SeleneShell.ApplyArtRoots();
                }));
            }
            if (!got && (needLogo && !System.IO.File.Exists(logo)) && (needHero && !System.IO.File.Exists(hero))) System.IO.File.WriteAllText(none, g.Name);
        }

        // a picture to a file (through .part, so a cut download never shows); false when there is none (or it isn't one)
        private static bool Save(string from, string to, bool png)
        {
            byte[] data;
            try { using (var web = Client()) data = web.DownloadData(from); }
            catch (WebException ex)
            {
                // only "not there" means there is none; anything else (too many requests, no connection) is tried another time
                var r = ex.Response as HttpWebResponse;
                if (r != null && (r.StatusCode == HttpStatusCode.NotFound || r.StatusCode == HttpStatusCode.Forbidden)) return false;
                throw;
            }
            bool ok = png ? data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 : data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8;
            if (!ok) return false;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to));
            var part = to + ".part";
            System.IO.File.WriteAllBytes(part, data);
            if (System.IO.File.Exists(to)) { System.IO.File.Delete(part); return true; }
            System.IO.File.Move(part, to);
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

        private static bool asked;   // the last game asked Steam something (the library pass rests only after those)

        private static WebClient Client()
        {
            asked = true;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }   // (as Selene's other downloads)
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
