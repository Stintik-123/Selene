using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace Selene
{
    public class NewsFeed
    {
        public string Url { get; set; }
        public bool Enabled { get; set; }
        // set while a feed is being read (not kept): what to call its articles, and the picture to use when one has none
        [ScriptIgnore] public string Label, Cover;
    }
    public class NewsArticle
    {
        public string Title, Url, Source, Image, Summary;
        public bool Game;   // from a game's own feed: the source is the game's name (its picture already says so)
        public DateTime Published;
    }
    public static class NewsFeeds
    {
        // a feed that is every Steam game in the library: each game's own news (set up by the News card)
        public const string SteamLibrary = "steam:library";
        public static bool IsSteamLibrary(string url) { return string.Equals(url, SteamLibrary, StringComparison.OrdinalIgnoreCase); }
        private static string FileName { get { return Path.Combine(SelenePaths.DataFolder, "news-feeds.json"); } }
        public static bool WebUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo);
        }
        public static List<NewsFeed> Load()
        {
            if (!File.Exists(FileName)) return new List<NewsFeed> { new NewsFeed { Url = SteamLibrary, Enabled = true }, new NewsFeed { Url = "https://www.pcgamer.com/rss/", Enabled = true } };   // (nothing saved yet: news without setting anything up)
            return (new JavaScriptSerializer().Deserialize<List<NewsFeed>>(File.ReadAllText(FileName)) ?? new List<NewsFeed>()).Where(f => f != null && (WebUrl(f.Url) || IsSteamLibrary(f.Url))).Take(30).ToList().Select(Migrate).ToList();
        }
        // Steam's one general feed is only Valve's own Team Fortress 2 posts: the library's games take its place
        private static NewsFeed Migrate(NewsFeed f)
        {
            if (f.Url != null && f.Url.StartsWith("https://store.steampowered.com/feeds/news.xml", StringComparison.OrdinalIgnoreCase)) f.Url = SteamLibrary;
            return f;
        }
        public static void Save(List<NewsFeed> feeds)
        {
            Directory.CreateDirectory(SelenePaths.DataFolder);
            var temp = FileName + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(feeds), Encoding.UTF8);
            if (File.Exists(FileName)) File.Replace(temp, FileName, null); else File.Move(temp, FileName);
            NewsCard.Refresh(true);
        }
        public static List<NewsArticle> Fetch(IEnumerable<NewsFeed> feeds, out string errors, Func<bool> cancelled = null)
        {
            var all = new List<NewsArticle>();
            var failed = new List<string>();
            foreach (var feed in feeds.Where(f => f.Enabled && !IsSteamLibrary(f.Url)))
            {
                if (cancelled != null && cancelled()) break;
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(feed.Url);
                    request.Timeout = 10000; request.ReadWriteTimeout = 10000;
                    request.UserAgent = "Selene RSS reader";
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    using (var response = request.GetResponse())
                    using (var input = response.GetResponseStream())
                    using (var memory = new MemoryStream())
                    {
                        var bytes = new byte[8192]; int count;
                        while ((count = input.Read(bytes, 0, bytes.Length)) > 0)
                        {
                            if (memory.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("Feed exceeds 2 MB");
                            memory.Write(bytes, 0, count);
                        }
                        memory.Position = 0;
                        foreach (var a in Parse(memory, response.ResponseUri))
                        {
                            if (feed.Label != null) { a.Source = feed.Label; a.Game = true; }
                            if (string.IsNullOrEmpty(a.Image)) a.Image = feed.Cover;
                            all.Add(a);
                        }
                    }
                }
                catch (Exception ex) { failed.Add(new Uri(feed.Url).Host); HomeNav.Log("news: " + feed.Url + ": " + ex.Message); }
            }
            errors = failed.Count == 0 ? "" : "Could not refresh: " + string.Join(", ", failed.ToArray());
            return all.GroupBy(a => a.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderByDescending(a => a.Published).Take(100).ToList();
        }
        private static string Child(XmlNode node, string name)
        {
            var found = node.SelectSingleNode("*[local-name()='" + name + "']");
            return found == null ? "" : found.InnerText.Trim();
        }
        // a line or two of the article's own words, without the markup
        private static string SummaryOf(XmlNode item)
        {
            try
            {
                foreach (var name in new[] { "description", "summary", "encoded", "content" })
                {
                    var html = WebUtility.HtmlDecode(Child(item, name));
                    if (html.Length == 0) continue;
                    var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");
                    text = System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
                    if (text.Length > 0) return text.Length > 560 ? text.Substring(0, 559).TrimEnd() + "\u2026" : text;
                }
            }
            catch { }
            return "";
        }

        // the article's picture: a media:thumbnail / media:content / enclosure that is an image, else the first <img> in its text
        private static string ImageOf(XmlNode item, Uri source)
        {
            try
            {
                foreach (XmlNode n in item.SelectNodes("*[local-name()='thumbnail' or local-name()='content' or local-name()='enclosure']"))
                {
                    var u = n.Attributes["url"]; var t = n.Attributes["type"]; var m = n.Attributes["medium"];
                    if (u == null) continue;
                    bool image = n.LocalName == "thumbnail" || (t != null && t.Value.StartsWith("image", StringComparison.OrdinalIgnoreCase)) || (m != null && m.Value == "image")
                              || (t == null && m == null && System.Text.RegularExpressions.Regex.IsMatch(u.Value, @"\.(jpe?g|png|gif)(\?|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                    Uri abs;
                    if (image && Uri.TryCreate(source, u.Value, out abs) && WebUrl(abs.AbsoluteUri)) return abs.AbsoluteUri;
                }
                foreach (var name in new[] { "description", "encoded", "summary", "content" })
                {
                    var html = WebUtility.HtmlDecode(Child(item, name));
                    if (html.Length == 0) continue;
                    var m = System.Text.RegularExpressions.Regex.Match(html, "<img[^>]+src=[\"']([^\"']+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Uri abs;
                    if (m.Success && Uri.TryCreate(source, WebUtility.HtmlDecode(m.Groups[1].Value), out abs) && WebUrl(abs.AbsoluteUri)) return abs.AbsoluteUri;
                }
            }
            catch { }
            return null;
        }
        public static List<NewsArticle> Parse(Stream input, Uri source)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 })) doc.Load(reader);
            var result = new List<NewsArticle>();
            foreach (XmlNode item in doc.SelectNodes("//*[local-name()='item' or local-name()='entry']"))
            {
                string link = Child(item, "link");
                foreach (XmlNode candidate in item.SelectNodes("*[local-name()='link']"))
                {
                    var href = candidate.Attributes["href"]; var rel = candidate.Attributes["rel"];
                    if (href != null && (rel == null || rel.Value == "alternate")) { link = href.Value; break; }
                }
                Uri url;
                if (!Uri.TryCreate(source, link, out url) || !WebUrl(url.AbsoluteUri) || string.IsNullOrWhiteSpace(link)) continue;
                string title = WebUtility.HtmlDecode(Child(item, "title"));
                if (string.IsNullOrWhiteSpace(title)) continue;
                string date = Child(item, "pubDate"); if (date == "") date = Child(item, "published"); if (date == "") date = Child(item, "updated");
                DateTimeOffset published;
                DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out published);
                result.Add(new NewsArticle { Title = title, Url = url.AbsoluteUri, Source = source.Host, Published = published.UtcDateTime, Image = ImageOf(item, source), Summary = SummaryOf(item) });
                if (result.Count == 100) break;
            }
            return result;
        }
    }
}
