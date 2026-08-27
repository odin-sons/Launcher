using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Which optional mods this player has explicitly turned on, keyed by top-level plugin
    /// folder name (see ModGrouping) — the same identity FileDownloader already groups
    /// optional.info entries by, so no separate mod registry needs to agree with this one.
    /// Lives in the client folder as one folder name per line — plain text on purpose, same
    /// reasoning as every other list file in this project (ClientLedger, force_check etc.):
    /// easy to read, diff, and hand-edit if something ever needs fixing manually.
    ///
    /// Turning a mod off never deletes anything by itself — see FileDownloader's use of this
    /// class. Files already on disk for a mod that gets deselected are simply left alone.
    /// </summary>
    public sealed class OptionalModSelection
    {
        public const string FileName = "optional_selected.txt";

        private readonly HashSet<string> _selected;

        private OptionalModSelection(HashSet<string> selected) => _selected = selected;

        public IReadOnlyCollection<string> SelectedFolders => _selected;

        public bool IsSelected(string folderKey) => _selected.Contains(folderKey);

        public void SetSelected(string folderKey, bool selected)
        {
            if (selected) _selected.Add(folderKey);
            else _selected.Remove(folderKey);
        }

        public static OptionalModSelection Load(string clientFolder)
        {
            string path = Path.Combine(clientFolder, FileName);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                            selected.Add(trimmed);
                    }
                }
            }
            catch
            {
                // A corrupt or unreadable file just means "nothing selected yet" —
                // not a reason to stop the launcher.
            }

            return new OptionalModSelection(selected);
        }

        public void Save(string clientFolder)
        {
            try
            {
                File.WriteAllLines(Path.Combine(clientFolder, FileName), _selected.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // Best-effort — losing the selection once isn't worth failing the update over.
            }
        }
    }
}
