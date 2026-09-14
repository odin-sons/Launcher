using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Windows Defender routinely quarantines or briefly locks a freshly-downloaded mod dll
    /// while it scans it — which the launcher's own hash check can see as a mismatch and log
    /// as a "suspicious re-download" (see ClientLedger) even though nothing is actually wrong.
    /// Offering an exclusion for the client folder up front heads that off at the source,
    /// instead of only flagging it after the fact.
    ///
    /// Windows-only by construction: on any other OS, both checks report "nothing to do"
    /// rather than throwing, so a caller doesn't need its own OperatingSystem.IsWindows() guard.
    /// </summary>
    public static class DefenderExclusion
    {
        // No longer written — the one-time prompt this marked as shown/dismissed was replaced
        // by a persistent checkbox on the Install tab (DefenderExclusionCheckBox), which reads
        // live Defender state instead of a one-shot marker file. Kept as a known/protected name
        // (ClientFolderGuard.OwnArtifacts, FileDownloader.ExcludeFiles) purely so a folder that
        // already has one from an older launcher version isn't flagged as foreign content.
        public const string PromptedMarkerName = "defender_prompt_shown";

        /// <summary>
        /// Whether the folder already has a Defender exclusion covering it — an exact match or
        /// a parent folder. Read-only: querying Defender's own preferences never needs
        /// elevation, unlike adding one, so this can run silently on every launch.
        /// </summary>
        public static bool IsExcluded(string folderPath)
        {
            if (!OperatingSystem.IsWindows()) return true; // nothing to warn about

            try
            {
                string fullPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
                string output = RunPowerShell("(Get-MpPreference).ExclusionPath");

                return output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim().TrimEnd('\\'))
                    .Any(excluded => excluded.Length > 0 &&
                                     fullPath.StartsWith(excluded, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Can't tell — Defender might be off, PowerShell might be missing or blocked by
                // policy, whatever. Treat as "already fine" rather than nag needlessly.
                return true;
            }
        }

        /// <summary>
        /// Prompts for elevation (UAC) and adds the exclusion. Returns false if the player
        /// declined the UAC prompt or the command failed for any other reason — never throws.
        /// </summary>
        public static bool TryAddExclusion(string folderPath) =>
            RunElevatedExclusionCommand("Add-MpPreference", folderPath);

        /// <summary>Same elevation/failure behavior as TryAddExclusion, in reverse — used when
        /// the player unchecks the exclusion checkbox on the Install tab.</summary>
        public static bool TryRemoveExclusion(string folderPath) =>
            RunElevatedExclusionCommand("Remove-MpPreference", folderPath);

        private static bool RunElevatedExclusionCommand(string cmdlet, string folderPath)
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string fullPath = Path.GetFullPath(folderPath);
                string escaped = fullPath.Replace("'", "''");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{cmdlet} -ExclusionPath '{escaped}'\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using Process process = Process.Start(psi);
                process.WaitForExit(15000);
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                // The UAC prompt was declined — not an error, just "no".
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string RunPowerShell(string command)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
    }
}
