using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Selene
{
    // Stills for clips (the Captures grid and card): Windows' own thumbnail for the file first (what Explorer shows; it
    // knows AV1 and HEVC when their extensions are installed), ffmpeg when it's on this PC and Windows has nothing. Made
    // one at a time on a thread of their own, kept as small JPEGs in Selene's media folder.
    internal static class Thumbnails
    {
        private const int Width = 480, Height = 270;
        private static readonly Queue<KeyValuePair<string, Action<string>>> queue = new Queue<KeyValuePair<string, Action<string>>>();
        private static readonly AutoResetEvent wake = new AutoResetEvent(false);
        private static Thread worker;
        private static string ffmpeg;
        private static bool ffmpegLooked;

        private static string Folder { get { return SelenePaths.Media("Captures"); } }

        // the still's file when it has been made already (quick, any thread)
        public static string Cached(string file)
        {
            var path = CachePath(file);
            return path != null && File.Exists(path) ? path : null;
        }

        private static string CachePath(string file)
        {
            try
            {
                var info = new FileInfo(file);
                var key = (file.ToLowerInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks).GetHashCode().ToString("x8");
                return Path.Combine(Folder, Path.GetFileNameWithoutExtension(file).Replace(' ', '_') + "-" + key + ".jpg");
            }
            catch { return null; }
        }

        // asks for the still; done(path or null) comes on the UI thread
        public static void Request(string file, Action<string> done)
        {
            lock (queue) queue.Enqueue(new KeyValuePair<string, Action<string>>(file, done));
            Start();
            wake.Set();
        }

        // the still, waiting for it (off the UI thread only)
        public static string Get(string file, int timeoutMs)
        {
            var have = Cached(file);
            if (have != null) return have;
            string result = null;
            using (var gate = new ManualResetEvent(false))
            {
                lock (queue) queue.Enqueue(new KeyValuePair<string, Action<string>>(file, p => { result = p; gate.Set(); }));
                Start();
                wake.Set();
                gate.WaitOne(timeoutMs);
            }
            return result;
        }

        private static void Start()
        {
            if (worker != null) return;
            worker = new Thread(Work) { IsBackground = true, Name = "Selene thumbnails" };
            worker.SetApartmentState(ApartmentState.STA);   // the shell's thumbnail handlers want an STA thread
            worker.Start();
        }

        private static void Work()
        {
            while (true)
            {
                KeyValuePair<string, Action<string>> job;
                lock (queue)
                {
                    if (queue.Count == 0) { Monitor.Exit(queue); wake.WaitOne(); Monitor.Enter(queue); continue; }
                    job = queue.Dequeue();
                }
                string result = null;
                try { result = Make(job.Key); }
                catch (Exception ex) { HomeNav.Log("thumbnail " + Path.GetFileName(job.Key) + ": " + ex.Message); }
                var done = job.Value;
                var r = result;
                if (done != null)
                {
                    var app = Application.Current;
                    if (app != null) app.Dispatcher.BeginInvoke(new Action(() => done(r)));
                }
            }
        }

        private static string Make(string file)
        {
            var path = CachePath(file);
            if (path == null) return null;
            if (File.Exists(path)) return path;
            if (!File.Exists(file)) return null;
            var bmp = FromShell(file);
            if (bmp != null) { Save(bmp, path); return path; }
            if (FromFfmpeg(file, path)) return path;
            return null;
        }

        private static void Save(BitmapSource bmp, string path)
        {
            var enc = new JpegBitmapEncoder { QualityLevel = 85 };
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var s = File.Create(path)) enc.Save(s);
        }

        // ---- Windows' thumbnail (IShellItemImageFactory, thumbnails only: no file icons) ----
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(Size size, int flags, out IntPtr bitmap);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Size { public int Width, Height; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        private static BitmapSource FromShell(string file)
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                SHCreateItemFromParsingName(file, IntPtr.Zero, ref iid, out factory);
                IntPtr hbm;
                const int ThumbnailOnly = 0x8, BiggerSizeOk = 0x1;
                int hr = factory.GetImage(new Size { Width = Width, Height = Height }, ThumbnailOnly | BiggerSizeOk, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DeleteObject(hbm); }
            }
            catch (Exception ex) { HomeNav.Log("thumbnail (shell) " + Path.GetFileName(file) + ": " + ex.Message); return null; }
        }

        // ---- ffmpeg, when this PC has it (on PATH or installed by winget) ----
        private static string Ffmpeg()
        {
            if (ffmpegLooked) return ffmpeg;
            ffmpegLooked = true;
            try
            {
                var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Trim().Length > 0).ToList();
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dirs.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
                foreach (var d in dirs)
                {
                    var p = Path.Combine(d.Trim(), "ffmpeg.exe");
                    if (File.Exists(p)) { ffmpeg = p; break; }
                }
                if (ffmpeg == null)
                {
                    var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
                    if (Directory.Exists(packages))
                        ffmpeg = Directory.GetDirectories(packages).Where(d => Path.GetFileName(d).IndexOf("FFmpeg", StringComparison.OrdinalIgnoreCase) >= 0)
                            .SelectMany(d => Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories)).FirstOrDefault();
                }
            }
            catch { }
            HomeNav.Log("thumbnails: ffmpeg " + (ffmpeg ?? "not found"));
            return ffmpeg;
        }

        private static bool FromFfmpeg(string file, string path)
        {
            var exe = Ffmpeg();
            if (exe == null) return false;
            try
            {
                var psi = new ProcessStartInfo(exe, "-v error -y -ss 1 -i \"" + file + "\" -frames:v 1 -vf \"scale=" + Width + ":-2\" \"" + path + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return false; }
                }
                return File.Exists(path);
            }
            catch (Exception ex) { HomeNav.Log("thumbnail (ffmpeg) " + Path.GetFileName(file) + ": " + ex.Message); return false; }
        }
    }
}
