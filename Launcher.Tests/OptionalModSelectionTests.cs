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
        public void DeselectingThenSaving_IsSelectedBecomesFalse()
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

        [Fact]
        public void NeverMentionedFolder_IsNotKnown()
        {
            var selection = OptionalModSelection.Load(TempFolder());

            Assert.False(selection.IsKnown("VNEI"));
        }

        [Fact]
        public void SelectedFolder_IsKnownAndSelected()
        {
            var selection = OptionalModSelection.Load(TempFolder());
            selection.SetSelected("VNEI", true);

            Assert.True(selection.IsKnown("VNEI"));
            Assert.True(selection.IsSelected("VNEI"));
        }

        [Fact]
        public void ExplicitlyDeselectedFolder_IsKnownButNotSelected()
        {
            // The distinction FileDownloader relies on to uninstall a deselected mod without
            // touching one the player brought themselves and never mentioned here at all.
            var selection = OptionalModSelection.Load(TempFolder());
            selection.SetSelected("VNEI", false);

            Assert.True(selection.IsKnown("VNEI"));
            Assert.False(selection.IsSelected("VNEI"));
        }

        [Fact]
        public void TurningOffThenSaving_PersistsAsKnownButDeselected_NotAsAbsent()
        {
            string folder = TempFolder();

            var selection = OptionalModSelection.Load(folder);
            selection.SetSelected("VNEI", true);
            selection.Save(folder);

            selection.SetSelected("VNEI", false);
            selection.Save(folder);

            var reloaded = OptionalModSelection.Load(folder);
            Assert.True(reloaded.IsKnown("VNEI"));
            Assert.False(reloaded.IsSelected("VNEI"));
        }
    }
}
