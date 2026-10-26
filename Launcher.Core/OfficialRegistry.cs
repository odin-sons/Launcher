using System;
using System.Collections.Generic;
using System.IO;
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

        public static async Task<IReadOnlyList<ProfileChoice>> FreshChoicesAsync(string baseFolder, CancellationToken ct = default) =>
            ProfileCatalog.Build(await Client(baseFolder).LoadAsync(ct).ConfigureAwait(false),
                id => IsInstalled(baseFolder, id));

        private static RegistryClient Client(string baseFolder) =>
            new RegistryClient(Mirrors, RegistryKeySet.Official, new RegistryCache(Path.Combine(baseFolder, "registry")),
                Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(3, 0, 0));
    }
}
