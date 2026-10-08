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

The Indexer takes `--game-manifest <name>` (default `game.info`). Run it in a profile folder
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
      or `steamcmd +download_depot` in CI). Depot ids for appid `892970`, **all
      confirmed 2026-09-10** via `steamcmd +app_info_print 892970` (the two earlier
      guesses, `892971` Win and `892975` Linux, were both wrong):
      - `892972` — Windows
      - `892973` — macOS
      - `892971` — Linux
      **Do not reuse `D:\SteamLibrary\steamapps\common\Valheim`** as the Windows
      reference even though it has depot `892972` installed — it's got half a dozen
      old `BepInEx*` folders, `doorstop_config.ini`/`doorstop_libs`, and a GreyDwarf
      launcher sitting in it, i.e. exactly the "modded r2modman folder" this TODO
      already warns against. Get a fresh copy instead — steamcmd is set up at
      `D:\Program Files\SteamCMD\steamcmd.exe`:
      `steamcmd +login anonymous +force_install_dir <fresh empty folder> +app_update 892970 validate +quit`
      (swap in the depot's own app id if download_depot is used directly instead).
- [ ] Server publish step: drop the three files, bump nothing else. Injector clients pick
      them up on next launch; the check is the tiny manifest, not a re-download.
- [ ] Optional: a `min_game_build` marker so the launcher can tell "you're on an older
      Valheim than this server build — update Steam" apart from "you're ahead of us".
- [ ] Timing: regenerate against the release build the day it ships (or from the Steam
      beta/branch if the build is available early) so injector mode never breaks for a
      window after the update.

## Keeping the game apart from the mod profile

The game doesn't have to sit inside the mod profile folder. Put each version in its own folder
named after its Steam depot and manifest, at the site root, one level above `Launcher`:

```
<site root>/
  Launcher/
    Indexer.exe
    Lite_v2/                  mod profile: update.info, game.info, ...
  Game/892972_<manifestid>/   vanilla Windows files (depot 892972)
```

`game.info` then carries the version in its header, and the launcher downloads the manifest's
files from `Game/<version>/` on the same host as the mirror it is using:

```
MANIFEST 3 sha256
# game: 892972_<manifestid>
<hash> <size> valheim.exe
```

The launcher builds that address itself and accepts only a plain `<depot>_<manifest>` name, so
the manifest can't point it elsewhere. Without the directive, game files are served from the
profile folder as before. Older launchers read the line as a comment. Every mirror has to serve
`/Game/` as well as `/Launcher/`.

### Indexing

There is one folder per Steam depot, and the depot decides the manifest it produces:

| Depot | OS | Manifest |
|-------|----|----------|
| 892972 | Windows | `game.info` |
| 892973 | macOS | `game_macos.info` |
| 892971 | Linux | `game_linux.info` |

```bash
Indexer --profile Lite_v2 \
  --game-root ../Game/892972_<manifestid> \
  --game-root ../Game/892973_<manifestid> \
  --game-root ../Game/892971_<manifestid>
```

The folders are remembered in `game_source.txt` in the profile folder, so later runs (a mod
update) just use `Indexer --profile Lite_v2`. A new game version is `--game-root` with the new
folder, once: it replaces the remembered folder of the same depot and leaves the others alone.
Paths inside each game manifest are relative to its game folder, and each depot keeps its own
hash cache (`hashes_game_<depot>.cache`). `game_files.txt` still keeps stray copies of the game
in the profile folder out of the mod manifests; without any game folder the Indexer works as
before, splitting the game out of the profile folder by that list (`--game-manifest` names the
file then). A folder must be named `<depot>_<manifest>` for a Valheim depot and sit at `Game/` at
the site root, or the run reports an error. `steam_appid.txt` is not part of the Steam depot:
keep it in the mod profile, off the `game_files.txt` list.
