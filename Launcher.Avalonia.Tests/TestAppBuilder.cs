using Avalonia;
using Avalonia.Headless;
using Odinsons.ValheimLauncher.Avalonia;

[assembly: AvaloniaTestApplication(typeof(Launcher.Avalonia.Tests.TestAppBuilder))]

namespace Launcher.Avalonia.Tests
{
    public class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
