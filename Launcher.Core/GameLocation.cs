using System;
using System.Text.RegularExpressions;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Where the vanilla game files are served from. A game manifest only names the version
    /// ("# game: 892972_1234567890", depot + manifest id); the address is built here, next to
    /// the server directories: <c>&lt;launcher root&gt;/Game/&lt;version&gt;/</c>.
    /// </summary>
    public static class GameLocation
    {
        public const string FolderName = "Game";
        public const string VersionDirective = "game";

        private static readonly Regex VersionPattern = new(@"^\d+_\d+$", RegexOptions.Compiled);

        public static bool IsValidVersion(string version) =>
            version is not null && VersionPattern.IsMatch(version);

        /// <summary>
        /// The folder URL (ending in '/') for <paramref name="version"/>, or null if the version
        /// is not a plain "depot_manifest" pair. <paramref name="serverDirectory"/> is
        /// "&lt;launcher root&gt;/&lt;server&gt;/", so the launcher root is its parent.
        /// </summary>
        public static string UrlFor(string serverDirectory, string version)
        {
            if (!IsValidVersion(version)) return null;
            if (!Uri.TryCreate(serverDirectory, UriKind.Absolute, out Uri server)) return null;

            return new Uri(server, $"../{FolderName}/{version}/").AbsoluteUri;
        }
    }
}
