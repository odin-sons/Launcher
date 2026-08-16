using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// InjectorLauncher.BuildPlan/PrepareGameFolder were already covered separately before.
    /// TryPrepareLaunch is the new entry point that ties them together with GameFolderInspector
    /// into one step: what FileDownloader actually calls when it decides not to duplicate game files.
    /// </summary>
    public sealed class InjectorLauncherTests
    {
        private static TestPack NewGameFolder(bool withExecutable = true)
        {
            var pack = new TestPack();
            if (withExecutable) pack.AddFile("valheim.exe", "not a real exe, just needs to exist");
            return pack;
        }

        private static TestPack NewProfileFolder(bool withPreloader = true, bool withWinhttp = true)
        {
            var pack = new TestPack();
            if (withPreloader) pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            if (withWinhttp) pack.AddFile("winhttp.dll", "our doorstop proxy");
            return pack;
        }

        [Fact]
        public void HappyPath_ReturnsPlanAndPreparesTheGameFolder()
        {
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.True(ok, reason);
            Assert.NotNull(plan);
            Assert.Equal(Path.Combine(game.Root, "valheim.exe"), plan.Executable);
            Assert.Equal("1", plan.Environment["DOORSTOP_ENABLED"]);
            Assert.True(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
            Assert.Contains("enabled = false", File.ReadAllText(Path.Combine(game.Root, "doorstop_config.ini")));
        }

        [Fact]
        public void BlockingBepInExFolder_IsMovedToBackup_ThenPrepSucceeds()
        {
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();

            // Another mod manager already left its own BepInEx folder here — it blocks the
            // relative target_assembly in doorstop_config.ini and must be moved to a backup.
            game.AddFile("BepInEx/core/SomeoneElseBepInEx.dll", "not ours");

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.True(ok, reason);
            Assert.NotNull(plan);
            Assert.False(Directory.Exists(Path.Combine(game.Root, "BepInEx")));
            Assert.True(File.Exists(Path.Combine(
                game.Root, GameFolderInspector.BackupFolderName, "BepInEx", "core", "SomeoneElseBepInEx.dll")));
        }

        [Fact]
        public void MissingPreloader_FailsWithoutTouchingTheGameFolder()
        {
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder(withPreloader: false);

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.False(string.IsNullOrEmpty(reason));

            // Since the plan never got built, there was no point preparing the game folder
            // (copying winhttp.dll, writing doorstop_config.ini) — it should stay untouched.
            Assert.False(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
            Assert.False(File.Exists(Path.Combine(game.Root, "doorstop_config.ini")));
        }

        [Fact]
        public void MissingGameExecutable_Fails()
        {
            using TestPack game = NewGameFolder(withExecutable: false);
            using TestPack profile = NewProfileFolder();

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.False(string.IsNullOrEmpty(reason));
        }
    }
}
