using System;

namespace Odinsons.ValheimLauncher
{
    /// <summary>The launcher's own colours for one scheme. Themes never supply any of these.</summary>
    public sealed class LauncherPalette
    {
        public ColorSchemeKind Kind { get; init; }
        public Rgb Background { get; init; }
        public Rgb Surface { get; init; }
        public Rgb Text { get; init; }
        public Rgb Muted { get; init; }
        public Rgb Line { get; init; }
        public Rgb NoticeBackground { get; init; }
        public Rgb NoticeText { get; init; }
        public Rgb NoticeBorder { get; init; }
        public Rgb NoticeIcon { get; init; }

        public static LauncherPalette Dark { get; } = new LauncherPalette
        {
            Kind = ColorSchemeKind.Dark,
            Background = Rgb.Parse("#14171E"),
            Surface = Rgb.Parse("#1E222C"),
            Text = Rgb.Parse("#F1F3F8"),
            Muted = Rgb.Parse("#B3BACB"),
            Line = Rgb.Parse("#3A4152"),
            NoticeBackground = Rgb.Parse("#1C2A4A"),
            NoticeText = Rgb.Parse("#EEF2FB"),
            NoticeBorder = Rgb.Parse("#4468B8"),
            NoticeIcon = Rgb.Parse("#9DB6EE")
        };

        public static LauncherPalette Light { get; } = new LauncherPalette
        {
            Kind = ColorSchemeKind.Light,
            Background = Rgb.Parse("#ECEFF4"),
            Surface = Rgb.Parse("#FFFFFF"),
            Text = Rgb.Parse("#161A22"),
            Muted = Rgb.Parse("#444D60"),
            Line = Rgb.Parse("#C9CFDB"),
            NoticeBackground = Rgb.Parse("#E4ECFB"),
            NoticeText = Rgb.Parse("#14213F"),
            NoticeBorder = Rgb.Parse("#4468B8"),
            NoticeIcon = Rgb.Parse("#2F4F9E")
        };

        public static LauncherPalette For(ColorSchemeKind kind) => kind == ColorSchemeKind.Dark ? Dark : Light;
    }

    [Flags]
    public enum ThemeAdjustments
    {
        None = 0,
        AccentMoved = 1,
        FillReplacedByAccent = 2,
        FillMoved = 4,
        BorderMoved = 8
    }

    public sealed class ResolvedButton
    {
        public Paint Fill { get; init; }
        public Paint Border { get; init; }
        public int BorderWidth { get; init; }
        public Rgb Label { get; init; }
        public double LabelContrast { get; init; }
        public double EdgeContrast { get; init; }
        public ThemeAdjustments Adjustments { get; init; }
    }

    public sealed class ResolvedScheme
    {
        public ColorSchemeKind Kind { get; init; }
        public LauncherPalette Palette { get; init; }
        public Rgb Accent { get; init; }
        public ResolvedButton Button { get; init; }
        public ArtSpec Art { get; init; }
        public ThemeAdjustments Adjustments { get; init; }
    }

    /// <summary>
    /// Applies the readability rules of docs/themes.md. Nothing is rejected: colours keep their hue and
    /// have their lightness moved, by the smallest shift that satisfies every rule that applies.
    /// </summary>
    public static class ThemeReadability
    {
        public const double AccentMinimum = 3.0;
        public const double LabelMinimum = 4.5;
        public const double EdgeMinimum = 3.0;
        public const double LightnessStep = 0.02;
        public const int MaxSteps = 50;

        public static readonly Rgb DarkLabel = new Rgb(0x11, 0x11, 0x11);
        public static readonly Rgb LightLabel = new Rgb(0xFF, 0xFF, 0xFF);

        public static ResolvedScheme Resolve(ThemeSpec theme, ColorSchemeKind kind)
        {
            LauncherPalette palette = LauncherPalette.For(kind);
            SchemeSpec scheme = theme.For(kind);

            Rgb accent = ResolveAccent(scheme.Accent, palette);
            ResolvedButton button = ResolveButton(scheme.ButtonFill, scheme.ButtonBorder, theme.ButtonBorderWidth, accent, palette);

            return new ResolvedScheme
            {
                Kind = kind,
                Palette = palette,
                Accent = accent,
                Button = button,
                Art = scheme.Art,
                Adjustments = button.Adjustments | (accent != scheme.Accent ? ThemeAdjustments.AccentMoved : ThemeAdjustments.None)
            };
        }

        public static Rgb ResolveAccent(Rgb accent, LauncherPalette palette) =>
            Fit(Paint.Solid(accent), p => p.MinContrastWith(palette.Surface) >= AccentMinimum).From;

