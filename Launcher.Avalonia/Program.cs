using System;
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
    }
}
