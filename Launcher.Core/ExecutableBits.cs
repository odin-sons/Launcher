using System;
using System.IO;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Downloaded files are created without the execute bit, and the manifest carries no file
    /// modes. Scripts and native libraries of the BepInEx/Doorstop packs need it on macOS/Linux.
    /// </summary>
    public static class ExecutableBits
    {
        public static bool NeedsExecuteBit(string relativePath)
        {
            string extension = Path.GetExtension(relativePath);

            return extension.Equals(".sh", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".so", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adds the execute bit where the file already has the matching read bit. Never throws.</summary>
        public static void Ensure(string fullPath, string relativePath)
        {
            if (OperatingSystem.IsWindows() || !NeedsExecuteBit(relativePath)) return;

            try
            {
                UnixFileMode mode = File.GetUnixFileMode(fullPath);
                UnixFileMode wanted = mode;

                if ((mode & UnixFileMode.UserRead) != 0) wanted |= UnixFileMode.UserExecute;
                if ((mode & UnixFileMode.GroupRead) != 0) wanted |= UnixFileMode.GroupExecute;
                if ((mode & UnixFileMode.OtherRead) != 0) wanted |= UnixFileMode.OtherExecute;

                if (wanted != mode) File.SetUnixFileMode(fullPath, wanted);
            }
            catch (Exception ex)
            {
                LauncherLog.WarnOnce("exec-bit-failed", $"could not set the execute bit on {relativePath}: {ex.Message}", ex);
            }
        }
    }
}
