using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Selene
{
    // Joins a DASH video track and its audio track (Steam's trailers: fragmented MP4 pieces, one init + numbered chunks each)
    // into one ordinary fragmented .mp4 that Playnite's player (Media Foundation) plays with its sound. Nothing is re-encoded:
    // the video's moov gets the audio's trak (as track 2) and a mehd with the length, then each chunk's moof+mdat goes in,
    // video then audio, the audio's track id and every fragment's sequence number rewritten.
    public static class Mp4Join
    {
        private class Box
        {
            public string Type;
            public byte[] Data;   // the whole box, header included
            public int Header;
        }

        public static void Write(string path, byte[] videoInit, List<byte[]> videoChunks, byte[] audioInit, List<byte[]> audioChunks)
        {
            var vInit = Boxes(videoInit, 0, videoInit.Length);
            var ftyp = vInit.First(b => b.Type == "ftyp");
            var vMoov = vInit.First(b => b.Type == "moov");
            var aMoov = audioInit != null ? Boxes(audioInit, 0, audioInit.Length).First(b => b.Type == "moov") : null;

            var vParts = Children(vMoov);
            var mvhd = vParts.First(b => b.Type == "mvhd");
            uint timescale = MvhdTimescale(mvhd);
            // the length: the longer track's chunks added up (tfdt + durations would be exact; chunk count x length is near enough
            // for the player's seek bar, and mehd only has to be at least the real length)
            ulong length = 0;
            foreach (var list in new[] { videoChunks, audioChunks })
                if (list != null) length = Math.Max(length, FragmentsLength(list, list == videoChunks ? TrackTimescale(vMoov) : TrackTimescale(aMoov), timescale));

            var moov = new List<byte[]>();
            moov.Add(SetMvhdNextTrack(mvhd.Data, aMoov != null ? 3u : 2u));
            foreach (var b in vParts.Where(b => b.Type == "trak")) moov.Add(SetTkhdId(b, 1));
            if (aMoov != null) foreach (var b in Children(aMoov).Where(b => b.Type == "trak")) moov.Add(SetTkhdId(b, 2));
            var mvex = new List<byte[]> { Mehd(length) };
            foreach (var b in Children(vParts.First(x => x.Type == "mvex")).Where(x => x.Type == "trex")) mvex.Add(SetTrexId(b.Data, 1));
            if (aMoov != null) foreach (var b in Children(Children(aMoov).First(x => x.Type == "mvex")).Where(x => x.Type == "trex")) mvex.Add(SetTrexId(b.Data, 2));
            moov.Add(Make("mvex", mvex));

            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                Put(file, ftyp.Data);
                Put(file, Make("moov", moov));
                uint seq = 1;
                int n = Math.Max(videoChunks.Count, audioChunks != null ? audioChunks.Count : 0);
                for (int i = 0; i < n; i++)
                {
                    if (i < videoChunks.Count) seq = PutFragments(file, videoChunks[i], 1, seq);
                    if (audioChunks != null && i < audioChunks.Count) seq = PutFragments(file, audioChunks[i], 2, seq);
                }
            }
        }

        // a chunk's moof+mdat pairs (its styp/sidx left out), the track id and sequence number set
        private static uint PutFragments(Stream file, byte[] chunk, uint track, uint seq)
        {
            foreach (var b in Boxes(chunk, 0, chunk.Length))
            {
                if (b.Type == "moof")
                {
                    var moof = (byte[])b.Data.Clone();
                    foreach (var c in Boxes(moof, b.Header, moof.Length, true))
                    {
                        if (c.Type == "mfhd") WriteU32(moof, c.Offset + 12, seq++);
                        else if (c.Type == "traf")
                            foreach (var t in Boxes(moof, c.Offset + 8, c.Offset + c.Data.Length, true))
                                if (t.Type == "tfhd") WriteU32(moof, t.Offset + 12, track);
                    }
                    Put(file, moof);
                }
                else if (b.Type == "mdat") Put(file, b.Data);
            }
            return seq;
        }

        // ---- boxes ----
        private class PlacedBox : Box { public int Offset; }

        private static List<Box> Boxes(byte[] data, int start, int end)
        {
            return Boxes(data, start, end, false).Cast<Box>().ToList();
        }

        private static List<PlacedBox> Boxes(byte[] data, int start, int end, bool placed)
        {
            var list = new List<PlacedBox>();
            int o = start;
            while (o + 8 <= end)
            {
                long size = ReadU32(data, o);
                int header = 8;
                if (size == 1) { size = (long)ReadU64(data, o + 8); header = 16; }
                else if (size == 0) size = end - o;
                if (size < header || o + size > end) break;
                var box = new PlacedBox { Type = System.Text.Encoding.ASCII.GetString(data, o + 4, 4), Header = header, Offset = o, Data = new byte[size] };
                Buffer.BlockCopy(data, o, box.Data, 0, (int)size);
                list.Add(box);
                o += (int)size;
            }
            return list;
        }

        private static List<Box> Children(Box b) { return Boxes(b.Data, b.Header, b.Data.Length); }

        private static byte[] Make(string type, List<byte[]> children)
        {
            int size = 8 + children.Sum(c => c.Length);
            var box = new byte[size];
            WriteU32(box, 0, (uint)size);
            System.Text.Encoding.ASCII.GetBytes(type, 0, 4, box, 4);
            int o = 8;
            foreach (var c in children) { Buffer.BlockCopy(c, 0, box, o, c.Length); o += c.Length; }
            return box;
        }

        // mehd v1: the whole length, in the movie's timescale
        private static byte[] Mehd(ulong length)
        {
            var box = new byte[20];
            WriteU32(box, 0, 20);
            System.Text.Encoding.ASCII.GetBytes("mehd", 0, 4, box, 4);
            box[8] = 1;
            WriteU64(box, 12, length);
            return box;
        }

        private static uint MvhdTimescale(Box mvhd) { return ReadU32(mvhd.Data, 8 + 4 + (mvhd.Data[8] == 1 ? 16 : 8)); }

        private static byte[] SetMvhdNextTrack(byte[] mvhd, uint next)
        {
            var copy = (byte[])mvhd.Clone();
            WriteU32(copy, copy.Length - 4, next);   // next_track_ID is mvhd's last field
            return copy;
        }

        // a trak with its tkhd's track id set (v0: after creation and modification times, 4 bytes each; v1: 8 each)
        private static byte[] SetTkhdId(Box trak, uint id)
        {
            var copy = (byte[])trak.Data.Clone();
            foreach (var b in Boxes(copy, trak.Header, copy.Length, true))
                if (b.Type == "tkhd") WriteU32(copy, b.Offset + 12 + (copy[b.Offset + 8] == 1 ? 16 : 8), id);
            return copy;
        }

        private static byte[] SetTrexId(byte[] trex, uint id)
        {
            var copy = (byte[])trex.Clone();
            WriteU32(copy, 12, id);
            return copy;
        }

        // a track's own timescale (its mdhd), what its fragments count durations in
        private static uint TrackTimescale(Box moov)
        {
            if (moov == null) return 0;
            foreach (var trak in Children(moov).Where(b => b.Type == "trak"))
                foreach (var mdia in Children(trak).Where(b => b.Type == "mdia"))
                    foreach (var mdhd in Children(mdia).Where(b => b.Type == "mdhd"))
                        return ReadU32(mdhd.Data, 8 + 4 + (mdhd.Data[8] == 1 ? 16 : 8));
            return 0;
        }

        // the chunks' length added up from their trun sample durations (or tfhd's default), in the movie's timescale
        private static ulong FragmentsLength(List<byte[]> chunks, uint trackScale, uint movieScale)
        {
            if (trackScale == 0 || movieScale == 0) return 0;
            ulong total = 0;
            foreach (var chunk in chunks)
                foreach (var moof in Boxes(chunk, 0, chunk.Length, true).Where(b => b.Type == "moof"))
                    foreach (var traf in Boxes(chunk, moof.Offset + 8, moof.Offset + moof.Data.Length, true).Where(b => b.Type == "traf"))
                    {
                        uint fallback = 0;
                        foreach (var b in Boxes(chunk, traf.Offset + 8, traf.Offset + traf.Data.Length, true))
                        {
                            int o = b.Offset;
                            uint flags = ReadU32(chunk, o + 8) & 0xFFFFFF;
                            if (b.Type == "tfhd")
                            {
                                int f = o + 16;
                                if ((flags & 0x1) != 0) f += 8;
                                if ((flags & 0x2) != 0) f += 4;
                                if ((flags & 0x8) != 0) fallback = ReadU32(chunk, f);
                            }
                            else if (b.Type == "trun")
                            {
                                uint count = ReadU32(chunk, o + 12);
                                int f = o + 16;
                                if ((flags & 0x1) != 0) f += 4;
                                if ((flags & 0x4) != 0) f += 4;
                                int each = ((flags & 0x100) != 0 ? 4 : 0) + ((flags & 0x200) != 0 ? 4 : 0) + ((flags & 0x400) != 0 ? 4 : 0) + ((flags & 0x800) != 0 ? 4 : 0);
                                for (uint i = 0; i < count; i++)
                                {
                                    total += (flags & 0x100) != 0 ? ReadU32(chunk, f) : fallback;
                                    f += each;
                                }
                            }
                        }
                    }
            return total * movieScale / trackScale;
        }

        private static void Put(Stream s, byte[] b) { s.Write(b, 0, b.Length); }
        private static uint ReadU32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        private static ulong ReadU64(byte[] b, int o) { return (ulong)ReadU32(b, o) << 32 | ReadU32(b, o + 4); }
        private static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static void WriteU64(byte[] b, int o, ulong v) { WriteU32(b, o, (uint)(v >> 32)); WriteU32(b, o + 4, (uint)v); }
    }
}
