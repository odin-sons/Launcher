using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Which optional mods this player has explicitly acted on through the mods panel, keyed by
    /// top-level plugin folder name (see ModGrouping) — the same identity FileDownloader already
    /// groups optional.info entries by, so no separate mod registry needs to agree with this one.
    /// Lives in the client folder as one folder name per line — plain text on purpose, same
    /// reasoning as every other list file in this project (ClientLedger, force_check etc.):
    /// easy to read, diff, and hand-edit if something ever needs fixing manually.
    ///
    /// Two lasting states per folder: selected (line present, no prefix) or explicitly
    /// deselected (line present, "-" prefix). A folder mentioned nowhere is only ever
    /// transient — FileDownloader treats "unknown" purely as "hasn't been looked at yet": the
    /// moment any of that mod's files are found on disk, it's adopted as selected right there,
    /// so a mod the player brought themselves before the panel existed doesn't sit forever in
    /// limbo (which would otherwise show as OFF in the panel despite actually being installed).
    /// A folder that's genuinely never been installed simply never gets an entry at all.
    /// </summary>
    public sealed class OptionalModSelection
    {
        public const string FileName = "optional_selected.txt";
        private const string DeselectedPrefix = "-";

        private readonly Dictionary<string, bool> _known;

        private OptionalModSelection(Dictionary<string, bool> known) => _known = known;

        /// <summary>Folders currently turned on — same as before, unaffected by the new off-state tracking.</summary>
        public IReadOnlyCollection<string> SelectedFolders =>
            _known.Where(entry => entry.Value).Select(entry => entry.Key).ToList();

        public bool IsSelected(string folderKey) => _known.TryGetValue(folderKey, out bool on) && on;

        /// <summary>Whether the player has ever acted on this mod through the panel — as opposed
        /// to one that simply sits in the folder because the player put it there themselves.</summary>
        public bool IsKnown(string folderKey) => _known.ContainsKey(folderKey);

        public void SetSelected(string folderKey, bool selected) => _known[folderKey] = selected;

        public static OptionalModSelection Load(string clientFolder)
        {
            string path = Path.Combine(clientFolder, FileName);
            var known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                        if (trimmed.StartsWith(DeselectedPrefix, StringComparison.Ordinal))
                            known[trimmed.Substring(DeselectedPrefix.Length)] = false;
                        else
                            known[trimmed] = true;
                    }
                }
            }
            catch
            {
                // A corrupt or unreadable file just means "nothing known yet" —
                // not a reason to stop the launcher.
            }

            return new OptionalModSelection(known);
        }

        public void Save(string clientFolder)
        {
            try
            {
                IEnumerable<string> lines = _known
                    .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => entry.Value ? entry.Key : DeselectedPrefix + entry.Key);

                File.WriteAllLines(Path.Combine(clientFolder, FileName), lines);
            }
            catch
            {
                // Best-effort — losing the selection once isn't worth failing the update over.
            }
        }
    }
}
