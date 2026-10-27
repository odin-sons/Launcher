using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    /// <summary>The size limits of the files a profile shows, from docs/themes.md and docs/server-page.md.</summary>
    public static class DisplayFileLimits
    {
        public const int Theme = 16 * 1024;
        public const int About = 16 * 1024;
        public const int Terms = 20 * 1024;
        public const int Changelog = 64 * 1024;
    }

    public sealed class DisplayFileResult
    {
        public byte[] Bytes { get; init; }

        /// <summary>Why the file is not available, or null when it is.</summary>
        public string Problem { get; init; }

        public bool Ok => Bytes != null;
    }

    /// <summary>
    /// Verified display files kept by their digest. A stored file is hashed again when it is read, so
    /// the folder can be edited, truncated or swapped without a wrong file ever being used.
    /// </summary>
    public sealed class DisplayFileCache
    {
        private static readonly Regex DigestPattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        private readonly string _folder;

        public DisplayFileCache(string folder)
        {
            _folder = folder;
        }

        public byte[] Read(string digest, int maxBytes)
        {
            if (!DigestPattern.IsMatch(digest))
                return null;

            string path = Path.Combine(_folder, digest);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > maxBytes)
                    return null;

                byte[] bytes = File.ReadAllBytes(path);
                if (DisplayFiles.Digest(bytes) == digest)
                    return bytes;

                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return null;
        }

        public void Store(string digest, byte[] bytes)
        {
            if (!DigestPattern.IsMatch(digest))
                return;

            string path = Path.Combine(_folder, digest);
            string temp = path + ".part";
            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { File.Delete(temp); } catch (Exception) { }
            }
        }
    }

    /// <summary>
    /// Fetches the files a registry entry pins for display (theme, About, terms, changelog, pictures).
    /// A file is used only if the entry pins its path and its bytes hash to the pinned SHA-256; nothing
    /// unpinned is ever requested. Failures are reported in the result, never thrown.
    /// </summary>
    public sealed class DisplayFiles
    {
        private readonly RegistryEntry _entry;
        private readonly IDisplayFileFetcher _fetcher;
        private readonly DisplayFileCache _cache;

        public DisplayFiles(RegistryEntry entry, IDisplayFileFetcher fetcher = null, DisplayFileCache cache = null)
        {
            _entry = entry;
            _fetcher = fetcher ?? new HttpRegistryFetcher();
            _cache = cache;
        }

        public static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        public async Task<DisplayFileResult> LoadAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
        {
            if (path == null || _entry.Digests == null || !_entry.Digests.TryGetValue(path, out string digest))
                return Failed($"'{path}' is not pinned by the registry.");

            byte[] cached = _cache?.Read(digest, maxBytes);
            if (cached != null)
                return new DisplayFileResult { Bytes = cached };

            var problems = new List<string>();
            foreach (string mirror in _entry.Mirrors)
            {
                byte[] bytes;
                try
                {
                    bytes = await _fetcher.FetchAsync(new Uri(new Uri(mirror), path), maxBytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException
                    || ex is IOException || ex is RegistryException)
                {
                    problems.Add($"{mirror}: {ex.Message}");
                    continue;
                }

                if (bytes == null || bytes.Length > maxBytes)
                {
                    problems.Add($"{mirror}: larger than {maxBytes} bytes.");
                    continue;
                }

                if (Digest(bytes) != digest)
                {
                    problems.Add($"{mirror}: the file does not match its pinned digest.");
                    continue;
                }

                _cache?.Store(digest, bytes);
                return new DisplayFileResult { Bytes = bytes };
            }

            return Failed($"'{path}' could not be fetched: {string.Join(" ", problems)}");
        }

        private static DisplayFileResult Failed(string problem) => new DisplayFileResult { Problem = problem };
    }
}
