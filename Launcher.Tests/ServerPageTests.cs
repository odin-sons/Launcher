using Odinsons.ValheimLauncher;
using Xunit;
using static Launcher.Tests.DisplayFileTestKit;

namespace Launcher.Tests
{
    public sealed class ServerPageTests : IDisposable
    {
        private readonly string _language = Loc.Language;

        public ServerPageTests() => Loc.Use("en");

        public void Dispose() => Loc.Use(_language);

        private const string ModpackManifest =
            "MANIFEST 3 sha256\n" +
            "aa 1000 BepInEx/plugins/alpha-Mod/alpha.dll\n" +
            "bb 500 BepInEx/plugins/alpha-Mod/manifest.json\n" +
            "cc 2000 BepInEx/plugins/beta-Mod/beta.dll\n" +
            "dd 7 doorstop_config.ini\n";

        private static AboutInfo AboutWith(params KeyValuePair<AboutFact, string>[] facts) => new AboutInfo
        {
            Tagline = AboutInfo.Empty.Tagline,
            Description = AboutInfo.Empty.Description,
            Facts = facts
        };

        [Fact]
        public void MeasuredFacts_CountModsByFolder_AndSumTheDownload()
        {
            MeasuredFacts facts = MeasuredFacts.FromManifests(ModpackManifest, null);

            Assert.Equal(2, facts.ModCount);
            Assert.Equal(3507, facts.DownloadBytes);
            Assert.Null(facts.GameVersion);
        }

