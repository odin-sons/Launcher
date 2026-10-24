# Native arm64 launch spike (#34)

Goal: find out whether modded Valheim runs natively on Apple Silicon with BepInEx 5.4.23.6
(universal) and UnityDoorstop 4.6.0, instead of under Rosetta. The harness is
[`scripts/arm64-spike.sh`](../scripts/arm64-spike.sh). It needs a real Apple Silicon Mac.

The harness reproduces what `run_bepinex.sh` from the BepInEx pack does, with the Steam
variables our launcher sets. It does not use the launcher, does not modify the Steam install,
and does not execute anything from the downloaded pack: the only code from the pack that runs is
the game, loading `libdoorstop.dylib` and the BepInEx assemblies, which is what is being tested.

## 1. Prepare

1. Install Valheim in Steam and start it once vanilla, so the Steam install is complete. Quit it.
   Keep Steam running and signed in.
2. Download the pack: [BepInEx 5.4.23.6, `BepInEx_macos_universal_5.4.23.6.zip`](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6).
3. Create an empty folder for the spike profile and unzip the pack **into it**, so that the
   folder root looks like this:

   ```
   <profile>/
     libdoorstop.dylib
     run_bepinex.sh        (not used by the harness)
     .doorstop_version
     BepInEx/core/BepInEx.Preloader.dll
     BepInEx/core/...
   ```

   `libdoorstop.dylib` is at the root, not in `doorstop_libs/`.
4. Remove the download quarantine flag, otherwise macOS may refuse to load the unsigned-by-Apple
   libraries and the result says nothing about arm64:

   ```bash
   xattr -dr com.apple.quarantine <profile>
   ```
5. Check that the pack is the expected one (SHA-256 of the files in the official zip):

   | file | sha256 |
   |---|---|
   | `libdoorstop.dylib` | `68a53bf977712692aed66f4acddfd8b722b9fa404624309c74e97c928009b758` |
   | `BepInEx/core/BepInEx.dll` | `780f175821781c14fdc349bf214603b7bab594ac556aecb1f6679b80248133f0` |
   | `BepInEx/core/BepInEx.Preloader.dll` | `80abc22c8d77622f1af422686749f572a8ee6606b922b8346fe110c0f628817c` |
   | `BepInEx/core/0Harmony.dll` | `1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031` |
   | `BepInEx/core/MonoMod.RuntimeDetour.dll` | `40e49bb314391cd7bddc2644f8553eeba92c194b940836b103df16955c464e0c` |
   | `BepInEx/core/HarmonyXInterop.dll` | `eafc025f3acf7ecd9fdc18c2309885f3feb452415d75a4f0fa058d993049b688` |

   The harness also writes these hashes to `file-hashes.txt`.

The game folder is the Steam one, usually `~/Library/Application Support/Steam/steamapps/common/Valheim`
(the folder that contains `valheim.app`).

## 2. Run 1: BepInEx only, no mods

```bash
bash scripts/arm64-spike.sh --game "<Valheim folder>" --profile "<profile>" --label bare
```

Optionally do a dry run first (`--dry-run`): it runs all the checks and prints the launch command
without starting the game.

What to do while it runs:

1. Wait for the main menu. The harness samples the process architecture after 3 s and 45 s.
2. Start or load a world and play for a minute, then quit the game normally.
3. The harness collects everything into `arm64-spike-bare-<timestamp>/`.

Useful variations of run 1 (each is a separate run with its own `--label`):

| flag | purpose |
|---|---|
| `--archpreference x86_64` | control run under Rosetta: proves the harness and pack work at all, separating "harness is wrong" from "arm64 does not work" |
| `--insert-absolute` | absolute library path in `DYLD_INSERT_LIBRARIES`, as our launcher does today, instead of the bare name `run_bepinex.sh` uses |
| `--no-overlay` | without the Steam overlay libraries, to isolate overlay problems |

## 3. Run 2: with the real mod set

