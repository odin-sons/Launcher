using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Keeps only one launcher process running at a time. A second launch attempt pings the
    /// first one to come to the foreground (named pipe — works the same on Windows/macOS/Linux,
    /// unlike a named Mutex, which .NET doesn't support by name outside Windows) and exits
    /// immediately, before showing any window of its own.
    ///
    /// Same PID-file pattern as <see cref="UpdateSession"/>'s lock, deliberately: written once
    /// and closed, not held open — staleness is detected by checking whether the recorded PID
    /// is still alive, not by an OS-level file lock. A process that dies without cleaning up
    /// (a hard `Environment.Exit`, a crash) just leaves a lock the next launch recognizes as
    /// stale and clears on its own — same self-healing as UpdateSession's orphan handling.
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        public const string LockFileName = "launcher-instance.lock";
        private const string PipeNamePrefix = "OdinsonsLauncher.SingleInstance.";

        private readonly string _lockPath;
        private readonly string _pipeName;
        private CancellationTokenSource _listenerCts;
        private bool _disposed;

        /// <summary>Raised on the primary instance whenever a later launch attempt pings it. Not
        /// guaranteed to fire on the UI thread — marshal accordingly.</summary>
        public event Action ActivateRequested;

        private SingleInstanceGuard(string lockPath, string pipeName)
        {
            _lockPath = lockPath;
            _pipeName = pipeName;
        }

        /// <summary>
        /// Tries to become the one running instance *of this install*. On success, starts
        /// listening for pings from later launch attempts and returns a guard the caller must
        /// keep alive (and eventually Dispose) for the life of the app. On failure, pings
        /// whichever instance already holds the lock to come to the foreground and returns
        /// null — the caller should exit immediately without creating any UI.
        ///
        /// The pipe name is derived from appFolder (see PipeNameFor) so two independent installs
        /// on the same machine — e.g. a game-bundled copy and a separate dist/ build, or several
        /// server presets each with their own folder — never contend for the same pipe. The lock
        /// FILE was already per-folder; the pipe name used to be a single hardcoded constant
        /// shared by every install, so a second install could end up activating a first
        /// install's window instead of ever showing (or even attempting) one of its own —
        /// looking exactly like it "remembered" settings it never actually loaded.
        /// </summary>
        public static SingleInstanceGuard TryBecomePrimary(string appFolder)
        {
            string lockPath = Path.Combine(appFolder, LockFileName);
            string pipeName = PipeNameFor(appFolder);
            var candidate = new SingleInstanceGuard(lockPath, pipeName);

            if (candidate.TryAcquireLock())
            {
                candidate.StartListening();
                return candidate;
            }

            NotifyPrimaryInstance(pipeName);
            return null;
        }

        /// <summary>A stable, filesystem-path-safe pipe name unique to this install folder.
        /// Full path (not just the folder name) so two differently-named folders never collide,
        /// and a short hash rather than the raw path so it stays within named-pipe length limits
        /// and away from characters pipe names can't contain.</summary>
        private static string PipeNameFor(string appFolder)
        {
            string normalized = Path.GetFullPath(appFolder).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return PipeNamePrefix + Convert.ToHexString(hash, 0, 8);
        }

        private bool TryAcquireLock()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var stream = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(stream);
                    writer.WriteLine("pid=" + Environment.ProcessId);
                    return true;
                }
                catch (IOException) when (File.Exists(_lockPath))
                {
                    if (IsLockAlive()) return false;

                    try
                    {
                        File.Delete(_lockPath);
                    }
                    catch
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private bool IsLockAlive()
        {
            string pidText;
            try
            {
                pidText = File.ReadAllLines(_lockPath)[0];
            }
            catch
            {
                return false; // unreadable — treat as garbage, safe to replace
            }

            if (!pidText.StartsWith("pid=", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(pidText.AsSpan(4), out int pid))
                return false;

            try
            {
                Process.GetProcessById(pid);
                return true;
            }
            catch (ArgumentException)
            {
                return false; // no such process — stale
            }
        }

        private void StartListening()
        {
            _listenerCts = new CancellationTokenSource();
            _ = ListenLoopAsync(_listenerCts.Token);
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    ActivateRequested?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // A stray pipe failure shouldn't take the listener down for the rest of
                    // the run — just try again.
                }
            }
        }

        /// <summary>Best-effort: if the primary instance's pipe isn't there for any reason,
        /// this launch attempt still exits, just without waking it up.</summary>
        private static void NotifyPrimaryInstance(string pipeName)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect(1000);
            }
            catch
            {
                // Nothing to do — the caller exits regardless.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _listenerCts?.Cancel();
            try { if (File.Exists(_lockPath)) File.Delete(_lockPath); } catch { }
        }
    }
}
