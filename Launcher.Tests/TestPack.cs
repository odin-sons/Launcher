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

        // ---- Per-OS game files -------------------------------------------------
        //
        // The launcher fetches game.info on Windows, game_macos.info on macOS,
        // game_linux.info on Linux — the layouts don't overlap. Tests write whichever
        // one the running OS will actually ask for, and place the game executable under
        // the name that OS launches (valheim.exe / valheim.app bundle / valheim.x86_64).

        /// <summary>The game-manifest name the launcher requests on the running OS.</summary>
        public static string GameManifestName =>
            RuntimePlatform.IsMacOS ? "game_macos.info" :
            RuntimePlatform.IsLinux ? "game_linux.info" : "game.info";

        public void WriteGameManifest(params string[] relativePaths) =>
            WriteManifest(GameManifestName, relativePaths);

        public void WriteEmptyGameManifest() => WriteEmptyManifest(GameManifestName);

        /// <summary>
        /// Adds the game executable the way Steam ships it for the running OS: a plain file
        /// on Windows/Linux, a minimal <c>.app</c> bundle on macOS. Returns the manifest-facing
        /// relative path(s) that describe it.
        /// </summary>
        public string[] AddGameExecutable(string content = "GAME EXE")
        {
            if (RuntimePlatform.IsMacOS)
            {
                AddFile("valheim.app/Contents/Info.plist",
                    "<?xml version=\"1.0\"?><plist><dict>" +
                    "<key>CFBundleExecutable</key><string>Valheim</string></dict></plist>");
                AddFile("valheim.app/Contents/MacOS/Valheim", content);
                return new[] { "valheim.app/Contents/Info.plist", "valheim.app/Contents/MacOS/Valheim" };
            }

            AddFile(InjectorLauncher.PrimaryExecutableName, content);
            return new[] { InjectorLauncher.PrimaryExecutableName };
        }

        /// <summary>Where <see cref="InjectorLauncher"/> resolves the game executable inside a folder.</summary>
        public static string GameExecutablePath(string folder) =>
            InjectorLauncher.ResolveGameExecutable(folder);

        /// <summary>
        /// The mod-side file the injector needs next to <c>BepInEx.Preloader.dll</c>: the winhttp.dll
        /// proxy on Windows, the native Doorstop library on macOS/Linux. Returns its relative path.
        /// </summary>
        public string AddDoorstopPrereq()
        {
            string rel = RuntimePlatform.IsWindows
                ? "winhttp.dll"
                : InjectorLauncher.DoorstopLibraryRelativePath;
            AddFile(rel, "native doorstop bits");
            return rel;
        }

        public void WriteForceCheck(params string[] relativePaths) =>
            File.WriteAllText(Path.Combine(Root, "force_check_files.txt"), string.Join('\n', relativePaths));

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* temp folder, not critical */ }
        }
    }
}
