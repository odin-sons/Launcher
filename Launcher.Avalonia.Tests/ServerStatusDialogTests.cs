using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Launcher.Tests;
using Odinsons.ValheimLauncher;
using Odinsons.ValheimLauncher.Avalonia.Views;
using Xunit;

namespace Launcher.Avalonia.Tests
{
    /// <summary>
    /// What the window does with each answer of the registry before an update or launch: the dialogs
    /// that appear and what the buttons do. Starting the game itself is not exercised here.
    /// </summary>
    public sealed class ServerStatusDialogTests : IDisposable
    {
        private static readonly ServerProfile Community = new("community", "Community", new[] { "https://server.example.test/Launcher/" });

        private readonly string _root = Directory.CreateTempSubdirectory("odinsons-gate-").FullName;
        private readonly TestRegistry.SwitchableFetcher _fetcher = new();

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private string ProfileFolder => Path.Combine(_root, "clients", "community", "main");

        private void LayInstalledProfile()
        {
            Directory.CreateDirectory(Path.Combine(ProfileFolder, "BepInEx", "plugins"));
            File.WriteAllText(Path.Combine(ProfileFolder, "BepInEx", "plugins", "mod.dll"), "mod");
        }

        private MainWindow WindowFor(ServerProfile profile, string status)
        {
            _fetcher.Body = TestRegistry.Index(1, status);
            var window = new MainWindow(profile) { ClientFolder = ProfileFolder };
            window.UseServerAccess(new ServerAccess(
                TestRegistry.Gate(Path.Combine(_root, "registry"), _fetcher), new ProfileQuarantine()));
            window.Show();
            return window;
        }

        private static async Task<T> Dialog<T>(Window owner) where T : Window
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                T? found = owner.OwnedWindows.OfType<T>().FirstOrDefault();
                if (found != null) return found;
                await Task.Delay(10);
            }
            throw new TimeoutException($"{typeof(T).Name} did not appear");
        }

        private static void Click(Window dialog, string buttonName) =>
            dialog.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        [AvaloniaFact]
        public async Task Suspended_OffersToRemove_AndKeepingLeavesEverything()
        {
            LayInstalledProfile();
            MainWindow window = WindowFor(Community, "suspended");

            Task<bool> gate = window.PassServerGateAsync(start: true);
            ConfirmationWindow dialog = await Dialog<ConfirmationWindow>(window);

            Assert.Contains("Community", dialog.FindControl<TextBlock>("TitleText")!.Text);
            Assert.Contains(ProfileFolder, dialog.FindControl<SelectableTextBlock>("MessageText")!.Text);
            Click(dialog, "NoButton");

            Assert.False(await gate);
            Assert.True(File.Exists(Path.Combine(ProfileFolder, "BepInEx", "plugins", "mod.dll")));
        }

        [AvaloniaFact]
        public async Task Suspended_RemovingTheFiles_DeletesTheProfileFolder_AndSaysSo()
        {
            LayInstalledProfile();
            MainWindow window = WindowFor(Community, "suspended");

            Task<bool> gate = window.PassServerGateAsync(start: true);
            Click(await Dialog<ConfirmationWindow>(window), "YesButton");
            Click(await Dialog<MessageBoxWindow>(window), "OkButton");

            Assert.False(await gate);
            Assert.False(Directory.Exists(ProfileFolder));
            Assert.True(Directory.Exists(_root));
        }

        [AvaloniaFact]
        public async Task Suspended_NotInstalled_ExplainsAndOffersNothingToRemove()
        {
            MainWindow window = WindowFor(Community, "suspended");

            Task<bool> gate = window.PassServerGateAsync(start: true);
            MessageBoxWindow dialog = await Dialog<MessageBoxWindow>(window);

            Assert.Contains("Community", dialog.FindControl<SelectableTextBlock>("MessageText")!.Text);
            Click(dialog, "OkButton");
            Assert.False(await gate);
            Assert.Empty(window.OwnedWindows.OfType<ConfirmationWindow>());
        }

        [AvaloniaFact]
        public async Task NeedsApproval_WarnsFirst_AndCancelStartsNothing()
        {
            LayInstalledProfile();
            MainWindow window = WindowFor(Community, "needs-approval");

            Task<bool> gate = window.PassServerGateAsync(start: true);
            ConfirmationWindow dialog = await Dialog<ConfirmationWindow>(window);

            Assert.Contains("Community", dialog.FindControl<TextBlock>("TitleText")!.Text);
            Click(dialog, "NoButton");

            Assert.False(await gate);
            Assert.True(File.Exists(Path.Combine(ProfileFolder, "BepInEx", "plugins", "mod.dll")));
        }

        [AvaloniaFact]
        public async Task NeedsApproval_WithoutPlaying_ExplainsThatUpdatesArePaused()
        {
            LayInstalledProfile();
            MainWindow window = WindowFor(Community, "needs-approval");

            Task<bool> gate = window.PassServerGateAsync(start: false);
            MessageBoxWindow dialog = await Dialog<MessageBoxWindow>(window);

            Assert.Equal(Loc.T("gate.paused.needsApproval", "Community"), dialog.FindControl<SelectableTextBlock>("MessageText")!.Text);
            Click(dialog, "OkButton");
            Assert.False(await gate);
        }

        [AvaloniaFact]
        public async Task Approved_GoesStraightToTheUpdate()
        {
            LayInstalledProfile();
            MainWindow window = WindowFor(Community, "approved");

            Assert.True(await window.PassServerGateAsync(start: true));
            Assert.Empty(window.OwnedWindows);
        }

        [AvaloniaFact]
        public async Task TheBuiltInProfile_GoesStraightToTheUpdate_WithoutAskingTheRegistry()
        {
            LayInstalledProfile();
            MainWindow window = WindowFor(ServerProfile.Default, "suspended");

            Assert.True(await window.PassServerGateAsync(start: true));
            Assert.Equal(0, _fetcher.Calls);
        }
    }
}
