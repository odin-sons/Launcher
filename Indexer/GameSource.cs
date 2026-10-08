using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Odinsons.ValheimLauncher;

namespace Indexer
{
    /// <summary>One vanilla-game folder: <c>&lt;depot&gt;_&lt;manifest&gt;</c>, and the game manifest it produces.</summary>
    internal sealed record GameRoot(string Path, string Version, string Depot, string ManifestName);

    /// <summary>
    /// The folders holding the vanilla game, kept apart from the mod profile, one per Steam depot
    /// (Windows, macOS, Linux). A path passed with <c>--game-root</c> is remembered in
    /// <c>game_source.txt</c> inside the profile folder, replacing the remembered folder of the
    /// same depot, so later runs (a mod update, say) index the same games without repeating the
    /// flags. Each folder is named after the game version, <c>&lt;depot&gt;_&lt;manifest&gt;</c>.
    /// </summary>
    internal static class GameSource
    {
        public const string FileName = "game_source.txt";

        private const string CachePrefix = "hashes_game_";
        private const string CacheSuffix = ".cache";

        /// <summary>One cache per depot: the same relative path (valheim_Data/...) exists in several of them.</summary>
        public static string CacheFileNameFor(string depot) => CachePrefix + depot + CacheSuffix;

        public static bool IsOwnArtifact(string relativePath) =>
            string.Equals(relativePath, FileName, StringComparison.OrdinalIgnoreCase)
            || (relativePath.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase)
                && relativePath.EndsWith(CacheSuffix, StringComparison.OrdinalIgnoreCase)
                && relativePath.IndexOf('/') < 0);

        /// <summary>Written relative to the profile folder when possible, so the file survives a move of the whole tree.</summary>
        public static void Write(string profileFolder, IEnumerable<string> gameRoots)
        {
            var lines = gameRoots.Select(root =>
                Path.GetRelativePath(profileFolder, root).Replace(Path.DirectorySeparatorChar, '/'));

            File.WriteAllText(Path.Combine(profileFolder, FileName), string.Join("\n", lines) + "\n");
        }

        /// <returns>The absolute game folders, empty when nothing has been remembered.</returns>
        public static List<string> Read(string profileFolder)
        {
            string file = Path.Combine(profileFolder, FileName);
            if (!File.Exists(file)) return new List<string>();

            return RuleFile.Parse(File.ReadAllLines(file))
                .Select(stored => Path.GetFullPath(Path.Combine(profileFolder, stored)))
                .ToList();
        }

        /// <summary>The remembered folders with every one of a depot named in <paramref name="given"/> replaced by the given folder.</summary>
        public static List<string> Merge(IEnumerable<string> remembered, IEnumerable<string> given)
        {
            var givenList = given.ToList();
            var givenDepots = givenList.Select(DepotOrNull).Where(depot => depot is not null).ToHashSet();

            return remembered
                .Where(root => !givenDepots.Contains(DepotOrNull(root)))
                .Concat(givenList)
                .ToList();
        }

        private static string DepotOrNull(string gameRoot) =>
            TryGetVersion(gameRoot, out string version) ? GameLocation.DepotOf(version) : null;

        public static bool TryGetVersion(string gameRoot, out string version)
        {
            version = Path.GetFileName(Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return GameLocation.IsValidVersion(version);
        }

        /// <param name="error">Why the folder can't be indexed, when it can't.</param>
        public static bool TryDescribe(string gameRoot, out GameRoot root, out string error)
        {
            root = null;
            error = null;

            if (!Directory.Exists(gameRoot))
            {
                error = $"game folder not found: {gameRoot}";
                return false;
            }

            if (!TryGetVersion(gameRoot, out string version))
            {
                error = $"the game folder must be named <depot>_<manifest>, got '{version}' ({gameRoot})";
                return false;
            }

            string depot = GameLocation.DepotOf(version);
            string manifestName = GameLocation.ManifestNameFor(depot);

            if (manifestName is null)
            {
                error = $"depot {depot} is not one of the Valheim depots ({gameRoot})";
                return false;
            }

            root = new GameRoot(Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar), version, depot, manifestName);
            return true;
        }

        /// <summary>Where the launcher looks for this version: <c>Game/&lt;version&gt;</c> at the site root, one level above Launcher.</summary>
        public static string ExpectedLocation(string profileFolder, string version) =>
            Path.GetFullPath(Path.Combine(profileFolder, "..", "..", GameLocation.FolderName, version));

        public static bool IsAtExpectedLocation(string profileFolder, string gameRoot, string version) =>
            string.Equals(
                Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                ExpectedLocation(profileFolder, version).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
