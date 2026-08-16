using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SteamQuery;

namespace Odinsons.ValheimLauncher
{
    // Property for controlling openness (can be bound or managed directly)

    // Class for deserializing JSON from /serverinfo
    public class ServerInfo
    {
        public string name { get; set; }
        public int playersCount { get; set; }
        public List<PlayerInfo> players { get; set; }
        public List<string> mods { get; set; }
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public string SteamID { get; set; }
        public string AvatarUrl { get; set; }
    }

    // Server and ServerList moved to Launcher.Core/ServerModels.cs — the CLI uses them too.

    public partial class MainWindow : Window, INotifyPropertyChanged, IUpdateUi
    {
        
        public static MainWindow Instance { get; private set; }
        private readonly DoubleAnimation _mainOA;
        public static Mutex GlobalMutex { get; private set; }

        // A single source — Launcher.Core/ServerModels.cs — so the CLI and GUI don't drift apart.
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
        private readonly string _exePath = Path.Combine(Environment.CurrentDirectory, "OdinsonsLauncher.exe");

        private BackgroundWorker _worker = new() { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
        private bool _isInitializing = true;
        private bool _isLoading;

        // Added HttpPort to the tuple
        private Dictionary<string, (string Ip, int QueryPort, int HttpPort)> _serverAddresses;

        // List of current players, for the popup
        private List<string> _currentPlayers = new List<string>();

        private static readonly string LogFilePath = Path.Combine(Environment.CurrentDirectory, "launcher_log.txt");

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                Dispatcher.Invoke(() =>
                {
                    ServerSelector.IsEnabled = !value && _serverDirectories?.Count > 1;
                    StartGameBtn.IsEnabled = !value;
                    FullCheck.IsEnabled = !value;
                });
            }
        }

        #region IUpdateUi — display adapter for FileDownloader

        // Marshaling to the UI thread lives here. The downloader knows nothing about Dispatcher.
        // ClientFolder and OnUpdateComplete are already declared below and implement the interface as-is.

        public void SetLoading(bool value) => Dispatcher.Invoke(() => IsLoading = value);

        public void ShowProgress() => Dispatcher.Invoke(() =>
        {
            progressbar.Visibility = Visibility.Visible;
            progressbartotal.Visibility = Visibility.Visible;
            totalpercent.Visibility = Visibility.Visible;
            totalpercenttext.Visibility = Visibility.Visible;
            currenttask.Visibility = Visibility.Visible;
        });

        public void HideProgress() => Dispatcher.Invoke(() =>
        {
            progressbar.Visibility = Visibility.Hidden;
            progressbartotal.Visibility = Visibility.Hidden;
            totalpercent.Visibility = Visibility.Hidden;
            currenttask.Visibility = Visibility.Hidden;
            totalpercenttext.Visibility = Visibility.Hidden;
        });

        public void SetStatus(string text) => Dispatcher.Invoke(() => currenttask.Text = text);

        public void SetTotalProgress(double percent, string percentText, string bytesText) => Dispatcher.Invoke(() =>
        {
            totalpercent.Content = percentText;
            progressbartotal.Value = Math.Clamp(percent, 0, 100) * (progressbartotal.Maximum / 100.0);
            totalpercenttext.Content = bytesText;
        });

        public void SetFileProgress(double percent) => Dispatcher.Invoke(() =>
        {
            progressbar.Value = Math.Clamp(percent, 0, 100) * (progressbar.Maximum / 100.0);
        });

        public void ShowMessage(string message, string title, UpdateMessageKind kind) => Dispatcher.Invoke(() =>
            MessageBox.Show(message, title, MessageBoxButton.OK, kind switch
            {
                UpdateMessageKind.Warning => MessageBoxImage.Warning,
                UpdateMessageKind.Error => MessageBoxImage.Error,
                _ => MessageBoxImage.None
            }));

        // TODO: a non-blocking indicator next to the progress bar isn't wired up
        // in XAML yet — didn't edit the markup blind, with no way to verify the
        // result visually. The warning itself is already logged through
        // FileDownloader/LauncherLog, so the diagnostic isn't lost; what's missing
        // here is only the visual duplication in the launcher window.
        public void SetDegradationWarning(string message) { }

