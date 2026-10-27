using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ThemeReadabilityTests
    {
        private static readonly LauncherPalette[] Palettes = { LauncherPalette.Dark, LauncherPalette.Light };

        private static readonly byte[] Levels = { 0, 51, 102, 153, 204, 255 };

        private static Rgb Hex(string text) => Rgb.Parse(text);

        public static TheoryData<ColorSchemeKind> Kinds => new() { ColorSchemeKind.Dark, ColorSchemeKind.Light };

        [Theory]
        [MemberData(nameof(Kinds))]
        public void ThePalettes_MeetTheirOwnTextRules(ColorSchemeKind kind)
        {
            LauncherPalette p = LauncherPalette.For(kind);

            Assert.True(ColorMath.Contrast(p.Text, p.Surface) >= 7);
            Assert.True(ColorMath.Contrast(p.Text, p.Background) >= 7);
            Assert.True(ColorMath.Contrast(p.Muted, p.Surface) >= 7);
            Assert.True(ColorMath.Contrast(p.Muted, p.Background) >= 7);
            Assert.True(ColorMath.Contrast(p.NoticeText, p.NoticeBackground) >= 7);
            Assert.True(ColorMath.Contrast(p.NoticeIcon, p.NoticeBackground) >= 3);
        }

        [Fact]
        public void TheContrastRatio_MatchesTheWcagDefinition()
        {
            Assert.Equal(21.0, ColorMath.Contrast(Hex("#000000"), Hex("#FFFFFF")), 6);
            Assert.Equal(1.0, ColorMath.Contrast(Hex("#336699"), Hex("#336699")), 6);
        }

        [Fact]
        public void AnAccentThatDisappearsIntoTheSurface_IsMoved_AndOneThatReadsIsLeftAlone()
        {
            Rgb dim = Hex("#202020");
            Rgb moved = ThemeReadability.ResolveAccent(dim, LauncherPalette.Dark);
            Assert.NotEqual(dim, moved);
            Assert.True(ColorMath.Contrast(moved, LauncherPalette.Dark.Surface) >= ThemeReadability.AccentMinimum);

            Rgb pale = Hex("#FAFAFA");
            Rgb darker = ThemeReadability.ResolveAccent(pale, LauncherPalette.Light);
            Assert.NotEqual(pale, darker);
            Assert.True(ColorMath.Contrast(darker, LauncherPalette.Light.Surface) >= ThemeReadability.AccentMinimum);

            Rgb fine = ThemeSpec.DefaultDarkAccent;
            Assert.Equal(fine, ThemeReadability.ResolveAccent(fine, LauncherPalette.Dark));
        }

        [Fact]
        public void TheDefaultTheme_NeedsNoAdjustment_InEitherScheme()
        {
            foreach (ColorSchemeKind kind in new[] { ColorSchemeKind.Dark, ColorSchemeKind.Light })
                Assert.Equal(ThemeAdjustments.None, ThemeReadability.Resolve(ThemeSpec.Default, kind).Adjustments);
        }

        [Fact]
        public void TheLabel_IsBlackOnALightFill_AndWhiteOnADarkOne()
        {
            ResolvedButton light = ThemeReadability.ResolveButton(Paint.Solid(Hex("#F0E0B0")), Paint.None, 0, Hex("#C8A24A"), LauncherPalette.Dark);
            Assert.Equal(ThemeReadability.DarkLabel, light.Label);

            ResolvedButton dark = ThemeReadability.ResolveButton(Paint.Solid(Hex("#102A5C")), Paint.None, 0, Hex("#C8A24A"), LauncherPalette.Light);
            Assert.Equal(ThemeReadability.LightLabel, dark.Label);
        }

        [Fact]
        public void AMidToneFill_IsMovedUntilItsLabelReadsAndItsEdgeShows()
        {
            foreach (LauncherPalette palette in Palettes)
            {
                ResolvedButton button = ThemeReadability.ResolveButton(Paint.Solid(Hex("#777777")), Paint.None, 0, Hex("#C8A24A"), palette);

                Assert.True(button.LabelContrast >= ThemeReadability.LabelMinimum);
                Assert.True(button.EdgeContrast >= ThemeReadability.EdgeMinimum);
            }
        }

        [Fact]
        public void ABorderThatMatchesTheSurface_IsMoved_AndSaysSo()
        {
            LauncherPalette palette = LauncherPalette.Dark;
            ResolvedButton button = ThemeReadability.ResolveButton(Paint.Solid(Hex("#C8A24A")), Paint.Solid(palette.Surface), 2, Hex("#C8A24A"), palette);

            Assert.True(button.EdgeContrast >= ThemeReadability.EdgeMinimum);
            Assert.True(button.Border.MinContrastWith(palette.Surface) >= ThemeReadability.EdgeMinimum);
            Assert.True(button.Adjustments.HasFlag(ThemeAdjustments.BorderMoved));
            Assert.Equal(2, button.BorderWidth);
        }

        [Fact]
        public void WithABorderOnly_TheLabelIsThePaletteText()
        {
            foreach (LauncherPalette palette in Palettes)
            {
                ResolvedButton button = ThemeReadability.ResolveButton(Paint.None, Paint.Solid(Hex("#E6C477")), 2, Hex("#C8A24A"), palette);

                Assert.Equal(PaintKind.None, button.Fill.Kind);
                Assert.Equal(palette.Text, button.Label);
                Assert.True(button.LabelContrast >= 7);
                Assert.True(button.EdgeContrast >= ThemeReadability.EdgeMinimum);
            }
        }

        [Fact]
        public void ABorderOfWidthZero_IsNoBorder()
        {
            ResolvedButton button = ThemeReadability.ResolveButton(Paint.Solid(Hex("#C8A24A")), Paint.Solid(Hex("#E6C477")), 0, Hex("#C8A24A"), LauncherPalette.Dark);

            Assert.Equal(PaintKind.None, button.Border.Kind);
            Assert.Equal(0, button.BorderWidth);
        }

        [Fact]
        public void NoFillAndNoBorder_BecomesAPlainAccentFill()
        {
            ResolvedButton button = ThemeReadability.ResolveButton(Paint.None, Paint.None, 0, Hex("#C8A24A"), LauncherPalette.Dark);

            Assert.Equal(PaintKind.Solid, button.Fill.Kind);
            Assert.True(button.Adjustments.HasFlag(ThemeAdjustments.FillReplacedByAccent));
        }

        [Fact]
        public void AGradient_IsHeldToTheRulesAlongItsWholeLength()
        {
            foreach (LauncherPalette palette in Palettes)
            {
                Paint fill = Paint.Gradient(Hex("#FF0000"), Hex("#0000FF"), GradientDirection.Horizontal);
                ResolvedButton button = ThemeReadability.ResolveButton(fill, Paint.None, 0, Hex("#C8A24A"), palette);

                foreach (Rgb sample in button.Fill.Samples())
                    Assert.True(ColorMath.Contrast(sample, button.Label) >= ThemeReadability.LabelMinimum);
                Assert.True(button.EdgeContrast >= ThemeReadability.EdgeMinimum);
            }
        }

        [Fact]
        public void EveryFillOnAGrid_EndsUpReadable_WithAndWithoutABorder()
        {
            foreach (LauncherPalette palette in Palettes)
                foreach (byte r in Levels)
                    foreach (byte g in Levels)
                        foreach (byte b in Levels)
                        {
                            var color = new Rgb(r, g, b);
                            Paint fill = Paint.Solid(color);

                            ResolvedButton plain = ThemeReadability.ResolveButton(fill, Paint.None, 0, color, palette);
                            Assert.True(plain.LabelContrast >= ThemeReadability.LabelMinimum, $"label {color} on {palette.Kind}");
                            Assert.True(plain.EdgeContrast >= ThemeReadability.EdgeMinimum, $"edge {color} on {palette.Kind}");

                            ResolvedButton bordered = ThemeReadability.ResolveButton(fill, Paint.Solid(color), 2, color, palette);
                            Assert.True(bordered.LabelContrast >= ThemeReadability.LabelMinimum, $"bordered label {color} on {palette.Kind}");
                            Assert.True(bordered.EdgeContrast >= ThemeReadability.EdgeMinimum, $"bordered edge {color} on {palette.Kind}");
                        }
        }

        [Fact]
        public void RandomGradients_EndUpReadable_AlongTheirWholeLength()
        {
            var random = new Random(20261027);
            foreach (LauncherPalette palette in Palettes)
                for (int i = 0; i < 300; i++)
                {
                    Rgb from = new Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                    Rgb to = new Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                    bool withBorder = random.Next(2) == 0;

                    ResolvedButton button = ThemeReadability.ResolveButton(
                        Paint.Gradient(from, to, GradientDirection.Diagonal),
                        withBorder ? Paint.Gradient(to, from, GradientDirection.Vertical) : Paint.None,
                        withBorder ? 3 : 0, from, palette);

                    Assert.True(button.LabelContrast >= ThemeReadability.LabelMinimum, $"label {from}->{to}");
                    Assert.True(button.EdgeContrast >= ThemeReadability.EdgeMinimum, $"edge {from}->{to}");
                    foreach (Rgb sample in button.Fill.Samples())
                        Assert.True(ColorMath.Contrast(sample, button.Label) >= ThemeReadability.LabelMinimum, $"sample {from}->{to}");
                }
        }

        [Fact]
        public void ResolvingATheme_MovesTheAccent_WithoutTouchingTheOtherScheme()
        {
            var theme = new ThemeSpec
            {
                Layout = LayoutTemplate.Classic,
                Online = OnlineSide.Left,
                Font = ThemeSpec.DefaultFont,
                ButtonRadius = 6,
                ButtonBorderWidth = 0,
                Dark = new SchemeSpec(Hex("#FFFFFF"), Paint.Solid(Hex("#FFFFFF")), Paint.None, ArtSpec.Default),
                Light = new SchemeSpec(Hex("#FFFFFF"), Paint.Solid(Hex("#FFFFFF")), Paint.None, ArtSpec.Default)
            };

            ResolvedScheme dark = ThemeReadability.Resolve(theme, ColorSchemeKind.Dark);
            ResolvedScheme light = ThemeReadability.Resolve(theme, ColorSchemeKind.Light);

            Assert.Equal(Hex("#FFFFFF"), dark.Accent);
            Assert.False(dark.Adjustments.HasFlag(ThemeAdjustments.AccentMoved));
            Assert.NotEqual(Hex("#FFFFFF"), light.Accent);
            Assert.True(light.Adjustments.HasFlag(ThemeAdjustments.AccentMoved));
            Assert.True(ColorMath.Contrast(light.Accent, LauncherPalette.Light.Surface) >= ThemeReadability.AccentMinimum);
        }

        [Fact]
        public void TheDarkeningLayer_NeedsNothingOverBlack_AndTheMostOverWhite()
        {
            Assert.Equal(0, ArtDarkening.Percent(Hex("#000000")));
            Assert.Equal(55, ArtDarkening.Percent(Hex("#FFFFFF")));
        }

        [Fact]
        public void TheDarkeningLayer_IsTheFirstStepThatReaches45To1_ForEveryGrey()
        {
            int previous = 0;
            for (int level = 0; level <= 255; level++)
            {
                var grey = new Rgb((byte)level, (byte)level, (byte)level);
                int percent = ArtDarkening.Percent(grey);

                Assert.InRange(percent, 0, ArtDarkening.MaxPercent);
                Assert.Equal(0, percent % ArtDarkening.StepPercent);
                Assert.True(percent >= previous, "a brighter picture never needs a lighter layer");
                previous = percent;

                var white = new Rgb(255, 255, 255);
                Assert.True(ColorMath.Contrast(white, ColorMath.Scale(grey, 1 - percent / 100.0)) >= ArtDarkening.TextMinimum);
                if (percent > 0)
                    Assert.True(ColorMath.Contrast(white, ColorMath.Scale(grey, 1 - (percent - ArtDarkening.StepPercent) / 100.0)) < ArtDarkening.TextMinimum);
            }
        }

        [Fact]
        public void TheBrightestCell_IsFoundInsideTheArea_AndOneStrayPixelDoesNotDecide()
        {
            const int size = 16;
            byte[] pixels = Frame(size, 0x20);
            Block(pixels, size, 8, 8, 4, 4, 0xFF);

            Assert.Equal(new Rgb(0xFF, 0xFF, 0xFF), ArtDarkening.BrightestCell(pixels, size, size, 0, 0, size, size, 4));
            Assert.Equal(new Rgb(0x20, 0x20, 0x20), ArtDarkening.BrightestCell(pixels, size, size, 0, 0, 8, size, 4));

            byte[] stray = Frame(size, 0x20);
            Block(stray, size, 5, 5, 1, 1, 0xFF);
            Rgb average = ArtDarkening.BrightestCell(stray, size, size, 0, 0, size, size, 4);
            Assert.InRange(average.R, 0x20, 0x30);
        }

        [Fact]
        public void APixelBuffer_ThatDoesNotMatchItsSize_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => ArtDarkening.BrightestCell(new byte[8], 4, 4, 0, 0, 4, 4, 2));
        }

        private static byte[] Frame(int size, byte level)
        {
            var pixels = new byte[size * size * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = level;
                pixels[i + 3] = 0xFF;
            }
            return pixels;
        }

        private static void Block(byte[] pixels, int size, int x, int y, int width, int height, byte level)
        {
            for (int row = y; row < y + height; row++)
                for (int column = x; column < x + width; column++)
                {
                    int i = (row * size + column) * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = level;
                }
        }
    }
}
