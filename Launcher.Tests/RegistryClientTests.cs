using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public sealed class RegistryClientTests : IDisposable
    {
        private const string MirrorA = "https://a.example.test/registry/";
        private const string MirrorB = "https://b.example.test/registry/";
        private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "registry-client-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private static RegistryKeySet Keys() =>
            new RegistryKeySet(new[] { new KeyValuePair<string, byte[]>("r1", SigningKey.ExportSubjectPublicKeyInfo()) });

        private static byte[] Index(long serial = 7, string expiresAt = "2026-01-08T00:00:00Z", string minLauncher = "3.0.0",
            ECDsa key = null)
        {
            string payload = "{\"schema\":1,\"serial\":" + serial + ",\"issuedAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"" +
                expiresAt + "\",\"minLauncher\":\"" + minLauncher + "\",\"servers\":[{\"id\":\"odinsons\",\"name\":\"Odinsons\"," +
                "\"status\":\"approved\",\"mirrors\":[\"https://server.example.test/Launcher/\"]," +
                "\"digests\":{\"servers.json\":\"" + Digest + "\"}}]}";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] signature = (key ?? SigningKey).SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                keyId = "r1",
                signature = Convert.ToBase64String(signature),
                payload = Convert.ToBase64String(data)
            }));
        }

        private RegistryClient Client(FakeFetcher fetcher, params string[] mirrors) =>
            new RegistryClient(mirrors.Length == 0 ? new[] { MirrorA, MirrorB } : mirrors, Keys(),
                new RegistryCache(_folder), new Version(3, 0, 0), fetcher, () => Now);

        [Fact]
        public async Task TheFirstGoodMirrorWins_AndIsCached()
        {
            var fetcher = new FakeFetcher { [MirrorA] = Index(7) };

            RegistryLoadResult result = await Client(fetcher).LoadAsync();

            Assert.Equal(7, result.Index.Serial);
            Assert.Equal(RegistrySource.Network, result.Source);
            Assert.Equal(new[] { MirrorA + "index.json" }, fetcher.Requested);
            Assert.NotNull(new RegistryCache(_folder).ReadEnvelope());
            Assert.Equal(7, new RegistryCache(_folder).ReadSerialFloor());
        }

        [Fact]
        public async Task ADownMirror_FallsBackToTheNext()
        {
            var fetcher = new FakeFetcher { [MirrorB] = Index(7) };

            RegistryLoadResult result = await Client(fetcher).LoadAsync();

            Assert.Equal(RegistrySource.Network, result.Source);
            Assert.Single(result.Problems);
        }

        [Fact]
        public async Task ATamperedIndex_IsSkipped()
        {
            using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var fetcher = new FakeFetcher { [MirrorA] = Index(9, key: other), [MirrorB] = Index(7) };

            RegistryLoadResult result = await Client(fetcher).LoadAsync();

            Assert.Equal(7, result.Index.Serial);
            Assert.Single(result.Problems);
        }

        [Fact]
        public async Task AnExpiredIndex_IsRejected()
        {
            var fetcher = new FakeFetcher { [MirrorA] = Index(expiresAt: "2026-01-03T00:00:00Z") };

            RegistryLoadResult result = await Client(fetcher, MirrorA).LoadAsync();

            Assert.Null(result.Index);
            Assert.Equal(RegistryFailure.Expired, result.Failure);
        }

        [Fact]
        public async Task AnOlderSerial_IsRejectedAsRollback()
        {
            await Client(new FakeFetcher { [MirrorA] = Index(7) }, MirrorA).LoadAsync();
            var fetcher = new FakeFetcher { [MirrorA] = Index(6), [MirrorB] = Index(8) };

            RegistryLoadResult result = await Client(fetcher).LoadAsync();

            Assert.Equal(8, result.Index.Serial);
            Assert.Contains("older", result.Problems[0]);
        }

        [Fact]
        public async Task OnlyRolledBackMirrors_FallBackToTheCacheNotTheOldIndex()
        {
            await Client(new FakeFetcher { [MirrorA] = Index(7) }, MirrorA).LoadAsync();

            RegistryLoadResult result = await Client(new FakeFetcher { [MirrorA] = Index(5) }, MirrorA).LoadAsync();

            Assert.Equal(RegistrySource.Cache, result.Source);
            Assert.Equal(7, result.Index.Serial);
        }

        [Fact]
        public async Task AllMirrorsDown_UsesTheCacheWhileItIsCurrent()
        {
            await Client(new FakeFetcher { [MirrorA] = Index(7) }, MirrorA).LoadAsync();

            RegistryLoadResult result = await Client(new FakeFetcher(), MirrorA).LoadAsync();

            Assert.Equal(RegistrySource.Cache, result.Source);
            Assert.Equal(7, result.Index.Serial);
        }

        [Fact]
        public async Task AllMirrorsDown_AndAnExpiredCache_GivesNothing()
        {
            await Client(new FakeFetcher { [MirrorA] = Index(7, expiresAt: "2026-01-04T00:00:00Z") }, MirrorA).LoadAsync();
            var later = new RegistryClient(new[] { MirrorA }, Keys(), new RegistryCache(_folder), new Version(3, 0, 0),
                new FakeFetcher(), () => Now.AddDays(2));

            RegistryLoadResult result = await later.LoadAsync();

            Assert.Null(result.Index);
            Assert.Equal(RegistryFailure.Expired, result.Failure);
            Assert.Equal(2, result.Problems.Count);
        }

        [Fact]
        public async Task AllMirrorsDown_AndNoCache_IsUnreachable()
        {
            RegistryLoadResult result = await Client(new FakeFetcher()).LoadAsync();

            Assert.Null(result.Index);
            Assert.Equal(RegistryFailure.Unreachable, result.Failure);
            Assert.Equal(2, result.Problems.Count);
        }

        [Fact]
        public async Task ATamperedCache_IsNotTrusted()
        {
            await Client(new FakeFetcher { [MirrorA] = Index(7) }, MirrorA).LoadAsync();
            File.WriteAllBytes(Path.Combine(_folder, "index.json"), Index(9, key: ECDsa.Create(ECCurve.NamedCurves.nistP256)));

            RegistryLoadResult result = await Client(new FakeFetcher(), MirrorA).LoadAsync();

            Assert.Null(result.Index);
            Assert.Equal(RegistryFailure.BadSignature, result.Failure);
        }

        [Fact]
        public async Task ATooOldLauncher_IsToldSo()
        {
            var fetcher = new FakeFetcher { [MirrorA] = Index(minLauncher: "3.1.0") };

            RegistryLoadResult result = await Client(fetcher, MirrorA).LoadAsync();

            Assert.Null(result.Index);
            Assert.Equal(RegistryFailure.LauncherTooOld, result.Failure);
        }

        [Fact]
        public async Task GarbageFromAMirror_IsSkipped()
        {
            var fetcher = new FakeFetcher { [MirrorA] = Encoding.UTF8.GetBytes("<html>"), [MirrorB] = Index(7) };

            RegistryLoadResult result = await Client(fetcher).LoadAsync();

            Assert.Equal(7, result.Index.Serial);
        }

        [Fact]
        public async Task Cancellation_IsPropagated()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Client(new FakeFetcher { [MirrorA] = Index() }).LoadAsync(cts.Token));
        }

        private sealed class FakeFetcher : IRegistryFetcher
        {
            private readonly Dictionary<string, byte[]> _responses = new Dictionary<string, byte[]>();

            public List<string> Requested { get; } = new List<string>();

            public byte[] this[string mirror]
            {
                set => _responses[mirror + "index.json"] = value;
            }

            public Task<byte[]> FetchAsync(Uri url, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requested.Add(url.ToString());
                return _responses.TryGetValue(url.ToString(), out byte[] bytes)
                    ? Task.FromResult(bytes)
                    : throw new HttpRequestException("HTTP 404");
            }
        }
    }
}