        /// <summary>
        /// The injector-mode launch plan, if one was built this run — see
        /// <see cref="InjectorLauncher"/>. OnUpdateComplete launches the game using it
        /// instead of valheim.exe from the client folder, when it's not null.
        /// </summary>
        private InjectorPlan _readyInjectorPlan;

        public void SetInjectorPlan(InjectorPlan plan) => _readyInjectorPlan = plan;

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                // Using File.AppendAllText to add lines, but the file is already cleared on startup
                File.AppendAllText(LogFilePath, $"[{timestamp}] {message}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to the log: {ex.Message}");
            }
        }

        public MainWindow()
        {
            try
            {
                // Clear the log file on startup
                if (File.Exists(LogFilePath))
                {
                    File.WriteAllText(LogFilePath, string.Empty); // Overwrite the file with an empty string
                }

                InitializeComponent();
                Instance = this;
                DataContext = this;

                GlobalMutex = new Mutex(false, "Odinsons.ValheimLauncher_UniqueMutex", out bool firstInstance);
                if (!firstInstance)
                {
                    Close();
                    return;
                }

                _mainOA = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(800)
                };
                BeginAnimation(OpacityProperty, _mainOA);
                LostFocus += (_, __) => _isDragging = false;

                Loaded += async (_, __) =>
                {
                    Log("Launcher starting");
                    await InitializeLauncherUrlAsync();
                    if (string.IsNullOrEmpty(ActiveLauncherUrl))
                    {
                        MessageBox.Show(Loc.T("gui.allServersDown"));
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
                                    var config = new IniFile(configPath);
                                    try
                                    {
                                        await config.WriteAsync("SelectedServer", SelectedServer, "Settings");
                                        Log($"Saved server {SelectedServer} to config.ini");
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show(Loc.T("gui.configWriteFailed", ex.Message, configPath));
                                        Log($"Error writing config.ini: {ex.Message}");
                                        Close();
                                        return;
                                    }

                                    var selectedItem = ServerSelector.Items.Cast<ComboBoxItem>()
                                        .FirstOrDefault(item => item.Tag.ToString() == SelectedServer);
                                    if (selectedItem != null)
                                        ServerSelector.SelectedItem = selectedItem;

                                    await CheckAndUpdateLauncherAsync();
                                    await InitializeAsync();
                                }
                                else
                                {
                                    var serverSelectionWindow = new ServerSelectionWindow(_serverDirectories);
                                    if (serverSelectionWindow.ShowDialog() == true && !string.IsNullOrEmpty(serverSelectionWindow.SelectedServer))
                                    {
                                        SelectedServer = serverSelectionWindow.SelectedServer;
                                        var config = new IniFile(configPath);
                                        try
                                        {
                                            await config.WriteAsync("SelectedServer", SelectedServer, "Settings");
                                            Log($"Saved server {SelectedServer} to config.ini");
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show(Loc.T("gui.configWriteFailed", ex.Message, configPath));
                                            Log($"Error writing config.ini: {ex.Message}");
                                            Close();
                                            return;
                                        }

                                        var selectedItem = ServerSelector.Items.Cast<ComboBoxItem>()
                                            .FirstOrDefault(item => item.Tag.ToString() == SelectedServer);
                                        if (selectedItem != null)
                                            ServerSelector.SelectedItem = selectedItem;

                                        await CheckAndUpdateLauncherAsync();
                                        await InitializeAsync();
                                    }
                                    else
                                    {
                                        MessageBox.Show(Loc.T("gui.noServerSelected"));
                                        Log("No server selected, closing the launcher");
                                        Close();
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                MessageBox.Show(Loc.T("gui.noServersToChoose"));
                                Log("No servers available to choose from, closing the launcher");
                                Close();
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(Loc.T("gui.initError", ex.Message));
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

                Dispatcher.Invoke(() =>
                {
                    VersionText.Inlines.Clear();
                    VersionText.Inlines.Add(new Run(_currentVersion) { FontFamily = new FontFamily("Sitka Text") });
                    CopyrightText.Text = Loc.T("gui.versionLabel");
                    FullCheckTooltipText.Text = Loc.T("gui.fullCheckTooltip");
                    PlayerCountText.Text = Loc.T("gui.vikingsCount", 0);
                });

                _isInitializing = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("gui.criticalStartupError", ex.Message));
                Log($"Critical error on startup: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private async Task InitializeLauncherUrlAsync()
        {
            Log("Starting mirror check");
            foreach (var url in LauncherUrls)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Log($"Checking mirror {url}, attempt {attempt + 1}");
                    if (await CheckMirrorAsync(url))
                    {
                        ActiveLauncherUrl = url;
                        Log($"Mirror selected: {url}");
                        return;
                    }
                    if (attempt < 2) await Task.Delay(1000);
                }
            }
            ActiveLauncherUrl = null;
            Log("All mirrors unreachable");
        }

        private async Task<bool> CheckMirrorAsync(string url)
        {
            try
            {
                using var responseVersion = await HttpClient.GetAsync(url + "version.txt");
                if (!responseVersion.IsSuccessStatusCode)
                {
                    Log($"version.txt error at {url}: {responseVersion.StatusCode}");
                    return false;
                }

                using var responseServers = await HttpClient.GetAsync(url + "servers.json");
                if (!responseServers.IsSuccessStatusCode)
                {
                    Log($"servers.json error at {url}: {responseServers.StatusCode}");
                    return false;
                }

                Log($"Mirror {url} is available: version.txt and servers.json loaded");
                return true;
            }
            catch (TaskCanceledException)
            {
                Log($"Timeout checking {url}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                Log($"HTTP error checking {url}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Unknown error checking {url}: {ex.Message}");
                return false;
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
                if (await CheckMirrorAsync(url) && await CheckServerUpdateAsync(url, serverName))
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
                    MessageBox.Show(Loc.T("gui.noServersAvailable"));
                    Log("No servers available");
                    Close();
                    return;
                }

                _serverDirectories = availableServers.ToDictionary(s => s.Name, s => ActiveLauncherUrl + s.Name + "/");

                // Added HttpPort to the dictionary
                _serverAddresses = availableServers.ToDictionary(
                    s => s.Name,
                    s => (s.Ip, s.QueryPort, s.HttpPort)
                );

                ServerSelector.Items.Clear();
                foreach (var server in availableServers)
                {
                    ServerSelector.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server.Name });
                }

                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                await config.LoadAsync();
                string selectedServerFromConfig = config.Read("SelectedServer", "Settings");
                var selectedItem = ServerSelector.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag.ToString() == selectedServerFromConfig);

                ServerSelector.SelectionChanged -= ServerSelector_SelectionChanged;
                ServerSelector.SelectedItem = selectedItem ?? ServerSelector.Items.Cast<ComboBoxItem>().FirstOrDefault();
                ServerSelector.IsEnabled = _serverDirectories.Count > 1 && !IsLoading;
                ServerSelector.SelectionChanged += ServerSelector_SelectionChanged;

                if (ServerSelector.SelectedItem is ComboBoxItem initialItem)
                {
                    SelectedServer = initialItem.Tag.ToString();
                    SelectedServerDirectory = _serverDirectories[SelectedServer];
                    ClientFolder = Path.Combine("clients", SelectedServer);
                    Directory.CreateDirectory(ClientFolder);

                    Log($"Server selected: {SelectedServer}");
                    if (!await CheckServerUpdateAsync(ActiveLauncherUrl, SelectedServer))
                    {
                        Log($"Server {SelectedServer} unavailable at {ActiveLauncherUrl}, looking for another mirror");
                        if (!await TrySwitchMirrorAsync(SelectedServer))
                        {
                            MessageBox.Show(Loc.T("gui.serverUnavailableAllMirrors", SelectedServer));
                            Log($"Server {SelectedServer} unavailable on all mirrors");
                            Close();
                            return;
                        }
                        SelectedServerDirectory = ActiveLauncherUrl + SelectedServer + "/";
                        _serverDirectories[SelectedServer] = SelectedServerDirectory;
                    }

                    await UpdateServerStatusAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("gui.serverListLoadFailed", ex.Message));
                Log($"Error loading the server list: {ex.Message}");
                Close();
            }
        }

        private async Task CheckAndUpdateLauncherAsync()
        {
            try
            {
                string baseDir = Environment.CurrentDirectory;
                string currentExePath = Path.Combine(baseDir, "OdinsonsLauncher.exe");
                string tempExePath = Path.Combine(baseDir, "new_launcher.exe");

                Log($"Checking launcher version at {ActiveLauncherUrl}");
                string serverVersion = (await HttpClient.GetStringAsync(ActiveLauncherUrl + "version.txt")).Trim();
                VersionComparison comparison = CompareVersions(serverVersion, _currentVersion);

                if (comparison != VersionComparison.Older)
                {
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
                {
                    throw new Exception(Loc.T("gui.launcherDownloadFailed"));
                }

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
                MessageBox.Show(Loc.T("gui.autoUpdateError", ex.Message));
                Log($"Auto-update error: {ex.Message}");
            }
        }

        private enum VersionComparison
        {
            Older,
            Same,
            Newer
        }

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

                // Manifests are cleaned up by FileDownloader — it's the only one that knows
                // the client folder. There used to be two SafeDeleteFile calls here that
                // looked for files via Environment.CurrentDirectory, i.e. the launcher's own
                // folder, and so deleted nothing.

                Dispatcher.Invoke(() =>
                {
                    progressbar.Visibility = Visibility.Hidden;
                    progressbartotal.Visibility = Visibility.Hidden;
                    totalpercent.Visibility = Visibility.Hidden;
                    currenttask.Visibility = Visibility.Hidden;
                    NewsGrid.Visibility = Visibility.Hidden;
                });

                try
                {
                    Log($"Loading news from {ActiveLauncherUrl}");
                    string news = await HttpClient.GetStringAsync(ActiveLauncherUrl + "news.info");
                    Dispatcher.Invoke(() =>
                    {
                        News.Text = $"{news}";
                        NewsGrid.Visibility = Visibility.Visible;
                        NewsGrid.BeginAnimation(OpacityProperty, new DoubleAnimation
                        {
                            From = 0,
                            To = 1,
                            Duration = TimeSpan.FromMilliseconds(800)
                        });
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => News.Text = Loc.T("gui.newsLoadFailed", ex.Message));
                    Log($"Error loading news: {ex.Message}");
                }
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    StartButtonGrid.Opacity = 1;
                    UpdateLayout();
                });
            }
        }

        // Fully reworked to use HTTP with a SteamQuery fallback
        private async Task UpdateServerStatusAsync()
        {
            try
            {
                if (!_serverAddresses.ContainsKey(SelectedServer))
                {
                    Dispatcher.Invoke(() =>
                    {
                        ServerStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                        PlayerCountText.Text = Loc.T("gui.vikingsCount", 0);
                    });
                    return;
                }

                var (ip, queryPort, httpPort) = _serverAddresses[SelectedServer];

                // Try HTTP first, if a port is set
                if (httpPort > 0)
                {
                    Log($"Requesting server status for {SelectedServer} via HTTP: http://{ip}:{httpPort}/serverinfo");
                    using var response = await HttpClient.GetAsync($"http://{ip}:{httpPort}/serverinfo");
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    var serverInfo = JsonSerializer.Deserialize<ServerInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    Dispatcher.Invoke(() =>
                    {
                        ServerStatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                        PlayerCountText.Text = Loc.T("gui.vikingsCount", serverInfo.playersCount);
                    });

                    _currentPlayers = serverInfo.players?.Select(p => p.Name).ToList() ?? new List<string>();
                    Log($"Received player list: {string.Join(", ", _currentPlayers)}");
                    return; // Success — done
                }
            }
            catch (Exception ex)
            {
                Log($"Error requesting server status for {SelectedServer} via HTTP: {ex.Message}. Falling back to SteamQuery.");
            }

            // Fallback to the old SteamQuery path (count only, empty player list)
            try
            {
                var (ip, queryPort, _) = _serverAddresses[SelectedServer];
                var endPoint = EndPointAddress.Create(IPAddress.Parse(ip), queryPort);

                var infoQuery = new ServerInfoQuery(endPoint);
                var infoTask = new TaskCompletionSource<ServerQueryResponse<ServerInfoData>>();

                infoQuery.QueryComplete += (s, r) => infoTask.SetResult(r);
                infoQuery.Send();

                // SteamQuery retries internally on an unanswered UDP query and can take up to a
                // couple of minutes to give up on its own; that's long enough to make the launcher
                // look hung (the Start button stays hidden until this call returns), so bound it
                // ourselves instead of trusting the library's own timeout.
                Task completed = await Task.WhenAny(infoTask.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                if (completed != infoTask.Task)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ServerStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                        PlayerCountText.Text = Loc.T("gui.vikingsCount", 0);
                    });
                    _currentPlayers = new List<string>();
                    Log($"SteamQuery timed out checking status for {SelectedServer}");
                    return;
                }

                var infoResponse = await infoTask.Task;

                Dispatcher.Invoke(() =>
                {
                    if (infoResponse.Result == ServerQueryResult.ResponseReceived)
                    {
                        ServerStatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                        PlayerCountText.Text = Loc.T("gui.vikingsCount", infoResponse.Data.PlayerCount);
                    }
                    else
                    {
                        ServerStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                        PlayerCountText.Text = Loc.T("gui.vikingsCount", 0);
                    }
                });

                _currentPlayers = new List<string>(); // No player list on the fallback path
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ServerStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                    PlayerCountText.Text = Loc.T("gui.vikingsCount", 0);
                });
                _currentPlayers = new List<string>();
                Log($"Error checking server status (fallback): {ex.Message}");
            }
        }

        private async void StartGameBtn_Click(object sender, RoutedEventArgs e) =>
            await StartGameAsync(false, true);

        private async void StartGameBtn_Click_Full(object sender, RoutedEventArgs e) =>
            await StartGameAsync(true, false);

        private async Task StartGameAsync(bool full, bool start)
        {
            if (IsLoading) return;

            IsLoading = true;
            var tcs = new TaskCompletionSource<bool>();
            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(800)
            };
            animation.Completed += (_, __) => tcs.SetResult(true);
            StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, animation);
            await tcs.Task;

            await StartUpdateAsync(full, start);
        }

        private async Task StartUpdateAsync(bool full, bool start)
        {
            if (_worker.IsBusy) return;

            _worker.Dispose();
            _worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

            if (!await CheckServerUpdateAsync(ActiveLauncherUrl, SelectedServer))
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(Loc.T("gui.clientUnavailableOnMirror", SelectedServer), Loc.T("gui.title.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsLoading = false;
                    StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(800)
                    });
                    UpdateLayout();
                });
                return;
            }

            // Permissions first: without folder access, nothing else can be determined,
            // and a permissions failure calls for very different advice to the player.
            if (!ClientFolderGuard.IsWritable(ClientFolder, out string permReason, out string permAdvice))
            {
                Log($"No write access: {ClientFolder} — {permReason}");
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        Loc.T("gui.updateStoppedPermissions", permReason, Path.GetFullPath(ClientFolder), permAdvice),
                        Loc.T("gui.title.noWriteAccess"), MessageBoxButton.OK, MessageBoxImage.Error);
                    IsLoading = false;
                    StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(800)
                    });
                    UpdateLayout();
                });
                return;
            }

            if (!ClientFolderGuard.IsSafeTarget(ClientFolder, out string unsafeReason))
            {
                Log($"Client folder failed validation: {ClientFolder} — {unsafeReason}");
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        Loc.T("gui.updateStoppedUnsafe", unsafeReason, Path.GetFullPath(ClientFolder)),
                        Loc.T("gui.title.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsLoading = false;
                    StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(800)
                    });
                    UpdateLayout();
                });
                return;
            }

            if (!UpdateSession.TryBegin(ClientFolder, out UpdateSession session, out string sessionReason))
            {
                Log($"Could not start the update: {sessionReason}");
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        Loc.T("gui.updateNotStarted", sessionReason),
                        Loc.T("gui.title.folderBusy"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsLoading = false;
                    StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(800)
                    });
                    UpdateLayout();
                });
                return;
            }

            // For the duration of the update, valheim.exe is moved aside: the game can't
            // be launched by accident, and Dispose moves the file back (or discards the
            // stashed copy if a fresh one was downloaded).
            using (session)
            {
                if (session.PreviousRunInterrupted && !full)
                {
                    Log("The previous update didn't finish — running a full check");
                    full = true;
                }

                await FileDownloader.StartUpdateAsync(_worker, this, full, start, SelectedServerDirectory,
                    Path.GetFileName(Assembly.GetExecutingAssembly().Location), maxConcurrentDownloads: 3,
                    session: session);
            }
        }

        private async void ServerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || IsLoading || ServerSelector.SelectedItem is not ComboBoxItem selectedItem)
                return;

            SelectedServer = selectedItem.Tag.ToString();
            SelectedServerDirectory = _serverDirectories[SelectedServer];
            ClientFolder = Path.Combine("clients", SelectedServer);

            var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
            try
            {
                await config.WriteAsync("SelectedServer", SelectedServer, "Settings");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("gui.configUpdateFailed", ex.Message));
                Log($"Error updating config.ini: {ex.Message}");
            }

            Directory.CreateDirectory(ClientFolder);
            ChangelogGrid.Visibility = Visibility.Collapsed;

            if (!await CheckServerUpdateAsync(ActiveLauncherUrl, SelectedServer))
            {
                Log($"Server {SelectedServer} unavailable at {ActiveLauncherUrl}, looking for another mirror");
                if (!await TrySwitchMirrorAsync(SelectedServer))
                {
                    MessageBox.Show(Loc.T("gui.serverUnavailableAllMirrors", SelectedServer));
                    Log($"Server {SelectedServer} unavailable on all mirrors");
                    return;
                }
                SelectedServerDirectory = ActiveLauncherUrl + SelectedServer + "/";
                _serverDirectories[SelectedServer] = SelectedServerDirectory;
            }

            await InitializeAsync();
            await UpdateServerStatusAsync();
        }

        private async void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsLoading) return;

            await ShowChangelogAsync();
        }

        private void CloseChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            ChangelogGrid.Visibility = Visibility.Collapsed;
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
                    if (File.Exists(changelogPath))
                    {
                        changelogText = await File.ReadAllTextAsync(changelogPath);
                    }
                    else
                    {
                        changelogText = Loc.T("gui.changelogUnavailable");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    ChangelogContent.Children.Clear();
                    ParseMarkdownToUI(changelogText);
                    ChangelogGrid.Visibility = Visibility.Visible;
                    ChangelogGrid.BeginAnimation(OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(800)
                    });
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ChangelogContent.Children.Clear();
                    ChangelogContent.Children.Add(new TextBlock
                    {
                        Text = Loc.T("gui.changelogLoadError", ex.Message),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontFamily = new FontFamily("Sitka Text"),
                        FontSize = 16,
                        TextWrapping = TextWrapping.Wrap
                    });
                    ChangelogGrid.Visibility = Visibility.Visible;
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

                TextBlock textBlock = new TextBlock
                {
                    Foreground = new SolidColorBrush(Colors.White),
                    FontFamily = new FontFamily("Sitka Text"),
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
                    textBlock.FontWeight = FontWeights.Bold;
                    textBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                    textBlock.Margin = new Thickness(0, 5, 0, 5);
                }
                else if (line.TrimStart().StartsWith("-") || line.TrimStart().StartsWith("*"))
                {
                    string listItem = line.TrimStart('-', '*', ' ').Trim();
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

        public void Button_Close(object sender, MouseButtonEventArgs e)
        {
            _worker?.CancelAsync();
            _worker?.Dispose();

            _mainOA.From = 1;
            _mainOA.To = 0;
            _mainOA.Completed += (_, __) => Environment.Exit(0);
            _mainOA.Duration = TimeSpan.FromMilliseconds(800);
            BeginAnimation(OpacityProperty, _mainOA);
        }

        private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private bool _isDragging;
        private Point _startPoint;

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _isDragging = true;
            _startPoint = e.GetPosition(this);
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point mousePos = e.GetPosition(this);
            double newLeft = Left + mousePos.X - _startPoint.X;
            double newTop = Top + mousePos.Y - _startPoint.Y;
            Rect workingArea = SystemParameters.WorkArea;
            Left = Math.Max(workingArea.Left, Math.Min(workingArea.Right - Width, newLeft));
            Top = Math.Max(workingArea.Top, Math.Min(workingArea.Bottom - Height, newTop));
        }

        private void Grid_MouseUp(object sender, MouseButtonEventArgs e) => _isDragging = false;

        private void DiscordButton_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        {
            FileName = "https://discord.gg/eTteBxWcfu",
            UseShellExecute = true
        });

        private void DonateButton_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        {
            FileName = "https://odinsons.club/",
            UseShellExecute = true
        });

        private void WebsiteButton_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        {
            FileName = "https://odinsons.club/",
            UseShellExecute = true
        });

        private void TelegramButton_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        {
            FileName = "https://t.me/+AHY_F2jClNJmNTcy",
            UseShellExecute = true
        });

        // Hover handlers for "Vikings: X"
        private bool _isPopupOpen = false;

        private void PlayerCountText_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_currentPlayers.Count > 0 && !_isPopupOpen)
            {
                PlayersListPanel.Children.Clear();
                foreach (var player in _currentPlayers)
                {
                    var tb = new TextBlock
                    {
                        Text = player,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontFamily = new FontFamily("Sitka Text"),
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    PlayersListPanel.Children.Add(tb);
                }
                PlayersPopup.IsOpen = true;
                _isPopupOpen = true;
            }
        }

        private void PlayerCountText_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isPopupOpen && !PlayersPopup.IsMouseOver)
            {
                PlayersPopup.IsOpen = false;
                _isPopupOpen = false;
            }
        }

        private void PlayersPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _isPopupOpen = true;
        }

        private void PlayersPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!PlayerCountText.IsMouseOver)
            {
                PlayersPopup.IsOpen = false;
                _isPopupOpen = false;
            }
        }

        private void PlayersPopup_Opened(object sender, EventArgs e)
        {
            _isPopupOpen = true;
        }

        private void PlayersPopup_Closed(object sender, EventArgs e)
        {
            _isPopupOpen = false;
        }

        private void SafeDeleteFile(string fileName)
        {
            string filePath = Path.Combine(Environment.CurrentDirectory, fileName);
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
                Thread.Sleep(500);
                try { File.Delete(filePath); } catch { }
            }
        }

        public void OnUpdateComplete(bool startAfter, bool canStartGame)
        {
            IsLoading = false;
            if (startAfter && canStartGame)
            {
                if (_readyInjectorPlan is not null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        HideProgress();
                        Button_Close(null, null);
                    });
                    InjectorLauncher.Launch(_readyInjectorPlan);
                    return;
                }

                string gamePath = Path.Combine(ClientFolder, "valheim.exe");
                if (File.Exists(gamePath))
                {
                    Dispatcher.Invoke(() =>
                    {
                        HideProgress();
                        Button_Close(null, null);
                    });
                    Process.Start(new ProcessStartInfo { FileName = gamePath, UseShellExecute = true });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(Loc.T("gui.valheimExeNotFound", ClientFolder), Loc.T("gui.title.launchError"), MessageBoxButton.OK, MessageBoxImage.Error);
                        HideProgress();
                        StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                        {
                            From = 0,
                            To = 1,
                            Duration = TimeSpan.FromMilliseconds(800)
                        });
                        UpdateLayout();
                    });
                    Log($"Launch error: valheim.exe not found in {ClientFolder}");
                }
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    HideProgress();
                    StartButtonGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(800)
                    });
                    UpdateLayout();
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

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(processStartInfo);
        }
    }
}