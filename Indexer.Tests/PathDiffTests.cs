using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// A general path diff between runs — not about the player/admin transition (that's a
    /// separate, already-existing report), but about files appearing and disappearing at
    /// all, from any manifest. The goal: three lines are enough to see "that's what I did",
    /// instead of manually reading through a 2500-line diff.
    /// </summary>
    public class PathDiffTests
    {
        [Fact]
        public void NewPath_IsReportedAsAdded()
        {
            var oldPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll" };
            var newPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll", "BepInEx/plugins/ModB/ModB.dll" };

            (List<string> added, List<string> removed) = Program.DiffPaths(oldPaths, newPaths);

            Assert.Equal(new[] { "BepInEx/plugins/ModB/ModB.dll" }, added);
            Assert.Empty(removed);
        }

        [Fact]
        public void MissingPath_IsReportedAsRemoved()
        {
            var oldPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll", "BepInEx/plugins/ModB/ModB.dll" };
            var newPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll" };

            (List<string> added, List<string> removed) = Program.DiffPaths(oldPaths, newPaths);

            Assert.Empty(added);
            Assert.Equal(new[] { "BepInEx/plugins/ModB/ModB.dll" }, removed);
        }

        [Fact]
        public void UnchangedPath_IsReportedNeither()
        {
            var oldPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll" };
            var newPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll" };

            (List<string> added, List<string> removed) = Program.DiffPaths(oldPaths, newPaths);

            Assert.Empty(added);
            Assert.Empty(removed);
        }

        [Fact]
        public void DiffPaths_IsAPureSetDifference_CallerDecidesWhetherFirstRunCounts()
        {
            // DiffPaths on its own is an honest set diff: an empty previous set means
            // "every current path is new". The decision "first run, nothing to compare
            // against, print nothing" is made by the calling code (Main), the same way
            // it's already handled for the player/admin transition report.
            var oldPaths = new HashSet<string>();
            var newPaths = new HashSet<string> { "BepInEx/plugins/ModA/ModA.dll" };

            (List<string> added, List<string> removed) = Program.DiffPaths(oldPaths, newPaths);

            Assert.Equal(new[] { "BepInEx/plugins/ModA/ModA.dll" }, added);
            Assert.Empty(removed);
        }
    }
}
