using System.Text;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class AboutReaderTests
    {
        private const string Documented = """
            {
              "aboutSchema": 1,
              "tagline": { "en": "Hardcore survival, no wipes", "de": "Harter Überlebenskampf" },
              "facts": {
                "restarts": "04:00 UTC",
                "playstyle": "Hardcore survival",
                "pvp": "off",
                "difficulty": "hard",
                "region": "EU",
                "languages": "en, ru",
                "wipes": "every 3 months",
                "invented": "ignored"
              },
              "description": {
                "en": "A long-running server.\n\n- Weekly events\n- Friendly admins",
                "de": "Derselbe Text."
              }
            }
            """;

        private static AboutInfo Read(string json) => AboutReader.Read(Encoding.UTF8.GetBytes(json));

        [Fact]
        public void ADocumentedFile_ReadsEveryField_AndFactsFollowTheVocabulary()
        {
            AboutInfo about = Read(Documented);

            Assert.Equal("Hardcore survival, no wipes", about.TaglineFor("en"));
            Assert.Equal("Harter Überlebenskampf", about.TaglineFor("de"));
            Assert.Equal(
                new[] { AboutFact.Playstyle, AboutFact.Pvp, AboutFact.Difficulty, AboutFact.Region, AboutFact.Languages, AboutFact.Wipes, AboutFact.Restarts },
                about.Facts.Select(f => f.Key));
            Assert.Equal("04:00 UTC", about.Facts[^1].Value);
            Assert.Equal("Derselbe Text.", about.DescriptionFor("de"));
            Assert.StartsWith("A long-running server.", about.DescriptionFor("fr"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"aboutSchema\":2}")]
        [InlineData("{\"aboutSchema\":\"1\"}")]
        [InlineData("{\"aboutSchema\":1.5}")]
        public void AFileTheLauncherCannotUse_GivesNoAboutZone(string json)
        {
            Assert.Null(Read(json));
        }

        [Fact]
        public void AFileOverTheSizeLimit_GivesNoAboutZone()
        {
            string json = "{\"aboutSchema\":1,\"description\":{\"en\":\"" + new string('a', AboutReader.MaxBytes) + "\"}}";

            Assert.Null(Read(json));
        }

        [Fact]
        public void AFieldThatBreaksItsRule_IsLeftOutAlone()
        {
            string json = "{\"aboutSchema\":1," +
                "\"tagline\":{\"en\":\"" + new string('t', AboutReader.MaxTaglineLength + 1) + "\"}," +
                "\"facts\":{\"pvp\":\"" + new string('f', AboutReader.MaxFactLength + 1) + "\",\"region\":\"EU\",\"wipes\":5}," +
                "\"description\":{\"en\":\"fine\",\"de\":\"" + new string('d', AboutReader.MaxDescriptionLength + 1) + "\"}}";

            AboutInfo about = Read(json);

            Assert.Empty(about.Tagline);
            Assert.Equal(AboutFact.Region, Assert.Single(about.Facts).Key);
            Assert.Equal("fine", about.DescriptionFor("de"));
        }

        [Fact]
        public void ATextWithoutEnglish_IsNotUsed()
        {
            AboutInfo about = Read("{\"aboutSchema\":1,\"tagline\":{\"de\":\"Nur Deutsch\"},\"description\":{\"fr\":\"Seulement\"}}");

            Assert.Empty(about.Tagline);
            Assert.Empty(about.Description);
            Assert.True(about.IsEmpty);
        }

        [Fact]
        public void ATagline_IsOneLineOfPlainText()
        {
            AboutInfo about = Read("{\"aboutSchema\":1,\"tagline\":{\"en\":\"first\\nsecond\\u202Eevil\"}}");

            Assert.Equal("first secondevil", about.TaglineFor("en"));
        }

        [Fact]
        public void ALanguageCodeThatIsNotOne_IsIgnored()
        {
            AboutInfo about = Read("{\"aboutSchema\":1,\"tagline\":{\"en\":\"ok\",\"English\":\"x\",\"\":\"y\",\"../x\":\"z\"}}");

            Assert.Equal(new[] { "en" }, about.Tagline.Keys);
        }

        [Fact]
        public void AFileWithOnlyTheSchema_IsAnEmptyZone()
        {
            AboutInfo about = Read("{\"aboutSchema\":1}");

            Assert.NotNull(about);
            Assert.True(about.IsEmpty);
            Assert.Null(about.TaglineFor("en"));
        }
    }
}
