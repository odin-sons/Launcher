using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// A local ledger of "what was already confirmed correct on this machine last time" —
    /// separate from the server manifest, which only describes what SHOULD be there, not what
    /// this particular player already verified.
    ///
    /// Exists for exactly one question: a file matched the manifest last run, but doesn't now,
    /// even though the server didn't change it (same expected hash). The player sees this as
    /// "the launcher keeps redownloading the same thing"; the cause is usually antivirus or write
    /// permissions, not the network — but without a ledger, this download is indistinguishable
    /// from an ordinary one where the server genuinely updated the file.
    ///
    /// The format reuses <see cref="Manifest"/>: the ledger is just a manifest of locally
    /// confirmed files, the same path → hash → size. Lives in the client folder, never
    /// published or downloaded.
    /// </summary>
    public sealed class ClientLedger
    {
        public const string FileName = "verified.info";

        private readonly Dictionary<string, string> _previouslyConfirmed;
        private readonly ConcurrentDictionary<string, Manifest.Entry> _confirmedThisRun = new(StringComparer.OrdinalIgnoreCase);

        private ClientLedger(Dictionary<string, string> previouslyConfirmed)
        {
            _previouslyConfirmed = previouslyConfirmed;
        }

        public static ClientLedger Load(string clientFolder)
        {
            string path = Path.Combine(clientFolder, FileName);
            var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(path))
                    foreach (Manifest.Entry entry in Manifest.ReadFile(path))
                        previous[entry.Path] = entry.Hash;
            }
            catch
            {
                // The ledger is a helper heuristic, not a source of truth. A corrupt or
                // foreign-format file simply means "nothing to compare against".
            }

            return new ClientLedger(previous);
        }

        /// <summary>What this file's hash was at the end of the PREVIOUS run, if it was checked at all.</summary>
        public bool TryGetLastConfirmedHash(string relativePath, out string hash) =>
            _previouslyConfirmed.TryGetValue(relativePath, out hash);

        /// <summary>
        /// The file currently matches the manifest — remember it for next time.
        /// Called from multiple threads at once, hence ConcurrentDictionary.
        /// </summary>
        public void RecordConfirmed(string relativePath, string hash, long size) =>
            _confirmedThisRun[relativePath] = new Manifest.Entry(relativePath, hash, size);

        /// <summary>
        /// Rewrites the ledger from scratch out of whatever was confirmed THIS run —
        /// without merging in the old content. A file that dropped out of the build or was
        /// deleted by the player naturally washes out of the ledger, no separate cleanup needed.
        /// </summary>
        public void Save(string clientFolder)
        {
            try
            {
                Manifest.WriteFile(Path.Combine(clientFolder, FileName), _confirmedThisRun.Values);
            }
            catch
            {
                // Couldn't save the ledger — we'll survive one run without the heuristic,
                // not a reason to stop or break the update itself.
            }
        }
    }
}