        public static ResolvedButton ResolveButton(Paint fill, Paint border, int borderWidth, Rgb accent, LauncherPalette palette)
        {
            var adjustments = ThemeAdjustments.None;
            if (borderWidth < 1)
                border = Paint.None;
            if (fill.Kind == PaintKind.None && border.Kind == PaintKind.None)
            {
                fill = Paint.Solid(accent);
                adjustments |= ThemeAdjustments.FillReplacedByAccent;
            }

            bool borderIsEdge = border.Kind != PaintKind.None;
            if (borderIsEdge)
            {
                Paint moved = Fit(border, p => p.MinContrastWith(palette.Surface) >= EdgeMinimum);
                if (moved != border)
                    adjustments |= ThemeAdjustments.BorderMoved;
                border = moved;
            }

            Rgb label = palette.Text;
            double labelContrast = ColorMath.Contrast(palette.Text, palette.Surface);
            if (fill.Kind != PaintKind.None)
            {
                Paint moved = Fit(fill, p => BestLabel(p).contrast >= LabelMinimum
                    && (borderIsEdge || p.MinContrastWith(palette.Surface) >= EdgeMinimum));
                if (moved != fill)
                    adjustments |= ThemeAdjustments.FillMoved;
                fill = moved;
                (label, labelContrast) = BestLabel(fill);
            }

            Paint edge = borderIsEdge ? border : fill;
            return new ResolvedButton
            {
                Fill = fill,
                Border = border,
                BorderWidth = borderIsEdge ? borderWidth : 0,
                Label = label,
                LabelContrast = labelContrast,
                EdgeContrast = edge.MinContrastWith(palette.Surface),
                Adjustments = adjustments
            };
        }

        /// <summary>Black or white, whichever has the better worst case over the fill.</summary>
        public static (Rgb label, double contrast) BestLabel(Paint fill)
        {
            double dark = fill.MinContrastWith(DarkLabel), light = fill.MinContrastWith(LightLabel);
            return dark >= light ? (DarkLabel, dark) : (LightLabel, light);
        }

        private static Paint Fit(Paint paint, Func<Paint, bool> ok)
        {
            if (ok(paint))
                return paint;
            for (int step = 1; step <= MaxSteps; step++)
                foreach (int sign in new[] { 1, -1 })
                {
                    Paint candidate = paint.ShiftLightness(sign * step * LightnessStep);
                    if (ok(candidate))
                        return candidate;
                }
            return paint;
        }
    }

    /// <summary>The black layer behind the text drawn on the art.</summary>
    public static class ArtDarkening
    {
        public const double TextMinimum = 4.5;
        public const int StepPercent = 5;
        public const int MaxPercent = 55;

        /// <summary>The first 5% step at which white text over <paramref name="brightest"/> reaches 4.5 to 1.</summary>
        public static int Percent(Rgb brightest)
        {
            var white = new Rgb(0xFF, 0xFF, 0xFF);
            for (int percent = 0; percent <= MaxPercent; percent += StepPercent)
                if (ColorMath.Contrast(white, ColorMath.Scale(brightest, 1 - percent / 100.0)) >= TextMinimum)
                    return percent;
            return MaxPercent;
        }

        /// <summary>
        /// The average colour of the brightest <paramref name="cell"/>-sized square inside the area of an
        /// RGBA pixel buffer, so one stray bright pixel does not decide the strength of the layer.
        /// </summary>
        public static Rgb BrightestCell(byte[] rgba, int width, int height, int areaX, int areaY, int areaWidth, int areaHeight, int cell)
        {
            if (rgba == null || cell < 1 || width < 1 || height < 1 || rgba.Length < width * height * 4)
                throw new ArgumentException("The pixel buffer does not match its size.");

            int x0 = Math.Clamp(areaX, 0, width), y0 = Math.Clamp(areaY, 0, height);
            int x1 = Math.Clamp(areaX + areaWidth, 0, width), y1 = Math.Clamp(areaY + areaHeight, 0, height);
            if (x1 <= x0 || y1 <= y0)
                return new Rgb(0, 0, 0);

            Rgb best = new Rgb(0, 0, 0);
            double bestLuminance = -1;
            for (int cy = y0; cy < y1; cy += cell)
                for (int cx = x0; cx < x1; cx += cell)
                {
                    long r = 0, g = 0, b = 0;
                    int count = 0;
                    for (int y = cy; y < Math.Min(cy + cell, y1); y++)
                        for (int x = cx; x < Math.Min(cx + cell, x1); x++)
                        {
                            int i = (y * width + x) * 4;
                            r += rgba[i];
                            g += rgba[i + 1];
                            b += rgba[i + 2];
                            count++;
                        }

                    var average = new Rgb(ColorMath.RoundByte(r / (double)count), ColorMath.RoundByte(g / (double)count),
                        ColorMath.RoundByte(b / (double)count));
                    double luminance = ColorMath.Luminance(average);
                    if (luminance > bestLuminance)
                    {
                        best = average;
                        bestLuminance = luminance;
                    }
                }
            return best;
        }
    }
}
