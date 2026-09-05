using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Odinsons.ValheimLauncher;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    // Duplicated from the WPF project on purpose: the two front ends don't reference each
    // other, only Launcher.Core, and this DTO is GUI-specific (the /serverinfo response
    // shape), not shared pipeline logic.
    public class ServerInfo
    {
        public string name { get; set; }
        public int playersCount { get; set; }
        public List<PlayerInfo> players { get; set; }

        // PublicWebLink (server-side mod) switched "mods" from a bare list of names to a list
        // of objects — deserializing that shape into List<string> throws, which silently took
        // down the whole status update (online indicator + player count included, not just the
        // mod list) since both come from this one response. Typed properly here even though
        // nothing displays it yet, so the same class doesn't need touching again if something
        // eventually does (a "mods on this server" tooltip, say).
        public List<ServerModInfo> mods { get; set; }
    }

    public class ServerModInfo
    {
        public string name { get; set; }
        public string guid { get; set; }
        public string version { get; set; }
        public string description { get; set; }
        public string websiteUrl { get; set; }
        public List<string> dependencies { get; set; }
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public string SteamID { get; set; }
        public string AvatarUrl { get; set; }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged, IUpdateUi
    {
        private static readonly List<string> LauncherUrls = LauncherMirrors.Default.ToList();
        private static string ActiveLauncherUrl { get; set; }

        private static readonly HttpClient HttpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10
        }) { Timeout = TimeSpan.FromSeconds(5) };

        private Dictionary<string, string> _serverDirectories;
        public string SelectedServer { get; private set; }
        public string SelectedServerDirectory { get; private set; }
        public string ClientFolder { get; set; }

        private readonly string _currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private BackgroundWorker _worker = new() { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
        private bool _isInitializing = true;
        private bool _isLoading;

        private Dictionary<string, (string Ip, int QueryPort, int HttpPort)> _serverAddresses;
        private List<PlayerInfo> _currentPlayers = new();

        private static readonly string LogFilePath = Path.Combine(Environment.CurrentDirectory, "launcher_log.txt");

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                Dispatcher.UIThread.Invoke(() =>
                {
                    ServerSelector.IsEnabled = !value && _serverDirectories?.Count > 1;
                    StartGameBtn.IsEnabled = !value;
                    FullCheck.IsEnabled = !value;
                });
            }
        }

        #region IUpdateUi — display adapter for FileDownloader

        public void SetLoading(bool value) => Dispatcher.UIThread.Invoke(() => IsLoading = value);

        public void ShowProgress() => Dispatcher.UIThread.Invoke(() => InstallProgressGrid.IsVisible = true);

        public void HideProgress() => Dispatcher.UIThread.Invoke(() => InstallProgressGrid.IsVisible = false);

        // Superseded by the grouped step list (SetSteps/StartStep/SetStepProgress/FinishStep
        // below) — FileDownloader still calls these for WPF/CLI's sake, Avalonia just ignores them.
        public void SetStatus(string text) { }
        public void SetTotalProgress(double percent, string percentText, string bytesText) { }
        public void SetFileProgress(double percent) { }

        public void ShowMessage(string message, string title, UpdateMessageKind kind) =>
            Dispatcher.UIThread.Invoke(() => _ = MessageBoxWindow.ShowAsync(this, message, title));

        // Same gap as the WPF launcher: no non-blocking indicator wired up in the markup
        // yet. The warning is still logged through FileDownloader/LauncherLog.
        public void SetDegradationWarning(string message) { }

        private InjectorPlan _readyInjectorPlan;

        public void SetInjectorPlan(InjectorPlan plan)
        {
            _readyInjectorPlan = plan;

            // TryPrepareInjectorMode already verified the files this plan points at against
            // game_files.txt before offering it — by the time this is called, whichever mode
            // it names is confirmed, not just intended.
            string text = plan is not null
                ? Loc.T("gui.installMode.injector")
                : Loc.T("gui.installMode.classic");
            string tip = plan is not null
                ? Loc.T("gui.installMode.injectorTip", plan.WorkingDirectory)
                : Loc.T("gui.installMode.classicTip");

            Dispatcher.UIThread.Invoke(() =>
            {
                InstallStatusText.Text = text;
                ToolTip.SetTip(InstallStatusText, tip);
            });
        }

        private enum InstallStepState { Pending, Active, Done }

        private sealed class InstallStepRow
        {
            public string Label;
            public InstallStepState State = InstallStepState.Pending;
            public double Progress;
            public Stopwatch Stopwatch;
            public TimeSpan? Elapsed;
        }

        private List<InstallStepRow> _installSteps = new();
        private int _activeInstallStepIndex = -1;

        public void SetSteps(IReadOnlyList<string> stepLabels) => Dispatcher.UIThread.Invoke(() =>
        {
            _installSteps = stepLabels.Select(label => new InstallStepRow { Label = label }).ToList();
            _activeInstallStepIndex = -1;
            InstallOverallProgress.Value = 0;
            RenderInstallSteps();
            UpdateInstallHeader();
        });

        public void StartStep(int index) => Dispatcher.UIThread.Invoke(() =>
        {
            if (index < 0 || index >= _installSteps.Count) return;

            _activeInstallStepIndex = index;
            InstallStepRow step = _installSteps[index];
            step.State = InstallStepState.Active;
            step.Progress = 0;
            step.Stopwatch = Stopwatch.StartNew();

            RenderInstallSteps();
            UpdateInstallOverallProgress();
            UpdateInstallHeader();
        });

        public void SetStepProgress(double percent) => Dispatcher.UIThread.Invoke(() =>
        {
            if (_activeInstallStepIndex < 0 || _activeInstallStepIndex >= _installSteps.Count) return;

            _installSteps[_activeInstallStepIndex].Progress = Math.Clamp(percent, 0, 100);
            UpdateInstallOverallProgress();
        });

        public void FinishStep(int index) => Dispatcher.UIThread.Invoke(() =>
        {
            if (index < 0 || index >= _installSteps.Count) return;

            InstallStepRow step = _installSteps[index];
            step.State = InstallStepState.Done;
            step.Progress = 100;
            step.Elapsed = step.Stopwatch?.Elapsed;
            if (_activeInstallStepIndex == index) _activeInstallStepIndex = -1;

            RenderInstallSteps();
            UpdateInstallOverallProgress();
            UpdateInstallHeader();
        });

        /// <summary>The one bar that must never move backwards within a run: completed steps
        /// count as a whole unit each, the active step contributes its own fractional progress.</summary>
        private void UpdateInstallOverallProgress()
        {
            if (_installSteps.Count == 0) { InstallOverallProgress.Value = 0; return; }

            double completed = _installSteps.Count(step => step.State == InstallStepState.Done);
            double activeFraction = _activeInstallStepIndex >= 0
                ? _installSteps[_activeInstallStepIndex].Progress / 100.0
                : 0;

            InstallOverallProgress.Value = Math.Clamp((completed + activeFraction) / _installSteps.Count * 100, 0, 100);
        }

        private void UpdateInstallHeader()
        {
            int total = _installSteps.Count;
            int current = _activeInstallStepIndex >= 0
                ? _activeInstallStepIndex + 1
                : Math.Min(_installSteps.Count(step => step.State == InstallStepState.Done), total);

            InstallStepLabel.Text = _activeInstallStepIndex >= 0
                ? _installSteps[_activeInstallStepIndex].Label
                : _installSteps.Count > 0 ? _installSteps[^1].Label : string.Empty;

            InstallStepCounter.Text = total > 0 ? Loc.T("dl.stepCounter", current, total) : string.Empty;

            bool isDownloading = _activeInstallStepIndex >= 0 &&
                _installSteps[_activeInstallStepIndex].Label == Loc.T("dl.step.download");
            InstallCancelButton.Content = isDownloading ? Loc.T("dl.cancelDownloading") : Loc.T("dl.cancelChecking");
        }

        private void RenderInstallSteps()
        {
            InstallStepsPanel.Children.Clear();
            foreach (InstallStepRow step in _installSteps)
                InstallStepsPanel.Children.Add(BuildInstallStepRow(step));
        }

        private static Control BuildInstallStepRow(InstallStepRow step)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto") };

            Control icon = step.State switch
            {
                InstallStepState.Done => new TextBlock
                {
                    Text = "✓", FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#FF6FCF6F")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                },
                InstallStepState.Active => BuildInstallStepSpinner(),
                _ => new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = new SolidColorBrush(Color.Parse("#FF808080")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(icon, 0);

            var label = new TextBlock
            {
                Text = step.Label,
                Foreground = new SolidColorBrush(step.State == InstallStepState.Pending ? Color.Parse("#FF808080") : Colors.White),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);

            var time = new TextBlock
            {
                Text = step.Elapsed is { } elapsed ? $"{elapsed.TotalSeconds:0.0}s" : string.Empty,
                Foreground = new SolidColorBrush(Color.Parse("#FFB0B0B0")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(time, 2);

            row.Children.Add(icon);
            row.Children.Add(label);
            row.Children.Add(time);
            return row;
        }

        private static Ellipse BuildInstallStepSpinner()
        {
            var spinner = new Ellipse { Width = 14, Height = 14, RenderTransform = new RotateTransform(0) };
            spinner.Classes.Add("stepSpinner");
            return spinner;
        }

        private void InstallCancelButton_Click(object? sender, RoutedEventArgs e) => _worker?.CancelAsync();

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(LogFilePath, $"[{timestamp}] {message}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to the log: {ex.Message}");
            }
        }

        private static string ComputeExecutableHash()
        {
            try
            {
                string exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return "unknown";

                using FileStream stream = File.OpenRead(exePath);
                byte[] hash = SHA256.HashData(stream);
                return Convert.ToHexString(hash);
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.Message})";
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            if (File.Exists(LogFilePath)) File.WriteAllText(LogFilePath, string.Empty);

            Opacity = 0;

            CopyrightText.Text = $"{Loc.T("gui.versionLabel")} {_currentVersion}";
            PopulatePlayerSelector(0);

            if (Program.InstanceGuard is not null)
                Program.InstanceGuard.ActivateRequested += () => Dispatcher.UIThread.Invoke(BringToForeground);

            Loaded += async (_, __) =>
            {
                Opacity = 1;

                Log($"Launcher starting — version {_currentVersion}, exe SHA-256 {ComputeExecutableHash()}");
                await InitializeLauncherUrlAsync();
                if (string.IsNullOrEmpty(ActiveLauncherUrl))
                {
                    await MessageBoxWindow.ShowAsync(this, Loc.T("gui.allServersDown"));
                    Log("All mirrors unreachable, closing the launcher");
                    Close();
                    return;
                }

                string baseDir = Environment.CurrentDirectory;
                string configPath = Path.Combine(baseDir, "config.ini");

                if (!File.Exists(configPath))
                {
                    try
                    {
                        await LoadServersAsync();
                        if (_serverDirectories?.Count > 0)
                        {
                            if (_serverDirectories.Count == 1)
                            {
                                SelectedServer = _serverDirectories.Keys.First();
                                if (!await SaveSelectedServerAsync(configPath)) return;

                                SelectComboBoxItem(SelectedServer);
                                await CheckAndUpdateLauncherAsync();
                                await InitializeAsync();
                            }
                            else
                            {
                                var serverSelectionWindow = new ServerSelectionWindow(_serverDirectories);
                                bool confirmed = await serverSelectionWindow.ShowDialog<bool>(this);
                                if (confirmed && !string.IsNullOrEmpty(serverSelectionWindow.SelectedServer))
                                {
                                    SelectedServer = serverSelectionWindow.SelectedServer;
                                    if (!await SaveSelectedServerAsync(configPath)) return;

                                    SelectComboBoxItem(SelectedServer);
                                    await CheckAndUpdateLauncherAsync();
                                    await InitializeAsync();
                                }
                                else
                                {
                                    await MessageBoxWindow.ShowAsync(this, Loc.T("gui.noServerSelected"));
                                    Log("No server selected, closing the launcher");
                                    Close();
                                    return;
                                }
                            }
                        }
                        else
                        {
                            await MessageBoxWindow.ShowAsync(this, Loc.T("gui.noServersToChoose"));
                            Log("No servers available to choose from, closing the launcher");
                            Close();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        await MessageBoxWindow.ShowAsync(this, Loc.T("gui.initError", ex.Message));
                        Log($"Initialization error: {ex.Message}");
                        Close();
                    }
                }
                else
                {
                    await MainWindowLoadedAsync();
                    await CheckAndUpdateLauncherAsync();
                }
            };

            _isInitializing = false;
        }

        private async Task<bool> SaveSelectedServerAsync(string configPath)
        {
            var config = new IniFile(configPath);
            try
            {
                await config.WriteAsync("SelectedServer", SelectedServer, "Settings");
                Log($"Saved server {SelectedServer} to config.ini");
                return true;
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.ShowAsync(this, Loc.T("gui.configWriteFailed", ex.Message, configPath));
                Log($"Error writing config.ini: {ex.Message}");
                Close();
                return false;
            }
        }

        private void SelectComboBoxItem(string server)
        {
            var selectedItem = ServerSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == server);
            if (selectedItem != null) ServerSelector.SelectedItem = selectedItem;
        }

        private async Task InitializeLauncherUrlAsync()
        {
            Log("Starting mirror check");
            foreach (var url in LauncherUrls)
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    Log($"Checking mirror {url}, attempt {attempt}");
                    (bool available, bool retryable, TimeSpan retryDelay) = await CheckMirrorAsync(url, attempt);
                    if (available)
                    {
                        ActiveLauncherUrl = url;
                        Log($"Mirror selected: {url}");
                        return;
                    }

                    if (!retryable || attempt == 3) break;

                    Log($"Mirror {url} busy, retrying in {retryDelay.TotalSeconds:0.0}s");
                    await Task.Delay(retryDelay);
                }
            }
            ActiveLauncherUrl = null;
            Log("All mirrors unreachable");
        }

        private async Task<(bool available, bool retryable, TimeSpan retryDelay)> CheckMirrorAsync(string url, int attempt = 1)
        {
            try
            {
                using var responseVersion = await HttpClient.GetAsync(url + "version.txt");
                if (!responseVersion.IsSuccessStatusCode)
                {
                    bool retryable = HttpRetry.IsRetryableStatus(responseVersion.StatusCode);
                    TimeSpan delay = retryable ? HttpRetry.Delay(attempt, responseVersion) : TimeSpan.Zero;
                    Log($"version.txt error at {url}: {responseVersion.StatusCode}" +
                        (retryable ? $" (retryable, {delay.TotalSeconds:0.0}s)" : ""));
                    return (false, retryable, delay);
                }

                using var responseServers = await HttpClient.GetAsync(url + "servers.json");
                if (!responseServers.IsSuccessStatusCode)
                {
                    bool retryable = HttpRetry.IsRetryableStatus(responseServers.StatusCode);
                    TimeSpan delay = retryable ? HttpRetry.Delay(attempt, responseServers) : TimeSpan.Zero;
                    Log($"servers.json error at {url}: {responseServers.StatusCode}" +
                        (retryable ? $" (retryable, {delay.TotalSeconds:0.0}s)" : ""));
                    return (false, retryable, delay);
                }

                Log($"Mirror {url} is available: version.txt and servers.json loaded");
                return (true, false, TimeSpan.Zero);
            }
            catch (TaskCanceledException)
            {
                Log($"Timeout checking {url}");
                return (false, true, HttpRetry.Delay(attempt, null));
            }
            catch (HttpRequestException ex)
            {
                Log($"HTTP error checking {url}: {ex.Message}");
                return (false, true, HttpRetry.Delay(attempt, null));
            }
            catch (Exception ex)
            {
                Log($"Unknown error checking {url}: {ex.Message}");
                return (false, false, TimeSpan.Zero);
            }
        }

        private async Task<bool> CheckServerUpdateAsync(string url, string serverName)
        {
            try
            {
                string updateUrl = $"{url}{serverName}/update.info";
                Log($"Checking update.info for {serverName} at {url}");
                using var responseUpdate = await HttpClient.GetAsync(updateUrl);
                if (responseUpdate.IsSuccessStatusCode)
                {
                    Log($"update.info for {serverName} is available at {url}");
                    return true;
                }
                Log($"update.info error for {serverName} at {url}: {responseUpdate.StatusCode}");
                return false;
            }
            catch (TaskCanceledException)
            {
                Log($"Timeout on update.info for {serverName} at {url}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"update.info error for {serverName} at {url}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TrySwitchMirrorAsync(string serverName)
        {
            foreach (var url in LauncherUrls)
            {
                if (url == ActiveLauncherUrl) continue;
                Log($"Checking alternate mirror {url} for {serverName}");
                if ((await CheckMirrorAsync(url)).available && await CheckServerUpdateAsync(url, serverName))
                {
                    ActiveLauncherUrl = url;
                    Log($"Switched to mirror {url} for {serverName}");
                    return true;
                }
            }
            Log($"No mirrors available for {serverName}");
            return false;
        }

        private async Task MainWindowLoadedAsync()
        {
            await LoadServersAsync();
            await InitializeAsync();
        }

        private async Task LoadServersAsync()
        {
            try
            {
                Log($"Loading server list from {ActiveLauncherUrl}");
                string serversJson = await HttpClient.GetStringAsync(ActiveLauncherUrl + "servers.json");
                var servers = JsonSerializer.Deserialize<ServerList>(serversJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                bool isAdmin = File.Exists(Path.Combine(Environment.CurrentDirectory, "admin"));
                var availableServers = isAdmin ? servers.Servers : servers.Servers.Where(s => !s.Hidden).ToList();

                if (availableServers.Count == 0)
                {
                    await MessageBoxWindow.ShowAsync(this, Loc.T("gui.noServersAvailable"));
                    Log("No servers available");
                    Close();
                    return;
                }

                _serverDirectories = availableServers.ToDictionary(s => s.Name, s => ActiveLauncherUrl + s.Name + "/");
                _serverAddresses = availableServers.ToDictionary(s => s.Name, s => (s.Ip, s.QueryPort, s.HttpPort));

                ServerSelector.Items.Clear();
                foreach (var server in availableServers)
                    ServerSelector.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server.Name });

                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                await config.LoadAsync();
                string selectedServerFromConfig = config.Read("SelectedServer", "Settings");
                var selectedItem = ServerSelector.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag?.ToString() == selectedServerFromConfig);

                ServerSelector.SelectionChanged -= ServerSelector_SelectionChanged;
                ServerSelector.SelectedItem = selectedItem ?? ServerSelector.Items.OfType<ComboBoxItem>().FirstOrDefault();
                ServerSelector.IsEnabled = _serverDirectories.Count > 1 && !IsLoading;
                ServerSelector.SelectionChanged += ServerSelector_SelectionChanged;

                if (ServerSelector.SelectedItem is ComboBoxItem initialItem)
                {
                    SelectedServer = initialItem.Tag.ToString();
                    SelectedServerDirectory = _serverDirectories[SelectedServer];
                    ClientFolder = Path.Combine("clients", SelectedServer);
                    Directory.CreateDirectory(ClientFolder);

                    // Fire-and-forget: must not block the update/launch flow below on a UAC
                    // prompt the player might not even answer right away.
                    _ = MaybeOfferDefenderExclusionAsync(ClientFolder);

                    // Independent of the mirror check below — see the same note in
                    // ServerSelector_SelectionChanged.
                    Task statusTask = UpdateServerStatusAsync();

                    Log($"Server selected: {SelectedServer}");
                    if (!await CheckServerUpdateAsync(ActiveLauncherUrl, SelectedServer))
                    {
                        Log($"Server {SelectedServer} unavailable at {ActiveLauncherUrl}, looking for another mirror");
                        if (!await TrySwitchMirrorAsync(SelectedServer))
                        {
                            await MessageBoxWindow.ShowAsync(this, Loc.T("gui.serverUnavailableAllMirrors", SelectedServer));
                            Log($"Server {SelectedServer} unavailable on all mirrors");
                            Close();
                            return;
                        }
                        SelectedServerDirectory = ActiveLauncherUrl + SelectedServer + "/";
                        _serverDirectories[SelectedServer] = SelectedServerDirectory;
                    }

                    await statusTask;
                }
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.ShowAsync(this, Loc.T("gui.serverListLoadFailed", ex.Message));
                Log($"Error loading the server list: {ex.Message}");
                Close();
            }
        }

        private async Task CheckAndUpdateLauncherAsync()
        {
#if DEBUG
            // This front end isn't published to the server yet — a Debug build has no
            // meaningful version to compare and self-updating here would just overwrite
            // it with the WPF exe. Only matters locally; Release builds still update
            // normally once this front end actually ships.
            Log("Skipping self-update check (Debug build)");
            return;
#else
            try
            {
                string baseDir = Environment.CurrentDirectory;
                string currentExePath = Path.Combine(baseDir, "OdinsonsLauncher.exe");
                string tempExePath = Path.Combine(baseDir, "new_launcher.exe");

                Log($"Checking launcher version at {ActiveLauncherUrl}");
                string serverVersion = (await HttpClient.GetStringAsync(ActiveLauncherUrl + "version.txt")).Trim();
                VersionComparison comparison = CompareVersions(serverVersion, _currentVersion);

                if (comparison != VersionComparison.Older) return;

                // Most launcher updates are mandatory — a protocol/manifest-format change on
                // the server can mean an old launcher simply doesn't work correctly anymore.
                // min_version.txt is the server's explicit opt-out: only a version at or above
                // it may be postponed. Missing or unreachable defaults to mandatory, matching
                // that most releases are — an admin has to deliberately publish this file to
                // make a specific update skippable, not the other way around.
                bool mandatory = await IsUpdateMandatoryAsync(serverVersion);

                var config = new IniFile(Path.Combine(baseDir, "config.ini"));
                await config.LoadAsync();

                // Postponing an update writes the version, not a bare flag: the player can decline
                // this exact one and still get asked again once something newer than it appears.
                if (!mandatory &&
                    string.Equals(config.Read("SkippedLauncherVersion", "Settings"), serverVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Update to {serverVersion} was previously postponed; not asking again until a newer version ships");
                    return;
                }

                string changelog = await TryFetchLauncherChangelogAsync();
                string message = string.IsNullOrWhiteSpace(changelog)
                    ? Loc.T("gui.launcherUpdateAvailableNoChangelog", serverVersion)
                    : Loc.T("gui.launcherUpdateAvailable", serverVersion) + "\n\n" + changelog;
                if (mandatory)
                    message = Loc.T("gui.launcherUpdateMandatoryNote") + "\n\n" + message;

                string declineButtonText = mandatory ? Loc.T("gui.launcherPostponeAndExit") : Loc.T("gui.launcherUpdateLater");
                bool accepted = await ConfirmationWindow.ShowAsync(this, Loc.T("gui.launcherUpdateTitle", serverVersion),
                    message, Loc.T("gui.launcherUpdateNow"), declineButtonText);

                if (!accepted)
                {
                    if (mandatory)
                    {
                        // Not truly "skippable" — declining just means "not right now", and the
                        // same mandatory prompt will be waiting on the next launch. No point
                        // persisting a skip for a version that was never allowed to be skipped.
                        Log($"Player postponed the mandatory update to {serverVersion}; closing");
                        Close();
                        return;
                    }

                    await config.WriteAsync("SkippedLauncherVersion", serverVersion, "Settings");
                    Log($"Player postponed the optional update to {serverVersion}");
                    return;
                }

                Log("Downloading the new launcher version");
                using var response = await HttpClient.GetAsync(ActiveLauncherUrl + "OdinsonsLauncher.exe");
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync();
                using (var fileStream = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                {
                    await stream.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                }

                if (!File.Exists(tempExePath))
                    throw new Exception(Loc.T("gui.launcherDownloadFailed"));

                CreateUpdateScript(currentExePath, tempExePath, baseDir);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                string tempExePath = Path.Combine(Environment.CurrentDirectory, "new_launcher.exe");
                if (File.Exists(tempExePath))
                {
                    try { File.Delete(tempExePath); } catch { }
                }
                await MessageBoxWindow.ShowAsync(this, Loc.T("gui.autoUpdateError", ex.Message));
                Log($"Auto-update error: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Whether the running version is old enough that the update can't be postponed.
        /// Reads Launcher/min_version.txt — the oldest version still allowed to keep running.
        /// Missing, unreachable, or unparsable defaults to mandatory: most updates are, and an
        /// admin has to explicitly publish this file to mark a specific one as skippable.
        /// </summary>
        private async Task<bool> IsUpdateMandatoryAsync(string serverVersion)
        {
            try
            {
                string minVersion = (await HttpClient.GetStringAsync(ActiveLauncherUrl + "min_version.txt")).Trim();
                // CompareVersions(a, b) reads as "is b older than a" — Older means the running
                // version is below the floor the server still allows.
                return CompareVersions(minVersion, _currentVersion) == VersionComparison.Older;
            }
            catch (Exception ex)
            {
                Log($"min_version.txt not available ({ex.Message}); treating update to {serverVersion} as mandatory");
                return true;
            }
        }

        /// <summary>
        /// Best-effort: a missing or unreachable changelog.md means the update dialog falls
        /// back to a plain "a new version is available" message, not a reason to skip the
        /// whole update prompt.
        /// </summary>
        private async Task<string> TryFetchLauncherChangelogAsync()
        {
            try
            {
                return (await HttpClient.GetStringAsync(ActiveLauncherUrl + "changelog.md")).Trim();
            }
            catch (Exception ex)
            {
                Log($"Could not load the launcher changelog: {ex.Message}");
                return null;
            }
        }

        private enum VersionComparison { Older, Same, Newer }

        private VersionComparison CompareVersions(string serverVersion, string localVersion)
        {
            var serverParts = serverVersion.Split('.').Select(int.Parse).ToArray();
            var localParts = localVersion.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Max(serverParts.Length, localParts.Length); i++)
            {
                int serverPart = i < serverParts.Length ? serverParts[i] : 0;
                int localPart = i < localParts.Length ? localParts[i] : 0;

                if (localPart < serverPart) return VersionComparison.Older;
                if (localPart > serverPart) return VersionComparison.Newer;
            }
            return VersionComparison.Same;
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = false;

                Dispatcher.UIThread.Invoke(() =>
                {
                    InstallProgressGrid.IsVisible = false;
                    NewsGrid.Opacity = 0;
                });

                try
                {
                    Log($"Loading news from {ActiveLauncherUrl}");
                    string news = await HttpClient.GetStringAsync(ActiveLauncherUrl + "news.info");
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        News.Text = news;
                        NewsGrid.Opacity = 1;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Invoke(() => News.Text = Loc.T("gui.newsLoadFailed", ex.Message));
                    Log($"Error loading news: {ex.Message}");
                }
            }
            finally
            {
                Dispatcher.UIThread.Invoke(() => StartButtonGrid.Opacity = 1);
            }
        }

        /// <summary>Spinner instead of the dot while a check is in flight — see the type's doc comment on ServerCheckSpinner in the markup.</summary>
        private void ShowServerChecking() => Dispatcher.UIThread.Invoke(() =>
        {
            ServerStatusIndicator.IsVisible = false;
            ServerCheckSpinner.IsVisible = true;
        });

        private void ShowServerStatus(bool online, int playerCount) => Dispatcher.UIThread.Invoke(() =>
        {
            ServerCheckSpinner.IsVisible = false;
            ServerStatusIndicator.IsVisible = true;
            ServerStatusIndicator.Fill = new SolidColorBrush(online ? Colors.Green : Colors.Red);
            PopulatePlayerSelector(playerCount);
        });

        private async Task UpdateServerStatusAsync()
        {
            ShowServerChecking();

            try
            {
                if (!_serverAddresses.ContainsKey(SelectedServer))
                {
                    ShowServerStatus(online: false, playerCount: 0);
                    return;
                }

                var (ip, _, httpPort) = _serverAddresses[SelectedServer];

                if (httpPort > 0)
                {
                    Log($"Requesting server status for {SelectedServer} via HTTP: http://{ip}:{httpPort}/serverinfo");
                    using var response = await HttpClient.GetAsync($"http://{ip}:{httpPort}/serverinfo");
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    var serverInfo = JsonSerializer.Deserialize<ServerInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    _currentPlayers = serverInfo.players ?? new List<PlayerInfo>();
                    ShowServerStatus(online: true, playerCount: serverInfo.playersCount);
                    Log($"Received player list: {string.Join(", ", _currentPlayers.Select(p => p.Name))}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"Error requesting server status for {SelectedServer} via HTTP: {ex.Message}.");
            }

            // Unlike the WPF launcher, there's no SteamQuery UDP fallback here yet — this
            // build only uses the HTTP status endpoint. A missed/unreachable endpoint just
            // shows as offline instead of falling back further.
            _currentPlayers = new List<PlayerInfo>();
            ShowServerStatus(online: false, playerCount: 0);
        }

        private async void StartGameBtn_Click(object? sender, RoutedEventArgs e) => await StartGameAsync(false, true);

        private async void StartGameBtn_Click_Full(object? sender, RoutedEventArgs e) => await StartGameAsync(true, false);

        private async Task StartGameAsync(bool full, bool start)
        {
            if (IsLoading) return;

            IsLoading = true;
            StartButtonGrid.Opacity = 0;
            await Task.Delay(800); // matches the WPF fade-out duration

            await StartUpdateAsync(full, start);
        }

        private async Task StartUpdateAsync(bool full, bool start)
        {
            if (_worker.IsBusy) return;

            _worker.Dispose();
            _worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

            if (!await CheckServerUpdateAsync(ActiveLauncherUrl, SelectedServer))
            {
                await MessageBoxWindow.ShowAsync(this, Loc.T("gui.clientUnavailableOnMirror", SelectedServer), Loc.T("gui.title.error"));
                IsLoading = false;
                StartButtonGrid.Opacity = 1;
                return;
            }

            if (!ClientFolderGuard.IsWritable(ClientFolder, out string permReason, out string permAdvice))
            {
                Log($"No write access: {ClientFolder} — {permReason}");
                await MessageBoxWindow.ShowAsync(this,
                    Loc.T("gui.updateStoppedPermissions", permReason, Path.GetFullPath(ClientFolder), permAdvice),
                    Loc.T("gui.title.noWriteAccess"));
                IsLoading = false;
                StartButtonGrid.Opacity = 1;
                return;
            }

            if (!ClientFolderGuard.IsSafeTarget(ClientFolder, out string unsafeReason))
            {
                Log($"Client folder failed validation: {ClientFolder} — {unsafeReason}");
                await MessageBoxWindow.ShowAsync(this,
                    Loc.T("gui.updateStoppedUnsafe", unsafeReason, Path.GetFullPath(ClientFolder)),
                    Loc.T("gui.title.error"));
                IsLoading = false;
                StartButtonGrid.Opacity = 1;
                return;
            }

            if (!UpdateSession.TryBegin(ClientFolder, out UpdateSession session, out string sessionReason))
            {
                Log($"Could not start the update: {sessionReason}");
                await MessageBoxWindow.ShowAsync(this, Loc.T("gui.updateNotStarted", sessionReason), Loc.T("gui.title.folderBusy"));
                IsLoading = false;
                StartButtonGrid.Opacity = 1;
                return;
            }

            using (session)
            {
                if (session.PreviousRunInterrupted && !full)
                {
                    Log("The previous update didn't finish — running a full check");
                    full = true;
                }

                string steamGameFolder = null;
                if (SteamLocator.TryFindGame(SteamLocator.ValheimAppId, SteamLocator.ValheimDepotId, null,
                                             out SteamGameInfo steamGame, out string steamReason))
                {
                    LauncherLog.Info($"steam: build {steamGame.BuildId}, depot manifest {steamGame.DepotManifestId}, " +
                                     $"stateFlags={steamGame.StateFlags}");

                    if (steamGame.FullyInstalled) steamGameFolder = steamGame.InstallPath;
                    else LauncherLog.Warn("steam copy is not fully installed — not using it as a source");
                }
                else
                {
                    LauncherLog.Info($"steam: {steamReason}");
                }

                await FileDownloader.StartUpdateAsync(_worker, this, full, start, SelectedServerDirectory,
                    Path.GetFileName(Environment.ProcessPath ?? "OdinsonsLauncher.exe"), maxConcurrentDownloads: 8,
                    steamGameFolder: steamGameFolder, session: session);
            }
        }

        private async void ServerSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || IsLoading || ServerSelector.SelectedItem is not ComboBoxItem selectedItem)
                return;

            SelectedServer = selectedItem.Tag.ToString();
            SelectedServerDirectory = _serverDirectories[SelectedServer];
            ClientFolder = Path.Combine("clients", SelectedServer);

            // Doesn't depend on the mirror/news chain below at all — it queries the game
            // server's own IP directly, not the launcher's CDN. Used to run after all of
            // that, which is why switching servers looked stuck on the old status for as
            // long as the slowest of those unrelated checks took.
            Task statusTask = UpdateServerStatusAsync();

            var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
            try
            {
                await config.WriteAsync("SelectedServer", SelectedServer, "Settings");
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.ShowAsync(this, Loc.T("gui.configUpdateFailed", ex.Message));
                Log($"Error updating config.ini: {ex.Message}");
            }

            Directory.CreateDirectory(ClientFolder);
            ChangelogGrid.IsVisible = false;

            if (!await CheckServerUpdateAsync(ActiveLauncherUrl, SelectedServer))
            {
                Log($"Server {SelectedServer} unavailable at {ActiveLauncherUrl}, looking for another mirror");
                if (!await TrySwitchMirrorAsync(SelectedServer))
                {
                    await MessageBoxWindow.ShowAsync(this, Loc.T("gui.serverUnavailableAllMirrors", SelectedServer));
                    Log($"Server {SelectedServer} unavailable on all mirrors");
                    await statusTask;
                    return;
                }
                SelectedServerDirectory = ActiveLauncherUrl + SelectedServer + "/";
                _serverDirectories[SelectedServer] = SelectedServerDirectory;
            }

            await InitializeAsync();
            await statusTask;
        }

        private async void ChangelogButton_Click(object? sender, RoutedEventArgs e)
        {
            // The gear icon used to open the changelog; it now opens the mods panel instead.
            // ShowChangelogAsync is still here, just with nothing wired to call it right now.
            if (IsLoading) return;
            await ShowModsPanelAsync();
        }

        private void CloseChangelogButton_Click(object? sender, RoutedEventArgs e) => ChangelogGrid.IsVisible = false;

        private void CloseModsButton_Click(object? sender, RoutedEventArgs e) => ModsGrid.IsVisible = false;

        /// <summary>
        /// Builds the mods panel straight from this project's own build manifests
        /// (update.info / update_admin.info / optional.info) — the same files the update
        /// pipeline already fetches, re-requested here read-only over HTTP rather than
        /// touching the real client folder or an update session/lock. Required and
        /// admin-only mods are informational; optional mods get a real toggle — see
        /// FileDownloader's use of OptionalModSelection for what flipping it does on the
        /// next update.
        ///
        /// A mod's plugin folder is its identity (ModGrouping); its display name, version,
        /// and description come from that folder's own manifest.json if it ships one — the
        /// standard Thunderstore package manifest, not a launcher-maintained duplicate that
        /// could drift from it.
        /// </summary>
        private async Task ShowModsPanelAsync()
        {
            ModsContent.Children.Clear();
            ModsContent.Children.Add(NewModsText(Loc.T("mods.loading"), bold: false));
            ModsGrid.IsVisible = true;

            if (string.IsNullOrEmpty(ClientFolder) || string.IsNullOrEmpty(SelectedServer))
            {
                ModsContent.Children.Clear();
                ModsContent.Children.Add(NewModsText(Loc.T("mods.unavailable"), bold: false));
                return;
            }

            bool isAdmin = File.Exists(Path.Combine(ClientFolder, "admin"));

            List<Manifest.Entry> requiredEntries = await FetchManifestAsync("update.info");
            List<Manifest.Entry> adminEntries = isAdmin
                ? await FetchManifestAsync("update_admin.info")
                : new List<Manifest.Entry>();
            List<Manifest.Entry> optionalEntries = await FetchManifestAsync("optional.info");

            var requiredPaths = requiredEntries.Select(entry => entry.Path).ToList();
            var requiredSet = new HashSet<string>(requiredPaths, StringComparer.OrdinalIgnoreCase);
            List<string> adminOnlyPaths = adminEntries
                .Select(entry => entry.Path)
                .Where(path => !requiredSet.Contains(path))
                .ToList();
            List<string> optionalPaths = optionalEntries.Select(entry => entry.Path).ToList();

            Dictionary<string, List<string>> requiredGroups = ModGrouping.GroupByPluginFolder(requiredPaths);
            Dictionary<string, List<string>> adminGroups = ModGrouping.GroupByPluginFolder(adminOnlyPaths);
            Dictionary<string, List<string>> optionalGroups = ModGrouping.GroupByPluginFolder(optionalPaths);

            ModsContent.Children.Clear();

            if (requiredGroups.Count == 0 && optionalGroups.Count == 0 && adminGroups.Count == 0)
            {
                ModsContent.Children.Add(NewModsText(Loc.T("mods.unavailable"), bold: false));
                return;
            }

            OptionalModSelection selection = OptionalModSelection.Load(ClientFolder);

            if (requiredGroups.Count > 0)
            {
                ModsContent.Children.Add(NewSectionHeader(Loc.T("mods.required")));
                foreach ((string folder, List<string> files) in requiredGroups.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    ModsContent.Children.Add(BuildModRow(folder, files, showToggle: false, isOn: true));
            }

            if (optionalGroups.Count > 0)
            {
                ModsContent.Children.Add(NewSectionHeader(Loc.T("mods.optional")));
                foreach ((string folder, List<string> files) in optionalGroups.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    ModsContent.Children.Add(BuildModRow(folder, files, showToggle: true, isOn: selection.IsSelected(folder)));
            }

            if (isAdmin && adminGroups.Count > 0)
            {
                ModsContent.Children.Add(NewSectionHeader(Loc.T("mods.adminOnly")));
                foreach ((string folder, List<string> files) in adminGroups.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    ModsContent.Children.Add(BuildModRow(folder, files, showToggle: false, isOn: true));
            }
        }

        private async Task<List<Manifest.Entry>> FetchManifestAsync(string fileName)
        {
            try
            {
                string text = await HttpClient.GetStringAsync(ActiveLauncherUrl + SelectedServer + "/" + fileName);
                using var reader = new StringReader(text);
                return Manifest.Read(reader);
            }
            catch (Exception ex)
            {
                Log($"Could not load {fileName} for the mods panel: {ex.Message}");
                return new List<Manifest.Entry>();
            }
        }

        private static TextBlock NewSectionHeader(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 16, 0, 8)
        };

        private static TextBlock NewModsText(string text, bool bold) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };

        private static Bitmap _thunderstoreIcon;
        private static Bitmap ThunderstoreIcon => _thunderstoreIcon ??=
            new Bitmap(AssetLoader.Open(new Uri("avares://OdinsonsLauncher/Resources/thunderstore_icon.png")));

        private Control BuildModRow(string folderKey, List<string> files, bool showToggle, bool isOn)
        {
            var name = new TextBlock
            {
                Text = folderKey, // placeholder until manifest.json (if any) fills in the real name
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var version = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var linkButton = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0, 0, 0),
                IsVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new Image { Source = ThunderstoreIcon, Width = 16, Height = 16 }
            };
            var description = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                FontSize = 12,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                IsVisible = false
            };

            var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
            headerRow.Children.Add(name);
            headerRow.Children.Add(version);
            headerRow.Children.Add(linkButton);

            var textStack = new StackPanel();
            textStack.Children.Add(headerRow);
            textStack.Children.Add(description);

            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 14),
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            Grid.SetColumn(textStack, 0);
            row.Children.Add(textStack);

            if (showToggle)
            {
                var toggle = new ToggleSwitch
                {
                    IsChecked = isOn,
                    OnContent = null,
                    OffContent = null,
                    VerticalAlignment = VerticalAlignment.Center
                };
                toggle.IsCheckedChanged += (_, __) =>
                {
                    if (string.IsNullOrEmpty(ClientFolder)) return;
                    OptionalModSelection current = OptionalModSelection.Load(ClientFolder);
                    current.SetSelected(folderKey, toggle.IsChecked == true);
                    current.Save(ClientFolder);
                    Log($"Optional mod {folderKey} {(toggle.IsChecked == true ? "selected" : "deselected")}");
                };
                Grid.SetColumn(toggle, 1);
                row.Children.Add(toggle);
            }

            string manifestJsonPath = ModGrouping.FindManifestJsonPath(files);
            if (manifestJsonPath != null)
                _ = LoadModManifestIntoRowAsync(manifestJsonPath, name, version, description, linkButton);

            return row;
        }

        private async Task LoadModManifestIntoRowAsync(string manifestJsonPath, TextBlock name, TextBlock version,
                                                        TextBlock description, Button linkButton)
        {
            try
            {
                string json = await HttpClient.GetStringAsync(SelectedServerDirectory + manifestJsonPath);
                ModManifest manifest = ModManifest.Parse(json);

                if (!string.IsNullOrEmpty(manifest.Name)) name.Text = manifest.Name;
                if (!string.IsNullOrEmpty(manifest.VersionNumber)) version.Text = manifest.VersionNumber;

                if (!string.IsNullOrEmpty(manifest.Description))
                {
                    description.Text = manifest.Description;
                    description.IsVisible = true;
                }

                if (!string.IsNullOrEmpty(manifest.WebsiteUrl))
                {
                    string url = manifest.WebsiteUrl;
                    linkButton.Click += (_, __) =>
                        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    linkButton.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                Log($"Could not load {manifestJsonPath}: {ex.Message}");
            }
        }

        private async Task ShowChangelogAsync()
        {
            try
            {
                string changelogPath = Path.Combine(ClientFolder, "changelog.md");
                string changelogText;

                try
                {
                    Log($"Loading changelog for {SelectedServer} from {ActiveLauncherUrl}");
                    changelogText = await HttpClient.GetStringAsync(ActiveLauncherUrl + SelectedServer + "/changelog.md");
                    await File.WriteAllTextAsync(changelogPath, changelogText);
                }
                catch (HttpRequestException)
                {
                    changelogText = File.Exists(changelogPath)
                        ? await File.ReadAllTextAsync(changelogPath)
                        : Loc.T("gui.changelogUnavailable");
                }

                Dispatcher.UIThread.Invoke(() =>
                {
                    ChangelogContent.Children.Clear();
                    ParseMarkdownToUI(changelogText);
                    ChangelogGrid.IsVisible = true;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    ChangelogContent.Children.Clear();
                    ChangelogContent.Children.Add(new TextBlock
                    {
                        Text = Loc.T("gui.changelogLoadError", ex.Message),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 16,
                        TextWrapping = TextWrapping.Wrap
                    });
                    ChangelogGrid.IsVisible = true;
                });
                Log($"Error loading changelog: {ex.Message}");
            }
        }

        private void ParseMarkdownToUI(string markdownText)
        {
            var lines = markdownText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var textBlock = new TextBlock
                {
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                if (line.StartsWith("#"))
                {
                    int headerLevel = line.TakeWhile(c => c == '#').Count();
                    string headerText = line.TrimStart('#').Trim();

                    textBlock.Text = headerText;
                    textBlock.FontSize = 18 - (headerLevel * 2);
                    textBlock.FontWeight = FontWeight.Bold;
                    textBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                    textBlock.Margin = new Thickness(0, 5, 0, 5);
                }
                else if (line.TrimStart().StartsWith("-") || line.TrimStart().StartsWith("*"))
                {
                    string listItem = line.TrimStart('-', '*', ' ').Trim();
                    textBlock.Inlines ??= new InlineCollection();
                    textBlock.Inlines.Add(new Run("• ") { Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)) });
                    textBlock.Inlines.Add(new Run(listItem));
                }
                else
                {
                    textBlock.Text = line.Trim();
                }

                ChangelogContent.Children.Add(textBlock);
            }
        }

        private async void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            _worker?.CancelAsync();
            _worker?.Dispose();

            Opacity = 0;
            await Task.Delay(800);
            Environment.Exit(0);
        }

        private void Minimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        /// <summary>Called when a second launch attempt pings this instance (see
        /// SingleInstanceGuard/Program.InstanceGuard). A plain Activate() is often ignored by
        /// Windows' focus-stealing prevention when the request comes from a background
        /// process — the Topmost flip forces it to the front regardless.</summary>
        private void BringToForeground()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
        }

        private void Background_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void DiscordButton_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        { FileName = "https://discord.gg/eTteBxWcfu", UseShellExecute = true });

        private void DonateButton_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        { FileName = "https://odinsons.club/", UseShellExecute = true });

        private void WebsiteButton_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        { FileName = "https://odinsons.club/", UseShellExecute = true });

        private void TelegramButton_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        { FileName = "https://t.me/+AHY_F2jClNJmNTcy", UseShellExecute = true });

        private readonly Dictionary<string, Bitmap> _avatarCache = new();

        /// <summary>Which of the three avatar-slot visuals a row shows — bound from the
        /// DataTemplate via PlayerListItem's ShowXxx properties below.</summary>
        private enum PlayerRowKind { Header, Player, Empty }

        /// <summary>One dropdown row — the header row (index 0, "Vikings: N") included, so the
        /// closed ComboBox always has a real SelectedItem to show instead of relying on
        /// PlaceholderText, which FluentTheme may style differently from an actual selection.</summary>
        private sealed class PlayerListItem : INotifyPropertyChanged
        {
            public string Name { get; init; }
            public PlayerRowKind Kind { get; init; } = PlayerRowKind.Player;

            private Bitmap _avatar;
            public Bitmap Avatar
            {
                get => _avatar;
                set
                {
                    _avatar = value;
                    Raise(nameof(Avatar));
                    Raise(nameof(ShowAvatar));
                    Raise(nameof(ShowSkeleton));
                }
            }

            // The header row has nothing to put in the avatar slot at all — collapsing it
            // outright (rather than leaving an empty Image there) is what keeps "Vikings: N"
            // from showing a pointless gap in the closed ComboBox.
            public bool ShowSlot => Kind != PlayerRowKind.Header;
            public bool ShowAvatar => Kind == PlayerRowKind.Player && Avatar is not null;
            public bool ShowSkeleton => Kind == PlayerRowKind.Player && Avatar is null;
            public bool ShowEmptyIcon => Kind == PlayerRowKind.Empty;

            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private PlayerListItem _playerCountHeaderItem;

        private void PopulatePlayerSelector(int playerCount)
        {
            _playerCountHeaderItem = new PlayerListItem
            {
                Name = Loc.T("gui.vikingsCount", playerCount),
                Kind = PlayerRowKind.Header
            };
            var items = new List<PlayerListItem> { _playerCountHeaderItem };

            if (_currentPlayers.Count == 0)
            {
                items.Add(new PlayerListItem { Name = Loc.T("gui.noPlayersOnline"), Kind = PlayerRowKind.Empty });
            }
            else
            {
                foreach (PlayerInfo player in _currentPlayers)
                {
                    var item = new PlayerListItem { Name = player.Name };
                    items.Add(item);

                    // Rows appear immediately with the name (and a loading skeleton in the
                    // avatar slot); avatars fill in as each one loads — no reason to hold up
                    // the list on a batch of Steam CDN requests.
                    if (!string.IsNullOrEmpty(player.AvatarUrl))
                        _ = LoadAvatarIntoItemAsync(player.AvatarUrl, item);
                }
            }

            PlayerCountSelector.ItemsSource = items;
            PlayerCountSelector.SelectedItem = _playerCountHeaderItem;
        }

        private void PlayerCountSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Nothing is actually selectable here — a click just closes the dropdown. Snapping
            // back to the header item keeps "Vikings: N" showing instead of a clicked player's
            // name; the equality guard stops this from re-triggering itself.
            if (!ReferenceEquals(PlayerCountSelector.SelectedItem, _playerCountHeaderItem))
                PlayerCountSelector.SelectedItem = _playerCountHeaderItem;
        }

        private async Task LoadAvatarIntoItemAsync(string url, PlayerListItem item)
        {
            try
            {
                if (_avatarCache.TryGetValue(url, out Bitmap cached))
                {
                    item.Avatar = cached;
                    return;
                }

                byte[] bytes = await HttpClient.GetByteArrayAsync(url);
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                _avatarCache[url] = bitmap;
                item.Avatar = bitmap;
            }
            catch (Exception ex)
            {
                Log($"Failed to load player avatar from {url}: {ex.Message}");
            }
        }

        /// <summary>
        /// One-time prompt, per client folder: offer to add it to Windows Defender's
        /// exclusions, to head off the "suspicious re-download" false positives ClientLedger
        /// flags when Defender briefly locks a just-downloaded mod file. Fires right after the
        /// client folder is known, deliberately not awaited by the caller — it must not hold up
        /// the update/launch flow on a UAC prompt the player might leave sitting there.
        /// </summary>
        private async Task MaybeOfferDefenderExclusionAsync(string clientFolder)
        {
            if (DefenderExclusion.HasBeenPrompted(clientFolder)) return;

            bool alreadyExcluded = await Task.Run(() => DefenderExclusion.IsExcluded(clientFolder));
            if (alreadyExcluded)
            {
                DefenderExclusion.MarkPrompted(clientFolder);
                return;
            }

            bool accepted = await ConfirmationWindow.ShowAsync(this, Loc.T("defender.title"), Loc.T("defender.message"),
                Loc.T("defender.addButton"), Loc.T("defender.notNowButton"));

            DefenderExclusion.MarkPrompted(clientFolder);

            if (accepted)
            {
                bool added = await Task.Run(() => DefenderExclusion.TryAddExclusion(clientFolder));
                if (!added)
                    await MessageBoxWindow.ShowAsync(this, Loc.T("defender.uacDeclined"));
            }
        }

        public void OnUpdateComplete(bool startAfter, bool canStartGame)
        {
            IsLoading = false;
            if (startAfter && canStartGame)
            {
                if (_readyInjectorPlan is not null)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        HideProgress();
                        Close();
                    });
                    InjectorLauncher.Launch(_readyInjectorPlan);
                    return;
                }

                string gamePath = Path.Combine(ClientFolder, "valheim.exe");
                if (File.Exists(gamePath))
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        HideProgress();
                        Close();
                    });
                    Process.Start(new ProcessStartInfo { FileName = gamePath, UseShellExecute = true });
                }
                else
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        _ = MessageBoxWindow.ShowAsync(this, Loc.T("gui.valheimExeNotFound", ClientFolder), Loc.T("gui.title.launchError"));
                        HideProgress();
                        StartButtonGrid.Opacity = 1;
                    });
                    Log($"Launch error: valheim.exe not found in {ClientFolder}");
                }
            }
            else
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    HideProgress();
                    StartButtonGrid.Opacity = 1;
                });
            }
        }

        private void CreateUpdateScript(string oldExePath, string tempExePath, string baseDir)
        {
            string scriptPath = Path.Combine(baseDir, "update.bat");

            string scriptContent = @"
                @echo off
                :loop
                del ""{0}"" 2>nul
                if exist ""{0}"" (
                    timeout /t 2
                    goto loop
                )
                move ""{1}"" ""{2}"" 2>nul
                if exist ""{2}"" (
                    start """" ""{2}""
                )
                del ""%~f0""
                ";
            scriptContent = string.Format(scriptContent, oldExePath, tempExePath, Path.Combine(baseDir, "OdinsonsLauncher.exe"));

            File.WriteAllText(scriptPath, scriptContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
