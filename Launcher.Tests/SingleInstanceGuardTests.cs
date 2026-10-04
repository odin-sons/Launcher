using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public class SingleInstanceGuardTests
    {
        private static string TempFolder() => Directory.CreateTempSubdirectory("odinsons-singleinstance-").FullName;

        [Fact]
        public void FreshFolder_BecomesPrimary_AndWritesTheLockFile()
        {
            string folder = TempFolder();

            using SingleInstanceGuard guard = SingleInstanceGuard.TryBecomePrimary(folder);

            Assert.NotNull(guard);
            Assert.True(File.Exists(Path.Combine(folder, SingleInstanceGuard.LockFileName)));
        }

        [Fact]
        public void SecondAttempt_WhilePrimaryIsAlive_ReturnsNull()
        {
            // Both attempts run as this same test process, so the recorded PID in the lock
            // file is genuinely alive throughout — a faithful stand-in for "another real
            // instance is still running".
            string folder = TempFolder();
            using SingleInstanceGuard primary = SingleInstanceGuard.TryBecomePrimary(folder);

            SingleInstanceGuard second = SingleInstanceGuard.TryBecomePrimary(folder);

            Assert.NotNull(primary);
            Assert.Null(second);
        }

        [Fact]
        public void TwoDifferentFolders_BothBecomePrimary_Independently()
        {
            // Regression: the pipe used to activate an existing instance used to be one
            // hardcoded name shared by every install on the machine, even though the lock FILE
            // was already per-folder. A player with more than one install side by side (a
            // game-bundled copy plus a separate dist/ build, say) launching the second one
            // could end up pinging/activating the first one's window instead of ever showing —
            // or even attempting — a window of its own, which looks exactly like the second
            // install "remembering" settings it never actually loaded.
            using SingleInstanceGuard first = SingleInstanceGuard.TryBecomePrimary(TempFolder());
            using SingleInstanceGuard second = SingleInstanceGuard.TryBecomePrimary(TempFolder());

            Assert.NotNull(first);
            Assert.NotNull(second);
        }

        [Fact]
        public async Task SecondAttempt_PingsThePrimaryInstanceToActivate()
        {
            string folder = TempFolder();
            using SingleInstanceGuard primary = SingleInstanceGuard.TryBecomePrimary(folder);
            var activated = new TaskCompletionSource();
            primary.ActivateRequested += () => activated.TrySetResult();

            SingleInstanceGuard second = SingleInstanceGuard.TryBecomePrimary(folder);

            Assert.Null(second);
            Task completed = await Task.WhenAny(activated.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(activated.Task, completed);
        }

        [Fact]
        public void StaleLockFromADeadProcess_IsReplaced()
        {
            string folder = TempFolder();
            string lockPath = Path.Combine(folder, SingleInstanceGuard.LockFileName);
            // A PID essentially guaranteed not to be a live process.
            File.WriteAllText(lockPath, "pid=999999999\n");

            using SingleInstanceGuard guard = SingleInstanceGuard.TryBecomePrimary(folder);

            Assert.NotNull(guard);
            string content = File.ReadAllText(lockPath);
            Assert.DoesNotContain("999999999", content);
        }

        [Fact]
        public void Dispose_RemovesTheLockFile_AndFreesTheFolderForANewPrimary()
        {
            string folder = TempFolder();
            string lockPath = Path.Combine(folder, SingleInstanceGuard.LockFileName);

            SingleInstanceGuard first = SingleInstanceGuard.TryBecomePrimary(folder);
            Assert.NotNull(first);
            first.Dispose();

            Assert.False(File.Exists(lockPath));

            using SingleInstanceGuard second = SingleInstanceGuard.TryBecomePrimary(folder);
            Assert.NotNull(second);
        }

        [Fact]
        public async Task RealisticLongTmpdir_PipeStillFitsTheUnixSocketPathLimit()
        {
            // Windows named pipes aren't filesystem paths, so TMPDIR/sun_path length doesn't apply.
            if (RuntimePlatform.IsWindows) return;

            // Regression: a macOS per-user TMPDIR (/private/var/folders/xx/<long-token>/T/) is
            // routinely 45-60 chars on its own. Combined with .NET's "CoreFxPipe_" Unix-socket
            // prefix and our own pipe name, the total used to blow past the ~103-char sun_path
            // limit, so the very first listen attempt threw before TryBecomePrimary ever returned.
            string folder = TempFolder(); // in the real temp dir, before TMPDIR is overridden below
            string tmpdir = "/private/var/folders/g1/" + new string('a', 24) + "/T/";
            string? original = Environment.GetEnvironmentVariable("TMPDIR");
            try
            {
                Environment.SetEnvironmentVariable("TMPDIR", tmpdir);

                Task<SingleInstanceGuard> attempt = Task.Run(() => SingleInstanceGuard.TryBecomePrimary(folder));
                Task completed = await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(5)));

                Assert.Same(attempt, completed);
                using SingleInstanceGuard guard = await attempt;
                Assert.NotNull(guard);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TMPDIR", original);
            }
        }

        [Fact]
        public async Task PipeCreationFailsRepeatedly_DoesNotBlockTheCallingThread()
        {
            if (RuntimePlatform.IsWindows) return;

            // Long enough that even a much shorter pipe name would still overflow the Unix
            // socket path limit, so every listen attempt is guaranteed to fail. Before the fix,
            // ListenLoopAsync's bare catch retried in a tight synchronous loop with no await,
            // so it never yielded back to the caller — TryBecomePrimary hung forever instead of
            // returning and letting the app show its window.
            string folder = TempFolder(); // in the real temp dir, before TMPDIR is overridden below
            string tmpdir = "/private/var/folders/" + new string('a', 150) + "/T/";
            string? original = Environment.GetEnvironmentVariable("TMPDIR");
            try
            {
                Environment.SetEnvironmentVariable("TMPDIR", tmpdir);

                Task<SingleInstanceGuard> attempt = Task.Run(() => SingleInstanceGuard.TryBecomePrimary(folder));
                Task completed = await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(2)));

                Assert.Same(attempt, completed);
                using SingleInstanceGuard guard = await attempt;
                Assert.NotNull(guard); // the lock file still succeeds even though the pipe can't
            }
            finally
            {
                Environment.SetEnvironmentVariable("TMPDIR", original);
            }
        }
    }
}
