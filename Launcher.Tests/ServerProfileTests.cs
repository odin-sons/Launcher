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
    }
}
