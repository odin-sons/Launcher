using Odinsons.ValheimLauncher;
using Xunit;
using static Launcher.Tests.DisplayFileTestKit;

namespace Launcher.Tests
{
    public sealed class ImageInspectorTests
    {
        [Fact]
        public void APng_ReportsItsSize()
        {
            Assert.Equal(new ImageInfo(ImageFormat.Png, 640, 360), ImageInspector.Inspect(Png(640, 360)));
        }

        [Theory]
        [InlineData(0xC0)]
        [InlineData(0xC1)]
        [InlineData(0xC2)]
        public void AJpeg_ReportsItsSize_BaselineOrProgressive(byte marker)
        {
            Assert.Equal(new ImageInfo(ImageFormat.Jpeg, 1920, 1080), ImageInspector.Inspect(Jpeg(1920, 1080, marker)));
        }

        [Fact]
        public void AnAnimatedPng_IsNotAcceptedAsAPicture()
        {
            Assert.Null(ImageInspector.Inspect(Png(100, 100, animated: true)));
        }

        [Theory]
        [InlineData(0xC3)]
        [InlineData(0xC9)]
        [InlineData(0xCF)]
        public void AJpegCodingASimpleDecoderCannotRead_IsNotAccepted(byte marker)
        {
            Assert.Null(ImageInspector.Inspect(Jpeg(100, 100, marker)));
        }

        [Fact]
        public void AZeroSize_IsNotAPicture()
        {
            Assert.Null(ImageInspector.Inspect(Png(0, 10)));
            Assert.Null(ImageInspector.Inspect(Jpeg(10, 0)));
        }

        [Fact]
        public void BytesThatAreNotAPicture_AreNotAccepted()
        {
            Assert.Null(ImageInspector.Inspect(null));
            Assert.Null(ImageInspector.Inspect(Array.Empty<byte>()));
            Assert.Null(ImageInspector.Inspect(Text("GIF89a......")));
            Assert.Null(ImageInspector.Inspect(Text("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")));
        }

        [Fact]
        public void ATruncatedPicture_IsNotAccepted()
        {
            byte[] png = Png(100, 100);
            byte[] jpeg = Jpeg(100, 100);

            Assert.Null(ImageInspector.Inspect(png[..20]));
            Assert.Null(ImageInspector.Inspect(jpeg[..30]));
        }

        [Fact]
        public void APngWhoseHeaderIsTheWrongLength_IsNotAccepted()
        {
            byte[] png = Png(100, 100);
            png[11] = 14;

            Assert.Null(ImageInspector.Inspect(png));
        }

        [Fact]
        public void TheLogo_MustBeAPng_AtMost512Square()
        {
            Assert.True(ImageInspector.Accepts(Png(512, 512), ImageRules.Logo, out _, out _));
            Assert.False(ImageInspector.Accepts(Png(513, 512), ImageRules.Logo, out _, out string size));
            Assert.Contains("512", size);
            Assert.False(ImageInspector.Accepts(Jpeg(100, 100), ImageRules.Logo, out _, out string kind));
            Assert.Contains("PNG", kind);
        }

        [Fact]
        public void TheArt_MayBeAPngOrAJpeg_AtMost1920By1080()
        {
            Assert.True(ImageInspector.Accepts(Png(1920, 1080), ImageRules.Art, out _, out _));
            Assert.True(ImageInspector.Accepts(Jpeg(1920, 1080), ImageRules.Art, out _, out _));
            Assert.False(ImageInspector.Accepts(Png(1921, 1080), ImageRules.Art, out _, out _));
            Assert.False(ImageInspector.Accepts(Jpeg(1920, 1081), ImageRules.Art, out _, out _));
        }

        [Fact]
        public void APictureThatClaimsAHugeSize_IsTurnedAwayFromItsHeader()
        {
            Assert.False(ImageInspector.Accepts(Png(100_000, 100_000), ImageRules.Art, out _, out _));
            Assert.False(ImageInspector.Accepts(Png(int.MaxValue, int.MaxValue), ImageRules.Art, out _, out _));
        }

        [Fact]
        public void APictureOverItsByteLimit_IsNotAccepted()
        {
            Assert.True(ImageInspector.Accepts(Png(100, 100, padding: 200 * 1024), ImageRules.Logo, out _, out _));
            Assert.False(ImageInspector.Accepts(Png(100, 100, padding: 300 * 1024), ImageRules.Logo, out _, out string reason));
            Assert.Contains("256 KiB", reason);
            Assert.False(ImageInspector.Accepts(Jpeg(100, 100, padding: 1024 * 1024), ImageRules.Art, out _, out _));
        }
    }
}
