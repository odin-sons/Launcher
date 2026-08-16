using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// A local ledger of "what was already confirmed correct on this machine" —
    /// separate from the server manifest, which only describes what SHOULD be
    /// there, not what this particular player already checked last run.
    ///
    /// Exists for one specific scenario: a file matched the manifest last run, but doesn't
    /// now, even though the server didn't change it. To the player this looks like "the
    /// launcher keeps redownloading the same thing", and the cause is usually antivirus or
    /// write permissions, not the network.
    /// </summary>
    public class ClientLedgerTests
    {
        private static string TempFolder() => Directory.CreateTempSubdirectory("odinsons-ledger-").FullName;

        [Fact]
        public void FreshLedger_HasNoConfirmedEntries()
        {
            var ledger = ClientLedger.Load(TempFolder());

            Assert.False(ledger.TryGetLastConfirmedHash("BepInEx/plugins/Mod.dll", out _));
        }

        [Fact]
        public void RecordThenSaveThenLoad_RoundTripsTheConfirmedHash()
        {
            string folder = TempFolder();

            var ledger = ClientLedger.Load(folder);
            ledger.RecordConfirmed("BepInEx/plugins/Mod.dll", "abc123", size: 42);
            ledger.Save(folder);

            var reloaded = ClientLedger.Load(folder);
            Assert.True(reloaded.TryGetLastConfirmedHash("BepInEx/plugins/Mod.dll", out string hash));
            Assert.Equal("abc123", hash);
        }

        [Fact]
        public void RecordConfirmed_OverwritesThePreviousHashForTheSamePath()
        {
            string folder = TempFolder();

            var ledger = ClientLedger.Load(folder);
            ledger.RecordConfirmed("BepInEx/plugins/Mod.dll", "old-hash", size: 1);
            ledger.RecordConfirmed("BepInEx/plugins/Mod.dll", "new-hash", size: 2);
            ledger.Save(folder);

            var reloaded = ClientLedger.Load(folder);
            reloaded.TryGetLastConfirmedHash("BepInEx/plugins/Mod.dll", out string hash);
            Assert.Equal("new-hash", hash);
        }

        [Fact]
        public void Save_OnlyKeepsEntriesRecordedInThatRun_StaleEntriesAreDropped()
        {
            // The file might have been removed from the build or from the player's folder —
            // the ledger must not grow forever with entries for things no longer checked.
            string folder = TempFolder();

            var first = ClientLedger.Load(folder);
            first.RecordConfirmed("BepInEx/plugins/OldMod.dll", "hash1", size: 1);
            first.Save(folder);

            var second = ClientLedger.Load(folder);
            second.RecordConfirmed("BepInEx/plugins/NewMod.dll", "hash2", size: 2);
            second.Save(folder);

            var third = ClientLedger.Load(folder);
            Assert.False(third.TryGetLastConfirmedHash("BepInEx/plugins/OldMod.dll", out _));
            Assert.True(third.TryGetLastConfirmedHash("BepInEx/plugins/NewMod.dll", out _));
        }

        [Fact]
        public void MissingOrCorruptLedgerFile_LoadsEmptyInsteadOfThrowing()
        {
            string folder = TempFolder();
            File.WriteAllText(Path.Combine(folder, "verified.info"), "this is not a manifest");

            var ledger = ClientLedger.Load(folder);

            Assert.False(ledger.TryGetLastConfirmedHash("anything", out _));
        }
    }
}
