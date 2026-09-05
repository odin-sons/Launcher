using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class ModManifestTests
    {
        [Fact]
        public void Parse_ReadsNameDescriptionVersionAndWebsiteUrl()
        {
            const string json = @"{
                ""name"": ""VNEI"",
                ""version_number"": ""0.17.5"",
                ""website_url"": ""https://thunderstore.io/c/valheim/p/MSCH/VNEI/"",
                ""description"": ""View Nearly Every Item — a database and testing mod."",
                ""dependencies"": [""denikson-BepInExPack_Valheim-5.4.2100""]
            }";

            ModManifest manifest = ModManifest.Parse(json);

            Assert.Equal("VNEI", manifest.Name);
            Assert.Equal("0.17.5", manifest.VersionNumber);
            Assert.Equal("https://thunderstore.io/c/valheim/p/MSCH/VNEI/", manifest.WebsiteUrl);
            Assert.Equal("View Nearly Every Item — a database and testing mod.", manifest.Description);
        }

        [Fact]
        public void Parse_MissingFields_LeavesThemNullInsteadOfThrowing()
        {
            const string json = @"{ ""name"": ""BareMod"" }";

            ModManifest manifest = ModManifest.Parse(json);

            Assert.Equal("BareMod", manifest.Name);
            Assert.Null(manifest.Description);
            Assert.Null(manifest.VersionNumber);
            Assert.Null(manifest.WebsiteUrl);
        }

        [Fact]
        public void Parse_EmptyWebsiteUrl_IsKeptAsIs()
        {
            // Thunderstore's own scaffold ships website_url as "" when the author didn't set one —
            // that's a legitimate value, not something to normalize away here.
            const string json = @"{ ""name"": ""NoLink"", ""website_url"": """" }";

            ModManifest manifest = ModManifest.Parse(json);

            Assert.Equal("", manifest.WebsiteUrl);
        }
    }
}