1. Copy the server profile's mod files into the spike profile: its `BepInEx/plugins`,
   `BepInEx/config`, `BepInEx/patchers` and `BepInEx/monomod` (whatever exists) over the spike
   profile's `BepInEx/`. Do **not** copy the profile's `BepInEx/core`, `doorstop_libs`, or any
   `libdoorstop_x64.dylib`, they would replace the 5.4.23.6 files under test. If the server profile ships its own
   `BepInEx.dll`, note its version in the results.
2. Run again:

   ```bash
   bash scripts/arm64-spike.sh --game "<Valheim folder>" --profile "<profile>" --label mods
   ```

3. Join the server (or at least reach the character screen) so mod patches actually execute, play for a few minutes.

`native-libs.txt` lists every `.dylib`/`.so`/`.bundle` under the profile and the game bundle with
its architectures; lines marked `NO arm64 SLICE` are the x86_64-only native mods.

## 4. What the harness collects

Everything goes into one folder `arm64-spike-<label>-<timestamp>/`. Home, game and profile paths
are replaced with `~`, `<GAME>`, `<PROFILE>` before anything is written there. Skim the files before attaching them
anyway.

| file | content |
|---|---|
| `summary.txt` | macOS version, CPU, Rosetta state, `arch -e` support, game and library architectures, entitlements and code signature of the game, quarantine and signature status of the profile, the exact launch plan, the verdict signals |
| `LogOutput.log` | BepInEx log of this run |
| `doorstop-output.txt` | stdout and stderr of the game process, where Doorstop prints its messages |
| `Player.log` | Unity player log, if this run wrote one |
| `process-early.txt`, `process-late.txt` | architecture evidence: `ps` flags (`P_TRANSLATED`), `vmmap` code type, whether the Rosetta runtime is mapped, the libraries mapped |
| `native-libs.txt` | architectures of all native libraries |
| `profile-plugins.txt` | plugin, patcher and monomod file names |
| `file-hashes.txt` | hashes of the BepInEx files used |
| `crash-reports/` | Valheim crash reports from `DiagnosticReports` written during the run |
| `game-folder-writes.txt` | anything that changed in the Steam game folder during the run (should be empty or only Steam's own bookkeeping) |

The architecture verdict is in the `[early]` / `[late]` lines of the console output and in
`summary.txt`: `P_TRANSLATED clear` + a `vmmap` code type of `ARM64` + no Rosetta runtime mapped
means the game ran natively. These signals are best-effort: `vmmap` may be refused for a hardened
process. If they disagree or are unreadable, also check Activity Monitor's *Kind* column while the
game runs (`Apple` = native, `Intel` = Rosetta) and write that into the results.

## 5. Results checklist (questions from #34)

Fill in for each run (bare / mods).

- [ ] Game ran natively (Activity Monitor says *Apple*; harness verdict native)
- [ ] Doorstop loaded: `doorstop-output.txt` / `LogOutput.log` show the preloader starting
- [ ] BepInEx preloader ran and the chainloader started (`LogOutput.log` has the "Chainloader started" line)
- [ ] Plugins loaded (`LogOutput.log` lists every plugin of the set as loaded)
- [ ] Harmony patches applied: a plugin that patches the game works in game, and `LogOutput.log` has no `MonoMod`/`Harmony`/`DetourException`/`NotSupportedException` errors from patching
- [ ] Game reached the main menu and a world / server without crashing
- [ ] x86_64-only native mods: which ones (`native-libs.txt`), what happened to them (not loaded, crash, skipped)
- [ ] Steam overlay (Shift+Tab) works; Steam achievements and the Steam name appear in game
- [ ] Unity-version hook problem, [Doorstop#108](https://github.com/NeighTools/UnityDoorstop/issues/108): present or absent (report exactly what is observed, e.g. Doorstop output complaining about the Unity version or hooks, or no `LogOutput.log` at all)
- [ ] Control run under Rosetta (`--archpreference x86_64`) behaves as the old launcher does
- [ ] Anything else: crash reports, quarantine or signature messages, beach balls

## 6. Template for #34

````markdown
**Mac:** <model>, <macOS version>, Rosetta installed: yes/no, terminal translated: no
**Pack:** BepInEx_macos_universal_5.4.23.6 (hashes match: yes/no), server profile BepInEx version: <x>
**Game:** build <x>, lipo -archs: <x>

| check | bare | mods |
|---|---|---|
| ran natively (Activity Monitor / harness) | | |
| Doorstop loaded | | |
| preloader + chainloader | | |
| plugins loaded | | |
| Harmony patches applied | | |
| main menu / world / server | | |
| x86_64-only native mods | | |
| Steam overlay | | |
| Doorstop#108 | | |
| Rosetta control run | | |

**Verdict:** native modded launch works / works with limits / does not work
**Evidence:** <relevant LogOutput.log lines, the arch verdict lines>
**Attached:** result folders (paths are redacted by the harness)
````

## 7. Differences from `InjectorLauncher.cs` for #35 and #36

Compared against `run_bepinex.sh` from BepInEx 5.4.23.6 and `universal/run.sh` from Doorstop 4.6.0.
The two scripts are identical apart from the default `target_assembly`, the header comments and the
Linux-only Steam bootstrap block in `run_bepinex.sh`. The `libdoorstop.dylib` in both packs is
byte-identical (x86_64, arm64, arm64e). The table is the current launcher on the left and what the script does on the right.

### Library and profile layout (#36)

| | launcher today | official script |
|---|---|---|
| library | `doorstop_libs/libdoorstop_x64.dylib`, x86_64 only | `libdoorstop.dylib` at the **root** of the profile, universal (x86_64 + arm64 + arm64e) |
| `DYLD_LIBRARY_PATH` | `<profile>/doorstop_libs` | `<profile>/` (script dir, with trailing `/`), then the inherited value |
| `DYLD_INSERT_LIBRARIES` | absolute library path first | bare `libdoorstop.dylib` first (resolved through `DYLD_LIBRARY_PATH`) |
| BepInEx core | whatever the server profile ships | must be 5.4.23.6 or newer: the arm64 plugin-loading fix (BepInEx#1402) is in `BepInEx.dll`, and the arm64 crash fix (#1288) are part of the same release |

Consequences for #36: `InjectorLauncher.DoorstopLibraryRelativePath`, `DoorstopLibraryName()`, the
`FileDownloader` list of native files, `TestPack`, the tests and `Launcher.Tests/README.md` all hard-code
`doorstop_libs/libdoorstop_x64.*`. The manifests, the Indexer and the server profile layout must
carry the new file at the profile root. Windows (`winhttp.dll`) and Linux (`libdoorstop_x64.so`) were not examined here. The quarantine flag or a broken
signature can stop dyld from loading the library, so the downloader must not leave a quarantine attribute on it.

### Launch mechanics (#35)

| | launcher today | official script |
|---|---|---|
| architecture | `arch -x86_64 /usr/bin/env ...`, forced Rosetta | no `-x86_64`; `ARCHPREFERENCE` is exported (default `arm64,x86_64`) and `arch` picks the first architecture the executable has. A universal game starts native; an x86_64-only game still starts under Rosetta |
| why `arch` | to get Rosetta | always, even natively: a universal executable runs as x86_64 whenever its parent does, so `arch` pins it |
| passing `DYLD_*` | as `NAME=value` arguments of `/usr/bin/env` after `arch -x86_64` | `arch -e DYLD_LIBRARY_PATH=... -e DYLD_INSERT_LIBRARIES=... <game>`. `arch` is a platform binary, so dyld strips `DYLD_*` from its environment; `-e` hands them to the game on the exec. There is no `env` hop any more |
| other variables | all in the `env` argument list | `DOORSTOP_*` and `ARCHPREFERENCE` are plain exports, the process environment (`ProcessStartInfo.Environment`) is enough for them |
| process replacement | child of the launcher | `exec`: the game replaces the script process |
| `arch -e` | not used | needs a macOS whose `arch` supports `-e`; the harness reports support in `summary.txt`. #35 should probe it the way `X64Runs` probes Rosetta, and say so if it is missing |
| Rosetta check | `RosettaMissing` blocks the launch on Apple Silicon without Rosetta | only needed when the game ends up x86_64: x86_64-only game build, or `ARCHPREFERENCE` forced to `x86_64`. Native launch must not be blocked by it (macOS 27 does not install Rosetta by default) |
| architecture detection | none | the script runs `file -b` on the executable only to choose the library name for Linux; on macOS the result is not used for the library, it still rejects PE32 and anything without `64-bit`/`32-bit` in the `file` output |

### Inherited Steam libraries

| | launcher today | official script |
|---|---|---|
| overlay libraries | the launcher looks for `steamloader.dylib` and `gameoverlayrenderer.dylib` in the Steam app bundle itself (`SteamOverlayLibraries`) and appends them after the Doorstop library | takes `DYLD_INSERT_LIBRARIES`, or `STEAM_DYLD_INSERT_LIBRARIES` when that is empty, from its own environment and appends it after `libdoorstop.dylib` |
| `STEAM_DYLD_INSERT_LIBRARIES` | not read | read; Steam sets it for launch scripts because `/bin/sh` is a platform binary and loses `DYLD_INSERT_LIBRARIES` |
| order | Doorstop first, then Steam | Doorstop first, then Steam (same) |

When the launcher itself starts the game it has neither variable, so the lookup in the Steam
bundle stays necessary. If the game is ever started through Steam (a launch option), the script
path above is what matters. Whether the overlay actually works with a native arm64 game is a
question for the spike (checklist, Steam overlay). The harness uses the inherited value when it
exists and the bundle lookup otherwise, and prints which one it used.

### Variables and arguments

| | launcher today | official script |
|---|---|---|
| `SteamAppId`, `SteamGameId` | set to 892970 | **not set** (Steam sets them; the launcher must keep setting them) |
| `SteamOverlayGameId`, `SteamEnv`, `SteamClientLaunch` | set | **not set**, same remark |
| `DOORSTOP_ENABLED`, `DOORSTOP_TARGET_ASSEMBLY` | set | set; target is made absolute (`abs_path`) |
| `DOORSTOP_MONO_DEBUG_ENABLED`, `DOORSTOP_IGNORE_DISABLED_ENV` | `0`, `0` | `0`, `0` by default |
| other `DOORSTOP_*` | not set | always exported, empty or default: `BOOT_CONFIG_OVERRIDE`, `MONO_DLL_SEARCH_PATH_OVERRIDE`, `MONO_DEBUG_ADDRESS=127.0.0.1:10000`, `MONO_DEBUG_SUSPEND=0`, `CLR_CORLIB_DIR`, and `CLR_RUNTIME_CORECLR_PATH` which becomes the literal `.dylib` when the path is empty (a quirk of the script, harmless for Mono) |
| `--doorstop-enabled true --doorstop-target-assembly <path>` | appended to the game's arguments | **consumed and removed** by the script; the game never sees them. With the universal library the launcher should either do the same (rely on the environment) or confirm by test that the library tolerates them |
| old spellings `--doorstop_enabled`, `--doorstop_target_assembly` | not used | still accepted by the script |
| game executable | resolved from `Info.plist` with a regex, `Contents/MacOS` scan as fallback | `defaults read <app>/Contents/Info CFBundleExecutable`, symlinks resolved; the first script argument must be an executable path (the `.app`) |
| working directory | game folder | whatever the caller has; the harness uses the game folder, as the launcher does |
| Steam start | launcher nudges Steam with `open -g -b com.valvesoftware.steam` | nothing |

### Unknowns the harness answers

- Whether `DYLD_INSERT_LIBRARIES=libdoorstop.dylib` (bare name) works for the real game or the absolute path (`--insert-absolute`) is needed.
- Whether the Valheim app's entitlements and signature (in `summary.txt`) allow dyld to insert an
  externally signed library into a native arm64 process (`allow-dyld-environment-variables`, library validation).
- Whether the Steam overlay libraries still load into an arm64 process.
