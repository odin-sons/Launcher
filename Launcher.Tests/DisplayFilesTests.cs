using Odinsons.ValheimLauncher;
using Xunit;
using static Launcher.Tests.DisplayFileTestKit;

namespace Launcher.Tests
{
    public sealed class DisplayFilesTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("display-files-").FullName;
        private readonly byte[] _about = Text("{\"aboutSchema\":1}");

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private (DisplayFiles Files, FakeDisplayFetcher Fetcher) Setup(DisplayFileCache? cache = null)
        {
            var fetcher = new FakeDisplayFetcher();
            fetcher.Files[MirrorA + "about.json"] = _about;
            var entry = Entry(new Dictionary<string, byte[]> { ["about.json"] = _about });
            return (new DisplayFiles(entry, fetcher, cache), fetcher);
        }

        [Fact]
        public async Task APinnedFile_WhoseHashMatches_IsReturned()
        {
            var (files, _) = Setup();

            DisplayFileResult result = await files.LoadAsync("about.json", DisplayFileLimits.About);

            Assert.True(result.Ok);
            Assert.Equal(_about, result.Bytes);
        }

        [Fact]
        public async Task AFileTheRegistryDoesNotPin_IsNeverRequested()
        {
            var (files, fetcher) = Setup();
            fetcher.Files[MirrorA + "extra.png"] = Png(10, 10);

            DisplayFileResult result = await files.LoadAsync("extra.png", 1024);

            Assert.False(result.Ok);
            Assert.NotNull(result.Problem);
            Assert.Empty(fetcher.Requests);
        }

        [Fact]
        public async Task AMirrorThatServesAnotherFile_IsPassedOver_ForTheNextMirror()
        {
            var (files, fetcher) = Setup();
            fetcher.Files[MirrorA + "about.json"] = Text("{\"aboutSchema\":1, \"swapped\":true}");
            fetcher.Files[MirrorB + "about.json"] = _about;

            DisplayFileResult result = await files.LoadAsync("about.json", DisplayFileLimits.About);

            Assert.True(result.Ok);
            Assert.Equal(_about, result.Bytes);
            Assert.Equal(new[] { MirrorA + "about.json", MirrorB + "about.json" }, fetcher.Requests);
        }

        [Fact]
        public async Task WhenNoMirrorHasTheRightFile_TheResultSaysSo_AndNothingIsThrown()
        {
            var (files, fetcher) = Setup();
            fetcher.Files[MirrorA + "about.json"] = Text("tampered");

            DisplayFileResult result = await files.LoadAsync("about.json", DisplayFileLimits.About);

            Assert.False(result.Ok);
            Assert.Contains("digest", result.Problem);
            Assert.Contains("404", result.Problem);
        }

        [Fact]
        public async Task AFileOverTheLimit_IsRefused_EvenFromAFetcherThatDoesNotEnforceIt()
        {
            var (files, fetcher) = Setup();
            fetcher.IgnoreLimit = true;

            DisplayFileResult result = await files.LoadAsync("about.json", _about.Length - 1);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task ACancelledLoad_PropagatesTheCancellation()
        {
            var (files, _) = Setup();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => files.LoadAsync("about.json", DisplayFileLimits.About, cts.Token));
        }

        [Fact]
        public async Task AVerifiedFile_IsKeptByItsDigest_AndServedWithoutTheNetwork()
        {
            var cache = new DisplayFileCache(_dir);
            var (files, fetcher) = Setup(cache);
            await files.LoadAsync("about.json", DisplayFileLimits.About);
            fetcher.Requests.Clear();

            DisplayFileResult again = await files.LoadAsync("about.json", DisplayFileLimits.About);

            Assert.True(again.Ok);
            Assert.Empty(fetcher.Requests);
        }

        [Fact]
        public async Task ACachedFileThatWasChanged_IsNotUsed_AndIsFetchedAgain()
        {
            var cache = new DisplayFileCache(_dir);
            var (files, fetcher) = Setup(cache);
            await files.LoadAsync("about.json", DisplayFileLimits.About);
            string stored = Path.Combine(_dir, DisplayFiles.Digest(_about));
            File.WriteAllBytes(stored, Text("tampered"));
            fetcher.Requests.Clear();

            DisplayFileResult result = await files.LoadAsync("about.json", DisplayFileLimits.About);

            Assert.Equal(_about, result.Bytes);
            Assert.Single(fetcher.Requests);
            Assert.Equal(_about, File.ReadAllBytes(stored));
        }

        [Theory]
        [InlineData("")]
        [InlineData("../escape")]
        [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
        public void ACacheName_ThatIsNotADigest_IsIgnored(string name)
        {
            var cache = new DisplayFileCache(_dir);

            cache.Store(name, _about);

            Assert.Empty(Directory.GetFileSystemEntries(_dir));
            Assert.Null(cache.Read(name, 1024));
        }
    }
}