        [Fact]
        public void MeasuredFacts_ReadTheGameVersionFromTheGameManifest()
        {
            MeasuredFacts facts = MeasuredFacts.FromManifests(null, "MANIFEST 3 sha256\n# game: 892972_123\naa 1 valheim.exe\n");

            Assert.Equal("892972_123", facts.GameVersion);
            Assert.Null(facts.ModCount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        public void AnUnreadableManifest_LeavesTheFactsOut(string text)
        {
            MeasuredFacts facts = MeasuredFacts.FromManifests(text, text);

            Assert.Null(facts.ModCount);
            Assert.Null(facts.DownloadBytes);
            Assert.Null(facts.GameVersion);
        }

        [Fact]
        public void Facts_ListMeasuredOnesFirst_ThenTheCommunitysInVocabularyOrder()
        {
            AboutInfo about = AboutWith(
                new KeyValuePair<AboutFact, string>(AboutFact.Pvp, "off"),
                new KeyValuePair<AboutFact, string>(AboutFact.Restarts, "04:00 UTC"));

            var lines = ServerPage.Facts(new MeasuredFacts("1_2", 12, 2048, 3, 20), about);

            Assert.Equal(new[] { "Game version", "Mods", "Download size", "Online now", "PvP", "Restarts" }, lines.Select(l => l.Label));
            Assert.Equal(new[] { true, true, true, true, false, false }, lines.Select(l => l.Measured));
            Assert.Equal("3 / 20", lines[3].Value);
            Assert.Equal("04:00 UTC", lines[5].Value);
        }

        [Fact]
        public void Online_WithoutACapacity_ShowsOnlyTheCount()
        {
            var lines = ServerPage.Facts(MeasuredFacts.None.WithOnline(5, null), AboutInfo.Empty);

            Assert.Equal("5", Assert.Single(lines).Value);
        }

        [Fact]
        public void NothingToShow_GivesNoFacts()
        {
            Assert.Empty(ServerPage.Facts(MeasuredFacts.None, AboutInfo.Empty));
        }

        [Fact]
        public void FactLabels_AreTranslated_ForEveryLanguage()
        {
            foreach (string language in Loc.Supported)
            {
                Loc.Use(language);
                var measured = new MeasuredFacts("1_2", 1, 1, 1, 1);
                AboutInfo about = AboutWith(Enum.GetValues<AboutFact>().Select(f => new KeyValuePair<AboutFact, string>(f, "x")).ToArray());

                foreach (FactLine line in ServerPage.Facts(measured, about))
                    Assert.False(line.Label.StartsWith("about.fact.", StringComparison.Ordinal), $"{language}: {line.Label}");
                Assert.NotEqual("about.measured", Loc.T("about.measured"));
            }
        }

        [Fact]
        public void FactLabels_FollowTheInterfaceLanguage()
        {
            Loc.Use("de");

            var lines = ServerPage.Facts(new MeasuredFacts("1_2", null, null, null, null), AboutInfo.Empty);

            Assert.Equal("Spielversion", Assert.Single(lines).Label);
        }

        [Fact]
        public void TheBuiltInProfile_MapsItsTaglineAndInfoToTheDescriptionAndTagline()
        {
            AboutInfo about = AboutInfo.FromLegacy("  Hardcore\nsurvival ", "## Rules\r\n\r\n- be kind\r\n");

            Assert.Equal("Hardcore survival", about.TaglineFor("en"));
            Assert.Equal("## Rules\n\n- be kind", about.DescriptionFor("en"));
            Assert.Empty(about.Facts);
        }

        [Fact]
        public void TheLegacyTagline_IsCutToTheTaglineLimit()
        {
            AboutInfo about = AboutInfo.FromLegacy(new string('t', 200), string.Empty);

            Assert.Equal(AboutReader.MaxTaglineLength, about.TaglineFor("en").Length);
        }

        [Fact]
        public void EmptyLegacyText_GivesAnEmptyAbout()
        {
            Assert.True(AboutInfo.FromLegacy(" ", "\n").IsEmpty);
        }

        [Fact]
        public async Task TheLoader_ReadsThePinnedAboutAndChangelog()
        {
            byte[] about = Text("{\"aboutSchema\":1,\"tagline\":{\"en\":\"Hi\"},\"description\":{\"en\":\"Welcome\"}}");
            byte[] news = Text("## 1.0\n\n- first");
            var fetcher = new FakeDisplayFetcher();
            fetcher.Files[MirrorA + "about.json"] = about;
            fetcher.Files[MirrorA + "changelog.md"] = news;
            RegistryEntry entry = Entry(new Dictionary<string, byte[]> { ["about.json"] = about, ["changelog.md"] = news }, about: "about.json");

            ServerPage page = await ServerPageLoader.LoadAsync(entry, new DisplayFiles(entry, fetcher));

            Assert.Equal("Hi", page.About.TaglineFor("en"));
            Assert.Equal("Welcome", page.About.DescriptionFor("en"));
            Assert.Equal("## 1.0\n\n- first", page.News);
        }

        [Fact]
        public async Task AFileThatIsNotPinned_IsNeverRequested()
        {
            var fetcher = new FakeDisplayFetcher();
            RegistryEntry entry = Entry(new Dictionary<string, byte[]> { ["other.txt"] = Text("x") });

            ServerPage page = await ServerPageLoader.LoadAsync(entry, new DisplayFiles(entry, fetcher));

            Assert.True(page.About.IsEmpty);
            Assert.Equal(string.Empty, page.News);
            Assert.Empty(fetcher.Requests);
        }

        [Fact]
        public async Task ASwappedFile_IsLeftOut()
        {
            byte[] pinnedAbout = Text("{\"aboutSchema\":1,\"description\":{\"en\":\"Welcome\"}}");
            byte[] swappedAbout = Text("{\"aboutSchema\":1,\"description\":{\"en\":\"Evil\"}}");
            var fetcher = new FakeDisplayFetcher();
            foreach (string mirror in new[] { MirrorA, MirrorB })
            {
                fetcher.Files[mirror + "about.json"] = swappedAbout;
                fetcher.Files[mirror + "changelog.md"] = Text("fake news");
            }
            RegistryEntry entry = Entry(new Dictionary<string, byte[]> { ["about.json"] = pinnedAbout, ["changelog.md"] = Text("real news") }, about: "about.json");

            ServerPage page = await ServerPageLoader.LoadAsync(entry, new DisplayFiles(entry, fetcher));

            Assert.True(page.About.IsEmpty);
            Assert.Equal(string.Empty, page.News);
        }
    }
}
