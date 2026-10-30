using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Odinsons.ValheimLauncher.Avalonia.Theming
{
    /// <summary>
    /// Holds the player's colour scheme setting and the profile's theme, resolves them against the
    /// launcher's palette with the readability rules, and publishes the result as application resources.
    /// </summary>
    internal sealed class ThemeController : IDisposable
    {
        public const string WizardFillKey = "WizardFill";
        public const string WizardBorderKey = "WizardBorder";
        public const string WizardLabelKey = "WizardLabel";
        public const string WizardHoverKey = "WizardHover";
        public const string WizardBorderWidthKey = "WizardBorderWidth";
        public const string WizardRadiusKey = "WizardRadius";

        private readonly Application _application;
        private readonly Dictionary<string, ArtPicture?> _pictures = new(StringComparer.Ordinal);
        private IPlatformSettings? _platformSettings;

        public ThemeController(Application application, ColorSchemeSetting setting, LoadedTheme? theme = null)
        {
            _application = application;
            Setting = setting;
            Loaded = theme ?? NewDefault();

            LauncherBrushes.Register(application.Resources);
            _platformSettings = application.PlatformSettings;
            if (_platformSettings != null)
                _platformSettings.ColorValuesChanged += OnSystemColorsChanged;

            Recompute();
        }

        public event Action? Changed;

        public LoadedTheme Loaded { get; private set; }
        public ThemeSpec Spec => Loaded.Spec;
        public ColorSchemeSetting Setting { get; private set; }
        public ColorSchemeKind Kind { get; private set; }
        public ResolvedScheme Scheme { get; private set; } = null!;

        public bool SystemIsDark =>
            _platformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

        public static LoadedTheme NewDefault() => new LoadedTheme
        {
            Spec = ThemeSpec.Default,
            Images = new Dictionary<string, byte[]>(),
            Problems = Array.Empty<string>()
        };

        public void SetSetting(ColorSchemeSetting setting)
        {
            if (setting == Setting)
                return;
            Setting = setting;
            Recompute();
        }

        public void SetTheme(LoadedTheme theme)
        {
            Loaded = theme;
            _pictures.Clear();
            Recompute();
        }

        /// <summary>The picture behind the art of the active scheme; null when the art is a colour or a gradient.</summary>
        public ArtPicture? ArtPicture
        {
            get
            {
                ArtSpec art = Scheme.Art;
                if (art.Kind == ArtKind.Paint)
                    return null;
                return art.Kind == ArtKind.Image && Picture(art.File) is { } picture ? picture : Theming.ArtPicture.Default;
            }
        }

        public ArtPicture? Logo => Spec.LogoFile == null ? null : Picture(Spec.LogoFile);

        private ArtPicture? Picture(string path)
        {
            if (_pictures.TryGetValue(path, out ArtPicture? known))
                return known;
            ArtPicture? picture = Loaded.Images.TryGetValue(path, out byte[]? bytes) ? ArtPicture.FromBytes(bytes) : null;
            _pictures[path] = picture;
            return picture;
        }

        private void OnSystemColorsChanged(object? sender, PlatformColorValues e)
        {
            if (Setting != ColorSchemeSetting.System)
                return;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(Recompute);
        }

        private void Recompute()
        {
            Kind = ColorSchemeSettings.Effective(Setting, SystemIsDark);
            Scheme = ThemeReadability.Resolve(Spec, Kind);

            LauncherBrushes.Apply(Scheme.Palette, Scheme.Accent);
            Publish(_application.Resources, Scheme, Spec);
            _application.RequestedThemeVariant = Kind == ColorSchemeKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Changed?.Invoke();
        }

        private static void Publish(IResourceDictionary resources, ResolvedScheme scheme, ThemeSpec spec)
        {
            ResolvedButton button = scheme.Button;
            resources[WizardFillKey] = ThemeBrushes.FromPaint(button.Fill);
            resources[WizardBorderKey] = ThemeBrushes.FromPaint(button.Border);
            resources[WizardLabelKey] = new SolidColorBrush(LauncherBrushes.ToColor(button.Label));
            bool darkLabel = button.Label == ThemeReadability.DarkLabel;
            resources[WizardHoverKey] = new SolidColorBrush(darkLabel ? Colors.White : Colors.Black, 0.16);
            resources[WizardBorderWidthKey] = new Thickness(button.BorderWidth);
            resources[WizardRadiusKey] = new CornerRadius(spec.ButtonRadius);

            resources["SystemControlHighlightAccentBrush"] = LauncherBrushes.Accent;
            resources["TabItemHeaderSelectedPipeFill"] = LauncherBrushes.Accent;
            resources["ProgressBarForeground"] = LauncherBrushes.Accent;

            Rgb accent = scheme.Accent;
            resources["SystemAccentColor"] = LauncherBrushes.ToColor(accent);
            resources["SystemAccentColorLight1"] = LauncherBrushes.ToColor(ColorMath.ShiftLightness(accent, 0.06));
            resources["SystemAccentColorLight2"] = LauncherBrushes.ToColor(ColorMath.ShiftLightness(accent, 0.12));
            resources["SystemAccentColorLight3"] = LauncherBrushes.ToColor(ColorMath.ShiftLightness(accent, 0.18));
            resources["SystemAccentColorDark1"] = LauncherBrushes.ToColor(ColorMath.ShiftLightness(accent, -0.06));
            resources["SystemAccentColorDark2"] = LauncherBrushes.ToColor(ColorMath.ShiftLightness(accent, -0.12));
            resources["SystemAccentColorDark3"] = LauncherBrushes.ToColor(ColorMath.ShiftLightness(accent, -0.18));
        }

        public void Dispose()
        {
            if (_platformSettings != null)
                _platformSettings.ColorValuesChanged -= OnSystemColorsChanged;
            _platformSettings = null;
        }
    }
}
