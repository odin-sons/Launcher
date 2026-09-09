using System;
using System.IO;
using Avalonia;

namespace Odinsons.ValheimLauncher.Avalonia
{
    internal static class Program
    {
        /// <summary>Held for the app's lifetime once acquired; MainWindow subscribes to
        /// ActivateRequested to bring itself to the foreground on a second launch attempt.</summary>
        public static SingleInstanceGuard InstanceGuard { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
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

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
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
