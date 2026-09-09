using System.Linq;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// <see cref="DownloadProgressTracker"/> — turns a parallel download into the
    /// "N / M mods · X / Y MB" line and the biggest-first short list the install window shows.
    /// </summary>
    public sealed class DownloadProgressTrackerTests
    {
        private static DownloadProgressTracker ThreeGroups() => new(new[]
        {
            ("EpicLoot", 100L), ("EpicLoot", 100L),   // 200 over 2 files
            ("Marketplace_Sounds", 1000L),            // 1000 over 1 file
            (DownloadGrouping.MiscKey, 10L),          // 10 over 1 file
        });

        [Fact]
        public void TotalsComeFromTheConstructor()
        {
            DownloadProgressTracker t = ThreeGroups();
            Assert.Equal(3, t.GroupsTotal);
            Assert.Equal(1210, t.BytesTotal);
            Assert.Equal(0, t.GroupsDone);
            Assert.Equal(0, t.BytesDone);
        }

        [Fact]
        public void AddBytes_AccumulatesPerGroup_AndClampsAtZeroOnRollback()
        {
            DownloadProgressTracker t = ThreeGroups();
            t.AddBytes("EpicLoot", 80);
            t.AddBytes("Marketplace_Sounds", 400);
            Assert.Equal(480, t.BytesDone);

            t.AddBytes("EpicLoot", -200); // retry rollback, larger than what was counted
            Assert.Equal(400, t.BytesDone);
        }

        [Fact]
        public void AGroupCountsAsDone_OnlyWhenEveryFileInItIsComplete()
        {
            DownloadProgressTracker t = ThreeGroups();

            t.CompleteFile("EpicLoot");
            Assert.Equal(0, t.GroupsDone); // 1 of EpicLoot's 2 files

            t.CompleteFile("EpicLoot");
            Assert.Equal(1, t.GroupsDone);

            t.CompleteFile("Marketplace_Sounds");
            t.CompleteFile(DownloadGrouping.MiscKey);
            Assert.Equal(3, t.GroupsDone);
        }

        [Fact]
        public void CompleteFile_NeverOvercounts_PastTheGroupSize()
        {
            DownloadProgressTracker t = ThreeGroups();
            t.CompleteFile("Marketplace_Sounds");
            t.CompleteFile("Marketplace_Sounds"); // stray extra call
            Assert.Equal(1, t.GroupsDone);
        }

        [Fact]
        public void ActiveGroups_AreStartedButUnfinished_BiggestRemainingFirst_Capped()
        {
            DownloadProgressTracker t = ThreeGroups();
            t.AddBytes("EpicLoot", 50);              // 150 remaining
            t.AddBytes("Marketplace_Sounds", 100);   // 900 remaining
            // misc: not started

            var active = t.ActiveGroups(10);
            Assert.Equal(new[] { "Marketplace_Sounds", "EpicLoot" }, active.Select(g => g.Key));

            Assert.Single(t.ActiveGroups(1));
            Assert.Equal("Marketplace_Sounds", t.ActiveGroups(1)[0].Key);
        }

        [Fact]
        public void ActiveGroups_ExcludesFinishedGroups_EvenIfBytesLagBehind()
        {
            DownloadProgressTracker t = ThreeGroups();
            t.AddBytes("Marketplace_Sounds", 100);
            t.CompleteFile("Marketplace_Sounds"); // file done; byte counter never reached 1000

            Assert.DoesNotContain(t.ActiveGroups(10), g => g.Key == "Marketplace_Sounds");
        }
    }
}
