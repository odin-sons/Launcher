using System.Text;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ThemeReaderTests
    {
        private const string Documented = """
            {
              "themeSchema": 1,
              "layout": "banner",
              "online": "right",
              "font": "montserrat",
              "logo": { "file": "theme/logo.png" },
              "button": { "radius": 8, "borderWidth": 2 },
              "schemes": {
                "dark": {
                  "accent": "#C8A24A",
                  "buttonFill": { "type": "gradient", "from": "#C8A24A", "to": "#8F6B1F", "direction": "vertical" },
                  "buttonBorder": { "type": "solid", "color": "#E6C477" },
                  "art": { "type": "image", "file": "theme/art-dark.jpg", "position": "right" }
                },
                "light": {
                  "accent": "#8F6B1F",
                  "buttonFill": { "type": "solid", "color": "#8F6B1F" },
                  "buttonBorder": { "type": "none" },
                  "art": { "type": "image", "file": "theme/art-light.jpg", "position": "left" }
                }
              }
            }
            """;

        private static ThemeSpec Read(string json) => ThemeReader.Read(Encoding.UTF8.GetBytes(json));

        private static string WithDark(string scheme) =>
            "{\"themeSchema\":1,\"button\":{\"borderWidth\":2},\"schemes\":{\"dark\":" + scheme + "}}";

        [Fact]
        public void ADocumentedTheme_ReadsEveryField()
        {
            ThemeSpec theme = Read(Documented);

            Assert.Equal(LayoutTemplate.Banner, theme.Layout);
            Assert.Equal(OnlineSide.Right, theme.Online);
            Assert.Equal("montserrat", theme.Font);
            Assert.Equal("theme/logo.png", theme.LogoFile);
            Assert.Equal(8, theme.ButtonRadius);
            Assert.Equal(2, theme.ButtonBorderWidth);

            Assert.Equal(Rgb.Parse("#C8A24A"), theme.Dark.Accent);
            Assert.Equal(Paint.Gradient(Rgb.Parse("#C8A24A"), Rgb.Parse("#8F6B1F"), GradientDirection.Vertical), theme.Dark.ButtonFill);
            Assert.Equal(Paint.Solid(Rgb.Parse("#E6C477")), theme.Dark.ButtonBorder);
            Assert.Equal(ArtSpec.FromImage("theme/art-dark.jpg", ArtPosition.Right), theme.Dark.Art);

            Assert.Equal(Rgb.Parse("#8F6B1F"), theme.Light.Accent);
            Assert.Equal(Paint.Solid(Rgb.Parse("#8F6B1F")), theme.Light.ButtonFill);
            Assert.Equal(Paint.None, theme.Light.ButtonBorder);
            Assert.Equal(ArtSpec.FromImage("theme/art-light.jpg", ArtPosition.Left), theme.Light.Art);
        }

        [Fact]
        public void ATheme_OverTheSizeLimit_IsTheDefault()
        {
            string padded = "{\"themeSchema\":1,\"layout\":\"banner\"}" + new string(' ', ThemeReader.MaxBytes);

            Assert.Same(ThemeSpec.Default, Read(padded));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"layout\":\"banner\"}")]
        [InlineData("{\"themeSchema\":2,\"layout\":\"banner\"}")]
        [InlineData("{\"themeSchema\":\"1\",\"layout\":\"banner\"}")]
        public void ADocumentThatIsNotAThemeOfThisSchema_IsTheDefault(string json)
        {
            Assert.Same(ThemeSpec.Default, Read(json));
        }

        [Fact]
        public void UnknownValues_FallBackToTheirDefaults()
        {
            ThemeSpec theme = Read("{\"themeSchema\":1,\"layout\":\"carousel\",\"online\":\"top\",\"font\":\"comic-sans\"}");

            Assert.Equal(LayoutTemplate.Classic, theme.Layout);
            Assert.Equal(OnlineSide.Left, theme.Online);
            Assert.Equal("montserrat", theme.Font);
        }

        [Fact]
        public void TheButtonShape_IsClamped_AndAFractionIsNotANumberOfPixels()
        {
            ThemeSpec clamped = Read("{\"themeSchema\":1,\"button\":{\"radius\":99,\"borderWidth\":9}}");
            Assert.Equal(12, clamped.ButtonRadius);
            Assert.Equal(4, clamped.ButtonBorderWidth);

            ThemeSpec negative = Read("{\"themeSchema\":1,\"button\":{\"radius\":-3,\"borderWidth\":-1}}");
            Assert.Equal(0, negative.ButtonRadius);
            Assert.Equal(0, negative.ButtonBorderWidth);

            ThemeSpec fraction = Read("{\"themeSchema\":1,\"button\":{\"radius\":3.5,\"borderWidth\":1.5}}");
            Assert.Equal(ThemeSpec.DefaultButtonRadius, fraction.ButtonRadius);
            Assert.Equal(0, fraction.ButtonBorderWidth);
        }

        [Fact]
        public void AMissingScheme_IsBuiltFromTheOtherOne_InEitherDirection()
        {
            ThemeSpec onlyDark = Read(WithDark("{\"accent\":\"#112233\"}"));
            Assert.Equal(onlyDark.Dark, onlyDark.Light);
            Assert.Equal(Rgb.Parse("#112233"), onlyDark.Light.Accent);

            ThemeSpec onlyLight = Read("{\"themeSchema\":1,\"schemes\":{\"light\":{\"accent\":\"#445566\"}}}");
            Assert.Equal(onlyLight.Light, onlyLight.Dark);
            Assert.Equal(Rgb.Parse("#445566"), onlyLight.Dark.Accent);
        }

        [Theory]
        [InlineData("{\"themeSchema\":1}")]
        [InlineData("{\"themeSchema\":1,\"schemes\":[]}")]
        [InlineData("{\"themeSchema\":1,\"schemes\":{\"dark\":5,\"light\":\"x\"}}")]
        public void NoUsableScheme_MeansTheDefaultSchemes(string json)
        {
            ThemeSpec theme = Read(json);

            Assert.Equal(ThemeSpec.DefaultScheme(ColorSchemeKind.Dark), theme.Dark);
            Assert.Equal(ThemeSpec.DefaultScheme(ColorSchemeKind.Light), theme.Light);
        }

        [Fact]
        public void AMalformedField_FallsBackAlone_AndTheRestOfTheSchemeStillApplies()
        {
            ThemeSpec theme = Read(WithDark(
                "{\"accent\":\"red\",\"buttonFill\":{\"type\":\"gradient\",\"from\":\"#102030\"},\"buttonBorder\":{\"type\":\"solid\",\"color\":\"#E6C477\"}}"));

            Assert.Equal(ThemeSpec.DefaultDarkAccent, theme.Dark.Accent);
            Assert.Equal(Paint.Solid(ThemeSpec.DefaultDarkAccent), theme.Dark.ButtonFill);
            Assert.Equal(Paint.Solid(Rgb.Parse("#E6C477")), theme.Dark.ButtonBorder);
        }

        [Fact]
        public void AColourWithAnAlphaChannel_IsNotAccepted()
        {
            ThemeSpec theme = Read(WithDark("{\"accent\":\"#C8A24A80\",\"buttonFill\":{\"type\":\"solid\",\"color\":\"#C8A24A80\"}}"));

            Assert.Equal(ThemeSpec.DefaultDarkAccent, theme.Dark.Accent);
            Assert.Equal(Paint.Solid(ThemeSpec.DefaultDarkAccent), theme.Dark.ButtonFill);
        }

        [Fact]
        public void AGradient_DefaultsToHorizontal_AndRefusesAnUnknownDirection()
        {
            ThemeSpec implicitDirection = Read(WithDark(
                "{\"buttonFill\":{\"type\":\"gradient\",\"from\":\"#102030\",\"to\":\"#405060\"}}"));
            Assert.Equal(GradientDirection.Horizontal, implicitDirection.Dark.ButtonFill.Direction);

            ThemeSpec unknown = Read(WithDark(
                "{\"buttonFill\":{\"type\":\"gradient\",\"from\":\"#102030\",\"to\":\"#405060\",\"direction\":\"spiral\"}}"));
            Assert.Equal(Paint.Solid(ThemeSpec.DefaultDarkAccent), unknown.Dark.ButtonFill);
        }

        [Fact]
        public void NoFill_NeedsABorderThatCanBeSeen()
        {
            Paint accentFill = Paint.Solid(ThemeSpec.DefaultDarkAccent);

            ThemeSpec noBorder = Read("{\"themeSchema\":1,\"button\":{\"borderWidth\":2},\"schemes\":{\"dark\":{\"buttonFill\":{\"type\":\"none\"}}}}");
            Assert.Equal(accentFill, noBorder.Dark.ButtonFill);

            ThemeSpec zeroWidth = Read("{\"themeSchema\":1,\"schemes\":{\"dark\":{\"buttonFill\":{\"type\":\"none\"},\"buttonBorder\":{\"type\":\"solid\",\"color\":\"#E6C477\"}}}}");
            Assert.Equal(accentFill, zeroWidth.Dark.ButtonFill);

            ThemeSpec bordered = Read("{\"themeSchema\":1,\"button\":{\"borderWidth\":2},\"schemes\":{\"dark\":{\"buttonFill\":{\"type\":\"none\"},\"buttonBorder\":{\"type\":\"solid\",\"color\":\"#E6C477\"}}}}");
            Assert.Equal(Paint.None, bordered.Dark.ButtonFill);
        }

        [Theory]
        [InlineData("../art.png")]
        [InlineData("/art.png")]
        [InlineData("theme\\art.png")]
        [InlineData("theme//art.png")]
        [InlineData("")]
        public void AnImageOutsideTheMirror_IsNotUsed(string file)
        {
            ThemeSpec theme = Read(WithDark("{\"art\":{\"type\":\"image\",\"file\":\"" + file.Replace("\\", "\\\\") + "\"}}"));

            Assert.Equal(ArtSpec.Default, theme.Dark.Art);
            Assert.Empty(theme.Files());
        }

        [Fact]
        public void TheArt_CanBeAColourOrAGradient_AndAPictureDefaultsToTheCentre()
        {
            ThemeSpec solid = Read(WithDark("{\"art\":{\"type\":\"solid\",\"color\":\"#3A4A63\"}}"));
            Assert.Equal(ArtSpec.FromPaint(Paint.Solid(Rgb.Parse("#3A4A63"))), solid.Dark.Art);

            ThemeSpec gradient = Read(WithDark("{\"art\":{\"type\":\"gradient\",\"from\":\"#1F2A44\",\"to\":\"#C9B37A\",\"direction\":\"diagonal\"}}"));
            Assert.Equal(ArtSpec.FromPaint(Paint.Gradient(Rgb.Parse("#1F2A44"), Rgb.Parse("#C9B37A"), GradientDirection.Diagonal)), gradient.Dark.Art);

            ThemeSpec picture = Read(WithDark("{\"art\":{\"type\":\"image\",\"file\":\"theme/art.png\"}}"));
            Assert.Equal(ArtSpec.FromImage("theme/art.png", ArtPosition.Center), picture.Dark.Art);

            ThemeSpec badPosition = Read(WithDark("{\"art\":{\"type\":\"image\",\"file\":\"theme/art.png\",\"position\":\"up\"}}"));
            Assert.Equal(ArtSpec.Default, badPosition.Dark.Art);

            ThemeSpec none = Read(WithDark("{\"art\":{\"type\":\"none\"}}"));
            Assert.Equal(ArtSpec.Default, none.Dark.Art);
        }

        [Fact]
        public void TheFilesATheme_Names_AreListedOnce()
        {
            ThemeSpec theme = Read("""
                {
                  "themeSchema": 1,
                  "logo": { "file": "theme/logo.png" },
                  "schemes": {
                    "dark": { "art": { "type": "image", "file": "theme/art.jpg" } },
                    "light": { "art": { "type": "image", "file": "theme/art.jpg" } }
                  }
                }
                """);

            Assert.Equal(new[] { "theme/logo.png", "theme/art.jpg" }, theme.Files());
        }

        [Fact]
        public void UnknownFields_AreIgnored_SoANewerThemeStillLoads()
        {
            ThemeSpec theme = Read("{\"themeSchema\":1,\"layout\":\"sidebar\",\"sparkle\":true,\"schemes\":{\"dark\":{\"glow\":1}}}");

            Assert.Equal(LayoutTemplate.Sidebar, theme.Layout);
        }
    }
}
