using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ServerPolicyTests : IDisposable
    {
        private const string Mirror = "https://a.example.test/registry/";
        private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "server-policy-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private static RegistryKeySet Keys() =>
            new RegistryKeySet(new[] { new KeyValuePair<string, byte[]>("r1", SigningKey.ExportSubjectPublicKeyInfo()) });

        private static byte[] Index(long serial, string status, string id = "community")
        {
            string server = "{\"id\":\"" + id + "\",\"name\":\"Community\",\"status\":\"" + status +
                "\",\"mirrors\":[\"https://server.example.test/Launcher/\"],\"digests\":{\"servers.json\":\"" + Digest + "\"}}";
            string payload = "{\"schema\":1,\"serial\":" + serial + ",\"issuedAt\":\"2026-01-01T00:00:00Z\"," +
                "\"expiresAt\":\"2026-01-08T00:00:00Z\",\"minLauncher\":\"3.0.0\",\"servers\":[" + server + "]}";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] signature = SigningKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                keyId = "r1",
                signature = Convert.ToBase64String(signature),
                payload = Convert.ToBase64String(data)
            }));
        }

        private static RegistryLoadResult Loaded(string status, string id = "community") =>
            new RegistryLoadResult { Index = RegistryIndexReader.Read(Index(1, status, id), Keys()) };

        private ServerGate Gate(SwitchableFetcher fetcher) =>
            new ServerGate(new RegistryClient(new[] { Mirror }, Keys(), new RegistryCache(_folder),
                new Version(3, 0, 0), fetcher, () => Now));

        [Fact]
        public void Approved_AllowsEverything()
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("approved"), "community", installed: false);

            Assert.True(d.Listed && d.CanInstall && d.CanUpdate && d.CanLaunch);
            Assert.False(d.NeedsConfirmation);
            Assert.Equal(ServerNotice.None, d.Notice);
        }

        [Fact]
        public void NeedsApproval_NotInstalled_IsHiddenAndCannotBeInstalled()
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("needs-approval"), "community", installed: false);

            Assert.False(d.Listed);
            Assert.False(d.CanInstall);
            Assert.False(d.CanLaunch);
            Assert.Equal(ServerNotice.NeedsApproval, d.Notice);
        }

        [Fact]
        public void NeedsApproval_Installed_LaunchesOnlyAfterConfirmation_AndNeverUpdates()
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("needs-approval"), "community", installed: true);

            Assert.True(d.Listed);
            Assert.True(d.CanLaunch);
            Assert.True(d.NeedsConfirmation);
            Assert.False(d.CanUpdate);
            Assert.False(d.CanInstall);
            Assert.Equal(ServerNotice.NeedsApproval, d.Notice);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Suspended_IsHiddenAndNeverLaunchedOrUpdated(bool installed)
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("suspended"), "community", installed);

            Assert.False(d.Listed);
            Assert.False(d.CanInstall || d.CanUpdate || d.CanLaunch);
            Assert.Equal(installed, d.OffersRemoval);
            Assert.Equal(ServerNotice.Suspended, d.Notice);
        }

        [Fact]
        public void AProfileMissingFromTheIndex_IsLeftAloneButNotUpdated()
        {
            ServerDecision installed = ServerPolicy.Evaluate(Loaded("approved", "other"), "community", installed: true);
            ServerDecision fresh = ServerPolicy.Evaluate(Loaded("approved", "other"), "community", installed: false);

            Assert.True(installed.CanLaunch);
            Assert.False(installed.CanUpdate);
            Assert.False(fresh.Listed || fresh.CanInstall || fresh.CanLaunch);
            Assert.Equal(ServerNotice.Unlisted, installed.Notice);
        }

        [Fact]
        public void WithoutAnIndex_AnInstalledProfileLaunchesButDoesNotUpdate()
        {
            var none = new RegistryLoadResult { Failure = RegistryFailure.Unreachable };

            ServerDecision installed = ServerPolicy.Evaluate(none, "community", installed: true);
            ServerDecision fresh = ServerPolicy.Evaluate(none, "community", installed: false);

            Assert.True(installed.CanLaunch);
            Assert.False(installed.CanUpdate);
            Assert.False(fresh.CanInstall);
            Assert.Equal(ServerNotice.RegistryUnavailable, installed.Notice);
        }

        [Theory]
        [InlineData("suspended")]
        [InlineData("needs-approval")]
        public void TheBuiltInProfile_IgnoresTheRegistry(string status)
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded(status, "odinsons"), "odinsons", installed: true);

            Assert.True(d.CanUpdate && d.CanLaunch);
            Assert.False(d.NeedsConfirmation);
        }

        [Fact]
        public async Task AStatusFlipBetweenTwoLaunches_TakesEffectOnTheSecond()
        {
            var fetcher = new SwitchableFetcher();
            ServerGate gate = Gate(fetcher);

            fetcher.Body = Index(1, "approved");
            ServerDecision first = await gate.CheckAsync("community", installed: true);

            fetcher.Body = Index(2, "suspended");
            ServerDecision second = await gate.CheckAsync("community", installed: true);

            fetcher.Body = Index(3, "approved");
            ServerDecision third = await gate.CheckAsync("community", installed: true);

            Assert.True(first.CanLaunch && first.CanUpdate);
            Assert.False(second.CanLaunch || second.CanUpdate);
            Assert.True(second.OffersRemoval);
            Assert.True(third.CanLaunch && third.CanUpdate);
        }

        [Fact]
        public async Task AReplayedApprovedIndex_DoesNotUndoASuspension()
        {
            var fetcher = new SwitchableFetcher();
            ServerGate gate = Gate(fetcher);

            fetcher.Body = Index(5, "suspended");
            await gate.CheckAsync("community", installed: true);

            fetcher.Body = Index(4, "approved");
            ServerDecision replayed = await gate.CheckAsync("community", installed: true);

            Assert.False(replayed.CanLaunch);
            Assert.Equal(ServerNotice.Suspended, replayed.Notice);
        }

        private sealed class SwitchableFetcher : IRegistryFetcher
        {
            public byte[] Body { get; set; }

            public Task<byte[]> FetchAsync(Uri url, CancellationToken cancellationToken) =>
                Body != null ? Task.FromResult(Body) : throw new HttpRequestException("HTTP 404");
        }
    }
}
