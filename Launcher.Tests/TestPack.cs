using Odinsons.ValheimLauncher;

namespace Launcher.Tests
{
    /// <summary>
    /// Builds a temporary build folder for a test: places files with the given
    /// content and writes manifests from their real hashes — using the same classes
    /// (<see cref="Manifest"/>, <see cref="FileHash"/>) that the Indexer uses,
    /// so the test checks exactly what the launcher will actually read.
    /// </summary>
    public sealed class TestPack : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("odinsons-pack-").FullName;

        private readonly Dictionary<string, string> _relativeToFull = new(StringComparer.OrdinalIgnoreCase);

        public TestPack AddFile(string relativePath, string content)
        {
            relativePath = relativePath.Replace('\\', '/');
            string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            _relativeToFull[relativePath] = full;

            return this;
        }

        public Manifest.Entry EntryFor(string relativePath)
        {
            string full = _relativeToFull[relativePath];
            var info = new FileInfo(full);
            return new Manifest.Entry(relativePath, FileHash.OfFile(full), info.Length);
        }

        public void WriteManifest(string manifestName, params string[] relativePaths) =>
            Manifest.WriteFile(Path.Combine(Root, manifestName), relativePaths.Select(EntryFor));

        /// <summary>An empty manifest — for optional.info, when a test has no optional mods.</summary>
        public void WriteEmptyManifest(string manifestName) =>
            Manifest.WriteFile(Path.Combine(Root, manifestName), Array.Empty<Manifest.Entry>());

        public void WriteForceCheck(params string[] relativePaths) =>
            File.WriteAllText(Path.Combine(Root, "force_check_files.txt"), string.Join('\n', relativePaths));

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* temp folder, not critical */ }
        }
    }
}
