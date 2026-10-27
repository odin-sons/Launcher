namespace Odinsons.ValheimLauncher
{
    /// <summary>The player's choice of light or dark. System, the default, follows the operating system.</summary>
    public enum ColorSchemeSetting { System, Light, Dark }

    public static class ColorSchemeSettings
    {
        public const string Key = "ColorScheme";

        public static ColorSchemeKind Effective(ColorSchemeSetting setting, bool systemIsDark) => setting switch
        {
            ColorSchemeSetting.Light => ColorSchemeKind.Light,
            ColorSchemeSetting.Dark => ColorSchemeKind.Dark,
            _ => systemIsDark ? ColorSchemeKind.Dark : ColorSchemeKind.Light
        };

        public static ColorSchemeSetting Parse(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "light" => ColorSchemeSetting.Light,
            "dark" => ColorSchemeSetting.Dark,
            _ => ColorSchemeSetting.System
        };

        public static string Format(ColorSchemeSetting setting) => setting switch
        {
            ColorSchemeSetting.Light => "light",
            ColorSchemeSetting.Dark => "dark",
            _ => "system"
        };
    }
}
