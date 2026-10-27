using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ColorSchemeSettingTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("color-scheme-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Theory]
        [InlineData(ColorSchemeSetting.System, true, ColorSchemeKind.Dark)]
        [InlineData(ColorSchemeSetting.System, false, ColorSchemeKind.Light)]
        [InlineData(ColorSchemeSetting.Light, true, ColorSchemeKind.Light)]
        [InlineData(ColorSchemeSetting.Dark, false, ColorSchemeKind.Dark)]
        public void TheEffectiveScheme_FollowsTheSettingAndOtherwiseTheSystem(ColorSchemeSetting setting, bool systemIsDark, ColorSchemeKind expected)
        {
            Assert.Equal(expected, ColorSchemeSettings.Effective(setting, systemIsDark));
        }

        [Theory]
        [InlineData(null, ColorSchemeSetting.System)]
        [InlineData("", ColorSchemeSetting.System)]
        [InlineData("sepia", ColorSchemeSetting.System)]
        [InlineData("light", ColorSchemeSetting.Light)]
        [InlineData(" DARK ", ColorSchemeSetting.Dark)]
        public void AnUnknownValue_MeansSystem(string? value, ColorSchemeSetting expected)
        {
            Assert.Equal(expected, ColorSchemeSettings.Parse(value));
        }

        [Fact]
        public void TheChoice_DefaultsToSystem_AndIsRememberedInConfig()
        {
            string path = Path.Combine(_dir, "config.ini");
            var config = new IniFile(path);
            Assert.Equal(ColorSchemeSetting.System, LauncherPaths.ColorScheme(config));

            LauncherPaths.SetColorScheme(config, ColorSchemeSetting.Dark);

            var reloaded = new IniFile(path);
            reloaded.Load();
            Assert.Equal(ColorSchemeSetting.Dark, LauncherPaths.ColorScheme(reloaded));
        }
    }
}
