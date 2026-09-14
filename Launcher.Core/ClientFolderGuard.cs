using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Guards against running an update against the wrong path.
    /// An update deletes everything absent from the manifest from the target folder, so a mistaken
    /// path is expensive. We only proceed into a folder that's either empty, already holds a
    /// Valheim client, or has leftover traces of an interrupted update.
    /// </summary>
    public static class ClientFolderGuard
    {
        /// <summary>Signs that a folder actually holds a game client.</summary>
        private static readonly string[] ClientMarkers =
        {
            "valheim.exe",      // Windows
            "valheim.x86_64",   // Linux
            "valheim_Data",
            "BepInEx"
        };

        /// <summary>What the updater itself might have left behind if the previous run was interrupted.</summary>
        private static readonly HashSet<string> OwnArtifacts = new(StringComparer.OrdinalIgnoreCase)
        {
            "update.info",
            "update_admin.info",
            "optional.info",
            "force_check_files.txt",
            ClientLedger.FileName,
            "changelog_seen.txt", // written by older launcher versions; no longer created
            "launcher_log.txt",
            "config.ini",
            "admin",
            DefenderExclusion.PromptedMarkerName,
            // Cached locally by FetchServerChangelogAsync/FetchServerInfoAsync as soon as the
            // Server tab loads — before any install exists, so a truly fresh client folder
            // already has these two by the time the player gets to Install.
            "changelog.md",
            "info.md",
            // Written the moment a player toggles an optional mod on the Mods tab — reachable
            // before Install has ever run, so a folder with nothing but this one file is still
            // a fresh install, not foreign content.
            OptionalModSelection.FileName,
            // FileDownloader caches the per-OS game manifest under this exact local name on
            // every platform (game_macos.info/game_linux.info downloaded, but always saved
            // locally as "game.info" — see FileDownloader.StartUpdateAsync). Written partway
            // through an update, before any game file itself lands, so a run interrupted right
            // after this download left nothing else behind is still a fresh/resumable install.
            "game.info"
        };

        static ClientFolderGuard()
        {
            // Traces of an interrupted update session are ours too, not foreign content.
            foreach (string name in UpdateSession.ArtifactNames) OwnArtifacts.Add(name);
        }

        /// <summary>
        /// Whether it's safe to update this folder.
        /// </summary>
        /// <param name="reason">Filled with a human-readable explanation if it isn't safe.</param>
        public static bool IsSafeTarget(string path, out string reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                reason = Loc.T("guard.noPath");
                return false;
            }

            var directory = new DirectoryInfo(path);

            // The folder doesn't exist yet — we'll create it ourselves, a routine fresh install.
            if (!directory.Exists) return true;

            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos();
            }
            catch (Exception ex)
            {
                reason = Loc.T("guard.unreadable", ex.Message);
                return false;
            }

            // Empty — also a fresh install.
            if (entries.Length == 0) return true;

            // A Valheim client is inside — proceed.
            if (entries.Any(e => ClientMarkers.Contains(e.Name, StringComparer.OrdinalIgnoreCase)))
                return true;

            string[] unexpected = entries
                .Where(e => !OwnArtifacts.Contains(e.Name))
                .Select(e => e.Name)
                .ToArray();

            // Only our own files from an interrupted attempt remain — no obstacle.
            if (unexpected.Length == 0) return true;

            string sample = string.Join(", ", unexpected.Take(5));
            if (unexpected.Length > 5) sample += " " + Loc.T("guard.andMore", unexpected.Length - 5);

            reason = Loc.T("guard.notClient", string.Join(" / ", ClientMarkers), sample);
            return false;
        }

        /// <summary>
        /// Whether the client folder is writable. Checked ahead of time with an actual write:
        /// otherwise a permissions gap would surface mid-way through a multi-gigabyte download
        /// and look like a vague "error while processing files".
        /// If the folder doesn't exist yet, we check the nearest existing parent — i.e. whether
        /// it CAN be created. Nothing is actually created here.
        /// </summary>
        /// <param name="advice">What the user should do; the text depends on the OS.</param>
        public static bool IsWritable(string path, out string reason, out string advice)
        {
            reason = null;
            advice = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                reason = Loc.T("guard.noPath");
                advice = Loc.T("guard.specifyFolder");
                return false;
            }

            string probeDirectory;
            try
            {
                probeDirectory = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                reason = Loc.T("guard.badPath", ex.Message);
                advice = Loc.T("guard.checkSpelling");
                return false;
            }

            // Walk up to the first existing folder — its permissions determine the outcome.
            while (!Directory.Exists(probeDirectory))
            {
                string parent = Path.GetDirectoryName(probeDirectory);
                if (string.IsNullOrEmpty(parent) || parent == probeDirectory)
                {
                    reason = Loc.T("guard.noParent", path);
                    advice = Loc.T("guard.checkDrive");
                    return false;
                }

                probeDirectory = parent;
            }

            string probeFile = Path.Combine(probeDirectory, ".launcher-write-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (FileStream stream = File.Create(probeFile, 1, FileOptions.DeleteOnClose))
                {
                    stream.WriteByte(0);
                }

                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                reason = Loc.T("guard.notWritable", probeDirectory, ex.GetType().Name, ex.Message);
                advice = WriteAdviceForCurrentOs(probeDirectory);
                return false;
            }
            finally
            {
                // DeleteOnClose usually removes the file itself; clean up in case it doesn't.
                try { if (File.Exists(probeFile)) File.Delete(probeFile); } catch { }
            }
        }

        private static string WriteAdviceForCurrentOs(string directory)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Loc.T("guard.advice.windows");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Loc.T("guard.advice.macos", directory);
            }

            return Loc.T("guard.advice.linux", directory);
        }
    }
}
