using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Indexer
{
    /// <summary>
    /// Remembers checksums between runs so 2.7 GB doesn't get recomputed for the sake
    /// of a couple of replaced mods.
    ///
    /// An entry is considered valid if the file's size and modification time both match.
    /// This isn't an absolute guarantee: a file could be swapped while preserving both
    /// attributes. In practice that doesn't happen — the build is assembled by copying,
    /// and a copy brings a new timestamp with it — but since there's no hard guarantee,
    /// there's --no-cache.
    ///
    /// Paths inside are relative. The cache lives right in the build, so the root is known
    /// from its own location, and an absolute path on every line would be not just
    /// redundant but harmful: it would tie the file to one machine (paths differ on the
    /// server — nothing would match) and leak the directory layout along with the username.
    /// </summary>
    internal sealed class HashCache
    {
        private const string Marker = "HASHCACHE";
        private const int Version = 2;

        private readonly string _root;
        private readonly string _cacheFile;
        private readonly Dictionary<string, Entry> _known;
        private readonly ConcurrentDictionary<string, Entry> _fresh = new(StringComparer.OrdinalIgnoreCase);

        private readonly record struct Entry(long Ticks, long Size, string Hash);

        private int _hits;

        /// <summary>Incremented from multiple threads at once — hence Interlocked.</summary>
        public int Hits => Volatile.Read(ref _hits);

        private HashCache(string root, string cacheFile, Dictionary<string, Entry> known)
        {
            _root = root;
            _cacheFile = cacheFile;
            _known = known;
        }

        public static HashCache Load(string packFolder)
        {
            string root = Path.GetFullPath(packFolder).TrimEnd(Path.DirectorySeparatorChar);
            string cacheFile = Path.Combine(root, "hashes.cache");
            var known = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(cacheFile))
                {
                    using var reader = new StreamReader(cacheFile, Encoding.UTF8);

                    string header = reader.ReadLine();
                    if (header is not null && header.StartsWith($"{Marker} {Version} ", StringComparison.Ordinal))
                    {
                        for (string line = reader.ReadLine(); line is not null; line = reader.ReadLine())
                        {
                            // ticks size hash path — path comes last, it may contain spaces
                            string[] parts = line.Split(' ', 4);
                            if (parts.Length != 4) continue;

                            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) continue;
                            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size)) continue;

                            known[parts[3]] = new Entry(ticks, size, parts[2]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // The cache is a speedup, not a source of truth. Any trouble with it just
                // means an extra recompute, not a reason to skip building the manifest.
                Console.WriteLine($"Hash cache unreadable, recomputing everything: {ex.Message}");
                known.Clear();
            }

            return new HashCache(root, cacheFile, known);
        }

        /// <summary>Hash from the cache, if the file hasn't changed since last time.</summary>
        public bool TryGet(string fullPath, FileInfo info, out string hash)
        {
            string key = Relative(fullPath);

            if (_known.TryGetValue(key, out Entry entry)
                && entry.Size == info.Length
                && entry.Ticks == info.LastWriteTimeUtc.Ticks)
            {
                hash = entry.Hash;
                _fresh[key] = entry;
                Interlocked.Increment(ref _hits);
                return true;
            }

            hash = null;
            return false;
        }

        public void Store(string fullPath, FileInfo info, string hash) =>
            _fresh[Relative(fullPath)] = new Entry(info.LastWriteTimeUtc.Ticks, info.Length, hash);

        /// <summary>
        /// Writes only what was seen in this run: files removed from the build
        /// naturally drop out of the cache, no separate cleanup needed.
        /// </summary>
        public void Save()
        {
            try
            {
                using var writer = new StreamWriter(_cacheFile, false, new UTF8Encoding(false));
                writer.Write($"{Marker} {Version} {Odinsons.ValheimLauncher.FileHash.AlgorithmName}\n");

                foreach (KeyValuePair<string, Entry> pair in _fresh)
                {
                    Entry e = pair.Value;
                    writer.Write($"{e.Ticks} {e.Size} {e.Hash} {pair.Key}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not save hash cache: {ex.Message}");
            }
        }

        /// <summary>Path relative to the build root, forward-slash separated.</summary>
        private string Relative(string fullPath)
        {
            string full = Path.GetFullPath(fullPath);

            if (full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                full = full.Substring(_root.Length);

            return full.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       .Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
