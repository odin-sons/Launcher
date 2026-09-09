using System;
using System.Collections.Generic;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>One group's slice of the download, as seen at a moment in time.</summary>
    public sealed class DownloadGroupProgress
    {
        public string Key { get; init; } = string.Empty;
        public long BytesDone { get; init; }
        public long BytesTotal { get; init; }
        public int FilesDone { get; init; }
        public int FilesTotal { get; init; }

        public long BytesRemaining => Math.Max(0, BytesTotal - BytesDone);
        public bool Finished => FilesDone >= FilesTotal;
        public bool Started => BytesDone > 0 || FilesDone > 0;
    }

    /// <summary>
    /// Aggregates a parallel download into per-group progress for the install window's
    /// "N of M mods · X of Y MB" line and its short list of what's transferring right now.
    /// Thread-safe: <see cref="FileDownloader"/> hits it from every download worker.
    ///
    /// A group is "done" by file count, not bytes — the byte total is only an estimate
    /// (manifest size vs the server's actual Content-Length), so counting files is what
    /// keeps "31 / 78" honest.
    /// </summary>
    public sealed class DownloadProgressTracker
    {
        private sealed class GroupState
        {
            public long BytesDone;
            public long BytesTotal;
            public int FilesDone;
            public int FilesTotal;
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, GroupState> _groups = new();

        public DownloadProgressTracker(IEnumerable<(string key, long size)> files)
        {
            foreach ((string key, long size) in files)
            {
                if (!_groups.TryGetValue(key, out GroupState g))
                    _groups[key] = g = new GroupState();
                g.FilesTotal++;
                g.BytesTotal += Math.Max(0, size);
            }
        }

        public int GroupsTotal
        {
            get { lock (_gate) return _groups.Count; }
        }

        public long BytesTotal
        {
            get { lock (_gate) return _groups.Values.Sum(g => g.BytesTotal); }
        }

        public long BytesDone
        {
            get { lock (_gate) return _groups.Values.Sum(g => g.BytesDone); }
        }

        public int GroupsDone
        {
            get { lock (_gate) return _groups.Values.Count(g => g.FilesDone >= g.FilesTotal); }
        }

        public void AddBytes(string key, long delta)
        {
            lock (_gate)
            {
                if (_groups.TryGetValue(key, out GroupState g))
                    g.BytesDone = Math.Max(0, g.BytesDone + delta);
            }
        }

        public void CompleteFile(string key)
        {
            lock (_gate)
            {
                if (_groups.TryGetValue(key, out GroupState g) && g.FilesDone < g.FilesTotal)
                    g.FilesDone++;
            }
        }

        /// <summary>Groups that have started but aren't finished, biggest remaining first,
        /// capped at <paramref name="max"/> — the churning short list.</summary>
        public IReadOnlyList<DownloadGroupProgress> ActiveGroups(int max)
        {
            lock (_gate)
            {
                return Snapshot()
                    .Where(g => g.Started && !g.Finished)
                    .OrderByDescending(g => g.BytesRemaining)
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(max)
                    .ToList();
            }
        }

        public IReadOnlyList<DownloadGroupProgress> Snapshot()
        {
            lock (_gate)
            {
                return _groups
                    .Select(kv => new DownloadGroupProgress
                    {
                        Key = kv.Key,
                        BytesDone = kv.Value.BytesDone,
                        BytesTotal = kv.Value.BytesTotal,
                        FilesDone = kv.Value.FilesDone,
                        FilesTotal = kv.Value.FilesTotal,
                    })
                    .ToList();
            }
        }
    }
}
