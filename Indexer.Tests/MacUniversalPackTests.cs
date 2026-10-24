using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// The macOS BepInEx 5.4.23.6 pack puts libdoorstop.dylib and run_bepinex.sh at the profile
    /// root and has no doorstop_libs/ folder. The Indexer has no special case for any of it.
    /// </summary>
    public class MacUniversalPackTests
    {
        private static readonly string[] UniversalPackFiles =
        {
            "libdoorstop.dylib",
            "run_bepinex.sh",
            "BepInEx/core/BepInEx.dll",
            "BepInEx/core/0Harmony.dll"
        };

        private static bool Matches(string rule, string relPath) =>
            Program.RuleMatches(rule, relPath, Path.GetFileName(relPath), relPath + "/");

        [Theory]
        [InlineData("**/.git/")]
        [InlineData("*.log")]
        [InlineData("*.pdb")]
        [InlineData(".DS_Store")]
        [InlineData("Indexer.exe")]
        public void CommonIgnoreRules_DoNotMatchThePackFiles(string rule)
        {
            Assert.All(UniversalPackFiles, path => Assert.False(Matches(rule, path), path));
        }

        [Fact]
        public void RootFilesOfThePack_AreNotGroupedWithAnythingElse()
        {
            Assert.Equal("libdoorstop.dylib", Program.ModFolderOf("libdoorstop.dylib"));
            Assert.Equal("run_bepinex.sh", Program.ModFolderOf("run_bepinex.sh"));
        }

        [Fact]
        public void MovingFromTheLegacyLibraryToTheUniversalOne_IsReportedAsAddedAndRemoved()
        {
            var before = new HashSet<string> { "doorstop_libs/libdoorstop_x64.dylib", "BepInEx/core/BepInEx.dll" };
            var after = new HashSet<string>(UniversalPackFiles);

            (List<string> added, List<string> removed) = Program.DiffPaths(before, after);

            Assert.Equal(new[] { "BepInEx/core/0Harmony.dll", "libdoorstop.dylib", "run_bepinex.sh" },
                added.OrderBy(p => p, StringComparer.Ordinal));
            Assert.Equal(new[] { "doorstop_libs/libdoorstop_x64.dylib" }, removed);
        }

        [Fact]
        public void KeepingBothLibraries_ReportsOnlyTheNewFilesAsAdded()
        {
            var before = new HashSet<string> { "doorstop_libs/libdoorstop_x64.dylib", "BepInEx/core/BepInEx.dll" };
            var after = new HashSet<string>(before) { "libdoorstop.dylib", "run_bepinex.sh" };

            (List<string> added, List<string> removed) = Program.DiffPaths(before, after);

            Assert.Equal(2, added.Count);
            Assert.Empty(removed);
        }
    }
}
