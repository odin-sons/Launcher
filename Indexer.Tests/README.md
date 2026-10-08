# Indexer.Tests

Server-side tests: exclusion-rule matching, the added/removed path diff between profiles, and
grouping changed files by mod folder for the report.

## ModFolderGroupingTests.cs

Grouping files into the "what changed" report, so one mod with hundreds of files prints as
a single line instead of hundreds.

- **FilesUnderAConfigSubfolder_AreGroupedByThatSubfolder** — files under one subfolder of
  `BepInEx/config/` (e.g. PlanBuild with 346 blueprint files) collapse into a single report
  line instead of being listed one by one.
- **BareFileDirectlyInConfig_IsNotGroupedWithUnrelatedConfigFiles** — files sitting directly
  in `BepInEx/config` with no mod subfolder are *not* grouped with each other — they share
  no common "mod".
- **FilesUnderPluginsSubfolder_StillGroupByThatSubfolder** — the same grouping still works
  for ordinary mods under `BepInEx/plugins/`.

## PathDiffTests.cs

Comparing file lists between two Indexer runs.

- **NewPath_IsReportedAsAdded** — a new path is reported as added.
- **MissingPath_IsReportedAsRemoved** — a path that disappeared is reported as removed.
- **UnchangedPath_IsReportedNeither** — an unchanged path is reported as neither.
- **DiffPaths_IsAPureSetDifference_CallerDecidesWhetherFirstRunCounts** — with an empty
  "previous" set (the first run), everything counts as added — `DiffPaths` itself is an
  honest set difference; the decision to suppress that report on a first run belongs to the
  caller, not to this function.

## GameManifestFlagTests.cs

`--game-manifest <name>` renames the game-file manifest so a macOS/Linux profile folder
produces `game_macos.info` / `game_linux.info` instead of overwriting the Windows one.

- **Default_IsGameInfo** — with no flag, the game manifest is `game.info`.
- **Flag_TakesTheNameAfterIt** — `--game-manifest game_macos.info` selects that name,
  wherever it sits among the other args.
- **FlagWithNoValue_IsIgnored** — a trailing `--game-manifest` with nothing after it falls
  back to the default rather than crashing.

## RuleMatchingTests.cs

The four forms an exclusion rule (`ignore_patterns.txt` and its siblings) can take.

- **ExactPathRule_MatchesOnlyThatExactFile** — a rule written as a full path matches only
  that exact file.
- **ExactPathRule_DoesNotMatchSameFileNameInADifferentFolder** — the same rule does *not*
  match a same-named file sitting in a different folder (the key regression check — a rule
  containing a slash used to silently fall back to matching by filename alone).
- **BareFileNameRule_StillMatchesAnywhereInTheTree** — a rule with no slash still catches a
  file with that name anywhere in the tree.
- **FolderPrefixRule_StillMatchesEverythingUnderIt** — a folder-prefix rule still catches
  everything underneath it.
- **MaskRule_StillMatchesByPathWhenItContainsASlash** — a mask rule (`*.old`) that contains
  a slash still matches by the full path.

## GameSourceTests.cs

The game folders kept apart from the mod profile, one per Steam depot: `--game-root` is
remembered in `game_source.txt`, so later runs index the same games without the flags.

- **NothingRemembered_MeansNoSeparateGame** — no file, no separate game folders.
- **PathsPassedOnce_AreReadBackOnLaterRuns** — every remembered path comes back as an
  absolute path.
- **TheRememberedPaths_AreStoredRelativeToTheProfileFolder** — stored as
  `../../Game/<version>`, so the file survives moving the whole tree.
- **ANewVersionOfADepot_ReplacesTheRememberedOne_AndLeavesTheOtherDepotsAlone** — moving to a
  new game version is one run with the new folder; the other OSes keep their folders.
- **ADepotNotRememberedYet_IsAdded** — a folder for a new depot joins the remembered ones.
- **OnlyADepotUnderscoreManifestFolderNameIsAVersion** — the folder name doubles as the
  version written to the game manifest, so it has to be `<depot>_<manifest>`.
- **TheDepot_DecidesWhichGameManifestTheFolderProduces** — 892972 gives `game.info`, 892973
  `game_macos.info`, 892971 `game_linux.info`.
- **ADepotThatIsNotValheim_IsRejected** / **AMissingFolder_IsRejected** — a folder that
  can't be indexed is an error, not a silent empty manifest.
- **TheGameFolder_IsExpectedAtTheSiteRoot** — the folder must sit at `Game/` at the site
  root, above the `Launcher` folder, where the launcher looks for it.
- **TheIndexersOwnFiles_AreNeverPartOfAManifest** — `game_source.txt` and the per-depot hash
  caches stay out of the mod manifests.
- **EachDepot_HasItsOwnHashCache** — Windows and Linux share relative paths such as
  `valheim_Data/...`, so their caches can't be one file.
- **ProfileFlag_TakesThePathAfterIt** — `--profile <folder>` (and any `--flag value` pair) is
  read from anywhere among the arguments; a trailing flag with no value is ignored.
- **GameRootFlag_CanBeRepeated** — `--game-root` can be given once per depot.
