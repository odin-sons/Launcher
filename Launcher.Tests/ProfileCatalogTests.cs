using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ProfileCatalogTests : IDisposable
    {
        private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        private readonly string _dir = Directory.CreateTempSubdirectory("profile-catalog-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static string Server(string id, string name, string status) =>
            "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"status\":\"" + status +
            "\",\"mirrors\":[\"https://" + id + ".example.test/Launcher/\"],\"digests\":{\"servers.json\":\"" + Digest + "\"}}";

        private static RegistryLoadResult Load(params string[] servers)
        {
            string payload = "{\"schema\":1,\"serial\":1,\"issuedAt\":\"2026-01-01T00:00:00Z\"," +
                "\"expiresAt\":\"2026-01-08T00:00:00Z\",\"minLauncher\":\"3.0.0\",\"servers\":[" + string.Join(",", servers) + "]}";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] envelope = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                keyId = "r1",
                signature = Convert.ToBase64String(SigningKey.SignData(data, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence)),
                payload = Convert.ToBase64String(data)
            }));
            var keys = new RegistryKeySet(new[] { new KeyValuePair<string, byte[]>("r1", SigningKey.ExportSubjectPublicKeyInfo()) });
            return new RegistryLoadResult { Index = RegistryIndexReader.Read(envelope, keys) };
        }

        private static string[] Ids(IReadOnlyList<ProfileChoice> choices) => choices.Select(c => c.Profile.Id).ToArray();

        [Fact]
        public void WithoutAnIndex_OnlyTheBuiltInProfileIsOffered()
        {
            var none = new RegistryLoadResult { Failure = RegistryFailure.Unreachable };

            Assert.Equal(new[] { "odinsons" }, Ids(ProfileCatalog.Build(none, _ => false)));
        }

        [Fact]
        public void TheBuiltInProfileComesFirst_ThenTheRegistryByName()
        {
            RegistryLoadResult load = Load(Server("zeta", "Zeta", "approved"), Server("alpha", "Alpha", "approved"));

            Assert.Equal(new[] { "odinsons", "alpha", "zeta" }, Ids(ProfileCatalog.Build(load, _ => false)));
        }

        [Fact]
        public void ARegistryEntryWithTheBuiltInId_DoesNotReplaceIt()
        {
            RegistryLoadResult load = Load(Server("odinsons", "Impostor", "approved"));

            IReadOnlyList<ProfileChoice> choices = ProfileCatalog.Build(load, _ => false);

            Assert.Single(choices);
            Assert.Equal(ServerProfile.Default.Mirrors, choices[0].Profile.Mirrors);
        }

        [Fact]
        public void SuspendedProfiles_AreNeverOffered_AndNeedsApprovalOnlyWhenInstalled()
        {
            RegistryLoadResult load = Load(
                Server("gone", "Gone", "suspended"),
                Server("pending", "Pending", "needs-approval"),
                Server("fine", "Fine", "approved"));

            Assert.Equal(new[] { "odinsons", "fine" }, Ids(ProfileCatalog.Build(load, _ => false)));
            Assert.Equal(new[] { "odinsons", "fine", "pending" }, Ids(ProfileCatalog.Build(load, id => id != "gone")));
        }

        [Fact]
        public void TheRememberedProfile_IsResolvedWhenOffered_OtherwiseTheBuiltInOne()
        {
            IReadOnlyList<ProfileChoice> choices = ProfileCatalog.Build(Load(Server("fine", "Fine", "approved")), _ => false);

            Assert.Equal("fine", ProfileCatalog.Resolve("fine", choices).Profile.Id);
            Assert.Equal("odinsons", ProfileCatalog.Resolve("gone", choices).Profile.Id);
            Assert.Equal("odinsons", ProfileCatalog.Resolve(null, choices).Profile.Id);
        }

        [Fact]
        public void TheChoice_IsRememberedInConfig()
        {
            var config = new IniFile(Path.Combine(_dir, "config.ini"));
            var profile = new ServerProfile("fine", "Fine", new[] { "https://fine.example.test/Launcher/" });

            Assert.Null(LauncherPaths.SelectedProfileId(config));
            LauncherPaths.SetSelectedProfile(config, profile);

            var reloaded = new IniFile(Path.Combine(_dir, "config.ini"));
            reloaded.Load();
            Assert.Equal("fine", LauncherPaths.SelectedProfileId(reloaded));
        }

        [Fact]
        public void TheCachedChoices_NeedNoNetwork_AndFallBackToTheBuiltInProfile()
        {
            Assert.Equal(new[] { "odinsons" }, Ids(OfficialRegistry.CachedChoices(_dir)));
        }
    }
}
