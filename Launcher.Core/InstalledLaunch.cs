namespace Odinsons.ValheimLauncher
{
    /// <summary>How to start the game from files that are already installed, without checking or updating anything.</summary>
    public static class InstalledLaunch
    {
        /// <param name="gameFolder">The game folder set in Install settings, if any.</param>
        /// <param name="steamFolder">The Steam install of the game, if it is complete.</param>
        /// <param name="plan">The injector plan, or null when the game copy in the profile folder starts as it is.</param>
        public static bool TryResolve(string profileFolder, string gameFolder, string steamFolder,
            out InjectorPlan plan, out string reason)
        {
            plan = null;
            reason = null;

            if (!string.IsNullOrWhiteSpace(gameFolder)
                && !LauncherPaths.AreSame(gameFolder, profileFolder)
                && !(steamFolder != null && LauncherPaths.AreSame(gameFolder, steamFolder)))
                return InjectorLauncher.TryPrepareLaunch(gameFolder, profileFolder, out plan, out reason);

            if (InjectorLauncher.ResolveGameExecutable(profileFolder) != null) return true;

            if (!string.IsNullOrWhiteSpace(steamFolder))
                return InjectorLauncher.TryPrepareLaunch(steamFolder, profileFolder, out plan, out reason);

            reason = Loc.T("gui.valheimExeNotFound", InjectorLauncher.PrimaryExecutableName, profileFolder);
            return false;
        }
    }
}
