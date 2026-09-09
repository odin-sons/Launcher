using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// Building and preparing the Doorstop launch plan.
    ///
    /// Every OS-shaped case runs for Windows, macOS and Linux on whatever host the suite is
    /// on: <see cref="RuntimePlatform.Pretend"/> swaps the OS the injector reasons about, and
    /// those paths only ever differ in strings and file layout — nothing that needs the other
    /// OS's kernel — so faking the answer exercises the real branch.
    /// </summary>
    public sealed class InjectorLauncherTests
    {
        public static readonly TheoryData<TargetOs> AllOs = new() { TargetOs.Windows, TargetOs.MacOS, TargetOs.Linux };
        public static readonly TheoryData<TargetOs> UnixOs = new() { TargetOs.MacOS, TargetOs.Linux };

        private static TestPack NewGameFolder(bool withExecutable = true)
        {
            var pack = new TestPack();
            if (withExecutable) pack.AddGameExecutable("not a real exe, just needs to exist");
            return pack;
        }

        private static TestPack NewProfileFolder(bool withPreloader = true, bool withDoorstopLib = true)
        {
            var pack = new TestPack();
            if (withPreloader) pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            if (RuntimePlatform.IsWindows) pack.AddFile("winhttp.dll", "our doorstop proxy");
            else if (withDoorstopLib) pack.AddFile(InjectorLauncher.DoorstopLibraryRelativePath, "native doorstop");
            return pack;
        }

        [Theory, MemberData(nameof(AllOs))]
        public void HappyPath_ReturnsPlanAndPreparesTheGameFolder(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.True(ok, reason);
            Assert.NotNull(plan);
            Assert.Equal(TestPack.GameExecutablePath(game.Root), plan!.Executable);
            Assert.Equal("1", plan.Environment["DOORSTOP_ENABLED"]);
            Assert.Equal(Path.GetFullPath(Path.Combine(profile.Root, "BepInEx/core/BepInEx.Preloader.dll")),
                plan.Environment["DOORSTOP_TARGET_ASSEMBLY"]);

            if (os == TargetOs.Windows)
            {
                // Windows injects via a substitute winhttp.dll next to the game + a disabled
                // doorstop_config.ini, so a direct Steam launch stays vanilla.
                Assert.True(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
                Assert.Contains("enabled = false", File.ReadAllText(Path.Combine(game.Root, "doorstop_config.ini")));
            }
            else
            {
                // macOS / Linux inject purely through environment variables — the game folder
                // is left untouched, and the native lib rides in an absolute preload path.
                string preload = os == TargetOs.MacOS ? "DYLD_INSERT_LIBRARIES" : "LD_PRELOAD";
                string libName = os == TargetOs.MacOS ? "libdoorstop_x64.dylib" : "libdoorstop_x64.so";
                Assert.EndsWith(libName, plan.Environment[preload]);
                Assert.True(Path.IsPathRooted(plan.Environment[preload]));
                Assert.False(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
                Assert.False(File.Exists(Path.Combine(game.Root, "doorstop_config.ini")));
            }
        }

        [Fact]
        public void BlockingBepInExFolder_IsMovedToBackup_ThenPrepSucceeds()
        {
            // Windows-only behaviour: the backup dance protects the relative target_assembly in
            // doorstop_config.ini, which only exists on the Windows injection path.
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();

            game.AddFile("BepInEx/core/SomeoneElseBepInEx.dll", "not ours");

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.True(ok, reason);
            Assert.NotNull(plan);
            Assert.False(Directory.Exists(Path.Combine(game.Root, "BepInEx")));
            Assert.True(File.Exists(Path.Combine(
                game.Root, GameFolderInspector.BackupFolderName, "BepInEx", "core", "SomeoneElseBepInEx.dll")));
        }

        [Theory, MemberData(nameof(AllOs))]
        public void MissingPreloader_FailsWithoutTouchingTheGameFolder(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder(withPreloader: false);

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.False(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
            Assert.False(File.Exists(Path.Combine(game.Root, "doorstop_config.ini")));
        }

        [Theory, MemberData(nameof(UnixOs))]
        public void MissingDoorstopLibrary_FailsWithoutTouchingTheGameFolder(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder(withDoorstopLib: false);

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.Contains("doorstop", reason, System.StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(game.Root, "BepInEx")));
        }

        [Theory, MemberData(nameof(AllOs))]
        public void MissingGameExecutable_Fails(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack game = NewGameFolder(withExecutable: false);
            using TestPack profile = NewProfileFolder();

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.False(string.IsNullOrEmpty(reason));
        }

        // --- Launch shape (what Process.Start is pointed at) -----------------------------------

        [Fact]
        public void MacLaunchShape_RunsX64UnderEnvWithAbsoluteDoorstopLibrary()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            Assert.True(InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason), reason);

            var si = InjectorLauncher.BuildStartInfo(plan!);
            var argv = si.ArgumentList.ToList();

            // arch -x86_64 /usr/bin/env  KEY=VALUE...  <valheim.app/Contents/MacOS/…>  [doorstop args]
            Assert.Equal("/usr/bin/arch", si.FileName);
            Assert.Equal("-x86_64", argv[0]);
            Assert.Equal("/usr/bin/env", argv[1]);

            string dyld = argv.Single(a => a.StartsWith("DYLD_INSERT_LIBRARIES="))["DYLD_INSERT_LIBRARIES=".Length..];
            Assert.True(Path.IsPathRooted(dyld));
            Assert.EndsWith("libdoorstop_x64.dylib", dyld);

            int exeIndex = argv.IndexOf(TestPack.GameExecutablePath(game.Root));
            Assert.True(exeIndex > 1, "game executable not in the argv");
            Assert.All(argv.Take(exeIndex).Skip(2), a => Assert.Contains('=', a)); // the KEY=VALUE block
        }

        [Theory]
        [InlineData(TargetOs.Windows)]
        [InlineData(TargetOs.Linux)]
        public void NonMacLaunchShape_RunsExecutableDirectlyWithEnvironmentVariables(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            Assert.True(InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason), reason);

            var si = InjectorLauncher.BuildStartInfo(plan!);

            Assert.Equal(TestPack.GameExecutablePath(game.Root), si.FileName);
            Assert.Equal("1", si.EnvironmentVariables["DOORSTOP_ENABLED"]);
            Assert.DoesNotContain(si.ArgumentList, a => a.Contains('='));
        }

        // --- ResolveGameExecutable ------------------------------------------------------------

        [Fact]
        public void ResolveGameExecutable_MacBundle_ResolvesToTheBinaryNamedInInfoPlist()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var pack = new TestPack();
            pack.AddFile("valheim.app/Contents/Info.plist",
                "<?xml version=\"1.0\"?><plist><dict>" +
                "<key>CFBundleName</key><string>Valheim</string>" +
                "<key>CFBundleExecutable</key><string>ValheimPlayer</string></dict></plist>");
            pack.AddFile("valheim.app/Contents/MacOS/ValheimPlayer", "the unity player");
            pack.AddFile("valheim.app/Contents/MacOS/decoy", "not the entry point");

            Assert.Equal(
                Path.Combine(pack.Root, "valheim.app", "Contents", "MacOS", "ValheimPlayer"),
                InjectorLauncher.ResolveGameExecutable(pack.Root));
        }

        [Fact]
        public void ResolveGameExecutable_MacBundleNoPlist_FallsBackToTheSoleBinary()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var pack = new TestPack();
            pack.AddFile("valheim.app/Contents/MacOS/Valheim", "the unity player");

            Assert.Equal(
                Path.Combine(pack.Root, "valheim.app", "Contents", "MacOS", "Valheim"),
                InjectorLauncher.ResolveGameExecutable(pack.Root));
        }

        [Fact]
        public void ResolveGameExecutable_MacBundleNoPlistAmbiguous_ReturnsNull()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var pack = new TestPack();
            pack.AddFile("valheim.app/Contents/MacOS/Valheim", "a");
            pack.AddFile("valheim.app/Contents/MacOS/Other", "b");

            Assert.Null(InjectorLauncher.ResolveGameExecutable(pack.Root));
        }

        [Theory]
        [InlineData(TargetOs.Windows, "valheim.exe")]
        [InlineData(TargetOs.Linux, "valheim.x86_64")]
        public void ResolveGameExecutable_NonMac_ResolvesThePlainFile(TargetOs os, string name)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            pack.AddFile(name, "the game");

            Assert.Equal(Path.Combine(pack.Root, name), InjectorLauncher.ResolveGameExecutable(pack.Root));
        }

        [Theory, MemberData(nameof(AllOs))]
        public void ResolveGameExecutable_NothingThere_ReturnsNull(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            Assert.Null(InjectorLauncher.ResolveGameExecutable(pack.Root));
        }
    }
}
