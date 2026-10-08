using System.IO;
using System.Linq;
using Xunit;

namespace Indexer.Tests
{
    public sealed class GameSourceTests : System.IDisposable
    {
        private readonly string _web = Directory.CreateTempSubdirectory("indexer-web-").FullName;
        private string ProfileFolder => Path.Combine(_web, "Launcher", "Lite_v2");
        private string GameFolder(string version) => Path.Combine(_web, "Game", version);

        public GameSourceTests()
        {
            Directory.CreateDirectory(ProfileFolder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_web, recursive: true); } catch { }
        }

        [Fact]
        public void NothingRemembered_MeansNoSeparateGame()
        {
            Assert.Empty(Indexer.GameSource.Read(ProfileFolder));
        }

        [Fact]
        public void PathsPassedOnce_AreReadBackOnLaterRuns()
        {
            string windows = GameFolder("892972_111");
            string linux = GameFolder("892971_333");

            Indexer.GameSource.Write(ProfileFolder, new[] { windows, linux });

            Assert.Equal(new[] { Path.GetFullPath(windows), Path.GetFullPath(linux) },
                Indexer.GameSource.Read(ProfileFolder));
        }

        [Fact]
        public void TheRememberedPaths_AreStoredRelativeToTheProfileFolder()
        {
            Indexer.GameSource.Write(ProfileFolder, new[] { GameFolder("892972_111") });

            string stored = File.ReadAllText(Path.Combine(ProfileFolder, Indexer.GameSource.FileName)).Trim();
            Assert.Equal("../../Game/892972_111", stored);
        }

        [Fact]
        public void ANewVersionOfADepot_ReplacesTheRememberedOne_AndLeavesTheOtherDepotsAlone()
        {
            var remembered = new[] { GameFolder("892972_111"), GameFolder("892973_222") };

            var merged = Indexer.GameSource.Merge(remembered, new[] { GameFolder("892972_999") });

            Assert.Equal(new[] { GameFolder("892973_222"), GameFolder("892972_999") }, merged);
        }

        [Fact]
        public void ADepotNotRememberedYet_IsAdded()
        {
            var merged = Indexer.GameSource.Merge(new[] { GameFolder("892972_111") }, new[] { GameFolder("892971_333") });

            Assert.Equal(new[] { GameFolder("892972_111"), GameFolder("892971_333") }, merged);
        }

        [Theory]
        [InlineData("892972_111", true)]
        [InlineData("892972", false)]
        [InlineData("latest", false)]
        [InlineData("892972_111_old", false)]
        public void OnlyADepotUnderscoreManifestFolderNameIsAVersion(string folder, bool valid)
        {
            Assert.Equal(valid, Indexer.GameSource.TryGetVersion(GameFolder(folder), out string version));
            Assert.Equal(folder, version);
        }

        [Theory]
        [InlineData("892972_111", "game.info")]
        [InlineData("892973_222", "game_macos.info")]
        [InlineData("892971_333", "game_linux.info")]
        public void TheDepot_DecidesWhichGameManifestTheFolderProduces(string folder, string manifestName)
        {
            string path = GameFolder(folder);
            Directory.CreateDirectory(path);

            Assert.True(Indexer.GameSource.TryDescribe(path, out Indexer.GameFolder? described, out _));
            Assert.Equal(manifestName, described!.ManifestName);
        }

        [Fact]
        public void ADepotThatIsNotValheim_IsRejected()
        {
            string path = GameFolder("123456_111");
            Directory.CreateDirectory(path);

            Assert.False(Indexer.GameSource.TryDescribe(path, out _, out string? error));
            Assert.Contains("123456", error);
        }

        [Fact]
        public void AMissingFolder_IsRejected()
        {
            Assert.False(Indexer.GameSource.TryDescribe(GameFolder("892972_111"), out _, out string? error));
            Assert.Contains("not found", error);
        }

        [Fact]
        public void TheGameFolder_IsExpectedAtTheSiteRoot()
        {
            Assert.True(Indexer.GameSource.IsAtExpectedLocation(ProfileFolder, GameFolder("892972_111"), "892972_111"));
            Assert.False(Indexer.GameSource.IsAtExpectedLocation(
                ProfileFolder, Path.Combine(_web, "Launcher", "Game", "892972_111"), "892972_111"));
        }

        [Fact]
        public void TheIndexersOwnFiles_AreNeverPartOfAManifest()
        {
            Assert.True(Indexer.GameSource.IsOwnArtifact("game_source.txt"));
            Assert.True(Indexer.GameSource.IsOwnArtifact(Indexer.GameSource.CacheFileNameFor("892972")));
            Assert.False(Indexer.GameSource.IsOwnArtifact("BepInEx/plugins/Mod/hashes_game_1.cache"));
            Assert.False(Indexer.GameSource.IsOwnArtifact("game_files.txt"));
        }

        [Fact]
        public void EachDepot_HasItsOwnHashCache()
        {
            Assert.NotEqual(Indexer.GameSource.CacheFileNameFor("892972"), Indexer.GameSource.CacheFileNameFor("892971"));
        }

        [Fact]
        public void ProfileFlag_TakesThePathAfterIt()
        {
            Assert.Equal("some/profile",
                Indexer.Program.ParseValue(new[] { "--no-cache", "--profile", "some/profile" }, "--profile"));
            Assert.Null(Indexer.Program.ParseValue(new[] { "--profile" }, "--profile"));
            Assert.Null(Indexer.Program.ParseValue(new[] { "--no-cache" }, "--game-folder"));
        }

        [Fact]
        public void GameFolderFlag_CanBeRepeated()
        {
            var roots = Indexer.Program.ParseValues(
                new[] { "--game-folder", "a", "--no-cache", "--game-folder", "b", "--game-folder" }, "--game-folder");

            Assert.Equal(new[] { "a", "b" }, roots.ToArray());
        }
    }
}
