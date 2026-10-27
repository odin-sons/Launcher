using System.Net.Http;
using System.Text;
using Odinsons.ValheimLauncher;

namespace Launcher.Tests
{
    internal sealed class FakeDisplayFetcher : IDisplayFileFetcher
    {
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();

        public List<string> Requests { get; } = new List<string>();

        public bool IgnoreLimit { get; set; }

        public Task<byte[]> FetchAsync(Uri url, int maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(url.AbsoluteUri);
            if (!Files.TryGetValue(url.AbsoluteUri, out byte[]? bytes))
                throw new HttpRequestException("HTTP 404");
            if (!IgnoreLimit && bytes.Length > maxBytes)
                throw new RegistryException(RegistryFailure.Malformed, "The response is larger than allowed.");
            return Task.FromResult(bytes);
        }
    }

    internal static class DisplayFileTestKit
    {
        public const string MirrorA = "https://a.example.test/community/";
        public const string MirrorB = "https://b.example.test/community/";

        public static RegistryEntry Entry(Dictionary<string, byte[]> pinned, string? theme = null) => new RegistryEntry
        {
            Id = "community",
            Name = "Community",
            Mirrors = new[] { MirrorA, MirrorB },
            Status = RegistryStatus.Approved,
            Digests = pinned.ToDictionary(p => p.Key, p => DisplayFiles.Digest(p.Value), StringComparer.Ordinal),
            Theme = theme
        };

        public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

        public static byte[] Png(int width, int height, bool animated = false, int padding = 0)
        {
            var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Chunk(bytes, "IHDR", Concat(Be(width), Be(height), new byte[] { 8, 2, 0, 0, 0 }));
            if (animated)
                Chunk(bytes, "acTL", new byte[8]);
            Chunk(bytes, "IDAT", new byte[16 + padding]);
            Chunk(bytes, "IEND", Array.Empty<byte>());
            return bytes.ToArray();
        }

        public static byte[] Jpeg(int width, int height, byte frameMarker = 0xC0, int padding = 0)
        {
            var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
            bytes.AddRange(new byte[14]);
            bytes.AddRange(new byte[] { 0xFF, frameMarker, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03 });
            bytes.AddRange(new byte[9]);
            bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0x00, 0xFF, 0xD9 });
            bytes.AddRange(new byte[padding]);
            return bytes.ToArray();
        }

        private static void Chunk(List<byte> bytes, string type, byte[] body)
        {
            bytes.AddRange(Be(body.Length));
            bytes.AddRange(Encoding.ASCII.GetBytes(type));
            bytes.AddRange(body);
            bytes.AddRange(new byte[4]);
        }

        private static byte[] Be(int value) =>
            new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

        private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    }
}
