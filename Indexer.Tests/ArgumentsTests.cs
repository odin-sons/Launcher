using Xunit;

namespace Indexer.Tests
{
    /// <summary>
    /// What the Indexer does with its command line: it understands a handful of flags and
    /// rejects everything else instead of quietly ignoring it.
    /// </summary>
    public sealed class ArgumentsTests
    {
        [Fact]
        public void NoArguments_AreFine()
        {
            Assert.Null(Indexer.Program.ValidateArgs(System.Array.Empty<string>()));
        }

        [Fact]
        public void EveryKnownFlag_IsAccepted_InAnyOrder()
        {
            Assert.Null(Indexer.Program.ValidateArgs(new[]
            {
                "--no-cache", "--game-folder", "a", "--profile", "p", "--game-folder", "b",
                "--game-manifest", "game_macos.info"
            }));
        }

        [Fact]
        public void FlagsAreCaseInsensitive()
        {
            Assert.Null(Indexer.Program.ValidateArgs(new[] { "--Profile", "p", "--NO-CACHE" }));
        }

        [Theory]
        [InlineData("--game-root")]
        [InlineData("--build")]
        [InlineData("--nocache")]
        [InlineData("-x")]
        public void AnUnknownOrOutdatedFlag_IsAnError_NamedInTheMessage(string flag)
        {
            string? error = Indexer.Program.ValidateArgs(new[] { "--profile", "p", flag, "value" });

            Assert.NotNull(error);
            Assert.Contains(flag, error);
            Assert.Contains("--game-folder", error);
        }

        [Fact]
        public void AStrayArgument_IsAnError()
        {
            string? error = Indexer.Program.ValidateArgs(new[] { "--profile", "p", "Lite_v2" });

            Assert.NotNull(error);
            Assert.Contains("Lite_v2", error);
        }

        [Theory]
        [InlineData("--profile")]
        [InlineData("--game-folder")]
        [InlineData("--game-manifest")]
        public void AFlagWithNoValue_IsAnError(string flag)
        {
            Assert.Contains(flag, Indexer.Program.ValidateArgs(new[] { flag })!);
        }

        [Fact]
        public void AFlagFollowedByAnotherFlag_HasNoValue()
        {
            Assert.NotNull(Indexer.Program.ValidateArgs(new[] { "--profile", "--no-cache" }));
        }
    }
}
