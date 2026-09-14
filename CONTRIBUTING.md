# Contributing

## Project structure

| project | purpose |
|---|---|
| `Launcher.Avalonia` | primary GUI launcher (Avalonia, cross-platform: Windows/macOS/Linux) |
| `Launcher` | legacy GUI launcher (WPF, Windows-only) — superseded by `Launcher.Avalonia`, no longer where development happens |
| `Launcher.Cli` | console version of the same update pipeline — Windows/Linux/macOS |
| `Launcher.Core` | shared logic: downloading, manifest verification, Steam detection, injector mode, client folder safety, localization |
| `Indexer` | server-side tool — lays out a build into the manifests the launcher consumes |
| `Launcher.Avalonia.Tests` | headless smoke tests for `Launcher.Avalonia` (constructs `MainWindow` via Avalonia.Headless to catch runtime-only XAML errors) |
| `Launcher.Tests` | tests for `Launcher.Core` — see [its README](Launcher.Tests/README.md) for what each one covers |
| `Indexer.Tests` | tests for `Indexer` — see [its README](Indexer.Tests/README.md) for what each one covers |

`Launcher.Avalonia`, `Launcher` and `Launcher.Cli` are front ends over the same
`Launcher.Core` pipeline — none of them duplicates the download/verification/injector
logic. Keep it that way: a fix belongs in `Launcher.Core` unless it's genuinely specific
to one front end's UI.

## Requirements

.NET SDK 9.0+. `Launcher` (the WPF GUI) only builds on Windows; the rest of the solution,
including `Launcher.Avalonia`, is cross-platform.

## Build

```bash
dotnet build Launcher.Core/Launcher.Core.csproj
dotnet build Launcher.Avalonia/Launcher.Avalonia.csproj
dotnet build Launcher.Cli/Launcher.Cli.csproj
dotnet build Launcher/Valheim-Online_Launcher.csproj   # Windows only (WPF, legacy)
dotnet build Indexer/Indexer.csproj
```

Or build everything at once from the solution root:

```bash
dotnet build Odinsons.ValheimLauncher.sln
```

A release build of `Launcher.Avalonia`, as a single distributable file:

```bash
Launcher.Avalonia/build-windows/build-windows.ps1   # Windows: dist/OdinsonsLauncher.exe
Launcher.Avalonia/build-macos.sh                    # macOS: a .app bundle
```

The legacy WPF `Launcher` builds the same way, just with plain `dotnet publish`:

```bash
dotnet publish Launcher/Valheim-Online_Launcher.csproj -c Release -r win-x64 --self-contained false
```

The result lands in `Launcher/bin/Release/net9.0-windows/win-x64/publish/OdinsonsLauncher.exe`
— this is genuinely one file with everything bundled in (`PublishSingleFile`); a plain
`dotnet build` output is **not** portable on its own, it depends on loose DLLs sitting next
to it in the same folder.

## Tests

```bash
dotnet test Launcher.Tests/Launcher.Tests.csproj
dotnet test Launcher.Avalonia.Tests/Launcher.Avalonia.Tests.csproj
dotnet test Indexer.Tests/Indexer.Tests.csproj
```

`Launcher.Tests` and `Indexer.Tests` run in CI via GitHub Actions on every push/PR;
`Launcher.Avalonia.Tests` isn't wired into CI yet, run it locally. See each project's own README
for what every individual test actually checks — worth reading before touching
`FileDownloader`, `InjectorLauncher`, `ClientLedger`, or the `Indexer` rule-matching logic,
since most of those tests exist specifically to pin down a past regression.

## Running it locally

- **CLI**: `dotnet run --project Launcher.Cli -- --url https://your-test-server/launcher/`
  points the whole pipeline at a specific mirror instead of auto-detecting one — the
  fastest way to exercise real download/verification logic against a build you control,
  without touching the GUI at all.
- **GUI**: the mirror list is hardcoded in `Launcher.Core/ServerModels.cs`
  (`LauncherMirrors.Default`) — there's no runtime override yet. For local testing against
  a different server, point it there temporarily and don't commit that change.

## Code tour

A short map of the pieces most contributions touch, in `Launcher.Core`:

- **`FileDownloader`** — the update pipeline itself: reads the manifests, decides what
  needs downloading, downloads with retry/backoff, cleans up extra files.
- **`Manifest`** — the text format (`MANIFEST 3 sha256`) shared by every `.info` file the
  server publishes and the launcher reads.
- **`ClientLedger`** — the "what did I already confirm was correct last run" heuristic
  (`verified.info`) that tells apart a genuine server-side update from a file quietly
  corrupted on the player's disk.
- **`InjectorLauncher`** / **`GameFolderInspector`** / **`SteamLocator`** — Steam injector
  mode: finding a Steam install, verifying its build actually matches the server, and
  preparing the Doorstop launch without copying the game.
- **`UpdateSession`** — the lock that keeps two updates (or an update and a launch) from
  running against the same client folder at once; also the thing that stashes and restores
  `valheim.exe` for the duration of an update.
- **`ClientFolderGuard`** — the "is this folder safe to operate on" and "do we actually
  have write access" checks that run before anything else does.
- **`HttpRetry`** — the shared backoff policy (exponential + jitter, honors `Retry-After`)
  for HTTP 429/503 against our own server. Used by both the downloader and the mirror
  check — extend this rather than adding a second ad hoc retry loop somewhere else.
- **`Loc`** — localization. To add an 11th language: add its code to `Loc.Supported` and its
  native name to `Loc.DisplayNames`, then add one more optional parameter to the private
  `L(...)` helper (defaulted to `null`, so every existing call site keeps compiling) and
  pass it through. Existing keys don't all need translating at once — a missing key for a
  given language falls back to English automatically.

## Conventions

- Code, comments, commit messages, and all docs in this repo are written in English,
  regardless of what language the accompanying conversation happens to be in.
- Keep `Launcher.Tests`/`Indexer.Tests`'s README files in sync when adding or renaming a
  test — they're meant to be read before the test source itself, not after.
