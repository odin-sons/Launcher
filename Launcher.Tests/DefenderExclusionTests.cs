using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// Only the marker-file bookkeeping is tested here — IsExcluded/TryAddExclusion spawn a
    /// real powershell.exe against the actual machine's Defender state (and TryAddExclusion
    /// triggers a real UAC prompt), neither of which belongs in an automated test.
    /// </summary>
    public class DefenderExclusionTests
    {
        private static string TempFolder() => Directory.CreateTempSubdirectory("odinsons-defender-").FullName;

        [Fact]
        public void FreshClientFolder_HasNotBeenPrompted()
        {
            Assert.False(DefenderExclusion.HasBeenPrompted(TempFolder()));
        }

        [Fact]
        public void MarkPrompted_MakesHasBeenPromptedTrue()
        {
            string folder = TempFolder();

            DefenderExclusion.MarkPrompted(folder);

            Assert.True(DefenderExclusion.HasBeenPrompted(folder));
        }

        [Fact]
        public void MarkerFile_UsesTheDocumentedName()
        {
            string folder = TempFolder();

            DefenderExclusion.MarkPrompted(folder);

            Assert.True(File.Exists(Path.Combine(folder, DefenderExclusion.PromptedMarkerName)));
        }
    }
}
