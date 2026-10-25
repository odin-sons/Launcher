using System;
using System.Linq;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class ServerProfileTests
    {
        [Fact]
        public void Default_IsTheBuiltInOdinsonsProfile()
        {
            Assert.Same(ServerProfile.Odinsons, ServerProfile.Default);
            Assert.Equal("odinsons", ServerProfile.Default.Id);
        }

        [Fact]
        public void Default_HasMirrors()
        {
            Assert.NotEmpty(ServerProfile.Default.Mirrors);
        }

        [Fact]
        public void Default_MirrorsAreHttpsBaseUrlsEndingInSlash()
        {
            foreach (string mirror in ServerProfile.Default.Mirrors)
            {
                Assert.True(Uri.TryCreate(mirror, UriKind.Absolute, out Uri uri));
                Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
                Assert.EndsWith("/", mirror);
            }
        }

        [Fact]
        public void Default_MirrorsAreDistinct()
        {
            Assert.Equal(ServerProfile.Default.Mirrors.Count, ServerProfile.Default.Mirrors.Distinct().Count());
        }

        [Fact]
        public void Default_HasWebsiteAndCommunityLinks()
        {
            Assert.StartsWith("https://", ServerProfile.Default.Website);
            Assert.StartsWith("https://", ServerProfile.Default.Community);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a url")]
        [InlineData("http://example.test/")]
        [InlineData("file:///tmp/payload")]
        [InlineData("ms-msdt:/id")]
        public void Links_NotHttps_AreDropped(string url)
        {
            var profile = new ServerProfile("test", "Test", new[] { "https://mirror.test/" }, url, url);

            Assert.Null(profile.Website);
            Assert.Null(profile.Community);
        }

        [Fact]
        public void Links_Https_AreKept()
        {
            var profile = new ServerProfile("test", "Test", new[] { "https://mirror.test/" },
                "https://example.test/", "https://community.test/invite");

            Assert.Equal("https://example.test/", profile.Website);
            Assert.Equal("https://community.test/invite", profile.Community);
        }
    }
}
