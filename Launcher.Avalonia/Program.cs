using System;
using System.IO;
using Avalonia;

namespace Odinsons.ValheimLauncher.Avalonia
{
    internal static class Program
    {
        /// <summary>Held for the app's lifetime once acquired; MainWindow subscribes to
        /// ActivateRequested to bring itself to the foreground on a second launch attempt.</summary>
        public static SingleInstanceGuard? InstanceGuard { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            // The elevated re-launch InjectorLauncher.EnsureGameFolderPrepared spawns (via
            // "runas") when the Steam game folder isn't writable by the current user — a short
            // one-shot helper invocation, not the normal app. No GUI, no SingleInstanceGuard
            // (it's a separate elevated process, not "another copy of the launcher"): do the
            // one privileged file operation and exit with a status code the un-elevated parent
            // reads back. Checked first, before anything else in this Main starts up.
            if (args.Length == 3 && args[0] == InjectorLauncher.ElevatedHelperArg)
            {
                Environment.Exit(InjectorLauncher.RunElevatedHelperMain(args[1], args[2]));
                return;
            }

            SetWorkingDirectory();

            InstanceGuard = SingleInstanceGuard.TryBecomePrimary(Environment.CurrentDirectory);
            if (InstanceGuard is null) return; // another instance is already running and was pinged

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                InstanceGuard.Dispose();
            }
        }

        // No .WithInterFont(): every window in this app sets its own FontFamily (Montserrat,
        // which covers Cyrillic too) directly, so the bundled Inter font is pure dead weight —
        // ~2MB uncompressed pulled into every self-contained build for a typeface nothing uses.
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        /// <summary>
        /// The launcher keeps its data (config.ini, clients/, launcher_log.txt) next to
        /// <see cref="Environment.CurrentDirectory"/>. On Windows that's the .exe folder, as
        /// before. A macOS .app (or a bare binary) opened from Finder starts with the working
        /// directory at "/", so point it at a per-user data directory instead.
        /// </summary>
        private static void SetWorkingDirectory()
        {
            if (OperatingSystem.IsWindows()) return;

            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dataDir = OperatingSystem.IsMacOS()
                    ? Path.Combine(home, "Library", "Application Support", "OdinsonsLauncher")
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OdinsonsLauncher");

                Directory.CreateDirectory(dataDir);
                Directory.SetCurrentDirectory(dataDir);
            }
            catch
            {
                // Keep whatever directory the process was handed.
            }
        }
    }
}
