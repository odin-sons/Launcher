using System.IO;
using Xunit;

namespace Indexer.Tests
{
    public sealed class GameSourceTests : System.IDisposable
    {
        private readonly string _web = Directory.CreateTempSubdirectory("indexer-web-").FullName;
        private string BuildFolder => Path.Combine(_web, "Lite_v2");
        private string GameFolder(string version) => Path.Combine(_web, "Game", version);

        public GameSourceTests()
        {
            Directory.CreateDirectory(BuildFolder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_web, recursive: true); } catch { }
        }

        [Fact]
        public void NothingRemembered_MeansNoSeparateGame()
        {
            Assert.Null(Indexer.GameSource.Read(BuildFolder));
        }

        [Fact]
        public void APathPassedOnce_IsReadBackOnLaterRuns()
        {
            string game = GameFolder("892972_111");
            Directory.CreateDirectory(game);

            Indexer.GameSource.Write(BuildFolder, game);

            Assert.Equal(Path.GetFullPath(game), Indexer.GameSource.Read(BuildFolder));
        }

        [Fact]
        public void ANewerPath_ReplacesTheRememberedOne()
        {
            Indexer.GameSource.Write(BuildFolder, GameFolder("892972_111"));
            Indexer.GameSource.Write(BuildFolder, GameFolder("892972_222"));

            Assert.Equal(Path.GetFullPath(GameFolder("892972_222")), Indexer.GameSource.Read(BuildFolder));
        }

        [Fact]
        public void TheRememberedPath_IsStoredRelativeToTheBuildFolder()
        {
            Indexer.GameSource.Write(BuildFolder, GameFolder("892972_111"));

            string stored = File.ReadAllText(Path.Combine(BuildFolder, Indexer.GameSource.FileName)).Trim();
            Assert.Equal("../Game/892972_111", stored);
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

        [Fact]
        public void TheGameFolder_IsExpectedNextToTheServerFolders()
        {
            Assert.True(Indexer.GameSource.IsAtExpectedLocation(BuildFolder, GameFolder("892972_111"), "892972_111"));
            Assert.False(Indexer.GameSource.IsAtExpectedLocation(
                BuildFolder, Path.Combine(BuildFolder, "Game", "892972_111"), "892972_111"));
        }

        [Fact]
        public void BuildFlag_TakesThePathAfterIt()
        {
            Assert.Equal("some/profile",
                Indexer.Program.ParseValue(new[] { "--no-cache", "--build", "some/profile" }, "--build"));
            Assert.Null(Indexer.Program.ParseValue(new[] { "--build" }, "--build"));
            Assert.Null(Indexer.Program.ParseValue(new[] { "--no-cache" }, "--game-root"));
        }
    }
}
