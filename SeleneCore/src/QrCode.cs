using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Selene
{
    // A QR code for a short link (byte mode, error correction M, versions 1-10), drawn as a WPF picture
    public class QrCode
    {
        // per version: error correction codewords per block, then (blocks, data codewords) for each block group
        private static readonly int[][] Blocks =
        {
            null,
            new[] { 10, 1, 16 }, new[] { 16, 1, 28 }, new[] { 26, 1, 44 }, new[] { 18, 2, 32 }, new[] { 24, 2, 43 },
            new[] { 16, 4, 27 }, new[] { 18, 4, 31 }, new[] { 22, 2, 38, 2, 39 }, new[] { 22, 3, 36, 2, 37 }, new[] { 26, 4, 43, 1, 44 },
        };
        private static readonly int[][] Align =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 },
            new[] { 6, 34 }, new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };

        public readonly int Size;
        private readonly bool[,] dark, function;   // [y, x]

        public bool this[int x, int y] { get { return dark[y, x]; } }

        public QrCode(string text) : this(text, -1) { }

        // mask -1 picks the best of the eight
        public QrCode(string text, int mask)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            int version = 1;
            for (; version <= 10; version++)
                if (4 + (version < 10 ? 8 : 16) + bytes.Length * 8 <= DataCodewords(version) * 8) break;
            if (version > 10) throw new ArgumentException("too long for a QR code here");
            Size = version * 4 + 17;
            dark = new bool[Size, Size];
            function = new bool[Size, Size];
            DrawFunctionPatterns(version);
            PlaceData(Codewords(bytes, version));
            if (mask < 0)
            {
                int best = int.MaxValue;
                for (int m = 0; m < 8; m++)
                {
                    ApplyMask(m);
                    DrawFormat(m);
                    int p = Penalty();
                    if (p < best) { best = p; mask = m; }
                    ApplyMask(m);
                }
            }
            ApplyMask(mask);
            DrawFormat(mask);
        }

        private static int DataCodewords(int version)
        {
            var b = Blocks[version];
            int n = b[1] * b[2];
            if (b.Length > 3) n += b[3] * b[4];
            return n;
        }

        // ---- the data: mode, length, bytes, padding, split into blocks with their error correction, interleaved ----
        private static byte[] Codewords(byte[] bytes, int version)
        {
            var bits = new List<bool>();
            Action<int, int> put = (value, count) => { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); };
            put(4, 4);
            put(bytes.Length, version < 10 ? 8 : 16);
            foreach (var b in bytes) put(b, 8);
            int capacity = DataCodewords(version) * 8;
            put(0, Math.Min(4, capacity - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) put(pad, 8);
            var data = new byte[capacity / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i >> 3] |= (byte)(0x80 >> (i & 7));

            var spec = Blocks[version];
            int ecLen = spec[0];
            var divisor = Divisor(ecLen);
            var dataBlocks = new List<byte[]>();
            var ecBlocks = new List<byte[]>();
            int at = 0;
            for (int g = 1; g < spec.Length; g += 2)
                for (int k = 0; k < spec[g]; k++)
                {
                    var block = new byte[spec[g + 1]];
                    Array.Copy(data, at, block, 0, block.Length);
                    at += block.Length;
                    dataBlocks.Add(block);
                    ecBlocks.Add(Remainder(block, divisor));
                }
            var result = new List<byte>();
            int longest = 0;
            foreach (var b in dataBlocks) longest = Math.Max(longest, b.Length);
            for (int i = 0; i < longest; i++) foreach (var b in dataBlocks) if (i < b.Length) result.Add(b[i]);
            for (int i = 0; i < ecLen; i++) foreach (var b in ecBlocks) result.Add(b[i]);
            return result.ToArray();
        }

        private static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        private static byte[] Divisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)Mul(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Mul(root, 2);
            }
            return result;
        }

        private static byte[] Remainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (var b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Mul(divisor[i], factor);
            }
            return result;
        }

        // ---- the fixed parts ----
        private void Set(int x, int y, bool on)
        {
            dark[y, x] = on;
            function[y, x] = true;
        }

        private void DrawFunctionPatterns(int version)
        {
            for (int i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
            Finder(3, 3); Finder(Size - 4, 3); Finder(3, Size - 4);
            var pos = Align[version];
            for (int i = 0; i < pos.Length; i++)
                for (int j = 0; j < pos.Length; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormat(0);   // reserves its places; drawn for real once the mask is chosen
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = version << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool on = ((bits >> i) & 1) != 0;
                    int a = Size - 11 + i % 3, b = i / 3;
                    Set(a, b, on);
                    Set(b, a, on);
                }
            }
        }

        private void Finder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, d != 2 && d != 4);
                }
        }

        private void DrawFormat(int mask)
        {
            int data = 0 << 3 | mask;   // error correction M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(8, i, bit(i));
            Set(8, 7, bit(6));
            Set(8, 8, bit(7));
            Set(7, 8, bit(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, bit(i));
            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, bit(i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, bit(i));
            Set(8, Size - 8, true);
        }

        private void PlaceData(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (function[y, x] || i >= data.Length * 8) continue;
                        dark[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (function[y, x]) continue;
                    bool flip;
                    switch (mask)
                    {
                        case 0: flip = (x + y) % 2 == 0; break;
                        case 1: flip = y % 2 == 0; break;
                        case 2: flip = x % 3 == 0; break;
                        case 3: flip = (x + y) % 3 == 0; break;
                        case 4: flip = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: flip = x * y % 2 + x * y % 3 == 0; break;
                        case 6: flip = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: flip = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (flip) dark[y, x] = !dark[y, x];
                }
        }

        // ---- the standard's score for a mask (lower is easier to read) ----
        private int Penalty()
        {
            int score = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    var line = new bool[Size];
                    for (int b = 0; b < Size; b++) line[b] = pass == 0 ? dark[a, b] : dark[b, a];
                    int run = 1;
                    for (int b = 1; b <= Size; b++)
                    {
                        if (b < Size && line[b] == line[b - 1]) { run++; continue; }
                        if (run >= 5) score += run - 2;
                        run = 1;
                    }
                    for (int b = 0; b + 7 <= Size; b++)
                    {
                        if (!(line[b] && !line[b + 1] && line[b + 2] && line[b + 3] && line[b + 4] && !line[b + 5] && line[b + 6])) continue;
                        bool before = true, after = true;
                        for (int k = 1; k <= 4; k++)
                        {
                            if (b - k >= 0 && line[b - k]) before = false;
                            if (b + 6 + k < Size && line[b + 6 + k]) after = false;
                        }
                        if (before || after) score += 40;
                    }
                }
            int total = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (dark[y, x]) total++;
                    if (x + 1 < Size && y + 1 < Size && dark[y, x] == dark[y, x + 1] && dark[y, x] == dark[y + 1, x] && dark[y, x] == dark[y + 1, x + 1]) score += 3;
                }
            score += Math.Abs(total * 20 - Size * Size * 10) / (Size * Size) * 10;
            return score;
        }

        // dark squares on white with the four-module quiet zone, sharp at any size
        public ImageSource Picture()
        {
            var g = new GeometryGroup();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (dark[y, x]) g.Children.Add(new RectangleGeometry(new Rect(x + 4, y + 4, 1, 1)));
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, Size + 8, Size + 8))));
            drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, g));
            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
    }
}
