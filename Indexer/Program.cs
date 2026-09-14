using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Indexer
{
    internal class Program
    {
        private static readonly HashSet<string> IgnoreRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Internal so Indexer.Tests can drive CheckInvariants directly without going through Main.</summary>
        internal static readonly HashSet<string> AdminOnlyMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Original-game paths (game_files.txt).
        ///
        /// Note: rules here are read DIFFERENTLY from ignore_patterns.txt.
        /// An entry ending in a slash is a path prefix from the build root ("valheim_Data/").
        /// An entry without a slash is an exact relative path, also from the root ("valheim.exe").
        /// This is deliberately stricter: in the exclusion list, a name without a slash matches
        /// a file anywhere in the tree, and that's dangerous for game files.
        /// </summary>
        private static readonly List<string> GameFileRules = new List<string>();

        /// <summary>
        /// What goes into optional.info (optional_patterns.txt). Rules are read the same
        /// way as in ignore_patterns.txt — with all the wildcard forms.
        /// </summary>
        private static readonly List<string> OptionalModRules = new List<string>();

        /// <summary>
        /// Where optional mods used to live before the list was moved to a file (back then
        /// this was called the greylist, in Azuanticheat's terms). Builds without
        /// optional_patterns.txt must behave exactly as before.
        /// </summary>
        private const string HistoricOptionalFolder = "Bepinex/config/Azuanticheat_greylist/";

        /// <summary>
        /// How many files are hashed at once. It's all disk-bound, not CPU-bound,
        /// so more than four threads adds nothing.
        /// </summary>
        private static readonly ParallelOptions HashOptions =
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };

        /// <summary>
        /// The game-file manifest is per-OS: <c>game.info</c> for a Windows build folder,
        /// <c>game_macos.info</c> for a macOS one, <c>game_linux.info</c> for Linux. The file
        /// layouts don't overlap, so the launcher fetches the one that matches the player's OS.
        /// Everything else (update.info, optional.info, …) is shared and keeps its name.
        /// Chosen with <c>--game-manifest &lt;name&gt;</c>; defaults to <c>game.info</c>.
        /// </summary>
        internal static string ParseGameManifestName(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "--game-manifest", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];

            return "game.info";
        }

        public static int Main(string[] args)
        {
            bool useCache = !args.Any(a => string.Equals(a, "--no-cache", StringComparison.OrdinalIgnoreCase));
            string gameManifestName = ParseGameManifestName(args);

            LoadRuleLists();

            // Read the previous manifests BEFORE WriteToFile overwrites them
            // below — only needed to compare "what changed since last time".
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousPlayer = TryReadManifest("update.info");
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousAdmin = TryReadManifest("update_admin.info");
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousOptional = TryReadManifest("optional.info");
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousGame = TryReadManifest(gameManifestName);

            string currentDir = Environment.CurrentDirectory;
            var allFiles = Directory.GetFiles(currentDir, "*.*", SearchOption.AllDirectories);

            // Game files go into a separate manifest and do NOT go into update.info:
            // the launcher takes them from the Steam install, and only pulls from the
            // server on a mismatch. The manifest format is versioned, so an old launcher
            // won't mistake the new file for its own and delete the game as "extra".
            var files = allFiles
                .Where(f => ShouldInclude(f, adminOnly: true) && !IsGameFile(f) && !IsOptionalMod(f))
                .OrderBy(f => f.Split(Path.DirectorySeparatorChar).Length)
                .ThenBy(f => f)
                .ToList();

            // update_admin.info — the admin build (only the general rules are excluded)
            var filesAdmin = allFiles
                .Where(f => ShouldInclude(f, adminOnly: false) && !IsGameFile(f) && !IsOptionalMod(f))
                .OrderBy(f => f.Split(Path.DirectorySeparatorChar).Length)
                .ThenBy(f => f)
                .ToList();

            // optional.info — optional mods. The launcher updates them if the player
            // already has them, doesn't install them if they don't, and never deletes them.
            //
            // They're subtracted from update.info and update_admin.info above, and that's
            // mandatory: in the launcher, the required-file list wins over the optional one —
            // (`if (allFiles.Contains(fileDir)) return;`), so a mod left behind in update.info
            // would keep being force-installed for everyone. This used to require remembering
            // to also duplicate the path in the exclusion list — two places that had to match.
            //
            // The general ignore_patterns.txt rules deliberately don't apply to the optional
            // mods themselves — that was already the case before.
            var optionalFiles = allFiles
                .Where(IsOptionalMod)
                .OrderBy(f => f.Split(Path.DirectorySeparatorChar).Length)
                .ThenBy(f => f)
                .ToList();

            // game.info — original-game files, split out of update.info.
            // The launcher folds them into the general list itself, first trying to take
            // them from the player's own Steam install.
            var gameFiles = allFiles
                .Where(f => ShouldInclude(f, adminOnly: true) && IsGameFile(f))
                .OrderBy(f => f.Split(Path.DirectorySeparatorChar).Length)
                .ThenBy(f => f)
                .ToList();

            // Hash ONCE for all manifests. Every WriteToFile call used to compute its own
            // checksums, so a file present in both update.info and update_admin.info got
            // read from disk twice — meaning the whole build was hashed twice over. The
            // manifests only differ in which lines they include; the checksums themselves
            // are shared.
            var hashes = ComputeHashes(new[] { files, filesAdmin, optionalFiles, gameFiles },
                                       currentDir, useCache);

            WriteToFile("update.info", files, hashes);
            WriteToFile("update_admin.info", filesAdmin, hashes);
            WriteToFile("optional.info", optionalFiles, hashes);
            WriteToFile(gameManifestName, gameFiles, hashes);

            long gameBytes = gameFiles.Sum(f => hashes[f].Size);

            Console.WriteLine();
            Console.WriteLine($"update.info:       {files.Count} files");
            Console.WriteLine($"update_admin.info: {filesAdmin.Count} files");
            Console.WriteLine($"optional.info:     {optionalFiles.Count} files");
            Console.WriteLine($"{gameManifestName,-17} {gameFiles.Count} files, {gameBytes / 1024.0 / 1024.0:0.0} MB");

            if (GameFileRules.Count == 0)
                Console.WriteLine("WARNING: no game-file rules — game.info is empty.");

            ReportAddedRemoved(previousPlayer, previousAdmin, previousOptional, previousGame,
                              files, filesAdmin, optionalFiles, gameFiles, hashes);
            ReportAdminPlayerTransitions(previousPlayer, previousAdmin, files, filesAdmin, hashes);

            var problems = new List<string>();
            CheckInvariants(files, filesAdmin, optionalFiles, gameFiles, hashes, problems);

            Console.WriteLine();

            int exitCode;
            if (problems.Count == 0)
            {
                Console.WriteLine("RESULT: OK");
                exitCode = 0;
            }
            else
            {
                foreach (string problem in problems)
                    Console.WriteLine($"PROBLEM: {problem}");

                Console.WriteLine($"RESULT: {problems.Count} problem(s) found");
                exitCode = 2;
            }

            WaitForKeyIfInteractive();
            return exitCode;
        }

        /// <summary>
        /// When launched by double-click, the tool draws in a new console window that closes
        /// along with the process — the RESULT line flashes and vanishes faster than it can be
        /// read. We hold the window open until a key is pressed, but only when input genuinely
        /// comes from a keyboard: with stdin redirected (a script, automation,
        /// `Indexer.exe > log.txt`), ReadLine() would otherwise hang forever, waiting for a
        /// keypress that will never come.
        /// </summary>
        private static void WaitForKeyIfInteractive()
        {
            if (Console.IsInputRedirected) return;

            Console.WriteLine();
            Console.Write("Press Enter to close this window...");
            Console.ReadLine();
        }

        /// <summary>
        /// Checks that must hold for any manifest regardless of how it was produced.
        /// Recomputed directly from the rules, not through the code that built the lists —
        /// otherwise a bug in the shared logic would go unnoticed by the same eyes that let it in.
        ///
        /// Invariant 5 guards against a real incident: admin_only_patterns.txt wasn't found,
        /// the list came up empty, and nine admin mods silently shipped to players — that was
        /// reported as a single WARNING line among thousands of lines of output; now a
        /// non-empty list that matches nothing is a failure with a non-zero exit code.
        /// An empty list by itself (invariant 4) isn't that same mistake — a build can
        /// genuinely have no admin-only mods — so it's a NOTE, not a failure.
        /// </summary>
        internal static void CheckInvariants(
            List<string> files, List<string> filesAdmin, List<string> optionalFiles, List<string> gameFiles,
            Dictionary<string, Odinsons.ValheimLauncher.Manifest.Entry> hashes, List<string> problems)
        {
            var playerPaths = new HashSet<string>(files.Select(f => hashes[f].Path), StringComparer.OrdinalIgnoreCase);
            var adminPaths = new HashSet<string>(filesAdmin.Select(f => hashes[f].Path), StringComparer.OrdinalIgnoreCase);
            var optionalPaths = new HashSet<string>(optionalFiles.Select(f => hashes[f].Path), StringComparer.OrdinalIgnoreCase);
            var gamePaths = new HashSet<string>(gameFiles.Select(f => hashes[f].Path), StringComparer.OrdinalIgnoreCase);

            // 1: no path in update.info should match a rule in admin_only_patterns.txt. This
            // doesn't catch an error in the rule itself (RuleMatches here is the same one),
            // but a wiring mistake — if someone later removes "!IsAdminOnly(f)" from the
            // filter for files, this check still catches it.
            foreach (string path in playerPaths)
            {
                string fileName = Path.GetFileName(path);
                string pathWithSlash = path + "/";

                foreach (string rule in AdminOnlyMods)
                {
                    if (RuleMatches(rule, path, fileName, pathWithSlash))
                    {
                        problems.Add($"admin-only path leaked to players: {path} (matches rule '{rule}')");
                        break;
                    }
                }
            }

            // 2: optional mods must not end up in either of the two main manifests —
            // otherwise the launcher would force-install them for everyone.
            foreach (string path in playerPaths.Intersect(optionalPaths, StringComparer.OrdinalIgnoreCase))
                problems.Add($"optional mod leaked into update.info: {path}");

            foreach (string path in adminPaths.Intersect(optionalPaths, StringComparer.OrdinalIgnoreCase))
                problems.Add($"optional mod leaked into update_admin.info: {path}");

            // 3: game files must not be duplicated in update.info.
            foreach (string path in playerPaths.Intersect(gamePaths, StringComparer.OrdinalIgnoreCase))
                problems.Add($"game file duplicated in update.info: {path}");

            // 4: the list is empty. Not a problem by itself — a build can genuinely have no
            // admin-only mods (a vanilla/game-file-only index, for instance) — just worth
            // saying out loud so it's never a silent assumption. The real hazard this used to
            // guard against was admin_only_patterns.txt going missing on a build that DOES
            // have admin mods (nine of them shipped to players once); invariant 5 below still
            // catches that shape of mistake — a non-empty list that matches nothing.
            if (AdminOnlyMods.Count == 0)
                Console.WriteLine("NOTE: no admin-only mods for this build — " +
                                   "update.info and update_admin.info are identical.");

            // 5: the list isn't empty, but no rule matched anything — both manifests come
            // out identical. Usually a typo in a path inside admin_only_patterns.txt,
            // not "there really are no admin mods right now".
            if (AdminOnlyMods.Count > 0 && files.Count == filesAdmin.Count)
                problems.Add("admin-only mod list is non-empty, but update.info and update_admin.info are " +
                             "identical (no rule matched any file — check the paths in admin_only_patterns.txt)");
        }

        private static List<Odinsons.ValheimLauncher.Manifest.Entry> TryReadManifest(string fileName)
        {
            string path = Path.Combine(Environment.CurrentDirectory, fileName);
            if (!File.Exists(path)) return new List<Odinsons.ValheimLauncher.Manifest.Entry>();

            try
            {
                return Odinsons.ValheimLauncher.Manifest.ReadFile(path);
            }
            catch
            {
                // The old binary format or a corrupt file — nothing to compare against,
                // not a reason to stop generating manifests.
                return new List<Odinsons.ValheimLauncher.Manifest.Entry>();
            }
        }

        /// <summary>
        /// Warns if a mod switched between the player build and the admin build since the
        /// last run. Both directions can be a deliberate decision, but they're easy to trigger
        /// by mistake — the wrong path moved while editing a list — and the cost of a mistake
        /// is high: either the tool disappears for every admin, or it ships to every player.
        /// Informational only — doesn't affect the exit code.
        /// </summary>
        private static void ReportAdminPlayerTransitions(
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousPlayer,
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousAdmin,
            List<string> files, List<string> filesAdmin,
            Dictionary<string, Odinsons.ValheimLauncher.Manifest.Entry> hashes)
        {
            if (previousPlayer.Count == 0 && previousAdmin.Count == 0) return; // first run — nothing to compare against

            var oldPlayer = new HashSet<string>(previousPlayer.Select(e => e.Path), StringComparer.OrdinalIgnoreCase);
            var oldAdminOnly = new HashSet<string>(
                previousAdmin.Select(e => e.Path).Except(oldPlayer, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            var newPlayer = new HashSet<string>(files.Select(f => hashes[f].Path), StringComparer.OrdinalIgnoreCase);
            var newAdminOnly = new HashSet<string>(
                filesAdmin.Select(f => hashes[f].Path).Except(newPlayer, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            List<string> toAdmin = oldPlayer.Intersect(newAdminOnly, StringComparer.OrdinalIgnoreCase).ToList();
            List<string> toPlayer = oldAdminOnly.Intersect(newPlayer, StringComparer.OrdinalIgnoreCase).ToList();

            if (toAdmin.Count == 0 && toPlayer.Count == 0) return;

            Console.WriteLine();
            Console.WriteLine("Player <-> admin tier changes since the last run:");

            foreach (string group in GroupByModFolder(toAdmin))
                Console.WriteLine($"  -> admin only : {group}");

            foreach (string group in GroupByModFolder(toPlayer))
                Console.WriteLine($"  -> all players: {group}");
        }

        /// <summary>
        /// An honest set diff, with no side decisions like "skip the first run" —
        /// that's up to the calling code, the same way it's handled for the
        /// player/admin transition report.
        /// </summary>
        internal static (List<string> Added, List<string> Removed) DiffPaths(
            HashSet<string> oldPaths, HashSet<string> newPaths)
        {
            var added = newPaths.Except(oldPaths, StringComparer.OrdinalIgnoreCase).ToList();
            var removed = oldPaths.Except(newPaths, StringComparer.OrdinalIgnoreCase).ToList();
            return (added, removed);
        }

        /// <summary>
        /// A general "what appeared and what disappeared" report across all four manifests
        /// at once — not about moving between the player and admin build (there's a separate
        /// report for that), but about a file's existence at all. The goal: three lines are
        /// enough to think "yes, that's what I did", instead of a 2500-line diff.
        /// </summary>
        private static void ReportAddedRemoved(
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousPlayer,
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousAdmin,
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousOptional,
            List<Odinsons.ValheimLauncher.Manifest.Entry> previousGame,
            List<string> files, List<string> filesAdmin, List<string> optionalFiles, List<string> gameFiles,
            Dictionary<string, Odinsons.ValheimLauncher.Manifest.Entry> hashes)
        {
            var oldUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in new[] { previousPlayer, previousAdmin, previousOptional, previousGame })
                foreach (var entry in list) oldUnion.Add(entry.Path);

            if (oldUnion.Count == 0) return; // first run — nothing to compare against

            var newUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in new[] { files, filesAdmin, optionalFiles, gameFiles })
                foreach (string f in list) newUnion.Add(hashes[f].Path);

            (List<string> added, List<string> removed) = DiffPaths(oldUnion, newUnion);

            if (added.Count == 0 && removed.Count == 0) return;

            Console.WriteLine();
            Console.WriteLine("Changes since the last run:");

            foreach (string group in GroupByModFolder(added))
                Console.WriteLine($"  + {group}");

            foreach (string group in GroupByModFolder(removed))
                Console.WriteLine($"  - {group}");
        }

        /// <summary>Collapses one mod's files into a single line, so N files aren't spelled out one per line.</summary>
        internal static IEnumerable<string> GroupByModFolder(List<string> paths)
        {
            return paths
                .GroupBy(p => ModFolderOf(p), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() == 1 ? g.First() : $"{g.Key} ({g.Count()} files)")
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Mods store data outside BepInEx/plugins too — PlanBuild and similar mods keep
        /// nested subfolders right inside BepInEx/config (see the test with 346 .blueprint
        /// files, which is what prompted this grouping in the first place). A file sitting
        /// DIRECTLY in one of these folders, with no subfolder of its own, isn't grouped with
        /// anything — it typically has no shared mod to begin with.
        /// </summary>
        private static readonly string[] GroupablePrefixes = { "BepInEx/plugins/", "BepInEx/config/" };

        internal static string ModFolderOf(string path)
        {
            foreach (string prefix in GroupablePrefixes)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                int slash = path.IndexOf('/', prefix.Length);
                return slash < 0 ? path : path.Substring(0, slash + 1);
            }

            return path;
        }

        /// <summary>
        /// Reads all four rule lists, each from its own file.
        /// </summary>
        private static void LoadRuleLists()
        {
            IgnoreRules.UnionWith(RuleList.Load(
                "Ignore rules",
                "NO LIST -> nothing will be excluded from the manifests",
                "ignore_patterns.txt"));

            AdminOnlyMods.UnionWith(RuleList.Load(
                "Admin-only mods",
                "NO LIST -> ADMIN MODS WILL BE PUBLISHED TO PLAYERS",
                "admin_only_patterns.txt"));

            foreach (string rule in RuleList.Load(
                         "Game files",
                         "no list -> game.info stays empty, game files remain in update.info",
                         "game_files.txt"))
            {
                string normalized = rule.Replace('\\', '/').TrimStart('/');

                if (normalized.Length > 0 && !GameFileRules.Contains(normalized))
                    GameFileRules.Add(normalized);
            }

            OptionalModRules.AddRange(RuleList.Load(
                "Optional mods",
                $"no list -> falling back to the historic folder {HistoricOptionalFolder}",
                "optional_patterns.txt"));

            if (OptionalModRules.Count == 0) OptionalModRules.Add(HistoricOptionalFolder);
        }

        /// <summary>Whether a file falls under the optional-mods list.</summary>
        private static bool IsOptionalMod(string fullPath)
        {
            string relPath = GetRelativePath(fullPath);
            string fileName = Path.GetFileName(relPath);
            string pathWithSlash = relPath + "/";

            foreach (string rule in OptionalModRules)
                if (RuleMatches(rule, relPath, fileName, pathWithSlash)) return true;

            return false;
        }

        /// <summary>Whether a file belongs to the original game.</summary>
        private static bool IsGameFile(string fullPath)
        {
            string relPath = GetRelativePath(fullPath);

            foreach (string rule in GameFileRules)
            {
                if (rule.EndsWith("/"))
                {
                    if (relPath.StartsWith(rule, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (string.Equals(relPath, rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Helper method — avoids duplicating the path-normalization code.
        /// </summary>
        private static string GetRelativePath(string fullPath)
        {
            return fullPath
                .Replace(Environment.CurrentDirectory, "")
                .Replace(Path.DirectorySeparatorChar, '/')
                .TrimStart('/');
        }

        private static bool ShouldInclude(string fullPath, bool adminOnly)
        {
            string relPath = GetRelativePath(fullPath);

            if (string.IsNullOrEmpty(relPath)) return false;

            string fileName = Path.GetFileName(relPath);
            string pathWithSlash = relPath + "/";

            // Check the general rules (ignore_patterns.txt) — always
            foreach (var rule in IgnoreRules)
                if (RuleMatches(rule, relPath, fileName, pathWithSlash)) return false;

            // Check admin_only_patterns.txt — only for the player build
            if (adminOnly)
                foreach (var rule in AdminOnlyMods)
                    if (RuleMatches(rule, relPath, fileName, pathWithSlash)) return false;

            return true;
        }

        /// <summary>
        /// One exclusion rule. Five forms:
        ///   "**/name/"      — a folder with this name at any depth;
        ///   "folder/"       — a path prefix from the build root;
        ///   "path/file.ext" — an exact path from the root (has a slash inside, no trailing one);
        ///   "name.ext"      — an exact filename anywhere in the tree (no slash at all);
        ///   with * or ?     — a mask: by path if it contains a slash, otherwise by filename.
        ///
        /// The exact-path form exists to exclude a file belonging to ONE specific mod —
        /// say, an admin tool's config — rather than any file with that name anywhere in the
        /// tree. Without this form, a rule containing a slash silently fell back to comparing
        /// by filename alone, and the path in the rule was effectively ignored — not what
        /// someone who wrote out a full path would expect.
        /// </summary>
        internal static bool RuleMatches(string rule, string relPath, string fileName, string pathWithSlash)
        {
            // "**/.git/" — matches a whole path segment, not a substring:
            // otherwise the rule ".git" would also catch a file named "mymod.gitignore".
            if (rule.StartsWith("**/", StringComparison.Ordinal))
            {
                string segment = rule.Substring(3).Trim('/');
                if (segment.Length == 0) return false;

                return ("/" + relPath).Contains("/" + segment + "/", StringComparison.OrdinalIgnoreCase);
            }

            if (rule.EndsWith("/"))
                return pathWithSlash.StartsWith(rule, StringComparison.OrdinalIgnoreCase);

            if (rule.IndexOf('*') >= 0 || rule.IndexOf('?') >= 0)
            {
                string subject = rule.IndexOf('/') >= 0 ? relPath : fileName;
                return FileSystemName.MatchesSimpleExpression(rule, subject, ignoreCase: true);
            }

            if (rule.IndexOf('/') >= 0)
                return string.Equals(relPath, rule, StringComparison.OrdinalIgnoreCase);

            return string.Equals(fileName, rule, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Computes checksums for every file appearing in at least one manifest —
        /// each file exactly once.
        /// </summary>
        private static Dictionary<string, Odinsons.ValheimLauncher.Manifest.Entry> ComputeHashes(
            IEnumerable<List<string>> manifestLists, string packFolder, bool useCache)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> list in manifestLists) union.UnionWith(list);

            HashCache cache = useCache ? HashCache.Load(packFolder) : null;
            var result = new ConcurrentDictionary<string, Odinsons.ValheimLauncher.Manifest.Entry>(
                StringComparer.OrdinalIgnoreCase);
            int computed = 0;

            var watch = System.Diagnostics.Stopwatch.StartNew();

            Parallel.ForEach(union, HashOptions, file =>
            {
                var info = new FileInfo(file);

                if (cache is null || !cache.TryGet(file, info, out string hash))
                {
                    hash = Odinsons.ValheimLauncher.FileHash.OfFile(file);
                    cache?.Store(file, info, hash);
                    Interlocked.Increment(ref computed);
                }

                result[file] = new Odinsons.ValheimLauncher.Manifest.Entry(
                    GetRelativePath(file), hash, info.Length);
            });

            watch.Stop();
            cache?.Save();

            Console.WriteLine();
            Console.WriteLine($"Hashed {union.Count} unique file(s) in {watch.Elapsed.TotalSeconds:0.0} s: " +
                              $"{computed} computed, {(cache?.Hits ?? 0)} reused from cache.");

            return new Dictionary<string, Odinsons.ValheimLauncher.Manifest.Entry>(
                result, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes the manifest in text format. Parsing and writing live in
        /// Odinsons.ValheimLauncher.Manifest — the one place that knows the format.
        ///
        /// There's no more per-file console output here: the manifest is text now,
        /// and its contents can be viewed directly in it.
        /// </summary>
        private static void WriteToFile(string fileName, List<string> files,
                                        Dictionary<string, Odinsons.ValheimLauncher.Manifest.Entry> hashes)
        {
            var entries = new List<Odinsons.ValheimLauncher.Manifest.Entry>(files.Count);

            foreach (string file in files) entries.Add(hashes[file]);

            Odinsons.ValheimLauncher.Manifest.WriteFile(fileName, entries);
        }
    }
}
