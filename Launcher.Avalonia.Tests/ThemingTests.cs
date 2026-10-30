using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Odinsons.ValheimLauncher;
using Odinsons.ValheimLauncher.Avalonia.Controls;
using Odinsons.ValheimLauncher.Avalonia.Theming;
using Odinsons.ValheimLauncher.Avalonia.Views;
using SkiaSharp;
using Xunit;

namespace Launcher.Avalonia.Tests
{
    /// <summary>
    /// The four layout templates and the two colour schemes on a real window, built headless: where each
    /// zone sits, which palette is applied, and the readability and trust guarantees. Nothing is drawn to
    /// a screen, so how it looks is for a person to judge; these tests hold the structure.
    /// </summary>
    [Collection("WorkingDirectory")]
    public sealed class ThemingTests : IDisposable
    {
        private static readonly ServerProfile Community = new("community", "Community", new[] { "https://mirror.test/" });

        private readonly string _previousDirectory = Environment.CurrentDirectory;
        private readonly string _root = Directory.CreateTempSubdirectory("odinsons-theming-").FullName;

        public ThemingTests() => Environment.CurrentDirectory = _root;

        public void Dispose()
        {
            Environment.CurrentDirectory = _previousDirectory;
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        public static IEnumerable<object[]> TemplatesAndSchemes() =>
            from template in Enum.GetValues<LayoutTemplate>()
            from scheme in new[] { ColorSchemeSetting.Dark, ColorSchemeSetting.Light }
            select new object[] { template, scheme };

        public static IEnumerable<object[]> Templates() =>
            Enum.GetValues<LayoutTemplate>().Select(t => new object[] { t });

        private static LoadedTheme ThemeOf(LayoutTemplate layout, OnlineSide online = OnlineSide.Left,
            SchemeSpec? dark = null, SchemeSpec? light = null) => new()
        {
            Spec = new ThemeSpec
            {
                Layout = layout,
                Online = online,
                Font = ThemeSpec.DefaultFont,
                ButtonRadius = 6,
                ButtonBorderWidth = 0,
                Dark = dark ?? ThemeSpec.DefaultScheme(ColorSchemeKind.Dark),
                Light = light ?? ThemeSpec.DefaultScheme(ColorSchemeKind.Light)
            },
            Images = new Dictionary<string, byte[]>(),
            Problems = Array.Empty<string>()
        };

        private static MainWindow Open(LoadedTheme theme, ColorSchemeSetting scheme)
        {
            var window = new MainWindow(Community);
            window.Show();
            window.UseTheme(theme);
            window.UseColorScheme(scheme);
            window.BuildActionButtons();
            window.UpdateLayout();
            return window;
        }

        private static void Select(MainWindow window, TabItem tab)
        {
            window.MainTabControl.SelectedItem = tab;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        private static Color ColorOf(IBrush brush) => ((ISolidColorBrush)brush).Color;

        [AvaloniaTheory]
        [MemberData(nameof(TemplatesAndSchemes))]
        public void EveryTemplate_BuildsAllSteps_InBothSchemes(LayoutTemplate template, ColorSchemeSetting scheme)
        {
            MainWindow window = Open(ThemeOf(template), scheme);
            ColorSchemeKind kind = scheme == ColorSchemeSetting.Dark ? ColorSchemeKind.Dark : ColorSchemeKind.Light;

            Assert.Equal(kind, window.ThemeState.Kind);
            Assert.Equal(LauncherBrushes.ToColor(LauncherPalette.For(kind).Surface), LauncherBrushes.Surface.Color);
            Assert.Equal(kind == ColorSchemeKind.Dark ? ThemeVariantName.Dark : ThemeVariantName.Light,
                Application.Current!.RequestedThemeVariant!.Key.ToString());

            foreach ((TabItem tab, StepFrame frame) in new[]
                     {
                         (window.ServerTab, window.ServerStepFrame),
                         (window.ModsTab, window.ModsStepFrame),
                         (window.InstallTab, window.InstallStepFrame)
                     })
            {
                Select(window, tab);
                Assert.Equal(FrameLayout.Plan(template, frame.Step, OnlineSide.Left), frame.Plan);
                Assert.True(frame.Bounds.Width > 0 && frame.Bounds.Height > 0);
                if (frame.Plan.Art != null)
                {
                    Assert.True(frame.Art.IsVisible);
                    Assert.True(frame.Art.Bounds.Width > 0 && frame.Art.Bounds.Height > 0);
                }
                else
                {
                    Assert.False(frame.Art.IsVisible);
                }
            }
        }

        [AvaloniaTheory]
        [MemberData(nameof(Templates))]
        public void ServerStep_PlacesTheOnlineListAtTheNamedSide(LayoutTemplate template)
        {
            foreach (OnlineSide side in Enum.GetValues<OnlineSide>())
            {
                MainWindow window = Open(ThemeOf(template, side), ColorSchemeSetting.Dark);
                Select(window, window.ServerTab);
                StepFrame frame = window.ServerStepFrame;

                double listMiddle = frame.Online!.Bounds.X + frame.Online.Bounds.Width / 2;
                double frameMiddle = frame.Bounds.Width / 2;
                Assert.Equal(side == OnlineSide.Left, listMiddle < frameMiddle);
                Assert.True(frame.Online.Bounds.Height >= frame.Bounds.Height - 1, "the Online list is a full-height column");
            }
        }

        [AvaloniaFact]
        public void ServerStep_ArrangesTheZonesTheWayEachTemplateSays()
        {
            MainWindow classic = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.Dark);
            Select(classic, classic.ServerTab);
            Assert.True(classic.ServerStepFrame.Art.Bounds.X < classic.ServerStepFrame.AboutNews!.Bounds.X);
            Assert.True(classic.AboutZone.Bounds.Y < classic.NewsZone.Bounds.Y);

            MainWindow sidebar = Open(ThemeOf(LayoutTemplate.Sidebar), ColorSchemeSetting.Dark);
            Select(sidebar, sidebar.ServerTab);
            Assert.True(sidebar.ServerStepFrame.AboutNews!.Bounds.X < sidebar.ServerStepFrame.Art.Bounds.X);
            Assert.True(sidebar.AboutZone.Bounds.Y < sidebar.NewsZone.Bounds.Y);

            MainWindow banner = Open(ThemeOf(LayoutTemplate.Banner), ColorSchemeSetting.Dark);
            Select(banner, banner.ServerTab);
            Assert.True(banner.ServerStepFrame.Art.Bounds.Y < banner.ServerStepFrame.AboutNews!.Bounds.Y);
            Assert.True(banner.AboutZone.Bounds.X < banner.NewsZone.Bounds.X);

            MainWindow minimal = Open(ThemeOf(LayoutTemplate.Minimal), ColorSchemeSetting.Dark);
            Select(minimal, minimal.ServerTab);
            Assert.False(minimal.NewsZone.IsVisible);
            Assert.True(minimal.ServerRightColumn.Bounds.Width < minimal.ServerStepFrame.Art.Bounds.Width);
        }

        [AvaloniaFact]
        public void OtherSteps_FrameTheirContentAndChooseOneOrTwoColumns()
        {
            foreach ((LayoutTemplate template, bool twoColumns) in new[]
                     {
                         (LayoutTemplate.Classic, false), (LayoutTemplate.Sidebar, false),
                         (LayoutTemplate.Banner, true), (LayoutTemplate.Minimal, true)
                     })
            {
                MainWindow window = Open(ThemeOf(template), ColorSchemeSetting.Dark);
                Select(window, window.InstallTab);

                Assert.Equal(twoColumns, window.InstallStepFrame.Plan.TwoColumns);
                Assert.Equal(twoColumns, window.InstallRightColumn.Children.Contains(window.InstallStepsBox));
                Assert.Equal(twoColumns, window.InstallRightColumn.IsVisible);
            }
        }

        [AvaloniaFact]
        public void TheHeader_ShowsTheProductNameFromAResource_NotFromTheProfile()
        {
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.Light);
            Application.Current!.TryGetResource("ProductName", null, out object? name);

            Assert.Equal("Odinsons Launcher", name);
            Assert.Equal(name, window.ProductNameText.Text);
            Assert.NotEqual(Community.Name, window.ProductNameText.Text);
            Assert.Equal("by OdinSons", window.ProductBylineText.Text);
        }

