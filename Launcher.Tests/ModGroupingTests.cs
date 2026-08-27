using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class ModGroupingTests
    {
        [Fact]
        public void GroupByPluginFolder_GroupsFilesUnderTheSamePluginsFolder()
        {
            var paths = new[]
            {
                "BepInEx/plugins/VNEI/VNEI.dll",
                "BepInEx/plugins/VNEI/icon.png",
                "BepInEx/plugins/VNEI/manifest.json"
            };

            var groups = ModGrouping.GroupByPluginFolder(paths);

            Assert.True(groups.ContainsKey("VNEI"));
            Assert.Equal(3, groups["VNEI"].Count);
        }

        [Fact]
        public void GroupByPluginFolder_SeparatesDifferentFolders()
        {
            var paths = new[] { "BepInEx/plugins/VNEI/VNEI.dll", "BepInEx/plugins/Shortcuts/Shortcuts.dll" };

            var groups = ModGrouping.GroupByPluginFolder(paths);

            Assert.Equal(2, groups.Count);
        }

        [Fact]
        public void GroupByPluginFolder_IgnoresBepInExConfig()
        {
            // Regression: BepInEx/config used to be grouped too. Real builds routinely have
            // a loose single .cfg file sitting directly in BepInEx/config with no subfolder
            // at all — grouping by "the third path segment" there means grouping by
            // filename, producing one fake single-file "mod" per config. And even a real
            // config subfolder is commonly named after the BepInEx plugin GUID
            // ("shudnal.Seasons"), not the plugins/ folder's Thunderstore package name
            // ("shudnal-Seasons") — the same mod would show up twice under unrelated names.
            var paths = new[]
            {
                "BepInEx/config/some.plugin.guid.cfg",
                "BepInEx/config/shudnal.Seasons/Default settings/Default environments.json"
            };

            var groups = ModGrouping.GroupByPluginFolder(paths);

            Assert.Empty(groups);
        }

        [Fact]
        public void GroupByPluginFolder_IgnoresABareFileDirectlyInPlugins()
        {
            var paths = new[] { "BepInEx/plugins/readme.txt" };

            var groups = ModGrouping.GroupByPluginFolder(paths);

            Assert.Empty(groups);
        }

        [Fact]
        public void GroupByPluginFolder_IgnoresFilesOutsideBepInExPlugins()
        {
            var paths = new[] { "changelog.md", "valheim.exe", "BepInEx/LogOutput.log" };

            var groups = ModGrouping.GroupByPluginFolder(paths);

            Assert.Empty(groups);
        }

        [Fact]
        public void FindManifestJsonPath_ReturnsThePathWhenPresent()
        {
            var files = new[] { "BepInEx/plugins/VNEI/VNEI.dll", "BepInEx/plugins/VNEI/manifest.json" };

            string path = ModGrouping.FindManifestJsonPath(files);

            Assert.Equal("BepInEx/plugins/VNEI/manifest.json", path);
        }

        [Fact]
        public void FindManifestJsonPath_ReturnsNullWhenAbsent()
        {
            var files = new[] { "BepInEx/plugins/VNEI/VNEI.dll" };

            string path = ModGrouping.FindManifestJsonPath(files);

            Assert.Null(path);
        }
    }
}
