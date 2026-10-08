using System.ComponentModel;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class GameBaseTests
    {
        private const string ServerDir = "https://mirror.example/Launcher/Lite_v2/";

        [Fact]
        public void RelativeBase_ResolvesAgainstTheServerDirectory()
        {
            string? url = GameBaseUrl.Resolve("../Game/892972_123/", ServerDir, out string? problem);

            Assert.Equal("https://mirror.example/Launcher/Game/892972_123/", url);
            Assert.Null(problem);
        }

        [Fact]
        public void BaseWithoutTrailingSlash_GetsOne()
        {
            Assert.Equal("https://mirror.example/Launcher/Game/892972_123/",
                GameBaseUrl.Resolve("../Game/892972_123", ServerDir, out _));
        }

        [Fact]
        public void MissingBase_MeansNoSeparateLocation()
        {
            Assert.Null(GameBaseUrl.Resolve(null, ServerDir, out string? problem));
            Assert.Null(GameBaseUrl.Resolve("  ", ServerDir, out _));
            Assert.Null(problem);
        }

        [Theory]
        [InlineData("https://evil.example/Game/")]
        [InlineData("//evil.example/Game/")]
        [InlineData("http://mirror.example/Launcher/Game/")]
        public void BasePointingOutsideTheServerHost_IsRejected(string directive)
        {
            Assert.Null(GameBaseUrl.Resolve(directive, ServerDir, out string? problem));
            Assert.NotNull(problem);
        }

        [Fact]
        public void Directive_IsReadFromTheHeader_AndSkippedByTheEntryParser()
        {
            string text = "MANIFEST 3 sha256\n# base: ../Game/892972_123/\nabc 5 valheim.exe\n";

            Assert.Equal("../Game/892972_123/", Manifest.ReadDirective(new StringReader(text), "base"));

            List<Manifest.Entry> entries = Manifest.Read(new StringReader(text));
            Assert.Single(entries);
            Assert.Equal("valheim.exe", entries[0].Path);
        }

        [Fact]
        public void Directive_AfterTheFirstEntry_IsNotPickedUp()
        {
            string text = "MANIFEST 3 sha256\nabc 5 valheim.exe\n# base: ../Game/x/\n";

            Assert.Null(Manifest.ReadDirective(new StringReader(text), "base"));
        }

        [Fact]
        public async Task GameFiles_AreDownloadedFromTheBase_ModsStayOnTheServerDirectory()
        {
            string web = Directory.CreateTempSubdirectory("odinsons-web-").FullName;
            try
            {
                using var mods = new TestPack(Path.Combine(web, "Lite_v2"));
                using var game = new TestPack(Path.Combine(web, "Game", "892972_123"));

                mods.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
                mods.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
                mods.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
                mods.WriteEmptyManifest("optional.info");

                string[] gamePaths = game.AddGameExecutable("GAME EXE").Append("valheim_Data/data.bin").ToArray();
                game.AddFile("valheim_Data/data.bin", "game data");
                mods.WriteGameManifest("../Game/892972_123/", game, gamePaths);

                using var server = new TestServer(web);
                string clientPath = Directory.CreateTempSubdirectory("odinsons-client-").FullName;
                try
                {
                    var ui = new RecordingUpdateUi(clientPath);
                    var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

                    await FileDownloader.StartUpdateAsync(worker, ui, full: true, startGame: false,
                        server.BaseUrl + "Lite_v2/", ownExecutableName: "test-launcher.exe",
                        maxConcurrentDownloads: 3, steamGameFolder: null, session: null);

                    Assert.True(File.Exists(Path.Combine(clientPath, "BepInEx/plugins/ReqMod/ReqMod.dll")));
                    Assert.True(File.Exists(Path.Combine(clientPath, "valheim_Data/data.bin")));
                    Assert.True(File.Exists(TestPack.GameExecutablePath(clientPath)));
                    Assert.True(ui.CompleteCalled);
                }
                finally
                {
                    try { Directory.Delete(clientPath, recursive: true); } catch { }
                }
            }
            finally
            {
                try { Directory.Delete(web, recursive: true); } catch { }
            }
        }
    }
}
