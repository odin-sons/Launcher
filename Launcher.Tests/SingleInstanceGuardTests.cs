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
    }
}
