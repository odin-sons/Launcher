using Avalonia.Headless.XUnit;
using Odinsons.ValheimLauncher.Avalonia.Views;
using Xunit;

namespace Launcher.Avalonia.Tests
{
    /// <summary>
    /// Catches the class of bug that "dotnet build" can't: MainWindow.axaml is parsed at
    /// runtime, not compile time, so a bad value in it (an unrecognized enum string, a
    /// null-reference in a handler that fires mid-InitializeComponent, …) only surfaces the
    /// first time the window is actually constructed — which used to mean the first time a
    /// player launched the real exe. Constructing it here, headless, catches that in CI
    /// instead. No Show()/Loaded, so it never reaches the network calls that follow.
    /// </summary>
    public class MainWindowSmokeTests
    {
        [AvaloniaFact]
        public void MainWindow_Constructs_WithoutThrowing()
        {
            var window = new MainWindow();

            Assert.NotNull(window);
        }

        /// <summary>
        /// BuildActionButtons (the Start/Stop buttons' sliced-image content — a dozen PNG
        /// decodes plus FormattedText measurement) moved out of the constructor and into
        /// Loaded for startup latency, so the test above no longer exercises it at all — Loaded
        /// isn't fired here, deliberately, to avoid its network calls. Calling it directly
        /// (internal, see AssemblyInfo.Tests.cs) keeps that coverage without pulling those in.
        /// </summary>
        [AvaloniaFact]
        public void BuildActionButtons_RunsWithoutThrowing()
        {
            var window = new MainWindow();

            window.BuildActionButtons();
        }
    }
}
