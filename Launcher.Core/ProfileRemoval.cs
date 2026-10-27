using System;
using System.Collections.Generic;
using System.IO;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Removes a profile folder, only when it passes the same checks as any folder the launcher works in.</summary>
    public static class ProfileRemoval
    {
        /// <param name="keep">Folders that must survive: the launcher's own, the game folder, the Steam install.</param>
        public static bool TryRemove(string profileFolder, IEnumerable<string> keep, out string reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(profileFolder))
            {
                reason = Loc.T("guard.noPath");
                return false;
            }

            string folder = Path.GetFullPath(profileFolder);

            if (string.Equals(Path.GetPathRoot(folder), folder, StringComparison.OrdinalIgnoreCase))
            {
                reason = Loc.T("gate.remove.root");
                return false;
            }

            if (!Directory.Exists(folder)) return true;

            foreach (string other in keep)
            {
                if (string.IsNullOrWhiteSpace(other)) continue;

                if (LauncherPaths.AreSame(folder, other) || LauncherPaths.IsInside(other, folder))
                {
                    reason = Loc.T("gate.remove.holdsOther", Path.GetFullPath(other));
                    return false;
                }
            }

            if (!LauncherPaths.CanUse(folder, out reason)) return false;

            try
            {
                Directory.Delete(folder, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}
