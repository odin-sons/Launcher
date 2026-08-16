using System;
using System.Net;
using System.Net.Http;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Shared backoff policy for HTTP calls against our own server: nginx's rate/connection
    /// limits (429, 503) are transient by nature and worth retrying, unlike a genuine 404/403.
    /// Used both by the file downloader and by the pre-gameplay mirror/manifest checks, so a
    /// burst of simultaneous launches doesn't get mistaken for "the server is down."
    /// </summary>
    public static class HttpRetry
    {
        private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
        private static readonly Random Jitter = new();

        /// <summary>
        /// Whether to retry for a given server response.
        /// 429 — nginx's rate limit slowed us down, 5xx — a temporary server-side problem.
        /// Everything else (404, 403) is pointless to retry.
        /// </summary>
        public static bool IsRetryableStatus(HttpStatusCode status) =>
            status == HttpStatusCode.TooManyRequests || (int)status >= 500;

        /// <summary>Pause before the next attempt; honors Retry-After if the server sent one.</summary>
        public static TimeSpan Delay(int attempt, HttpResponseMessage response)
        {
            TimeSpan? serverAsked = response?.Headers.RetryAfter?.Delta;
            if (serverAsked is null && response?.Headers.RetryAfter?.Date is { } date)
                serverAsked = date - DateTimeOffset.UtcNow;

            if (serverAsked is { TotalSeconds: > 0 and < 120 })
                return serverAsked.Value;

            // Exponential, with a random addition: without it, several parallel callers that hit
            // the same rate limit would retry in lockstep and hit it again together.
            double seconds = BaseDelay.TotalSeconds * Math.Pow(2, attempt - 1);
            lock (Jitter) seconds += Jitter.NextDouble() * 0.5;

            return TimeSpan.FromSeconds(Math.Min(seconds, 30));
        }
    }
}
