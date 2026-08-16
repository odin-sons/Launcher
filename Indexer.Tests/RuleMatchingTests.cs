using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// Exclusion rules currently support four forms: a path segment at any depth
    /// ("**/name/"), a prefix from the root ("folder/"), a mask ("*.ext"), and a filename
    /// anywhere in the tree ("name.ext"). Missing a fifth — an exact relative path
    /// ("BepInEx/config/file.cfg").
    ///
    /// Without it, there's no way to exclude the config of ONE specific mod by address: a
    /// rule with no slash catches a same-named file in any folder, and a rule with a single
    /// slash inside currently falls back silently to comparing by filename alone, meaning
    /// the path in the rule is effectively ignored — not what someone who wrote out a full
    /// path would expect.
    /// </summary>
    public class RuleMatchingTests
    {
        [Fact]
        public void ExactPathRule_MatchesOnlyThatExactFile()
        {
            bool matches = Program.RuleMatches(
                rule: "BepInEx/config/cooley.easyspawner.cfg",
                relPath: "BepInEx/config/cooley.easyspawner.cfg",
                fileName: "cooley.easyspawner.cfg",
                pathWithSlash: "BepInEx/config/cooley.easyspawner.cfg/");

            Assert.True(matches);
        }

        [Fact]
        public void ExactPathRule_DoesNotMatchSameFileNameInADifferentFolder()
        {
            // The rule specifies a concrete path — it must not catch a same-named
            // file that happens to sit somewhere else in the tree.
            bool matches = Program.RuleMatches(
                rule: "BepInEx/config/cooley.easyspawner.cfg",
                relPath: "BepInEx/config/backup/cooley.easyspawner.cfg",
                fileName: "cooley.easyspawner.cfg",
                pathWithSlash: "BepInEx/config/backup/cooley.easyspawner.cfg/");

            Assert.False(matches);
        }

        [Fact]
        public void BareFileNameRule_StillMatchesAnywhereInTheTree()
        {
            // Previous behavior — a rule with no slash catches a file with that name
            // anywhere in the tree — must not break with the addition of exact-path rules.
            bool matches = Program.RuleMatches(
                rule: "changelog.md",
                relPath: "BepInEx/plugins/SomeMod/CHANGELOG.md",
                fileName: "CHANGELOG.md",
                pathWithSlash: "BepInEx/plugins/SomeMod/CHANGELOG.md/");

            Assert.True(matches);
        }

        [Fact]
        public void FolderPrefixRule_StillMatchesEverythingUnderIt()
        {
            bool matches = Program.RuleMatches(
                rule: "BepInEx/plugins/Cooleyy-EasySpawner/",
                relPath: "BepInEx/plugins/Cooleyy-EasySpawner/EasySpawner.dll",
                fileName: "EasySpawner.dll",
                pathWithSlash: "BepInEx/plugins/Cooleyy-EasySpawner/EasySpawner.dll/");

            Assert.True(matches);
        }

        [Fact]
        public void MaskRule_StillMatchesByPathWhenItContainsASlash()
        {
            bool matches = Program.RuleMatches(
                rule: "BepInEx/config/*.old",
                relPath: "BepInEx/config/stale.old",
                fileName: "stale.old",
                pathWithSlash: "BepInEx/config/stale.old/");

            Assert.True(matches);
        }
    }
}
