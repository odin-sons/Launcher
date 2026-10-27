using Odinsons.ValheimLauncher;
using Xunit;
using static Launcher.Tests.DisplayFileTestKit;

namespace Launcher.Tests
{
    public sealed class ThemeLoaderTests
    {
        private const string ThemeJson = """
            {
              "themeSchema": 1,
              "layout": "banner",
              "logo": { "file": "theme/logo.png" },
              "schemes": {
                "dark": { "accent": "#C8A24A", "art": { "type": "image", "file": "theme/art-dark.jpg", "position": "right" } },
                "light": { "accent": "#8F6B1F", "art": { "type": "image", "file": "theme/art-light.png" } }
              }
            }
            """;

        private readonly Dictionary<string, byte[]> _pinned = new Dictionary<string, byte[]>
        {
            ["theme/theme.json"] = Text(ThemeJson),
            ["theme/logo.png"] = Png(256, 256),
            ["theme/art-dark.jpg"] = Jpeg(1600, 900),
            ["theme/art-light.png"] = Png(1600, 900)
        };

        private (RegistryEntry Entry, DisplayFiles Files, FakeDisplayFetcher Fetcher) Setup(string? theme = "theme/theme.json")
        {
            var fetcher = new FakeDisplayFetcher();
            foreach ((string path, byte[] bytes) in _pinned)
                fetcher.Files[MirrorA + path] = bytes;
            RegistryEntry entry = Entry(_pinned, theme);
            return (entry, new DisplayFiles(entry, fetcher), fetcher);
        }

        [Fact]
        public async Task AThemeWhoseFilesAllCheckOut_IsLoadedWithItsPictures()
        {
            var (entry, files, _) = Setup();

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Equal(LayoutTemplate.Banner, loaded.Spec.Layout);
            Assert.Equal("theme/logo.png", loaded.Spec.LogoFile);
            Assert.Equal(ArtSpec.FromImage("theme/art-dark.jpg", ArtPosition.Right), loaded.Spec.Dark.Art);
            Assert.Equal(ArtSpec.FromImage("theme/art-light.png", ArtPosition.Center), loaded.Spec.Light.Art);
            Assert.Equal(3, loaded.Images.Count);
            Assert.Empty(loaded.Problems);
        }

        [Fact]
        public async Task AProfileWithoutATheme_GetsTheDefault_WithoutAnyRequest()
        {
            var (entry, files, fetcher) = Setup(theme: null);

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Same(ThemeSpec.Default, loaded.Spec);
            Assert.Empty(fetcher.Requests);
        }

        [Fact]
        public async Task AThemeThatCannotBeFetched_IsTheDefault_AndSaysWhy()
        {
            var (entry, files, fetcher) = Setup();
            fetcher.Files.Clear();

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Same(ThemeSpec.Default, loaded.Spec);
            Assert.Single(loaded.Problems);
        }

        [Fact]
        public async Task APictureWhoseHashDiffers_IsDropped_AndTheOtherSchemeKeepsItsOwn()
        {
            var (entry, files, fetcher) = Setup();
            fetcher.Files[MirrorA + "theme/art-dark.jpg"] = Jpeg(1600, 900, padding: 1);

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Equal(ArtSpec.Default, loaded.Spec.Dark.Art);
            Assert.Equal(ArtKind.Image, loaded.Spec.Light.Art.Kind);
            Assert.False(loaded.Images.ContainsKey("theme/art-dark.jpg"));
            Assert.Contains(loaded.Problems, p => p.Contains("art-dark.jpg"));
        }

        [Fact]
        public async Task APictureOverItsPixelLimit_IsDropped()
        {
            _pinned["theme/art-light.png"] = Png(3840, 2160);
            var (entry, files, _) = Setup();

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Equal(ArtSpec.Default, loaded.Spec.Light.Art);
            Assert.Equal(ArtKind.Image, loaded.Spec.Dark.Art.Kind);
            Assert.Contains(loaded.Problems, p => p.Contains("1920 x 1080"));
        }

        [Fact]
        public async Task ALogoThatIsNotAPng_IsDropped_AndTheRestOfTheThemeStays()
        {
            _pinned["theme/logo.png"] = Jpeg(100, 100);
            var (entry, files, _) = Setup();

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Null(loaded.Spec.LogoFile);
            Assert.Equal(LayoutTemplate.Banner, loaded.Spec.Layout);
            Assert.False(loaded.Images.ContainsKey("theme/logo.png"));
        }

        [Fact]
        public async Task APictureBothSchemesName_IsFetchedOnce()
        {
            _pinned["theme/theme.json"] = Text(ThemeJson.Replace("art-light.png", "art-dark.jpg"));
            var (entry, files, fetcher) = Setup();

            LoadedTheme loaded = await ThemeLoader.LoadAsync(entry, files);

            Assert.Equal(1, fetcher.Requests.Count(r => r.EndsWith("art-dark.jpg", StringComparison.Ordinal)));
            Assert.Equal(2, loaded.Images.Count);
        }
    }
}
