using System.ComponentModel;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// The game-file manifest is per-OS: <c>game.info</c> (Windows), <c>game_macos.info</c>,
    /// <c>game_linux.info</c>. The layouts don't overlap, so the launcher must fetch the one
    /// that matches the running OS — and on non-Windows there is deliberately NO fallback to
    /// the Windows <c>game.info</c>, which would drag the whole unusable Windows build in as
    /// an ordinary download.
    ///
    /// Every case runs for all three OSes on any host via <see cref="RuntimePlatform.Pretend"/>.
    /// </summary>
    public sealed class GameManifestSelectionTests
    {
        public static readonly TheoryData<TargetOs> AllOs = new() { TargetOs.Windows, TargetOs.MacOS, TargetOs.Linux };
        public static readonly TheoryData<TargetOs> UnixOs = new() { TargetOs.MacOS, TargetOs.Linux };

        private static async Task RunAsync(RecordingUpdateUi ui, string serverDir, string? steamGameFolder = null)
        {
            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            await FileDownloader.StartUpdateAsync(worker, ui, full: true, startGame: false, serverDir,
                ownExecutableName: "test-launcher.exe", maxConcurrentDownloads: 3,
                steamGameFolder: steamGameFolder, session: null);
        }

        private sealed class TempClient : IDisposable
        {
            public string Path { get; } = Directory.CreateTempSubdirectory("odinsons-gm-").FullName;
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        [Theory, MemberData(nameof(AllOs))]
        public async Task ThisOs_PicksItsOwnManifest_NotAnotherPlatformsGameInfo(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");

            // A "wrong OS" game file listed under game.info that must never be pulled.
            string wrongOsFile = os == TargetOs.Windows ? "valheim.x86_64" : "valheim.exe";
            pack.AddFile(wrongOsFile, "WRONG OS GAME FILE");
            pack.AddFile("valheim_Data/data.bin", "game data");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("game.info", wrongOsFile, "valheim_Data/data.bin");
            pack.WriteEmptyManifest("optional.info");

            // The right-OS manifest lists a game file this OS could actually run.
            string[] rightOsPaths = pack.AddGameExecutable("RIGHT OS GAME FILE");
            if (TestPack.GameManifestName != "game.info")
                pack.WriteManifest(TestPack.GameManifestName, rightOsPaths.Append("valheim_Data/data.bin").ToArray());

            using var server = new TestServer(pack.Root);
            using var client = new TempClient();
            await RunAsync(new RecordingUpdateUi(client.Path, wrongOsFile), server.BaseUrl);

            if (os == TargetOs.Windows)
                Assert.True(File.Exists(Path.Combine(client.Path, "valheim.x86_64")));
            else
                // The Windows game.info is ignored; its valheim.exe is never downloaded.
                Assert.False(File.Exists(Path.Combine(client.Path, "valheim.exe")));

            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/ReqMod/ReqMod.dll")));
        }

        [Theory, MemberData(nameof(UnixOs))]
        public async Task NonWindows_ServerHasOnlyWindowsGameInfo_NoGameFilesArePulled(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("valheim.exe", "WINDOWS GAME");
            pack.AddFile("valheim_Data/data.bin", "windows game data");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("game.info", "valheim.exe", "valheim_Data/data.bin");
            pack.WriteEmptyManifest("optional.info");
            // No game_macos.info / game_linux.info published.

            using var server = new TestServer(pack.Root);
            using var client = new TempClient();
            await RunAsync(new RecordingUpdateUi(client.Path, "valheim.exe"), server.BaseUrl);

            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/ReqMod/ReqMod.dll")));
            Assert.False(File.Exists(Path.Combine(client.Path, "valheim.exe")));
            Assert.False(File.Exists(Path.Combine(client.Path, "valheim_Data/data.bin")));
        }

        [Theory, MemberData(nameof(AllOs))]
        public async Task ThisOs_MatchingSteamCopy_InjectorActivatesFromTheOsManifest(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            string doorstopPrereq = pack.AddDoorstopPrereq();
            string[] gamePaths = pack.AddGameExecutable("MATCHING GAME");
            pack.AddFile("valheim_Data/data.bin", "matching game data");

            pack.WriteManifest("update.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq);
            pack.WriteManifest("update_admin.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq);
            pack.WriteGameManifest(gamePaths.Append("valheim_Data/data.bin").ToArray());
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClient();

            using var steam = new TestPack();
            steam.AddGameExecutable("MATCHING GAME");
            steam.AddFile("valheim_Data/data.bin", "matching game data");

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, steamGameFolder: steam.Root);

            Assert.NotNull(ui.LastInjectorPlan);
            Assert.Equal(TestPack.GameExecutablePath(steam.Root), ui.LastInjectorPlan!.Executable);
            foreach (string p in gamePaths)
                Assert.False(File.Exists(Path.Combine(client.Path, p.Replace('/', Path.DirectorySeparatorChar))));
        }
    }
}
