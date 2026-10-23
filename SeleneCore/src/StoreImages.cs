using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Selene
{
    // Pictures for the Store, fetched by Selene itself: a few downloads at a time (WPF's own web pictures, a hundred at
    // once, ran the process out of memory and took Playnite down), kept on disk for next time and offline, decoded small
    // and frozen, handed back on the UI thread. Anything that goes wrong just means no picture.
    public static class StoreImages
    {
        private const string Agent = "Selene/1.0 (Playnite theme; github.com/mattxslv/Selene)";
        private const int MemoryMax = 120;
        private static readonly Dictionary<string, ImageSource> memory = new Dictionary<string, ImageSource>();
        private static readonly LinkedList<string> order = new LinkedList<string>();
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(4);
        private static readonly object sync = new object();

        private static string Folder { get { return Path.Combine(SelenePaths.DataFolder, "store-pictures"); } }

        private static string FileFor(string url)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
                var sb = new StringBuilder();
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return Path.Combine(Folder, sb + ".img");
            }
        }

        // `done(picture)` on the UI thread (at once when it's already in memory); null when it can't be had
        public static void Get(string url, int width, Action<ImageSource> done)
        {
            if (string.IsNullOrEmpty(url)) { done(null); return; }
            var key = url + "|" + width;
            ImageSource hit;
            lock (sync)
            {
                if (memory.TryGetValue(key, out hit)) { order.Remove(key); order.AddFirst(key); }
            }
            if (hit != null) { done(hit); return; }
            var ui = System.Windows.Application.Current.Dispatcher;
            Task.Run(() =>
            {
                ImageSource img = null;
                gate.Wait();
                try { img = Decode(Bytes(url), width); }
                catch (Exception ex) { HomeNav.Log("store picture: " + ex.Message + " (" + url + ")"); }
                finally { gate.Release(); }
                if (img != null)
                    lock (sync)
                    {
                        memory[key] = img;
                        order.AddFirst(key);
                        while (order.Count > MemoryMax) { memory.Remove(order.Last.Value); order.RemoveLast(); }
                    }
                ui.BeginInvoke(new Action(() => done(img)));
            });
        }

        private static byte[] Bytes(string url)
        {
            var file = FileFor(url);
            try { if (File.Exists(file)) return File.ReadAllBytes(file); } catch { }
            byte[] data;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = Agent;
                data = web.DownloadData(url);
            }
            try { Directory.CreateDirectory(Folder); File.WriteAllBytes(file, data); } catch { }
            return data;
        }

        private static ImageSource Decode(byte[] data, int width)
        {
            if (data == null || data.Length == 0) return null;
            using (var ms = new MemoryStream(data))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = width;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
    }
}
