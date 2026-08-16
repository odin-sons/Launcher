using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// Diffs must collapse one mod's files into a single line — otherwise PlanBuild with
    /// its 346 blueprint files prints 346 lines instead of one. Caught by this test:
    /// PlanBuild stores data under BepInEx/config/PlanBuild/..., not
    /// BepInEx/plugins/..., and the grouping only knew about plugins/.
    /// </summary>
    public class ModFolderGroupingTests
    {
        [Fact]
        public void FilesUnderAConfigSubfolder_AreGroupedByThatSubfolder()
        {
            var paths = new List<string>
            {
                "BepInEx/config/PlanBuild/BlackForest/a.blueprint",
                "BepInEx/config/PlanBuild/Swamp/b.blueprint",
                "BepInEx/config/PlanBuild/c.blueprint",
            };

            var grouped = Program.GroupByModFolder(paths).ToList();

            Assert.Single(grouped);
            Assert.Contains("BepInEx/config/PlanBuild/", grouped[0]);
            Assert.Contains("3 files", grouped[0]);
        }

        [Fact]
        public void BareFileDirectlyInConfig_IsNotGroupedWithUnrelatedConfigFiles()
        {
            // Files sitting DIRECTLY in BepInEx/config (no mod subfolder) must not
            // get merged with each other — they share no common "mod".
            var paths = new List<string>
            {
                "BepInEx/config/icecub.ValheimAdminTool.cfg",
                "BepInEx/config/some.other.cfg",
            };

            var grouped = Program.GroupByModFolder(paths).ToList();

            Assert.Equal(2, grouped.Count);
        }

        [Fact]
        public void FilesUnderPluginsSubfolder_StillGroupByThatSubfolder()
        {
            var paths = new List<string>
            {
                "BepInEx/plugins/SomeMod/SomeMod.dll",
                "BepInEx/plugins/SomeMod/icon.png",
            };

            var grouped = Program.GroupByModFolder(paths).ToList();

            Assert.Single(grouped);
            Assert.Contains("BepInEx/plugins/SomeMod/", grouped[0]);
        }
    }
}
