using System.Collections.Generic;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// The admin-only-mods invariants specifically: an empty list must not fail the build
    /// (a run can genuinely have no admin-only mods), but a non-empty list matching nothing
    /// still must — that shape of mistake is what shipped nine admin mods to players once.
    /// </summary>
    public sealed class CheckInvariantsTests
    {
        private static Dictionary<string, Manifest.Entry> HashesFor(params string[] paths)
        {
            var hashes = new Dictionary<string, Manifest.Entry>();
            foreach (string path in paths)
                hashes[path] = new Manifest.Entry(path, "hash", 1);
            return hashes;
        }

        [Fact]
        public void EmptyAdminOnlyList_IsNotReportedAsAProblem()
        {
            Program.AdminOnlyMods.Clear();

            var files = new List<string> { "a" };
            var filesAdmin = new List<string> { "a" }; // identical: no admin-only mods exist
            var hashes = HashesFor("a");
            var problems = new List<string>();

            Program.CheckInvariants(files, filesAdmin, new List<string>(), new List<string>(), hashes, problems);

            Assert.Empty(problems);
        }

        [Fact]
        public void NonEmptyAdminOnlyListMatchingNothing_IsStillReportedAsAProblem()
        {
            // A rule that matches nothing in either build — the exact shape of the original
            // incident: the list wasn't empty, it just never took effect.
            Program.AdminOnlyMods.Clear();
            Program.AdminOnlyMods.Add("nonexistent-admin-mod.dll");

            var files = new List<string> { "a" };
            var filesAdmin = new List<string> { "a" }; // still identical — the rule matched nothing
            var hashes = HashesFor("a");
            var problems = new List<string>();

            Program.CheckInvariants(files, filesAdmin, new List<string>(), new List<string>(), hashes, problems);

            Assert.Contains(problems, p => p.Contains("identical"));
        }
    }
}
