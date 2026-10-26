using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Odinsons.ValheimLauncher.Avalonia.Views;

namespace Odinsons.ValheimLauncher.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                string baseFolder = Environment.CurrentDirectory;
                IReadOnlyList<ProfileChoice> choices = OfficialRegistry.CachedChoices(baseFolder);
                var config = new IniFile(Path.Combine(baseFolder, "config.ini"));
                config.Load();
                ProfileChoice current = ProfileCatalog.Resolve(LauncherPaths.SelectedProfileId(config), choices);
                desktop.MainWindow = new MainWindow(current.Profile, choices);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
