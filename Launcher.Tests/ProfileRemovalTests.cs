using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    public sealed class ProfileRemovalTests : IDisposable
    {
        private readonly string _base = Directory.CreateTempSubdirectory("odinsons-removal-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_base, recursive: true); } catch { }
        }

        private string Profile => Path.Combine(_base, "clients", "community", "main");

        private void LayProfile()
        {
            Directory.CreateDirectory(Path.Combine(Profile, "BepInEx", "plugins"));
            File.WriteAllText(Path.Combine(Profile, "BepInEx", "plugins", "mod.dll"), "mod");
            File.WriteAllText(Path.Combine(Profile, "winhttp.dll.suspended"), "loader");
            File.WriteAllText(Path.Combine(Profile, ProfileQuarantine.RecordFileName), "{}");
        }

        [Fact]
        public void TheProfileFolder_IsRemovedWithEverythingInIt_AndNothingAround()
        {
            LayProfile();
            string sibling = Path.Combine(_base, "clients", "community", "other");
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "keep.txt"), "x");
            File.WriteAllText(Path.Combine(_base, "config.ini"), "x");

            bool removed = ProfileRemoval.TryRemove(Profile, new[] { _base }, out string? reason);

            Assert.True(removed, reason);
            Assert.False(Directory.Exists(Profile));
            Assert.True(File.Exists(Path.Combine(sibling, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(_base, "config.ini")));
        }

        [Fact]
        public void AFolderThatIsAlreadyGone_IsNothingToRemove()
        {
            Assert.True(ProfileRemoval.TryRemove(Profile, Array.Empty<string>(), out _));
        }

        [Fact]
        public void AFolderWithSomeoneElsesFiles_IsRefusedAndLeftAlone()
        {
            Directory.CreateDirectory(Profile);
            File.WriteAllText(Path.Combine(Profile, "taxes.xlsx"), "private");

            bool removed = ProfileRemoval.TryRemove(Profile, Array.Empty<string>(), out string? reason);

            Assert.False(removed);
            Assert.Contains("taxes.xlsx", reason);
            Assert.True(File.Exists(Path.Combine(Profile, "taxes.xlsx")));
        }

        [Fact]
        public void AFolderThatHoldsAFolderToKeep_IsRefusedAndLeftAlone()
        {
            LayProfile();
            string game = Path.Combine(Profile, "game");
            Directory.CreateDirectory(game);

            bool removed = ProfileRemoval.TryRemove(Profile, new[] { game }, out string? reason);

            Assert.False(removed);
            Assert.Contains(game, reason);
            Assert.True(File.Exists(Path.Combine(Profile, "BepInEx", "plugins", "mod.dll")));
        }

        [Fact]
        public void AFolderThatIsAFolderToKeep_IsRefused()
        {
            LayProfile();

            Assert.False(ProfileRemoval.TryRemove(Profile, new[] { Profile }, out _));
            Assert.True(Directory.Exists(Profile));
        }

        [Fact]
        public void AFolderInsideAFolderToKeep_IsStillRemoved()
        {
            LayProfile();

            Assert.True(ProfileRemoval.TryRemove(Profile, new[] { _base }, out _));
        }

        [Fact]
        public void ADriveRoot_IsRefused()
        {
            string root = Path.GetPathRoot(_base)!;

            Assert.False(ProfileRemoval.TryRemove(root, Array.Empty<string>(), out string? reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void NoPath_IsRefused()
        {
            Assert.False(ProfileRemoval.TryRemove("", Array.Empty<string>(), out string? reason));
            Assert.NotNull(reason);
        }
    }
}
