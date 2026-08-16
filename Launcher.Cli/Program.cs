using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher.Cli
{
    internal static class Program
    {
        // Exit codes
        private const int ExitOk = 0;
        private const int ExitNotReady = 1;
        private const int ExitBadArgs = 2;
        private const int ExitNoMirror = 3;
        private const int ExitNoServer = 4;
        private const int ExitFailed = 5;
        private const int ExitUnsafeTarget = 6;
        private const int ExitNoWriteAccess = 7;
        private const int ExitBusy = 8;
        private const int ExitCancelled = 130;

        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10
        }) { Timeout = TimeSpan.FromSeconds(15) };

        private static async Task<int> Main(string[] args)
        {
            // The console is always in English, regardless of the system language: CLI
            // output is read by admins and scripts, and a single language matters more
            // here than localization. Strings come from the same dictionary as the GUI,
            // just pinned to en.
            Loc.Use("en");

            LauncherLog.FilePath = Path.Combine(Environment.CurrentDirectory, "launcher_log.txt");

            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                PrintUsage();
                return ExitBadArgs;
            }

            if (options.Help)
            {
                PrintUsage();
                return ExitOk;
            }

            if (options.CheckManifest is not null)
                return CheckManifest(options.CheckManifest);

            if (options.LogLevel is { } level) LauncherLog.Level = level;
            LauncherLog.SessionHeader("launcher-cli",
                typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?");

            try
            {
                return await RunAsync(options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Loc.T("cli.unexpectedError", ex.Message));
                return ExitFailed;
            }
        }

        private static async Task<int> RunAsync(Options options)
        {
            string mirror = options.Url ?? await FindMirrorAsync();
            if (mirror is null)
            {
                Console.Error.WriteLine(Loc.T("cli.noMirror"));

                // No mirror responded. Break it down step by step to see exactly where it
                // breaks: without this, a player's complaint can't distinguish a restricted
                // channel to our server from having no network at all.
                Console.Error.WriteLine("Running connectivity diagnostics, see launcher_log.txt");
                await ConnectivityDiagnostics.ProbeAllAsync(LauncherMirrors.Default);

                return ExitNoMirror;
            }

            if (!mirror.EndsWith("/")) mirror += "/";
            Console.WriteLine(Loc.T("cli.mirror", mirror));

            List<Server> servers;
            try
            {
                servers = await LoadServersAsync(mirror);
            }
            catch (Exception ex)
            {
                // Mostly reached with --url pointing at a broken address: mirror
                // auto-detection is skipped, and the first network error shows up here.
                LauncherLog.Error($"failed to load the server list from {mirror}", ex);
                Console.Error.WriteLine(Loc.T("cli.noMirror"));
                Console.Error.WriteLine($"  {ex.Message}");
                Console.Error.WriteLine("Running connectivity diagnostics, see launcher_log.txt");

                await ConnectivityDiagnostics.ProbeAllAsync(new[] { mirror });
                return ExitNoMirror;
            }

            if (servers is null || servers.Count == 0)
            {
                LauncherLog.Error($"server list from {mirror} is empty or unparsable");
                Console.Error.WriteLine(Loc.T("cli.noServers"));
                return ExitNoServer;
            }

            LauncherLog.Info($"server list: {servers.Count} entries [{string.Join(", ", servers.Select(s => s.Name))}]");

            if (options.List)
            {
                foreach (Server s in servers)
                    Console.WriteLine(s.Name + (s.Hidden ? "  " + Loc.T("cli.hidden") : string.Empty));
                return ExitOk;
            }

            string serverName = await ResolveServerNameAsync(options, servers);
            if (serverName is null) return ExitNoServer;

            string clientFolder = options.ClientFolder ?? Path.Combine("clients", serverName);

            // Permissions are checked first: without folder access, nothing else can be
            // determined anyway, and a permissions failure calls for very different advice.
            // This also catches it before the download rather than mid-way through a
            // multi-gigabyte transfer.
            if (!ClientFolderGuard.IsWritable(clientFolder, out string permReason, out string permAdvice))
            {
                Console.Error.WriteLine(Loc.T("cli.refused", Path.GetFullPath(clientFolder)));
                Console.Error.WriteLine($"  {permReason}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("  " + Loc.T("cli.howToFix"));
                Console.Error.WriteLine("  " + permAdvice);
                return ExitNoWriteAccess;
            }

            if (!ClientFolderGuard.IsSafeTarget(clientFolder, out string unsafeReason))
            {
                Console.Error.WriteLine(Loc.T("cli.refused", Path.GetFullPath(clientFolder)));
                Console.Error.WriteLine($"  {unsafeReason}");
                foreach (string line in Loc.T("cli.deletesWarning").Split('\n'))
                    Console.Error.WriteLine("  " + line);
                return ExitUnsafeTarget;
            }

            Directory.CreateDirectory(clientFolder);

            if (!UpdateSession.TryBegin(clientFolder, out UpdateSession session, out string sessionReason))
            {
                Console.Error.WriteLine(Loc.T("cli.refused", sessionReason));
                Console.Error.WriteLine("  " + Loc.T("cli.waitOrClose"));
                return ExitBusy;
            }

            using (session)
            {
                // The previous run didn't finish — check everything fully.
                bool full = options.Full || session.PreviousRunInterrupted;
                if (session.PreviousRunInterrupted && !options.Full)
                    Console.WriteLine(Loc.T("cli.interruptedFullCheck"));

                Console.WriteLine(Loc.T("cli.server", serverName));
                Console.WriteLine(Loc.T("cli.clientFolder", Path.GetFullPath(clientFolder)));
                Console.WriteLine(Loc.T(full ? "cli.modeFull" : "cli.modeNormal"));
                Console.WriteLine();

                // The Steam install is looked up here, not in the downloader: the
                // downloader knows nothing about Steam, it just gets a ready path or null.
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

                var ui = new ConsoleUpdateUi(clientFolder, !Console.IsOutputRedirected);
                var worker = new BackgroundWorker { WorkerSupportsCancellation = true };

                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;             // don't kill the process — let the downloader stop gracefully
                    worker.CancelAsync();
                    Console.Error.WriteLine("\n" + Loc.T("cli.cancelling"));
                };

                await FileDownloader.StartUpdateAsync(
                    worker,
                    ui,
                    full,
                    startGame: false,
                    selectedServerDirectory: mirror + serverName + "/",
                    ownExecutableName: Path.GetFileName(Environment.ProcessPath ?? string.Empty),
                    maxConcurrentDownloads: options.Parallel,
                    steamGameFolder: steamGameFolder,
                    session: session);

                if (worker.CancellationPending) return ExitCancelled;
                return ui.CanStartGame == true ? ExitOk : ExitNotReady;
            }
        }

        /// <summary>Walks the mirrors in the same order as the GUI and takes the first one that responds.</summary>
        private static async Task<string> FindMirrorAsync()
        {
            foreach (string url in LauncherMirrors.Default)
            {
                try
                {
                    using HttpResponseMessage response =
                        await Http.GetAsync(url + "servers.json", HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode) return url;
                }
                catch
                {
                    // mirror unreachable — try the next one
                }
            }

            return null;
        }

        private static async Task<List<Server>> LoadServersAsync(string mirror)
        {
            string json = await Http.GetStringAsync(mirror + "servers.json");
            ServerList list = JsonSerializer.Deserialize<ServerList>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (list?.Servers is null) return null;

            // The admin marker file next to the working directory reveals hidden servers — same as the GUI.
            bool isAdmin = File.Exists(Path.Combine(Environment.CurrentDirectory, "admin"));
            return isAdmin ? list.Servers : list.Servers.Where(s => !s.Hidden).ToList();
        }

        /// <summary>Priority: --server, then config.ini, then the only one available.</summary>
        private static async Task<string> ResolveServerNameAsync(Options options, List<Server> servers)
        {
            if (options.Server is not null)
            {
                Server match = servers.FirstOrDefault(
                    s => string.Equals(s.Name, options.Server, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match.Name;

                Console.Error.WriteLine(Loc.T("cli.serverNotFound", options.Server));
                foreach (Server s in servers) Console.Error.WriteLine("  " + s.Name);
                return null;
            }

            string configPath = Path.Combine(Environment.CurrentDirectory, "config.ini");
            if (File.Exists(configPath))
            {
                var config = new IniFile(configPath);
                await config.LoadAsync();
                string saved = config.Read("SelectedServer", "Settings");
                if (!string.IsNullOrWhiteSpace(saved) &&
                    servers.Any(s => string.Equals(s.Name, saved, StringComparison.OrdinalIgnoreCase)))
                {
                    return servers.First(s => string.Equals(s.Name, saved, StringComparison.OrdinalIgnoreCase)).Name;
                }
            }

            if (servers.Count == 1) return servers[0].Name;

            Console.Error.WriteLine(Loc.T("cli.serverNotSpecified"));
            foreach (Server s in servers) Console.Error.WriteLine("  " + s.Name);
            return null;
        }

        /// <summary>
        /// Validates a manifest and prints a summary.
        ///
        /// This command used to convert a binary file to text. With the text format there's
        /// nothing to convert — the manifest opens in any editor — so all that's left is a
        /// check: does the file parse at all, and what's in it. A parse error names the line number.
        /// </summary>
        private static int CheckManifest(string path)
        {
            try
            {
                List<Manifest.Entry> entries = Manifest.ReadFile(path);

                long total = 0;
                var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Manifest.Entry entry in entries)
                {
                    total += entry.Size;
                    if (!seen.Add(entry.Path)) duplicates.Add(entry.Path);
                }

                Console.WriteLine($"{Path.GetFullPath(path)}");
                Console.WriteLine($"  entries : {entries.Count}");
                Console.WriteLine($"  total   : {total:N0} bytes ({total / 1024.0 / 1024.0:0.0} MB)");
                Console.WriteLine($"  largest : {LargestOf(entries)}");

                if (duplicates.Count > 0)
                {
                    Console.Error.WriteLine($"  WARNING: {duplicates.Count} duplicate path(s):");
                    foreach (string duplicate in duplicates) Console.Error.WriteLine($"    {duplicate}");
                    return ExitFailed;
                }

                Console.WriteLine("  status  : OK");
                return ExitOk;
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"cannot read manifest {path}", ex);
                Console.Error.WriteLine($"Cannot read manifest {path}: {ex.Message}");
                return ExitFailed;
            }
        }

        private static string LargestOf(List<Manifest.Entry> entries)
        {
            if (entries.Count == 0) return "(none)";

            Manifest.Entry biggest = entries[0];
            foreach (Manifest.Entry entry in entries)
                if (entry.Size > biggest.Size) biggest = entry;

            return $"{biggest.Size / 1024.0 / 1024.0:0.0} MB  {biggest.Path}";
        }

        private static void PrintUsage()
        {
            Console.WriteLine("""
                launcher-cli — update a modpack client without the graphical launcher.

                Usage:
                  launcher-cli [options] [client-folder]

                  The folder may be omitted; clients/<server> is used then,
                  the same way the graphical launcher does it.

                Options:
                  --server <name>       Server name from servers.info.
                                        Default: the value from config.ini, or the only one available.
                  --url <address>       Use a specific mirror instead of auto-detection.
                  --full                Full check (verify everything, ignoring exclusions).
                  --parallel <N>        Concurrent downloads, 8 by default.
                  --list                Print the server list and exit.
                  --check-manifest <f>  Validate a .info manifest, print a summary and exit.
                                        Manifests are plain text — open them in any editor.
                  --log-level <lvl>     error | warn | info | debug | trace. Default: info.
                                        Use trace when investigating a specific complaint.
                  -h, --help            This help.

                Warning: the update removes everything from the client folder that is not in
                the server manifest. A non-empty folder without signs of a Valheim client is
                therefore always rejected, and this cannot be overridden. Specify an empty
                folder or the directory of an already installed client.

                Exit codes:
                  0   the client is up to date
                  1   update finished, but the client is not ready
                  2   bad arguments
                  3   no mirror responded
                  4   server not resolved
                  5   unexpected error
                  6   the client folder failed validation
                  7   no write access to the client folder
                  8   the folder is already being updated by another launcher
                  130 cancelled by the user
                """);
        }

        private sealed class Options
        {
            public string Server { get; private init; }
            public string ClientFolder { get; private init; }
            public string Url { get; private init; }
            public bool Full { get; private init; }
            public bool List { get; private init; }
            public bool Help { get; private init; }
            public string CheckManifest { get; private init; }
            public LogLevel? LogLevel { get; private init; }
            public int Parallel { get; private init; } = 8;

            public static Options Parse(string[] args)
            {
                string server = null, clientFolder = null, url = null, checkManifest = null;
                bool full = false, list = false, help = false;
                int parallel = 8;
                LogLevel? logLevel = null;

                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--server":
                            server = Next(args, ref i, "--server");
                            break;
                        case "--url":
                            url = Next(args, ref i, "--url");
                            break;
                        case "--parallel":
                            string raw = Next(args, ref i, "--parallel");
                            if (!int.TryParse(raw, out parallel) || parallel < 1 || parallel > 16)
                                throw new ArgumentException(Loc.T("cli.parallelRange"));
                            break;
                        case "--full":
                            full = true;
                            break;
                        case "--list":
                            list = true;
                            break;
                        case "--check-manifest":
                            checkManifest = Next(args, ref i, "--check-manifest");
                            break;
                        case "--log-level":
                            string levelText = Next(args, ref i, "--log-level");
                            if (!LauncherLog.TryParseLevel(levelText, out LogLevel parsed))
                                throw new ArgumentException(
                                    $"--log-level expects one of: error warn info debug trace (got '{levelText}')");
                            logLevel = parsed;
                            break;
                        case "-h":
                        case "--help":
                            help = true;
                            break;
                        default:
                            // Anything starting with a dash is assumed to be a typo in an
                            // option name, not a path. Otherwise a typo would silently become
                            // the client folder, and the downloader deletes "extra" files from it.
                            if (args[i].StartsWith('-'))
                                throw new ArgumentException(Loc.T("cli.unknownOption", args[i]));

                            if (clientFolder is not null)
                                throw new ArgumentException(
                                    Loc.T("cli.folderTwice", clientFolder, args[i]));

                            clientFolder = args[i];
                            break;
                    }
                }

                return new Options
                {
                    Server = server,
                    ClientFolder = clientFolder,
                    Url = url,
                    Full = full,
                    List = list,
                    Help = help,
                    CheckManifest = checkManifest,
                    LogLevel = logLevel,
                    Parallel = parallel
                };
            }

            private static string Next(string[] args, ref int i, string name)
            {
                if (i + 1 >= args.Length) throw new ArgumentException(Loc.T("cli.optionNeedsValue", name));
                return args[++i];
            }
        }
    }
}
