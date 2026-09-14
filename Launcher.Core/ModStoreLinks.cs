namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Builds Thunderstore/Hexium package page URLs straight from a plugin folder's own name —
    /// no extra manifest field needed, since every mod manager that produces these folders
    /// (Gale, r2modman, the Thunderstore Mod Manager) already names them "{namespace}-{name}",
    /// Thunderstore's own package-identity convention (see ModGrouping's doc comment). Thunderstore
    /// itself disallows hyphens inside a namespace or name specifically so this split is always
    /// unambiguous.
    /// </summary>
    public static class ModStoreLinks
    {
        public static bool TrySplit(string folderKey, out string ns, out string name)
        {
            ns = null;
            name = null;
            if (string.IsNullOrEmpty(folderKey)) return false;

            int dash = folderKey.IndexOf('-');
            if (dash <= 0 || dash >= folderKey.Length - 1) return false;

            ns = folderKey.Substring(0, dash);
            name = folderKey.Substring(dash + 1);
            return true;
        }

        public static string ThunderstoreUrl(string folderKey) =>
            TrySplit(folderKey, out string ns, out string name)
                ? $"https://thunderstore.io/c/valheim/p/{ns}/{name}/"
                : null;

        public static string HexiumUrl(string folderKey) =>
            TrySplit(folderKey, out string ns, out string name)
                ? $"https://valheim.hexium.gg/mods/{ns}/{name}"
                : null;
    }
}
