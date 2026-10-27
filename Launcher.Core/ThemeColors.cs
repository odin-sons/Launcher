using System;
using System.Globalization;

namespace Odinsons.ValheimLauncher
{
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        /// <summary>Accepts exactly <c>#RRGGBB</c>; an alpha channel is rejected on purpose.</summary>
        public static bool TryParse(string text, out Rgb color)
        {
            color = default;
            if (text == null || text.Length != 7 || text[0] != '#')
                return false;

            const NumberStyles hex = NumberStyles.AllowHexSpecifier;
            if (!byte.TryParse(text.AsSpan(1, 2), hex, CultureInfo.InvariantCulture, out byte r)
                || !byte.TryParse(text.AsSpan(3, 2), hex, CultureInfo.InvariantCulture, out byte g)
                || !byte.TryParse(text.AsSpan(5, 2), hex, CultureInfo.InvariantCulture, out byte b))
                return false;

            color = new Rgb(r, g, b);
            return true;
        }

        public static Rgb Parse(string text) =>
            TryParse(text, out Rgb color) ? color : throw new FormatException($"'{text}' is not a #RRGGBB colour.");

        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

        public override string ToString() => ToHex();
    }

    /// <summary>WCAG 2 relative luminance and contrast, plus the lightness shift the readability rules use.</summary>
    public static class ColorMath
    {
        public static byte RoundByte(double value) => (byte)Math.Clamp((int)Math.Floor(value + 0.5), 0, 255);

        public static double Luminance(Rgb c) =>
            0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        public static double Contrast(Rgb a, Rgb b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }

        public static Rgb Blend(Rgb a, Rgb b, double t) => new Rgb(
            RoundByte(a.R + (b.R - a.R) * t),
            RoundByte(a.G + (b.G - a.G) * t),
            RoundByte(a.B + (b.B - a.B) * t));

        public static Rgb Scale(Rgb c, double factor) =>
            new Rgb(RoundByte(c.R * factor), RoundByte(c.G * factor), RoundByte(c.B * factor));

        /// <summary>Moves HSL lightness by <paramref name="delta"/> (clamped to 0..1), keeping hue and saturation.</summary>
        public static Rgb ShiftLightness(Rgb c, double delta)
        {
            (double h, double s, double l) = ToHsl(c);
            return FromHsl(h, s, Math.Clamp(l + delta, 0, 1));
        }

        private static double Linear(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static (double h, double s, double l) ToHsl(Rgb c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, h = 0, s = 0, d = max - min;
            if (d != 0)
            {
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
                h /= 6;
            }
            return (h, s, l);
        }

        private static Rgb FromHsl(double h, double s, double l)
        {
            if (s == 0)
            {
                byte gray = RoundByte(l * 255);
                return new Rgb(gray, gray, gray);
            }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            return new Rgb(
                RoundByte(HueChannel(p, q, h + 1.0 / 3) * 255),
                RoundByte(HueChannel(p, q, h) * 255),
                RoundByte(HueChannel(p, q, h - 1.0 / 3) * 255));
        }

        private static double HueChannel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
