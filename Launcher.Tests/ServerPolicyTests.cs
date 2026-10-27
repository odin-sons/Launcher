using System;
using System.IO;
using System.Threading.Tasks;
using Odinsons.ValheimLauncher;
using Xunit;
using static Launcher.Tests.TestRegistry;

namespace Launcher.Tests
{
    public sealed class ServerPolicyTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "server-policy-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private ServerGate Gate(SwitchableFetcher fetcher) => TestRegistry.Gate(_folder, fetcher);

        [Fact]
        public void Approved_AllowsEverything()
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("approved"), "community", installed: false);

            Assert.True(d.Listed && d.CanInstall && d.CanUpdate && d.CanLaunch);
            Assert.False(d.NeedsConfirmation);
            Assert.Equal(ServerNotice.None, d.Notice);
        }

        [Fact]
        public void NeedsApproval_NotInstalled_IsHiddenAndCannotBeInstalled()
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("needs-approval"), "community", installed: false);

            Assert.False(d.Listed);
            Assert.False(d.CanInstall);
            Assert.False(d.CanLaunch);
            Assert.Equal(ServerNotice.NeedsApproval, d.Notice);
        }

        [Fact]
        public void NeedsApproval_Installed_LaunchesOnlyAfterConfirmation_AndNeverUpdates()
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("needs-approval"), "community", installed: true);

            Assert.True(d.Listed);
            Assert.True(d.CanLaunch);
            Assert.True(d.NeedsConfirmation);
            Assert.False(d.CanUpdate);
            Assert.False(d.CanInstall);
            Assert.Equal(ServerNotice.NeedsApproval, d.Notice);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Suspended_IsHiddenAndNeverLaunchedOrUpdated(bool installed)
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded("suspended"), "community", installed);

            Assert.False(d.Listed);
            Assert.False(d.CanInstall || d.CanUpdate || d.CanLaunch);
            Assert.Equal(installed, d.OffersRemoval);
            Assert.Equal(ServerNotice.Suspended, d.Notice);
        }

        [Fact]
        public void AProfileMissingFromTheIndex_IsLeftAloneButNotUpdated()
        {
            ServerDecision installed = ServerPolicy.Evaluate(Loaded("approved", "other"), "community", installed: true);
            ServerDecision fresh = ServerPolicy.Evaluate(Loaded("approved", "other"), "community", installed: false);

            Assert.True(installed.CanLaunch);
            Assert.False(installed.CanUpdate);
            Assert.False(fresh.Listed || fresh.CanInstall || fresh.CanLaunch);
            Assert.Equal(ServerNotice.Unlisted, installed.Notice);
        }

        [Fact]
        public void WithoutAnIndex_AnInstalledProfileLaunchesButDoesNotUpdate()
        {
            var none = new RegistryLoadResult { Failure = RegistryFailure.Unreachable };

            ServerDecision installed = ServerPolicy.Evaluate(none, "community", installed: true);
            ServerDecision fresh = ServerPolicy.Evaluate(none, "community", installed: false);

            Assert.True(installed.CanLaunch);
            Assert.False(installed.CanUpdate);
            Assert.False(fresh.CanInstall);
            Assert.Equal(ServerNotice.RegistryUnavailable, installed.Notice);
        }

        [Theory]
        [InlineData("suspended")]
        [InlineData("needs-approval")]
        public void TheBuiltInProfile_IgnoresTheRegistry(string status)
        {
            ServerDecision d = ServerPolicy.Evaluate(Loaded(status, "odinsons"), "odinsons", installed: true);

            Assert.True(d.CanUpdate && d.CanLaunch);
            Assert.False(d.NeedsConfirmation);
        }

        [Fact]
        public async Task AStatusFlipBetweenTwoLaunches_TakesEffectOnTheSecond()
        {
            var fetcher = new SwitchableFetcher();
            ServerGate gate = Gate(fetcher);

            fetcher.Body = Index(1, "approved");
            ServerDecision first = await gate.CheckAsync("community", installed: true);

            fetcher.Body = Index(2, "suspended");
            ServerDecision second = await gate.CheckAsync("community", installed: true);

            fetcher.Body = Index(3, "approved");
            ServerDecision third = await gate.CheckAsync("community", installed: true);

            Assert.True(first.CanLaunch && first.CanUpdate);
            Assert.False(second.CanLaunch || second.CanUpdate);
            Assert.True(second.OffersRemoval);
            Assert.True(third.CanLaunch && third.CanUpdate);
        }

        [Fact]
        public async Task AReplayedApprovedIndex_DoesNotUndoASuspension()
        {
            var fetcher = new SwitchableFetcher();
            ServerGate gate = Gate(fetcher);

            fetcher.Body = Index(5, "suspended");
            await gate.CheckAsync("community", installed: true);

            fetcher.Body = Index(4, "approved");
            ServerDecision replayed = await gate.CheckAsync("community", installed: true);

            Assert.False(replayed.CanLaunch);
            Assert.Equal(ServerNotice.Suspended, replayed.Notice);
        }
    }
}
