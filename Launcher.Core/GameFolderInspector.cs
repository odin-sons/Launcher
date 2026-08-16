using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Odinsons.ValheimLauncher
{
    /// <summary>What was found in the Steam game folder and whether it blocks injector mode.</summary>
    public sealed class GameFolderInspection
    {
        /// <summary>The folder is clean — the game can be referenced in place, without copying it.</summary>
        public bool CanUseInjector => Blocking.Count == 0;

        /// <summary>What must be moved to a backup. Currently just the BepInEx folder.</summary>
        public IReadOnlyList<string> Blocking { get; init; } = Array.Empty<string>();

        /// <summary>Other mod traces nearby. Left untouched, shown for information only.</summary>
        public IReadOnlyList<string> Informational { get; init; } = Array.Empty<string>();

        /// <summary>An existing doorstop_config.ini — we'll change it, keeping the original.</summary>
        public string ExistingDoorstopConfig { get; init; }

        /// <summary>An existing winhttp.dll — replaced only if it differs from ours.</summary>
        public string ExistingWinhttp { get; init; }
    }

    /// <summary>
    /// Inspects the game folder before enabling injector mode and moves anything blocking to a backup.
    ///
    /// Only a folder literally named BepInEx can block: it's exactly what the relative
    /// target_assembly in doorstop_config.ini points to. Variants like "BepInEx old" or
    /// "BepInEx - Copy" aren't referenced by anything and stay untouched — we only reach
    /// into someone else's files as much as is actually necessary.
    ///
    /// Nothing is deleted: everything moves into a single backup folder with names preserved,
    /// so the player can restore the previous state by hand.
    /// </summary>
    public static class GameFolderInspector
    {
        /// <summary>The only folder we create inside the game folder.</summary>
        public const string BackupFolderName = "OdinsonsLauncher-Backup";

        private const string BlockingFolder = "BepInEx";

        /// <summary>Name of the explanatory file — in the system language, so the player reads it.</summary>
        private static string ReadmeName => Loc.T("backup.readmeFileName");

        public static GameFolderInspection Inspect(string gameFolder, string ourWinhttpPath = null)
        {
            var blocking = new List<string>();
            var informational = new List<string>();
            string doorstopConfig = null;
            string winhttp = null;

            if (!Directory.Exists(gameFolder))
                return new GameFolderInspection();

            foreach (DirectoryInfo dir in new DirectoryInfo(gameFolder).GetDirectories())
            {
                if (string.Equals(dir.Name, BackupFolderName, StringComparison.OrdinalIgnoreCase))
                    continue; // our own backup

                if (string.Equals(dir.Name, BlockingFolder, StringComparison.OrdinalIgnoreCase))
                    blocking.Add(dir.FullName);
                else if (dir.Name.StartsWith(BlockingFolder, StringComparison.OrdinalIgnoreCase))
                    informational.Add(dir.FullName);
            }

            string ini = Path.Combine(gameFolder, "doorstop_config.ini");
            if (File.Exists(ini)) doorstopConfig = ini;

            string dll = Path.Combine(gameFolder, "winhttp.dll");
            if (File.Exists(dll) && DiffersFromOurs(dll, ourWinhttpPath)) winhttp = dll;

            return new GameFolderInspection
            {
                Blocking = blocking,
                Informational = informational,
                ExistingDoorstopConfig = doorstopConfig,
                ExistingWinhttp = winhttp
            };
        }

        private static bool DiffersFromOurs(string existing, string ours)
        {
            if (string.IsNullOrEmpty(ours) || !File.Exists(ours)) return true; // nothing to compare against — treat as foreign
            try
            {
                var a = new FileInfo(existing);
                var b = new FileInfo(ours);
                if (a.Length != b.Length) return true;
                return !File.ReadAllBytes(existing).AsSpan().SequenceEqual(File.ReadAllBytes(ours));
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Moves the given files and folders into a backup inside the game folder.
        /// A previously made backup isn't overwritten — a name collision gets a suffix appended.
        /// </summary>
        public static bool MoveToBackup(string gameFolder, IEnumerable<string> items,
                                        out string backupFolder, out string reason)
        {
            backupFolder = Path.Combine(gameFolder, BackupFolderName);
            reason = null;

            var moved = new List<(string From, string To)>();
            try
            {
                Directory.CreateDirectory(backupFolder);

                foreach (string item in items)
                {
                    if (string.IsNullOrWhiteSpace(item)) continue;

                    bool isDirectory = Directory.Exists(item);
                    if (!isDirectory && !File.Exists(item)) continue;

                    string target = UniqueTarget(backupFolder, Path.GetFileName(item.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

                    if (isDirectory) Directory.Move(item, target);
                    else File.Move(item, target);

                    moved.Add((item, target));
                }

                WriteReadme(backupFolder, moved);
                return true;
            }
            catch (Exception ex)
            {
                reason = Loc.T("backup.moveFailed", ex.Message);

                // Roll back what was already moved — the game folder isn't ours,
                // it can't be left in an intermediate state.
                foreach ((string from, string to) in moved)
                {
                    try
                    {
                        if (Directory.Exists(to)) Directory.Move(to, from);
                        else if (File.Exists(to)) File.Move(to, from);
                    }
                    catch { }
                }

                return false;
            }
        }

        private static string UniqueTarget(string backupFolder, string name)
        {
            string candidate = Path.Combine(backupFolder, name);
            int index = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
                candidate = Path.Combine(backupFolder, $"{name} ({index++})");

            return candidate;
        }

        private static void WriteReadme(string backupFolder, List<(string From, string To)> moved)
        {
            if (moved.Count == 0) return;

            var text = new StringBuilder();
            text.AppendLine(Loc.T("backup.readme.title"));
            text.AppendLine();
            text.AppendLine(Loc.T("backup.readme.why"));
            text.AppendLine();
            text.AppendLine(Loc.T("backup.readme.nothingDeleted"));
            text.AppendLine();

            foreach ((string from, string to) in moved)
            {
                text.AppendLine(Loc.T("backup.readme.from", to));
                text.AppendLine(Loc.T("backup.readme.to", from));
                text.AppendLine();
            }

            text.AppendLine(Loc.T("backup.readme.date",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));

            try
            {
                File.WriteAllText(Path.Combine(backupFolder, ReadmeName), text.ToString(), new UTF8Encoding(true));
            }
            catch
            {
                // The explanation is a nice-to-have, not a reason to consider the move failed.
            }
        }
    }
}