        [AvaloniaFact]
        public void TheHeader_StaysDark_InTheLightScheme()
        {
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.Light);

            Assert.Equal(LauncherBrushes.ToColor(LauncherPalette.Dark.Background), ColorOf((IBrush)window.HeaderScope.Resources["HeaderBackground"]!));
            Assert.Equal(LauncherBrushes.ToColor(LauncherPalette.Dark.Text), ColorOf((IBrush)window.HeaderScope.Resources[LauncherBrushes.TextKey]!));
            Assert.Equal(LauncherBrushes.ToColor(LauncherPalette.Light.Surface), LauncherBrushes.Surface.Color);
        }

        [AvaloniaTheory]
        [InlineData(RegistryStatus.Approved)]
        [InlineData(RegistryStatus.NeedsApproval)]
        [InlineData(RegistryStatus.Suspended)]
        public void TrustIcon_HasANameAndATooltip_ForEveryStatus(RegistryStatus status)
        {
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.Light);

            window.ShowTrust(status);

            TrustIcon icon = window.TrustStatusIcon;
            Assert.True(icon.IsVisible);
            string? name = global::Avalonia.Automation.AutomationProperties.GetName(icon);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(icon) as string));
        }

        [AvaloniaFact]
        public void TrustIcon_HasADifferentShapeForEachStatus()
        {
            var icon = new TrustIcon();
            var shapes = new List<string>();
            foreach (RegistryStatus status in Enum.GetValues<RegistryStatus>())
            {
                icon.Show(status);
                var canvas = (Canvas)((Viewbox)icon.Child!).Child!;
                shapes.Add(string.Join("|", canvas.Children.Select(c => c.GetType().Name + c.Bounds)));
                shapes[^1] += canvas.Children.Count;
            }

            Assert.Equal(3, shapes.Distinct().Count());
        }

        [AvaloniaFact]
        public void TrustIcon_FollowsTheRegistryNotice()
        {
            Assert.Equal(RegistryStatus.Approved, TrustIcon.FromNotice(ServerNotice.None));
            Assert.Equal(RegistryStatus.NeedsApproval, TrustIcon.FromNotice(ServerNotice.NeedsApproval));
            Assert.Equal(RegistryStatus.Suspended, TrustIcon.FromNotice(ServerNotice.Suspended));
            Assert.Null(TrustIcon.FromNotice(ServerNotice.RegistryUnavailable));
        }

        [AvaloniaTheory]
        [InlineData(ColorSchemeSetting.Dark)]
        [InlineData(ColorSchemeSetting.Light)]
        public void ReadabilityRules_ApplyPerScheme_AgainstItsOwnPalette(ColorSchemeSetting scheme)
        {
            var yellow = new SchemeSpec(Rgb.Parse("#FFFF00"), Paint.Gradient(Rgb.Parse("#FFFF00"), Rgb.Parse("#FFEE99"), GradientDirection.Horizontal),
                Paint.None, ArtSpec.Default);
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic, dark: yellow, light: yellow), scheme);

            ResolvedScheme resolved = window.ThemeState.Scheme;
            LauncherPalette palette = resolved.Palette;
            Assert.True(ColorMath.Contrast(resolved.Accent, palette.Surface) >= ThemeReadability.AccentMinimum);
            Assert.True(resolved.Button.LabelContrast >= ThemeReadability.LabelMinimum);
            Assert.True(resolved.Button.EdgeContrast >= ThemeReadability.EdgeMinimum);
            Assert.Equal(LauncherBrushes.ToColor(resolved.Accent), LauncherBrushes.Accent.Color);

            Application.Current!.TryGetResource(ThemeController.WizardLabelKey, null, out object? label);
            Assert.Equal(LauncherBrushes.ToColor(resolved.Button.Label), ColorOf((IBrush)label!));
        }

        [AvaloniaFact]
        public void TheWizardButton_TakesItsShapeFromTheTheme()
        {
            LoadedTheme theme = ThemeOf(LayoutTemplate.Classic);
            var spec = new ThemeSpec
            {
                Layout = theme.Spec.Layout, Online = theme.Spec.Online, Font = theme.Spec.Font,
                ButtonRadius = 12, ButtonBorderWidth = 3, Dark = theme.Spec.Dark with { ButtonBorder = Paint.Solid(Rgb.Parse("#E6C477")) },
                Light = theme.Spec.Light
            };
            MainWindow window = Open(new LoadedTheme { Spec = spec, Images = theme.Images, Problems = theme.Problems }, ColorSchemeSetting.Dark);

            Application.Current!.TryGetResource(ThemeController.WizardRadiusKey, null, out object? radius);
            Application.Current.TryGetResource(ThemeController.WizardBorderWidthKey, null, out object? width);
            Assert.Equal(new CornerRadius(12), radius);
            Assert.Equal(new Thickness(3), width);
        }

        [AvaloniaFact]
        public void TheSchemeChoice_IsSavedInConfig_AndRestoredByTheNextWindow()
        {
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.System);
            window.ColorSchemeSelector.SelectedIndex = (int)ColorSchemeSetting.Dark;

            var config = new IniFile(Path.Combine(_root, "config.ini"));
            config.Load();
            Assert.Equal(ColorSchemeSetting.Dark, LauncherPaths.ColorScheme(config));

            var next = new MainWindow(Community);
            Assert.Equal(ColorSchemeSetting.Dark, next.ThemeState.Setting);
            Assert.Equal(ColorSchemeKind.Dark, next.ThemeState.Kind);
        }

        [AvaloniaFact]
        public void System_FollowsTheOperatingSystem()
        {
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.System);

            Assert.Equal(window.ThemeState.SystemIsDark ? ColorSchemeKind.Dark : ColorSchemeKind.Light, window.ThemeState.Kind);
        }

        [AvaloniaFact]
        public void TheSchemeSelector_SitsNextToTheLanguageSelector_WithThreeChoices()
        {
            MainWindow window = Open(ThemeOf(LayoutTemplate.Classic), ColorSchemeSetting.Dark);

            Assert.Same(window.ColorSchemeSelector.Parent, window.LanguageSelector.Parent);
            Assert.Equal(3, window.ColorSchemeSelector.ItemCount);
        }

        [AvaloniaFact]
        public void Darkening_FollowsTheBrightestPartOfThePictureThatTheFrameShows()
        {
            ArtPicture picture = SplitPicture();
            var frame = new Size(100, 100);
            var text = new Rect(10, 70, 80, 20);

            int showingDark = picture.DarkeningFor(frame, ArtPosition.Left, text);
            int showingBright = picture.DarkeningFor(frame, ArtPosition.Right, text);

            Assert.Equal(0, showingDark);
            Assert.Equal(ArtDarkening.Percent(new Rgb(255, 255, 255)), showingBright);
            Assert.True(showingBright <= 55);
        }

        [AvaloniaFact]
        public void Darkening_OfAColourOrGradient_UsesItsBrightestPoint()
        {
            var gradient = Paint.Gradient(Rgb.Parse("#101010"), Rgb.Parse("#FFFFFF"), GradientDirection.Horizontal);

            Assert.Equal(ArtDarkening.Percent(Rgb.Parse("#FFFFFF")), ArtPicture.DarkeningFor(gradient));
            Assert.Equal(0, ArtPicture.DarkeningFor(Paint.Solid(Rgb.Parse("#101010"))));
        }

        [AvaloniaFact]
        public void TheArt_DarkensItsOwnFrame_WhenAThemePicturePutsWhiteBehindTheText()
        {
            string path = "theme/art.png";
            var scheme = new SchemeSpec(ThemeSpec.DefaultDarkAccent, Paint.Solid(ThemeSpec.DefaultDarkAccent), Paint.None,
                ArtSpec.FromImage(path, ArtPosition.Right));
            LoadedTheme theme = ThemeOf(LayoutTemplate.Classic, dark: scheme, light: scheme);
            var images = new Dictionary<string, byte[]> { [path] = SplitPng() };
            MainWindow window = Open(new LoadedTheme { Spec = theme.Spec, Images = images, Problems = theme.Problems }, ColorSchemeSetting.Dark);

            Select(window, window.ServerTab);

            Assert.True(window.ServerStepFrame.Art.DarkeningPercent > 0);
            Assert.True(window.ServerStepFrame.Art.TextArea.Width > 0);
        }

        private static ArtPicture SplitPicture() => ArtPicture.FromBytes(SplitPng())!;

        private static byte[] SplitPng()
        {
            using var bitmap = new SKBitmap(200, 100);
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 200; x++)
                    bitmap.SetPixel(x, y, x < 100 ? SKColors.Black : SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static class ThemeVariantName
        {
            public const string Dark = "Dark";
            public const string Light = "Light";
        }
    }

    [CollectionDefinition("WorkingDirectory", DisableParallelization = true)]
    public sealed class WorkingDirectoryCollection
    {
    }
}
