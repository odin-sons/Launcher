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
        public void SecondRun_WithNothingChanged_TouchesNeitherFileNorCreatesABackup()
        {
            // Windows-only: this is exactly the "already prepared" case EnsureGameFolderPrepared/
            // NeedsPreparation exist to detect without needing write access (let alone
            // elevation) just to find out there's nothing to do.
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();

            Assert.True(InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan firstPlan, out string reason1), reason1);

            string configPath = Path.Combine(game.Root, "doorstop_config.ini");
            string configAfterFirstRun = File.ReadAllText(configPath);
            DateTime configWriteTime = File.GetLastWriteTimeUtc(configPath);

            bool ok = InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason2);

            Assert.True(ok, reason2);
            Assert.NotNull(plan);
            Assert.False(Directory.Exists(Path.Combine(game.Root, GameFolderInspector.BackupFolderName)),
                "a second run with nothing changed should not create a backup folder");
            Assert.Equal(configAfterFirstRun, File.ReadAllText(configPath));
            Assert.Equal(configWriteTime, File.GetLastWriteTimeUtc(configPath));
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

        [Fact]
        public void MacLaunchShape_PassesTheSteamAppIdToTheGame()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            Assert.True(InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason), reason);

            var argv = InjectorLauncher.BuildStartInfo(plan!).ArgumentList.ToList();

            Assert.Contains("SteamAppId=892970", argv);
            Assert.Contains("SteamGameId=892970", argv);
        }

        [Theory]
        [InlineData(TargetOs.Windows)]
        [InlineData(TargetOs.Linux)]
        public void NonMacPlan_CarriesTheSteamAppId(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            Assert.True(InjectorLauncher.TryPrepareLaunch(game.Root, profile.Root, out InjectorPlan plan, out string reason), reason);

            Assert.Equal("892970", plan!.Environment["SteamAppId"]);
            Assert.Equal("892970", plan.Environment["SteamGameId"]);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        public void RosettaMissing_OnlyWhenAppleSiliconCannotRunX64(bool appleSilicon, bool x64Runs, bool expected)
        {
            Assert.Equal(expected, InjectorLauncher.RosettaMissing(appleSilicon, () => x64Runs));
        }

        [Fact]
        public void MacPlan_InjectsTheSteamOverlayAfterDoorstop_WhenItsLibrariesExist()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            using TestPack steam = new TestPack();
            steam.AddFile("steamloader.dylib", "loader");
            steam.AddFile("gameoverlayrenderer.dylib", "overlay");

            InjectorPlan plan = InjectorLauncher.BuildPlan(game.Root, profile.Root, out string reason, steam.Root);

            Assert.NotNull(plan);
            string doorstop = Path.Combine(Path.GetFullPath(profile.Root), "doorstop_libs", "libdoorstop_x64.dylib");
            Assert.Equal(
                string.Join(":", doorstop, Path.Combine(steam.Root, "steamloader.dylib"),
                    Path.Combine(steam.Root, "gameoverlayrenderer.dylib")),
                plan.Environment["DYLD_INSERT_LIBRARIES"]);
            Assert.Equal("892970", plan.Environment["SteamOverlayGameId"]);
            Assert.Equal("1", plan.Environment["SteamEnv"]);
            Assert.Equal("1", plan.Environment["SteamClientLaunch"]);
        }

        [Fact]
        public void MacPlan_WithoutSteamOverlayLibraries_InjectsOnlyDoorstop()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            using TestPack steam = new TestPack();

            InjectorPlan plan = InjectorLauncher.BuildPlan(game.Root, profile.Root, out string reason, steam.Root);

            Assert.NotNull(plan);
            Assert.EndsWith("libdoorstop_x64.dylib", plan.Environment["DYLD_INSERT_LIBRARIES"]);
            Assert.DoesNotContain("steamloader", plan.Environment["DYLD_INSERT_LIBRARIES"]);
        }

        [Fact]
        public void RosettaMissing_DoesNotProbeOnIntel()
        {
            Assert.False(InjectorLauncher.RosettaMissing(false, () => throw new InvalidOperationException("probed")));
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

        // --- Native arm64 vs x86_64/Rosetta (macOS) ---------------------------------------------

        private static TestPack NewUniversalProfileFolder(string universalRelativePath = "libdoorstop.dylib",
                                                          bool alsoX64 = false)
        {
            var pack = new TestPack();
            pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            pack.AddFile(universalRelativePath, "universal doorstop");
            if (alsoX64) pack.AddFile("doorstop_libs/libdoorstop_x64.dylib", "x64 doorstop");
            return pack;
        }

        private static InjectorPlan BuildMacPlan(TestPack game, TestPack profile, string steamBin,
                                                 bool forceX64 = false)
        {
            InjectorPlan plan = InjectorLauncher.BuildPlan(game.Root, profile.Root, out string reason, steamBin, forceX64);
            Assert.True(plan is not null, reason);
            return plan!;
        }

        [Theory]
        [InlineData("libdoorstop.dylib")]
        [InlineData("doorstop_libs/libdoorstop.dylib")]
        public void Native_ChosenOnAppleSiliconWithUniversalLibraryAndArm64Executable(string libraryPath)
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(appleSilicon: true, executableHasArm64Slice: true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewUniversalProfileFolder(libraryPath);
            using TestPack steam = new TestPack();
            steam.AddFile("steamloader.dylib", "loader");
            steam.AddFile("gameoverlayrenderer.dylib", "overlay");

            InjectorPlan plan = BuildMacPlan(game, profile, steam.Root);

            string library = Path.Combine(Path.GetFullPath(profile.Root), libraryPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(plan.NativeArm64);
            Assert.Equal(
                string.Join(":", library, Path.Combine(steam.Root, "steamloader.dylib"),
                    Path.Combine(steam.Root, "gameoverlayrenderer.dylib")),
                plan.Environment["DYLD_INSERT_LIBRARIES"]);
            Assert.Equal(Path.GetDirectoryName(library), plan.Environment["DYLD_LIBRARY_PATH"]);
            Assert.Equal("892970", plan.Environment["SteamAppId"]);
            Assert.Equal("892970", plan.Environment["SteamGameId"]);
            Assert.Equal("892970", plan.Environment["SteamOverlayGameId"]);
            Assert.Equal("1", plan.Environment["SteamEnv"]);
            Assert.Equal("1", plan.Environment["SteamClientLaunch"]);
        }

        [Fact]
        public void Native_PrefersTheUniversalLibraryOverTheX64OneWhenBothExist()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(true, true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewUniversalProfileFolder(alsoX64: true);
            using TestPack steam = new TestPack();

            InjectorPlan plan = BuildMacPlan(game, profile, steam.Root);

            Assert.True(plan.NativeArm64);
            Assert.EndsWith("libdoorstop.dylib", plan.Environment["DYLD_INSERT_LIBRARIES"]);
            Assert.DoesNotContain("_x64", plan.Environment["DYLD_INSERT_LIBRARIES"]);
        }

        [Fact]
        public void Native_StartInfo_RunsArchWithEForDyldAndPlainEnvForTheRest()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(true, true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewUniversalProfileFolder();
            using TestPack steam = new TestPack();
            InjectorPlan plan = BuildMacPlan(game, profile, steam.Root);

            var si = InjectorLauncher.BuildStartInfo(plan);
            var argv = si.ArgumentList.ToList();

            Assert.Equal("/usr/bin/arch", si.FileName);
            Assert.Equal(
                new[]
                {
                    "-e", $"DYLD_LIBRARY_PATH={plan.Environment["DYLD_LIBRARY_PATH"]}",
                    "-e", $"DYLD_INSERT_LIBRARIES={plan.Environment["DYLD_INSERT_LIBRARIES"]}",
                    plan.Executable,
                    "--doorstop-enabled", "true",
                    "--doorstop-target-assembly", plan.Environment["DOORSTOP_TARGET_ASSEMBLY"]
                },
                argv);
            Assert.DoesNotContain("-x86_64", argv);
            Assert.DoesNotContain("/usr/bin/env", argv);

            Assert.Equal("arm64,x86_64", si.EnvironmentVariables["ARCHPREFERENCE"]);
            Assert.Equal("1", si.EnvironmentVariables["DOORSTOP_ENABLED"]);
            Assert.Equal("892970", si.EnvironmentVariables["SteamAppId"]);
            Assert.Equal("1", si.EnvironmentVariables["SteamEnv"]);
        }

        [Fact]
        public void Native_SkipsTheRosettaCheck()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(true, true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewUniversalProfileFolder();
            using TestPack steam = new TestPack();
            InjectorPlan plan = BuildMacPlan(game, profile, steam.Root);

            Assert.False(InjectorLauncher.RosettaMissing(plan, true, () => throw new InvalidOperationException("probed")));
        }

        [Fact]
        public void X64Plan_StillRunsTheRosettaCheck()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(true, true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            using TestPack steam = new TestPack();
            InjectorPlan plan = BuildMacPlan(game, profile, steam.Root);

            Assert.False(plan.NativeArm64);
            Assert.True(InjectorLauncher.RosettaMissing(plan, true, () => false));
        }

        public static readonly TheoryData<string, bool, bool, bool, bool> RosettaFallbacks = new()
        {
            { "old profile, x64 library only", true, true, false, false },
            { "Intel host", false, true, true, false },
            { "executable without an arm64 slice", true, false, true, false },
            { "forced x86_64", true, true, true, true }
        };

        [Theory, MemberData(nameof(RosettaFallbacks))]
        public void FallsBackToX64UnderRosetta(string scenario, bool appleSilicon, bool arm64Slice,
                                               bool universalLibrary, bool forceX64)
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(appleSilicon, arm64Slice);
            using TestPack game = NewGameFolder();
            using TestPack profile = universalLibrary ? NewUniversalProfileFolder(alsoX64: true) : NewProfileFolder();
            using TestPack steam = new TestPack();

            InjectorPlan plan = BuildMacPlan(game, profile, steam.Root, forceX64);

            Assert.False(plan.NativeArm64, scenario);
            Assert.EndsWith("libdoorstop_x64.dylib", plan.Environment["DYLD_INSERT_LIBRARIES"]);
            var si = InjectorLauncher.BuildStartInfo(plan);
            Assert.Equal("/usr/bin/arch", si.FileName);
            Assert.Equal(new[] { "-x86_64", "/usr/bin/env" }, si.ArgumentList.Take(2).ToArray());
        }

        [Fact]
        public void FallsBackToX64_WhenForcedThroughTheEnvironmentVariable()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(true, true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewUniversalProfileFolder(alsoX64: true);
            using TestPack steam = new TestPack();

            string? previous = Environment.GetEnvironmentVariable(InjectorLauncher.ForceX64EnvironmentVariable);
            Environment.SetEnvironmentVariable(InjectorLauncher.ForceX64EnvironmentVariable, "1");
            try
            {
                Assert.False(BuildMacPlan(game, profile, steam.Root).NativeArm64);
            }
            finally
            {
                Environment.SetEnvironmentVariable(InjectorLauncher.ForceX64EnvironmentVariable, previous);
            }
        }

        [Fact]
        public void UniversalLibraryOnly_WithoutNativeSupport_FailsWithMissingDoorstopLibrary()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.MacOS);
            using var __ = MacArchitecture.Pretend(appleSilicon: false, executableHasArm64Slice: true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewUniversalProfileFolder();

            InjectorPlan? plan = InjectorLauncher.BuildPlan(game.Root, profile.Root, out string reason);

            Assert.Null(plan);
            Assert.Contains("libdoorstop_x64.dylib", reason);
        }

        [Theory]
        [InlineData(TargetOs.Windows)]
        [InlineData(TargetOs.Linux)]
        public void NonMac_IgnoresTheNativeSwitches(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var __ = MacArchitecture.Pretend(true, true);
            using TestPack game = NewGameFolder();
            using TestPack profile = NewProfileFolder();
            profile.AddFile("libdoorstop.dylib", "universal doorstop");

            InjectorPlan? plan = InjectorLauncher.BuildPlan(game.Root, profile.Root, out string reason);

            Assert.True(plan is not null, reason);
            Assert.False(plan!.NativeArm64);
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
