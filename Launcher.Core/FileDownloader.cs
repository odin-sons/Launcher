using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    public static class FileDownloader
    {
        private static readonly string UpdateFileAdmin = "update_admin.info";
        private const string ForceCheckFileName = "force_check_files.txt";
        private static string CurrentUpdateFile = "update.info";
        private static long _totalBytesDownloaded;
        private static long _totalBytesToDownload;

        private static readonly HashSet<string> ExcludeFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "update.info", "update_admin.info", "Indexer.exe", "full.info", "version.info", "news.info",
            "OdinsonsLauncher.exe", "ValknutLauncher.exe", "LogOutput.log", "admin",
            "admin_only_patterns.txt", "force_check_files.txt", "game.info", "optional.info", ClientLedger.FileName
        };

        static FileDownloader()
        {
            // The lock file and the moved-aside exe are created by UpdateSession — they aren't "extra".
            foreach (string name in UpdateSession.ArtifactNames) ExcludeFiles.Add(name);
        }

        private static bool CanStartGame;
        private static string clientFolder;
        private static bool FullCheck;
        private static bool StartAfter;

        /// <summary>Current display event sink. Set in <see cref="StartUpdateAsync"/>.</summary>
        private static IUpdateUi _ui;

        /// <summary>
        /// Folders whose entries aren't checked during an ordinary run. Doesn't affect the extra-file
        /// cleanup — that pass is separate and only spares what's actually in the manifests.
        ///
        /// There used to also be BepInEx/plugins/_custommods here — a "folder for your own mods".
        /// It gave no real protection: a mod placed there was deleted as unrecognized exactly like
        /// one placed directly in plugins. Allowed mods are now listed in optional_patterns.txt and
        /// live alongside everything else.
        /// </summary>
        private static readonly HashSet<string> ExcludeRootOnly = new(StringComparer.OrdinalIgnoreCase) { "BepInEx/config" };
        private static readonly HashSet<string> ExcludeFullFolders = new(StringComparer.OrdinalIgnoreCase) { "BepInEx/DumpedAssemblies/valheim", "BepInEx/cache" };
        private static readonly HashSet<string> ForceCheckFiles = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient HttpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10,

            // Separate timeout for establishing the connection. An unreachable mirror is cut off
            // quickly, but a slow-but-alive channel isn't punished for it — before, both cases
            // were covered by one shared five-second timeout.
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            // Covers receiving the response headers. The body isn't included with
            // HttpCompletionOption.ResponseHeadersRead — otherwise large build files
            // (hundreds of megabytes) would never finish downloading.
            Timeout = TimeSpan.FromSeconds(60)
        };

        /// <summary>How long to wait for the next chunk of bytes before considering the connection dead.</summary>
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        /// <summary>How many times to try one file, counting the first attempt.</summary>
        private const int MaxAttempts = 4;

        private static SemaphoreSlim DownloadSemaphore = new SemaphoreSlim(1, 8);

        /// <summary>
        /// How many files are hash-checked at once.
        ///
        /// The cap specifically matters as an upper bound: the check used to go through
        /// Select(async ...) + WhenAll, meaning all several thousand files were read from disk at
        /// once. Survivable on an SSD; on an HDD the head thrashes between thousands of open files
        /// and the check takes longer than the download itself. It's all disk-bound, not CPU-bound
        /// (SHA-256 does about 2 GB/s per core), so four threads is already enough to saturate the drive.
        /// </summary>
        private static readonly ParallelOptions HashOptions =
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
        private static long _lastBytesDownloaded;
        private static DateTime _lastSpeedUpdate = DateTime.MinValue;
        private static double _currentSpeed;

        private static readonly string LogFilePath = Path.Combine(Environment.CurrentDirectory, "launcher_log.txt");

        /// <summary>
        /// Old logging entry point. Kept so all call sites don't need rewriting at once;
        /// everything flows into the shared LauncherLog at Info level. New code writes
        /// through LauncherLog directly and picks its own level.
        /// </summary>
        private static void Log(string message) => LauncherLog.Info(message);

        /// <summary>Paths from game.info — files belonging to the original game.</summary>
        private static readonly HashSet<string> GameFiles = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The full game.info entries. Kept separate from GameFiles because the two sets serve
        /// different purposes: the set of paths answers "should we try taking this file from the
        /// Steam install", while actually installing the file also needs its hash and size.
        /// </summary>
        private static readonly List<Manifest.Entry> GameEntries = new();

        /// <summary>
        /// The Steam install of the game, if found. Files from game.info are tried from there
        /// first and only downloaded if that fails. The caller does the lookup: the downloader
        /// itself shouldn't know anything about Steam beyond a ready-made path.
        /// </summary>
        private static string _steamGameFolder;

        /// <summary>
        /// The ready injector launch plan, if one was built this run — see
        /// <see cref="TryPrepareInjectorMode"/>. While it's non-null, game files are deliberately
        /// left out of the check list: the game launches straight from the Steam install instead
        /// of a copy in the client folder.
        /// </summary>
        private static InjectorPlan _injectorPlan;

        private static int _takenLocally;
        private static long _bytesTakenLocally;

        /// <param name="session">
        /// The update session, if the caller opened one. Needed to move the stashed
        /// valheim.exe back into place before the update reports completion — see the
        /// EndUpdate call below.
        /// </param>
        public static async Task StartUpdateAsync(BackgroundWorker worker, IUpdateUi ui, bool full, bool startGame, string selectedServerDirectory, string ownExecutableName, int maxConcurrentDownloads = 8, string steamGameFolder = null, UpdateSession session = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            // Put the log next to the working directory unless the caller set something else.
            LauncherLog.FilePath ??= LogFilePath;

            _ui.SetLoading(true);

            LauncherLog.Info($"update started: server dir '{selectedServerDirectory}', " +
                             $"full={full}, startGame={startGame}, parallel={maxConcurrentDownloads}");
            LauncherLog.Debug($"own executable name for exclusions: '{ownExecutableName}'");

            // Our own executable's name comes from outside: inside the library,
            // Assembly.GetExecutingAssembly() would return Launcher.Core, not the app.
            if (!string.IsNullOrEmpty(ownExecutableName))
                ExcludeFiles.Add(ownExecutableName);

            clientFolder = _ui.ClientFolder;
            CanStartGame = true;
            FullCheck = full;
            StartAfter = startGame;

            _steamGameFolder = Directory.Exists(steamGameFolder) ? steamGameFolder : null;
            _takenLocally = 0;
            _bytesTakenLocally = 0;
            _injectorPlan = null;
            GameFiles.Clear();
            GameEntries.Clear();

            if (_steamGameFolder is null)
                LauncherLog.Info(steamGameFolder is null
                    ? "steam install not provided: game files will come from the server"
                    : $"steam install '{steamGameFolder}' does not exist: game files will come from the server");
            else
                LauncherLog.Info($"steam install: {_steamGameFolder} (game files will be taken locally when hashes match)");

            DownloadSemaphore.Dispose();
            DownloadSemaphore = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);

            bool isAdmin = File.Exists(Path.Combine(clientFolder, "admin"));
            CurrentUpdateFile = isAdmin ? UpdateFileAdmin : "update.info";
            LauncherLog.Info($"client folder: {Path.GetFullPath(clientFolder)}");
            LauncherLog.Info($"admin marker {(isAdmin ? "found" : "absent")} -> using manifest '{CurrentUpdateFile}'");

            try
            {
                Log($"Downloading {CurrentUpdateFile} from {selectedServerDirectory}");
                await DownloadFileAsync(new Uri(selectedServerDirectory + CurrentUpdateFile), Path.Combine(clientFolder, CurrentUpdateFile));

                // === Downloading force_check.txt (optional) ===
                //
                // A list of files that are always checked, even during an ordinary run.
                // Needed because BepInEx/config is deliberately left untouched during an
                // ordinary run — that's where the player's own settings live. The configs
                // listed here are exempt from that rule: the server owns them, and they must match.
                string forceCheckFilePath = Path.Combine(clientFolder, ForceCheckFileName);
                try
                {
                    LauncherLog.Debug($"downloading {ForceCheckFileName} from {selectedServerDirectory}");
                    using var response = await HttpClient.GetAsync(new Uri(selectedServerDirectory + ForceCheckFileName), HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    string text = await response.Content.ReadAsStringAsync();

                    ForceCheckFiles.Clear();

                    foreach (string rule in RuleFile.Parse(text.Split('\n')))
                        ForceCheckFiles.Add(rule.Replace("\\", "/").Trim('/'));

                    LauncherLog.Info($"{ForceCheckFileName}: {ForceCheckFiles.Count} entry(ies)");
                }
                catch (Exception ex)
                {
                    // Not fatal: without the list, BepInEx/config just isn't checked
                    // during an ordinary run, which is the intended default anyway.
                    LauncherLog.Info($"{ForceCheckFileName} not available ({ex.Message}); " +
                                     "player-side config will not be force-checked");
                }
                finally
                {
                    SafeDeleteFile(forceCheckFilePath);
                }

                // === Downloading optional.info (optional) ===
                string optionalListFileName = "optional.info";
                string optionalListFullPath = Path.Combine(clientFolder, optionalListFileName);
                try
                {
                    Log($"Downloading {optionalListFileName} from {selectedServerDirectory}");
                    await DownloadFileAsync(new Uri(selectedServerDirectory + optionalListFileName), optionalListFullPath);
                    Log($"{optionalListFileName} downloaded");
                }
                catch (Exception ex)
                {
                    Log($"optional.info missing or failed to download: {ex.Message}. Required mods only.");
                    SafeDeleteFile(optionalListFullPath);
                }

                // === Downloading game.info (optional) ===
                //
                // A separate manifest of original-game files. It may not exist: a server
                // running the old HashCreator doesn't publish it, and game files just sit
                // inside update.info as before.
                string gameListPath = Path.Combine(clientFolder, "game.info");
                try
                {
                    LauncherLog.Debug($"downloading game.info from {selectedServerDirectory}");
                    await DownloadFileAsync(new Uri(selectedServerDirectory + "game.info"), gameListPath);

                    foreach (Manifest.Entry entry in Manifest.ReadFile(gameListPath))
                    {
                        GameFiles.Add(entry.Path);
                        GameEntries.Add(entry);
                    }

                    LauncherLog.Info($"game.info: {GameFiles.Count} game file(s) listed separately");
                }
                catch (Exception ex)
                {
                    LauncherLog.Info($"game.info not available ({ex.Message}); " +
                                     "game files, if any, are handled as ordinary entries");
                }
                finally
                {
                    SafeDeleteFile(gameListPath);
                }
            }
            catch (Exception ex)
            {
                SafeDeleteManifests();
                _ui.ShowMessage(Loc.T("dl.error.updateInfo", ex.Message), Loc.T("dl.title.error"), UpdateMessageKind.Warning);
                _ui.SetLoading(false);
                Log($"Failed to download update.info: {ex.Message}");
                return;
            }

            _ui.ShowProgress();
            _ui.SetStatus(Loc.T("dl.initializing"));

            await EnsureInjectorPrerequisitesAsync(selectedServerDirectory);
            // Hashing up to hundreds of Steam game files runs the same status/progress
            // reporting as the ordinary check below, so it needs to run off the UI thread —
            // otherwise the progress panel we just showed can't actually paint.
            await Task.Run(TryPrepareInjectorMode);
            _ui.SetInjectorPlan(_injectorPlan);

            bool logFileMissing = !File.Exists(Path.Combine(clientFolder, "BepInEx", "LogOutput.log"));
            FullCheck = full || logFileMissing;
            LauncherLog.Info($"check mode: {(FullCheck ? "FULL" : "normal")} " +
                             $"(requested full={full}, BepInEx/LogOutput.log missing={logFileMissing})");

            await Task.Run(() => WorkAsync(worker, new DoWorkEventArgs(null), selectedServerDirectory));

            // Strictly before OnComplete: that's what reports "ready", and the launcher
            // starts the game on that signal. If the stashed valheim.exe were moved back
            // later — when the lock is released — the launch would land on a moment when
            // the file doesn't exist under its own name yet.
            session?.EndUpdate();

            OnComplete();
        }

        /// <summary>Relative paths the injector plan itself depends on, regardless of game files.</summary>
        private static IEnumerable<string> InjectorPrerequisiteRelativePaths()
        {
            yield return "BepInEx/core/BepInEx.Preloader.dll";
            if (OperatingSystem.IsWindows()) yield return "winhttp.dll";
        }

        /// <summary>
        /// Downloads BepInEx.Preloader.dll (and winhttp.dll on Windows) right now if the client
        /// folder doesn't already have a current copy, instead of waiting for the ordinary mod-file
        /// pass later in this same run to get around to them.
        ///
        /// Without this, injector eligibility could only be decided from what a PREVIOUS run already
        /// downloaded — meaning a brand new client folder would always duplicate the game files once
        /// before injector mode could ever kick in. The whole point of the injector is to never
        /// duplicate them in the first place, so these two small files are fetched eagerly here.
        /// </summary>
        private static async Task EnsureInjectorPrerequisitesAsync(string selectedServerDirectory)
        {
            if (_steamGameFolder is null) return;

            List<Manifest.Entry> entries;
            try
            {
                entries = Manifest.ReadFile(Path.Combine(clientFolder, CurrentUpdateFile));
            }
            catch (Exception ex)
            {
                LauncherLog.Debug($"injector mode: could not pre-read {CurrentUpdateFile} to fetch prerequisites early: {ex.Message}");
                return;
            }

            var byPath = new Dictionary<string, Manifest.Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (Manifest.Entry entry in entries) byPath[entry.Path] = entry;

            foreach (string relativePath in InjectorPrerequisiteRelativePaths())
            {
                if (!byPath.TryGetValue(relativePath, out Manifest.Entry entry)) continue;

                string fullPath = Path.Combine(clientFolder, relativePath);
                if (string.Equals(CurrentHashOf(fullPath), entry.Hash, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);
                    await DownloadFileAsync(new Uri(selectedServerDirectory + relativePath), fullPath);
                    LauncherLog.Info($"injector mode: pre-fetched {relativePath} to decide eligibility this run");
                }
                catch (Exception ex)
                {
                    LauncherLog.Info($"injector mode: could not pre-fetch {relativePath} this run ({ex.Message}); " +
                                     "will re-check next run");
                }
            }
        }

        /// <summary>
        /// Verifies the Steam install's game files against the hashes the server actually expects.
        /// Valheim can auto-update in Steam independently of our server, so a matching valheim.exe
        /// PATH proves nothing about its CONTENT — <see cref="InjectorLauncher.BuildPlan"/> only checks
        /// the path exists, by design (see its class doc comment): the build-match check is the
        /// caller's job. Skipping this would mean silently launching the wrong game build with our mods.
        /// </summary>
        private static bool GameFilesMatchSteamInstall()
        {
            if (GameEntries.Count == 0) return true;

            int total = GameEntries.Count;
            int processed = 0;
            bool mismatchFound = false;

            Parallel.ForEach(GameEntries, HashOptions, (entry, state) =>
            {
                if (mismatchFound) { state.Stop(); return; }

                string fullPath = Path.Combine(_steamGameFolder, entry.Path);
                bool matches = File.Exists(fullPath) &&
                    string.Equals(FileHash.OfFile(fullPath), entry.Hash, StringComparison.OrdinalIgnoreCase);

                if (!matches)
                {
                    mismatchFound = true;
                    state.Stop();
                    return;
                }

                int currentProcessed = Interlocked.Increment(ref processed);
                if (currentProcessed % Math.Max(1, total / 100) == 0)
                {
                    SetStateLabel(Loc.T("dl.checkingFiles"), 0, 0);
                    SetTotalPercent((double)currentProcessed / total * 100, 0, 0);
                }
            });

            return !mismatchFound;
        }

        private static void TryPrepareInjectorMode()
        {
            if (_steamGameFolder is null || GameFiles.Count == 0) return;

            if (!GameFilesMatchSteamInstall())
            {
                LauncherLog.Info("injector mode not available this run: the Steam install's game files do not " +
                                 "match what the server currently expects; falling back to the classic download path");
                return;
            }

            if (InjectorLauncher.TryPrepareLaunch(_steamGameFolder, clientFolder, out InjectorPlan plan, out string reason))
            {
                _injectorPlan = plan;
                LauncherLog.Info($"injector mode ready: Valheim will run directly from '{_steamGameFolder}', " +
                                 $"{GameFiles.Count} game file(s) will not be duplicated into the client folder");
            }
            else
            {
                LauncherLog.Info($"injector mode not available this run ({reason}); " +
                                 "game files handled the classic way");
            }
        }

        private class FileToDownload
        {
            public string WebDir { get; init; }
            public string LocalDir { get; init; }
            public long FileSize { get; init; }
            public long LastBytesReceived { get; set; }
        }

        /// <summary>
        /// Checksum of the file the player currently has.
        ///
        /// Accounts for UpdateSession stashing valheim.exe as valheim.exe.updating during
        /// an update — a guard against launching the game mid-download. Without this
        /// adjustment, the check would see the executable as missing and redownload it on
        /// every launcher run, even when the player's copy is perfectly fine.
        ///
        /// No risk of clobbering a freshly downloaded file: UpdateSession.Dispose only moves
        /// the stashed copy back if the real file is absent.
        /// </summary>
        private static string CurrentHashOf(string fullPath)
        {
            if (File.Exists(fullPath)) return FileHash.OfFile(fullPath);

            string stashed = fullPath + UpdateSession.StashSuffix;
            return File.Exists(stashed) ? FileHash.OfFile(stashed) : null;
        }

        private static bool ShouldExcludeFile(string fileDir, bool fullCheck)
        {
            if (fullCheck) return false;

            string normalizedFileDir = fileDir.Trim('/').Replace("\\", "/");

            if (ForceCheckFiles.Contains(normalizedFileDir))
                return false;

            string fileParentDir = Path.GetDirectoryName(normalizedFileDir)?.Replace("\\", "/") ?? "";

            return ExcludeRootOnly.Contains(fileParentDir) || ExcludeFullFolders.Any(excluded => normalizedFileDir.StartsWith(excluded));
        }

        private static async Task WorkAsync(BackgroundWorker worker, DoWorkEventArgs e, string selectedServerDirectory)
        {
            var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);         // required + admin-only
            var optionalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);    // optional.info (protection from deletion only)
            var filesToDownload = new List<FileToDownload>();
            bool differencesFound = false;

            // A local ledger of "what was already confirmed correct on this machine" — separate
            // from the manifest, which only knows what SHOULD be there, not what this player already
            // verified last run. Needed to tell an ordinary download apart from a file that
            // consistently won't stick (antivirus, write permissions) — both look identical as
            // "downloading" without remembering the history.
            ClientLedger ledger = ClientLedger.Load(clientFolder);
            var suspiciousFiles = new ConcurrentBag<string>();

            try
            {
                // Read the main update.info
                var fileList = Manifest.ReadFile(Path.Combine(clientFolder, CurrentUpdateFile));
                SetStateLabel(Loc.T("dl.preparing"), 0, 0);

                _totalBytesDownloaded = 0;
                _totalBytesToDownload = 0;

                foreach (Manifest.Entry entry in fileList) allFiles.Add(entry.Path);

                // Game files arrive as a separate manifest, and they absolutely need to be
                // folded into the general list. Otherwise it goes doubly wrong: the game
                // never downloads, and then gets deleted too — the extra-file cleanup wipes
                // out everything absent from every manifest, which would mean valheim.exe
                // and the entire valheim_Data folder.
                //
                // Add() filters out duplicates: a server running the old HashCreator keeps
                // game files right inside update.info, and then game.info only marks them,
                // adding nothing new.
                int gameEntriesAdded = 0;

                if (_injectorPlan is null)
                {
                    foreach (Manifest.Entry entry in GameEntries)
                    {
                        if (!allFiles.Add(entry.Path)) continue;

                        fileList.Add(entry);
                        gameEntriesAdded++;
                    }

                    if (GameEntries.Count > 0)
                        LauncherLog.Info($"game.info merged: {gameEntriesAdded} entry(ies) added to the check list, " +
                                         $"{GameEntries.Count - gameEntriesAdded} already present in update.info");
                }
                else
                {
                    LauncherLog.Info($"injector mode: {GameEntries.Count} game file(s) excluded from the check " +
                                     "list — served directly from Steam, not duplicated into the client folder");
                }

                int totalFiles = fileList.Count;

                SafeDeleteManifests();

                // Read optional.info (if it exists)
                string optionalListPath = Path.Combine(clientFolder, "optional.info");
                var optionalFileList = new List<Manifest.Entry>();
                if (File.Exists(optionalListPath))
                {
                    try
                    {
                        optionalFileList = Manifest.ReadFile(optionalListPath);
                        foreach (Manifest.Entry entry in optionalFileList) optionalFiles.Add(entry.Path);
                        Log($"Loaded {optionalFiles.Count} entries from optional.info");
                    }
                    catch (Exception ex)
                    {
                        // Without optional.info, optional mods lose their protection from deletion —
                        // this can't pass silently, or the player won't understand where they went.
                        Log($"Failed to read optional.info: {ex.Message}");
                        _ui.ShowMessage(
                            Loc.T("dl.warn.optionalUnreadable", ex.Message),
                            Loc.T("dl.title.warning"), UpdateMessageKind.Warning);
                    }
                    finally
                    {
                        SafeDeleteFile(optionalListPath);
                    }
                }
                else
                {
                    Log("optional.info absent — extra files will be removed by the default rules.");
                }

                // Checking required files
                int processedFiles = 0;
                Parallel.ForEach(fileList, HashOptions, file =>
                {
                    if (worker.CancellationPending) { e.Cancel = true; return; }

                    string fileDir = file.Path;
                    string fileHash = file.Hash;
                    long fileSize = file.Size;

                    if (ShouldExcludeFile(fileDir, FullCheck)) return;

                    string fileDirFull = Path.Combine(clientFolder, fileDir);
                    Directory.CreateDirectory(Path.GetDirectoryName(fileDirFull) ?? string.Empty);

                    string currentHash = CurrentHashOf(fileDirFull);

                    if (currentHash != fileHash)
                    {
                        // The server didn't change this file (fileHash is the same one we
                        // already confirmed last run), yet it doesn't match now — suspicious:
                        // usually means antivirus or permissions, not a genuine update.
                        if (ledger.TryGetLastConfirmedHash(fileDir, out string lastGood) &&
                            string.Equals(lastGood, fileHash, StringComparison.OrdinalIgnoreCase))
                        {
                            suspiciousFiles.Add(fileDir);
                        }

                        bool satisfiedLocally = false;

                        // An original-game file can be taken from the player's own disk — if it's
                        // exactly the same version there. The hash decides, not a build comparison:
                        // for a different version, the copy simply won't match and the file falls
                        // through to a download.
                        if (_steamGameFolder is not null && GameFiles.Contains(fileDir))
                        {
                            LocalGameSource.Outcome outcome =
                                LocalGameSource.TryCopy(_steamGameFolder, fileDir, fileHash, fileDirFull);

                            if (outcome == LocalGameSource.Outcome.Copied)
                            {
                                satisfiedLocally = true;
                                Interlocked.Increment(ref _takenLocally);
                                Interlocked.Add(ref _bytesTakenLocally, fileSize);
                                LauncherLog.Trace($"taken from steam install: {fileDir}");
                            }
                            else
                            {
                                LauncherLog.Trace($"steam copy not usable for {fileDir}: {outcome}");
                                if (outcome == LocalGameSource.Outcome.Failed)
                                    LauncherLog.WarnOnce("steam-copy-failed",
                                        $"could not copy {fileDir} from the Steam install; downloading instead");
                            }
                        }

                        if (!satisfiedLocally)
                        {
                            differencesFound = true;
                            lock (filesToDownload)
                            {
                                filesToDownload.Add(new FileToDownload
                                {
                                    WebDir = fileDir,
                                    LocalDir = fileDirFull,
                                    FileSize = fileSize,
                                    LastBytesReceived = 0
                                });
                                Interlocked.Add(ref _totalBytesToDownload, fileSize);
                            }
                        }
                    }
                    else
                    {
                        // Matched — remember it for next run.
                        ledger.RecordConfirmed(fileDir, fileHash, fileSize);
                    }

                    int currentProcessed = Interlocked.Increment(ref processedFiles);
                    if (currentProcessed % Math.Max(1, totalFiles / 100) == 0)
                    {
                        SetStateLabel(Loc.T("dl.checkingFiles"), 0, 0);
                        SetTotalPercent((double)currentProcessed / totalFiles * 100, 0, 0);
                    }
                });

                ledger.Save(clientFolder);

                if (!suspiciousFiles.IsEmpty)
                {
                    List<string> distinct = suspiciousFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    LauncherLog.Warn($"{distinct.Count} file(s) needed re-downloading despite matching last run's " +
                                     $"verified state — possible antivirus quarantine or missing write permission: " +
                                     $"{string.Join(", ", distinct.Take(5))}" +
                                     (distinct.Count > 5 ? $" (+{distinct.Count - 5} more, see log)" : ""));

                    foreach (string path in distinct) LauncherLog.Debug($"  suspicious re-download: {path}");

                    _ui.SetDegradationWarning(Loc.T("dl.warn.suspiciousRedownload", distinct.Count));
                }
                else
                {
                    _ui.SetDegradationWarning(null);
                }

                // Checking optional mods (update only if the client already has the file)
                if (optionalFileList.Count > 0)
                {
                    Parallel.ForEach(optionalFileList, HashOptions, optionalFile =>
                    {
                        if (worker.CancellationPending) { e.Cancel = true; return; }

                        (string fileDir, string fileHash, long fileSize) = optionalFile;

                        if (allFiles.Contains(fileDir)) return; // already handled as required

                        string fileDirFull = Path.Combine(clientFolder, fileDir);
                        if (!File.Exists(fileDirFull)) return; // don't download missing optional files

                        string currentHash = FileHash.OfFile(fileDirFull);

                        if (currentHash != fileHash)
                        {
                            differencesFound = true;
                            lock (filesToDownload)
                            {
                                filesToDownload.Add(new FileToDownload
                                {
                                    WebDir = fileDir,
                                    LocalDir = fileDirFull,
                                    FileSize = fileSize,
                                    LastBytesReceived = 0
                                });
                                Interlocked.Add(ref _totalBytesToDownload, fileSize);
                            }
                        }
                    });
                }

                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    _ui.SetLoading(false);
                    return;
                }

                CanStartGame = !differencesFound;
                LauncherLog.Info($"check finished: {filesToDownload.Count} file(s) to download, " +
                                 $"{_totalBytesToDownload / 1024.0 / 1024.0:0.0} MB, differences={differencesFound}");

                if (_takenLocally > 0)
                    LauncherLog.Info($"taken from the Steam install: {_takenLocally} file(s), " +
                                     $"{_bytesTakenLocally / 1024.0 / 1024.0:0.0} MB not downloaded");
                else if (_steamGameFolder is not null && GameFiles.Count > 0)
                    LauncherLog.Info("steam install found, but no file matched — the player's game build differs");

                // Looking for extra files (optional.info protects optional mods from deletion)
                LauncherLog.Debug("scanning client folder for files absent from both manifests");
                var extraFiles = Directory.EnumerateFiles(clientFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        string relPath = f.Replace(clientFolder, "").Replace("\\", "/").Trim('/');
                        return !ExcludeFiles.Contains(Path.GetFileName(f)) &&
                               !allFiles.Contains(relPath) &&
                               !optionalFiles.Contains(relPath);
                    })
                    .ToList();

                if (differencesFound || extraFiles.Count > 0)
                {
                    if (extraFiles.Count > 0)
                    {
                        LauncherLog.Info($"extra files to remove: {extraFiles.Count}");
                        SetStateLabel(Loc.T("dl.deletingExtra"), 0, 0);
                        extraFiles.Sort((a, b) => b.Split('\\').Length.CompareTo(a.Split('\\').Length));

                        // Collect failures to show one summary message instead of
                        // a dialog per file.
                        var deleteFailures = new List<string>();

                        for (int i = 0; i < extraFiles.Count; ++i)
                        {
                            if (worker.CancellationPending)
                            {
                                e.Cancel = true;
                                _ui.SetLoading(false);
                                return;
                            }

                            string file = extraFiles[i];
                            string fileDir = file.Replace(clientFolder, "").Replace("\\", "/").Trim('/');

                            try
                            {
                                SetStateLabel(Loc.T("dl.deletingOne", fileDir), 0, 0);
                                LauncherLog.Trace($"deleting extra file: {fileDir}");
                                File.Delete(file);

                                string directory = Path.GetDirectoryName(file);
                                if (directory != null && !Directory.EnumerateFileSystemEntries(directory).Any())
                                    Directory.Delete(directory);
                            }
                            catch (Exception ex)
                            {
                                deleteFailures.Add(fileDir);
                                LauncherLog.WarnOnce("delete-failed", $"failed to delete {fileDir}: {ex.Message}", ex);
                            }

                            SetTotalPercent((double)i / extraFiles.Count * 100, 0, 0);
                        }

                        if (deleteFailures.Count > 0)
                        {
                            string sample = string.Join("\n", deleteFailures.Take(10));
                            if (deleteFailures.Count > 10)
                                sample += "\n" + Loc.T("dl.andMore", deleteFailures.Count - 10);

                            _ui.ShowMessage(
                                Loc.T("dl.warn.deleteFailed", deleteFailures.Count, sample),
                                Loc.T("dl.warn.deleteFailedTitle"), UpdateMessageKind.Warning);
                        }
                    }

                    SetTotalPercent(100, 0, 0);
                    if (CanStartGame) return;

                    _lastBytesDownloaded = 0;
                    _lastSpeedUpdate = DateTime.Now;

                    var downloadTasks = filesToDownload.Select((file, index) =>
                        DownloadFileAsyncWithProgress(file, index, filesToDownload.Count, worker, selectedServerDirectory)).ToArray();

                    SetStateLabel(Loc.T("dl.startingDownload"), 0, 0);
                    await Task.WhenAll(downloadTasks);

                    CanStartGame = true;
                }
            }
            catch (Exception ex)
            {
                _ui.ShowMessage(Loc.T("dl.error.processing", ex.Message), Loc.T("dl.title.error"), UpdateMessageKind.None);
                _ui.SetLoading(false);
                Log($"Error while processing files: {ex.Message}");
            }
        }

        private static async Task DownloadFileAsync(Uri uri, string destination)
        {
            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
            await stream.CopyToAsync(fileStream);
        }

        private static async Task DownloadFileAsyncWithProgress(FileToDownload file, int index, int totalFiles, BackgroundWorker worker, string selectedServerDirectory)
        {
            if (worker.CancellationPending) return;

            await DownloadSemaphore.WaitAsync();
            try
            {
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (worker.CancellationPending) return;

                    // A previous attempt may have already counted some bytes into the total
                    // progress. Roll it back explicitly, or the bar will overshoot by the
                    // amount that never finished downloading.
                    if (file.LastBytesReceived > 0)
                    {
                        LauncherLog.Debug($"retry rollback: {file.WebDir} discarding {file.LastBytesReceived} B counted earlier");
                        Interlocked.Add(ref _totalBytesDownloaded, -file.LastBytesReceived);
                        file.LastBytesReceived = 0;
                    }

                    (bool done, bool retryable, TimeSpan wait, string problem) =
                        await TryDownloadOnceAsync(file, worker, selectedServerDirectory, attempt);

                    if (done)
                    {
                        LauncherLog.Trace($"downloaded {file.WebDir} on attempt {attempt}/{MaxAttempts}");
                        return;
                    }

                    if (worker.CancellationPending) return;

                    if (!retryable || attempt == MaxAttempts)
                    {
                        LauncherLog.Error($"giving up on {file.WebDir} after {attempt} attempt(s): {problem}");
                        _ui.ShowMessage(Loc.T("dl.error.downloadFile", file.WebDir, problem),
                            Loc.T("dl.title.error"), UpdateMessageKind.Error);
                        _ui.SetLoading(false);
                        return;
                    }

                    LauncherLog.WarnOnce($"retry:{problem}",
                        $"attempt {attempt}/{MaxAttempts} for {file.WebDir} failed ({problem}); " +
                        $"retrying in {wait.TotalSeconds:0.0}s");
                    SetStateLabel(Loc.T("dl.retrying", file.WebDir, attempt + 1, MaxAttempts), 0, 0);

                    try { await Task.Delay(wait); } catch { return; }
                }
            }
            finally
            {
                DownloadSemaphore.Release();
            }
        }

        /// <summary>
        /// One attempt to download a file.
        /// </summary>
        /// <returns>
        /// done — the file is on disk; retryable — whether retrying makes sense;
        /// wait — how long to wait before the next attempt; problem — what to write to the log and the player.
        /// </returns>
        private static async Task<(bool done, bool retryable, TimeSpan wait, string problem)>
            TryDownloadOnceAsync(FileToDownload file, BackgroundWorker worker,
                                 string selectedServerDirectory, int attempt)
        {
            string url = selectedServerDirectory + file.WebDir;

            try
            {
                LauncherLog.Trace($"GET {url} (attempt {attempt}/{MaxAttempts})");
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    bool retryable = HttpRetry.IsRetryableStatus(response.StatusCode);
                    TimeSpan wait = retryable ? HttpRetry.Delay(attempt, response) : TimeSpan.Zero;
                    string problem = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

                    LauncherLog.Warn($"{problem} for {file.WebDir}" +
                                     (retryable ? string.Empty : " (not retryable)"));

                    return (false, retryable, wait, problem);
                }

                long totalBytes = response.Content.Headers.ContentLength ?? file.FileSize;
                LauncherLog.Trace($"200 OK {file.WebDir}, {totalBytes} B expected");

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(file.LocalDir, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);

                byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);

                // Stall guard. HttpClient.Timeout only covers the headers: if the connection
                // hangs mid-body, the read would wait forever. The countdown resets on every
                // successfully read chunk, so a slow but alive download doesn't trip the guard.
                using var stall = new CancellationTokenSource(StallTimeout);

                try
                {
                    long bytesReceived = 0;
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), stall.Token)) > 0)
                    {
                        stall.CancelAfter(StallTimeout);

                        if (worker.CancellationPending)
                        {
                            LauncherLog.Info($"cancelled while downloading {file.WebDir}");
                            _ui.SetLoading(false);
                            return (false, false, TimeSpan.Zero, "cancelled");
                        }

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        bytesReceived += bytesRead;

                        long delta = bytesReceived - file.LastBytesReceived;
                        file.LastBytesReceived = bytesReceived;
                        Interlocked.Add(ref _totalBytesDownloaded, delta);

                        UpdateSpeed();

                        SetFilePercent((double)bytesReceived / totalBytes * 100);
                        SetTotalPercent((double)_totalBytesDownloaded / _totalBytesToDownload * 100, _totalBytesDownloaded, _totalBytesToDownload);
                        SetStateLabel(Loc.T("dl.downloading"), _totalBytesDownloaded, _totalBytesToDownload);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                return (true, false, TimeSpan.Zero, null);
            }
            catch (OperationCanceledException) when (!worker.CancellationPending)
            {
                // The cancellation was requested by the stall guard, not the user.
                string problem = Loc.T("dl.error.stalled", file.WebDir, StallTimeout.TotalSeconds);
                LauncherLog.Warn($"stalled: {file.WebDir}, no data for {StallTimeout.TotalSeconds:0}s " +
                                 $"(attempt {attempt}/{MaxAttempts})");
                return (false, true, HttpRetry.Delay(attempt, null), problem);
            }
            catch (HttpRequestException ex)
            {
                LauncherLog.Warn($"network error on {file.WebDir} (attempt {attempt}/{MaxAttempts})", ex);
                return (false, true, HttpRetry.Delay(attempt, null), ex.Message);
            }
            catch (IOException ex)
            {
                // The connection dropped mid-body, or a disk-write problem.
                LauncherLog.Warn($"I/O error on {file.WebDir} (attempt {attempt}/{MaxAttempts})", ex);
                return (false, true, HttpRetry.Delay(attempt, null), ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                // A file-permission problem won't be fixed by retrying.
                LauncherLog.Error($"no write access for {file.LocalDir}", ex);
                return (false, false, TimeSpan.Zero, ex.Message);
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"unexpected error while downloading {file.WebDir}", ex);
                return (false, false, TimeSpan.Zero, ex.Message);
            }
        }

        private static void UpdateSpeed()
        {
            DateTime now = DateTime.Now;
            if ((now - _lastSpeedUpdate).TotalSeconds >= 0.5)
            {
                long bytesSinceLast = _totalBytesDownloaded - _lastBytesDownloaded;
                _currentSpeed = bytesSinceLast / (now - _lastSpeedUpdate).TotalSeconds;
                _lastBytesDownloaded = _totalBytesDownloaded;
                _lastSpeedUpdate = now;
            }
        }

        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                Thread.Sleep(500);
                try { File.Delete(path); } catch { }
            }
        }

        /// <summary>
        /// Removes both manifests from the client folder, not just the one this
        /// update ran against.
        ///
        /// Otherwise: an admin has the 'admin' marker, the update runs against
        /// update_admin.info, and that's the one we delete — while update.info from a
        /// previous run stays behind. It's protected from the "extra files" cleanup by
        /// the exclusion list, so it lives forever and misleads — you open it months
        /// later and see long-stale content.
        /// </summary>
        private static void SafeDeleteManifests()
        {
            SafeDeleteFile(Path.Combine(clientFolder, "update.info"));
            SafeDeleteFile(Path.Combine(clientFolder, UpdateFileAdmin));
        }

        private static void OnComplete()
        {
            LauncherLog.FlushRepeats();
            LauncherLog.Info($"update finished: canStartGame={CanStartGame}, startAfter={StartAfter}, " +
                             $"downloaded {_totalBytesDownloaded / 1024.0 / 1024.0:0.0} MB");
            _ui.OnUpdateComplete(StartAfter, CanStartGame);
        }

        private static void SetStateLabel(string msg, long current, long total)
        {
            _ui.SetStatus(current == 0 && total == 0
                ? msg
                : Loc.T("dl.speed", FormatSpeed(_currentSpeed)));
        }

        private static void SetTotalPercent(double percent, long downloaded, long total)
        {
            _ui.SetTotalProgress(
                percent,
                $"{Math.Round(percent)}%",
                $"{FormatBytes(downloaded)}/{FormatBytes(total)}");
        }

        private static void SetFilePercent(double percent) => _ui.SetFileProgress(percent);

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = Loc.T("dl.sizeUnits").Split('|');
            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            return $"{size:0.0} {suffixes[suffixIndex]}";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            string[] suffixes = Loc.T("dl.speedUnits").Split('|');
            int suffixIndex = 0;
            double speed = bytesPerSecond;
            while (speed >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                speed /= 1024;
                suffixIndex++;
            }
            return $"{speed:0.0} {suffixes[suffixIndex]}";
        }

    }
}