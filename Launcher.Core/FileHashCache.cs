using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Remembers a file's hash between launcher runs, keyed by its relative path together with
    /// size and exact write time — the same technique Indexer's own HashCache already uses on
    /// the build side (see Indexer/HashCache.cs) to avoid rehashing gigabytes for a couple of
    /// changed mods. The launcher re-hashes its entire client folder (and, separately, the
    /// player's Steam install for the game-file check) on every single run, including ones
    /// where nothing changed at all — that repeated full read-and-hash is the actual cost,
    /// not the number of files touched by a cheap size/mtime lookup.
    ///
    /// Not a security boundary, deliberately: a player who reproduces both the size and the
    /// exact timestamp of a previously-verified file could poison their own cache and skip
    /// re-verifying a tampered copy — but that only fools their own launcher, not the server,
    /// which verifies mods and behavior independently. This cache exists purely to skip
    /// redundant disk reads and CPU time on the common case where nothing changed, backed by
    /// the existing FullCheck path (see FileDownloader) as the real safety net.
    ///
    /// Two independent instances exist in FileDownloader — one for the client folder, one for
    /// the Steam install — both persisted as plain files inside the client folder (never inside
    /// the Steam library, which isn't ours to write into). Neither root is baked into the key:
    /// the caller always passes a path relative to whichever root it's checking.
    /// </summary>
    public sealed class FileHashCache
    {
        private const string Marker = "HASHCACHE";
        private const int Version = 1;

        private readonly string _cacheFile;
        private readonly Dictionary<string, Entry> _known;
        private readonly ConcurrentDictionary<string, Entry> _fresh = new(StringComparer.OrdinalIgnoreCase);

        private readonly record struct Entry(long Ticks, long Size, string Hash);

        private int _hits;
        private int _misses;

        /// <summary>Files whose hash was reused from the cache this run.</summary>
        public int Hits => Volatile.Read(ref _hits);

        /// <summary>Files that had to be read and hashed for real this run (changed, or never seen before).</summary>
        public int Misses => Volatile.Read(ref _misses);

        private FileHashCache(string cacheFile, Dictionary<string, Entry> known)
        {
            _cacheFile = cacheFile;
            _known = known;
        }

        public static FileHashCache Load(string cacheFilePath)
        {
            var known = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(cacheFilePath))
                {
                    using var reader = new StreamReader(cacheFilePath, Encoding.UTF8);

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
            catch
            {
                // The cache is a speedup, not a source of truth. Any trouble reading it just
                // means everything gets rehashed this run — not a reason to fail the update.
                known.Clear();
            }

            return new FileHashCache(cacheFilePath, known);
        }

        /// <summary>
        /// The file's hash — from the cache if its size and exact write time still match what
        /// was recorded last time, freshly computed (and remembered for next time) otherwise.
        /// Assumes the file exists; callers already handle the not-found case themselves
        /// (e.g. FileDownloader.CurrentHashOf's stashed-file fallback).
        /// </summary>
        public string GetHash(string relativePath, string fullPath)
        {
            var info = new FileInfo(fullPath);

            if (_known.TryGetValue(relativePath, out Entry entry)
                && entry.Size == info.Length
                && entry.Ticks == info.LastWriteTimeUtc.Ticks)
            {
                _fresh[relativePath] = entry;
                Interlocked.Increment(ref _hits);
                return entry.Hash;
            }

            string hash = FileHash.OfFile(fullPath);
            _fresh[relativePath] = new Entry(info.LastWriteTimeUtc.Ticks, info.Length, hash);
            Interlocked.Increment(ref _misses);
            return hash;
        }

        /// <summary>Writes only what was seen THIS run — a file no longer checked naturally drops out, no separate cleanup.</summary>
        public void Save()
        {
            try
            {
                using var writer = new StreamWriter(_cacheFile, false, new UTF8Encoding(false));
                writer.Write($"{Marker} {Version} {FileHash.AlgorithmName}\n");

                foreach (KeyValuePair<string, Entry> pair in _fresh)
                {
                    Entry e = pair.Value;
                    writer.Write($"{e.Ticks} {e.Size} {e.Hash} {pair.Key}\n");
                }
            }
            catch
            {
                // Couldn't save — we'll just miss the cache next run, not fatal.
            }
        }
    }
}
