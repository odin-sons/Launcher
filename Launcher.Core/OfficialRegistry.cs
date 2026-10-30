using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    /// <summary>The official registry as this launcher build uses it: the profile choices, from the
    /// cached index at startup and from the network afterwards.</summary>
    public static class OfficialRegistry
    {
        public static readonly string[] Mirrors = Array.Empty<string>();

        public static bool IsInstalled(string baseFolder, string profileId) =>
            Directory.Exists(Path.Combine(baseFolder, "clients", profileId));

        public static IReadOnlyList<ProfileChoice> CachedChoices(string baseFolder) =>
            ProfileCatalog.Build(Client(baseFolder).LoadCached(), id => IsInstalled(baseFolder, id));

        /// <summary>Loads a fresh index, disables every installed profile it suspends, and returns the choices.</summary>
        public static async Task<IReadOnlyList<ProfileChoice>> RefreshAsync(string baseFolder, CancellationToken ct = default)
        {
            RegistryLoadResult load = await Client(baseFolder).LoadAsync(ct).ConfigureAwait(false);
            Access(baseFolder).ApplySuspensions(load, id => InstalledFolders(baseFolder, id));
            return ProfileCatalog.Build(load, id => IsInstalled(baseFolder, id));
        }

        /// <summary>The theme the cached index pins for a profile (the default theme when the entry names none); null when the profile is not in the cached index.</summary>
        public static async Task<LoadedTheme> LoadThemeAsync(string baseFolder, string profileId, CancellationToken ct = default)
        {
            RegistryEntry entry = Client(baseFolder).LoadCached()?.Index?.Entries.FirstOrDefault(e => e.Id == profileId);
            if (entry == null)
                return null;

            var files = new DisplayFiles(entry, null, new DisplayFileCache(Path.Combine(baseFolder, "registry", "display")));
            return await ThemeLoader.LoadAsync(entry, files, ct).ConfigureAwait(false);
        }

        public static ServerAccess Access(string baseFolder) =>
            new ServerAccess(new ServerGate(Client(baseFolder)), new ProfileQuarantine());

        private static IReadOnlyList<string> InstalledFolders(string baseFolder, string profileId)
        {
            IniFile config = null;
            try
            {
                config = new IniFile(Path.Combine(baseFolder, "config.ini"));
                config.Load();
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Could not read config.ini for the profile folders", ex);
            }
            return LauncherPaths.ExistingProfileFolders(config, baseFolder, profileId);
        }

        private static RegistryClient Client(string baseFolder) =>
            new RegistryClient(Mirrors, RegistryKeySet.Official, new RegistryCache(Path.Combine(baseFolder, "registry")),
                Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(3, 0, 0));
    }
}
