# Indexer.Tests

Server-side tests: exclusion-rule matching, the added/removed path diff between builds, and
grouping changed files by mod folder for the build report.

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
