# Per-OS game manifests & re-indexing for a new Valheim release

## What these files are

The launcher never ships the game itself. It ships the mod set (`update.info`) and,
separately, a **manifest of the vanilla game files** — hashes and sizes only, no content:

| Player OS | File the launcher fetches | Describes |
|-----------|---------------------------|-----------|
| Windows   | `game.info`               | `valheim.exe`, `valheim_Data/**`, `MonoBleedingEdge/**`, … |
| macOS     | `game_macos.info`         | `valheim.app/Contents/**` |
| Linux     | `game_linux.info`         | `valheim.x86_64`, `valheim_Data/**`, … |

The layouts don't overlap, so there's one file per OS. On non-Windows there is **no
fallback to `game.info`** — a Windows manifest can never match a mac/Linux Steam install
and would drag the whole unusable Windows build in as an ordinary download.

The launcher uses the manifest only to answer *"is the player's Steam copy exactly the
build this mod set was made for?"* (`GameFilesMatchSteamInstall`). If yes → injector mode:
the game runs in place from Steam, nothing is downloaded. If no (Steam auto-updated ahead
of us, or a corrupt file) → it falls back to the classic download path.

## Why this matters for a game update (e.g. 2026-09-09)

When Valheim releases a new version, **Steam auto-updates every player's local copy**.
Their `assembly_valheim.dll` / `globalgamemanagers` / etc. change. The moment that
happens, the server's manifests describe the *old* build:

* `GameFilesMatchSteamInstall` starts returning **false** for everyone,
* injector mode stops engaging,
* the launcher falls back to downloading — on macOS/Linux that download can't even run.

So the manifests must be regenerated from the **new vanilla build** and published
**at or before** the game release. Every player is on the same server build, so there's
exactly one set of manifests to keep current.

## How to (re)generate one manifest

The Indexer takes `--game-manifest <name>` (default `game.info`). Run it in a build folder
that contains a **vanilla** install for that OS plus the rule files:

```bash
# in a folder holding an untouched Valheim install for the target OS
#   game_files.txt      ->  the game's top-level dirs/files (e.g. "valheim.app/")
#   ignore_patterns.txt ->  empty (nothing to exclude in a vanilla folder)
Indexer --game-manifest game_macos.info --no-cache
```

Output: `game_macos.info` (the game files) plus a throwaway `update.info` you discard.
Do the same on/for each OS → `game.info`, `game_macos.info`, `game_linux.info`.
Upload all three to the server directory next to `update.info`.

`game_macos.info` for the current release (Valheim 0.221.12, build 21981559) is already
generated — 819 files, `valheim.app/**`.

## TODO — make release day turnkey

Not built yet; do before 2026-09-09:

- [ ] A `tools/reindex-game.sh` (or CI job) that, given paths to three vanilla installs
      (Win / macOS / Linux — a Steam Depot download or a clean copy kept aside, *not* a
      modded r2modman folder), regenerates all three manifests and stages them for upload.
- [ ] Decide where the vanilla reference installs live (a locked Steam library folder,
      or `steamcmd +download_depot` in CI: appid `892970`, depots `892971` Win /
      `892973` macOS / `892975` Linux — confirm depot ids).
- [ ] Server publish step: drop the three files, bump nothing else. Injector clients pick
      them up on next launch; the check is the tiny manifest, not a re-download.
- [ ] Optional: a `min_game_build` marker so the launcher can tell "you're on an older
      Valheim than this server build — update Steam" apart from "you're ahead of us".
- [ ] Timing: regenerate against the release build the day it ships (or from the Steam
      beta/branch if the build is available early) so injector mode never breaks for a
      window after the update.
