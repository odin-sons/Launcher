using System.Runtime.CompilerServices;

// Exposes internal members (BuildActionButtons) to Launcher.Avalonia.Tests, so the headless
// smoke test can exercise it directly without firing Loaded — Loaded also kicks off real
// network calls (InitializeLauncherUrlAsync etc.), which the smoke test deliberately avoids.
[assembly: InternalsVisibleTo("Launcher.Avalonia.Tests")]
