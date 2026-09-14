using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
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
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Line = Avalonia.Controls.Shapes.Line;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    // Duplicated from the WPF project on purpose: the two front ends don't reference each
    // other, only Launcher.Core, and this DTO is GUI-specific (the /serverinfo response
    // shape), not shared pipeline logic.
    public class ServerInfo
    {
        public string? name { get; set; }
        public int playersCount { get; set; }
        public List<PlayerInfo>? players { get; set; }

        // PublicWebLink (server-side mod) switched "mods" from a bare list of names to a list
        // of objects — deserializing that shape into List<string> throws, which silently took
        // down the whole status update (online indicator + player count included, not just the
        // mod list) since both come from this one response. Typed properly here even though
        // nothing displays it yet, so the same class doesn't need touching again if something
        // eventually does (a "mods on this server" tooltip, say).
        public List<ServerModInfo>? mods { get; set; }
    }

    public class ServerModInfo
    {
        public string? name { get; set; }
        public string? guid { get; set; }
        public string? version { get; set; }
        public string? description { get; set; }
        public string? websiteUrl { get; set; }
        public List<string>? dependencies { get; set; }
    }

    public class PlayerInfo
    {
        public string? Name { get; set; }
        public string? SteamID { get; set; }
        public string? AvatarUrl { get; set; }
    }

    // Source-generated (de)serialization for servers.json / /serverinfo — the reflection-based
    // JsonSerializer.Deserialize<T> overload is flagged RequiresUnreferencedCode: trimming can't
    // prove which members a reflection-driven deserializer needs, so a trimmed build risks
    // silently dropping one. A JsonSerializerContext sidesteps that entirely (no reflection at
    // runtime), which matters here since PublishTrimmed is something this project experiments with.
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(ServerList))]
    [JsonSerializable(typeof(ServerInfo))]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }

    public partial class MainWindow : Window, INotifyPropertyChanged, IUpdateUi
    {
        private static readonly List<string> LauncherUrls = LauncherMirrors.Default.ToList();

        // Empty (not null) until a mirror answers, or forever if none do — string.IsNullOrEmpty
        // is the check used everywhere this is read, so "no mirror yet" and "unset" share one
        // non-null representation instead of needing a separate null case.
        private static string ActiveLauncherUrl { get; set; } = string.Empty;

        // Scheme+host of ActiveLauncherUrl (e.g. "https://server.odinsons.club"), no path —
        // /serverinfo is proxied at each domain's own root ("<origin>/<server>/serverinfo"),
        // not under "/Launcher/" like servers.json and the manifests. Reusing the same
        // mirror-fallback domain here (instead of servers.json's own Ip/HttpPort) means status
        // checks work through whatever firewall/proxy setup already makes the rest of the
        // launcher reachable, instead of needing a second port opened per server.
        private static string ActiveLauncherOrigin =>
            Uri.TryCreate(ActiveLauncherUrl, UriKind.Absolute, out Uri? uri)
                ? uri.GetLeftPart(UriPartial.Authority)
                : string.Empty;

        // 20s, not the original 5s: these are small requests (status/version/manifest
        // existence checks), but nginx under a load spike (e.g. every player self-updating at
        // once) can genuinely take longer than 5s to respond — that's not the same as the
        // server being down. CheckMirrorAsync/FetchServerVersionAsync retry on top of this via
        // HttpRetry, so a single slow response doesn't need the full budget anyway.
        private static readonly HttpClient HttpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10
        }) { Timeout = TimeSpan.FromSeconds(20) };

        // The one shared "secondary text" gray for everything built in code — matches
        // #FFD6D6D6 in MainWindow.axaml (see that value's own comment, on TabControl.main
        // TabItem's style, for the WCAG contrast reasoning behind this exact number).
        private static readonly Color SecondaryTextColor = Color.FromRgb(0xD6, 0xD6, 0xD6);

        private Dictionary<string, string> _serverDirectories = new();
        public string SelectedServer { get; private set; } = string.Empty;
        public string SelectedServerDirectory { get; private set; } = string.Empty;
        public string ClientFolder { get; set; } = string.Empty;

        private readonly string _currentVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private BackgroundWorker _worker = new() { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
        private bool _isInitializing = true;
        private bool _isLoading;

        private Dictionary<string, string?> _serverDescriptions = new();

        // Backs ServerSelector.ItemsSource — one row per server, each with its own live
        // online/offline/checking dot (see ServerListItem below).
        private List<ServerListItem> _serverListItems = new();

        // Which server ShowModsPanelAsync last actually finished building the mods panel for —
        // null until the first successful build. Reselecting the Mods tab for the SAME server
        // just re-shows what's already there instead of re-fetching every manifest.json again;
        // switching servers naturally invalidates this since SelectedServer no longer matches.
        private string? _modsPanelLoadedForServer;
        private List<PlayerInfo> _currentPlayers = new();

        // Every static Loc.T() string ShowModsPanelAsync built ModsContent's current tree
        // from — cleared and repopulated on every rebuild, so RefreshModsLocalization can
        // re-translate them on a language switch without re-fetching every manifest.json.
        // Real mod names/descriptions aren't in here: they aren't localized strings.
        private readonly List<(TextBlock Block, string Key)> _modsLocalizedTexts = new();

        // Every mod row that has a description — cleared and repopulated on every
        // ShowModsPanelAsync rebuild, same reasoning as _modsLocalizedTexts. See
        // RefreshModsDescriptionAvailability: descriptions only ever come from a mod's own
        // manifest.json (Thunderstore convention), which has no per-language variants, so
        // reading one is only actually useful in the English UI.
        private readonly List<(Panel ChevronSlot, Grid HeaderRow, Action Collapse)> _modsDescriptionToggles = new();

        // Null until the first server check completes — see PopulatePlayersList/
        // RefreshPlayersLocalization. Distinct from _currentPlayers.Count itself since that's
        // 0 both before any check and after a real "nobody's online" answer.
        private int? _lastKnownPlayerCount;

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

        // Switches to the Install tab too — the progress panel lives there now instead of a
        // full-window overlay, so without this an update starting while the player is looking
        // at, say, Mods would just look like nothing happened.
        public void ShowProgress() => Dispatcher.UIThread.Invoke(() =>
        {
            InstallProgressPanel.IsVisible = true;
            InstallCancelButton.IsVisible = true;
            InstallCancelButton.IsEnabled = true;
            MainTabControl.SelectedItem = InstallTab;

            // The stale "no check has been run yet" (or a previous run's injector/classic mode
            // line) would otherwise sit there throughout the whole check — misleading once
            // there's no "Start" button to reference, and just wrong once SetInjectorPlan hasn't
            // fired yet for this run. SetInjectorPlan repopulates it partway through.
            InstallStatusText.Text = string.Empty;
            ToolTip.SetTip(InstallStatusText, null);

            // Just noise competing with the step list for space once real work starts — the
            // chevron stays clickable regardless, so re-expanding to look is never blocked,
            // only editing is (InstallSettingsStack.IsEnabled), until HideProgress re-enables it.
            CollapseInstallSettings();
            InstallSettingsStack.IsEnabled = false;
        });

        public void HideProgress() => Dispatcher.UIThread.Invoke(() =>
        {
            InstallProgressPanel.IsVisible = false;
            InstallCancelButton.IsVisible = false;
            InstallSettingsStack.IsEnabled = true;
        });

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

        private InjectorPlan? _readyInjectorPlan;

        // Distinct from _readyInjectorPlan itself being null: that's also the classic-mode
        // outcome of a completed check, not just "no check yet" — see
        // RefreshInstallStatusLocalization, which needs to tell the two apart.
        private bool _installStatusChecked;

        // InstallSettingsBox/InstallSettingsChevronSlot's collapse state — see
        // SetupInstallSettingsGroup/CollapseInstallSettings/ExpandInstallSettings. Starts true:
        // the group is expanded from the moment the window opens (unlike a mod's own
        // description, which starts collapsed), since these are settings worth seeing
        // up front, not something to dig for.
        private bool _installSettingsExpanded = true;
        private RotateTransform? _installSettingsChevronRotation;

        private RotateTransform? _refreshPlayersRotation;
        private CancellationTokenSource? _refreshPlayersSpinCts;

        public void SetInjectorPlan(InjectorPlan? plan)
        {
            _readyInjectorPlan = plan;
            _installStatusChecked = true;

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

        /// <summary>Re-localizes InstallStatusText on a language switch by re-deriving it from
        /// the same state SetInjectorPlan already tracks, rather than blindly overwriting it —
        /// otherwise a real result (or the "not checked yet" placeholder) could get clobbered
        /// by the wrong one. Doesn't account for the narrow transient window between
        /// ShowProgress blanking this text and the next SetInjectorPlan call repopulating it —
        /// a language switch in that exact moment briefly redisplays the previous run's status
        /// instead of staying blank, which self-corrects the moment the new check catches up.</summary>
        private void RefreshInstallStatusLocalization()
        {
            if (!_installStatusChecked)
            {
                InstallStatusText.Text = Loc.T("gui.installStatus.notCheckedYet");
                return;
            }

            InstallStatusText.Text = _readyInjectorPlan is not null
                ? Loc.T("gui.installMode.injector")
                : Loc.T("gui.installMode.classic");
            ToolTip.SetTip(InstallStatusText, _readyInjectorPlan is not null
                ? Loc.T("gui.installMode.injectorTip", _readyInjectorPlan.WorkingDirectory)
                : Loc.T("gui.installMode.classicTip"));
        }

        // State + fold-away behaviour live in InstallStepModel (Launcher.Core, unit-tested);
        // this class only renders it and keeps the per-step wall-clock timings, which are
        // cosmetic and don't belong in the model.
        private InstallStepModel? _stepModel;
        private readonly List<Stopwatch?> _stepStopwatches = new();
        private readonly List<TimeSpan?> _stepElapsed = new();
        private bool _interrupted;

        // The label overlaid on InstallCancelButton's sliced-image content — its Content is a
        // whole Grid (see BuildStretchButtonContent), so the "Stop checking"/"Stop downloading"/
        // "Stopping…" text goes here instead of Button.Content directly.
        private TextBlock _installCancelLabel = null!;

        // Same pattern for the main action button — its label changes with the wizard step
        // (see UpdateWizardButton), so it's a field instead of a one-shot local like the old
        // single "Играть" text was.
        private TextBlock _startButtonLabel = null!;

        // Loaded once in BuildActionButtons, reused by every RebuildStartButtonContent call
        // after that — the PNGs themselves don't change per wizard step, only how wide the
        // built content ends up (see RebuildStartButtonContent's own comment for why it rebuilds
        // per step instead of sizing once for the widest of all three, like InstallCancelButton
        // does).
        private (IImage Left, Bitmap Center, IImage Right) _startNormalSlices;
        private (IImage Left, Bitmap Center, IImage Right) _startHoverSlices;

        // Persisted across launches (config.ini) — see LoadAutoStartPreference. Governs whether
        // the wizard button's first Install-tab click (StartGameAsync's "start" flag) launches
        // the game right after validating, or leaves the player to click Play separately.
        private bool _autoStartAfterValidation = true;

        // Persisted across launches (config.ini, stored by name via Enum.TryParse/ToString) —
        // see LoadHasVisitedModsPreference/MarkModsTabVisited. Drives the main action button's
        // label/target (UpdateWizardButton): "Go to mods" until this is Visited, then a
        // Play/Install-flavored label depending on IsClientInstalled. Grandfathered to Visited
        // for an already-installed client that predates this flag (see
        // RefreshFullCheckVisibility) so upgrading players aren't sent back to Mods on their
        // next launch.
        private enum ModsTabVisit { NotVisited, Visited }
        private ModsTabVisit _modsTabVisit;

        // Last-known Normal-state placement — see RestoreWindowPlacement/SaveWindowPlacement.
        private PixelPoint? _normalPosition;
        private Size? _normalSize;

        public void SetSteps(IReadOnlyList<InstallStep> steps) => Dispatcher.UIThread.Invoke(() =>
        {
            _stepModel = new InstallStepModel(steps);
            _stepStopwatches.Clear();
            _stepElapsed.Clear();
            for (int i = 0; i < steps.Count; i++) { _stepStopwatches.Add(null); _stepElapsed.Add(null); }
            _interrupted = false;

            InstallOverallProgress.Value = 0;
            DownloadDetailPanel.IsVisible = false;
            DownloadDetailList.Children.Clear();
            // undoes ShowInterruptedProgress / InstallCancelButton_Click from a previous run
            InstallCancelButton.IsVisible = true;
            InstallCancelButton.IsEnabled = true;
            RenderInstallSteps();
            UpdateInstallHeader();
        });

        public void StartStep(int index) => Dispatcher.UIThread.Invoke(() =>
        {
            if (_stepModel is null) return;

            _stepModel.Start(index);
            if (index >= 0 && index < _stepStopwatches.Count) _stepStopwatches[index] = Stopwatch.StartNew();

            RenderInstallSteps();
            SyncOverallBar();
            UpdateInstallHeader();
        });

        public void SetStepProgress(double percent) => Dispatcher.UIThread.Invoke(() =>
        {
            if (_stepModel is null) return;

            _stepModel.SetProgress(percent);
            SyncOverallBar();
        });

        public void FinishStep(int index) => Dispatcher.UIThread.Invoke(() =>
        {
            if (_stepModel is null) return;

            _stepModel.Finish(index);
            if (index >= 0 && index < _stepStopwatches.Count && _stepStopwatches[index] is { } sw)
                _stepElapsed[index] = sw.Elapsed;

            RenderInstallSteps();
            SyncOverallBar();
            UpdateInstallHeader();
        });

        private void SyncOverallBar() =>
            InstallOverallProgress.Value = _stepModel?.OverallPercent ?? 0;

        private bool OnDownloadStep =>
            _stepModel is not null && _stepModel.CurrentLabel == Loc.T("dl.step.download");

        public void SetDownloadDetail(DownloadDetail detail) => Dispatcher.UIThread.Invoke(() =>
        {
            if (!OnDownloadStep) { DownloadDetailPanel.IsVisible = false; return; }

            DownloadDetailPanel.IsVisible = true;
            DownloadDetailHeadline.Text = detail.HeadlineText;

            DownloadDetailList.Children.Clear();
            foreach (DownloadDetailItem item in detail.Active)
                DownloadDetailList.Children.Add(BuildDownloadDetailRow(item));
        });

        private static Control BuildDownloadDetailRow(DownloadDetailItem item)
        {
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var title = new TextBlock
            {
                Text = item.Title,
                Foreground = Brushes.White,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);

            var size = new TextBlock
            {
                Text = item.SizeText,
                Foreground = new SolidColorBrush(Color.Parse("#FFA8A8A8")),
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(size, 1);

            top.Children.Add(title);
            top.Children.Add(size);

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = item.Fraction,
                Height = 3,
                Margin = new Thickness(0, 3, 0, 0)
            };

            return new StackPanel { Spacing = 0, Children = { top, bar } };
        }

        private void UpdateInstallHeader()
        {
            if (_stepModel is null || _stepModel.Steps.Count == 0)
            {
                InstallStepLabel.Text = string.Empty;
                InstallStepCounter.Text = string.Empty;
                return;
            }

            InstallStepLabel.Text = _stepModel.CurrentLabel;
            InstallStepCounter.Text = Loc.T("dl.stepCounter", _stepModel.CurrentOrdinal, _stepModel.Steps.Count);

            bool isDownloading = _stepModel.CurrentLabel == Loc.T("dl.step.download");
            _installCancelLabel.Text = isDownloading ? Loc.T("dl.cancelDownloading") : Loc.T("dl.cancelChecking");
            if (!isDownloading) DownloadDetailPanel.IsVisible = false;
        }

        /// <summary>Called from OnUpdateComplete instead of HideProgress when the run that just
        /// ended was cancelled — the progress bar and step order are left exactly as they were,
        /// just relabelled so the last-known percentage stays visible instead of the whole panel
        /// disappearing on cancel. The one step that was Active gets its spinner swapped for a
        /// paused icon (RenderInstallSteps, via _interrupted) — a still-spinning "checking"/
        /// "downloading" row otherwise reads as work still happening.</summary>
        private void ShowInterruptedProgress()
        {
            InstallStepLabel.Text = Loc.T("dl.interrupted");
            InstallStepCounter.Text = Loc.T("dl.interruptedPercent", (int)Math.Round(InstallOverallProgress.Value));
            InstallCancelButton.IsVisible = false; // nothing left to cancel
            _interrupted = true;
            RenderInstallSteps();
        }

        private void RenderInstallSteps()
        {
            InstallStepsPanel.Children.Clear();
            if (_stepModel is null) return;

            int stepIndex = 0;
            for (int g = 0; g < _stepModel.Groups.Count; g++)
            {
                InstallStepModel.GroupView group = _stepModel.Groups[g];
                int firstStep = stepIndex;
                int stepCount = group.Steps.Count;
                stepIndex += stepCount;

                bool singleStep = stepCount == 1;
                // A one-step group (Downloading, Finishing up) has nothing to fold — draw it
                // as a plain step row, no chevron, no header/child duplication.
                if (singleStep)
                {
                    InstallStepsPanel.Children.Add(BuildStepRow(group.Steps[0], _stepElapsed[firstStep], indent: false));
                    continue;
                }

                TimeSpan? groupElapsed = GroupElapsed(firstStep, stepCount);
                InstallStepsPanel.Children.Add(BuildGroupHeader(group, g, groupElapsed));

                if (!group.Collapsed)
                    for (int s = 0; s < stepCount; s++)
                        InstallStepsPanel.Children.Add(BuildStepRow(group.Steps[s], _stepElapsed[firstStep + s], indent: true));
            }
        }

        private TimeSpan? GroupElapsed(int firstStep, int count)
        {
            TimeSpan sum = TimeSpan.Zero;
            bool any = false;
            for (int i = firstStep; i < firstStep + count && i < _stepElapsed.Count; i++)
                if (_stepElapsed[i] is { } e) { sum += e; any = true; }
            return any ? sum : null;
        }

        private void GroupHeader_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: int groupIndex })
            {
                _stepModel?.ToggleGroup(groupIndex);
                RenderInstallSteps();
            }
        }

        private Control BuildGroupHeader(InstallStepModel.GroupView group, int groupIndex, TimeSpan? elapsed)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto") };

            Control icon = group.State switch
            {
                InstallStepModel.ItemState.Done => new TextBlock
                {
                    Text = "✓", FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#FF6FCF6F")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                },
                InstallStepModel.ItemState.Active => _interrupted ? BuildPausedIcon() : BuildInstallStepSpinner(),
                _ => new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = new SolidColorBrush(Color.Parse("#FF808080")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(icon, 0);

            var toggle = new Button
            {
                Tag = groupIndex,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = (group.Collapsed ? "▸  " : "▾  ") + group.Label,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                }
            };
            toggle.Click += GroupHeader_Click;
            Grid.SetColumn(toggle, 1);

            var time = new TextBlock
            {
                Text = elapsed is { } e ? $"{e.TotalSeconds:0.0}s" : string.Empty,
                Foreground = new SolidColorBrush(SecondaryTextColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(time, 2);

            row.Children.Add(icon);
            row.Children.Add(toggle);
            row.Children.Add(time);
            return row;
        }

        private Control BuildStepRow(InstallStepModel.StepView step, TimeSpan? elapsed, bool indent)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto") };
            if (indent) row.Margin = new Thickness(18, 0, 0, 0);

            Control icon = step.State switch
            {
                InstallStepModel.ItemState.Done => new TextBlock
                {
                    Text = "✓", FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#FF6FCF6F")),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                },
                InstallStepModel.ItemState.Active => _interrupted ? BuildPausedIcon() : BuildInstallStepSpinner(),
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
                Foreground = new SolidColorBrush(step.State == InstallStepModel.ItemState.Pending ? Color.Parse("#FF808080") : Colors.White),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);

            var time = new TextBlock
            {
                Text = elapsed is { } e ? $"{e.TotalSeconds:0.0}s" : string.Empty,
                Foreground = new SolidColorBrush(SecondaryTextColor),
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

        private static Bitmap LoadBitmap(string avaresUri) => new(AssetLoader.Open(new Uri(avaresUri)));

        /// <summary>
        /// Loads three separately-exported end-cap/tile images for BuildStretchButtonContent.
        /// </summary>
        private static (IImage Left, Bitmap Center, IImage Right) LoadStretchSlices(
            string leftUri, string centerUri, string rightUri) =>
            (LoadBitmap(leftUri), LoadBitmap(centerUri), LoadBitmap(rightUri));

        /// <summary>Font used for BuildStretchButtonContent's label, matched between the
        /// rendered TextBlock and the FormattedText used to measure it for auto-width.</summary>
        private static readonly Typeface StretchButtonTypeface =
            new(new FontFamily("avares://OdinsonsLauncher/Resources/Fonts#Montserrat"), FontStyle.Normal, FontWeight.Bold);
        private const double StretchButtonFontSize = 22;
        // Each side, between the text and where the tile ends — deliberately small: the caps
        // themselves (left/right button images) already read as the button's own padding, wood
        // carved in from the outer edge, so this only needs to keep the text off the tile
        // texture directly, not recreate a second layer of edge space. Was 24, then 16, back
        // when this only ever sized itself around one known word ("Играть") — now that the same
        // button's width has to fit whichever of "Далее"/"Проверить и установить"/"Играть" is
        // longest, any padding here is paid twice (once per side) on top of an already-long
        // phrase.
        private const double StretchButtonTextPadding = 6;

        private static double MeasureTextWidth(string text) =>
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                StretchButtonTypeface, StretchButtonFontSize, Brushes.White).Width;

        /// <summary>Valheim caps character names at 24 characters (no official spec, but
        /// community-confirmed), but sizing the column for 24 repeats of the single widest
        /// glyph ('W'/'Ш') massively overshoots — real names mix narrow and wide characters, so
        /// that pathological case never actually occurs and just wastes width nobody needs.
        /// Instead this measures each alphabet's *average* character width and multiplies by a
        /// budget a little under the full cap (21, not 24) — a real name at the cap can still
        /// lose its last character or two to PlayersList's TextTrimming="CharacterEllipsis"
        /// rather than the column paying for headroom an average name never uses. Same
        /// size/weight as that TextBlock, which sets neither explicitly and so inherits the
        /// Window's Montserrat/Medium.</summary>
        private static double ComputePlayersColumnWidth()
        {
            const int nameLengthBudget = 21;
            const double nameFontSize = 14;
            var nameTypeface = new Typeface(
                new FontFamily("avares://OdinsonsLauncher/Resources/Fonts#Montserrat"), FontStyle.Normal, FontWeight.Medium);

            double AverageCharWidth(string alphabet) =>
                new FormattedText(alphabet, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    nameTypeface, nameFontSize, Brushes.White).Width / alphabet.Length;

            // Upper/lower/digits/underscore — covers what Steam/Valheim names are actually
            // built from, not just letters.
            double latinAvg = AverageCharWidth("AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxYyZz0123456789_");
            double cyrillicAvg = AverageCharWidth("АаБбВвГгДдЕеЖжЗзИиЙйКкЛлМмНнОоПпРрСсТтУуФфХхЦцЧчШшЩщЪъЫыЬьЭэЮюЯя");

            const double avatarWidth = 28;
            const double avatarGap = 10; // PlayersList's own row Spacing="10"
            const double sideMargins = 22; // its StackPanel's Margin (12 left, 10 facing the right column)
            const double scrollbarAllowance = 4;

            return Math.Ceiling(Math.Max(latinAvg, cyrillicAvg) * nameLengthBudget)
                + avatarWidth + avatarGap + sideMargins + scrollbarAllowance;
        }

        /// <summary>
        /// A button skin built from three slices instead of one fixed-size image: the end caps
        /// render at their native aspect ratio, the middle repeats via ImageBrush TileMode to
        /// fill however much width the label needs — so one source asset works at any button
        /// width instead of needing a re-export per size (or looking stretched/blurry from
        /// naively scaling the whole thing, caps included).
        ///
        /// Width is driven by content, not fixed: <paramref name="possibleTexts"/> covers every
        /// string the label might ever show (a button whose text changes between states — Stop's
        /// "Stop checking"/"Stop downloading"/"Stopping…" — needs to be wide enough for all of
        /// them, not just whichever set it first, or it'd visibly resize mid-run).
        /// </summary>
        private static Panel BuildStretchButtonContent(
            double height, IReadOnlyList<string> possibleTexts,
            (IImage Left, Bitmap Center, IImage Right) normal,
            (IImage Left, Bitmap Center, IImage Right) hover,
            out TextBlock label)
        {
            // Whole pixels, not fractional DIPs: at this scale (source images ~470-730px tall,
            // rendered ~64px) a fractional edge — 54.008 instead of 54 — lands each adjacent
            // piece's boundary on a different sub-pixel offset, and the rasterizer antialiases
            // each independently, which is what the seam actually was (extending the caps'
            // overlap alone didn't fix it because that overlap was itself still fractional).
            double leftWidth = Math.Round(height * (normal.Left.Size.Width / normal.Left.Size.Height));
            double rightWidth = Math.Round(height * (normal.Right.Size.Width / normal.Right.Size.Height));
            double tileWidth = Math.Round(height * (normal.Center.Size.Width / normal.Center.Size.Height));

            double textWidth = possibleTexts.Count == 0 ? 0 : possibleTexts.Max(MeasureTextWidth);
            double centerWidth = Math.Round(textWidth) + StretchButtonTextPadding * 2;

            // Round up to a whole number of tiles: TileMode.Tile repeats the center texture
            // every tileWidth px, and DestinationRect only sizes ONE tile — the browser/renderer
            // clips whatever's left over at the end. When centerWidth isn't an exact multiple of
            // tileWidth, that leftover is a partial tile cut off mid-pattern right before the
            // right cap — a second, independent source of the seam from the "whole pixels" fix
            // above (that one fixed sub-pixel edges; this one fixes a sub-*tile* edge). Barely
            // mattered for the old single-text Play button (one specific width, rarely far from
            // a clean multiple); now that one button's width is driven by whichever of several
            // very different-length texts is longest, it needs handling for real.
            centerWidth = Math.Ceiling(centerWidth / tileWidth) * tileWidth;

            double width = leftWidth + centerWidth + rightWidth;

            // Small integer overlap on top of the rounding above — each cap renders a couple
            // pixels wider than its own aspect ratio calls for (stretched slightly, still
            // anchored to the outer edge), and the tile reaches the same couple pixels back
            // under it, so there's still a safety margin even if the two land a pixel apart.
            const double overlap = 2;

            var root = new Panel { Width = width, Height = height };

            root.Children.Add(BuildStretchCenterLayer(normal.Center, height, tileWidth, leftWidth, rightWidth, overlap, "normalImg"));
            root.Children.Add(BuildStretchCenterLayer(hover.Center, height, tileWidth, leftWidth, rightWidth, overlap, "hoverImg"));

            root.Children.Add(BuildStretchCapImage(normal.Left, leftWidth, overlap, height, HorizontalAlignment.Left, "normalImg"));
            root.Children.Add(BuildStretchCapImage(hover.Left, leftWidth, overlap, height, HorizontalAlignment.Left, "hoverImg"));
            root.Children.Add(BuildStretchCapImage(normal.Right, rightWidth, overlap, height, HorizontalAlignment.Right, "normalImg"));
            root.Children.Add(BuildStretchCapImage(hover.Right, rightWidth, overlap, height, HorizontalAlignment.Right, "hoverImg"));

            label = new TextBlock
            {
                Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = StretchButtonFontSize,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            root.Children.Add(label);

            return root;
        }

        private static Border BuildStretchCenterLayer(
            Bitmap tile, double height, double tileWidth, double leftWidth, double rightWidth, double overlap, string hoverClass)
        {
            var border = new Border
            {
                Margin = new Thickness(leftWidth - overlap, 0, rightWidth - overlap, 0),
                IsHitTestVisible = false,
                Background = new ImageBrush
                {
                    Source = tile,
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    // DestinationRect, not Viewport — this Avalonia version's TileBrush has no
                    // Viewport/ViewportUnits (that's WPF); DestinationRect+Absolute is the
                    // equivalent, sizing one tile in the destination's own pixel space.
                    DestinationRect = new RelativeRect(0, 0, tileWidth, height, RelativeUnit.Absolute)
                }
            };
            border.Classes.Add(hoverClass);
            return border;
        }

        private static Image BuildStretchCapImage(
            IImage source, double capWidth, double overlap, double height, HorizontalAlignment side, string hoverClass)
        {
            var image = new Image
            {
                // Slightly wider than the cap's own aspect ratio calls for, still anchored to
                // the outer edge — the extra sliver is a barely-there stretch (a few percent
                // over ~54-70px), trading unnoticeable distortion for guaranteed opaque coverage
                // all the way past the seam.
                Source = source, Stretch = Stretch.Fill,
                Width = capWidth + overlap, Height = height,
                HorizontalAlignment = side, IsHitTestVisible = false
            };
            image.Classes.Add(hoverClass);
            return image;
        }

        // Same 14x14 slot as the spinner, so swapping between them doesn't shift the row.
        private static Panel BuildPausedIcon() => new()
        {
            Width = 14, Height = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 3,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new Rectangle { Width = 3, Height = 12, Fill = Brushes.White },
                        new Rectangle { Width = 3, Height = 12, Fill = Brushes.White }
                    }
                }
            }
        };

        private void InstallCancelButton_Click(object? sender, RoutedEventArgs e)
        {
            // Immediate feedback: the parallel file-check loops can take a moment to notice
            // CancellationPending (see FileDownloader's Parallel.ForEach state.Stop() calls),
            // and without this the button just sat there looking hung for that gap.
            InstallCancelButton.IsEnabled = false;
            _installCancelLabel.Text = Loc.T("dl.stopping");
            _worker?.CancelAsync();
        }

        #endregion

        // AvaloniaObject already declares its own PropertyChanged (for AvaloniaProperty change
        // notifications) — new deliberately hides it with this unrelated INotifyPropertyChanged one.
        public new event PropertyChangedEventHandler? PropertyChanged;
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
                string? exePath = Environment.ProcessPath;
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
            // Before InitializeComponent: nothing in it calls Loc.T today, but the very next
            // thing this constructor does is a batch of them (ApplyLocalizedChrome, below), so
            // the preferred language needs to already be active before those run.
            LoadLanguagePreference();

            InitializeComponent();
            DataContext = this;

            WireScrollShadows(PlayersScrollViewer, PlayersTopShadow, PlayersBottomShadow);
            WireScrollShadows(ServerRightScrollViewer, ServerRightTopShadow, ServerRightBottomShadow);
            WireScrollShadows(ModsScrollViewer, ModsTopShadow, ModsBottomShadow);
            WireScrollShadows(InstallScrollViewer, InstallTopShadow, InstallBottomShadow);
            SetupInstallSettingsGroup();
            SetupRefreshPlayersButton();

            PlayersColumn.Width = ComputePlayersColumnWidth();

            // Windows 11's own automatic window-corner rounding doesn't apply to
            // SystemDecorations="None" windows by default — everything drawn inside (the
            // central panel included) can be as rounded as it likes, but the window's own
            // outer silhouette stays a hard rectangle unless asked for explicitly. No-ops
            // harmlessly on Windows 10 (DWMWA_WINDOW_CORNER_PREFERENCE isn't recognized there)
            // and on macOS/Linux (RuntimePlatform.IsWindows guard).
            Opened += (_, __) => ApplyRoundedWindowCorners();

            // Only while WindowState is Normal: Width/Height/Position all reflect the
            // maximized bounds while maximized, and "restore to this later" has to mean the
            // normal size, not that — see SaveWindowPlacement.
            PositionChanged += (_, e) => { if (WindowState == WindowState.Normal) _normalPosition = e.Point; };
            Resized += (_, e) => { if (WindowState == WindowState.Normal) _normalSize = e.ClientSize; };
            // Alt-F4, the taskbar's own "Close window", or anything else that closes the window
            // without going through CloseButton_Click (the drawn "X", the only thing wired to
            // it directly) used to skip _worker.CancelAsync() entirely — an update/check
            // running at that moment got no cancellation signal at all, just whatever happens
            // when the process dies mid-operation. Same best-effort cancel-then-save as
            // CloseButton_Click now, and the same _closing guard so the two paths don't double
            // up if one leads to the other.
            Closing += (_, __) =>
            {
                if (_closing) return;
                _closing = true;

                try { _worker?.CancelAsync(); } catch { /* not running */ }
                SaveWindowPlacement();
            };
            RestoreWindowPlacement();
            LoadAutoStartPreference();
            AutoStartAfterValidationCheckBox.IsChecked = _autoStartAfterValidation;
            LoadHasVisitedModsPreference();
            LoadDesktopShortcutState();
            LoadStartMenuShortcutState();

            LanguageSelector.ItemsSource = LanguageListItems;
            LanguageSelector.SelectedItem = LanguageListItems.FirstOrDefault(i => i.Code == Loc.Language);

            if (File.Exists(LogFilePath)) File.WriteAllText(LogFilePath, string.Empty);

            // Covers InstallStatusText/PlayersHeading/PlayersList/MaximizeButton's tooltip too —
            // see ApplyLocalizedChrome, which folds in RefreshInstallStatusLocalization/
            // RefreshPlayersLocalization/UpdateMaximizeIcon precisely so this one call is enough
            // both here and from LanguageSelector_SelectionChanged.
            ApplyLocalizedChrome();

            if (Program.InstanceGuard is not null)
                Program.InstanceGuard.ActivateRequested += () => Dispatcher.UIThread.Invoke(BringToForeground);

            Loaded += async (_, __) =>
            {
                // Deferred out of the constructor and behind Background priority specifically:
                // decoding a dozen PNGs (both buttons' normal+hover slices) synchronously in the
                // constructor ran before the window ever painted, adding a visible chunk to the
                // time between double-click and the window actually appearing. Background
                // priority means whatever's already queued (the first paint included) goes
                // first — the buttons pop in a beat later instead, which reads as far snappier
                // than the window itself being slow to open.
                await Dispatcher.UIThread.InvokeAsync(BuildActionButtons, DispatcherPriority.Background);

                Log($"Launcher starting — version {_currentVersion}, exe SHA-256 {ComputeExecutableHash()}");
                await InitializeLauncherUrlAsync();
                if (string.IsNullOrEmpty(ActiveLauncherUrl))
                {
                    await MessageBoxWindow.ShowAsync(this, Loc.T("gui.allServersDown"));
                    Log("All mirrors unreachable, closing the launcher");
                    Close();
                    return;
                }

                // Server choice comes only from the top selector or config.ini (the CLI passes
                // it the same way). LoadServersAsync reads config.ini, falls back to the first
                // server, and persists that choice on first run — no separate picker dialog.
                await MainWindowLoadedAsync();
                await CheckAndUpdateLauncherAsync();
            };

            _isInitializing = false;
        }

        private static readonly List<LanguageListItem> LanguageListItems =
            Loc.Supported.Select(code => new LanguageListItem { Code = code, Name = Loc.DisplayNames[code] }).ToList();

        /// <summary>Re-applies every purely-static, always-correct-to-overwrite piece of chrome
        /// text — tab headers, section headings, footer info, the wizard button — so
        /// LanguageSelector_SelectionChanged can call this and have the change actually show up
        /// without restarting. Deliberately narrower than "everything Loc.T touches": anything
        /// that could already hold real, non-placeholder state by the time the language changes
        /// (InstallStatusText once a check has run, the players list/heading, already-rendered
        /// server info/changelog/mods content) is left alone here — overwriting those blind
        /// would clobber real information with a stale placeholder. They pick up the new
        /// language the next time they're naturally refreshed (next check, next tab visit, or
        /// simply the next launch).</summary>
        private void ApplyLocalizedChrome()
        {
            LauncherVersionText.Text = $"{Loc.T("gui.launcherVersionLabel")} v{_currentVersion}";
            CopyrightText.Text = Loc.T("gui.copyrightLabel");
            OurSiteLinkText.Text = Loc.T("gui.websiteLink");

            InstallTab.Header = Loc.T("gui.tab.install");
            ServerTab.Header = Loc.T("gui.tab.server");
            ModsTab.Header = Loc.T("gui.tab.mods");
            ServerInfoHeading.Text = Loc.T("gui.tab.serverInfo");
            ChangelogHeading.Text = Loc.T("gui.tab.changelog");
            FullCheckLabel.Text = Loc.T("gui.fullCheckTooltip");
            InstallSettingsHeaderText.Text = Loc.T("gui.installSettingsHeading");
            AutoStartAfterValidationCheckBox.Content = Loc.T("gui.autoStartAfterValidation");
            if (RuntimePlatform.IsWindows)
            {
                DesktopShortcutCheckBox.Content = Loc.T("gui.desktopShortcut");
                StartMenuShortcutCheckBox.Content = Loc.T("gui.startMenuShortcut");
                DefenderExclusionCheckBox.Content = Loc.T("gui.defenderExclusionCheckbox");
                ToolTip.SetTip(DefenderExclusionCheckBox, Loc.T("defender.message"));
            }

            ToolTip.SetTip(MinimizeButton, Loc.T("gui.tooltip.minimize"));
            ToolTip.SetTip(CloseWindowButton, Loc.T("gui.tooltip.close"));
            ToolTip.SetTip(RefreshPlayersButton, Loc.T("gui.tooltip.refreshPlayers"));
            UpdateMaximizeIcon(WindowState); // also refreshes MaximizeButton's Maximize/Restore tooltip text

            UpdateWizardButton();
            RefreshPlayersLocalization();
            RefreshInstallStatusLocalization();
            RefreshModsLocalization();
            RefreshModsDescriptionAvailability();
        }

        private const string LanguageSettingKey = "Language";

        /// <summary>Same synchronous IniFile pattern as LoadAutoStartPreference, for the same
        /// reason — called first in the constructor, before InitializeComponent, so it's
        /// settled before anything reads Loc.T(). Loc.UsePreferredOrSystem already falls back to
        /// the system UI language on its own for an absent or unsupported saved value.</summary>
        private void LoadLanguagePreference()
        {
            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();
                Loc.UsePreferredOrSystem(config.Read(LanguageSettingKey, WindowSettingsSection));
            }
            catch (Exception ex)
            {
                Log($"Could not load the language preference: {ex.Message}");
            }
        }

        private void LanguageSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (LanguageSelector.SelectedItem is not LanguageListItem item || item.Code == Loc.Language) return;

            Loc.Use(item.Code);
            ApplyLocalizedChrome();

            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();
                config.Write(LanguageSettingKey, item.Code, WindowSettingsSection);
            }
            catch (Exception ex)
            {
                Log($"Could not save the language preference: {ex.Message}");
            }
        }

        /// <summary>Builds the Start/Stop buttons' sliced-image content — see
        /// BuildStretchButtonContent. Split out of the constructor and called from Loaded
        /// instead (Background priority) purely for startup latency: decoding a dozen PNGs
        /// synchronously before the window ever paints was a real, measurable chunk of the
        /// double-click-to-window-appearing delay.</summary>
        internal void BuildActionButtons()
        {
            const double buttonHeight = 64;
            const string res = "avares://OdinsonsLauncher/Resources/";

            _startNormalSlices = LoadStretchSlices(res + "start-left.png", res + "start-center.png", res + "start-right.png");
            _startHoverSlices = LoadStretchSlices(res + "start-left_hover.png", res + "start-center_hover.png", res + "start-right_hover.png");
            UpdateWizardButton();

            // Wide enough for whichever of these three the label ends up showing (see
            // UpdateInstallHeader / InstallCancelButton_Click) — not just the first one set.
            string[] cancelTexts =
            {
                Loc.T("dl.cancelChecking"), Loc.T("dl.cancelDownloading"), Loc.T("dl.stopping")
            };
            InstallCancelButton.Content = BuildStretchButtonContent(buttonHeight, cancelTexts,
                LoadStretchSlices(
                    res + "start-danger-burgundy-left.png", res + "start-danger-burgundy-center.png", res + "start-danger-burgundy-right.png"),
                LoadStretchSlices(
                    res + "start-danger-burgundy-left_hover.png", res + "start-danger-burgundy-center_hover.png", res + "start-danger-burgundy-right_hover.png"),
                out _installCancelLabel);

            StartGameBtn.IsEnabled = true; // starts disabled in XAML — see its own comment there
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
            ActiveLauncherUrl = string.Empty;
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
                var servers = JsonSerializer.Deserialize(serversJson, AppJsonContext.Default.ServerList);
                List<Server> allServers = servers?.Servers ?? new List<Server>();

                bool isAdmin = File.Exists(Path.Combine(Environment.CurrentDirectory, "admin"));
                var availableServers = isAdmin ? allServers : allServers.Where(s => !s.Hidden).ToList();

                if (availableServers.Count == 0)
                {
                    await MessageBoxWindow.ShowAsync(this, Loc.T("gui.noServersAvailable"));
                    Log("No servers available");
                    Close();
                    return;
                }

                _serverDirectories = availableServers.ToDictionary(s => s.Name, s => ActiveLauncherUrl + s.Name + "/");
                _serverDescriptions = availableServers.ToDictionary(s => s.Name, s => (string?)s.Description);

                _serverListItems = availableServers.Select(s => new ServerListItem { Name = s.Name }).ToList();
                ServerSelector.ItemsSource = _serverListItems;

                // Fire-and-forget: paints every server's dot (not just the selected one) without
                // holding up the rest of startup on N HTTP round-trips.
                _ = RefreshAllServerStatusesAsync();

                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                await config.LoadAsync();
                string selectedServerFromConfig = config.Read("SelectedServer", "Settings");
                var selectedItem = _serverListItems.FirstOrDefault(item => item.Name == selectedServerFromConfig);

                ServerSelector.SelectionChanged -= ServerSelector_SelectionChanged;
                ServerSelector.SelectedItem = selectedItem ?? _serverListItems.FirstOrDefault();
                ServerSelector.IsEnabled = _serverDirectories.Count > 1 && !IsLoading;
                ServerSelector.SelectionChanged += ServerSelector_SelectionChanged;
                RefreshServerSelectorMode();

                if (ServerSelector.SelectedItem is ServerListItem initialItem)
                {
                    SelectedServer = initialItem.Name;
                    SelectedServerDirectory = _serverDirectories[SelectedServer];
                    ClientFolder = Path.Combine("clients", SelectedServer);
                    Directory.CreateDirectory(ClientFolder);

                    // First run (or the stored server disappeared): persist the effective choice
                    // so config.ini always reflects what the launcher is using.
                    if (selectedServerFromConfig != SelectedServer)
                    {
                        try
                        {
                            await config.WriteAsync("SelectedServer", SelectedServer, "Settings");
                            Log($"Persisted selected server {SelectedServer} to config.ini");
                        }
                        catch (Exception ex)
                        {
                            Log($"Could not persist server selection to config.ini: {ex.Message}");
                        }
                    }

                    // Fire-and-forget: must not block the update/launch flow below.
                    _ = RefreshDefenderExclusionCheckboxAsync();

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
            await Task.CompletedTask;
            return;
#else
            // The self-update swaps a running .exe via a .bat helper — Windows only. On
            // macOS/Linux the launcher is distributed as a bundle/package and updates through
            // that channel, so just note a newer build exists and carry on.
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    string serverVer = await FetchServerVersionAsync();
                    if (CompareVersions(serverVer, _currentVersion) == VersionComparison.Older)
                        Log($"A newer launcher build is available ({serverVer}); self-update is Windows-only, skipping");
                }
                catch (Exception ex) { Log($"Launcher version check skipped: {ex.Message}"); }
                return;
            }

            try
            {
                string baseDir = Environment.CurrentDirectory;
                string currentExePath = Path.Combine(baseDir, "OdinsonsLauncher.exe");
                string tempExePath = Path.Combine(baseDir, "new_launcher.exe");

                Log($"Checking launcher version at {ActiveLauncherUrl}");
                string serverVersion = await FetchServerVersionAsync();
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

                string message = Loc.T("gui.launcherUpdateAvailable", serverVersion);
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

        /// <summary>Fetches version.txt with the same retry-on-429/503/timeout policy
        /// CheckMirrorAsync already uses for the rest of the pre-gameplay checks — nginx rate
        /// limits or a brief hiccup during a mass rollout (every player self-updating at once)
        /// shouldn't fail the self-update check outright on the very first attempt.</summary>
        private static async Task<string> FetchServerVersionAsync()
        {
            const int maxAttempts = 3;
            HttpStatusCode? lastStatus = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool lastAttempt = attempt == maxAttempts;
                try
                {
                    using var response = await HttpClient.GetAsync(ActiveLauncherUrl + "version.txt");
                    if (response.IsSuccessStatusCode)
                        return (await response.Content.ReadAsStringAsync()).Trim();

                    lastStatus = response.StatusCode;
                    if (!HttpRetry.IsRetryableStatus(response.StatusCode) || lastAttempt) break;

                    TimeSpan delay = HttpRetry.Delay(attempt, response);
                    Log($"version.txt busy ({response.StatusCode}), retrying in {delay.TotalSeconds:0.0}s");
                    await Task.Delay(delay);
                }
                catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
                {
                    if (lastAttempt) throw;

                    TimeSpan delay = HttpRetry.Delay(attempt, null);
                    Log($"version.txt check failed ({ex.GetType().Name}: {ex.Message}), retrying in {delay.TotalSeconds:0.0}s");
                    await Task.Delay(delay);
                }
            }

            throw new HttpRequestException($"version.txt unreachable after {maxAttempts} attempts (last status: {lastStatus})");
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

        // Not async: every step in here is either synchronous or deliberately fire-and-forget
        // (ShowChangelogAsync/ShowServerInfoAsync), so there's nothing left to await —
        // Task.CompletedTask keeps the signature callers already await unchanged.
        private Task InitializeAsync()
        {
            try
            {
                IsLoading = false;

                UpdateServerDescriptionText();
                RefreshFullCheckVisibility();

                Dispatcher.UIThread.Invoke(() =>
                {
                    InstallProgressPanel.IsVisible = false;
                    // Panel stays visible; the skeleton stands in for the text while it loads.
                    ServerInfoContent.Children.Clear();
                    ServerInfoSkeleton.IsVisible = true;
                });

                // Fire-and-forget, both of them: neither gates the rest of startup, and
                // failures inside either are already swallowed (see their own doc comments).
                _ = ShowChangelogAsync();
                _ = ShowServerInfoAsync();
            }
            finally
            {
                Dispatcher.UIThread.Invoke(() => StartButtonGrid.Opacity = 1);
            }

            return Task.CompletedTask;
        }

        /// <summary>Static text set once per server in servers.json (e.g. mode, wipe schedule) —
        /// see Server.Description in Launcher.Core/ServerModels.cs. Hidden when absent so an
        /// empty line doesn't sit above the player list.</summary>
        private void UpdateServerDescriptionText() => Dispatcher.UIThread.Invoke(() =>
        {
            string? text = _serverDescriptions.TryGetValue(SelectedServer, out string? d) ? d : null;
            ServerDescriptionText.Text = text ?? string.Empty;
            ServerDescriptionText.IsVisible = !string.IsNullOrWhiteSpace(text);
        });

        /// <summary>BepInEx/LogOutput.log is the same "is this client actually installed" signal
        /// FileDownloader itself already uses to force a full check when it's missing — reused
        /// here for the Full check button's visibility and the wizard button's next-step
        /// decision (see UpdateWizardButton).</summary>
        private bool IsClientInstalled =>
            !string.IsNullOrEmpty(ClientFolder) && File.Exists(Path.Combine(ClientFolder, "BepInEx", "LogOutput.log"));

        /// <summary>The Full check button is only meaningful once a client actually exists —
        /// see FullCheck's doc comment in MainWindow.axaml.</summary>
        private void RefreshFullCheckVisibility() => Dispatcher.UIThread.Invoke(() =>
        {
            FullCheck.IsVisible = IsClientInstalled;
            if (IsClientInstalled) MarkModsTabVisited();
            UpdateWizardButton();
        });

        /// <summary>Keeps the main action button labeled (and targeted) for whatever the next
        /// unresolved setup step actually is: "Go to mods" until Mods has ever been visited
        /// (MarkModsTabVisited), then "Play" once installed. While not yet installed, the label
        /// only gets specific about *what* clicking it does ("Install"/"Install & play", per
        /// AutoStartAfterValidationCheckBox — StartGameAsync runs the same real check either
        /// way, "Install & play" just also launches afterward) once already on the Install tab;
        /// from any other tab it's still just "Go to install", a navigation hint, not a promise
        /// of what happens next. Called on every tab change (MainTabControl_SelectionChanged)
        /// and whenever install/autostart state might have changed (RefreshFullCheckVisibility,
        /// AutoStartAfterValidationCheckBox_Click). See StartGameBtn_Click for what each state
        /// actually does when clicked.</summary>
        private void UpdateWizardButton()
        {
            if (_startNormalSlices.Center is null) return; // BuildActionButtons hasn't run yet

            string text;
            if (_modsTabVisit == ModsTabVisit.NotVisited)
                text = Loc.T("gui.wizardGoToMods");
            else if (IsClientInstalled)
                text = Loc.T("gui.playButton");
            else if (ReferenceEquals(MainTabControl.SelectedItem, InstallTab))
                text = _autoStartAfterValidation ? Loc.T("gui.installAndPlayButton") : Loc.T("gui.installButton");
            else
                text = Loc.T("gui.wizardGoToInstall");

            RebuildStartButtonContent(text);
        }

        /// <summary>Rebuilds StartGameBtn's sliced-image content sized for just this one text,
        /// instead of once for the widest of UpdateWizardButton's possible texts and reusing
        /// that same width for all of them (InstallCancelButton's approach, appropriate there
        /// since its text changes while visibly static on screen mid-download — resizing then
        /// would be jarring). This button's text only ever changes at a tab switch or
        /// install/autostart state change, a natural moment for its size to change too. The
        /// slice PNGs are loaded once in BuildActionButtons (_startNormalSlices/
        /// _startHoverSlices) and just reused here.</summary>
        private void RebuildStartButtonContent(string text)
        {
            const double buttonHeight = 64;
            StartGameBtn.Content = BuildStretchButtonContent(buttonHeight, new[] { text },
                _startNormalSlices, _startHoverSlices, out _startButtonLabel);
            _startButtonLabel.Text = text;
        }

        /// <summary>Builds the chevron for InstallSettingsChevronSlot and wires the click that
        /// expands/collapses InstallSettingsBox — same pattern as a mod row's own description
        /// (BuildModRow), just for this one fixed, XAML-known group instead of something built
        /// per-row in code. Called once, from the constructor.</summary>
        private void SetupInstallSettingsGroup()
        {
            _installSettingsChevronRotation = new RotateTransform(90) // starts expanded: pointing down
            {
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(180) }
                }
            };
            InstallSettingsChevronSlot.Children.Add(BuildModChevronVisual(_installSettingsChevronRotation));

            InstallSettingsBox.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Border.HeightProperty,
                    Duration = TimeSpan.FromMilliseconds(220),
                    Easing = new CubicEaseOut()
                }
            };

            InstallSettingsHeaderRow.PointerPressed += (_, __) =>
            {
                if (_installSettingsExpanded) CollapseInstallSettings();
                else ExpandInstallSettings();
            };
        }

        /// <summary>No-op if already collapsed (safe to call from ShowProgress regardless of
        /// whether the player got there first). InstallSettingsBox.Height is read here as a
        /// concrete number for the first time — up to now it's been left at Auto/NaN, correct
        /// for showing the group at its natural height with no code involved while expanded,
        /// but DoubleTransition needs two real numbers to animate between.</summary>
        private void CollapseInstallSettings()
        {
            if (!_installSettingsExpanded) return;
            _installSettingsExpanded = false;

            _installSettingsChevronRotation!.Angle = 0;
            InstallSettingsBox.Height = InstallSettingsBox.Bounds.Height;
            InstallSettingsBox.Height = 0;
        }

        private void ExpandInstallSettings()
        {
            if (_installSettingsExpanded) return;
            _installSettingsExpanded = true;

            _installSettingsChevronRotation!.Angle = 90;

            // Measuring InstallSettingsStack itself, not InstallSettingsBox — same reasoning as
            // BuildModRow's own Expand: the Border already has an explicit Height (0, right
            // now), which Measure would just echo back instead of the content's real height.
            InstallSettingsStack.Measure(new Size(InstallSettingsBox.Bounds.Width, double.PositiveInfinity));
            InstallSettingsBox.Height = InstallSettingsStack.DesiredSize.Height;
        }

        private const string AutoStartSettingKey = "AutoStartAfterValidation";

        /// <summary>Loads the persisted auto-start checkbox state — same synchronous IniFile
        /// pattern as RestoreWindowPlacement (called right alongside it, before Show()), for the
        /// same reason: a sync-over-async call here from the constructor would deadlock. Defaults
        /// to true (absent = never saved yet) so a first run behaves exactly like the old
        /// hardcoded-true "always launch after checking".</summary>
        private void LoadAutoStartPreference()
        {
            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();
                string saved = config.Read(AutoStartSettingKey, WindowSettingsSection);
                _autoStartAfterValidation = string.IsNullOrEmpty(saved) || saved == "true";
            }
            catch (Exception ex)
            {
                Log($"Could not load the auto-start preference: {ex.Message}");
            }
        }

        private static string DesktopShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OdinsonsLauncher.url");

        // Per-user (%APPDATA%\Microsoft\Windows\Start Menu\Programs) — no admin rights needed,
        // and Explorer's Start Menu "all apps" list has scanned this same folder (alongside the
        // all-users one) for .url shortcuts as long as .url shortcuts have existed, same as
        // .lnk; that scan is unrelated to the newer pinned-tiles/Store-app machinery.
        private static string StartMenuShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OdinsonsLauncher.url");

        private void LoadDesktopShortcutState()
        {
            DesktopShortcutCheckBox.IsVisible = RuntimePlatform.IsWindows;
            if (RuntimePlatform.IsWindows) LoadShortcutCheckboxState(DesktopShortcutCheckBox, DesktopShortcutPath, "desktop");
        }

        private void LoadStartMenuShortcutState()
        {
            StartMenuShortcutCheckBox.IsVisible = RuntimePlatform.IsWindows;
            if (RuntimePlatform.IsWindows) LoadShortcutCheckboxState(StartMenuShortcutCheckBox, StartMenuShortcutPath, "Start Menu");
        }

        /// <summary>Windows only — sets a shortcut checkbox's initial IsChecked from whether its
        /// target file actually exists, rather than a persisted config.ini flag: the file itself
        /// is the only source of truth, so a player deleting it by hand (or moving the exe and
        /// creating a new one) can't leave the checkbox showing a stale state.</summary>
        private void LoadShortcutCheckboxState(CheckBox checkbox, string path, string label)
        {
            try
            {
                checkbox.IsChecked = File.Exists(path);
            }
            catch (Exception ex)
            {
                Log($"Could not check for an existing {label} shortcut: {ex.Message}");
            }
        }

        private void DesktopShortcutCheckBox_Click(object? sender, RoutedEventArgs e) =>
            UpdateUrlShortcut(DesktopShortcutCheckBox, DesktopShortcutPath, "desktop");

        private void StartMenuShortcutCheckBox_Click(object? sender, RoutedEventArgs e) =>
            UpdateUrlShortcut(StartMenuShortcutCheckBox, StartMenuShortcutPath, "Start Menu");

        /// <summary>Shared by DesktopShortcutCheckBox_Click/StartMenuShortcutCheckBox_Click — a
        /// plain .url (Internet Shortcut) file, not a native .lnk. Windows has no built-in .NET
        /// API for the Shell Link binary format; creating one otherwise means a COM call into
        /// WScript.Shell. A .url pointing at a local file:// path still launches the target on
        /// double-click (from the desktop or the Start Menu alike) and still shows its own icon
        /// via IconFile/IconIndex, with no COM involved.</summary>
        private void UpdateUrlShortcut(CheckBox checkbox, string path, string label)
        {
            try
            {
                if (checkbox.IsChecked == true)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        checkbox.IsChecked = false;
                        return;
                    }

                    string content =
                        "[InternetShortcut]\r\n" +
                        $"URL=file:///{exePath.Replace('\\', '/')}\r\n" +
                        $"IconFile={exePath}\r\n" +
                        "IconIndex=0\r\n";
                    File.WriteAllText(path, content);
                }
                else
                {
                    File.Delete(path); // no-op if it never existed
                }
            }
            catch (Exception ex)
            {
                Log($"Could not update the {label} shortcut: {ex.Message}");
            }
        }

        /// <summary>Windows only — sets DefenderExclusionCheckBox from whether Defender's own
        /// exclusion list currently covers ClientFolder (DefenderExclusion.IsExcluded), the same
        /// "reflect live reality, not a stored flag" approach as the shortcut checkboxes above.
        /// Called both at startup and on every server switch, since each server has its own
        /// client folder and so its own independent exclusion state.</summary>
        private async Task RefreshDefenderExclusionCheckboxAsync()
        {
            DefenderExclusionCheckBox.IsVisible = RuntimePlatform.IsWindows;
            if (!RuntimePlatform.IsWindows || string.IsNullOrEmpty(ClientFolder)) return;

            string clientFolder = ClientFolder;
            bool excluded = await Task.Run(() => DefenderExclusion.IsExcluded(clientFolder));

            // The selected server (and so ClientFolder) could have changed again while this
            // ran — don't let a slow, now-stale check overwrite a newer one's result.
            if (clientFolder != ClientFolder) return;

            DefenderExclusionCheckBox.IsChecked = excluded;
        }

        /// <summary>Unlike the shortcut checkboxes, adding (or removing) a Defender exclusion
        /// needs a UAC prompt and can take a moment — disabled while that runs so a second
        /// click can't overlap it, and reverted to its previous state if the player declines
        /// the prompt or the command otherwise fails.</summary>
        private async void DefenderExclusionCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ClientFolder)) return;

            string clientFolder = ClientFolder;
            bool wantExcluded = DefenderExclusionCheckBox.IsChecked == true;

            DefenderExclusionCheckBox.IsEnabled = false;
            try
            {
                bool succeeded = await Task.Run(() => wantExcluded
                    ? DefenderExclusion.TryAddExclusion(clientFolder)
                    : DefenderExclusion.TryRemoveExclusion(clientFolder));

                if (!succeeded) DefenderExclusionCheckBox.IsChecked = !wantExcluded;
            }
            finally
            {
                DefenderExclusionCheckBox.IsEnabled = true;
            }
        }

        private const string HasVisitedModsSettingKey = "HasVisitedMods";

        /// <summary>Same synchronous IniFile pattern as LoadAutoStartPreference, for the same
        /// reason. Stored by enum name, not "true"/"false" — Enum.TryParse leaves _modsTabVisit
        /// at its default (NotVisited) for an absent/unrecognized value, same effect as the old
        /// bool's "never saved" default. MarkModsTabVisited grandfathers an already-installed
        /// client in once IsClientInstalled is known, later in startup.</summary>
        private void LoadHasVisitedModsPreference()
        {
            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();
                Enum.TryParse(config.Read(HasVisitedModsSettingKey, WindowSettingsSection), out _modsTabVisit);
            }
            catch (Exception ex)
            {
                Log($"Could not load the mods-visited preference: {ex.Message}");
            }
        }

        /// <summary>Records that the player has reached the Mods tab at least once — see
        /// UpdateWizardButton, which stops offering "Go to mods" as the next step once this is
        /// Visited. Called both from MainTabControl_SelectionChanged (a real visit) and
        /// RefreshFullCheckVisibility (grandfathering an already-installed client that predates
        /// this flag, since the old wizard always routed through Mods before a first install).</summary>
        private void MarkModsTabVisited()
        {
            if (_modsTabVisit == ModsTabVisit.Visited) return;
            _modsTabVisit = ModsTabVisit.Visited;

            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();
                config.Write(HasVisitedModsSettingKey, _modsTabVisit.ToString(), WindowSettingsSection);
            }
            catch (Exception ex)
            {
                Log($"Could not save the mods-visited preference: {ex.Message}");
            }
        }

        private void AutoStartAfterValidationCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            _autoStartAfterValidation = AutoStartAfterValidationCheckBox.IsChecked == true;
            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();
                config.Write(AutoStartSettingKey, _autoStartAfterValidation ? "true" : "false", WindowSettingsSection);
            }
            catch (Exception ex)
            {
                Log($"Could not save the auto-start preference: {ex.Message}");
            }

            // Only actually changes the label while sitting on the Install tab, not installed
            // yet — "Install" vs "Install & play" (see UpdateWizardButton) — but cheap enough
            // to just always refresh rather than checking first.
            UpdateWizardButton();
        }

        /// <summary>Fetches this server's changelog.md and caches it locally, falling back to
        /// the cached copy (or an "unavailable" message) if the mirror can't be reached — used
        /// by the Changelog tab (ShowChangelogAsync) and the unread-badge check.</summary>
        private async Task<string> FetchServerChangelogAsync()
        {
            string changelogPath = Path.Combine(ClientFolder, "changelog.md");
            try
            {
                Log($"Loading changelog for {SelectedServer} from {ActiveLauncherUrl}");
                string changelogText = await HttpClient.GetStringAsync(ActiveLauncherUrl + SelectedServer + "/changelog.md");
                await File.WriteAllTextAsync(changelogPath, changelogText);
                return changelogText;
            }
            catch (HttpRequestException)
            {
                return File.Exists(changelogPath)
                    ? await File.ReadAllTextAsync(changelogPath)
                    : Loc.T("gui.changelogUnavailable");
            }
        }

        /// <summary>
        /// Stale-while-revalidate for the Server tab's "About" section: a cached info.md (from
        /// a previous run) shows immediately — no reason to make the player wait on a network
        /// round-trip for text that's very likely unchanged since last time — while the current
        /// text is fetched in the background and swapped in only if it actually differs. With no
        /// cache yet (first-ever run for this server) the skeleton just stays up until the
        /// network answer arrives, since there's nothing else to show in the meantime.
        /// </summary>
        private async Task ShowServerInfoAsync()
        {
            string infoPath = Path.Combine(ClientFolder, "info.md");
            string? cached = null;
            try
            {
                if (File.Exists(infoPath)) cached = await File.ReadAllTextAsync(infoPath);
            }
            catch (Exception ex)
            {
                Log($"Could not read cached server info: {ex.Message}");
            }

            if (cached is not null)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    ParseMarkdownToUI(cached, ServerInfoContent);
                    ServerInfoSkeleton.IsVisible = false;
                });
            }

            try
            {
                Log($"Checking server info for {SelectedServer} at {ActiveLauncherUrl}");
                string fresh = await HttpClient.GetStringAsync(ActiveLauncherUrl + SelectedServer + "/info.md");
                if (fresh == cached) return; // unchanged — nothing to redraw

                await File.WriteAllTextAsync(infoPath, fresh);
                Dispatcher.UIThread.Invoke(() =>
                {
                    ParseMarkdownToUI(fresh, ServerInfoContent);
                    ServerInfoSkeleton.IsVisible = false;
                });
            }
            catch (Exception ex)
            {
                Log($"Could not refresh server info: {ex.Message}");

                // Already showing the cached copy — a failed background revalidation isn't
                // worth alarming the player over. Only genuinely nothing-to-show gets a message.
                if (cached is null)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        ServerInfoContent.Children.Add(new TextBlock
                        {
                            Text = Loc.T("gui.serverInfoUnavailable"),
                            Foreground = new SolidColorBrush(Colors.White),
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap
                        });
                        ServerInfoSkeleton.IsVisible = false;
                    });
                }
            }
        }

        /// <summary>Updates the matching row's dot in ServerSelector.ItemsSource — a no-op if
        /// the server was removed from servers.json between the list loading and this call.</summary>
        private void SetServerListItemStatus(string serverName, ServerOnlineStatus status)
        {
            ServerListItem? item = _serverListItems.FirstOrDefault(i => i.Name == serverName);
            if (item is not null) item.Status = status;
        }

        /// <summary>Picks which of ServerSelector/SingleServerDisplay is actually shown — see
        /// the comment on them in MainWindow.axaml. A disabled dropdown with nothing else to
        /// pick reads as broken, not "there's only one server, deliberately" — this shows a
        /// plain label instead once there's nothing to actually choose between.</summary>
        private void RefreshServerSelectorMode()
        {
            bool multipleServers = _serverListItems.Count > 1;
            ServerSelector.IsVisible = multipleServers;
            SingleServerDisplay.IsVisible = !multipleServers;
            SingleServerDisplay.Content = ServerSelector.SelectedItem ?? _serverListItems.FirstOrDefault();
        }

        /// <summary>Shows/hides a scroll edge's shadow (ScrollShadowTopBrush/BottomBrush in
        /// MainWindow.axaml) based on whether there's actually more content that way — "can
        /// scroll up" and "can scroll down" respectively, not just "is a ScrollViewer". Content
        /// added/removed later (rebuilding a tab's panel, a window resize) re-fires
        /// ScrollChanged the same as the player actually scrolling, since Extent/Viewport
        /// changing is exactly what that event reports.</summary>
        private static void WireScrollShadows(ScrollViewer scrollViewer, Border topShadow, Border bottomShadow)
        {
            void Update()
            {
                double maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                topShadow.IsVisible = scrollViewer.Offset.Y > 1;
                bottomShadow.IsVisible = scrollViewer.Offset.Y < maxOffset - 1;
            }

            scrollViewer.ScrollChanged += (_, __) => Update();
            Update();
        }

        /// <summary>Spinner instead of the dot while a check is in flight.</summary>
        private void ShowServerChecking() => Dispatcher.UIThread.Invoke(() =>
            SetServerListItemStatus(SelectedServer, ServerOnlineStatus.Checking));

        private void ShowServerStatus(bool online, int playerCount) => Dispatcher.UIThread.Invoke(() =>
        {
            SetServerListItemStatus(SelectedServer, online ? ServerOnlineStatus.Online : ServerOnlineStatus.Offline);
            PopulatePlayersList(playerCount);
        });

        /// <summary>Pings every server's /serverinfo purely for the dropdown's dots — unlike
        /// UpdateServerStatusAsync (selected server only), this doesn't touch the player list,
        /// so it's cheap to run for every server at once. Runs on startup (after the server list
        /// loads) and whenever the dropdown opens, so the dots don't go stale while it sits closed.</summary>
        private async Task RefreshAllServerStatusesAsync()
        {
            List<ServerListItem> items = _serverListItems;
            if (items.Count == 0) return;

            Dispatcher.UIThread.Invoke(() =>
            {
                foreach (ServerListItem item in items) item.Status = ServerOnlineStatus.Checking;
            });

            await Task.WhenAll(items.Select(async item =>
            {
                bool online = await ProbeServerOnlineAsync(item.Name);
                Dispatcher.UIThread.Invoke(() =>
                    item.Status = online ? ServerOnlineStatus.Online : ServerOnlineStatus.Offline);
            }));
        }

        private async Task<bool> ProbeServerOnlineAsync(string serverName)
        {
            if (string.IsNullOrEmpty(ActiveLauncherOrigin)) return false;

            try
            {
                using var response = await HttpClient.GetAsync($"{ActiveLauncherOrigin}/{serverName}/serverinfo");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log($"Status probe failed for {serverName}: {ex.Message}");
                return false;
            }
        }

        private void ServerSelector_DropDownOpened(object? sender, EventArgs e) => _ = RefreshAllServerStatusesAsync();

        private async Task UpdateServerStatusAsync()
        {
            ShowServerChecking();

            try
            {
                if (string.IsNullOrEmpty(ActiveLauncherOrigin))
                {
                    ShowServerStatus(online: false, playerCount: 0);
                    return;
                }

                string url = $"{ActiveLauncherOrigin}/{SelectedServer}/serverinfo";
                Log($"Requesting server status for {SelectedServer} via HTTP: {url}");
                using var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var serverInfo = JsonSerializer.Deserialize(json, AppJsonContext.Default.ServerInfo)
                    ?? throw new InvalidOperationException("empty /serverinfo response");

                _currentPlayers = serverInfo.players ?? new List<PlayerInfo>();
                ShowServerStatus(online: true, playerCount: serverInfo.playersCount);
                Log($"Received player list: {string.Join(", ", _currentPlayers.Select(p => p.Name))}");
                return;
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

        private async void RefreshPlayersButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_refreshPlayersSpinCts is not null) return; // a refresh is already in flight

            RefreshPlayersButton.IsEnabled = false;
            _refreshPlayersSpinCts = new CancellationTokenSource();
            Task spin = SpinRefreshPlayersIconAsync(_refreshPlayersSpinCts.Token);

            await UpdateServerStatusAsync();

            _refreshPlayersSpinCts.Cancel();
            await spin;
            _refreshPlayersSpinCts.Dispose();
            _refreshPlayersSpinCts = null;
            RefreshPlayersButton.IsEnabled = true;
        }

        /// <summary>Adds a full 360° turn every leg, for as long as the token isn't cancelled.
        /// Cancelling doesn't touch the leg already in flight — that keeps animating on its own
        /// via _refreshPlayersRotation's Transition until it lands back on a multiple of 360°
        /// (visually identical to the resting position); cancelling only stops scheduling the
        /// *next* one. So the icon always finishes its current lap before coming to rest,
        /// instead of snapping to a stop mid-turn the moment the refresh completes.</summary>
        private async Task SpinRefreshPlayersIconAsync(CancellationToken token)
        {
            TimeSpan legDuration = TimeSpan.FromMilliseconds(600);
            while (!token.IsCancellationRequested)
            {
                _refreshPlayersRotation!.Angle += 360;
                try { await Task.Delay(legDuration, token); }
                catch (TaskCanceledException) { return; }
            }
        }

        /// <summary>The one button doing whatever UpdateWizardButton's label promises, not just
        /// "advance a tab":
        /// - "Go to mods" (Mods never visited): switch tabs only, nothing to check yet.
        /// - "Play" (Mods visited, client installed): attempt to play right away, from
        ///   whichever tab is showing — StartGameAsync runs its usual check first, and if that
        ///   turns up real work to do, ShowProgress (IUpdateUi) already switches to the Install
        ///   tab on its own, so the player still sees it happening instead of it running
        ///   invisibly behind the Server tab.
        /// - "Go to install" (Mods visited, not installed): switch to the Install tab first —
        ///   once already there, the same click performs the actual check-and-install instead
        ///   (launching afterward only if AutoStartAfterValidationCheckBox says to).</summary>
        private async void StartGameBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (_modsTabVisit == ModsTabVisit.NotVisited)
            {
                MainTabControl.SelectedItem = ModsTab;
                return;
            }

            if (IsClientInstalled)
            {
                await StartGameAsync(false, start: true);
                return;
            }

            if (!ReferenceEquals(MainTabControl.SelectedItem, InstallTab))
            {
                MainTabControl.SelectedItem = InstallTab;
                return;
            }

            await StartGameAsync(false, _autoStartAfterValidation);
        }

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

                string? steamGameFolder = null;
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
            if (_isInitializing || IsLoading || ServerSelector.SelectedItem is not ServerListItem selectedItem)
                return;

            SelectedServer = selectedItem.Name;
            SelectedServerDirectory = _serverDirectories[SelectedServer];
            ClientFolder = Path.Combine("clients", SelectedServer);
            _ = RefreshDefenderExclusionCheckboxAsync();

            // Doesn't depend on the mirror/news chain below at all — it queries the game
            // server's own IP directly, not the launcher's own file mirror. Used to run after
            // all of that, which is why switching servers looked stuck on the old status for
            // as long as the slowest of those unrelated checks took.
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

        // The launcher's own version history lives on GitHub now, not fetched/rendered inline —
        // simpler once the repo went public, and one less thing to keep published on the server.
        private const string LauncherChangelogUrl = "https://github.com/odin-sons/Launcher/blob/master/CHANGELOG.md";

        private void LauncherChangelogButton_Click(object? sender, RoutedEventArgs e) =>
            Process.Start(new ProcessStartInfo { FileName = LauncherChangelogUrl, UseShellExecute = true });

        /// <summary>Selecting the Моды tab loads its content on demand — it used to be a
        /// click-to-open overlay (ViewModsChangelogButton_Click), now it's just "the tab became
        /// selected". The changelog no longer has a tab of its own to select — it's always part
        /// of the Server tab's own content, loaded once from InitializeAsync alongside the rest
        /// of that tab's info (see ShowChangelogAsync).</summary>
        private void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // sender, not the MainTabControl field: this can fire from inside InitializeComponent
            // itself (the TabControl selecting its first tab by default), before the named-field
            // pass that assigns MainTabControl has run — reading that field here threw a
            // NullReferenceException on every launch. sender is always the live instance already.
            if (sender is not TabControl tabControl) return;

            if (ReferenceEquals(tabControl.SelectedItem, ModsTab)) MarkModsTabVisited();

            UpdateWizardButton();

            if (!ReferenceEquals(tabControl.SelectedItem, ModsTab)) return;

            if (!IsLoading && _modsPanelLoadedForServer != SelectedServer) _ = ShowModsPanelAsync();
        }

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
            _modsLocalizedTexts.Clear();
            _modsDescriptionToggles.Clear();
            ModsContent.Children.Clear();
            ModsContent.Children.Add(BuildModsSkeleton());

            if (string.IsNullOrEmpty(ClientFolder) || string.IsNullOrEmpty(SelectedServer))
            {
                ModsContent.Children.Clear();
                ModsContent.Children.Add(NewTrackedModsText("mods.unavailable"));
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

            if (requiredGroups.Count == 0 && optionalGroups.Count == 0 && adminGroups.Count == 0)
            {
                ModsContent.Children.Clear();
                ModsContent.Children.Add(NewTrackedModsText("mods.unavailable"));
                return;
            }

            // Fetch every mod's manifest.json (name/version/description/website) BEFORE
            // building any real row — this is what actually kills the "placeholder name pops
            // in, then the row visibly grows once its description arrives" shift: nothing
            // swaps in from the skeleton until every row already has its final content.
            var allGroups = requiredGroups.Concat(optionalGroups).Concat(adminGroups)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First());
            var modData = new Dictionary<string, ModRowData>(StringComparer.OrdinalIgnoreCase);
            await Task.WhenAll(allGroups.Select(async kv => modData[kv.Key] = await FetchModRowDataAsync(kv.Key, kv.Value)));

            OptionalModSelection selection = OptionalModSelection.Load(ClientFolder);

            var requiredColumn = new StackPanel();
            if (requiredGroups.Count > 0)
            {
                requiredColumn.Children.Add(NewTrackedSectionHeader("mods.required", topGap: false));
                foreach (string folder in requiredGroups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    requiredColumn.Children.Add(BuildModRow(modData[folder], showToggle: false, isOn: true));
            }

            var optionalColumn = new StackPanel();
            if (optionalGroups.Count > 0)
            {
                optionalColumn.Children.Add(NewTrackedSectionHeader("mods.optional", topGap: false));
                foreach (string folder in optionalGroups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    optionalColumn.Children.Add(BuildModRow(modData[folder], showToggle: true, isOn: selection.IsSelected(folder)));
            }

            var finalContent = new StackPanel();
            if (requiredColumn.Children.Count > 0 || optionalColumn.Children.Count > 0)
                finalContent.Children.Add(BuildResponsiveModColumns(requiredColumn, optionalColumn));

            if (isAdmin && adminGroups.Count > 0)
            {
                finalContent.Children.Add(NewTrackedSectionHeader("mods.adminOnly"));
                foreach (string folder in adminGroups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    finalContent.Children.Add(BuildModRow(modData[folder], showToggle: false, isOn: true));
            }

            ModsContent.Children.Clear();
            ModsContent.Children.Add(finalContent);
            _modsPanelLoadedForServer = SelectedServer;
        }

        /// <summary>A handful of generic placeholder rows shown while ShowModsPanelAsync fetches
        /// every mod's manifest.json — swapped for the real content in one shot once everything
        /// is ready, instead of each row popping in and growing independently as its own fetch
        /// resolves (the actual cause of the old layout-shifting).</summary>
        private static Control BuildModsSkeleton()
        {
            var panel = new StackPanel();
            (double NameWidth, double DescWidth)[] rows = { (170, 340), (140, 300), (190, 260), (150, 320) };

            foreach ((double nameWidth, double descWidth) in rows)
            {
                var group = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

                var nameBar = new Border { Width = nameWidth, Height = 16, Margin = new Thickness(0, 0, 0, 6), Child = new Rectangle() };
                nameBar.Classes.Add("skeletonBar");
                var descBar = new Border { Width = descWidth, Child = new Rectangle() };
                descBar.Classes.Add("skeletonBar");

                group.Children.Add(nameBar);
                group.Children.Add(descBar);
                panel.Children.Add(group);
            }

            return panel;
        }

        /// <summary>
        /// Required and optional mods side by side when there's room, required on the left —
        /// below a width threshold, stacked instead, optional ON TOP: it's the shorter list a
        /// player is more likely to actually want to glance at and toggle, required is mostly
        /// just "yes, these are here" and can sit below the fold.
        /// </summary>
        private static Control BuildResponsiveModColumns(Control required, Control optional)
        {
            const double narrowThreshold = 520;

            var grid = new Grid();
            grid.Children.Add(required);
            grid.Children.Add(optional);

            // Not SizeChanged with an initial Relayout(grid.Bounds.Width) call — Bounds is still
            // (0,0,0,0) at that point (the grid hasn't been through a layout pass yet, since it
            // isn't even attached to the visual tree until the caller adds the returned control
            // to ModsContent), so that first call always picked the narrow layout regardless of
            // the real width, and apparently nothing ever corrected it afterwards. LayoutUpdated
            // fires after every real layout pass instead, so Bounds is always current by the
            // time Relayout reads it; lastWidth just avoids pointlessly reconfiguring
            // ColumnDefinitions/RowDefinitions (itself a re-layout) on passes where the width
            // hasn't actually changed.
            double lastWidth = double.NaN;

            void Relayout(double width)
            {
                if (Math.Abs(width - lastWidth) < 0.5) return;
                lastWidth = width;

                if (width >= narrowThreshold)
                {
                    grid.ColumnDefinitions = new ColumnDefinitions("*,20,*");
                    grid.RowDefinitions = new RowDefinitions("Auto");
                    Grid.SetColumn(required, 0);
                    Grid.SetRow(required, 0);
                    Grid.SetColumn(optional, 2);
                    Grid.SetRow(optional, 0);
                }
                else
                {
                    grid.ColumnDefinitions = new ColumnDefinitions("*");
                    // Middle row is a fixed-height spacer, not content — the two headers no
                    // longer carry their own top margin (see NewSectionHeader's topGap), so
                    // this is the only thing separating "optional" from "required" when stacked.
                    grid.RowDefinitions = new RowDefinitions("Auto,16,Auto");
                    Grid.SetColumn(optional, 0);
                    Grid.SetRow(optional, 0);
                    Grid.SetColumn(required, 0);
                    Grid.SetRow(required, 2);
                }
            }

            grid.LayoutUpdated += (_, __) => Relayout(grid.Bounds.Width);
            return grid;
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

        // topGap separates this header from whatever's above it — skip it when the header is
        // the first thing in its own container (BuildResponsiveModColumns' two columns), or it
        // stacks with that container's own top margin and reads as a lopsided gap before the
        // very first line of the tab.
        private static TextBlock NewSectionHeader(string text, bool topGap = true) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, topGap ? 16 : 0, 0, 8)
        };

        private static TextBlock NewModsText(string text, bool bold) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };

        /// <summary>NewSectionHeader, registered in _modsLocalizedTexts so
        /// RefreshModsLocalization can re-translate it later without rebuilding the panel.</summary>
        private TextBlock NewTrackedSectionHeader(string key, bool topGap = true)
        {
            TextBlock block = NewSectionHeader(Loc.T(key), topGap);
            _modsLocalizedTexts.Add((block, key));
            return block;
        }

        /// <summary>NewModsText (never bold — every current caller passes bold: false), same
        /// tracking as NewTrackedSectionHeader.</summary>
        private TextBlock NewTrackedModsText(string key)
        {
            TextBlock block = NewModsText(Loc.T(key), bold: false);
            _modsLocalizedTexts.Add((block, key));
            return block;
        }

        /// <summary>Re-translates every section heading/placeholder ShowModsPanelAsync built
        /// ModsContent's current tree from — see _modsLocalizedTexts. Doesn't touch real mod
        /// names/descriptions (not localized strings) or re-fetch anything, so a language
        /// switch updates "Обязательные моды"/"Опциональные моды" immediately instead of only
        /// on the next tab visit or server switch.</summary>
        private void RefreshModsLocalization()
        {
            foreach ((TextBlock block, string key) in _modsLocalizedTexts)
                block.Text = Loc.T(key);
        }

        /// <summary>Registers a mod row's expand affordance and immediately applies today's
        /// language gate to it — called once per row, from BuildModRow, right after its click
        /// handler is wired up.</summary>
        private void RegisterModDescriptionToggle(Panel chevronSlot, Grid headerRow, Action collapse)
        {
            _modsDescriptionToggles.Add((chevronSlot, headerRow, collapse));
            ApplyModDescriptionLanguageGate(chevronSlot, headerRow);
        }

        private static void ApplyModDescriptionLanguageGate(Panel chevronSlot, Grid headerRow)
        {
            bool available = Loc.Language == "en";
            chevronSlot.IsVisible = available;
            headerRow.Cursor = available ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }

        /// <summary>Re-applies the English-only gate on every already-built mod row when the
        /// language changes — hides/shows the chevron and collapses anything left expanded when
        /// switching away from English (its description is no longer readable, so leaving it
        /// open serves nothing); switching back to English just re-reveals the chevron, always
        /// starting collapsed again rather than restoring whatever was open before.</summary>
        private void RefreshModsDescriptionAvailability()
        {
            bool available = Loc.Language == "en";
            foreach ((Panel chevronSlot, Grid headerRow, Action collapse) in _modsDescriptionToggles)
            {
                ApplyModDescriptionLanguageGate(chevronSlot, headerRow);
                if (!available) collapse();
            }
        }

        private static Bitmap? _thunderstoreIcon;
        private static Bitmap ThunderstoreIcon => _thunderstoreIcon ??=
            new Bitmap(AssetLoader.Open(new Uri("avares://OdinsonsLauncher/Resources/thunderstore_icon.png")));

        private static Bitmap? _hexiumIcon;
        private static Bitmap HexiumIcon => _hexiumIcon ??=
            new Bitmap(AssetLoader.Open(new Uri("avares://OdinsonsLauncher/Resources/hexium_icon.png")));

        /// <summary>Everything a mod row needs to render, resolved up front — see
        /// FetchModRowDataAsync. Keeping BuildModRow purely synchronous (no row ever mutates
        /// after being built) is what avoids the old per-row layout shift.</summary>
        private sealed record ModRowData(
            string FolderKey, string Name, string? Version, string? Description,
            string? ThunderstoreUrl, string? HexiumUrl, string? WebsiteUrl);

        private async Task<ModRowData> FetchModRowDataAsync(string folderKey, List<string> files)
        {
            string thunderstoreUrl = ModStoreLinks.ThunderstoreUrl(folderKey);
            string hexiumUrl = ModStoreLinks.HexiumUrl(folderKey);

            string manifestJsonPath = ModGrouping.FindManifestJsonPath(files);
            if (manifestJsonPath is null)
                return new ModRowData(folderKey, folderKey, null, null, thunderstoreUrl, hexiumUrl, null);

            try
            {
                string json = await HttpClient.GetStringAsync(SelectedServerDirectory + manifestJsonPath);
                ModManifest manifest = ModManifest.Parse(json);

                return new ModRowData(
                    folderKey,
                    string.IsNullOrEmpty(manifest.Name) ? folderKey : manifest.Name,
                    manifest.VersionNumber,
                    manifest.Description,
                    thunderstoreUrl, hexiumUrl, manifest.WebsiteUrl);
            }
            catch (Exception ex)
            {
                Log($"Could not load {manifestJsonPath}: {ex.Message}");
                return new ModRowData(folderKey, folderKey, null, null, thunderstoreUrl, hexiumUrl, null);
            }
        }

        /// <summary>Collapsed by default — expanding every mod's description at once (the old
        /// behavior) is what made a long list this dense in the first place. Chevron rotates
        /// 0°→90° (right→down) and the description's own height animates from/to its measured
        /// value, not IsVisible toggling — see the "not yet expanded" comment below for the one
        /// case this doesn't track (a live width change while a row happens to be expanded).</summary>
        private Control BuildModRow(ModRowData data, bool showToggle, bool isOn)
        {
            bool hasDescription = !string.IsNullOrEmpty(data.Description);

            var chevronRotation = new RotateTransform(0)
            {
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(180) }
                }
            };
            // Fixed-width slot even when there's nothing to expand, so every row's name still
            // starts at the same x regardless of which mods happen to have a description.
            var chevronSlot = new Panel { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            if (hasDescription)
                chevronSlot.Children.Add(BuildModChevronVisual(chevronRotation));

            var name = new TextBlock
            {
                Text = data.Name,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 0, 0)
            };
            var version = new TextBlock
            {
                Text = data.Version,
                Foreground = new SolidColorBrush(SecondaryTextColor),
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Chevron + name + version share this sub-grid so the name column (the only "*"
            // one) is what actually shrinks/ellipsizes — version, the link icons, and the
            // optional-mod toggle all stay at their natural width in headerRow below and never
            // get pushed out of the row.
            var toggleZone = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Grid.SetColumn(chevronSlot, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(version, 2);
            toggleZone.Children.Add(chevronSlot);
            toggleZone.Children.Add(name);
            toggleZone.Children.Add(version);

            var linkIcons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (data.ThunderstoreUrl is not null)
                linkIcons.Children.Add(BuildModLinkButton(NewIconImage(ThunderstoreIcon), data.ThunderstoreUrl));
            if (data.HexiumUrl is not null)
                linkIcons.Children.Add(BuildModLinkButton(NewIconImage(HexiumIcon), data.HexiumUrl));
            if (!string.IsNullOrEmpty(data.WebsiteUrl))
                linkIcons.Children.Add(BuildModLinkButton(BuildWebsiteIconVisual(), data.WebsiteUrl));

            // The toggle switch lives in this same Grid, not as a sibling of the whole
            // name+description block below — that's what kept it vertically centered against
            // the row's full (collapsed-or-expanded) height instead of just this one header
            // line, visibly sinking below the header once descriptions stopped always being
            // shown. One shared row height here, everything in it lines up the same way
            // regardless of whether the description under it is expanded.
            var headerRow = new Grid
            {
                // Explicit Background, not left null/Transparent's absence: an unset Background
                // makes the gaps between children non-hit-testable in Avalonia (same fix as
                // Button.iconButton's own template, see its comment above in Window.Styles) —
                // without this, only the actual glyph pixels of the chevron/text register
                // clicks, which is most of why "hitting the arrow" was so unreliable.
                Background = Brushes.Transparent,
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
            };
            Grid.SetColumn(toggleZone, 0);
            Grid.SetColumn(linkIcons, 1);
            headerRow.Children.Add(toggleZone);
            headerRow.Children.Add(linkIcons);

            ToggleSwitch? toggle = null;
            if (showToggle)
            {
                toggle = new ToggleSwitch
                {
                    IsChecked = isOn,
                    OnContent = null,
                    OffContent = null,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                toggle.IsCheckedChanged += (_, __) =>
                {
                    if (string.IsNullOrEmpty(ClientFolder)) return;
                    OptionalModSelection current = OptionalModSelection.Load(ClientFolder);
                    current.SetSelected(data.FolderKey, toggle.IsChecked == true);
                    current.Save(ClientFolder);
                    Log($"Optional mod {data.FolderKey} {(toggle.IsChecked == true ? "selected" : "deselected")}");
                };
                Grid.SetColumn(toggle, 2);
                headerRow.Children.Add(toggle);
            }

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(headerRow);

            if (hasDescription)
            {
                var descriptionText = new TextBlock
                {
                    Text = data.Description,
                    Foreground = new SolidColorBrush(SecondaryTextColor),
                    FontSize = 12,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                // Height starts and stays a concrete number (never Auto/NaN) so DoubleTransition
                // always has two real values to animate between — collapsed to 0, expanded to
                // whatever descriptionText measures at the row's actual width. ClipToBounds hides
                // the text while collapsed instead of it just poking out above Height:0.
                var descriptionBox = new Border
                {
                    ClipToBounds = true,
                    Height = 0,
                    Child = descriptionText,
                    Transitions = new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = Border.HeightProperty,
                            Duration = TimeSpan.FromMilliseconds(220),
                            Easing = new CubicEaseOut()
                        }
                    }
                };

                bool expanded = false;

                void Collapse()
                {
                    if (!expanded) return;
                    expanded = false;
                    chevronRotation.Angle = 0;
                    descriptionBox.Height = 0;
                }

                void Expand()
                {
                    if (expanded) return;
                    expanded = true;
                    chevronRotation.Angle = 90;

                    // Measuring descriptionText itself, not descriptionBox: the Border already
                    // has an explicit Height (0, right now) that Measure would just echo back
                    // as its own DesiredSize instead of the content's real wanted height. The
                    // TextBlock has no such override, so its DesiredSize is the one number that
                    // actually reflects how tall the wrapped text needs to be at this width.
                    //
                    // Not tracked afterward: if the column width changes (window resize) while
                    // this row happens to be expanded, the fixed pixel Height set here won't
                    // follow a rewrap until the next collapse/expand — a rare, self-correcting
                    // edge case, not worth a live-resize listener for.
                    descriptionText.Measure(new Size(descriptionBox.Bounds.Width, double.PositiveInfinity));
                    descriptionBox.Height = descriptionText.DesiredSize.Height;
                }

                // On headerRow, not just toggleZone: makes the whole header line (including the
                // gap next to the link icons/toggle) the click target for expanding, while the
                // link buttons and the toggle switch — real Button/ToggleSwitch controls, not
                // just glyphs — still intercept their own presses first and never reach here.
                headerRow.PointerPressed += (_, e) =>
                {
                    // Descriptions only ever come from a mod's own manifest.json (Thunderstore
                    // convention) — there's no per-language variant, so reading one is only
                    // actually useful in the English UI. RegisterModDescriptionToggle already
                    // hides the chevron and resets the cursor outside English; this is the
                    // second half of that gate, blocking the click itself regardless of
                    // whatever the cursor happens to be showing at the moment.
                    if (Loc.Language != "en") return;

                    if (expanded) Collapse(); else Expand();
                };

                row.Children.Add(descriptionBox);
                RegisterModDescriptionToggle(chevronSlot, headerRow, Collapse);
            }

            return row;
        }

        private static Image NewIconImage(Bitmap bitmap) => new() { Source = bitmap, Width = 16, Height = 16 };

        // Margin, not Padding: Padding stays inside the button's own hit-test bounds, so with
        // it the clickable/hoverable area started 6px before the icon actually appeared — one
        // press or hover reaction covering what looked like a gap. Margin sits outside instead,
        // so the hit area is exactly the 16x16 icon, nothing more.
        private static Button BuildModLinkButton(Control icon, string url)
        {
            var button = new Button
            {
                Classes = { "iconButton", "modLinkIcon" },
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = icon
            };
            button.Click += (_, __) => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return button;
        }

        /// <summary>Feather Icons "chevron-right" (MIT) — points right collapsed, BuildModRow's
        /// click handler rotates the given RotateTransform to 90° (pointing down) on expand.</summary>
        private static Control BuildModChevronVisual(RotateTransform rotation)
        {
            var path = new ShapePath
            {
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Data = Geometry.Parse("M9 18l6-6-6-6")
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);

            return new Viewbox
            {
                Width = 16,
                Height = 16,
                Child = canvas,
                RenderTransform = rotation,
                RenderTransformOrigin = RelativePoint.Center
            };
        }

        /// <summary>Same service.png/service_green.png hover-swap pair as FullCheck's icon
        /// (Button Image.normalImg/.hoverImg — see Window.Styles). The rotation is applied to
        /// the wrapping Panel so both layers turn together; driven by hand via the
        /// RotateTransform's own Transition (set up in SetupRefreshPlayersButton) instead of
        /// Ellipse.stepSpinner's Style.Animations, which has no hook to finish the current lap
        /// and settle at 0° before stopping.</summary>
        private static Control BuildRefreshPlayersIcon(RotateTransform rotation)
        {
            var panel = new Panel
            {
                Width = 16,
                Height = 16,
                RenderTransform = rotation,
                RenderTransformOrigin = RelativePoint.Center
            };
            panel.Children.Add(new Image
            {
                Classes = { "normalImg" },
                Source = LoadBitmap("avares://OdinsonsLauncher/Resources/service.png"),
                Stretch = Stretch.Fill
            });
            panel.Children.Add(new Image
            {
                Classes = { "hoverImg" },
                Source = LoadBitmap("avares://OdinsonsLauncher/Resources/service_green.png"),
                Stretch = Stretch.Fill
            });
            return panel;
        }

        /// <summary>Called once, from the constructor — mirrors SetupInstallSettingsGroup.</summary>
        private void SetupRefreshPlayersButton()
        {
            _refreshPlayersRotation = new RotateTransform(0)
            {
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(600) }
                }
            };
            RefreshPlayersButton.Content = BuildRefreshPlayersIcon(_refreshPlayersRotation);
        }

        /// <summary>Feather Icons "globe" (MIT) — the same glyph the bottom bar's website
        /// button uses (MainWindow.axaml, OurSiteLinkText), rebuilt here in code since mod rows
        /// are built programmatically. A generic site icon, not a brand mark.</summary>
        private static Control BuildWebsiteIconVisual()
        {
            var circle = new Ellipse { Width = 20, Height = 20, Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(circle, 2);
            Canvas.SetTop(circle, 2);

            var equator = new Line
            {
                StartPoint = new Point(2, 12), EndPoint = new Point(22, 12),
                Stroke = Brushes.White, StrokeThickness = 2
            };

            var meridian = new ShapePath
            {
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Data = Geometry.Parse("M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z")
            };

            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(circle);
            canvas.Children.Add(equator);
            canvas.Children.Add(meridian);

            return new Viewbox { Width = 16, Height = 16, Child = canvas };
        }

        private async Task ShowChangelogAsync()
        {
            try
            {
                string changelogText = await FetchServerChangelogAsync();

                Dispatcher.UIThread.Invoke(() =>
                {
                    ChangelogContent.Children.Clear();
                    ParseMarkdownToUI(changelogText, ChangelogContent);
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
                });
                Log($"Error loading changelog: {ex.Message}");
            }
        }

        private static void ParseMarkdownToUI(string markdownText, StackPanel target)
        {
            var lines = markdownText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var textBlock = new TextBlock
                {
                    Foreground = new SolidColorBrush(SecondaryTextColor),
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
                    textBlock.Foreground = new SolidColorBrush(Colors.White);
                    textBlock.Margin = new Thickness(0, 5, 0, 5);
                }
                else if (line.TrimStart().StartsWith("-") || line.TrimStart().StartsWith("*"))
                {
                    string listItem = line.TrimStart('-', '*', ' ').Trim();
                    textBlock.Inlines ??= new InlineCollection();
                    textBlock.Inlines.Add(new Run("• "));
                    textBlock.Inlines.Add(new Run(listItem));
                }
                else
                {
                    textBlock.Text = line.Trim();
                }

                target.Children.Add(textBlock);
            }
        }

        private bool _closing;

        private void CloseButton_Click(object? sender, RoutedEventArgs? e)
        {
            if (_closing) return;
            _closing = true;

            try { _worker?.CancelAsync(); } catch { /* not running */ }

            // Belt and suspenders with the Closing-event hook in the constructor: not certain
            // desktop.Shutdown() below reliably raises Closing on the way down, and this is the
            // one guaranteed-to-run path for the actual close button. SaveWindowPlacement is
            // idempotent, so calling it twice if Closing does also fire is harmless.
            SaveWindowPlacement();

            // Not Environment.Exit(0): calling it from inside the UI loop tears the native
            // layer down under the render thread, and macOS reports that as a crash.
            if (global::Avalonia.Application.Current?.ApplicationLifetime
                is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(0);
            else
                Close();
        }

        private void Minimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        /// <summary>Restoring from Maximized doesn't reliably bring back the pre-maximize
        /// size/position on its own for a SystemDecorations="None" window — there's no native
        /// chrome for the OS to have tracked "restore bounds" against in the first place, so
        /// WindowState=Normal alone just left whatever Width/Height/Position happened to be
        /// showing (typically still the maximized ones). Reapplying _normalSize/_normalPosition
        /// — the same last-known-Normal snapshot SaveWindowPlacement already trusts over the
        /// live properties — fixes that. Captured into locals before touching WindowState: the
        /// PositionChanged/Resized handlers that update those fields guard on WindowState
        /// already being Normal, and a stray event firing mid-transition could otherwise
        /// overwrite them with a transient (still-maximized-looking) value before this method
        /// gets to use them.</summary>
        private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                Size? normalSize = _normalSize;
                PixelPoint? normalPosition = _normalPosition;

                WindowState = WindowState.Normal;

                if (normalSize is { } size) { Width = size.Width; Height = size.Height; }
                if (normalPosition is { } position) Position = position;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        /// <summary>Keeps MaximizeIcon/RestoreIcon in sync with the real WindowState — not just
        /// after MaximizeButton_Click, since the OS's own Win+Up/Win+Down (or a taskbar
        /// right-click "Maximize") still change it directly even with SystemDecorations="None".</summary>
        private void UpdateMaximizeIcon(WindowState state)
        {
            bool maximized = state == WindowState.Maximized;
            MaximizeIcon.IsVisible = !maximized;
            RestoreIcon.IsVisible = maximized;
            ToolTip.SetTip(MaximizeButton, Loc.T(maximized ? "gui.tooltip.restore" : "gui.tooltip.maximize"));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        /// <summary>WindowDecorations="None" opts this window out of Windows 11's own automatic
        /// corner rounding along with the rest of its native chrome, so it has to be asked for
        /// explicitly via DWM — otherwise the window's own outer silhouette stays a hard
        /// rectangle no matter how rounded the content drawn inside it is (see the central
        /// panel's own Border/ClipToBounds, a separate and already-working mechanism that only
        /// affects rendering, not the OS window shape). No-ops harmlessly pre-Windows 11
        /// (unrecognized attribute) and on non-Windows.</summary>
        private void ApplyRoundedWindowCorners()
        {
            if (!RuntimePlatform.IsWindows) return;

            try
            {
                IPlatformHandle? handle = TryGetPlatformHandle();
                if (handle is null) return;

                int preference = DwmwcpRound;
                DwmSetWindowAttribute(handle.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            }
            catch
            {
                // Cosmetic polish only — never worth failing startup over.
            }
        }

        private const string WindowSettingsSection = "Settings";

        /// <summary>Applies the size/position/maximized state saved by SaveWindowPlacement, if
        /// any — called from the constructor, before Show(), so the window opens directly where
        /// it was left rather than at the XAML defaults and then jumping.</summary>
        private void RestoreWindowPlacement()
        {
            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();

                if (double.TryParse(config.Read("WindowWidth", WindowSettingsSection), NumberStyles.Float, CultureInfo.InvariantCulture, out double width) &&
                    double.TryParse(config.Read("WindowHeight", WindowSettingsSection), NumberStyles.Float, CultureInfo.InvariantCulture, out double height) &&
                    width > 0 && height > 0)
                {
                    Width = Math.Max(MinWidth, width);
                    Height = Math.Max(MinHeight, height);
                }

                if (int.TryParse(config.Read("WindowX", WindowSettingsSection), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) &&
                    int.TryParse(config.Read("WindowY", WindowSettingsSection), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                {
                    var savedPosition = new PixelPoint(x, y);

                    // Only trust it if it still lands on a screen that's actually connected
                    // right now — otherwise a monitor unplugged or rearranged since the last
                    // run would leave the window stranded off-screen with no way back short of
                    // editing config.ini by hand.
                    if (Screens.All.Any(s => s.Bounds.Contains(savedPosition)))
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Position = savedPosition;
                    }
                }

                if (config.Read("WindowMaximized", WindowSettingsSection) == "true")
                    WindowState = WindowState.Maximized;
            }
            catch (Exception ex)
            {
                Log($"Could not restore window placement: {ex.Message}");
            }
        }

        /// <summary>Called from Closing. _normalPosition/_normalSize, not Width/Height/Position
        /// directly — those reflect the maximized bounds while WindowState is Maximized, and
        /// "restore to this later" has to mean the normal size to restore back to, not that.</summary>
        private void SaveWindowPlacement()
        {
            try
            {
                var config = new IniFile(Path.Combine(Environment.CurrentDirectory, "config.ini"));
                config.Load();

                Size size = _normalSize ?? new Size(Width, Height);
                PixelPoint position = _normalPosition ?? Position;

                config.Write("WindowWidth", size.Width.ToString(CultureInfo.InvariantCulture), WindowSettingsSection);
                config.Write("WindowHeight", size.Height.ToString(CultureInfo.InvariantCulture), WindowSettingsSection);
                config.Write("WindowX", position.X.ToString(CultureInfo.InvariantCulture), WindowSettingsSection);
                config.Write("WindowY", position.Y.ToString(CultureInfo.InvariantCulture), WindowSettingsSection);
                config.Write("WindowMaximized", WindowState == WindowState.Maximized ? "true" : "false", WindowSettingsSection);
            }
            catch (Exception ex)
            {
                Log($"Could not save window placement: {ex.Message}");
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WindowStateProperty) UpdateMaximizeIcon(WindowState);
        }

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

        /// <summary>The 8 invisible edge/corner Rectangles in the markup all share this one
        /// handler — Tag carries which WindowEdge each one represents.</summary>
        private void ResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control || control.Tag is not string edgeName) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (Enum.TryParse(edgeName, out WindowEdge edge)) BeginResizeDrag(edge, e);
        }

        private void DiscordButton_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        { FileName = "https://discord.gg/eTteBxWcfu", UseShellExecute = true });

        private void WebsiteButton_Click(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
        { FileName = "https://odinsons.club/", UseShellExecute = true });

        private readonly Dictionary<string, Bitmap> _avatarCache = new();

        /// <summary>One row in LanguageSelector — Loc.Supported's code plus its own native
        /// display name (Loc.DisplayNames). Never changes after creation, unlike ServerListItem/
        /// PlayerListItem, so no INotifyPropertyChanged.</summary>
        // internal, not private: same reason as ServerListItem below — LanguageSelector's
        // ItemTemplate x:DataType needs to resolve this by name from the XAML compiler.
        internal sealed class LanguageListItem
        {
            public required string Code { get; init; }
            public required string Name { get; init; }
        }

        internal enum ServerOnlineStatus { Unknown, Checking, Online, Offline }

        /// <summary>One row in ServerSelector — a server name plus its own live status dot, so
        /// every server's online/offline/checking state shows in the dropdown without having to
        /// select it first. See RefreshAllServerStatusesAsync / SetServerListItemStatus.</summary>
        // internal, not private: the compiled-binding codegen for ServerSelector.ItemTemplate's
        // x:DataType needs to resolve this type by name from the XAML compiler.
        internal sealed class ServerListItem : INotifyPropertyChanged
        {
            public required string Name { get; init; }

            private ServerOnlineStatus _status = ServerOnlineStatus.Unknown;
            public ServerOnlineStatus Status
            {
                get => _status;
                set
                {
                    _status = value;
                    Raise(nameof(Status));
                    Raise(nameof(ShowOnline));
                    Raise(nameof(ShowOffline));
                    Raise(nameof(ShowChecking));
                }
            }

            public bool ShowOnline => Status == ServerOnlineStatus.Online;
            public bool ShowOffline => Status == ServerOnlineStatus.Offline;
            // Unknown (not checked yet) reads as Checking, not Offline — otherwise every row
            // briefly shows the offline dot before its first probe even starts, which for a
            // moment overlapped with the spinner appearing on top of it (the offline dot was
            // still true for that first frame). Rolling "not checked yet" into "checking" means
            // there's never a real state where a row should show offline before it's actually
            // been probed at least once.
            public bool ShowChecking => Status is ServerOnlineStatus.Checking or ServerOnlineStatus.Unknown;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>Which of the avatar-slot visuals a row shows — bound from the DataTemplate
        /// via PlayerListItem's ShowXxx properties below. Loading is for the placeholder row(s)
        /// shown before the first real player check has come back at all — distinct from Empty
        /// (a real "nobody's online" answer) so the launcher never claims that before it's
        /// actually checked.</summary>
        internal enum PlayerRowKind { Player, Empty, Loading }

        /// <summary>One row in the always-visible player list on the Сервер tab.</summary>
        // internal, not private: same reason as ServerListItem above — PlayersList.ItemTemplate's
        // x:DataType needs to resolve this type by name from the XAML compiler.
        internal sealed class PlayerListItem : INotifyPropertyChanged
        {
            public required string Name { get; init; }
            public PlayerRowKind Kind { get; init; } = PlayerRowKind.Player;

            private Bitmap? _avatar;
            public Bitmap? Avatar
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

            public bool ShowAvatar => Kind == PlayerRowKind.Player && Avatar is not null;
            public bool ShowSkeleton => Kind == PlayerRowKind.Loading || (Kind == PlayerRowKind.Player && Avatar is null);
            public bool ShowEmptyIcon => Kind == PlayerRowKind.Empty;

            // A real player name is one line, trimmed with an ellipsis if the (deliberately
            // narrow) column can't fit it — see ComputePlayersColumnWidth. The Loading/Empty
            // placeholder text is a full sentence in some languages ("Сейчас на сервере никого
            // нет") that genuinely needs to wrap instead of just getting clipped.
            public TextWrapping NameWrapping => Kind == PlayerRowKind.Player ? TextWrapping.NoWrap : TextWrapping.Wrap;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>Shown once, in the constructor, before any server has actually been
        /// checked — PopulatePlayersList(0) used to run there instead, which claimed "Vikings:
        /// 0 — no vikings online" as if that were a real answer rather than just not having
        /// asked yet. Heading stays blank rather than guessing at a count too.</summary>
        private void ShowPlayersLoadingState()
        {
            PlayersHeading.Text = string.Empty;
            // Hidden until PopulatePlayersList shows it back — otherwise it sat to the left of
            // the still-empty heading (nothing else in the StackPanel to push it over yet) and
            // visibly jumped into place the moment real text appeared.
            RefreshPlayersButton.IsVisible = false;
            PlayersList.ItemsSource = new List<PlayerListItem>
            {
                new() { Name = Loc.T("gui.checkingPlayers"), Kind = PlayerRowKind.Loading }
            };
        }

        /// <summary>Re-localizes the players panel on a language switch, without
        /// PopulatePlayersList's own side effect of re-fetching every avatar. Real player names
        /// aren't localized strings, so once players are actually present there's nothing left
        /// to refresh beyond the heading — only the "checking…"/"nobody online" placeholder rows
        /// (no avatars, so safe to just rebuild) need their text redone.</summary>
        private void RefreshPlayersLocalization()
        {
            if (_lastKnownPlayerCount is not int count)
            {
                ShowPlayersLoadingState();
                return;
            }

            PlayersHeading.Text = Loc.T("gui.vikingsCount", count);

            if (_currentPlayers.Count == 0)
            {
                PlayersList.ItemsSource = new List<PlayerListItem>
                {
                    new() { Name = Loc.T("gui.noPlayersOnline"), Kind = PlayerRowKind.Empty }
                };
            }
        }

        private void PopulatePlayersList(int playerCount)
        {
            _lastKnownPlayerCount = playerCount;
            PlayersHeading.Text = Loc.T("gui.vikingsCount", playerCount);
            RefreshPlayersButton.IsVisible = true;

            var items = new List<PlayerListItem>();

            if (_currentPlayers.Count == 0)
            {
                items.Add(new PlayerListItem { Name = Loc.T("gui.noPlayersOnline"), Kind = PlayerRowKind.Empty });
            }
            else
            {
                foreach (PlayerInfo player in _currentPlayers)
                {
                    var item = new PlayerListItem { Name = player.Name ?? string.Empty };
                    items.Add(item);

                    // Rows appear immediately with the name (and a loading skeleton in the
                    // avatar slot); avatars fill in as each one loads — no reason to hold up
                    // the list on a batch of Steam CDN requests.
                    string? avatarUrl = player.AvatarUrl;
                    if (!string.IsNullOrEmpty(avatarUrl))
                        _ = LoadAvatarIntoItemAsync(avatarUrl, item);
                }
            }

            PlayersList.ItemsSource = items;
        }

        private async Task LoadAvatarIntoItemAsync(string url, PlayerListItem item)
        {
            try
            {
                if (_avatarCache.TryGetValue(url, out Bitmap? cached))
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

        public void OnUpdateComplete(bool startAfter, bool canStartGame)
        {
            IsLoading = false;
            RefreshFullCheckVisibility();

            if (!startAfter || !canStartGame)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    // A cancelled run still ends up here (canStartGame is always false for one)
                    // — CancellationPending tells the two apart from an ordinary failed/check-only
                    // completion, which still just hides the panel as before.
                    if (_worker.CancellationPending) ShowInterruptedProgress();
                    else HideProgress();
                    StartButtonGrid.Opacity = 1;
                });
                return;
            }

            // Start the game first, close the launcher second — so a launch failure is still shown.
            try
            {
                if (_readyInjectorPlan is not null)
                {
                    InjectorLauncher.Launch(_readyInjectorPlan);
                    Log($"Injector mode: launched from '{_readyInjectorPlan.WorkingDirectory}', closing the launcher");
                }
                else
                {
                    string gamePath = InjectorLauncher.ResolveGameExecutable(ClientFolder);
                    if (gamePath is null)
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            _ = MessageBoxWindow.ShowAsync(this,
                                Loc.T("gui.valheimExeNotFound", ClientFolder), Loc.T("gui.title.launchError"));
                            HideProgress();
                            StartButtonGrid.Opacity = 1;
                        });
                        Log($"Launch error: no runnable game executable in {ClientFolder}");
                        return;
                    }

                    Process.Start(new ProcessStartInfo { FileName = gamePath, UseShellExecute = true });
                    Log($"Launched {gamePath}, closing the launcher");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    _ = MessageBoxWindow.ShowAsync(this, ex.Message, Loc.T("gui.title.launchError"));
                    HideProgress();
                    StartButtonGrid.Opacity = 1;
                });
                Log($"Launch failed: {ex}");
                return;
            }

            Dispatcher.UIThread.Invoke(() =>
            {
                HideProgress();
                CloseButton_Click(null, null);
            });
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
