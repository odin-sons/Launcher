using System;
using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Groups manifest file paths by the top-level plugin folder they belong to
    /// (BepInEx/plugins/&lt;folder&gt;/...). That folder name is the mod's identity
    /// everywhere in this project's own build/launcher/indexer pipeline — no separate
    /// mod-name registry to keep in sync with it.
    ///
    /// BepInEx/config is deliberately NOT a source here, even though a mod's config often
    /// lives in its own subfolder there too. Two problems, both confirmed against a real
    /// build's manifests: (1) plenty of mods drop a single loose .cfg file straight in
    /// BepInEx/config with no subfolder at all — grouping by "the third path segment"
    /// there means grouping by filename, producing one fake single-file "mod" per config;
    /// (2) even where a config subfolder exists, it's commonly named after the BepInEx
    /// plugin GUID ("shudnal.Seasons"), not the Thunderstore package name the plugins/
    /// folder uses ("shudnal-Seasons") — the exact same mod would show up twice under two
    /// unrelated-looking names, and there's no reliable way to tell they're the same mod
    /// without guessing. A handful of mods (e.g. PlanBuild) ship data only under
    /// BepInEx/config with no plugins/ folder at all and so won't appear here — acceptable,
    /// since the only place this grouping's accuracy actually matters is optional mods
    /// (for the toggle), and every optional mod on the live build has a plugins/ folder.
    /// </summary>
    public static class ModGrouping
    {
        public static Dictionary<string, List<string>> GroupByPluginFolder(IEnumerable<string> paths)
        {
            var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                string folder = TopLevelPluginFolder(path);
                if (folder is null) continue;

                if (!byFolder.TryGetValue(folder, out List<string> list))
                    byFolder[folder] = list = new List<string>();
                list.Add(path);
            }

            return byFolder;
        }

        /// <summary>The path to that folder's manifest.json, if the group has one.</summary>
        public static string FindManifestJsonPath(IReadOnlyList<string> filesInFolder)
        {
            foreach (string path in filesInFolder)
                if (path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                    return path;
            return null;
        }

        private static string TopLevelPluginFolder(string relativePath)
        {
            string[] parts = relativePath.Replace('\\', '/').Split('/');
            // Must have something INSIDE the folder (4+ segments) — a bare file sitting
            // directly in BepInEx/plugins/ (3 segments) is not a plugin folder.
            if (parts.Length < 4) return null;
            if (!string.Equals(parts[0], "BepInEx", StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(parts[1], "plugins", StringComparison.OrdinalIgnoreCase)) return null;
            return parts[2];
        }
    }
}
