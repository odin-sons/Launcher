using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odinsons.ValheimLauncher;

namespace Launcher.Tests
{
    /// <summary>Signed registry indexes and a fetcher to serve them, for tests that need a registry without a network.</summary>
    internal static class TestRegistry
    {
        public const string Mirror = "https://a.example.test/registry/";
        private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public static readonly DateTimeOffset Now = new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        public static RegistryKeySet Keys() =>
            new(new[] { new KeyValuePair<string, byte[]>("r1", SigningKey.ExportSubjectPublicKeyInfo()) });

        public static byte[] Index(long serial, string status, string id = "community") =>
            Index(serial, (id, status));

        public static byte[] Index(long serial, params (string Id, string Status)[] servers)
        {
            string entries = string.Join(",", servers.Select(server =>
                "{\"id\":\"" + server.Id + "\",\"name\":\"Community " + server.Id + "\",\"status\":\"" + server.Status +
                "\",\"mirrors\":[\"https://server.example.test/Launcher/\"],\"digests\":{\"servers.json\":\"" + Digest + "\"}}"));
            string payload = "{\"schema\":1,\"serial\":" + serial + ",\"issuedAt\":\"2026-01-01T00:00:00Z\"," +
                "\"expiresAt\":\"2026-01-08T00:00:00Z\",\"minLauncher\":\"3.0.0\",\"servers\":[" + entries + "]}";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] signature = SigningKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                keyId = "r1",
                signature = Convert.ToBase64String(signature),
                payload = Convert.ToBase64String(data)
            }));
        }

        public static RegistryLoadResult Loaded(string status, string id = "community") =>
            new() { Index = RegistryIndexReader.Read(Index(1, status, id), Keys()) };

        public static ServerGate Gate(string cacheFolder, SwitchableFetcher fetcher) =>
            new(new RegistryClient(new[] { Mirror }, Keys(), new RegistryCache(cacheFolder),
                new Version(3, 0, 0), fetcher, () => Now));

        /// <summary>Serves whatever <see cref="Body"/> holds, or fails like an unreachable mirror when it is null.</summary>
        public sealed class SwitchableFetcher : IRegistryFetcher
        {
            public byte[]? Body { get; set; }

            public int Calls { get; private set; }

            public Task<byte[]> FetchAsync(Uri url, CancellationToken cancellationToken)
            {
                Calls++;
                return Body != null ? Task.FromResult(Body) : throw new HttpRequestException("HTTP 404");
            }
        }
    }
}
