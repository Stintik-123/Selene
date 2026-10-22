using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Selene
{
    // Avatars that move: the Steam account's animated avatar (fetched from its public profile) and GIF pictures
    // people pick, played by stepping through their frames (WPF shows only a GIF's first frame by itself).
    public static class AnimatedAvatar
    {
        private static readonly Dictionary<string, List<KeyValuePair<BitmapSource, TimeSpan>>> frames = new Dictionary<string, List<KeyValuePair<BitmapSource, TimeSpan>>>();

        // a brush for a round avatar: a still picture, or the GIF playing
        public static ImageBrush Brush(string file, int width)
        {
            if (file != null && file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                var list = Frames(file);
                if (list != null && list.Count > 1)
                {
                    var brush = new ImageBrush(list[0].Key) { Stretch = Stretch.UniformToFill };
                    var play = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                    var at = TimeSpan.Zero;
                    foreach (var f in list)
                    {
                        play.KeyFrames.Add(new DiscreteObjectKeyFrame(f.Key, KeyTime.FromTimeSpan(at)));
                        at += f.Value;
                    }
                    play.Duration = at;
                    brush.BeginAnimation(ImageBrush.ImageSourceProperty, play);
                    return brush;
                }
            }
            var still = Wallpapers.Load(file, width);
            return still != null ? new ImageBrush(still) { Stretch = Stretch.UniformToFill } : null;
        }

        // the GIF's frames composed onto its full canvas (frames may cover only part of it), with their delays
        private static List<KeyValuePair<BitmapSource, TimeSpan>> Frames(string file)
        {
            List<KeyValuePair<BitmapSource, TimeSpan>> list;
            if (frames.TryGetValue(file, out list)) return list;
            list = null;
            try
            {
                var decoder = new GifBitmapDecoder(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                int w = decoder.Frames[0].PixelWidth, h = decoder.Frames[0].PixelHeight;
                var meta = decoder.Metadata as BitmapMetadata;
                try { w = Convert.ToInt32(meta.GetQuery("/logscrdesc/Width")); h = Convert.ToInt32(meta.GetQuery("/logscrdesc/Height")); } catch { }
                list = new List<KeyValuePair<BitmapSource, TimeSpan>>();
                BitmapSource canvas = null;
                foreach (var frame in decoder.Frames)
                {
                    var fm = frame.Metadata as BitmapMetadata;
                    int left = Query(fm, "/imgdesc/Left"), top = Query(fm, "/imgdesc/Top"), delay = Query(fm, "/grctlext/Delay"), disposal = Query(fm, "/grctlext/Disposal");
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        if (canvas != null) dc.DrawImage(canvas, new Rect(0, 0, w, h));
                        dc.DrawImage(frame, new Rect(left, top, frame.PixelWidth, frame.PixelHeight));
                    }
                    var composed = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    composed.Render(visual);
                    composed.Freeze();
                    list.Add(new KeyValuePair<BitmapSource, TimeSpan>(composed, TimeSpan.FromMilliseconds(Math.Max(20, delay * 10))));
                    // 2 = restore to background: the next frame starts from the canvas before this one
                    if (disposal != 2) canvas = composed;
                }
            }
            catch (Exception ex) { HomeNav.Log("avatar gif " + file + ": " + ex.Message); list = null; }
            frames[file] = list;
            return list;
        }

        private static int Query(BitmapMetadata m, string q)
        {
            try { return m != null && m.ContainsQuery(q) ? Convert.ToInt32(m.GetQuery(q)) : 0; } catch { return 0; }
        }

        // ---------------- the Steam account's animated avatar ----------------
        public static string SteamFile { get { return Path.Combine(SelenePaths.DataFolder, "steam-animated-avatar.gif"); } }

        // the saved animated avatar when there is one
        public static string Steam { get { return File.Exists(SteamFile) ? SteamFile : null; } }

        // reads the account's public profile page (at most once a day) and saves its animated avatar; off the UI thread
        public static void Fetch(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) return;
            if (File.Exists(SteamFile) && (DateTime.Now - File.GetLastWriteTime(SteamFile)).TotalHours < 24) return;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var web = new WebClient())
                    {
                        web.Headers[HttpRequestHeader.UserAgent] = "Selene-Core";
                        var page = web.DownloadString("https://steamcommunity.com/profiles/" + steamId);
                        // the avatar itself is the picture after the (optional) avatar frame, inside playerAvatarAutoSizeInner
                        int at = page.IndexOf("playerAvatarAutoSizeInner", StringComparison.Ordinal);
                        if (at < 0) return;
                        var block = page.Substring(at, Math.Min(4000, page.Length - at));
                        int frameEnd = block.IndexOf("profile_avatar_frame", StringComparison.Ordinal) >= 0 ? block.IndexOf("</div>", StringComparison.Ordinal) : 0;
                        var m = Regex.Match(block.Substring(Math.Max(0, frameEnd)), "<img[^>]+srcset=\"(https://[^\"]+\\.gif)\"");
                        if (!m.Success) { if (File.Exists(SteamFile)) File.Delete(SteamFile); return; }   // no animated avatar (any more)
                        var tmp = SteamFile + ".tmp";
                        web.DownloadFile(m.Groups[1].Value, tmp);
                        if (File.Exists(SteamFile)) File.Delete(SteamFile);
                        File.Move(tmp, SteamFile);
                        HomeNav.Log("steam: animated avatar saved");
                    }
                }
                catch (Exception ex) { HomeNav.Log("steam: no animated avatar (" + ex.Message + ")"); }
            });
        }
    }
}
