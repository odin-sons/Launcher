using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>
    /// FileHashCache skips rehashing a file whose size and exact write time haven't changed
    /// since it was last recorded — the same technique Indexer's own HashCache already uses on
    /// the build side, applied here to the launcher's own per-run file check.
    /// </summary>
    public class FileHashCacheTests
    {
        private static string TempFolder() => Directory.CreateTempSubdirectory("odinsons-hashcache-").FullName;

        private static string CacheFile(string folder) => Path.Combine(folder, "filehashes.cache");

        private static string WriteFile(string folder, string name, string content)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void FreshCache_FirstLookup_IsAMiss()
        {
            string folder = TempFolder();
            string file = WriteFile(folder, "mod.dll", "content");

            var cache = FileHashCache.Load(CacheFile(folder));
            string hash = cache.GetHash("BepInEx/plugins/Mod/mod.dll", file);

            Assert.Equal(FileHash.OfFile(file), hash);
            Assert.Equal(0, cache.Hits);
            Assert.Equal(1, cache.Misses);
        }

        [Fact]
        public void UnchangedFile_AfterSaveAndReload_IsACacheHit()
        {
            string folder = TempFolder();
            string file = WriteFile(folder, "mod.dll", "content");
            string expectedHash = FileHash.OfFile(file);

            var first = FileHashCache.Load(CacheFile(folder));
            first.GetHash("BepInEx/plugins/Mod/mod.dll", file);
            first.Save();

            var second = FileHashCache.Load(CacheFile(folder));
            string hash = second.GetHash("BepInEx/plugins/Mod/mod.dll", file);

            Assert.Equal(expectedHash, hash);
            Assert.Equal(1, second.Hits);
            Assert.Equal(0, second.Misses);
        }

        [Fact]
        public void FileSizeChanged_CacheMiss_ReturnsTheNewHash()
        {
            string folder = TempFolder();
            string file = WriteFile(folder, "mod.dll", "content");

            var first = FileHashCache.Load(CacheFile(folder));
            first.GetHash("BepInEx/plugins/Mod/mod.dll", file);
            first.Save();

            File.WriteAllText(file, "different content, different length");
            string expectedHash = FileHash.OfFile(file);

            var second = FileHashCache.Load(CacheFile(folder));
            string hash = second.GetHash("BepInEx/plugins/Mod/mod.dll", file);

            Assert.Equal(expectedHash, hash);
            Assert.Equal(0, second.Hits);
            Assert.Equal(1, second.Misses);
        }

        [Fact]
        public void SameSizeButNewerWriteTime_CacheMiss_ReturnsTheNewHash()
        {
            // Same length, different bytes — the case a bare size check alone would miss.
            string folder = TempFolder();
            string file = WriteFile(folder, "mod.dll", "aaaaaaa");

            var first = FileHashCache.Load(CacheFile(folder));
            first.GetHash("BepInEx/plugins/Mod/mod.dll", file);
            first.Save();

            File.WriteAllText(file, "bbbbbbb");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
            string expectedHash = FileHash.OfFile(file);

            var second = FileHashCache.Load(CacheFile(folder));
            string hash = second.GetHash("BepInEx/plugins/Mod/mod.dll", file);

            Assert.Equal(expectedHash, hash);
            Assert.Equal(0, second.Hits);
            Assert.Equal(1, second.Misses);
        }

        [Fact]
        public void Save_OnlyKeepsEntriesSeenThisRun_UnseenEntriesAreDropped()
        {
            string folder = TempFolder();
            string fileA = WriteFile(folder, "a.dll", "a-content");
            string fileB = WriteFile(folder, "b.dll", "b-content");

            var first = FileHashCache.Load(CacheFile(folder));
            first.GetHash("a.dll", fileA);
            first.GetHash("b.dll", fileB);
            first.Save();

            // Second run only ever looks at fileA (e.g. b.dll left BepInEx/config, which an
            // ordinary run skips) — its cache entry must not survive into the next save.
            var second = FileHashCache.Load(CacheFile(folder));
            second.GetHash("a.dll", fileA);
            second.Save();

            var third = FileHashCache.Load(CacheFile(folder));
            third.GetHash("b.dll", fileB);

            Assert.Equal(0, third.Hits);
            Assert.Equal(1, third.Misses);
        }

        [Fact]
        public void MissingOrCorruptCacheFile_LoadsEmptyInsteadOfThrowing()
        {
            string folder = TempFolder();
            string file = WriteFile(folder, "mod.dll", "content");
            File.WriteAllText(CacheFile(folder), "this is not a hash cache");

            var cache = FileHashCache.Load(CacheFile(folder));
            cache.GetHash("mod.dll", file);

            Assert.Equal(0, cache.Hits);
            Assert.Equal(1, cache.Misses);
        }
    }
}
