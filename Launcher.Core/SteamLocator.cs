using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Odinsons.ValheimLauncher
{
    /// <summary>What could be learned about the copy of the game installed via Steam.</summary>
    public sealed class SteamGameInfo
    {
        /// <summary>Folder shaped like …/steamapps/common/Valheim.</summary>
        public string InstallPath { get; init; }

        /// <summary>Path to the appmanifest_*.acf everything was read from.</summary>
        public string ManifestPath { get; init; }

        public string BuildId { get; init; }

        /// <summary>Depot content identifier — more precise than buildid.</summary>
        public string DepotManifestId { get; init; }

        public int StateFlags { get; init; }

        /// <summary>4 means "fully installed", with no download or update in progress.</summary>
        public bool FullyInstalled => StateFlags == 4;
    }

    /// <summary>
    /// Finds a Steam-installed game without touching the registry.
    ///
    /// The registry is deliberately not used: this way the code is identical on all three OSes,
    /// needs no platform-specific attributes, and adds no branches to the test matrix. The cost:
    /// players with a non-standard Steam client location won't be auto-detected; an explicit
    /// override path is provided for them (<paramref name="steamRootOverride"/>).
    ///
    /// Any failure here isn't an error: the caller simply moves on to the next tier —
    /// copying or downloading from the server.
    /// </summary>
    public static class SteamLocator
    {
        /// <summary>Where to look by default. This is data, not logic.</summary>
        public static IReadOnlyList<string> CandidateRoots()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (OperatingSystem.IsWindows())
            {
                return new[]
                {
                    Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { Length: > 0 } x86
                        ? Path.Combine(x86, "Steam") : null,
                    Environment.GetEnvironmentVariable("ProgramFiles") is { Length: > 0 } pf
                        ? Path.Combine(pf, "Steam") : null
                }.Where(p => p is not null).ToArray();
            }

            if (OperatingSystem.IsMacOS())
            {
                return new[] { Path.Combine(home, "Library", "Application Support", "Steam") };
            }

            // Linux: a regular install, a snap install, and Flatpak.
            return new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
            };
        }

        /// <summary>The Steam root — the folder that contains steamapps.</summary>
        public static string FindSteamRoot(string steamRootOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(steamRootOverride) &&
                Directory.Exists(Path.Combine(steamRootOverride, "steamapps")))
            {
                return steamRootOverride;
            }

            return CandidateRoots().FirstOrDefault(
                root => Directory.Exists(Path.Combine(root, "steamapps")));
        }

        /// <summary>
        /// All Steam libraries. The root itself comes first — it's a library too.
        /// A pure function of libraryfolders.vdf's contents.
        /// </summary>
        public static IReadOnlyList<string> ParseLibraryFolders(string vdfText, string steamRoot)
        {
            var result = new List<string> { steamRoot };
            if (string.IsNullOrEmpty(vdfText)) return result;

            foreach (Match m in Regex.Matches(vdfText, "\"path\"\\s+\"(.+?)\""))
            {
                // Backslashes are escaped in VDF.
                string path = m.Groups[1].Value.Replace("\\\\", "\\");
                if (path.Length > 0 && !result.Contains(path, StringComparer.OrdinalIgnoreCase))
                    result.Add(path);
            }

            return result;
        }

        /// <summary>Parses appmanifest_*.acf. A pure function of the text.</summary>
        public static SteamGameInfo ParseAppManifest(string acfText, int depotId, string libraryPath, string acfPath)
        {
            if (string.IsNullOrEmpty(acfText)) return null;

            string Field(string name)
            {
                Match m = Regex.Match(acfText, "\"" + name + "\"\\s+\"(.*?)\"", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : null;
            }

            string installDir = Field("installdir");
            if (string.IsNullOrEmpty(installDir)) return null;

            // A specific depot's manifest lives inside the InstalledDepots block.
            string depotManifest = null;
            Match depot = Regex.Match(
                acfText,
                "\"" + depotId + "\"\\s*\\{(.*?)\\}",
                RegexOptions.Singleline);
            if (depot.Success)
            {
                Match man = Regex.Match(depot.Groups[1].Value, "\"manifest\"\\s+\"(\\d+)\"");
                if (man.Success) depotManifest = man.Groups[1].Value;
            }

            int.TryParse(Field("StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int state);

            return new SteamGameInfo
            {
                InstallPath = Path.Combine(libraryPath, "steamapps", "common", installDir),
                ManifestPath = acfPath,
                BuildId = Field("buildid"),
                DepotManifestId = depotManifest,
                StateFlags = state
            };
        }

        /// <summary>
        /// The full pass: find Steam, walk the libraries, read the app manifest.
        /// </summary>
        /// <param name="reason">Filled in if the game couldn't be found.</param>
        public static bool TryFindGame(int appId, int depotId, string steamRootOverride,
                                       out SteamGameInfo info, out string reason)
        {
            info = null;
            reason = null;

            string steamRoot = FindSteamRoot(steamRootOverride);
            if (steamRoot is null)
            {
                reason = Loc.T("steam.notInstalled");
                return false;
            }

            IReadOnlyList<string> libraries;
            try
            {
                string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                libraries = ParseLibraryFolders(File.Exists(vdf) ? File.ReadAllText(vdf) : null, steamRoot);
            }
            catch (Exception ex)
            {
                reason = Loc.T("steam.librariesUnreadable", ex.Message);
                return false;
            }

            foreach (string library in libraries)
            {
                string acf = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(acf)) continue;

                try
                {
                    SteamGameInfo candidate = ParseAppManifest(File.ReadAllText(acf), depotId, library, acf);
                    if (candidate is null || !Directory.Exists(candidate.InstallPath)) continue;

                    info = candidate;
                    return true;
                }
                catch
                {
                    // Corrupt manifest — try the next library.
                }
            }

            reason = Loc.T("steam.gameNotFound");
            return false;
        }
    }
}
