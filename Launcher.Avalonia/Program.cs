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
        /// <see cref="Environment.CurrentDirectory"/>, so this needs to reliably land on the
        /// .exe's own folder regardless of how the process was started.
        ///
        /// On Windows, double-clicking OdinsonsLauncher.exe directly gets this right for free —
        /// Explorer sets the CWD to the exe's folder. A .url shortcut (Desktop/Start Menu,
        /// chosen over .lnk specifically to avoid COM interop — see UpdateUrlShortcut) has no
        /// "Start in" field at all, unlike .lnk, and leaves the CWD wherever the shell happens
        /// to put it — reported as the "admin" marker file and the server list behaving
        /// differently launched from a shortcut vs the exe directly (LoadServersAsync's
        /// isAdmin check and config.ini both read relative to CurrentDirectory). Pinning it to
        /// AppContext.BaseDirectory here, unconditionally, makes every launch path identical.
        ///
        /// A macOS .app (or a bare binary) opened from Finder starts with the working directory
        /// at "/" instead, so that platform points at a per-user data directory rather than the
        /// bundle's own (often read-only, code-signed) folder.
        /// </summary>
        private static void SetWorkingDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                try { Directory.SetCurrentDirectory(AppContext.BaseDirectory); }
                catch { /* keep whatever directory the process was handed */ }
                return;
            }

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
