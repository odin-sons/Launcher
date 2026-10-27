using System;

namespace Odinsons.ValheimLauncher
{
    public enum ImageFormat { Png, Jpeg }

    public readonly record struct ImageInfo(ImageFormat Format, int Width, int Height);

    /// <summary>What a display picture may be: a size in bytes, a size in pixels and a format.</summary>
    public sealed record ImageRules(int MaxBytes, int MaxWidth, int MaxHeight, bool AllowJpeg)
    {
        public static ImageRules Logo { get; } = new ImageRules(256 * 1024, 512, 512, AllowJpeg: false);

        public static ImageRules Art { get; } = new ImageRules(1024 * 1024, 1920, 1080, AllowJpeg: true);
    }

    /// <summary>
    /// Reads the format and size of a picture from its header, so a picture that is too large or not
    /// the right kind is turned away before anything decodes it.
    /// </summary>
    public static class ImageInspector
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>Null when the bytes are not a still PNG or a baseline/progressive JPEG with a readable header.</summary>
        public static ImageInfo? Inspect(byte[] data)
        {
            if (data == null || data.Length < 4)
                return null;
            if (data.AsSpan(0, Math.Min(PngSignature.Length, data.Length)).SequenceEqual(PngSignature))
                return InspectPng(data);
            if (data[0] == 0xFF && data[1] == 0xD8)
                return InspectJpeg(data);
            return null;
        }

        public static bool Accepts(byte[] data, ImageRules rules, out ImageInfo info, out string reason)
        {
            info = default;
            if (data == null || data.Length == 0 || data.Length > rules.MaxBytes)
            {
                reason = $"larger than {rules.MaxBytes / 1024} KiB";
                return false;
            }

            ImageInfo? inspected = Inspect(data);
            if (inspected == null)
            {
                reason = "not a still PNG or JPEG";
                return false;
            }

            info = inspected.Value;
            if (info.Format == ImageFormat.Jpeg && !rules.AllowJpeg)
            {
                reason = "must be a PNG";
                return false;
            }

            if (info.Width > rules.MaxWidth || info.Height > rules.MaxHeight)
            {
                reason = $"larger than {rules.MaxWidth} x {rules.MaxHeight} pixels";
                return false;
            }

            reason = null;
            return true;
        }

        private static ImageInfo? InspectPng(byte[] data)
        {
            int position = PngSignature.Length;
            bool first = true;
            int width = 0, height = 0;

            while (position + 8 <= data.Length)
            {
                long length = ReadUInt32(data, position);
                string type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
                int body = position + 8;

                if (first)
                {
                    if (type != "IHDR" || length != 13 || body + 13 > data.Length)
                        return null;
                    long w = ReadUInt32(data, body);
                    long h = ReadUInt32(data, body + 4);
                    if (w < 1 || h < 1 || w > int.MaxValue || h > int.MaxValue)
                        return null;
                    width = (int)w;
                    height = (int)h;
                    first = false;
                }
                else if (type == "acTL")
                {
                    return null;
                }
                else if (type == "IDAT")
                {
                    return new ImageInfo(ImageFormat.Png, width, height);
                }

                long next = (long)body + length + 4;
                if (next > data.Length)
                    return null;
                position = (int)next;
            }

            return null;
        }

        private static ImageInfo? InspectJpeg(byte[] data)
        {
            int position = 2;
            while (position + 4 <= data.Length)
            {
                if (data[position] != 0xFF)
                    return null;
                while (position < data.Length && data[position] == 0xFF)
                    position++;
                if (position >= data.Length)
                    return null;

                byte marker = data[position++];
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                    continue;
                if (marker == 0xD9 || marker == 0xDA)
                    return null;
                if (position + 2 > data.Length)
                    return null;

                int length = (data[position] << 8) | data[position + 1];
                if (length < 2 || position + length > data.Length)
                    return null;

                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                {
                    if (length < 8)
                        return null;
                    int height = (data[position + 3] << 8) | data[position + 4];
                    int width = (data[position + 5] << 8) | data[position + 6];
                    return width < 1 || height < 1 ? null : new ImageInfo(ImageFormat.Jpeg, width, height);
                }

                if (marker >= 0xC3 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    return null;

                position += length;
            }

            return null;
        }

        private static long ReadUInt32(byte[] data, int offset) =>
            ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];
    }
}
