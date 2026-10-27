using Odinsons.ValheimLauncher;
using Xunit;
using static Launcher.Tests.TestRegistry;

namespace Launcher.Tests
{
    public sealed class ServerAccessTests : IDisposable
    {
        private static readonly ServerProfile Community = new("community", "Community", new[] { "https://server.example.test/Launcher/" });

        private readonly string _cache = Path.Combine(Path.GetTempPath(), "server-access-" + Guid.NewGuid().ToString("N"));
        private readonly TestPack _profile = new();
        private readonly SwitchableFetcher _fetcher = new();
        private readonly ServerAccess _access;
        private readonly IDisposable _os = RuntimePlatform.Pretend(TargetOs.Windows);

        public ServerAccessTests()
        {
            _access = new ServerAccess(Gate(_cache, _fetcher), new ProfileQuarantine());
        }

        public void Dispose()
        {
            _os.Dispose();
            _profile.Dispose();
            if (Directory.Exists(_cache)) Directory.Delete(_cache, true);
        }

        private string EntryPoint => Path.Combine(_profile.Root, "winhttp.dll");
        private string Suspended => EntryPoint + ProfileQuarantine.Suffix;

        private void LayGameCopy()
        {
            _profile.AddGameExecutable();
            _profile.AddFile("BepInEx/plugins/mod.dll", "mod");
            _profile.AddFile("winhttp.dll", "loader");
        }

        private void LayInjectorProfile()
        {
            _profile.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            _profile.AddFile("winhttp.dll", "loader");
        }

        private Task<ServerCheck> Check(bool start = true) => _access.CheckAsync(Community, _profile.Root, start);

