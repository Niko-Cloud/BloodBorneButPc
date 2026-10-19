using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using LibOrbisPkg.PFS;
using LibOrbisPkg.PKG;

namespace BloodborneLauncher.Core
{
    /// <summary>A PKG as its header and param.sfo describe it.</summary>
    public sealed record PkgInfo(string Path, string TitleId, string Version, string Category, long Size)
    {
        /// <summary>param.sfo CATEGORY: "gd" is a game, "gp" its update.</summary>
        public bool IsGame => Category == "gd";
        public bool IsUpdate => Category == "gp";

        public string Kind => Category switch
        {
            "gd" => "a game",
            "gp" => "an update",
            "ac" => "add-on content",
            "" => "an unknown package",
            _ => $"a package of category {Category}",
        };
    }

    /// <summary>How far a dump is: Done of Total (bytes while extracting, files while checking).</summary>
    public sealed record DumpProgress(string Step, long Done, long Total);

    /// <summary>
    /// A game folder made from the player's own fake-signed PKGs, as bb-dump/dump.sh does: the
    /// base game's files, its sce_sys entries (param.sfo, icons, trophies), then the update's over
    /// them. It is written to &lt;target&gt;.part, every .dcx archive in it is decompressed once to
    /// prove it whole, and only then is it renamed to the target.
    /// </summary>
    public static class GameDump
    {
        /// <summary>Reads a PKG's header and param.sfo (no game data). Throws on a file that is not a PKG.</summary>
        public static PkgInfo Inspect(string path)
        {
            using var file = File.OpenRead(path);
            var pkg = new PkgReader(file).ReadPkg();
            var sfo = pkg.ParamSfo?.ParamSfo;
            string Value(string key) => sfo?[key]?.ToString()?.TrimEnd('\0') ?? "";
            return new PkgInfo(path, Value("TITLE_ID"), Value("APP_VER"), Value("CATEGORY"), file.Length);
        }

        /// <summary>Why these two packages cannot make a game folder the port runs, or null.</summary>
        public static string? Problem(PkgInfo? game, PkgInfo? update)
        {
            if (game == null)
                return "Choose the game package";
            if (!game.IsGame)
                return $"{System.IO.Path.GetFileName(game.Path)} is {game.Kind}, not the base game";
            if (!GameCheck.SupportedTitles.Contains(game.TitleId))
                return $"The game package is {game.TitleId}, not a retail Bloodborne";
            if (update == null)
                return "Choose the 1.09 update package";
            if (!update.IsUpdate)
                return $"{System.IO.Path.GetFileName(update.Path)} is {update.Kind}, not an update";
            if (update.TitleId != game.TitleId)
                return $"The update is for {update.TitleId}, the game is {game.TitleId}: both must be the same region";
            if (update.Version != GameCheck.SupportedVersion)
                return $"The update is version {update.Version}: update {GameCheck.SupportedVersion} is needed";
            return null;
        }

        /// <summary>The folder the dump becomes inside the chosen one: &lt;folder&gt;/&lt;title id&gt;.</summary>
        public static string Target(string folder, PkgInfo game) => System.IO.Path.Combine(folder, game.TitleId);

        /// <summary>
        /// Makes the dump; blocks, so call it off the UI thread. Throws with a message for the player
        /// when a package cannot be read, the disk is too small or an archive comes out damaged.
        /// </summary>
        public static void Make(PkgInfo game, PkgInfo update, string target, IProgress<DumpProgress> progress, CancellationToken cancel)
        {
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                throw new IOException($"{target} already exists and is not empty: choose another folder or remove it first");

            var part = target + ".part";
            if (Directory.Exists(part))
                Directory.Delete(part, true); // a dump stopped half way
            Directory.CreateDirectory(part);
            try
            {
                using (var gameImage = new PkgImage(game.Path))
                using (var updateImage = new PkgImage(update.Path))
                {
                    var total = gameImage.Bytes + updateImage.Bytes;
                    var free = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(part))!).AvailableFreeSpace;
                    if (free < total + (1L << 30))
                        throw new IOException($"Not enough free space: the dump needs about {Gb(total)} and the disk has {Gb(free)}");

                    var meter = new Meter(progress, total);
                    meter.Step = "Extracting the game";
                    gameImage.ExtractTo(part, meter, cancel);
                    meter.Step = "Extracting the 1.09 update";
                    updateImage.ExtractTo(part, meter, cancel);
                }

                var damaged = CheckArchives(part, progress, cancel);
                if (damaged.Count > 0)
                    throw new InvalidDataException(
                        $"{damaged.Count} game archives came out damaged (first: {System.IO.Path.GetRelativePath(part, damaged[0])}). " +
                        "The package files may be incomplete: download or copy them again");

