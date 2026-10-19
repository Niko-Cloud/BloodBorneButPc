using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// zsync, the AppImage update format: a release's file made from the parts of the old one it
    /// shares, downloading only the rest. The release's .zsync lists every block of the new file (a
    /// weak rolling checksum and the start of its MD4); the old file is searched at every byte
    /// offset for them, and the blocks it does not have come from the new file with HTTP range
    /// requests. The result is checked against the SHA-1 the .zsync gives.
    /// </summary>
    public static class Zsync
    {
        /// <summary>A .zsync file: the new file's length, SHA-1 and block checksums.</summary>
        public sealed class Control
        {
            public int BlockSize { get; init; }
            public long Length { get; init; }
            /// <summary>Blocks that have to match one after another (1 or 2): two let the weak
            /// checksum be shorter.</summary>
            public int SeqMatches { get; init; }
            public int RsumBytes { get; init; }
            public int ChecksumBytes { get; init; }
            public byte[] Sha1 { get; init; } = Array.Empty<byte>();
            public uint[] Rsums { get; init; } = Array.Empty<uint>();
            /// <summary>ChecksumBytes of each block's MD4, block after block.</summary>
            public byte[] Checksums { get; init; } = Array.Empty<byte>();
            public int Blocks => (int)((Length + BlockSize - 1) / BlockSize);
        }

        /// <summary>Requests at most: nearby missing parts are fetched together, gaps included.</summary>
        private const int MaxRanges = 64;
        private const int Parallel = 4;

        public static Control Parse(byte[] data)
        {
            var end = data.AsSpan().IndexOf("\n\n"u8);
            if (end < 0) throw new InvalidDataException("The .zsync file has no header.");
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in Encoding.ASCII.GetString(data, 0, end).Split('\n'))
                if (line.IndexOf(':') is var colon and > 0)
                    header[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            string Field(string name) => header.TryGetValue(name, out var v) ? v : throw new InvalidDataException($"The .zsync file has no {name}.");

            var lengths = Field("Hash-Lengths").Split(',').Select(int.Parse).ToArray();
            var blockSize = int.Parse(Field("Blocksize"));
            var length = long.Parse(Field("Length"));
            var sha1 = Convert.FromHexString(Field("SHA-1"));
            if (lengths.Length != 3 || blockSize < 512 || length < 0 || lengths[0] is < 1 or > 2 ||
                lengths[1] is < 1 or > 4 || lengths[2] is < 3 or > 16 || sha1.Length != 20)
                throw new InvalidDataException("The .zsync file's header is not one this launcher reads.");
            int rsumBytes = lengths[1], checksumBytes = lengths[2];

            var blocks = (int)((length + blockSize - 1) / blockSize);
            var entry = rsumBytes + checksumBytes;
            if (data.Length - (end + 2) < (long)blocks * entry)
                throw new InvalidDataException("The .zsync file is cut short.");
            // Each block: the last rsumBytes of its rolling checksum (a, b: 16 bits each, big-endian),
            // then the first checksumBytes of its MD4.
            var rsums = new uint[blocks];
            var checksums = new byte[blocks * checksumBytes];
            var at = end + 2;
            for (var i = 0; i < blocks; i++, at += entry)
            {
                uint r = 0;
                for (var k = 0; k < rsumBytes; k++)
                    r = r << 8 | data[at + k];
                rsums[i] = r;
                Array.Copy(data, at + rsumBytes, checksums, i * checksumBytes, checksumBytes);
            }
            return new Control
            {
                BlockSize = blockSize, Length = length, SeqMatches = lengths[0], RsumBytes = rsumBytes,
                ChecksumBytes = checksumBytes, Sha1 = sha1, Rsums = rsums, Checksums = checksums,
            };
        }

        /// <summary>
        /// Makes <paramref name="output"/> from <paramref name="oldFile"/> and the parts of
        /// <paramref name="url"/> it lacks. False when that would not save much (most of the file
        /// changed) or the result is not the file the .zsync describes; the caller downloads it whole.
        /// </summary>
        public static async Task<bool> BuildAsync(HttpClient http, Control control, string url, string oldFile, string output,
            IProgress<double>? progress = null)
        {
            var old = await File.ReadAllBytesAsync(oldFile);
            var source = Find(control, old);

            var bs = control.BlockSize;
            var ranges = new List<(long Start, long End)>(); // end exclusive
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] >= 0) continue;
                long start = (long)i * bs, stop = Math.Min(start + bs, control.Length);
                if (ranges.Count > 0 && ranges[^1].End == start) ranges[^1] = (ranges[^1].Start, stop);
                else ranges.Add((start, stop));
            }
            ranges = Merge(ranges, MaxRanges);
            var needed = ranges.Sum(r => r.End - r.Start);
            if (needed > control.Length * 7 / 10)
                return false;

            using (var handle = File.OpenHandle(output, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.None, control.Length))
            {
                for (var i = 0; i < source.Length; i++)
                    if (source[i] >= 0)
                    {
                        var length = (int)Math.Min(bs, control.Length - (long)i * bs);
                        RandomAccess.Write(handle, old.AsSpan((int)source[i], length), (long)i * bs);
                    }

                long done = 0;
                progress?.Report(0);
                using var gate = new SemaphoreSlim(Parallel);
                await Task.WhenAll(ranges.Select(async range =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        // A dropped connection costs only its part: tried again twice.
                        for (var attempt = 1; ; attempt++)
                        {
                            long counted = 0;
                            try
                            {
                                await FetchAsync(http, url, range.Start, range.End, handle, read =>
                                {
                                    counted += read;
                                    var total = Interlocked.Add(ref done, read);
                                    if (needed > 0) progress?.Report((double)total / needed);
                                });
                                break;
                            }
                            catch (Exception e) when (attempt < 3 && e is HttpRequestException or IOException)
                            {
                                Interlocked.Add(ref done, -counted);
                                await Task.Delay(1000 * attempt);
                            }
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }
            return Matches(output, control);
        }

        /// <summary>The file is the one the .zsync describes: its length and SHA-1.</summary>
        public static bool Matches(string file, Control control)
        {
            using var stream = File.OpenRead(file);
            return stream.Length == control.Length && SHA1.HashData(stream).AsSpan().SequenceEqual(control.Sha1);
        }

        /// <summary>For each block of the new file, where the old file has it, or -1.</summary>
        private static long[] Find(Control c, byte[] old)
        {
            var blocks = c.Blocks;
            var source = new long[blocks];
            Array.Fill(source, -1L);
            var bs = c.BlockSize;
            var seq = c.SeqMatches;
            var span = (long)bs * seq;
            if (blocks < seq || old.Length < span) return source;

            // A key per block (with its successor's when two must match), in a hash table chained
            // through next[].
            var mask = c.RsumBytes == 4 ? uint.MaxValue : (1u << (8 * c.RsumBytes)) - 1;
            var indexed = blocks - (seq - 1);
            var bits = Math.Max(16, (int)Math.Ceiling(Math.Log2(indexed * 4.0)));
            var head = new int[1 << bits];
            Array.Fill(head, -1);
            var next = new int[indexed];
            var keys = new ulong[indexed];
            for (var i = indexed - 1; i >= 0; i--)
            {
                keys[i] = seq == 2 ? (ulong)c.Rsums[i] << 32 | c.Rsums[i + 1] : c.Rsums[i];
                var h = Bucket(keys[i], bits);
                next[i] = head[h];
                head[h] = i;
            }

            // Rolling checksums of the window at p and of the one after it.
            Sum(old, 0, bs, out var a0, out var b0);
            ushort a1 = 0, b1 = 0;
            if (seq == 2) Sum(old, bs, bs, out a1, out b1);
            var digest = new byte[16];
            var digestNext = new byte[16];
            long p = 0;
            while (true)
            {
                var key = seq == 2
                    ? (ulong)(((uint)a0 << 16 | b0) & mask) << 32 | (((uint)a1 << 16 | b1) & mask)
                    : ((uint)a0 << 16 | b0) & mask;
                var matched = false;
                bool hashed = false, hashedNext = false;
                for (var i = head[Bucket(key, bits)]; i >= 0; i = next[i])
                {
                    if (keys[i] != key || (source[i] >= 0 && (seq == 1 || source[i + 1] >= 0))) continue;
                    if (!hashed) { Md4.Hash(old.AsSpan((int)p, bs), digest); hashed = true; }
                    if (!Same(c, i, digest)) continue;
                    if (seq == 2)
                    {
                        if (!hashedNext) { Md4.Hash(old.AsSpan((int)(p + bs), bs), digestNext); hashedNext = true; }
                        if (!Same(c, i + 1, digestNext)) continue;
                        source[i + 1] = p + bs;
                    }
                    source[i] = p;
                    matched = true;
                }

                if (matched)
                {
                    // On to the next block: the window after this one becomes the first.
                    p += bs;
                    if (p + span > old.Length) break;
                    if (seq == 2)
                    {
                        (a0, b0) = (a1, b1);
                        Sum(old, (int)(p + bs), bs, out a1, out b1);
                    }
                    else
                    {
                        Sum(old, (int)p, bs, out a0, out b0);
                    }
                    continue;
                }

                if (p + span >= old.Length) break;
                // One byte on: zsync's rolling sum (a: the bytes' sum, b: each weighted by its
                // distance from the window's end), both 16 bits.
                byte out0 = old[p], in0 = old[p + bs];
                a0 = (ushort)(a0 - out0 + in0);
                b0 = (ushort)(b0 + a0 - (out0 * bs));
                if (seq == 2)
                {
                    byte in1 = old[p + 2 * bs];
                    a1 = (ushort)(a1 - in0 + in1);
                    b1 = (ushort)(b1 + a1 - (in0 * bs));
                }
                p++;
            }
            return source;
        }

        private static int Bucket(ulong key, int bits) => (int)((key * 0x9E3779B97F4A7C15UL) >> (64 - bits));

        private static bool Same(Control c, int block, byte[] digest) =>
            digest.AsSpan(0, c.ChecksumBytes).SequenceEqual(c.Checksums.AsSpan(block * c.ChecksumBytes, c.ChecksumBytes));

        private static void Sum(byte[] data, int start, int length, out ushort a, out ushort b)
        {
            uint sa = 0, sb = 0;
            for (var k = 0; k < length; k++)
            {
                sa += data[start + k];
                sb += (uint)(length - k) * data[start + k];
            }
            a = (ushort)sa;
            b = (ushort)sb;
        }

        /// <summary>At most <paramref name="max"/> ranges: the smallest gaps between them are filled.</summary>
        private static List<(long Start, long End)> Merge(List<(long Start, long End)> ranges, int max)
        {
            if (ranges.Count <= max) return ranges;
            // Gap i lies between range i and i + 1; the smallest ones are closed.
            var closed = new bool[ranges.Count];
            foreach (var i in Enumerable.Range(0, ranges.Count - 1)
                         .OrderBy(i => ranges[i + 1].Start - ranges[i].End).Take(ranges.Count - max))
                closed[i] = true;
            var merged = new List<(long Start, long End)> { ranges[0] };
            for (var i = 1; i < ranges.Count; i++)
            {
                if (closed[i - 1]) merged[^1] = (merged[^1].Start, ranges[i].End);
                else merged.Add(ranges[i]);
            }
            return merged;
        }

        private static async Task FetchAsync(HttpClient http, string url, long start, long end, Microsoft.Win32.SafeHandles.SafeFileHandle handle,
            Action<int> read)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end - 1);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            // A server that ignores the range sends the whole file: not this method's to take.
            if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start)
                throw new InvalidDataException("The server does not send parts of the file.");
            await using var input = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[81920];
            var at = start;
            while (at < end)
            {
                var n = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, end - at)));
                if (n == 0) throw new IOException("The download ended early.");
                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, n), at);
                at += n;
                read(n);
            }
        }

        /// <summary>MD4 (RFC 1320), zsync's block checksum. .NET has none.</summary>
        private static class Md4
        {
            public static void Hash(ReadOnlySpan<byte> data, Span<byte> digest)
            {
                uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
                Span<uint> x = stackalloc uint[16];
                Span<byte> tail = stackalloc byte[128];
                var full = data.Length / 64 * 64;
                for (var i = 0; i < full; i += 64)
                    Block(data.Slice(i, 64), x, ref a, ref b, ref c, ref d);
                var rest = data.Length - full;
                tail.Clear();
                data[full..].CopyTo(tail);
                tail[rest] = 0x80;
                var padded = rest < 56 ? 64 : 128;
                BinaryPrimitives.WriteUInt64LittleEndian(tail.Slice(padded - 8), (ulong)data.Length * 8);
                for (var i = 0; i < padded; i += 64)
                    Block(tail.Slice(i, 64), x, ref a, ref b, ref c, ref d);
                BinaryPrimitives.WriteUInt32LittleEndian(digest, a);
                BinaryPrimitives.WriteUInt32LittleEndian(digest[4..], b);
                BinaryPrimitives.WriteUInt32LittleEndian(digest[8..], c);
                BinaryPrimitives.WriteUInt32LittleEndian(digest[12..], d);
            }

            private static uint L(uint v, int s) => v << s | v >> (32 - s);

            private static readonly int[] Round1 = { 0, 4, 8, 12 }, Round2 = { 0, 1, 2, 3 }, Round3 = { 0, 2, 1, 3 };

            private static void Block(ReadOnlySpan<byte> block, Span<uint> x, ref uint a, ref uint b, ref uint c, ref uint d)
            {
                for (var i = 0; i < 16; i++)
                    x[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(i * 4));
                uint aa = a, bb = b, cc = c, dd = d;
                foreach (var k in Round1)
                {
                    a = L(a + ((b & c) | (~b & d)) + x[k], 3);
                    d = L(d + ((a & b) | (~a & c)) + x[k + 1], 7);
                    c = L(c + ((d & a) | (~d & b)) + x[k + 2], 11);
                    b = L(b + ((c & d) | (~c & a)) + x[k + 3], 19);
                }
                foreach (var k in Round2)
                {
                    a = L(a + ((b & c) | (b & d) | (c & d)) + x[k] + 0x5a827999, 3);
                    d = L(d + ((a & b) | (a & c) | (b & c)) + x[k + 4] + 0x5a827999, 5);
                    c = L(c + ((d & a) | (d & b) | (a & b)) + x[k + 8] + 0x5a827999, 9);
                    b = L(b + ((c & d) | (c & a) | (d & a)) + x[k + 12] + 0x5a827999, 13);
                }
                foreach (var k in Round3)
                {
                    a = L(a + (b ^ c ^ d) + x[k] + 0x6ed9eba1, 3);
                    d = L(d + (a ^ b ^ c) + x[k + 8] + 0x6ed9eba1, 9);
                    c = L(c + (d ^ a ^ b) + x[k + 4] + 0x6ed9eba1, 11);
                    b = L(b + (c ^ d ^ a) + x[k + 12] + 0x6ed9eba1, 15);
                }
                a += aa; b += bb; c += cc; d += dd;
            }
        }
    }
}
