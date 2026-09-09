# Detailing the download step

## Collapsible install-step groups — DONE

`InstallStepModel` (`Launcher.Core`, unit-tested in `Launcher.Tests/InstallStepModelTests.cs`)
holds the step list as an ordered set of steps folded into named groups. `IUpdateUi.SetSteps`
now takes `IReadOnlyList<InstallStep>` (`record InstallStep(string Group, string Label)`);
`FileDownloader.BuildStepList` tags:

| Group (`dl.group.*`) | Steps |
|---|---|
| **Проверка файлов** | Steam check · game-file check · optional-mod check |
| **Загрузка контента** | download |
| **Завершение** | finalize |

**Fold rule** (general, in the model): the moment a step in a later group goes active,
every earlier group whose steps are all done collapses to one summary row with a `▸`
chevron. The active group is always open. The user can reopen a folded group and that
choice sticks. So the check phase shrinks to a single line the instant downloading starts,
and the download line shrinks once "Завершение" starts — same mechanism, reused.

Rendering is in `MainWindow.axaml.cs` (`RenderInstallSteps` / `BuildGroupHeader` /
`BuildStepRow`). A one-step group draws as a plain row (nothing to fold). Per-step
wall-clock timings stay view-side (cosmetic); the group header shows their sum.

WPF / CLI ignore `SetSteps` (default no-op) — unchanged.

## Download detail: group by mod — DONE

- `DownloadGrouping.GroupKeyFor(relPath)` — folder right under `BepInEx/plugins/` or
  `BepInEx/config/`; loose files → one catch-all (`MiscKey`, shown as "Прочие файлы").
- `DownloadProgressTracker` — thread-safe aggregation across the 8-wide download: per-group
  bytes + file counts, "N of M mods" (done by **file count**, not the byte estimate),
  and `ActiveGroups(max)` = started-but-unfinished, biggest remaining first, capped at 8.
- `IUpdateUi.SetDownloadDetail(DownloadDetail)` — pre-formatted headline
  ("Моды: 31 / 78 · 412 / 1180 МБ · 6.2 МБ/с") + the ≤8 active rows (title, size, mini
  bar). Pushed from `FileDownloader` on a 250 ms throttle, forced on group completion and
  at start/end.
- Avalonia renders it under the step list (`DownloadDetailPanel`), visible only while the
  download step is active — so it disappears via the same fold as the group when "Завершение"
  starts. WPF/CLI ignore it.

Tests: `DownloadGroupingTests`, `DownloadProgressTrackerTests`.

Open: still no server-defined category grouping (raw package names for now). MB/speed text
that Avalonia previously didn't render at all is now in the headline.

## Superseded notes

### Configs are NOT instant — measured

`BepInEx/config` on the live `Rune_v2_test` client is **559 MB / 1218 files**, not a fast
tail. It's almost all in a few subfolders:

| subfolder | size | files |
|---|---:|---:|
| `Marketplace_Sounds` | 320 MB | 93 (mp3) |
| `Intermission` | 127 MB | 102 |
| `expand_world` | 97 MB | 32 |
| `wackysDatabase` | 14 MB | 601 (small yml) |
| loose files at `config/` root | **4.6 MB** | 94 |

So "configs / dll" as the split is wrong twice over: configs are a big chunk, and it's
data (png/mat/mp3/jpg), not `.cfg`. Only the 94 loose root files are trivial.

`BepInEx/plugins`: 626 MB across 57 mod folders. Whole client folder: **1.2 GB** (game runs
from Steam via injector, not copied).

### The plan: group the download by mod

The download bar is already **byte-accurate** (`_totalBytesDownloaded /
_totalBytesToDownload`), so overall position is fine under 8-wide parallelism. What's
missing is a sense of *where*. Note: Avalonia currently renders **no** MB/speed text during
download (`SetStatus` / `SetTotalProgress` are no-ops there) — that needs wiring too.

Group key per `FileToDownload`, assigned when `filesToDownload` is built:

1. `BepInEx/plugins/<folder>/…` → group `<folder>` (`ModGrouping.TopLevelPluginFolder`,
   make it public).
2. `BepInEx/config/<subfolder>/…` → group `<subfolder>` — this is where the heavy data is
   (`Marketplace_Sounds`, `Intermission`, `expand_world`, …). Names won't always match the
   plugins-folder name (GUID vs package name — see `ModGrouping` doc); for a *progress list*
   that's acceptable, it doesn't need to be the same identity as the optional-mod toggle.
3. everything else (loose config files, root files, loose plugin files) → one `база` bucket
   (~5–10 MB, fine to lump).

That's ~70–80 groups on a fresh install.

### Rendering the large list — proposed

Don't draw 80 rows. Under the download step:

- **counter**: `Моды: 31 / 78 · 412 / 1180 МБ · 6.2 МБ/с`
- a short **live list of what's transferring right now** (≤8 rows — the parallelism width),
  sorted by largest-remaining first so `Marketplace_Sounds` is visible while it dominates
  the time; each row a mini bar.
- completed groups just increment the counter and disappear; pending groups are only the
  counter.
- the whole thing sits inside the "Загрузка контента" group, so it folds away via the
  mechanism above once finalize starts.

Open question the user flagged: still hard to picture with a list this big. The ≤8-row
churning list + counter is the current best idea; revisit with a mock.

Optionally later: a server-defined `group`/`category` field in `manifest.json` (same
extension pass as the store-link params) so the server can say "Ядро" / "Контент" / "UI"
instead of raw package names.

### Testing

Test-first in `Launcher.Core` + `Launcher.Tests`: the group-key assignment for a mix of
paths (nested plugin file, loose plugin file, config subfolder file, loose config file,
root file) and the "N / M mods" / per-group completion math. The window itself is
view-layer, not unit-tested.
