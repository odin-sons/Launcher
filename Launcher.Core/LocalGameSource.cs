using System;
using System.IO;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Attempts to take a game file from the player's disk instead of downloading it from the server.
    ///
    /// The decision is made per file, not "the whole game or nothing": between adjacent
    /// Valheim builds, only a small fraction of files change, so even with a version mismatch,
    /// most bytes are already sitting on the player's machine.
    ///
    /// Correctness is guaranteed by the hash, not by comparing manifest IDs: if the content
    /// doesn't match the manifest, the file simply isn't taken and falls through to a download.
    /// That's what makes this component safe even with a completely different game version.
    /// </summary>
    public static class LocalGameSource
    {
        /// <summary>Result of attempting to take a file locally.</summary>
        public enum Outcome
        {
            /// <summary>The file isn't in the Steam install.</summary>
            NotAvailable,

            /// <summary>The file exists, but its content is wrong — a different build is needed.</summary>
            HashMismatch,

            /// <summary>Copied, hash matched.</summary>
            Copied,

            /// <summary>The copy failed (permissions, file in use, disk space).</summary>
            Failed
        }

        /// <summary>
        /// Attempts to copy one file from the Steam install into the client folder.
        /// </summary>
        /// <param name="steamGameFolder">The game's folder from a Steam install.</param>
        /// <param name="relativePath">Path from the manifest, forward-slash separated.</param>
        /// <param name="expectedHash">Lowercase hash, as it appears in the manifest.</param>
        /// <param name="destination">Where to put it, full path.</param>
        public static Outcome TryCopy(string steamGameFolder, string relativePath,
                                      string expectedHash, string destination)
        {
            if (string.IsNullOrWhiteSpace(steamGameFolder) || string.IsNullOrWhiteSpace(relativePath))
                return Outcome.NotAvailable;

            string source = Path.Combine(steamGameFolder,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(source)) return Outcome.NotAvailable;

            string actualHash;
            try
            {
                actualHash = FileHash.OfFile(source);
            }
            catch
            {
                return Outcome.Failed;
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return Outcome.HashMismatch;

            try
            {
                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.Copy(source, destination, overwrite: true);

                // Re-verify once it's in place: the copy could have been interrupted, and
                // reporting "taken locally" for a corrupt file is worse than downloading it.
                return string.Equals(FileHash.OfFile(destination), expectedHash, StringComparison.OrdinalIgnoreCase)
                    ? Outcome.Copied
                    : Outcome.Failed;
            }
            catch
            {
                return Outcome.Failed;
            }
        }
    }
}
