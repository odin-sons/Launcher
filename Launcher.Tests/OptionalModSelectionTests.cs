using System.IO;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class OptionalModSelectionTests
    {
        private static string TempFolder() => Directory.CreateTempSubdirectory("odinsons-optselect-").FullName;

        [Fact]
        public void FreshSelection_NothingIsSelected()
        {
            var selection = OptionalModSelection.Load(TempFolder());

            Assert.False(selection.IsSelected("VNEI"));
        }

        [Fact]
        public void SetSelectedThenSaveThenLoad_RoundTrips()
        {
            string folder = TempFolder();

            var selection = OptionalModSelection.Load(folder);
            selection.SetSelected("VNEI", true);
            selection.Save(folder);

            var reloaded = OptionalModSelection.Load(folder);
            Assert.True(reloaded.IsSelected("VNEI"));
        }

        [Fact]
        public void DeselectingThenSaving_RemovesItFromTheSavedFile()
        {
            string folder = TempFolder();

            var selection = OptionalModSelection.Load(folder);
            selection.SetSelected("VNEI", true);
            selection.Save(folder);

            selection.SetSelected("VNEI", false);
            selection.Save(folder);

            var reloaded = OptionalModSelection.Load(folder);
            Assert.False(reloaded.IsSelected("VNEI"));
        }

        [Fact]
        public void MissingOrCorruptFile_LoadsEmptyInsteadOfThrowing()
        {
            string folder = TempFolder();
            File.WriteAllText(Path.Combine(folder, OptionalModSelection.FileName), "\0\0garbage\0\0");

            var selection = OptionalModSelection.Load(folder);

            Assert.False(selection.IsSelected("anything"));
        }
    }
}
