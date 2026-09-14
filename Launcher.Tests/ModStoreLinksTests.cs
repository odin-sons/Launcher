using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class ModStoreLinksTests
    {
        [Theory]
        [InlineData("shudnal-Seasons", "shudnal", "Seasons")]
        [InlineData("Azumatt-AzuCraftyBoxes", "Azumatt", "AzuCraftyBoxes")]
        [InlineData("denikson-BepInExPack_Valheim", "denikson", "BepInExPack_Valheim")]
        public void TrySplit_StandardFolderName_SplitsOnFirstDash(string folderKey, string expectedNs, string expectedName)
        {
            Assert.True(ModStoreLinks.TrySplit(folderKey, out string ns, out string name));
            Assert.Equal(expectedNs, ns);
            Assert.Equal(expectedName, name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("NoDashAtAll")]
        [InlineData("-LeadingDash")]
        [InlineData("TrailingDash-")]
        public void TrySplit_NotAStandardFolderName_ReturnsFalse(string folderKey)
        {
            Assert.False(ModStoreLinks.TrySplit(folderKey, out _, out _));
        }

        [Fact]
        public void TrySplit_NullFolderName_ReturnsFalse()
        {
            Assert.False(ModStoreLinks.TrySplit(null, out _, out _));
        }

        [Fact]
        public void ThunderstoreUrl_BuildsExpectedPackagePageUrl()
        {
            Assert.Equal("https://thunderstore.io/c/valheim/p/shudnal/Seasons/",
                ModStoreLinks.ThunderstoreUrl("shudnal-Seasons"));
        }

        [Fact]
        public void HexiumUrl_BuildsExpectedPackagePageUrl()
        {
            Assert.Equal("https://valheim.hexium.gg/mods/shudnal/Seasons",
                ModStoreLinks.HexiumUrl("shudnal-Seasons"));
        }

        [Fact]
        public void ThunderstoreUrl_NotAStandardFolderName_ReturnsNull()
        {
            Assert.Null(ModStoreLinks.ThunderstoreUrl("NoDashAtAll"));
        }

        [Fact]
        public void HexiumUrl_NotAStandardFolderName_ReturnsNull()
        {
            Assert.Null(ModStoreLinks.HexiumUrl("NoDashAtAll"));
        }
    }
}
