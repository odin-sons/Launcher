# macOS files in the server profile

For server operators: which BepInEx/Doorstop files a profile needs for Mac players, and how a
profile moves from the x86_64 (Rosetta) launch to a native Apple Silicon one.

## Two launch paths

| Profile contains | macOS launch |
|---|---|
| `doorstop_libs/libdoorstop_x64.dylib` only (every profile so far) | x86_64 under Rosetta: `arch -x86_64 /usr/bin/env … <game>` |
| the universal `libdoorstop.dylib` (see below) | native arm64 on Apple Silicon, once the launcher's native-launch support (task #35) is released; Intel Macs use the same file |

Apple has announced that macOS 27 is the last release with full Rosetta support and that it is
not installed by default there, so the first path will stop working for new Macs. Keep it in the
profile for as long as you have players on it.

## Where the files come from

UnityDoorstop 4.6.0 and BepInEx 5.4.23.6 ship **one universal macOS library**, not a separate
arm64 file. Take the macOS pack from the BepInEx release:

```
gh release download v5.4.23.6 -R BepInEx/BepInEx -p BepInEx_macos_universal_5.4.23.6.zip
```

(or download `BepInEx_macos_universal_5.4.23.6.zip` from the
[release page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6)). Unpack it into a
scratch folder first and copy from there.

The pack layout:

```
.doorstop_version
changelog.txt
libdoorstop.dylib          universal (arm64 + x86_64) Doorstop library
run_bepinex.sh             BepInEx's own start script
BepInEx/core/*             BepInEx 5.4.23.6, including the native arm64 plugin-loading fix
```

There is no `doorstop_libs/` folder and no `doorstop_config.ini` in it. The launcher builds
the Doorstop settings itself; it does not use `run_bepinex.sh`.

## Where the files go in the profile

Copy the pack **as is** into the profile folder, merged with what is already there:

| From the pack | In the profile |
|---|---|
| `libdoorstop.dylib` | `libdoorstop.dylib` (profile root) |
| `run_bepinex.sh` | `run_bepinex.sh` (profile root) |
| `BepInEx/core/*` | `BepInEx/core/*` (replaces the old core files) |

`libdoorstop.dylib` at the profile root is the location the launcher's native mode looks for
first; `doorstop_libs/libdoorstop.dylib` is accepted as well.

**Keep `doorstop_libs/libdoorstop_x64.dylib` in the profile.** Players on a launcher that
predates native mode, and Intel/Rosetta players, still use it. The two libraries sit side by
side without conflict. Remove the x64 file only when no player needs the Rosetta path any
more; the next update then deletes it from their folders like any file that is no longer listed.

## Indexing

Run the Indexer as usual. The new files need no special handling: they are hashed, listed in
`update.info` / `update_admin.info`, delta-updated when the hash changes, and removed from
players' folders when you drop them from the profile. Do not put `libdoorstop.dylib`,
`run_bepinex.sh` or `BepInEx/` into `ignore_patterns.txt` or `game_files.txt`.

The Indexer output shows the swap in its change report, e.g. `+ libdoorstop.dylib`,
`+ run_bepinex.sh`, and `- doorstop_libs/libdoorstop_x64.dylib` if you dropped the old library.

## File permissions

The manifest carries no file modes and downloads are created without the execute bit. The
launcher sets the execute bit on `.sh`, `.dylib` and `.so` files it installs or finds already
up to date. Nothing needs to be done on the server for this, and the modes in the zip
(`-rw-------`) are irrelevant.

## An old profile

A profile that only has `doorstop_libs/libdoorstop_x64.dylib` keeps working unchanged: the
launcher does not find the universal library and uses the x86_64/Rosetta path. Players
updating from an old profile to a new one lose nothing; files that are no longer listed are
deleted, files that are listed are downloaded, and a folder left with only `libdoorstop.dylib`
or `run_bepinex.sh` after an interrupted update is still accepted as a launcher folder.

## Before you switch a profile to native

Whether Harmony/MonoMod patches work in the real modded game on native arm64 is **not
confirmed** yet (BepInEx#899 is still open; tracking issue #34). Do not publish the universal
library to all players on the strength of this document alone. Verify on a real Apple Silicon
Mac first:

1. The preloader and every plugin of your mod set load natively (check `BepInEx/LogOutput.log`).
2. Harmony patches of your actual mods apply: not only a hello-world plugin, but the mods that
   patch heavily.
3. No mod ships an x86_64-only native library (`.dylib`/`.so`); it cannot load into an arm64
   process.
4. The Steam overlay still loads, and the game reaches the main menu and a server.

The spike harness for this is `scripts/arm64-spike.sh`; the steps are in `docs/arm64-spike.md`.
Until the checks pass,
leave `libdoorstop.dylib` out of the production profile, or publish it to a separate test
profile only.
