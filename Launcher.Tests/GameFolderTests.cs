using System.ComponentModel;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// The game in a folder of its own, apart from the client (profile) folder that holds the mods.
    /// </summary>
    public sealed class GameFolderTests
    {
        public static readonly TheoryData<TargetOs> AllOs = new() { TargetOs.Windows, TargetOs.MacOS, TargetOs.Linux };

        private static async Task RunAsync(RecordingUpdateUi ui, string serverDir, string? gameFolder,
                                           string? steamGameFolder = null, UpdateSession? session = null)
        {
            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            await FileDownloader.StartUpdateAsync(worker, ui, full: true, startGame: false, serverDir,
                ownExecutableName: "test-launcher.exe", maxConcurrentDownloads: 3,
                steamGameFolder: steamGameFolder, session: session, gameFolder: gameFolder);
        }

        private sealed class TempFolder : IDisposable
        {
            public string Path { get; } = Directory.CreateTempSubdirectory("odinsons-gf-").FullName;
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private sealed record Server(TestPack Pack, string[] GamePaths, string DoorstopPrereq) : IDisposable
        {
            public void Dispose() => Pack.Dispose();
        }

        private static Server BuildServer(string gameContent = "GAME EXE", string dataContent = "game data")
        {
            var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            string doorstopPrereq = pack.AddDoorstopPrereq();
            string[] gamePaths = pack.AddGameExecutable(gameContent).Append("valheim_Data/data.bin").ToArray();
            pack.AddFile("valheim_Data/data.bin", dataContent);

            string[] mods = { "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq };
            pack.WriteManifest("update.info", mods);
            pack.WriteManifest("update_admin.info", mods);
            pack.WriteGameManifest(gamePaths);
            pack.WriteEmptyManifest("optional.info");

            return new Server(pack, gamePaths, doorstopPrereq);
        }

        private static string In(string folder, string relativePath) =>
            Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        [Theory, MemberData(nameof(AllOs))]
        public async Task GameFiles_LandInTheGameFolder_ModsInTheClientFolder_AndTheGameStartsFromThere(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using Server s = BuildServer();
            using var testServer = new TestServer(s.Pack.Root);
            using var client = new TempFolder();
            using var game = new TempFolder();

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, testServer.BaseUrl, gameFolder: game.Path);

            foreach (string path in s.GamePaths)
            {
                Assert.True(File.Exists(In(game.Path, path)), $"{path} should be in the game folder");
                Assert.False(File.Exists(In(client.Path, path)), $"{path} should not be in the client folder");
            }

            Assert.True(File.Exists(In(client.Path, "BepInEx/plugins/ReqMod/ReqMod.dll")));
            Assert.True(File.Exists(In(client.Path, "BepInEx/core/BepInEx.Preloader.dll")));

            Assert.True(ui.CanStartGameAtComplete);
            Assert.NotNull(ui.LastInjectorPlan);
            Assert.Equal(TestPack.GameExecutablePath(game.Path), ui.LastInjectorPlan!.Executable);
        }

        [Theory, MemberData(nameof(AllOs))]
        public async Task ASecondRun_FindsTheGameFolderComplete_AndStillLaunchesFromIt(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using Server s = BuildServer();
            using var testServer = new TestServer(s.Pack.Root);
            using var client = new TempFolder();
            using var game = new TempFolder();

            await RunAsync(new RecordingUpdateUi(client.Path), testServer.BaseUrl, gameFolder: game.Path);

            var second = new RecordingUpdateUi(client.Path);
            await RunAsync(second, testServer.BaseUrl, gameFolder: game.Path);

            Assert.True(second.CanStartGameAtComplete);
            Assert.Equal(TestPack.GameExecutablePath(game.Path), second.LastInjectorPlan!.Executable);
            Assert.False(Directory.GetFiles(client.Path, "valheim*", SearchOption.AllDirectories)
                .Any(f => !f.EndsWith(".cache")));
        }

        [Theory, MemberData(nameof(AllOs))]
        public async Task ASteamCopyStillSuppliesGameFiles_ButIntoTheGameFolder(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using Server s = BuildServer();
            using var testServer = new TestServer(s.Pack.Root);
            using var client = new TempFolder();
            using var game = new TempFolder();

            using var steam = new TestPack();
            steam.AddGameExecutable("GAME EXE");
            steam.AddFile("valheim_Data/data.bin", "game data");

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, testServer.BaseUrl, gameFolder: game.Path, steamGameFolder: steam.Root);

            Assert.True(File.Exists(In(game.Path, "valheim_Data/data.bin")));
            Assert.False(File.Exists(In(client.Path, "valheim_Data/data.bin")));
            Assert.Equal(TestPack.GameExecutablePath(game.Path), ui.LastInjectorPlan!.Executable);
        }

        [Fact]
        public async Task TheSteamFolderAsTheGameFolder_IsNeverWrittenTo()
        {
            using Server s = BuildServer(gameContent: "GAME EXE v2", dataContent: "game data v2");
            using var testServer = new TestServer(s.Pack.Root);
            using var client = new TempFolder();

            using var steam = new TestPack();
            steam.AddGameExecutable("GAME EXE v1");
            steam.AddFile("valheim_Data/data.bin", "game data v1");

            await RunAsync(new RecordingUpdateUi(client.Path), testServer.BaseUrl,
                gameFolder: steam.Root, steamGameFolder: steam.Root);

            Assert.Equal("game data v1", File.ReadAllText(In(steam.Root, "valheim_Data/data.bin")));
            Assert.Equal("game data v2", File.ReadAllText(In(client.Path, "valheim_Data/data.bin")));
        }

        [Fact]
        public void TheGameExecutable_IsStashedInTheGameFolder_AndComesBackBeforeTheUpdateReportsReady()
        {
            using var client = new TempFolder();
            using var game = new TempFolder();
            string exe = Path.Combine(game.Path, "valheim.exe");
            File.WriteAllText(exe, "GAME EXE");

            Assert.True(UpdateSession.TryBegin(client.Path, out UpdateSession? session, out _, gameFolder: game.Path));
            using (session)
            {
                Assert.False(File.Exists(exe));
                Assert.True(File.Exists(exe + UpdateSession.StashSuffix));

                session!.EndUpdate();

                Assert.True(File.Exists(exe));
                Assert.False(File.Exists(exe + UpdateSession.StashSuffix));
            }
        }
    }
}
