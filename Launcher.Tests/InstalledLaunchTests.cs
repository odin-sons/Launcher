using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class InstalledLaunchTests
    {
        public static readonly TheoryData<TargetOs> AllOs = new() { TargetOs.Windows, TargetOs.MacOS, TargetOs.Linux };

        private static TestPack Profile()
        {
            var pack = new TestPack();
            pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            if (RuntimePlatform.IsWindows) pack.AddFile("winhttp.dll", "our doorstop proxy");
            else pack.AddFile(InjectorLauncher.DoorstopLibraryRelativePath, "native doorstop");
            return pack;
        }

        private static TestPack Game()
        {
            var pack = new TestPack();
            pack.AddGameExecutable();
            return pack;
        }

        [Theory, MemberData(nameof(AllOs))]
        public void AGameCopyInTheProfileFolder_StartsAsItIs(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack profile = Profile();
            profile.AddGameExecutable();
            using TestPack steam = Game();

            bool ok = InstalledLaunch.TryResolve(profile.Root, null, steam.Root, out InjectorPlan? plan, out string? reason);

            Assert.True(ok, reason);
            Assert.Null(plan);
        }

        [Theory, MemberData(nameof(AllOs))]
        public void AGameFolderOfItsOwn_StartsFromThereWithTheMods(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack profile = Profile();
            using TestPack game = Game();
            using TestPack steam = Game();

            bool ok = InstalledLaunch.TryResolve(profile.Root, game.Root, steam.Root, out InjectorPlan? plan, out string? reason);

            Assert.True(ok, reason);
            Assert.Equal(TestPack.GameExecutablePath(game.Root), plan!.Executable);
        }

        [Theory, MemberData(nameof(AllOs))]
        public void NoGameCopyAndNoGameFolder_StartsFromTheSteamInstall(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack profile = Profile();
            using TestPack steam = Game();

            bool ok = InstalledLaunch.TryResolve(profile.Root, null, steam.Root, out InjectorPlan? plan, out string? reason);

            Assert.True(ok, reason);
            Assert.Equal(TestPack.GameExecutablePath(steam.Root), plan!.Executable);
        }

        [Fact]
        public void TheSteamInstallSetAsTheGameFolder_IsUsedTheSameWayAsWithoutOne()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Linux);
            using TestPack profile = Profile();
            using TestPack steam = Game();

            bool ok = InstalledLaunch.TryResolve(profile.Root, steam.Root, steam.Root, out InjectorPlan? plan, out string? reason);

            Assert.True(ok, reason);
            Assert.Equal(TestPack.GameExecutablePath(steam.Root), plan!.Executable);
        }

        [Fact]
        public void NothingToStart_SaysWhichExecutableIsMissingAndWhere()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            using TestPack profile = Profile();

            bool ok = InstalledLaunch.TryResolve(profile.Root, null, null, out InjectorPlan? plan, out string? reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.Contains("valheim.exe", reason);
            Assert.Contains(profile.Root, reason);
        }

        [Fact]
        public void AGameFolderWithoutTheGame_IsReportedNotSilentlyReplacedBySteam()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Linux);
            using TestPack profile = Profile();
            using TestPack emptyGameFolder = new();
            using TestPack steam = Game();

            bool ok = InstalledLaunch.TryResolve(profile.Root, emptyGameFolder.Root, steam.Root, out InjectorPlan? plan, out string? reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.False(string.IsNullOrEmpty(reason));
        }
    }
}
