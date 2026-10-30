using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Odinsons.ValheimLauncher.Avalonia.Controls;
using Odinsons.ValheimLauncher.Avalonia.Theming;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    public partial class MainWindow
    {
        private ThemeController _theme = null!;
        private bool _stepsUseTwoColumns;
        private bool _installColumnsSplit;

        internal ThemeController ThemeState => _theme;

        internal StepFrame ServerStepFrame => ServerFrame;
        internal StepFrame ModsStepFrame => ModsFrame;
        internal StepFrame InstallStepFrame => InstallFrame;
        internal TrustIcon TrustStatusIcon => TrustIcon;

        private void InitializeTheming()
        {
            ColorSchemeSetting setting = ColorSchemeSetting.System;
            try
            {
                var config = new IniFile(ConfigPath);
                config.Load();
                setting = LauncherPaths.ColorScheme(config);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Could not load the color scheme preference", ex);
            }

            _theme = new ThemeController(Application.Current!, setting);
            ApplyHeaderPalette();

            ServerFrame.Online = PlayersColumn;
            ServerFrame.AboutNews = ServerRightColumn;
            ModsFrame.Body = ModsBody;
            InstallFrame.Body = InstallBody;
            ServerFrame.PlanApplied += ArrangeServerZones;
            ModsFrame.PlanApplied += plan => UseTwoColumns(plan.TwoColumns);
            InstallFrame.PlanApplied += plan => UseTwoColumns(plan.TwoColumns);
            foreach (StepFrame frame in new[] { ServerFrame, ModsFrame, InstallFrame })
                frame.Bind(_theme, _profile.Name);

            ColorSchemeSelector.ItemsSource = new string[3];
            ColorSchemeSelector.SelectedIndex = (int)setting;
            ShowTrust(InitialTrust());

            Closed += (_, __) => _theme.Dispose();
        }

        private void ApplyHeaderPalette()
        {
            LauncherPalette dark = LauncherPalette.Dark;
            HeaderScope.Resources["HeaderBackground"] = new SolidColorBrush(LauncherBrushes.ToColor(dark.Background));
            HeaderScope.Resources["HeaderLine"] = new SolidColorBrush(LauncherBrushes.ToColor(dark.Line));
            HeaderScope.Resources[LauncherBrushes.TextKey] = new SolidColorBrush(LauncherBrushes.ToColor(dark.Text));
            HeaderScope.Resources[LauncherBrushes.MutedKey] = new SolidColorBrush(LauncherBrushes.ToColor(dark.Muted));
            HeaderScope.Resources[LauncherBrushes.LineKey] = new SolidColorBrush(LauncherBrushes.ToColor(dark.Line));
        }

        private void ApplyThemingLocalization()
        {
            if (_theme == null)
                return;

            int selected = ColorSchemeSelector.SelectedIndex;
            ColorSchemeSelector.ItemsSource = new[]
            {
                Loc.T("gui.colorScheme.system"), Loc.T("gui.colorScheme.light"), Loc.T("gui.colorScheme.dark")
            };
            ColorSchemeSelector.SelectedIndex = selected < 0 ? (int)_theme.Setting : selected;
            ToolTip.SetTip(ColorSchemeSelector, Loc.T("gui.colorScheme.tooltip"));
            global::Avalonia.Automation.AutomationProperties.SetName(ColorSchemeSelector, Loc.T("gui.colorScheme.tooltip"));
            DescribeTrust();
        }

        private void ColorSchemeSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_theme == null || ColorSchemeSelector.SelectedIndex < 0)
                return;

            var setting = (ColorSchemeSetting)ColorSchemeSelector.SelectedIndex;
            if (setting == _theme.Setting)
                return;

            _theme.SetSetting(setting);
            try
            {
                var config = new IniFile(ConfigPath);
                config.Load();
                LauncherPaths.SetColorScheme(config, setting);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Could not save the color scheme preference", ex);
            }
        }

        internal void UseTheme(LoadedTheme theme) => _theme.SetTheme(theme);

        internal void UseColorScheme(ColorSchemeSetting setting) => ColorSchemeSelector.SelectedIndex = (int)setting;

        private async Task LoadProfileThemeAsync()
        {
            if (string.Equals(_profile.Id, ServerProfile.Default.Id, StringComparison.Ordinal))
                return;

            try
            {
                LoadedTheme? loaded = await Task.Run(() =>
                    OfficialRegistry.LoadThemeAsync(Environment.CurrentDirectory, _profile.Id));
                if (loaded == null)
                    return;
                foreach (string problem in loaded.Problems)
                    LauncherLog.Warn($"Theme of '{_profile.Id}': {problem}");
                await Dispatcher.UIThread.InvokeAsync(() => UseTheme(loaded));
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Could not load the profile's theme, the default theme stays", ex);
            }
        }

        private RegistryStatus? InitialTrust()
        {
            if (string.Equals(_profile.Id, ServerProfile.Default.Id, StringComparison.Ordinal))
                return RegistryStatus.Approved;
            ProfileChoice? choice = _profileChoices.FirstOrDefault(c => c.Profile.Id == _profile.Id);
            return choice == null ? null : TrustIcon.FromNotice(choice.Decision.Notice);
        }

        internal void ShowTrust(RegistryStatus? status)
        {
            TrustIcon.Show(status);
            DescribeTrust();
        }

        private void DescribeTrust()
        {
            string text = TrustIcon.Status switch
            {
                RegistryStatus.Approved => Loc.T("trust.approved"),
                RegistryStatus.NeedsApproval => Loc.T("trust.needsApproval"),
                RegistryStatus.Suspended => Loc.T("trust.suspended"),
                _ => string.Empty
            };
            TrustIcon.Describe($"{Loc.T("trust.accessibleName")}: {text}", text);
        }

        private void ArrangeServerZones(FramePlan plan)
        {
            bool sideBySide = plan.Flow == ZoneFlow.SideBySide;
            AboutNewsGrid.ColumnDefinitions = new ColumnDefinitions(sideBySide ? "*,*" : "*");
            AboutNewsGrid.RowDefinitions = new RowDefinitions(sideBySide ? "Auto" : "Auto,Auto");
            Grid.SetColumn(AboutZone, 0);
            Grid.SetRow(AboutZone, 0);
            Grid.SetColumn(NewsZone, sideBySide ? 1 : 0);
            Grid.SetRow(NewsZone, sideBySide ? 0 : 1);
            NewsZone.Margin = sideBySide ? new Thickness(20, 0, 0, 0) : new Thickness(0, 14, 0, 0);
            NewsSeparator.IsVisible = !sideBySide;
            NewsZone.IsVisible = plan.ShowNews && _hasNewsZone;

            bool card = plan.AboutIsCard && (AboutZone.IsVisible || NewsZone.IsVisible);
            ServerRightColumn.Background = card ? LauncherBrushes.Surface : null;
            ServerRightColumn.BorderBrush = card ? LauncherBrushes.Line : null;
            ServerRightColumn.BorderThickness = new Thickness(card ? 1 : 0);
            ServerRightColumn.MaxWidth = plan.AboutIsCard ? 360 : double.PositiveInfinity;
            ServerRightColumn.MaxHeight = plan.AboutIsCard ? 320 : double.PositiveInfinity;
        }

        private void UseTwoColumns(bool twoColumns)
        {
            _stepsUseTwoColumns = twoColumns;
            SplitInstallColumns(twoColumns);
            ModsContent.InvalidateMeasure();
        }

        private void SplitInstallColumns(bool split)
        {
            if (split == _installColumnsSplit)
                return;
            _installColumnsSplit = split;

            InstallColumnsGrid.ColumnDefinitions = new ColumnDefinitions(split ? "*,*" : "*,Auto");
            InstallRightColumn.IsVisible = split;

            if (split)
            {
                InstallProgressPanel.Children.Remove(InstallStepsBox);
                InstallRightColumn.Children.Add(InstallStepsBox);
            }
            else
            {
                InstallRightColumn.Children.Remove(InstallStepsBox);
                InstallProgressPanel.Children.Add(InstallStepsBox);
            }
        }
    }
}