                if (Directory.Exists(target))
                    Directory.Delete(target); // empty, checked above
                Directory.Move(part, target);
            }
            catch
            {
                try { Directory.Delete(part, true); } catch (Exception) { }
                throw;
            }
        }

        public static string Gb(long bytes) => $"{bytes / 1e9:0.0} GB";

        /// <summary>
        /// The .dcx archives (FromSoftware's zlib containers) that do not decompress to the size
        /// their header gives: a block the extraction lost. Others (not DFLT) are passed over.
        /// </summary>
        private static List<string> CheckArchives(string root, IProgress<DumpProgress> progress, CancellationToken cancel)
        {
            var files = Directory.EnumerateFiles(root, "*.dcx", SearchOption.AllDirectories).ToList();
            var damaged = new System.Collections.Concurrent.ConcurrentBag<string>();
            var meter = new Meter(progress, files.Count) { Step = "Checking the game archives" };
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancel }, path =>
            {
                if (!DcxWhole(path)) damaged.Add(path);
                meter.Add(1);
            });
            return damaged.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        private static bool DcxWhole(string path)
        {
            try
            {
                using var file = File.OpenRead(path);
                var header = new byte[0x4C];
                if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length) return true;
                if (!header.AsSpan(0, 4).SequenceEqual("DCX\0"u8) || !header.AsSpan(0x28, 4).SequenceEqual("DFLT"u8))
                    return true;
                var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x1C));
                using var zlib = new ZLibStream(file, CompressionMode.Decompress);
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
                    total += read;
                return total == size;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        /// <summary>Work done so far, added from any thread and reported a few times a second (and at the end).</summary>
        private sealed class Meter(IProgress<DumpProgress> progress, long total)
        {
            private long _done, _lastReport;
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            public string Step = "";

            public void Add(long amount)
            {
                var done = Interlocked.Add(ref _done, amount);
                var now = _clock.ElapsedMilliseconds;
                var last = Interlocked.Read(ref _lastReport);
                if ((now - last >= 200 || done == total) && Interlocked.CompareExchange(ref _lastReport, now, last) == last)
                    progress.Report(new DumpProgress(Step, done, total));
            }
        }

        /// <summary>A fake PKG opened for reading: its decrypted outer PFS and the inner (game) PFS in it.</summary>
        private sealed class PkgImage : IDisposable
        {
            private readonly MemoryMappedFile _file;
            private readonly MemoryMappedViewAccessor _view;
            private readonly Pkg _pkg;
            private readonly PfsReader _inner;
            private readonly List<PfsReader.File> _files;

            public long Bytes { get; }

            public PkgImage(string path)
            {
                var name = System.IO.Path.GetFileName(path);
                _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
                try
                {
                    using (var s = _file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
                        _pkg = new PkgReader(s).ReadPkg();
                    // Only a fake-signed (decrypted) PKG carries a key this can open; a console's
                    // store package is encrypted for that console.
                    var ekpfs = _pkg.GetEkpfs() ??
                        throw new InvalidDataException($"{name} is encrypted: only a decrypted (fake-signed) dump of your own copy can be used");
                    _view = _file.CreateViewAccessor((long)_pkg.Header.pfs_image_offset, (long)_pkg.Header.pfs_image_size, MemoryMappedFileAccess.Read);
                    try
                    {
                        var outer = new PfsReader(_view, _pkg.Header.pfs_flags, ekpfs);
                        _inner = new PfsReader(new PFSCReader(outer.GetFile("pfs_image.dat").GetView()));
                        _files = _inner.GetAllFiles().ToList();
                    }
                    catch (Exception e) when (e is not InvalidDataException)
                    {
                        throw new InvalidDataException($"{name} could not be opened ({e.Message}): the file may be incomplete");
                    }
                    Bytes = _files.Sum(f => f.size);
                }
                catch
                {
                    _view?.Dispose();
                    _file.Dispose();
                    throw;
                }
            }

            /// <summary>The files under uroot/ into root (replacing any there), then the sce_sys entries.</summary>
            public void ExtractTo(string root, Meter meter, CancellationToken cancel)
            {
                var full = System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar;
                Parallel.ForEach(_files, new ParallelOptions { CancellationToken = cancel }, () => new byte[1 << 20], (f, _, buffer) =>
                {
                    var dest = Destination(full, f.FullName);
                    if (dest == null) return buffer;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                    var view = f.GetView();
                    using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                    output.SetLength(f.size);
                    for (long pos = 0; pos < f.size;)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var count = (int)Math.Min(buffer.Length, f.size - pos);
                        view.Read(pos, buffer, 0, count);
                        output.Write(buffer, 0, count);
                        pos += count;
                        meter.Add(count);
                    }
                    return buffer;
                }, _ => { });

                // param.sfo and the rest live beside the PFS, as PKG entries; encrypted ones (keys,
                // licences) and the PKG's own tables (names starting with a dot) stay out.
                foreach (var meta in _pkg.Metas.Metas)
                {
                    if (meta.Encrypted || !EntryNames.IdToName.TryGetValue(meta.id, out var entry) || entry.StartsWith('.'))
                        continue;
                    var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(full, "sce_sys", entry));
                    if (!dest.StartsWith(full, StringComparison.Ordinal)) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                    using var input = _file.CreateViewStream(meta.DataOffset, meta.DataSize, MemoryMappedFileAccess.Read);
                    using var output = File.Create(dest);
                    input.CopyTo(output);
                    output.SetLength(meta.DataSize); // a view rounds up to the page
                }
            }

            /// <summary>Where a PFS path (/uroot/...) goes under root, or null outside uroot or outside root.</summary>
            private static string? Destination(string root, string pfsPath)
            {
                const string prefix = "/uroot/";
                if (!pfsPath.StartsWith(prefix, StringComparison.Ordinal)) return null;
                var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, pfsPath[prefix.Length..]));
                return dest.StartsWith(root, StringComparison.Ordinal) ? dest : null;
            }

            public void Dispose()
            {
                _view.Dispose();
                _file.Dispose();
            }
        }
    }
}
