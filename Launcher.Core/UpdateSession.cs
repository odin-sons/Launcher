using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Owns updating the client folder for the duration of the run and handles two concerns:
    ///
    /// 1. Prevents accidentally launching the game mid-update — the executable is moved
    ///    aside, and a double-click simply does nothing.
    /// 2. Prevents two launchers from writing to the same folder — a lock file with a PID.
    ///
    /// A side benefit: if the previous run was interrupted, it shows up as an orphaned lock,
    /// and the next run can be forced to do a full check.
    ///
    /// Doesn't protect against deliberate circumvention and doesn't try to.
    /// </summary>
    public sealed class UpdateSession : IDisposable
    {
        public const string LockFileName = ".launcher-lock";
        public const string StashSuffix = ".updating";

        /// <summary>Game executables that get hidden away for the duration of the run.</summary>
        private static readonly string[] GameExecutables = { "valheim.exe", "valheim.x86_64" };

        /// <summary>Everything the session might create in the client folder — never count these as "extra".</summary>
        public static IEnumerable<string> ArtifactNames =>
            new[] { LockFileName }.Concat(GameExecutables.Select(e => e + StashSuffix));

        private readonly string _clientFolder;
        private readonly string _lockPath;
        private readonly List<string> _stashed = new();
        private bool _disposed;
        private bool _executablesRestored;

        /// <summary>The previous run didn't finish the update — worth checking everything fully.</summary>
        public bool PreviousRunInterrupted { get; private set; }

        private UpdateSession(string clientFolder)
        {
            _clientFolder = clientFolder;
            _lockPath = Path.Combine(clientFolder, LockFileName);
        }

        public static bool TryBegin(string clientFolder, out UpdateSession session, out string reason)
        {
            session = null;
            var candidate = new UpdateSession(clientFolder);

            if (!candidate.TryAcquireLock(out reason)) return false;

            candidate.RestoreOrphanedStash();
            candidate.StashExecutables();

            session = candidate;
            return true;
        }

        private bool TryAcquireLock(out string reason)
        {
            reason = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var stream = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream);
                    writer.WriteLine("pid=" + Environment.ProcessId);
                    writer.WriteLine("machine=" + Environment.MachineName);
                    writer.WriteLine("started=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    return true;
                }
                catch (IOException) when (File.Exists(_lockPath))
                {
                    // The lock already exists. Is it still alive?
                    if (IsLockAlive(out string owner))
                    {
                        reason = Loc.T("session.busy", owner);
                        return false;
                    }

                    // Orphaned — the previous process died without cleaning up after itself.
                    PreviousRunInterrupted = true;
                    try
                    {
                        File.Delete(_lockPath);
                    }
                    catch (Exception ex)
                    {
                        reason = Loc.T("session.staleLockFailed", _lockPath, ex.Message);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    reason = Loc.T("session.lockCreateFailed", _lockPath, ex.Message);
                    return false;
                }
            }

            reason = Loc.T("session.lockFailed");
            return false;
        }

        private bool IsLockAlive(out string owner)
        {
            owner = Loc.T("session.unknownProcess");

            Dictionary<string, string> fields;
            try
            {
                fields = File.ReadAllLines(_lockPath)
                    .Select(l => l.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // unreadable — treat as garbage
            }

            fields.TryGetValue("machine", out string machine);
            fields.TryGetValue("started", out string started);

            // A different machine: the process can't be checked. This happens when the
            // client folder gets synced between computers. Treat the lock as dead, but
            // report this fact upward.
            if (!string.IsNullOrEmpty(machine) &&
                !string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!fields.TryGetValue("pid", out string pidText) || !int.TryParse(pidText, out int pid))
                return false;

            try
            {
                Process process = Process.GetProcessById(pid);
                owner = Loc.T("session.owner", pid, process.ProcessName, started);
                return true;
            }
            catch (ArgumentException)
            {
                return false; // no such process
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>The previous run died without moving the executable back — restore it.</summary>
        private void RestoreOrphanedStash()
        {
            foreach (string executable in GameExecutables)
            {
                string real = Path.Combine(_clientFolder, executable);
                string stash = real + StashSuffix;

                if (!File.Exists(stash)) continue;

                PreviousRunInterrupted = true;
                try
                {
                    if (File.Exists(real)) File.Delete(stash);
                    else File.Move(stash, real);
                }
                catch
                {
                    // Not fatal: the update will redownload the file anyway.
                }
            }
        }

        private void StashExecutables()
        {
            foreach (string executable in GameExecutables)
            {
                string real = Path.Combine(_clientFolder, executable);
                if (!File.Exists(real)) continue;

                string stash = real + StashSuffix;
                try
                {
                    if (File.Exists(stash)) File.Delete(stash);
                    File.Move(real, stash);
                    _stashed.Add(executable);
                }
                catch
                {
                    // Couldn't stash it — the update still needs to go through.
                    // Protection against an accidental launch just won't work this run.
                }
            }
        }

        /// <summary>
        /// Moves stashed executables back into place.
        ///
        /// Call this right when the update finishes, not when the lock is released. The file
        /// is only hidden away to prevent the game from being launched mid-download; as soon
        /// as the download is done, it must come back. Otherwise: the update reports "ready",
        /// the launcher tries to start the game on that signal — but valheim.exe is still
        /// sitting under the name valheim.exe.updating, and the player sees "file not found".
        ///
        /// Idempotent: Dispose calls this same method, a repeat call does nothing.
        /// </summary>
        public void EndUpdate()
        {
            if (_executablesRestored) return;
            _executablesRestored = true;

            foreach (string executable in _stashed)
            {
                string real = Path.Combine(_clientFolder, executable);
                string stash = real + StashSuffix;
                if (!File.Exists(stash)) continue;

                try
                {
                    // If the update downloaded a fresh exe, there's nothing to restore —
                    // discard the stashed copy. Otherwise move the stashed file back.
                    if (File.Exists(real)) File.Delete(stash);
                    else File.Move(stash, real);

                    LauncherLog.Debug($"restored '{executable}' after the update");
                }
                catch (Exception ex)
                {
                    // RestoreOrphanedStash will pick up the leftover on the next run.
                    LauncherLog.Warn($"could not restore '{executable}': {ex.Message}");
                }
            }

            _stashed.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            EndUpdate();

            try { if (File.Exists(_lockPath)) File.Delete(_lockPath); } catch { }
        }
    }
}
