// What a video file holds, read from its own header (no decoder needed): the container and the codec of its picture and
// sound tracks. MP4 / MOV: the sample descriptions in moov > trak > mdia > minf > stbl > stsd; Matroska / WebM: the CodecID
// strings. Used to know whether a trailer has sound at all, and which player can decode what (TrailerFix).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Selene
{
    public class MediaProbe
    {
        public string Container = "unknown";   // mp4 / matroska / unknown
        public string Video;                    // avc1, hvc1, av01, vp09, V_VP9... (null: no picture track found)
        public string Audio;                    // mp4a, Opus, A_OPUS, ac-3... (null: no sound track found)

        public bool HasVideo { get { return Video != null; } }
        public bool HasAudio { get { return Audio != null; } }
        public override string ToString() { return Container + " video=" + (Video ?? "none") + " audio=" + (Audio ?? "none"); }

        // the picture is one Windows' own player decodes without extra Store extensions
        public bool VideoWindowsBasic { get { var v = Norm(Video); return v == "avc" || v == "mpeg4"; } }
        // Chromium (Playnite's browser, built without the licensed codecs) decodes it
        public bool VideoBrowser { get { var v = Norm(Video); return v == "vp9" || v == "vp8" || v == "av1"; } }
        public bool AudioWindows { get { var a = Norm(Audio); return a == "aac" || a == "mp3" || a == "ac3" || a == "flac" || a == "pcm" || a == "alac"; } }
        public bool AudioBrowser { get { var a = Norm(Audio); return a == "opus" || a == "vorbis" || a == "flac" || a == "mp3" || a == "pcm"; } }

        public static string Norm(string codec)
        {
            if (codec == null) return null;
            var c = codec.ToLowerInvariant();
            if (c == "avc1" || c == "avc3" || c.Contains("avc")) return "avc";
            if (c == "hvc1" || c == "hev1" || c.Contains("hevc")) return "hevc";
            if (c == "av01" || c.Contains("av1")) return "av1";
            if (c == "vp09" || c.Contains("vp9")) return "vp9";
            if (c == "vp08" || c.Contains("vp8")) return "vp8";
            if (c == "mp4v" || c.Contains("mpeg4/iso/asp") || c.Contains("mpeg4/iso/sp")) return "mpeg4";
            if (c == "mp4a" || c.Contains("aac")) return "aac";
            if (c == "opus" || c.Contains("opus")) return "opus";
            if (c.Contains("vorbis")) return "vorbis";
            if (c == "flac" || c.Contains("flac")) return "flac";
            if (c == ".mp3" || c == "mp3" || c.Contains("mpeg/l3")) return "mp3";
            if (c == "ac-3" || c == "ec-3" || c.Contains("ac3")) return "ac3";
            if (c == "alac") return "alac";
            if (c == "lpcm" || c == "sowt" || c == "twos" || c.Contains("pcm")) return "pcm";
            return c;
        }

        public static MediaProbe Read(string path)
        {
            var p = new MediaProbe();
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var head = new byte[12];
                    if (f.Read(head, 0, 12) < 12) return p;
                    if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) { p.Container = "matroska"; ReadMatroska(f, p); }
                    else if (Encoding.ASCII.GetString(head, 4, 4) == "ftyp" || Encoding.ASCII.GetString(head, 4, 4) == "moov") { p.Container = "mp4"; f.Position = 0; Boxes(f, 0, f.Length, p, 0, null); }
                }
            }
            catch (Exception ex) { HomeNav.Log("media probe: " + Path.GetFileName(path) + ": " + ex.Message); }
            return p;
        }

        // ---- MP4 / MOV ----
        private static readonly HashSet<string> Containers = new HashSet<string> { "moov", "trak", "mdia", "minf", "stbl" };

        private static void Boxes(Stream f, long from, long to, MediaProbe p, int depth, string handler)
        {
            long pos = from;
            var hdr = new byte[16];
            while (pos + 8 <= to && depth < 8)
            {
                f.Position = pos;
                if (f.Read(hdr, 0, 8) < 8) return;
                long size = ((long)hdr[0] << 24) | ((long)hdr[1] << 16) | ((long)hdr[2] << 8) | hdr[3];
                var type = Encoding.ASCII.GetString(hdr, 4, 4);
                long body = pos + 8;
                if (size == 1) { if (f.Read(hdr, 8, 8) < 8) return; size = 0; for (int i = 8; i < 16; i++) size = (size << 8) | hdr[i]; body = pos + 16; }
                else if (size == 0) size = to - pos;
                if (size < 8 || pos + size > to + 8) return;
                if (type == "trak") handler = null;
                if (type == "hdlr" && body + 12 <= pos + size)
                {
                    var h = new byte[12];
                    f.Position = body;
                    if (f.Read(h, 0, 12) == 12) handler = Encoding.ASCII.GetString(h, 8, 4);   // version/flags, pre_defined, handler_type
                }
                if (type == "stsd" && body + 16 <= pos + size)
                {
                    var s = new byte[16];
                    f.Position = body;
                    if (f.Read(s, 0, 16) == 16)
                    {
                        var codec = Encoding.ASCII.GetString(s, 12, 4);   // version/flags, entry_count, then the first entry: size, format
                        if (handler == "vide" && p.Video == null) p.Video = codec;
                        else if (handler == "soun" && p.Audio == null) p.Audio = codec;
                    }
                }
                if (Containers.Contains(type))
                {
                    Boxes(f, body, pos + size, p, depth + 1, handler);
                    if (type == "mdia") handler = null;
                }
                // (hdlr sits in mdia before minf: the handler found is passed down to stbl through the recursion's argument)
                if (type == "mdia") { }
                pos += size;
            }
        }

        // ---- a copy without one kind of track (MP4 only) ----
        // Windows refuses a whole file when one track has no decoder (H.264 with Dolby AC-3 sound: no picture either). The
        // copy has the unwanted tracks' boxes renamed "free" (players skip those): no sizes or offsets change, nothing is
        // re-encoded. drop: "soun" (keep the picture) or "vide" (keep the sound). Kept in the temp folder for next time;
        // null when it can't be made.
        public static string WithoutTracks(string path, string drop)
        {
            try
            {
                var fi = new FileInfo(path);
                var dir = Path.Combine(Path.GetTempPath(), "selene-trailer-copies");
                Directory.CreateDirectory(dir);
                foreach (var old in new DirectoryInfo(dir).GetFiles()) if (old.LastWriteTime < DateTime.Now.AddDays(-3)) { try { old.Delete(); } catch { } }
                var key = (path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks).GetHashCode().ToString("x8");
                var copy = Path.Combine(dir, key + "-no" + drop + ".mp4");
                if (File.Exists(copy)) return copy;
                var offsets = new List<long>();
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) Traks(f, 0, f.Length, drop, offsets, 0);
                if (offsets.Count == 0) return null;
                var temp = copy + ".part";
                File.Copy(path, temp, true);
                using (var f = new FileStream(temp, FileMode.Open, FileAccess.Write))
                {
                    var free = Encoding.ASCII.GetBytes("free");
                    foreach (var at in offsets) { f.Position = at + 4; f.Write(free, 0, 4); }
                }
                if (File.Exists(copy)) File.Delete(copy);
                File.Move(temp, copy);
                return copy;
            }
            catch (Exception ex) { HomeNav.Log("media copy: " + ex.Message); return null; }
        }

        // the file offsets of the trak boxes whose handler is `kind`
        private static void Traks(Stream f, long from, long to, string kind, List<long> found, int depth)
        {
            long pos = from;
            var hdr = new byte[16];
            while (pos + 8 <= to && depth < 3)
            {
                f.Position = pos;
                if (f.Read(hdr, 0, 8) < 8) return;
                long size = ((long)hdr[0] << 24) | ((long)hdr[1] << 16) | ((long)hdr[2] << 8) | hdr[3];
                var type = Encoding.ASCII.GetString(hdr, 4, 4);
                long body = pos + 8;
                if (size == 1) { if (f.Read(hdr, 8, 8) < 8) return; size = 0; for (int i = 8; i < 16; i++) size = (size << 8) | hdr[i]; body = pos + 16; }
                else if (size == 0) size = to - pos;
                if (size < 8) return;
                if (type == "moov") Traks(f, body, pos + size, kind, found, depth + 1);
                else if (type == "trak")
                {
                    var p = new MediaProbe();
                    Boxes(f, body, pos + size, p, 1, null);
                    var seen = p.Video != null ? "vide" : p.Audio != null ? "soun" : null;
                    if (seen == kind) found.Add(pos);
                }
                pos += size;
            }
        }

        // ---- Matroska / WebM: the track entries' CodecID strings in the first megabyte ----
        private static void ReadMatroska(Stream f, MediaProbe p)
        {
            f.Position = 0;
            var buf = new byte[(int)Math.Min(f.Length, 1 << 20)];
            int n = f.Read(buf, 0, buf.Length);
            var text = Encoding.ASCII.GetString(buf, 0, n);
            foreach (var id in new[] { "V_VP9", "V_VP8", "V_AV1", "V_MPEG4/ISO/AVC", "V_MPEGH/ISO/HEVC", "V_MPEG4/ISO/ASP" })
                if (p.Video == null && text.Contains(id)) p.Video = id;
            foreach (var id in new[] { "A_OPUS", "A_VORBIS", "A_AAC", "A_AC3", "A_EAC3", "A_FLAC", "A_MPEG/L3", "A_PCM" })
                if (p.Audio == null && text.Contains(id)) p.Audio = id;
            if (text.Contains("webm")) p.Container = "matroska (webm)";
        }
    }
}
