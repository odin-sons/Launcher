using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class LocalizedTextTests
    {
        private static readonly IReadOnlyDictionary<string, string> Texts = new Dictionary<string, string>
        {
            ["en"] = "English",
            ["de"] = "Deutsch",
            ["pt-BR"] = "Português do Brasil"
        };

        [Theory]
        [InlineData("de", "Deutsch")]
        [InlineData("DE", "Deutsch")]
        [InlineData("de-AT", "Deutsch")]
        [InlineData("de_CH", "Deutsch")]
        [InlineData("pt-br", "Português do Brasil")]
        [InlineData("fr", "English")]
        [InlineData("pt", "English")]
        [InlineData("", "English")]
        [InlineData(null, "English")]
        public void TheInterfaceLanguage_IsUsedElseItsBase_ElseEnglish(string language, string expected)
        {
            Assert.Equal(expected, LocalizedText.Pick(Texts, language));
        }

        [Fact]
        public void WithoutEnglish_AnUnknownLanguageGetsNothing()
        {
            var texts = new Dictionary<string, string> { ["de"] = "Deutsch" };

            Assert.Null(LocalizedText.Pick(texts, "fr"));
            Assert.Null(LocalizedText.Pick<string>(null, "fr"));
            Assert.Null(LocalizedText.Pick(new Dictionary<string, string>(), "en"));
        }
    }
}
