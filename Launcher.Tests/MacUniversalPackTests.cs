using System.ComponentModel;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// The macOS BepInEx 5.4.23.6 pack keeps libdoorstop.dylib and run_bepinex.sh at its root,
    /// next to BepInEx/ and with no doorstop_libs/ — the fixture below mimics that layout.
    /// </summary>
    public sealed class MacUniversalPackTests
    {
        private static readonly string[] UniversalPackFiles =
        {
            "libdoorstop.dylib",
            "run_bepinex.sh",
            "BepInEx/core/BepInEx.dll",
            "BepInEx/core/BepInEx.Preloader.dll",
            "BepInEx/core/0Harmony.dll"
        };

        private const string LegacyLibrary = "doorstop_libs/libdoorstop_x64.dylib";

        private static async Task RunAsync(RecordingUpdateUi ui, string serverDir)
        {
            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            await FileDownloader.StartUpdateAsync(worker, ui, full: true, startGame: false, serverDir,
                ownExecutableName: "test-launcher.exe", maxConcurrentDownloads: 3);
        }

        private static TestPack UniversalPack(params string[] extraFiles)
        {
            var pack = new TestPack();
            string[] all = UniversalPackFiles.Concat(extraFiles).ToArray();

            foreach (string path in all) pack.AddFile(path, "content of " + path);

            pack.WriteManifest("update.info", all);
            pack.WriteManifest("update_admin.info", all);
            pack.WriteEmptyGameManifest();
            pack.WriteEmptyManifest("optional.info");
            return pack;
        }

        private static string In(string folder, string relativePath) =>
            Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string NewClientFolder() => Directory.CreateTempSubdirectory("odinsons-client-").FullName;

        [Theory]
        [InlineData("run_bepinex.sh", true)]
        [InlineData("libdoorstop.dylib", true)]
        [InlineData("doorstop_libs/libdoorstop_x64.so", true)]
        [InlineData("Libs/LIBFOO.DYLIB", true)]
        [InlineData("BepInEx/core/BepInEx.dll", false)]
        [InlineData("doorstop_config.ini", false)]
        [InlineData("BepInEx/config/readme.txt", false)]
        public void NeedsExecuteBit_ForScriptsAndNativeLibrariesOnly(string path, bool expected)
        {
            Assert.Equal(expected, ExecutableBits.NeedsExecuteBit(path));
        }

        [Fact]
        public void Ensure_AddsExecuteBit_OnlyWhereTheFileIsReadable()
        {
            if (OperatingSystem.IsWindows()) return;

            string folder = Directory.CreateTempSubdirectory("odinsons-exec-").FullName;
            string script = Path.Combine(folder, "run_bepinex.sh");
            string plain = Path.Combine(folder, "notes.txt");
            File.WriteAllText(script, "#!/bin/sh");
            File.WriteAllText(plain, "text");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.SetUnixFileMode(plain, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            try
            {
                ExecutableBits.Ensure(script, "run_bepinex.sh");
                ExecutableBits.Ensure(plain, "notes.txt");

                UnixFileMode mode = File.GetUnixFileMode(script);
                Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
                Assert.True(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.False(mode.HasFlag(UnixFileMode.OtherExecute));
                Assert.False(File.GetUnixFileMode(plain).HasFlag(UnixFileMode.UserExecute));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task FreshInstall_DownloadsEveryPackFile_AndMakesTheScriptAndLibraryExecutable()
        {
            using var pack = UniversalPack();
            using var server = new TestServer(pack.Root);
            string client = NewClientFolder();

            try
            {
                var ui = new RecordingUpdateUi(client);
                await RunAsync(ui, server.BaseUrl);

                foreach (string path in UniversalPackFiles)
                    Assert.True(File.Exists(In(client, path)), path);

                Assert.Null(ui.LastDegradationWarning);
                Assert.True(ui.CanStartGameAtComplete);

                if (!OperatingSystem.IsWindows())
                {
                    Assert.True(File.GetUnixFileMode(In(client, "run_bepinex.sh")).HasFlag(UnixFileMode.UserExecute));
                    Assert.True(File.GetUnixFileMode(In(client, "libdoorstop.dylib")).HasFlag(UnixFileMode.UserExecute));
                    Assert.False(File.GetUnixFileMode(In(client, "BepInEx/core/BepInEx.dll")).HasFlag(UnixFileMode.UserExecute));
                }
            }
            finally
            {
                Directory.Delete(client, recursive: true);
            }
        }

        [Fact]
        public async Task ExistingInstallWithoutExecuteBit_GetsItOnTheNextRun()
        {
            if (OperatingSystem.IsWindows()) return;

            using var pack = UniversalPack();
            using var server = new TestServer(pack.Root);
            string client = NewClientFolder();

            try
            {
                await RunAsync(new RecordingUpdateUi(client), server.BaseUrl);

                string script = In(client, "run_bepinex.sh");
                File.SetUnixFileMode(script,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

                var second = new RecordingUpdateUi(client);
                await RunAsync(second, server.BaseUrl);

                Assert.True(File.GetUnixFileMode(script).HasFlag(UnixFileMode.UserExecute));
                Assert.Null(second.LastDegradationWarning);
            }
            finally
            {
                Directory.Delete(client, recursive: true);
            }
        }

        [Fact]
        public async Task LegacyProfile_MovedToUniversalPack_RemovesTheOldLibraryAndKeepsTheNewFiles()
        {
            string client = NewClientFolder();

            try
            {
                using (var legacy = new TestPack())
                {
                    string[] files = { LegacyLibrary, "BepInEx/core/BepInEx.Preloader.dll" };
                    foreach (string path in files) legacy.AddFile(path, "legacy " + path);

                    legacy.WriteManifest("update.info", files);
                    legacy.WriteManifest("update_admin.info", files);
                    legacy.WriteEmptyGameManifest();
                    legacy.WriteEmptyManifest("optional.info");

                    using var legacyServer = new TestServer(legacy.Root);
                    await RunAsync(new RecordingUpdateUi(client), legacyServer.BaseUrl);
                }

                Assert.True(File.Exists(In(client, LegacyLibrary)));

                using var universal = UniversalPack();
                using var universalServer = new TestServer(universal.Root);
                var ui = new RecordingUpdateUi(client);
                await RunAsync(ui, universalServer.BaseUrl);

                foreach (string path in UniversalPackFiles)
                    Assert.True(File.Exists(In(client, path)), path);

                Assert.False(File.Exists(In(client, LegacyLibrary)));
                Assert.False(Directory.Exists(In(client, "doorstop_libs")));
                Assert.Equal("content of BepInEx/core/BepInEx.Preloader.dll",
                    File.ReadAllText(In(client, "BepInEx/core/BepInEx.Preloader.dll")));
                Assert.Null(ui.LastDegradationWarning);
            }
            finally
            {
                Directory.Delete(client, recursive: true);
            }
        }

        [Fact]
        public async Task ProfileWithBothLibraries_KeepsTheLegacyOneAlongsideTheUniversalOne()
        {
            using var pack = UniversalPack(LegacyLibrary);
            using var server = new TestServer(pack.Root);
            string client = NewClientFolder();

            try
            {
                await RunAsync(new RecordingUpdateUi(client), server.BaseUrl);
                await RunAsync(new RecordingUpdateUi(client), server.BaseUrl);

                Assert.True(File.Exists(In(client, LegacyLibrary)));
                Assert.True(File.Exists(In(client, "libdoorstop.dylib")));
            }
            finally
            {
                Directory.Delete(client, recursive: true);
            }
        }

        [Theory]
        [InlineData("libdoorstop.dylib")]
        [InlineData("run_bepinex.sh")]
        [InlineData("doorstop_libs")]
        public void InterruptedInstall_LeavingOnlyAPackRootEntry_IsStillASafeTarget(string leftover)
        {
            string folder = Directory.CreateTempSubdirectory("odinsons-guard-").FullName;

            try
            {
                string full = Path.Combine(folder, leftover);
                if (leftover == "doorstop_libs") Directory.CreateDirectory(full);
                else File.WriteAllText(full, "partial");

                Assert.True(ClientFolderGuard.IsSafeTarget(folder, out _));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
