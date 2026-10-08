using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class LauncherPathsTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("odinsons-paths-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private string ConfigPath => Path.Combine(_dir, "config.ini");
        private string ProfileDir => Path.Combine(_dir, "profile");
        private string GameDir => Path.Combine(_dir, "game");

        private IniFile LoadConfig()
        {
            var config = new IniFile(ConfigPath);
            config.Load();
            return config;
        }

        [Fact]
        public void NothingSet_MeansTheDefaults()
        {
            IniFile config = LoadConfig();

            Assert.Equal(Path.Combine("clients", "Lite_v2"), LauncherPaths.ProfileFolder(config, "Lite_v2"));
            Assert.Null(LauncherPaths.GameFolder(config));
        }

        [Fact]
        public void TheProfileFolder_IsPerServer_AndSurvivesARestart()
        {
            IniFile config = LoadConfig();
            LauncherPaths.SetProfileFolder(config, "Lite_v2", ProfileDir);

            IniFile reloaded = LoadConfig();

            Assert.Equal(ProfileDir, LauncherPaths.ProfileFolder(reloaded, "Lite_v2"));
            Assert.Equal(Path.Combine("clients", "Other"), LauncherPaths.ProfileFolder(reloaded, "Other"));
        }

        [Fact]
        public void TheGameFolder_IsOneForAllServers_AndSurvivesARestart()
        {
            IniFile config = LoadConfig();
            LauncherPaths.SetGameFolder(config, GameDir);

            Assert.Equal(GameDir, LauncherPaths.GameFolder(LoadConfig()));
        }

        [Fact]
        public void AnEmptyValue_GoesBackToTheDefault()
        {
            IniFile config = LoadConfig();
            LauncherPaths.SetProfileFolder(config, "Lite_v2", ProfileDir);
            LauncherPaths.SetGameFolder(config, GameDir);

            LauncherPaths.SetProfileFolder(config, "Lite_v2", "");
            LauncherPaths.SetGameFolder(config, null);

            IniFile reloaded = LoadConfig();
            Assert.Equal(Path.Combine("clients", "Lite_v2"), LauncherPaths.ProfileFolder(reloaded, "Lite_v2"));
            Assert.Null(LauncherPaths.GameFolder(reloaded));
        }

        [Fact]
        public void TheOtherSettings_AreLeftAlone()
        {
            IniFile config = LoadConfig();
            config.Write("SelectedServer", "Lite_v2", "Settings");

            LauncherPaths.SetGameFolder(config, GameDir);

            Assert.Equal("Lite_v2", LoadConfig().Read("SelectedServer", "Settings"));
        }

        [Fact]
        public void ANewFolder_IsUsable()
        {
            Assert.True(LauncherPaths.CanUse(Path.Combine(_dir, "new-folder"), out string? reason));
            Assert.Null(reason);
        }

        [Fact]
        public void AnEmptyFolder_IsUsable()
        {
            string folder = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

            Assert.True(LauncherPaths.CanUse(folder, out _));
        }

        [Fact]
        public void AFolderWithSomeoneElsesFiles_IsRefused()
        {
            string folder = Directory.CreateDirectory(Path.Combine(_dir, "documents")).FullName;
            File.WriteAllText(Path.Combine(folder, "taxes.xlsx"), "private");

            Assert.False(LauncherPaths.CanUse(folder, out string? reason));
            Assert.Contains("taxes.xlsx", reason);
        }

        [Fact]
        public void AFolderThatAlreadyHoldsAClient_IsUsable()
        {
            string folder = Directory.CreateDirectory(Path.Combine(_dir, "client")).FullName;
            Directory.CreateDirectory(Path.Combine(folder, "valheim_Data"));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "anything");

            Assert.True(LauncherPaths.CanUse(folder, out _));
        }

        [Fact]
        public void NoPath_IsRefused()
        {
            Assert.False(LauncherPaths.CanUse("", out string? reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void OneFolderInsideTheOther_IsNesting_InEitherOrder()
        {
            string outer = Path.Combine(_dir, "outer");
            string inner = Path.Combine(outer, "inner");

            Assert.True(LauncherPaths.AreNested(outer, inner));
            Assert.True(LauncherPaths.AreNested(inner, outer));
        }

        [Fact]
        public void SiblingsWithACommonPrefix_AreNotNesting()
        {
            Assert.False(LauncherPaths.AreNested(Path.Combine(_dir, "Game"), Path.Combine(_dir, "Game2")));
        }

        [Fact]
        public void TheSameFolder_IsNotNesting_ButIsTheSame()
        {
            string folder = Path.Combine(_dir, "same");

            Assert.False(LauncherPaths.AreNested(folder, folder + Path.DirectorySeparatorChar));
            Assert.True(LauncherPaths.AreSame(folder, folder + Path.DirectorySeparatorChar));
        }
    }
}
