using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class LocTests
    {
        [Theory]
        [InlineData(TargetOs.Windows, "valheim.exe")]
        [InlineData(TargetOs.MacOS, "valheim.app")]
        [InlineData(TargetOs.Linux, "valheim.x86_64")]
        public void ValheimExeNotFoundMessage_NamesThisOssExecutable(TargetOs os, string expectedName)
        {
            // Regression: the message used to say "valheim.exe" on every OS, even though the
            // actual file the launcher looked for (and the player should go check for) is
            // named differently on macOS/Linux. Call order matches the real call sites in
            // MainWindow.axaml.cs / MainWindow.xaml.cs: executable name, then folder.
            using var _ = RuntimePlatform.Pretend(os);

            string message = Loc.T("gui.valheimExeNotFound", InjectorLauncher.PrimaryExecutableName, "clients/Lite_v2");

            Assert.Contains(expectedName, message);
            Assert.Contains("clients/Lite_v2", message);
            if (os != TargetOs.Windows) Assert.DoesNotContain("valheim.exe", message);
        }
    }
}
