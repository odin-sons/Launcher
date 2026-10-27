using System;
using System.Collections.Generic;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    public enum LayoutTemplate { Classic, Sidebar, Banner, Minimal }

    public enum OnlineSide { Left, Right }

    /// <summary>The scheme the window is drawn in.</summary>
    public enum ColorSchemeKind { Dark, Light }

    public enum PaintKind { None, Solid, Gradient }

    public enum GradientDirection { Horizontal, Vertical, Diagonal }

    public enum ArtKind { Default, Paint, Image }

    public enum ArtPosition { Left, Center, Right }

    /// <summary>A solid colour, a two-stop gradient, or nothing.</summary>
    public readonly record struct Paint(PaintKind Kind, Rgb From, Rgb To, GradientDirection Direction)
    {
        public const int GradientSamples = 9;

        public static Paint None => new Paint(PaintKind.None, default, default, GradientDirection.Horizontal);

        public static Paint Solid(Rgb color) => new Paint(PaintKind.Solid, color, color, GradientDirection.Horizontal);

        public static Paint Gradient(Rgb from, Rgb to, GradientDirection direction) =>
            new Paint(PaintKind.Gradient, from, to, direction);

        /// <summary>The colours a gradient is checked at: both ends and the points between them.</summary>
        public IReadOnlyList<Rgb> Samples()
        {
            switch (Kind)
            {
                case PaintKind.Solid:
                    return new[] { From };
                case PaintKind.Gradient:
                    var samples = new Rgb[GradientSamples];
                    for (int i = 0; i < GradientSamples; i++)
                        samples[i] = ColorMath.Blend(From, To, i / (double)(GradientSamples - 1));
                    return samples;
                default:
                    return Array.Empty<Rgb>();
            }
        }

        public double MinContrastWith(Rgb other) =>
            Kind == PaintKind.None ? double.PositiveInfinity : Samples().Min(c => ColorMath.Contrast(c, other));

        public Paint ShiftLightness(double delta) => Kind switch
        {
            PaintKind.None => this,
            PaintKind.Solid => Solid(ColorMath.ShiftLightness(From, delta)),
            _ => Gradient(ColorMath.ShiftLightness(From, delta), ColorMath.ShiftLightness(To, delta), Direction)
        };
    }

    public sealed record ArtSpec(ArtKind Kind, Paint Paint, string File, ArtPosition Position)
    {
        public static ArtSpec Default { get; } = new ArtSpec(ArtKind.Default, Paint.None, null, ArtPosition.Center);

        public static ArtSpec FromPaint(Paint paint) => new ArtSpec(ArtKind.Paint, paint, null, ArtPosition.Center);

        public static ArtSpec FromImage(string file, ArtPosition position) =>
            new ArtSpec(ArtKind.Image, Paint.None, file, position);
    }

    /// <summary>One colour scheme of a theme, as the community wrote it, before the readability rules.</summary>
    public sealed record SchemeSpec(Rgb Accent, Paint ButtonFill, Paint ButtonBorder, ArtSpec Art);

    public sealed class ThemeSpec
    {
        public const string DefaultFont = "montserrat";
        public const int DefaultButtonRadius = 6;

        public static readonly Rgb DefaultDarkAccent = new Rgb(0xC8, 0xA2, 0x4A);
        public static readonly Rgb DefaultLightAccent = new Rgb(0x8F, 0x6B, 0x1F);

        public static ThemeSpec Default { get; } = new ThemeSpec
        {
            Layout = LayoutTemplate.Classic,
            Online = OnlineSide.Left,
            Font = DefaultFont,
            LogoFile = null,
            ButtonRadius = DefaultButtonRadius,
            ButtonBorderWidth = 0,
            Dark = DefaultScheme(ColorSchemeKind.Dark),
            Light = DefaultScheme(ColorSchemeKind.Light)
        };

        public LayoutTemplate Layout { get; init; }
        public OnlineSide Online { get; init; }
        public string Font { get; init; }
        public string LogoFile { get; init; }
        public int ButtonRadius { get; init; }
        public int ButtonBorderWidth { get; init; }
        public SchemeSpec Dark { get; init; }
        public SchemeSpec Light { get; init; }

        public SchemeSpec For(ColorSchemeKind kind) => kind == ColorSchemeKind.Dark ? Dark : Light;

        /// <summary>Every file the theme names, so each can be checked against the registry's digests.</summary>
        public IReadOnlyList<string> Files()
        {
            var files = new List<string>();
            if (LogoFile != null)
                files.Add(LogoFile);
            foreach (SchemeSpec scheme in new[] { Dark, Light })
                if (scheme.Art.Kind == ArtKind.Image && !files.Contains(scheme.Art.File, StringComparer.Ordinal))
                    files.Add(scheme.Art.File);
            return files;
        }

        public static SchemeSpec DefaultScheme(ColorSchemeKind kind)
        {
            Rgb accent = kind == ColorSchemeKind.Dark ? DefaultDarkAccent : DefaultLightAccent;
            return new SchemeSpec(accent, Paint.Solid(accent), Paint.None, ArtSpec.Default);
        }
    }
}
