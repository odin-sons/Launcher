using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class ChangelogMarkdownTests
    {
        [Fact]
        public void BracketedVersionWithDate_ExtractsJustTheVersion()
        {
            string text = "# Changelog\n\n## [1.4.3] - 2026-09-10\n### Fixed\n- something\n";

            Assert.Equal("1.4.3", ChangelogMarkdown.LatestVersion(text));
        }

        [Fact]
        public void PlainDateHeader_ExtractsTheDate()
        {
            string text = "## 2026-09-10 - Ashlands hotfix\n- something\n";

            Assert.Equal("2026-09-10", ChangelogMarkdown.LatestVersion(text));
        }

        [Fact]
        public void HeaderWithNoDashSeparator_IsTakenWhole()
        {
            string text = "## Beta\n- something\n";

            Assert.Equal("Beta", ChangelogMarkdown.LatestVersion(text));
        }

        [Fact]
        public void SubsectionHeaders_AreNotMistakenForVersionBoundaries()
        {
            // "### Fixed" has an extra '#' where a version header has a space — must not match.
            string text = "## [2.0.0] - 2026-09-01\n### Fixed\n### Added\n";

            Assert.Equal("2.0.0", ChangelogMarkdown.LatestVersion(text));
        }

        [Fact]
        public void MultipleEntries_TakesTheFirstOne_NewestIsAssumedFirst()
        {
            string text = "## [2.0.0] - 2026-09-10\n- new\n\n## [1.0.0] - 2026-01-01\n- old\n";

            Assert.Equal("2.0.0", ChangelogMarkdown.LatestVersion(text));
        }

        [Fact]
        public void NoVersionHeaders_ReturnsNull()
        {
            Assert.Null(ChangelogMarkdown.LatestVersion("Just some plain text, no headers at all."));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyOrNullInput_ReturnsNull(string? text)
        {
            Assert.Null(ChangelogMarkdown.LatestVersion(text));
        }

        [Fact]
        public void CrLfLineEndings_AreHandledTheSameAsLf()
        {
            string text = "# Changelog\r\n\r\n## [1.4.3] - 2026-09-10\r\n- fixed\r\n";

            Assert.Equal("1.4.3", ChangelogMarkdown.LatestVersion(text));
        }
    }
}
