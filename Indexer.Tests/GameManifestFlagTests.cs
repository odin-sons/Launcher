using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// <c>--game-manifest &lt;name&gt;</c> renames the game-file manifest the Indexer writes,
    /// so a macOS/Linux build folder can produce <c>game_macos.info</c> / <c>game_linux.info</c>
    /// instead of overwriting the Windows <c>game.info</c>.
    /// </summary>
    public sealed class GameManifestFlagTests
    {
        [Fact]
        public void Default_IsGameInfo()
        {
            Assert.Equal("game.info", Indexer.Program.ParseGameManifestName(System.Array.Empty<string>()));
            Assert.Equal("game.info", Indexer.Program.ParseGameManifestName(new[] { "--no-cache" }));
        }

        [Fact]
        public void Flag_TakesTheNameAfterIt()
        {
            Assert.Equal("game_macos.info",
                Indexer.Program.ParseGameManifestName(new[] { "--no-cache", "--game-manifest", "game_macos.info" }));
            Assert.Equal("game_linux.info",
                Indexer.Program.ParseGameManifestName(new[] { "--game-manifest", "game_linux.info", "--no-cache" }));
        }

        [Fact]
        public void FlagWithNoValue_IsIgnored()
        {
            Assert.Equal("game.info", Indexer.Program.ParseGameManifestName(new[] { "--game-manifest" }));
        }
    }
}
