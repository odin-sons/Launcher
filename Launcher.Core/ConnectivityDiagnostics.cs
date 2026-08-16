using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Which step the connection to a mirror broke at.</summary>
    public enum ProbeStage
    {
        Dns,
        TcpConnect,
        TlsHandshake,
        HttpRequest,
        Ok
    }

    /// <summary>Result of checking a single mirror.</summary>
    public sealed class MirrorProbe
    {
        public string Url { get; init; }
        public string Host { get; init; }
        public int Port { get; init; }
        public bool IsHttps { get; init; }
        public bool HostIsLiteralIp { get; init; }

        public ProbeStage FailedAt { get; set; } = ProbeStage.Ok;
        public string Error { get; set; }

        public IReadOnlyList<string> Addresses { get; set; } = Array.Empty<string>();
        public long DnsMs { get; set; } = -1;
        public long TcpMs { get; set; } = -1;
        public long TlsMs { get; set; } = -1;
        public long HttpMs { get; set; } = -1;
        public int HttpStatus { get; set; }
        public string TlsProtocol { get; set; }

        public bool Ok => FailedAt == ProbeStage.Ok;
    }

    /// <summary>
    /// Step-by-step mirror reachability check.
    ///
    /// Exists for one specific case: the launcher couldn't reach any mirror, and we need to
    /// tell apart a restricted channel to our server from the player having no network at all.
    /// A plain HTTP request doesn't answer that: it fails the same way on a DNS failure, a TLS
    /// break, or no route at all. So we go step by step and record exactly where it broke.
    ///
    /// Characteristic patterns:
    ///   DNS doesn't resolve, but the mirror answers by raw IP — DNS is being interfered with;
    ///   TCP connects, but TLS breaks — filtering by hostname in SNI;
    ///   everything passes, but bytes barely trickle through — a rate limit.
    ///
    /// No third-party hosts are queried: only our own addresses are checked, the state of
    /// someone else's internet isn't our concern.
    /// </summary>
    public static class ConnectivityDiagnostics
    {
        private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);

        public static async Task<IReadOnlyList<MirrorProbe>> ProbeAllAsync(
            IEnumerable<string> mirrorUrls, CancellationToken cancellationToken = default)
        {
            var results = new List<MirrorProbe>();

            foreach (string url in mirrorUrls)
            {
                MirrorProbe probe = await ProbeAsync(url, cancellationToken);
                results.Add(probe);
                LogProbe(probe);
            }

            LogVerdict(results);
            return results;
        }

        public static async Task<MirrorProbe> ProbeAsync(string url, CancellationToken cancellationToken = default)
        {
            Uri uri;
            try
            {
                uri = new Uri(url);
            }
            catch (Exception ex)
            {
                return new MirrorProbe
                {
                    Url = url,
                    FailedAt = ProbeStage.Dns,
                    Error = "malformed URL: " + ex.Message
                };
            }

            bool literalIp = IPAddress.TryParse(uri.Host, out IPAddress literal);

            var probe = new MirrorProbe
            {
                Url = url,
                Host = uri.Host,
                Port = uri.Port,
                IsHttps = uri.Scheme == Uri.UriSchemeHttps,
                HostIsLiteralIp = literalIp
            };

            var watch = Stopwatch.StartNew();

            // --- 1. DNS -----------------------------------------------------
            IPAddress[] addresses;
            if (literalIp)
            {
                addresses = new[] { literal };
                probe.Addresses = new[] { literal.ToString() };
                probe.DnsMs = 0;
            }
            else
            {
                try
                {
                    watch.Restart();
                    using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    dnsCts.CancelAfter(StepTimeout);

                    addresses = await Dns.GetHostAddressesAsync(uri.Host, dnsCts.Token);
                    probe.DnsMs = watch.ElapsedMilliseconds;
                    probe.Addresses = addresses.Select(a => a.ToString()).ToArray();

                    if (addresses.Length == 0)
                    {
                        probe.FailedAt = ProbeStage.Dns;
                        probe.Error = "DNS returned no addresses";
                        return probe;
                    }
                }
                catch (Exception ex)
                {
                    probe.DnsMs = watch.ElapsedMilliseconds;
                    probe.FailedAt = ProbeStage.Dns;
                    probe.Error = Describe(ex);
                    return probe;
                }
            }

            // --- 2. TCP -----------------------------------------------------
            using var socket = new TcpClient();
            try
            {
                watch.Restart();
                using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                tcpCts.CancelAfter(StepTimeout);

                await socket.ConnectAsync(addresses[0], uri.Port, tcpCts.Token);
                probe.TcpMs = watch.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                probe.TcpMs = watch.ElapsedMilliseconds;
                probe.FailedAt = ProbeStage.TcpConnect;
                probe.Error = Describe(ex);
                return probe;
            }

            // --- 3. TLS -----------------------------------------------------
            if (probe.IsHttps)
            {
                try
                {
                    watch.Restart();
                    using var tlsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    tlsCts.CancelAfter(StepTimeout);

                    await using var tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false);
                    await tls.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions { TargetHost = uri.Host }, tlsCts.Token);

                    probe.TlsMs = watch.ElapsedMilliseconds;
                    probe.TlsProtocol = tls.SslProtocol.ToString();
                }
                catch (Exception ex)
                {
                    probe.TlsMs = watch.ElapsedMilliseconds;
                    probe.FailedAt = ProbeStage.TlsHandshake;
                    probe.Error = Describe(ex);
                    return probe;
                }
            }

            // --- 4. HTTP ----------------------------------------------------
            try
            {
                watch.Restart();
                using var http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = StepTimeout })
                {
                    Timeout = StepTimeout
                };

                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

                probe.HttpMs = watch.ElapsedMilliseconds;
                probe.HttpStatus = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    probe.FailedAt = ProbeStage.HttpRequest;
                    probe.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                }
            }
            catch (Exception ex)
            {
                probe.HttpMs = watch.ElapsedMilliseconds;
                probe.FailedAt = ProbeStage.HttpRequest;
                probe.Error = Describe(ex);
            }

            return probe;
        }

        private static string Describe(Exception ex)
        {
            string text = $"{ex.GetType().Name}: {ex.Message}";

            // The socket error code says more than the text: ConnectionReset at the TLS step
            // is almost always a break caused by equipment in the middle of the path.
            for (Exception e = ex; e is not null; e = e.InnerException)
                if (e is SocketException socket)
                    return text + $" [socket {socket.SocketErrorCode}]";

            return text;
        }

        private static void LogProbe(MirrorProbe p)
        {
            string where = p.Ok ? "OK" : $"FAILED at {p.FailedAt}";
            LauncherLog.Info($"probe {p.Url}: {where}");
            LauncherLog.Debug($"    host={p.Host} port={p.Port} https={p.IsHttps} literalIp={p.HostIsLiteralIp}");
            LauncherLog.Debug($"    dns={p.DnsMs}ms -> [{string.Join(", ", p.Addresses)}]");
            LauncherLog.Debug($"    tcp={p.TcpMs}ms tls={p.TlsMs}ms ({p.TlsProtocol}) http={p.HttpMs}ms status={p.HttpStatus}");
            if (!p.Ok) LauncherLog.Warn($"    reason: {p.Error}");
        }

        /// <summary>
        /// Folds the picture across all mirrors into a single output line.
        /// Not a verdict, just a hint for digging into a complaint.
        /// </summary>
        private static void LogVerdict(IReadOnlyList<MirrorProbe> probes)
        {
            if (probes.Count == 0) return;

            if (probes.Any(p => p.Ok))
            {
                LauncherLog.Info($"connectivity: {probes.Count(p => p.Ok)}/{probes.Count} mirrors reachable");
                return;
            }

            var byName = probes.Where(p => !p.HostIsLiteralIp).ToList();
            var byIp = probes.Where(p => p.HostIsLiteralIp).ToList();

            string verdict;

            if (byIp.Count > 0 && byIp.All(p => p.FailedAt == ProbeStage.Ok))
                verdict = "reachable by raw IP but not by name — name resolution is being interfered with";
            else if (byName.Count > 0 && byName.All(p => p.FailedAt == ProbeStage.Dns))
                verdict = "every hostname failed to resolve — DNS level";
            else if (byName.Any(p => p.FailedAt == ProbeStage.TlsHandshake))
                verdict = "TCP connects but TLS handshake breaks — filtering by hostname in SNI is likely";
            else if (probes.All(p => p.FailedAt == ProbeStage.TcpConnect))
                verdict = "no TCP connection to any mirror — route blocked or no network at all";
            else
                verdict = "mixed failures, see per-mirror lines above";

            LauncherLog.Error($"connectivity: no mirror reachable. Pattern: {verdict}");
        }
    }
}
