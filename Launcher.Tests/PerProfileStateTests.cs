using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class PerProfileStateTests : IDisposable
    {
        private static readonly ServerProfile Other = new("other", "Other", new[] { "https://mirror.test/" });

        private readonly string _dir = Directory.CreateTempSubdirectory("odinsons-profiles-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private string ConfigPath => Path.Combine(_dir, "config.ini");

        private IniFile LoadConfig()
        {
            var config = new IniFile(ConfigPath);
            config.Load();
            return config;
        }

        [Fact]
        public void A2xLayout_IsReadByTheBuiltInProfile_AndNothingMoves()
        {
            string client = Path.Combine(_dir, "clients", "Lite_v2");
            Directory.CreateDirectory(client);
            File.WriteAllText(ConfigPath,
                "[Settings]\nSelectedServer=Lite_v2\n\n[Server:Lite_v2]\nProfilePath=" + client + "\n");
            new TestPack(client).AddFile("BepInEx/plugins/a.dll", "a")
                .WriteManifest(ClientLedger.FileName, "BepInEx/plugins/a.dll");
            File.WriteAllLines(Path.Combine(client, OptionalModSelection.FileName), new[] { "ModA", "-ModB" });
            string[] before = Snapshot(_dir);

            IniFile config = LoadConfig();
            string server = LauncherPaths.SelectedServer(config, ServerProfile.Default);
            string folder = LauncherPaths.ProfileFolder(config, ServerProfile.Default, server);
            ClientLedger ledger = ClientLedger.Load(folder);
            OptionalModSelection selection = OptionalModSelection.Load(folder);

            Assert.Equal("Lite_v2", server);
            Assert.Equal(client, folder);
            Assert.True(ledger.TryGetLastConfirmedHash("BepInEx/plugins/a.dll", out _));
            Assert.True(selection.IsSelected("ModA"));
            Assert.False(selection.IsEnabled("ModB", enabledByDefault: true));
            Assert.Equal(before, Snapshot(_dir));
        }

        [Fact]
        public void A2xLayoutWithoutExplicitPaths_StillResolvesToTheSameFolder()
        {
            File.WriteAllText(ConfigPath, "[Settings]\nSelectedServer=Lite_v2\n");

            IniFile config = LoadConfig();

            Assert.Equal(Path.Combine("clients", "Lite_v2"),
                LauncherPaths.ProfileFolder(config, ServerProfile.Default, "Lite_v2"));
        }

        [Fact]
        public void AnotherProfile_GetsItsOwnFolderAndSelection()
        {
            IniFile config = LoadConfig();
            LauncherPaths.SetSelectedServer(config, ServerProfile.Default, "Lite_v2");
            LauncherPaths.SetSelectedServer(config, Other, "Lite_v2");
            LauncherPaths.SetProfileFolder(config, Other, "Lite_v2", Path.Combine(_dir, "custom"));

            config = LoadConfig();

            Assert.Equal(Path.Combine("clients", "other", "Lite_v2"), LauncherPaths.DefaultProfileFolder(Other, "Lite_v2"));
            Assert.Equal(Path.Combine(_dir, "custom"), LauncherPaths.ProfileFolder(config, Other, "Lite_v2"));
            Assert.Equal(Path.Combine("clients", "Lite_v2"), LauncherPaths.ProfileFolder(config, ServerProfile.Default, "Lite_v2"));
        }

        [Fact]
        public void TheSelectedServer_IsRememberedPerProfile()
        {
            IniFile config = LoadConfig();
            LauncherPaths.SetSelectedServer(config, ServerProfile.Default, "Lite_v2");
            LauncherPaths.SetSelectedServer(config, Other, "Hardcore");

            config = LoadConfig();

            Assert.Equal("Lite_v2", LauncherPaths.SelectedServer(config, ServerProfile.Default));
            Assert.Equal("Hardcore", LauncherPaths.SelectedServer(config, Other));
        }

        private static string[] Snapshot(string root) =>
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => f + ":" + FileHash.OfFile(f))
                .ToArray();
    }
}
