using System.ComponentModel;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// Scenarios that, during the launcher's development, used to be run by hand on
    /// one-off test rigs — an HTTP server over a fixture, a run, checking the client
    /// folder. Every test here is a regression for a specific case where behavior once
    /// silently diverged from expected, with no error and no message to the player.
    /// </summary>
    public sealed class UpdateScenarioTests
    {
        private static async Task RunAsync(RecordingUpdateUi ui, string serverDir, bool full = false,
                                           string? steamGameFolder = null, UpdateSession? session = null)
        {
            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            await FileDownloader.StartUpdateAsync(worker, ui, full, startGame: false, serverDir,
                ownExecutableName: "test-launcher.exe", maxConcurrentDownloads: 3,
                steamGameFolder: steamGameFolder, session: session);
        }

        [Fact]
        public async Task FreshInstall_GetsRequiredAndGameFiles_SkipsOptionalAndAdmin()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/OptMod/OptMod.dll", "optional v1");
            pack.AddFile("BepInEx/plugins/AdminMod/EasySpawner.dll", "admin tool");
            pack.AddFile("valheim.exe", "GAME EXE");
            pack.AddFile("valheim_Data/data.bin", "game data");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/plugins/AdminMod/EasySpawner.dll");
            pack.WriteGameManifest("valheim.exe", "valheim_Data/data.bin");
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            var ui = new RecordingUpdateUi(client.Path);

            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/ReqMod/ReqMod.dll")));

            // Regression: game.info used to be only a marker "can be taken from Steam"
            // and wasn't folded into the general check list — game files either never
            // downloaded at all, or were deleted immediately as "extra", since they
            // weren't in any manifest the launcher actually checked.
            Assert.True(File.Exists(Path.Combine(client.Path, "valheim.exe")));
            Assert.True(File.Exists(Path.Combine(client.Path, "valheim_Data/data.bin")));

            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/OptMod/OptMod.dll")));
            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/AdminMod/EasySpawner.dll")));

            Assert.True(ui.CompleteCalled);
            Assert.True(ui.CanStartGameAtComplete);
        }

        [Fact]
        public async Task OptionalMod_SurvivesUpdate_ButUntrackedFileIsRemoved()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/OptMod/OptMod.dll", "optional v1");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteManifest("optional.info", "BepInEx/plugins/OptMod/OptMod.dll");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();

            // The player already installed the optional mod themselves, plus there's a
            // stray file sitting there that isn't in any manifest.
            client.AddFile("BepInEx/plugins/OptMod/OptMod.dll", "optional v1");
            client.AddFile("BepInEx/plugins/Stray.dll", "a file nobody knows about");

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            // optional.info protects from deletion but doesn't push the mod onto players who
            // don't have it — this is a regression of the rule "the path is written only in
            // optional_patterns.txt, and that's enough": it used to also require duplicating
            // the path in the exclusion list, and a forgotten duplicate meant the mod either
            // got deleted everywhere or force-installed for everyone.
            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/OptMod/OptMod.dll")));
            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/Stray.dll")));
        }

        [Fact]
        public async Task AdminMarker_SwitchesToAdminManifest_BringsAdminOnlyMods()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/AdminMod/EasySpawner.dll", "admin tool");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/plugins/AdminMod/EasySpawner.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            client.AddFile("admin", string.Empty); // admin marker

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/AdminMod/EasySpawner.dll")));
        }

        [Fact]
        public async Task ForceCheck_RepairsServerOwnedConfig_LeavesPlayerConfigAlone()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/config/server.cfg", "server v2"); // set by the server
            pack.AddFile("BepInEx/config/player.cfg", "default value"); // the player may override this

            pack.WriteManifest("update.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/config/server.cfg", "BepInEx/config/player.cfg");
            pack.WriteManifest("update_admin.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/config/server.cfg", "BepInEx/config/player.cfg");
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");
            pack.WriteForceCheck("BepInEx/config/server.cfg");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            client.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            client.AddFile("BepInEx/config/server.cfg", "CORRUPTED by the player");
            client.AddFile("BepInEx/config/player.cfg", "the player's settings");
            client.AddFile("BepInEx/LogOutput.log", "log from a previous run"); // not a full check

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: false);

            // force_check_files.txt exempts server.cfg from the general rule "don't touch
            // BepInEx/config during an ordinary check" — it must revert to the server's value.
            Assert.Equal("server v2", File.ReadAllText(Path.Combine(client.Path, "BepInEx/config/server.cfg")));

            // player.cfg still falls under that same general rule and stays untouched —
            // that's exactly why force_check_files.txt doesn't check everything.
            Assert.Equal("the player's settings", File.ReadAllText(Path.Combine(client.Path, "BepInEx/config/player.cfg")));
        }

        [Fact]
        public async Task StashedExecutable_IsRestoredBeforeCompletionIsReported()
        {
            using var pack = new TestPack();
            pack.AddFile("valheim.exe", "GAME EXE");
            pack.WriteManifest("update.info");
            pack.WriteManifest("update_admin.info");
            pack.WriteGameManifest("valheim.exe");
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            client.AddFile("valheim.exe", "GAME EXE"); // already current, no need to update
            client.AddFile("BepInEx/LogOutput.log", "log from a previous run");

            Assert.True(UpdateSession.TryBegin(client.Path, out UpdateSession session, out _));

            // TryBegin has already moved the file aside for the duration of the update.
            Assert.False(File.Exists(Path.Combine(client.Path, "valheim.exe")));
            Assert.True(File.Exists(Path.Combine(client.Path, "valheim.exe.updating")));

            var ui = new RecordingUpdateUi(client.Path);

            using (session)
                await RunAsync(ui, server.BaseUrl, full: false, session: session);

            // Regression: EndUpdate used to be called in the using block's Dispose — i.e.
            // AFTER the completion report. OnUpdateComplete saw valheim.exe still stashed
            // under .updating and the launcher tried to start the game at a path that
            // didn't exist. Now the restore happens before the report.
            Assert.True(ui.WatchedFileExistedAtComplete);
            Assert.True(File.Exists(Path.Combine(client.Path, "valheim.exe")));
            Assert.False(File.Exists(Path.Combine(client.Path, "valheim.exe.updating")));
        }

        [Fact]
        public async Task FileThatDriftsAfterBeingConfirmedGood_IsFlaggedAsSuspicious()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            string dllPath = Path.Combine(client.Path, "BepInEx/plugins/ReqMod/ReqMod.dll");
            client.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1"); // already correct

            // First run: the file already matches — this just confirms it in the ledger.
            var firstRun = new RecordingUpdateUi(client.Path);
            await RunAsync(firstRun, server.BaseUrl, full: true);
            Assert.Null(firstRun.LastDegradationWarning);

            // Something corrupted the file on disk between runs — the server didn't change it.
            File.WriteAllText(dllPath, "CORRUPTED by an antivirus");

            var secondRun = new RecordingUpdateUi(client.Path);
            await RunAsync(secondRun, server.BaseUrl, full: false);

            // The file gets fixed by a normal download, but on top of that this should be
            // recognized as suspicious: the same path, the same target hash that was already
            // confirmed — and now it diverged again.
            Assert.Equal("required v1", File.ReadAllText(dllPath));
            Assert.NotNull(secondRun.LastDegradationWarning);
        }

        public static readonly TheoryData<TargetOs> AllOs = new() { TargetOs.Windows, TargetOs.MacOS, TargetOs.Linux };

        [Theory, MemberData(nameof(AllOs))]
        public async Task InjectorMode_ActivatesOnTheVeryFirstRun_NeverDuplicatesGameFiles(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            string doorstopPrereq = pack.AddDoorstopPrereq();
            string[] gamePaths = pack.AddGameExecutable("GAME EXE");
            pack.AddFile("valheim_Data/data.bin", "game data");

            pack.WriteManifest("update.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq);
            pack.WriteManifest("update_admin.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq);
            pack.WriteGameManifest(gamePaths.Append("valheim_Data/data.bin").ToArray());
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder(); // completely empty — nothing installed

            // Plays the role of the Steam install — the same game files as the build.
            using var steam = new TestPack();
            steam.AddGameExecutable("GAME EXE");
            steam.AddFile("valheim_Data/data.bin", "game data");

            // Regression: injector mode used to require BepInEx.Preloader.dll to already be
            // sitting in the client folder BEFORE the run started, meaning on the very first
            // run (when it isn't there yet) game files would still get duplicated into the
            // client folder. The whole point of the injector is to never pull game files
            // twice from the network/disk at all, so a missing preloader must be fetched
            // ahead of time, within this same run.
            var firstRun = new RecordingUpdateUi(client.Path);
            await RunAsync(firstRun, server.BaseUrl, full: true, steamGameFolder: steam.Root);

            Assert.NotNull(firstRun.LastInjectorPlan);
            Assert.Equal(TestPack.GameExecutablePath(steam.Root), firstRun.LastInjectorPlan.Executable);
            foreach (string p in gamePaths)
                Assert.False(File.Exists(Path.Combine(client.Path, p.Replace('/', Path.DirectorySeparatorChar))));
            Assert.False(File.Exists(Path.Combine(client.Path, "valheim_Data/data.bin")));

            // The mod's own files (preloader + the native doorstop bits) still needed to be
            // downloaded — without them the injector itself has nothing to preload.
            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/core/BepInEx.Preloader.dll")));
            Assert.True(File.Exists(Path.Combine(client.Path, doorstopPrereq.Replace('/', Path.DirectorySeparatorChar))));
        }

        [Theory, MemberData(nameof(AllOs))]
        public async Task InjectorMode_SteamBuildDoesNotMatchServer_FallsBackAndDownloadsCorrectFiles(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            string doorstopPrereq = pack.AddDoorstopPrereq();
            string[] gamePaths = pack.AddGameExecutable("GAME EXE v2");
            pack.AddFile("valheim_Data/data.bin", "game data v2");

            pack.WriteManifest("update.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq);
            pack.WriteManifest("update_admin.info",
                "BepInEx/plugins/ReqMod/ReqMod.dll", "BepInEx/core/BepInEx.Preloader.dll", doorstopPrereq);
            pack.WriteGameManifest(gamePaths.Append("valheim_Data/data.bin").ToArray());
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();

            // The player's Steam copy hasn't updated to the server's current build — it
            // matters that the injector doesn't silently launch the game with the wrong
            // files, but falls back to the ordinary check and downloads exactly what the
            // server expects.
            using var steam = new TestPack();
            steam.AddGameExecutable("GAME EXE v1 (stale build)");
            steam.AddFile("valheim_Data/data.bin", "game data v1 (stale build)");

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true, steamGameFolder: steam.Root);

            Assert.Null(ui.LastInjectorPlan);
            Assert.Equal("game data v2", File.ReadAllText(Path.Combine(client.Path, "valheim_Data/data.bin")));
            foreach (string p in gamePaths.Where(p => !p.EndsWith(".plist")))
                Assert.Equal("GAME EXE v2", File.ReadAllText(
                    Path.Combine(client.Path, p.Replace('/', Path.DirectorySeparatorChar))));
        }

        [Fact]
        public async Task SelectedOptionalMod_MissingFile_GetsDownloaded()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteManifest("optional.info", "BepInEx/plugins/VNEI/VNEI.dll");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();

            // The player turned the mod on via the mods panel — recorded by its plugin
            // folder name, the same identity ModGrouping uses to group optional.info entries.
            client.AddFile(OptionalModSelection.FileName, "VNEI\n");

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            // Regression: optional files used to download only if the player already had
            // them — there was no way to opt into a mod you didn't already have installed.
            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/VNEI/VNEI.dll")));
        }

        [Fact]
        public async Task UnselectedOptionalMod_MissingFile_StaysAbsent()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteManifest("optional.info", "BepInEx/plugins/VNEI/VNEI.dll");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            // No optional_selected.txt at all — the player never turned VNEI on.

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/VNEI/VNEI.dll")));
        }

        [Fact]
        public async Task DeselectedOptionalMod_InstalledFile_GetsUninstalled()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteManifest("optional.info", "BepInEx/plugins/VNEI/VNEI.dll");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            client.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");
            // The player turned the mod off in the mods panel — "-" prefix, not just absent.
            client.AddFile(OptionalModSelection.FileName, "-VNEI\n");

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/VNEI/VNEI.dll")));
            // The now-empty plugin folder is cleaned up along with the file.
            Assert.False(Directory.Exists(Path.Combine(client.Path, "BepInEx/plugins/VNEI")));
        }

        [Fact]
        public async Task NeverToggledOptionalMod_FoundInstalled_IsAdoptedAsSelected()
        {
            // A mod the player brought themselves before the mods panel existed — never
            // mentioned in optional_selected.txt at all. Must not be deleted, and must stop
            // being a mystery to the panel: found on disk -> recorded as selected, so the
            // toggle shows it ON (matching reality) instead of lying that it's off.
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteManifest("optional.info", "BepInEx/plugins/VNEI/VNEI.dll");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            client.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");
            // No optional_selected.txt at all.

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.True(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/VNEI/VNEI.dll")));

            OptionalModSelection adopted = OptionalModSelection.Load(client.Path);
            Assert.True(adopted.IsSelected("VNEI"));
        }

        [Fact]
        public async Task NeverInstalledOptionalMod_StaysUnknown_NoSelectionFileWritten()
        {
            // The flip side of adoption: a mod that was never installed and never toggled
            // gets no entry at all — only presence-on-disk triggers adoption.
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.AddFile("BepInEx/plugins/VNEI/VNEI.dll", "vnei contents");

            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteManifest("update_admin.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyGameManifest();
            pack.WriteManifest("optional.info", "BepInEx/plugins/VNEI/VNEI.dll");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();
            // VNEI is neither on disk nor mentioned in optional_selected.txt.

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/VNEI/VNEI.dll")));

            OptionalModSelection selection = OptionalModSelection.Load(client.Path);
            Assert.False(selection.IsKnown("VNEI"));
        }

        [Fact]
        public async Task CorruptManifest_FailsClosed_DoesNotAllowGameToStart()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "req-mod-content");
            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyManifest("update_admin.info");
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");
            // Overwrite with a file that isn't a manifest at all, simulating the
            // stale pre-1.3.0 binary format (or any other unreadable update.info).
            File.WriteAllText(Path.Combine(pack.Root, "update.info"), "not a manifest at all");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();

            var ui = new RecordingUpdateUi(client.Path);
            await RunAsync(ui, server.BaseUrl, full: true);

            Assert.False(ui.CanStartGameAtComplete);
        }

        [Fact]
        public async Task CancelledPartwayThrough_FailsClosed_DoesNotAllowGameToStart()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyManifest("update_admin.info");
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder(); // nothing installed yet — a download would be required

            // Cancelling the instant the client-file check step starts is the earliest point a
            // real "Прервать проверку" click could land — and, before the fix, was silently
            // ignored: none of WorkAsync's cancellation early-exits touched CanStartGame, which
            // defaults true, so a cancelled run could still let the (unchecked) game launch.
            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            var ui = new RecordingUpdateUi(client.Path)
            {
                Worker = worker,
                CancelOnStepLabel = Loc.T("dl.step.clientCheck")
            };

            await FileDownloader.StartUpdateAsync(worker, ui, full: true, startGame: false, server.BaseUrl,
                ownExecutableName: "test-launcher.exe", maxConcurrentDownloads: 3);

            Assert.True(ui.CompleteCalled);
            Assert.False(ui.CanStartGameAtComplete);
            Assert.False(File.Exists(Path.Combine(client.Path, "BepInEx/plugins/ReqMod/ReqMod.dll")));
        }

        [Fact]
        public async Task CancelledPartwayThrough_DoesNotReportTheFinalizeStepAsStartedOrFinished()
        {
            using var pack = new TestPack();
            pack.AddFile("BepInEx/plugins/ReqMod/ReqMod.dll", "required v1");
            pack.WriteManifest("update.info", "BepInEx/plugins/ReqMod/ReqMod.dll");
            pack.WriteEmptyManifest("update_admin.info");
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");

            using var server = new TestServer(pack.Root);
            using var client = new TempClientFolder();

            // EndUpdate (restoring the stashed exe) still runs on a cancelled run — only the
            // step-list reporting around it is skipped. Before the fix, StartStep/FinishStep
            // for "Finishing up" ran unconditionally, so a cancelled download showed that later
            // step as done while an earlier one (Downloading) was still sitting there interrupted.
            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            var ui = new RecordingUpdateUi(client.Path)
            {
                Worker = worker,
                CancelOnStepLabel = Loc.T("dl.step.clientCheck")
            };

            await FileDownloader.StartUpdateAsync(worker, ui, full: true, startGame: false, server.BaseUrl,
                ownExecutableName: "test-launcher.exe", maxConcurrentDownloads: 3);

            Assert.True(ui.CompleteCalled);
            Assert.DoesNotContain(Loc.T("dl.step.finalize"), ui.StartedStepLabels);
            Assert.DoesNotContain(Loc.T("dl.step.finalize"), ui.FinishedStepLabels);
        }

        /// <summary>A temporary client folder with a few files, for setting up an "already installed" state.</summary>
        private sealed class TempClientFolder : IDisposable
        {
            public string Path { get; } = Directory.CreateTempSubdirectory("odinsons-client-").FullName;

            public void AddFile(string relativePath, string content)
            {
                string full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); } catch { /* temp folder, not critical */ }
            }
        }
    }
}
