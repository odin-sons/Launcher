using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// <see cref="DownloadGrouping.GroupKeyFor"/> — the bucket a downloaded file shows under
    /// in the progress list. Plugin and config subfolders each become their own group;
    /// loose files fall into one catch-all.
    /// </summary>
    public sealed class DownloadGroupingTests
    {
        [Theory]
        [InlineData("BepInEx/plugins/EpicLoot/EpicLoot.dll", "EpicLoot")]
        [InlineData("BepInEx/plugins/EpicLoot/assets/loot.json", "EpicLoot")]
        [InlineData("BepInEx\\plugins\\ValheimRAFT\\ValheimRAFT.dll", "ValheimRAFT")]
        [InlineData("BepInEx/config/Marketplace_Sounds/mus_taverna.mp3", "Marketplace_Sounds")]
        [InlineData("BepInEx/config/wackysDatabase/Items/sword.yml", "wackysDatabase")]
        public void FilesUnderAPluginOrConfigFolder_GroupByThatFolder(string path, string expected)
        {
            Assert.Equal(expected, DownloadGrouping.GroupKeyFor(path));
        }

        [Theory]
        [InlineData("BepInEx/config/Azumatt.AzuClock.cfg")]   // loose .cfg, no folder
        [InlineData("BepInEx/plugins/some_loose_file.dll")]   // loose plugin file
        [InlineData("winhttp.dll")]                           // game-root file
        [InlineData("doorstop_libs/libdoorstop_x64.dylib")]
        [InlineData("")]
        public void LooseFiles_LandInTheCatchAllBucket(string path)
        {
            Assert.Equal(DownloadGrouping.MiscKey, DownloadGrouping.GroupKeyFor(path));
        }

        [Fact]
        public void LeadingSlashesAndBackslashesDontMatter()
        {
            Assert.Equal("EpicLoot", DownloadGrouping.GroupKeyFor("/BepInEx/plugins/EpicLoot/EpicLoot.dll"));
            Assert.Equal("EpicLoot", DownloadGrouping.GroupKeyFor("\\BepInEx\\plugins\\EpicLoot\\x.dll"));
        }
    }
}
