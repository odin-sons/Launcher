using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    public interface IRegistryFetcher
    {
        Task<byte[]> FetchAsync(Uri url, CancellationToken cancellationToken);
    }

    public interface IDisplayFileFetcher
    {
        /// <summary>Throws <see cref="RegistryException"/> if the response is longer than <paramref name="maxBytes"/>.</summary>
        Task<byte[]> FetchAsync(Uri url, int maxBytes, CancellationToken cancellationToken);
    }

    public sealed class HttpRegistryFetcher : IRegistryFetcher, IDisplayFileFetcher
    {
        private const int Attempts = 3;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public Task<byte[]> FetchAsync(Uri url, CancellationToken cancellationToken) =>
            FetchAsync(url, RegistryIndexReader.MaxEnvelopeBytes, cancellationToken);

        public async Task<byte[]> FetchAsync(Uri url, int maxBytes, CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; attempt++)
            {
                HttpResponseMessage response = null;
                try
                {
                    response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return await ReadLimitedAsync(response, maxBytes, cancellationToken).ConfigureAwait(false);

                    if (attempt == Attempts || !HttpRetry.IsRetryableStatus(response.StatusCode))
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}");

                    await Task.Delay(HttpRetry.Delay(attempt, response), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    response?.Dispose();
                }
            }
        }

        private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
        {
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new RegistryException(RegistryFailure.Malformed, "The response is larger than allowed.");

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    throw new RegistryException(RegistryFailure.Malformed, "The response is larger than allowed.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    public enum RegistrySource
    {
        Network,
        Cache
    }

    public sealed class RegistryLoadResult
    {
        /// <summary>Null when no mirror and no cached copy gave an acceptable index.</summary>
        public RegistryIndex Index { get; init; }

        public RegistrySource Source { get; init; }

        /// <summary>Why each mirror, and the cache, was passed over.</summary>
        public IReadOnlyList<string> Problems { get; init; }

        /// <summary>The failure that decided the outcome when Index is null.</summary>
        public RegistryFailure? Failure { get; init; }
    }

    /// <summary>
    /// Loads the signed registry index: tries each mirror in order, accepts the first index that
    /// verifies, is current, is not older than one already accepted and fits this launcher, and
    /// falls back to the cached copy within its expiry.
    /// </summary>
    public sealed class RegistryClient
    {
        private const string IndexFile = "index.json";

        private readonly IReadOnlyList<string> _mirrors;
        private readonly RegistryKeySet _keys;
        private readonly RegistryCache _cache;
        private readonly IRegistryFetcher _fetcher;
        private readonly Version _launcherVersion;
        private readonly Func<DateTimeOffset> _clock;

        public RegistryClient(IReadOnlyList<string> mirrors, RegistryKeySet keys, RegistryCache cache,
            Version launcherVersion, IRegistryFetcher fetcher = null, Func<DateTimeOffset> clock = null)
        {
            _mirrors = mirrors;
            _keys = keys;
            _cache = cache;
            _launcherVersion = launcherVersion;
            _fetcher = fetcher ?? new HttpRegistryFetcher();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public async Task<RegistryLoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            var problems = new List<string>();
            RegistryFailure? decisive = null;
            long floor = _cache.ReadSerialFloor();

            foreach (string mirror in _mirrors)
            {
                byte[] envelope;
                try
                {
                    envelope = await _fetcher.FetchAsync(new Uri(new Uri(mirror), IndexFile), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RegistryException ex)
                {
                    Note(problems, ref decisive, mirror, ex.Failure, ex.Message);
                    continue;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException)
                {
                    Note(problems, ref decisive, mirror, RegistryFailure.Unreachable, ex.Message);
                    continue;
                }

                try
                {
                    RegistryIndex index = Accept(envelope, floor);
                    _cache.Store(envelope, index.Serial);
                    return new RegistryLoadResult
                    {
                        Index = index,
                        Source = RegistrySource.Network,
                        Problems = problems
                    };
                }
                catch (RegistryException ex)
                {
                    Note(problems, ref decisive, mirror, ex.Failure, ex.Message);
                }
            }

            byte[] cached = _cache.ReadEnvelope();
            if (cached != null)
            {
                try
                {
                    return new RegistryLoadResult
                    {
                        Index = Accept(cached, floor),
                        Source = RegistrySource.Cache,
                        Problems = problems
                    };
                }
                catch (RegistryException ex)
                {
                    Note(problems, ref decisive, "cache", ex.Failure, ex.Message);
                }
            }

            return new RegistryLoadResult
            {
                Source = RegistrySource.Network,
                Problems = problems,
                Failure = decisive ?? RegistryFailure.Unreachable
            };
        }

        /// <summary>The cached index if it still verifies and has not expired; never touches the network.</summary>
        public RegistryLoadResult LoadCached()
        {
            byte[] cached = _cache.ReadEnvelope();
            if (cached != null)
            {
                try
                {
                    return new RegistryLoadResult
                    {
                        Index = Accept(cached, _cache.ReadSerialFloor()),
                        Source = RegistrySource.Cache,
                        Problems = Array.Empty<string>()
                    };
                }
                catch (RegistryException ex)
                {
                    return new RegistryLoadResult
                    {
                        Source = RegistrySource.Cache,
                        Problems = new[] { "cache: " + ex.Message },
                        Failure = ex.Failure
                    };
                }
            }

            return new RegistryLoadResult
            {
                Source = RegistrySource.Cache,
                Problems = Array.Empty<string>(),
                Failure = RegistryFailure.Unreachable
            };
        }

        private RegistryIndex Accept(byte[] envelope, long floor)
        {
            RegistryIndex index = RegistryIndexReader.Read(envelope, _keys);

            if (index.IsExpired(_clock()))
                throw new RegistryException(RegistryFailure.Expired, "The registry index has expired.");
            if (index.Serial < floor)
                throw new RegistryException(RegistryFailure.Rollback,
                    $"The registry index serial {index.Serial} is older than the accepted {floor}.");
            if (index.MinLauncher > _launcherVersion)
                throw new RegistryException(RegistryFailure.LauncherTooOld,
                    $"The registry needs launcher {index.MinLauncher} or newer.");
            return index;
        }

        // A launcher that is too old is the most useful thing to tell the player, so it wins.
        private static void Note(List<string> problems, ref RegistryFailure? decisive, string source,
            RegistryFailure failure, string message)
        {
            problems.Add($"{source}: {message}");
            if (decisive == null || failure == RegistryFailure.LauncherTooOld
                || (decisive == RegistryFailure.Unreachable && failure != RegistryFailure.Unreachable))
                decisive = failure;
        }
    }
}