        [Fact]
        public async Task Approved_Proceeds_AndLeavesTheFilesAlone()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "approved");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.Proceed, check.Step);
            Assert.True(File.Exists(EntryPoint));
            Assert.False(File.Exists(Path.Combine(_profile.Root, ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public async Task Approved_NotInstalledYet_ProceedsToInstall()
        {
            _fetcher.Body = Index(1, "approved");

            ServerCheck check = await Check();

            Assert.False(check.Installed);
            Assert.Equal(GateStep.Proceed, check.Step);
        }

        [Fact]
        public async Task Suspended_Installed_RefusesAndOffersRemoval_AndDisablesTheEntryPoint()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "suspended");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.OfferRemoval, check.Step);
            Assert.Equal(ServerNotice.Suspended, check.Decision.Notice);
            Assert.False(File.Exists(EntryPoint));
            Assert.True(File.Exists(Suspended));
            Assert.True(File.Exists(Path.Combine(_profile.Root, "BepInEx", "plugins", "mod.dll")));
        }

        [Fact]
        public async Task Suspended_InInjectorMode_RefusesWithNothingToDisable()
        {
            LayInjectorProfile();
            _fetcher.Body = Index(1, "suspended");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.OfferRemoval, check.Step);
            Assert.True(File.Exists(EntryPoint));
            Assert.False(File.Exists(Suspended));
            Assert.False(File.Exists(Path.Combine(_profile.Root, ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public async Task Suspended_NotInstalled_RefusesWithoutOfferingRemoval()
        {
            _fetcher.Body = Index(1, "suspended");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.Refuse, check.Step);
            Assert.False(check.Decision.OffersRemoval);
        }

        [Fact]
        public async Task NeedsApproval_Installed_AsksFirst_AndKeepsTheLoaderUntilTheAnswerIsYes()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "suspended");
            await Check();
            _fetcher.Body = Index(2, "needs-approval");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.ConfirmThenLaunch, check.Step);
            Assert.True(check.Decision.NeedsConfirmation);
            Assert.False(check.Decision.CanUpdate);
            Assert.False(File.Exists(EntryPoint));

            QuarantineResult restored = _access.ConfirmLaunch(_profile.Root);

            Assert.Equal(QuarantineOutcome.Restored, restored.Outcome);
            Assert.True(File.Exists(EntryPoint));
        }

        [Fact]
        public async Task NeedsApproval_Installed_WithoutPlaying_OnlyPausesTheUpdate()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "needs-approval");

            ServerCheck check = await Check(start: false);

            Assert.Equal(GateStep.Paused, check.Step);
            Assert.Equal(ServerNotice.NeedsApproval, check.Decision.Notice);
            Assert.True(File.Exists(EntryPoint));
        }

        [Fact]
        public async Task NeedsApproval_NotInstalled_CannotBeAdded()
        {
            _fetcher.Body = Index(1, "needs-approval");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.Refuse, check.Step);
        }

        [Fact]
        public async Task NeedsApproval_IsNeverQuarantined()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "needs-approval");

            await Check();

            Assert.True(File.Exists(EntryPoint));
            Assert.False(File.Exists(Path.Combine(_profile.Root, ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public async Task AProfileMissingFromTheIndex_StartsFromTheInstalledFiles_ButIsNotUpdated()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "approved", "someone-else");

            ServerCheck started = await Check();
            ServerCheck checkedOnly = await Check(start: false);

            Assert.Equal(GateStep.LaunchExisting, started.Step);
            Assert.Equal(ServerNotice.Unlisted, started.Decision.Notice);
            Assert.Equal(GateStep.Paused, checkedOnly.Step);
        }

        [Fact]
        public async Task AProfileMissingFromTheIndex_CannotBeAdded()
        {
            _fetcher.Body = Index(1, "approved", "someone-else");

            ServerCheck check = await Check();

            Assert.Equal(GateStep.Refuse, check.Step);
        }

        [Fact]
        public async Task WithoutAnIndex_AnInstalledProfileStartsFromItsFiles_ButIsNotUpdated()
        {
            LayGameCopy();
            _fetcher.Body = null;

            ServerCheck started = await Check();
            ServerCheck checkedOnly = await Check(start: false);

            Assert.Equal(GateStep.LaunchExisting, started.Step);
            Assert.Equal(ServerNotice.RegistryUnavailable, started.Decision.Notice);
            Assert.Equal(GateStep.Paused, checkedOnly.Step);
        }

        [Fact]
        public async Task WithoutAnIndex_AProfileThatIsNotInstalledCannotBeAdded()
        {
            _fetcher.Body = null;

            ServerCheck check = await Check();

            Assert.Equal(GateStep.Refuse, check.Step);
        }

        [Fact]
        public async Task TheBuiltInProfile_IgnoresTheRegistryEntirely()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "suspended", ServerProfile.Default.Id);

            ServerCheck check = await _access.CheckAsync(ServerProfile.Default, _profile.Root, start: true);

            Assert.Equal(GateStep.Proceed, check.Step);
            Assert.Equal(0, _fetcher.Calls);
            Assert.True(File.Exists(EntryPoint));
        }

        [Fact]
        public async Task EveryCheck_LoadsAFreshIndex()
        {
            LayGameCopy();
            _fetcher.Body = Index(1, "approved");

            await Check();
            await Check();

            Assert.Equal(2, _fetcher.Calls);
        }

        [Fact]
        public async Task SuspendedThenApprovedAgain_PutsTheLoaderBackBeforeTheNextLaunch()
        {
            LayGameCopy();

            _fetcher.Body = Index(1, "suspended");
            ServerCheck suspended = await Check();
            bool disabled = !File.Exists(EntryPoint);

            _fetcher.Body = Index(2, "approved");
            ServerCheck approved = await Check();

            Assert.Equal(GateStep.OfferRemoval, suspended.Step);
            Assert.True(disabled);
            Assert.Equal(GateStep.Proceed, approved.Step);
            Assert.Equal("loader", File.ReadAllText(EntryPoint));
            Assert.False(File.Exists(Suspended));
            Assert.False(File.Exists(Path.Combine(_profile.Root, ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public async Task ASuspensionStaysInForce_WhenAnOldApprovedIndexIsReplayed()
        {
            LayGameCopy();
            _fetcher.Body = Index(5, "suspended");
            await Check();

            _fetcher.Body = Index(4, "approved");
            ServerCheck replayed = await Check();

            Assert.Equal(GateStep.OfferRemoval, replayed.Step);
            Assert.False(File.Exists(EntryPoint));
        }

        [Fact]
        public async Task ApplySuspensions_DisablesEverySuspendedInstalledProfile_AndNothingElse()
        {
            using var other = new TestPack();
            using var approved = new TestPack();
            using var missing = new TestPack();
            LayGameCopy();
            foreach (TestPack pack in new[] { other, approved })
            {
                pack.AddGameExecutable();
                pack.AddFile("winhttp.dll", "loader");
            }
            _fetcher.Body = Index(1, ("community", "suspended"), ("other", "suspended"), ("fine", "approved"),
                ("pending", "needs-approval"));
            RegistryLoadResult load = await new RegistryClient(new[] { Mirror }, Keys(), new RegistryCache(_cache),
                new Version(3, 0, 0), _fetcher, () => Now).LoadAsync();

            _access.ApplySuspensions(load, id => id switch
            {
                "community" => new[] { _profile.Root },
                "other" => new[] { other.Root, missing.Root },
                "fine" => new[] { approved.Root },
                _ => Array.Empty<string>()
            });

            Assert.False(File.Exists(EntryPoint));
            Assert.False(File.Exists(Path.Combine(other.Root, "winhttp.dll")));
            Assert.True(File.Exists(Path.Combine(approved.Root, "winhttp.dll")));
            Assert.False(File.Exists(Path.Combine(missing.Root, ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public void ApplySuspensions_WithoutAnIndex_DisablesNothing()
        {
            LayGameCopy();

            _access.ApplySuspensions(new RegistryLoadResult { Failure = RegistryFailure.Unreachable },
                _ => new[] { _profile.Root });

            Assert.True(File.Exists(EntryPoint));
        }

        [Fact]
        public void ApplySuspensions_NeverTouchesTheBuiltInProfile()
        {
            LayGameCopy();

            _access.ApplySuspensions(Loaded("suspended", ServerProfile.Default.Id), _ => new[] { _profile.Root });

            Assert.True(File.Exists(EntryPoint));
        }

        [Theory]
        [InlineData("approved", true, true, GateStep.Proceed)]
        [InlineData("approved", false, true, GateStep.Proceed)]
        [InlineData("needs-approval", true, true, GateStep.ConfirmThenLaunch)]
        [InlineData("needs-approval", true, false, GateStep.Paused)]
        [InlineData("needs-approval", false, true, GateStep.Refuse)]
        [InlineData("suspended", true, true, GateStep.OfferRemoval)]
        [InlineData("suspended", true, false, GateStep.OfferRemoval)]
        [InlineData("suspended", false, true, GateStep.Refuse)]
        public void Plan_PerStatus(string status, bool installed, bool start, GateStep expected)
        {
            ServerDecision decision = ServerPolicy.Evaluate(Loaded(status), "community", installed);

            Assert.Equal(expected, ServerAccess.Plan(decision, installed, start));
        }
    }
}
