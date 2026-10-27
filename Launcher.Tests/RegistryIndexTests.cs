using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class RegistryIndexTests
    {
        private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string MirrorsJson = "\"mirrors\":[\"https://server.example.test/Launcher/\"]";
        private const string DigestsJson = "\"servers.json\":\"" + Digest + "\"";

        private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        private static RegistryKeySet Keys(ECDsa key = null) =>
            new RegistryKeySet(new[]
            {
                new KeyValuePair<string, byte[]>("r1", (key ?? SigningKey).ExportSubjectPublicKeyInfo())
            });

        private static string Server(string id = "odinsons", string extra = "") =>
            "{\"id\":\"" + id + "\",\"name\":\"Odinsons\",\"status\":\"approved\"," + MirrorsJson +
            ",\"digests\":{" + DigestsJson + "}" + extra + "}";

        private static string Payload(string servers = null, string window = null, string schema = "1",
            string serial = "7", string minLauncher = "3.0.0") =>
            "{\"schema\":" + schema + ",\"serial\":" + serial + "," +
            (window ?? "\"issuedAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"2026-01-08T00:00:00Z\"") +
            ",\"minLauncher\":\"" + minLauncher + "\",\"servers\":[" + (servers ?? Server()) + "]}";

        private static byte[] Sign(string payload, string keyId = "r1", ECDsa key = null)
        {
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] signature = (key ?? SigningKey).SignData(data, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                keyId,
                signature = Convert.ToBase64String(signature),
                payload = Convert.ToBase64String(data)
            }));
        }

        private static RegistryFailure FailureOf(Action action) =>
            Assert.Throws<RegistryException>(action).Failure;

        private static void AssertInvalid(string payload) =>
            Assert.Equal(RegistryFailure.Invalid, FailureOf(() => RegistryIndexReader.Read(Sign(payload), Keys())));

        [Fact]
        public void ReadsASignedIndex()
        {
            RegistryIndex index = RegistryIndexReader.Read(Sign(Payload()), Keys());

            Assert.Equal(7, index.Serial);
            Assert.Equal(new Version(3, 0, 0), index.MinLauncher);
            RegistryEntry entry = Assert.Single(index.Entries);
            Assert.Equal("odinsons", entry.Id);
            Assert.Equal(RegistryStatus.Approved, entry.Status);
            Assert.Equal(Digest, entry.Digests["servers.json"]);
            Assert.Equal("odinsons", entry.ToProfile().Id);
        }

        [Fact]
        public void ATamperedPayload_IsRejected()
        {
            string json = Encoding.UTF8.GetString(Sign(Payload()));
            string original = Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload()));
            string forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload(serial: "8")));
            string tampered = json.Replace(original, forged);

            Assert.NotEqual(json, tampered);
            Assert.Equal(RegistryFailure.BadSignature,
                FailureOf(() => RegistryIndexReader.Read(Encoding.UTF8.GetBytes(tampered), Keys())));
        }

        [Fact]
        public void ASignatureFromAnotherKey_IsRejected()
        {
            using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            Assert.Equal(RegistryFailure.BadSignature,
                FailureOf(() => RegistryIndexReader.Read(Sign(Payload(), key: other), Keys())));
        }

        [Fact]
        public void AnUnknownKeyId_IsRejected()
        {
            Assert.Equal(RegistryFailure.UnknownKey,
                FailureOf(() => RegistryIndexReader.Read(Sign(Payload(), keyId: "r2"), Keys())));
        }

        [Fact]
        public void ALauncherWithNoPinnedKeys_TrustsNothing()
        {
            Assert.Equal(RegistryFailure.UnknownKey,
                FailureOf(() => RegistryIndexReader.Read(Sign(Payload()), RegistryKeySet.Official)));
        }

        [Fact]
        public void AGarbageSignature_IsRejectedNotThrown()
        {
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload()));
            string envelope = "{\"keyId\":\"r1\",\"signature\":\"AAAA\",\"payload\":\"" + payload + "\"}";

            Assert.Equal(RegistryFailure.BadSignature,
                FailureOf(() => RegistryIndexReader.Read(Encoding.UTF8.GetBytes(envelope), Keys())));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{\"keyId\":\"r1\"}")]
        [InlineData("{\"keyId\":\"r1\",\"signature\":\"***\",\"payload\":\"***\"}")]
        public void AMalformedEnvelope_IsRejected(string envelope)
        {
            Assert.Equal(RegistryFailure.Malformed,
                FailureOf(() => RegistryIndexReader.Read(Encoding.UTF8.GetBytes(envelope), Keys())));
        }

        [Fact]
        public void AnEmptyOrOversizedEnvelope_IsRejected()
        {
            Assert.Equal(RegistryFailure.Malformed,
                FailureOf(() => RegistryIndexReader.Read(Array.Empty<byte>(), Keys())));
            Assert.Equal(RegistryFailure.Malformed,
                FailureOf(() => RegistryIndexReader.Read(new byte[RegistryIndexReader.MaxEnvelopeBytes + 1], Keys())));
        }

        [Fact]
        public void AnUnsupportedSchema_IsRefused()
        {
            Assert.Equal(RegistryFailure.UnsupportedSchema,
                FailureOf(() => RegistryIndexReader.Read(Sign(Payload(schema: "2")), Keys())));
        }

        [Fact]
        public void TheSchemaIsOnlyInterpretedAfterTheSignatureChecks()
        {
            using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            Assert.Equal(RegistryFailure.BadSignature,
                FailureOf(() => RegistryIndexReader.Read(Sign(Payload(schema: "2"), key: other), Keys())));
        }

        [Fact]
        public void ExpiryIsReportedByTheIndex()
        {
            RegistryIndex index = RegistryIndexReader.Read(Sign(Payload()), Keys());

            Assert.False(index.IsExpired(new DateTimeOffset(2026, 1, 7, 23, 59, 59, TimeSpan.Zero)));
            Assert.True(index.IsExpired(new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero)));
        }

        [Theory]
        [InlineData("\"issuedAt\":\"2026-01-08T00:00:00Z\",\"expiresAt\":\"2026-01-01T00:00:00Z\"")]
        [InlineData("\"issuedAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"2026-03-01T00:00:00Z\"")]
        [InlineData("\"issuedAt\":\"2026-01-01\",\"expiresAt\":\"2026-01-08\"")]
        [InlineData("\"issuedAt\":\"2026-01-01T00:00:00+02:00\",\"expiresAt\":\"2026-01-08T00:00:00Z\"")]
        public void AnInvalidValidityWindow_IsRejected(string window) => AssertInvalid(Payload(window: window));

        [Theory]
        [InlineData("0")]
        [InlineData("-3")]
        [InlineData("\"7\"")]
        public void AnInvalidSerial_IsRejected(string serial) => AssertInvalid(Payload(serial: serial));

        [Fact]
        public void ANonVersionMinLauncher_IsRejected() => AssertInvalid(Payload(minLauncher: "soon"));

        [Fact]
        public void TheSameIdTwice_IsRejected() => AssertInvalid(Payload(Server() + "," + Server()));

        [Theory]
        [InlineData("Odinsons")]
        [InlineData("a")]
        [InlineData("-bad")]
        [InlineData("has space")]
        [InlineData("../x")]
        public void AnInvalidId_IsRejected(string id) => AssertInvalid(Payload(Server(id)));

        [Theory]
        [InlineData("\"mirrors\":[\"http://server.example.test/Launcher/\"]")]
        [InlineData("\"mirrors\":[\"https://server.example.test/Launcher\"]")]
        [InlineData("\"mirrors\":[\"https://user:pw@server.example.test/\"]")]
        [InlineData("\"mirrors\":[\"https://server.example.test/?a=1/\"]")]
        [InlineData("\"mirrors\":[]")]
        [InlineData("\"mirrors\":[5]")]
        public void InvalidMirrors_AreRejected(string mirrors) =>
            AssertInvalid(Payload(Server().Replace(MirrorsJson, mirrors)));

        [Theory]
        [InlineData("\"status\":\"approved\"", "\"status\":\"trusted\"")]
        [InlineData("\"status\":\"approved\"", "\"status\":1")]
        [InlineData("\"name\":\"Odinsons\"", "\"name\":\"\"")]
        [InlineData("\"name\":\"Odinsons\"", "\"name\":\"Bad\\nName\"")]
        public void InvalidScalarFields_AreRejected(string original, string replacement) =>
            AssertInvalid(Payload(Server().Replace(original, replacement)));

        [Theory]
        [InlineData("\"servers.json\":\"ABC\"")]
        [InlineData("\"../servers.json\":\"" + Digest + "\"")]
        [InlineData("\"/servers.json\":\"" + Digest + "\"")]
        [InlineData("\"a\\\\b\":\"" + Digest + "\"")]
        [InlineData("")]
        public void InvalidDigests_AreRejected(string digests) =>
            AssertInvalid(Payload(Server().Replace(DigestsJson, digests)));

        [Fact]
        public void LinksMustBeHttps()
        {
            RegistryIndex index = RegistryIndexReader.Read(
                Sign(Payload(Server(extra: ",\"website\":\"https://example.test/\",\"community\":null"))), Keys());
            RegistryEntry entry = Assert.Single(index.Entries);
            Assert.Equal("https://example.test/", entry.Website);
            Assert.Null(entry.Community);

            AssertInvalid(Payload(Server(extra: ",\"website\":\"javascript:alert(1)\"")));
            AssertInvalid(Payload(Server(extra: ",\"community\":\"http://example.test/\"")));
        }

        [Fact]
        public void UnknownFields_AreIgnored()
        {
            RegistryIndex index = RegistryIndexReader.Read(
                Sign(Payload(Server(extra: ",\"somethingNew\":{\"a\":1}"))), Keys());

            Assert.Single(index.Entries);
        }

        [Fact]
        public void AKeyThatIsNotP256_IsRefusedWhenPinned()
        {
            using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

            Assert.Throws<ArgumentException>(() => Keys(p384));
        }

        private static string SamplePath(string relative)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "docs", "registry-samples")))
                dir = System.IO.Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return System.IO.Path.Combine(dir, "docs", "registry-samples", relative);
        }

        [Fact]
        public void TheSampleIndexPayload_IsValid()
        {
            RegistryIndex index = RegistryIndexReader.ParsePayload(
                System.IO.File.ReadAllBytes(SamplePath("index.payload.json")));

            Assert.Equal("odinsons", Assert.Single(index.Entries).Id);
        }

        [Fact]
        public void TheSampleEntryFile_IsValid_AndMatchesTheSampleIndex()
        {
            using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(SamplePath("servers/odinsons.json")));
            RegistryEntry entry = RegistryIndexReader.ParseEntry(doc.RootElement);
            RegistryIndex index = RegistryIndexReader.ParsePayload(
                System.IO.File.ReadAllBytes(SamplePath("index.payload.json")));

            Assert.Equal(index.Find("odinsons").Mirrors, entry.Mirrors);
            Assert.Equal(index.Find("odinsons").Digests, entry.Digests);
        }

        private static string Pinned(params string[] paths) =>
            string.Concat(paths.Select(path => ",\"" + path + "\":\"" + Digest + "\""));

        private static string ServerWith(string pinned, string extra) =>
            "{\"id\":\"odinsons\",\"name\":\"Odinsons\",\"status\":\"approved\"," + MirrorsJson +
            ",\"digests\":{" + DigestsJson + pinned + "}" + extra + "}";

        [Fact]
        public void ThePresentationFiles_AreReadWhenTheRegistryPinsThem()
        {
            string server = ServerWith(Pinned("theme/theme.json", "about.json", "terms/en.md", "terms/de.md", "changelog.md"),
                ",\"theme\":\"theme/theme.json\",\"about\":\"about.json\"," +
                "\"terms\":{\"en\":\"terms/en.md\",\"de\":\"terms/de.md\"}");

            RegistryEntry entry = Assert.Single(RegistryIndexReader.Read(Sign(Payload(server)), Keys()).Entries);

            Assert.Equal("theme/theme.json", entry.Theme);
            Assert.Equal("about.json", entry.About);
            Assert.Equal("terms/de.md", entry.Terms["de"]);
            Assert.Equal("changelog.md", entry.Changelog);
        }

        [Fact]
        public void AnEntryWithoutPresentationFiles_HasNone()
        {
            RegistryEntry entry = Assert.Single(RegistryIndexReader.Read(Sign(Payload()), Keys()).Entries);

            Assert.Null(entry.Theme);
            Assert.Null(entry.About);
            Assert.Empty(entry.Terms);
            Assert.Null(entry.Changelog);
        }

        [Theory]
        [InlineData(",\"theme\":\"theme/theme.json\"")]
        [InlineData(",\"about\":\"about.json\"")]
        [InlineData(",\"terms\":{\"en\":\"terms/en.md\"}")]
        public void APresentationFileThatIsNotPinned_IsRejected(string extra)
        {
            AssertInvalid(Payload(ServerWith("", extra)));
        }

        [Theory]
        [InlineData(",\"theme\":\"../theme.json\"")]
        [InlineData(",\"theme\":\"/theme.json\"")]
        [InlineData(",\"about\":5")]
        [InlineData(",\"terms\":[]")]
        [InlineData(",\"terms\":{\"de\":\"terms/en.md\"}")]
        [InlineData(",\"terms\":{\"EN\":\"terms/en.md\"}")]
        [InlineData(",\"terms\":{\"en\":\"..\\terms.md\"}")]
        public void AMalformedPresentationField_IsRejected(string extra)
        {
            AssertInvalid(Payload(ServerWith(Pinned("theme/theme.json", "terms/en.md"), extra)));
        }
    }
}
