using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ProfileQuarantineTests : IDisposable
    {
        public static readonly TheoryData<TargetOs> AllOs = new() { TargetOs.Windows, TargetOs.MacOS, TargetOs.Linux };

        private readonly TestPack _profile = new();

        public void Dispose() => _profile.Dispose();

        private string Path(string relative) => System.IO.Path.Combine(_profile.Root, relative);

        private static string EntryPoint => ProfileQuarantine.EntryPoints[0];

        /// <summary>A profile folder that holds the game copy and the mod loader's entry point.</summary>
        private void LayGameCopy()
        {
            _profile.AddGameExecutable();
            _profile.AddFile("BepInEx/plugins/mod.dll", "mod");
            _profile.AddFile("BepInEx/config/mod.cfg", "setting");
            _profile.AddFile("valheim_Data/data.bin", "data");
            _profile.AddFile(EntryPoint, "loader");
        }

        private void LayInjectorProfile()
        {
            _profile.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");
            _profile.AddFile(EntryPoint, "loader");
        }

        private static Action<string, string> Failing() =>
            (_, _) => throw new IOException("the file is in use");

        private string[] Snapshot() =>
            Directory.EnumerateFiles(_profile.Root, "*", SearchOption.AllDirectories)
                .Select(file => System.IO.Path.GetRelativePath(_profile.Root, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();

        [Theory, MemberData(nameof(AllOs))]
        public void Apply_RenamesOnlyTheEntryPoint_AndRecordsIt(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            LayGameCopy();
            string[] before = Snapshot();

            QuarantineResult result = new ProfileQuarantine().Apply(_profile.Root);

            Assert.Equal(QuarantineOutcome.Applied, result.Outcome);
            Assert.False(File.Exists(Path(EntryPoint)));
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint + ProfileQuarantine.Suffix)));
            Assert.True(File.Exists(Path(ProfileQuarantine.RecordFileName)));

            string[] expected = before.Where(f => f != EntryPoint)
                .Append(EntryPoint + ProfileQuarantine.Suffix)
                .Append(ProfileQuarantine.RecordFileName)
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, Snapshot());
        }

        [Theory, MemberData(nameof(AllOs))]
        public void InjectorMode_HasNothingToDisable(TargetOs os)
        {
            using var _ = RuntimePlatform.Pretend(os);
            LayInjectorProfile();
            string[] before = Snapshot();

            QuarantineResult result = new ProfileQuarantine().Apply(_profile.Root);

            Assert.Equal(QuarantineOutcome.NotApplicable, result.Outcome);
            Assert.Equal(before, Snapshot());
        }

        [Fact]
        public void AGameCopyWithoutALoader_HasNothingToDisable()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            _profile.AddGameExecutable();
            _profile.AddFile("BepInEx/core/BepInEx.Preloader.dll", "preloader");

            Assert.Equal(QuarantineOutcome.NotApplicable, new ProfileQuarantine().Apply(_profile.Root).Outcome);
            Assert.False(File.Exists(Path(ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public void AMissingFolder_HasNothingToDisable()
        {
            QuarantineResult result = new ProfileQuarantine().Apply(Path("gone"));

            Assert.Equal(QuarantineOutcome.NotApplicable, result.Outcome);
            Assert.False(Directory.Exists(Path("gone")));
        }

        [Fact]
        public void TheGameCopy_IsStillRecognisedWhileAnUpdateHasItsExecutableStashed()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            File.Move(Path("valheim.exe"), Path("valheim.exe" + UpdateSession.StashSuffix));

            Assert.Equal(QuarantineOutcome.Applied, new ProfileQuarantine().Apply(_profile.Root).Outcome);
        }

        [Fact]
        public void Apply_Twice_ChangesNothingTheSecondTime()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            var quarantine = new ProfileQuarantine();
            quarantine.Apply(_profile.Root);
            string[] afterFirst = Snapshot();

            QuarantineResult second = quarantine.Apply(_profile.Root);

            Assert.Equal(QuarantineOutcome.AlreadyApplied, second.Outcome);
            Assert.Equal(afterFirst, Snapshot());
        }

        [Fact]
        public void Restore_PutsTheOriginalNameBack_AndDropsTheRecord()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            string[] before = Snapshot();
            var quarantine = new ProfileQuarantine();
            quarantine.Apply(_profile.Root);

            QuarantineResult result = quarantine.Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.Restored, result.Outcome);
            Assert.Equal(before, Snapshot());
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint)));
        }

        [Fact]
        public void Restore_WithoutARecord_LeavesASuspendedFileAlone()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            File.Move(Path(EntryPoint), Path(EntryPoint + ProfileQuarantine.Suffix));
            string[] before = Snapshot();

            QuarantineResult result = new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.NotApplicable, result.Outcome);
            Assert.Equal(before, Snapshot());
        }

        [Fact]
        public void ARenameThatFails_LeavesTheRecordAndTheFile_AndTheNextApplyFinishesIt()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();

            QuarantineResult failed = new ProfileQuarantine(Failing()).Apply(_profile.Root);

            Assert.Equal(QuarantineOutcome.Failed, failed.Outcome);
            Assert.True(File.Exists(Path(ProfileQuarantine.RecordFileName)));
            Assert.True(File.Exists(Path(EntryPoint)));
            Assert.False(File.Exists(Path(EntryPoint + ProfileQuarantine.Suffix)));

            QuarantineResult retried = new ProfileQuarantine().Apply(_profile.Root);

            Assert.Equal(QuarantineOutcome.Applied, retried.Outcome);
            Assert.False(File.Exists(Path(EntryPoint)));
            Assert.True(File.Exists(Path(EntryPoint + ProfileQuarantine.Suffix)));
        }

        [Fact]
        public void Interrupted_RecordedButNotRenamed_RestoreJustDropsTheRecord()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            new ProfileQuarantine(Failing()).Apply(_profile.Root);

            QuarantineResult result = new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.NotApplicable, result.Outcome);
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint)));
            Assert.False(File.Exists(Path(ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public void Interrupted_RenamedButTheRecordKept_IsTheOrdinaryQuarantinedState_AndRestoreFinishesIt()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            new ProfileQuarantine().Apply(_profile.Root);
            Assert.False(File.Exists(Path(EntryPoint)));
            Assert.True(File.Exists(Path(ProfileQuarantine.RecordFileName)));

            QuarantineResult result = new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.Restored, result.Outcome);
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint)));
            Assert.False(File.Exists(Path(ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public void Interrupted_RestoredButTheRecordKept_ASecondRestoreDropsTheRecord()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            new ProfileQuarantine().Apply(_profile.Root);
            string record = File.ReadAllText(Path(ProfileQuarantine.RecordFileName));
            File.Move(Path(EntryPoint + ProfileQuarantine.Suffix), Path(EntryPoint));
            File.WriteAllText(Path(ProfileQuarantine.RecordFileName), record);

            QuarantineResult result = new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.NotApplicable, result.Outcome);
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint)));
            Assert.False(File.Exists(Path(ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public void ARestoreThatFails_KeepsTheRecordSoItCanBeRetried()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            new ProfileQuarantine().Apply(_profile.Root);

            QuarantineResult failed = new ProfileQuarantine(Failing()).Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.Failed, failed.Outcome);
            Assert.True(File.Exists(Path(ProfileQuarantine.RecordFileName)));

            Assert.Equal(QuarantineOutcome.Restored, new ProfileQuarantine().Restore(_profile.Root).Outcome);
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint)));
        }

        [Fact]
        public void Restore_NeverOverwritesAFileThatIsAlreadyBackUnderItsName()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            new ProfileQuarantine().Apply(_profile.Root);
            File.WriteAllText(Path(EntryPoint), "a newer loader");

            new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal("a newer loader", File.ReadAllText(Path(EntryPoint)));
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint + ProfileQuarantine.Suffix)));
            Assert.False(File.Exists(Path(ProfileQuarantine.RecordFileName)));
        }

        [Fact]
        public void Apply_ReplacesAStaleSuspendedCopy()
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            File.WriteAllText(Path(EntryPoint + ProfileQuarantine.Suffix), "stale");

            QuarantineResult result = new ProfileQuarantine().Apply(_profile.Root);

            Assert.Equal(QuarantineOutcome.Applied, result.Outcome);
            Assert.Equal("loader", File.ReadAllText(Path(EntryPoint + ProfileQuarantine.Suffix)));
        }

        [Theory]
        [InlineData("../outside.txt")]
        [InlineData("valheim.exe")]
        [InlineData("BepInEx/plugins/mod.dll")]
        public void ARecordNamingAnythingButAnEntryPoint_RenamesNothing(string name)
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            File.WriteAllText(Path(name.TrimStart('.', '/')) + ProfileQuarantine.Suffix, "bait");
            File.WriteAllText(Path(ProfileQuarantine.RecordFileName),
                "{\"version\":1,\"entries\":[" + System.Text.Json.JsonSerializer.Serialize(name) + "]}");
            File.Delete(Path(name.TrimStart('.', '/')));
            string[] before = Snapshot();

            new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal(before.Where(f => f != ProfileQuarantine.RecordFileName), Snapshot());
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"entries\":\"winhttp.dll\"}")]
        [InlineData("[]")]
        public void AnUnreadableRecord_IsIgnored(string content)
        {
            using var _ = RuntimePlatform.Pretend(TargetOs.Windows);
            LayGameCopy();
            File.Move(Path(EntryPoint), Path(EntryPoint + ProfileQuarantine.Suffix));
            File.WriteAllText(Path(ProfileQuarantine.RecordFileName), content);
            string[] before = Snapshot();

            QuarantineResult result = new ProfileQuarantine().Restore(_profile.Root);

            Assert.Equal(QuarantineOutcome.NotApplicable, result.Outcome);
            Assert.Equal(before, Snapshot());
        }
    }
}
