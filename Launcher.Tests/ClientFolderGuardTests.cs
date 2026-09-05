using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class ClientFolderGuardTests
    {
        private static string TempFolder() => Directory.CreateTempSubdirectory("odinsons-guard-").FullName;

        [Fact]
        public void EmptyFolder_IsSafe()
        {
            Assert.True(ClientFolderGuard.IsSafeTarget(TempFolder(), out _));
        }

        [Fact]
        public void MissingFolder_IsSafe()
        {
            string path = Path.Combine(TempFolder(), "does-not-exist-yet");

            Assert.True(ClientFolderGuard.IsSafeTarget(path, out _));
        }

        [Fact]
        public void FolderWithOnlyTheDefenderPromptMarker_IsStillSafe()
        {
            // Regression: MaybeOfferDefenderExclusionAsync writes this marker into the client
            // folder the moment it's known — before the player ever presses "Играть". A fresh
            // Rune_v2_test folder with nothing but this marker used to be rejected as "not
            // empty, not a client install", since OwnArtifacts didn't know the marker's name.
            string folder = TempFolder();
            DefenderExclusion.MarkPrompted(folder);

            Assert.True(ClientFolderGuard.IsSafeTarget(folder, out string reason));
            Assert.Null(reason);
        }

        [Fact]
        public void FolderWithAnUnrelatedFile_IsNotSafe()
        {
            string folder = TempFolder();
            File.WriteAllText(Path.Combine(folder, "some_other_program.exe"), "not ours");

            Assert.False(ClientFolderGuard.IsSafeTarget(folder, out string reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void FolderWithAClientMarker_IsSafe()
        {
            string folder = TempFolder();
            File.WriteAllText(Path.Combine(folder, "valheim.exe"), "game exe");

            Assert.True(ClientFolderGuard.IsSafeTarget(folder, out _));
        }
    }
}
