using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Odinsons.ValheimLauncher.Avalonia.Theming
{
    /// <summary>
    /// A decoded art picture and a small copy of its pixels, so the darkening layer can be computed
    /// for the part of the picture each frame actually shows.
    /// </summary>
    internal sealed class ArtPicture
    {
        public const string DefaultUri = "avares://OdinsonsLauncher/Resources/background.jpg";

        private const int PreviewWidth = 192;
        private const int CellDivisor = 24;

        private static ArtPicture? _default;

        private readonly byte[] _preview;
        private readonly int _previewWidth;
        private readonly int _previewHeight;

        private ArtPicture(Bitmap bitmap, int width, int height, byte[] preview, int previewWidth, int previewHeight)
        {
            Bitmap = bitmap;
            Width = width;
            Height = height;
            _preview = preview;
            _previewWidth = previewWidth;
            _previewHeight = previewHeight;
        }

        public Bitmap Bitmap { get; }
        public int Width { get; }
        public int Height { get; }

        public static ArtPicture Default
        {
            get
            {
                if (_default == null)
                {
                    using Stream stream = AssetLoader.Open(new Uri(DefaultUri));
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    _default = FromBytes(copy.ToArray())!;
                }
                return _default;
            }
        }

        public static ArtPicture? FromBytes(byte[] bytes)
        {
            try
            {
                using SKBitmap decoded = SKBitmap.Decode(bytes);
                if (decoded == null || decoded.Width < 1 || decoded.Height < 1)
                    return null;

                int previewWidth = Math.Min(PreviewWidth, decoded.Width);
                int previewHeight = Math.Max(1, (int)Math.Round(decoded.Height * (previewWidth / (double)decoded.Width)));
                byte[] preview = Downsample(decoded.Pixels, decoded.Width, decoded.Height, previewWidth, previewHeight);

                return new ArtPicture(new Bitmap(new MemoryStream(bytes)), decoded.Width, decoded.Height, preview, previewWidth, previewHeight);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Could not decode an art picture", ex);
                return null;
            }
        }

        /// <summary>The part of the picture, in picture pixels, that fills <paramref name="frame"/> once it is cropped to it.</summary>
        public Rect VisibleSource(Size frame, ArtPosition position)
        {
            double scale = Math.Max(frame.Width / Width, frame.Height / Height);
            double visibleWidth = Math.Min(Width, frame.Width / scale), visibleHeight = Math.Min(Height, frame.Height / scale);
            double x = position switch
            {
                ArtPosition.Left => 0,
                ArtPosition.Right => Width - visibleWidth,
                _ => (Width - visibleWidth) / 2
            };
            return new Rect(x, (Height - visibleHeight) / 2, visibleWidth, visibleHeight);
        }

        /// <summary>The strength of the black layer, in percent, white text needs over <paramref name="textArea"/> of <paramref name="frame"/>.</summary>
        public int DarkeningFor(Size frame, ArtPosition position, Rect textArea)
        {
            if (frame.Width < 1 || frame.Height < 1)
                return 0;

            Rect source = VisibleSource(frame, position);
            double toPreview = _previewWidth / (double)Width;
            double perDip = source.Width / frame.Width;

            int x = (int)Math.Floor((source.X + textArea.X * perDip) * toPreview);
            int y = (int)Math.Floor((source.Y + textArea.Y * perDip) * toPreview);
            int w = (int)Math.Ceiling(textArea.Width * perDip * toPreview);
            int h = (int)Math.Ceiling(textArea.Height * perDip * toPreview);

            Rgb brightest = ArtDarkening.BrightestCell(_preview, _previewWidth, _previewHeight, x, y, w, h,
                Math.Max(2, _previewWidth / CellDivisor));
            return ArtDarkening.Percent(brightest);
        }

        /// <summary>The strength for a solid colour or gradient: its brightest point decides.</summary>
        public static int DarkeningFor(Paint paint)
        {
            Rgb brightest = new Rgb(0, 0, 0);
            double luminance = -1;
            foreach (Rgb sample in paint.Samples())
                if (ColorMath.Luminance(sample) > luminance)
                {
                    brightest = sample;
                    luminance = ColorMath.Luminance(sample);
                }
            return ArtDarkening.Percent(brightest);
        }

        private static byte[] Downsample(SKColor[] pixels, int width, int height, int targetWidth, int targetHeight)
        {
            var result = new byte[targetWidth * targetHeight * 4];
            for (int ty = 0; ty < targetHeight; ty++)
            {
                int y0 = ty * height / targetHeight, y1 = Math.Max(y0 + 1, (ty + 1) * height / targetHeight);
                for (int tx = 0; tx < targetWidth; tx++)
                {
                    int x0 = tx * width / targetWidth, x1 = Math.Max(x0 + 1, (tx + 1) * width / targetWidth);
                    long r = 0, g = 0, b = 0;
                    int count = 0;
                    for (int y = y0; y < Math.Min(y1, height); y++)
                        for (int x = x0; x < Math.Min(x1, width); x++)
                        {
                            SKColor pixel = pixels[y * width + x];
                            r += pixel.Red;
                            g += pixel.Green;
                            b += pixel.Blue;
                            count++;
                        }

                    int i = (ty * targetWidth + tx) * 4;
                    result[i] = (byte)(r / count);
                    result[i + 1] = (byte)(g / count);
                    result[i + 2] = (byte)(b / count);
                    result[i + 3] = 255;
                }
            }
            return result;
        }
    }
}
