using System;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Which mod/data bucket a to-be-downloaded file belongs to, for the download progress
    /// list. Display-only — unlike <see cref="ModGrouping"/> (which backs the optional-mod
    /// toggle and deliberately ignores <c>BepInEx/config</c>), this one groups config
    /// subfolders too, because on a real build those hold hundreds of MB (music, textures)
    /// and are the opposite of a fast tail.
    ///
    /// Rule: the folder right under <c>BepInEx/plugins/</c> or <c>BepInEx/config/</c>. A file
    /// sitting loose directly in either (a stray <c>.cfg</c>, a root file) has no such folder
    /// and lands in the catch-all bucket (<see cref="MiscKey"/>) — those are small and few.
    /// Names from <c>config/</c> won't always match the <c>plugins/</c> folder name for the
    /// same mod (GUID vs package name); acceptable here, the list only needs readable labels
    /// and honest byte counts, not a stable mod identity.
    /// </summary>
    public static class DownloadGrouping
    {
        /// <summary>Bucket for files with no plugin/config folder of their own.</summary>
        public const string MiscKey = "\0misc";

        public static string GroupKeyFor(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return MiscKey;

            string[] p = relativePath.Replace('\\', '/').Trim('/').Split('/');
            if (p.Length >= 4 &&
                p[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase) &&
                (p[1].Equals("plugins", StringComparison.OrdinalIgnoreCase) ||
                 p[1].Equals("config", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrEmpty(p[2]))
                return p[2];

            return MiscKey;
        }
    }
}
