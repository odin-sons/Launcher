using System;
using System.IO;
using System.Linq;
using Odinsons.ValheimLauncher;

namespace Indexer
{
    /// <summary>
    /// The folder holding the vanilla game, kept apart from the mod build. A path passed with
    /// <c>--game-root</c> is remembered in <c>game_source.txt</c> inside the build folder, so
    /// later runs (a mod update, say) index the same game without repeating the flag. The
    /// folder is named after the game version, <c>&lt;depot&gt;_&lt;manifest&gt;</c>.
    /// </summary>
    internal static class GameSource
    {
        public const string FileName = "game_source.txt";
        public const string CacheFileName = "hashes_game.cache";

        /// <summary>Written relative to the build folder when possible, so the file survives a move of the whole tree.</summary>
        public static void Write(string buildFolder, string gameRoot)
        {
            string stored = Path.GetRelativePath(buildFolder, gameRoot).Replace(Path.DirectorySeparatorChar, '/');
            File.WriteAllText(Path.Combine(buildFolder, FileName), stored + "\n");
        }

        /// <returns>The absolute game folder, or null when nothing has been remembered.</returns>
        public static string Read(string buildFolder)
        {
            string file = Path.Combine(buildFolder, FileName);
            if (!File.Exists(file)) return null;

            string stored = RuleFile.Parse(File.ReadAllLines(file)).FirstOrDefault();
            return stored is null ? null : Path.GetFullPath(Path.Combine(buildFolder, stored));
        }

        public static bool TryGetVersion(string gameRoot, out string version)
        {
            version = Path.GetFileName(Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return GameLocation.IsValidVersion(version);
        }

        /// <summary>Where the launcher looks for this version: <c>Game/&lt;version&gt;</c> next to the server folders.</summary>
        public static string ExpectedLocation(string buildFolder, string version) =>
            Path.GetFullPath(Path.Combine(buildFolder, "..", GameLocation.FolderName, version));

        public static bool IsAtExpectedLocation(string buildFolder, string gameRoot, string version) =>
            string.Equals(
                Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                ExpectedLocation(buildFolder, version).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
