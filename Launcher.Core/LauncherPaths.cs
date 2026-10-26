using System;
using System.IO;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Where a player keeps things on disk, read from and written to config.ini.
    ///
    /// The profile folder is the server's mod set (BepInEx, plugins, settings) and differs from
    /// one server to the next, so it is stored per server; unset, it is <c>clients/&lt;server&gt;</c> for the built-in profile and
    /// <c>clients/&lt;profile&gt;/&lt;server&gt;</c> for any other.
    /// The game folder is one for all servers; unset, the game runs from the Steam install when its
    /// version matches the server and otherwise lives in the profile folder.
    /// </summary>
    public static class LauncherPaths
    {
        public const string GameSection = "Paths";
        public const string GameKey = "GamePath";
        public const string ProfileKey = "ProfilePath";

        public const string SelectedServerKey = "SelectedServer";
        private const string LegacySettingsSection = "Settings";

        // The built-in profile keeps the 2.x layout, so existing installs stay where they are.
        private static bool IsBuiltIn(ServerProfile profile) =>
            string.Equals(profile.Id, ServerProfile.Default.Id, StringComparison.Ordinal);

        private static string ServerSection(ServerProfile profile, string server) =>
            IsBuiltIn(profile) ? "Server:" + server : $"Server:{profile.Id}/{server}";

        public static string DefaultProfileFolder(string server) =>
            DefaultProfileFolder(ServerProfile.Default, server);

        public static string DefaultProfileFolder(ServerProfile profile, string server) =>
            IsBuiltIn(profile) ? Path.Combine("clients", server) : Path.Combine("clients", profile.Id, server);

        public static string ProfileFolder(IniFile config, string server) =>
            ProfileFolder(config, ServerProfile.Default, server);

        public static string ProfileFolder(IniFile config, ServerProfile profile, string server) =>
            Clean(config.Read(ProfileKey, ServerSection(profile, server))) ?? DefaultProfileFolder(profile, server);

        public static string SelectedServer(IniFile config, ServerProfile profile) =>
            Clean(config.Read(SelectedServerKey, SelectedServerSection(profile)));

        public static void SetSelectedServer(IniFile config, ServerProfile profile, string server) =>
            config.Write(SelectedServerKey, server?.Trim() ?? string.Empty, SelectedServerSection(profile));

        public static System.Threading.Tasks.Task SetSelectedServerAsync(IniFile config, ServerProfile profile, string server) =>
            config.WriteAsync(SelectedServerKey, server?.Trim() ?? string.Empty, SelectedServerSection(profile));

        private static string SelectedServerSection(ServerProfile profile) =>
            IsBuiltIn(profile) ? LegacySettingsSection : "Profile:" + profile.Id;

        public const string SelectedProfileKey = "SelectedProfile";

        public static string SelectedProfileId(IniFile config) =>
            Clean(config.Read(SelectedProfileKey, LegacySettingsSection));

        public static void SetSelectedProfile(IniFile config, ServerProfile profile) =>
            config.Write(SelectedProfileKey, profile.Id, LegacySettingsSection);

        public static System.Threading.Tasks.Task SetSelectedProfileAsync(IniFile config, ServerProfile profile) =>
            config.WriteAsync(SelectedProfileKey, profile.Id, LegacySettingsSection);

        public static string GameFolder(IniFile config) => Clean(config.Read(GameKey, GameSection));

        /// <param name="path">Null or empty goes back to the default.</param>
        public static void SetProfileFolder(IniFile config, string server, string path) =>
            SetProfileFolder(config, ServerProfile.Default, server, path);

        public static void SetProfileFolder(IniFile config, ServerProfile profile, string server, string path) =>
            config.Write(ProfileKey, path?.Trim() ?? string.Empty, ServerSection(profile, server));

        /// <param name="path">Null or empty goes back to the default.</param>
        public static void SetGameFolder(IniFile config, string path) =>
            config.Write(GameKey, path?.Trim() ?? string.Empty, GameSection);

        /// <summary>
        /// Whether a folder can be taken for the game or a profile: writable, and either empty or
        /// already a Valheim client / ours — never a folder with someone else's files in it.
        /// </summary>
        public static bool CanUse(string folder, out string reason)
        {
            if (!ClientFolderGuard.IsWritable(folder, out string writeReason, out string advice))
            {
                reason = Loc.T("path.rejected", $"{writeReason} {advice}");
                return false;
            }

            if (!ClientFolderGuard.IsSafeTarget(folder, out string unsafeReason))
            {
                reason = Loc.T("path.rejected", unsafeReason);
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>Whether one folder lies inside the other (the same folder is not nesting).</summary>
        public static bool AreNested(string a, string b)
        {
            string first = Normalize(a);
            string second = Normalize(b);
            StringComparison comparison = RuntimePlatform.IsLinux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (string.Equals(first, second, comparison)) return false;

            return first.StartsWith(second + Path.DirectorySeparatorChar, comparison)
                || second.StartsWith(first + Path.DirectorySeparatorChar, comparison);
        }

        public static bool AreSame(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b),
                RuntimePlatform.IsLinux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
