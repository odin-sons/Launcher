# Launcher.Tests

Client-side tests: the update pipeline (`FileDownloader`, `UpdateSession`), the injector
(Doorstop) launch mode, and the local ledger of confirmed-good files.

Anything OS-shaped runs for Windows, macOS **and** Linux on whatever host CI is on:
`RuntimePlatform.Pretend(TargetOs)` swaps the OS the injector/downloader reasons about, and
those code paths only ever differ in strings and file layout — nothing that needs the other
OS's kernel — so a faked answer exercises the real branch. Nothing is `[Skip]`-ped by OS.
The suite runs serially (`CollectionBehavior(DisableTestParallelization = true)`), so the
`Pretend` scope is safe.

## UpdateScenarioTests.cs

End-to-end scenarios against the whole update pipeline, run over a real HTTP server serving
a fixture build. Each one is a regression for a specific case where behavior once silently
diverged from what a player would expect, with no error and no message.

- **FreshInstall_GetsRequiredAndGameFiles_SkipsOptionalAndAdmin** — a clean install pulls
  required mods and game files (`game.info`), but not optional or admin-only ones.
- **OptionalMod_SurvivesUpdate_ButUntrackedFileIsRemoved** — an already-installed optional
  mod survives an update; a file tracked by no manifest at all gets removed.
- **AdminMarker_SwitchesToAdminManifest_BringsAdminOnlyMods** — an `admin` marker file in
  the client folder switches the launcher to `update_admin.info` and pulls admin-only mods.
- **ForceCheck_RepairsServerOwnedConfig_LeavesPlayerConfigAlone** — a file listed in
  `force_check_files.txt` gets overwritten with the server's version even on a non-full
  check; an ordinary player config file does not.
- **StashedExecutable_IsRestoredBeforeCompletionIsReported** — `valheim.exe`, moved aside
  for the duration of the update, is restored *before* the launcher reports "ready to
  play", not after.
- **FileThatDriftsAfterBeingConfirmedGood_IsFlaggedAsSuspicious** — a file that matched the
  manifest last run and now doesn't, with the server's expected hash unchanged, is flagged
  as suspicious (the ledger mechanism).
- **InjectorMode_ActivatesOnTheVeryFirstRun_NeverDuplicatesGameFiles** (×Windows/macOS/Linux)
  — injector mode activates on the very first run (doesn't require the preloader to already
  be sitting in the client folder beforehand); game files are never duplicated, and the
  native doorstop bits (`winhttp.dll` / `libdoorstop_x64.{dylib,so}`) are fetched ahead of
  time so eligibility can be decided this run.
- **InjectorMode_SteamBuildDoesNotMatchServer_FallsBackAndDownloadsCorrectFiles**
  (×Windows/macOS/Linux) — when the Steam copy's build doesn't match what the server
  expects, the launcher does not start the game against the wrong files — it falls back to
  the classic check and downloads what the server actually wants.

Fixtures follow the pretended OS: `TestPack.AddGameExecutable` lays down a plain file on
Windows/Linux and a minimal `.app` bundle on macOS, `TestPack.WriteGameManifest` writes
`game.info` / `game_macos.info` / `game_linux.info`, and `TestPack.AddDoorstopPrereq` the
matching native piece.

## GameManifestSelectionTests.cs

The game-file manifest is per-OS (`game.info` / `game_macos.info` / `game_linux.info`); the
layouts don't overlap. Each case runs for all three OSes.

- **ThisOs_PicksItsOwnManifest_NotAnotherPlatformsGameInfo** (×Windows/macOS/Linux) — the
  launcher fetches the manifest for its OS; a game file listed only under another platform's
  `game.info` is never downloaded.
- **NonWindows_ServerHasOnlyWindowsGameInfo_NoGameFilesArePulled** (×macOS/Linux) — a server
  that only publishes the Windows `game.info` yields no game files at all (there is
  deliberately no fallback) — the mods still install, but not one Windows game file is
  dragged in.
- **ThisOs_MatchingSteamCopy_InjectorActivatesFromTheOsManifest** (×Windows/macOS/Linux) —
  with a matching Steam copy, injector mode activates off the OS-specific manifest and
  points at the Steam install, duplicating no game files.

## InjectorLauncherTests.cs

Building and preparing the Doorstop launch plan. Every OS-shaped case is a `[Theory]` over
`TargetOs` (`RuntimePlatform.Pretend`), so all three platforms are exercised on any host.

- **HappyPath_ReturnsPlanAndPreparesTheGameFolder** (×Windows/macOS/Linux) — the plan is
  built correctly; on Windows `winhttp.dll` is copied and `doorstop_config.ini` written with
  `enabled = false`, on macOS/Linux the game folder is left untouched and the native
  doorstop library rides in an absolute `DYLD_INSERT_LIBRARIES` / `LD_PRELOAD`.
- **BlockingBepInExFolder_IsMovedToBackup_ThenPrepSucceeds** (Windows) — a foreign `BepInEx`
  folder (left by another mod manager) is moved to a backup rather than deleted, and
  preparation still succeeds afterward.
- **MissingPreloader_FailsWithoutTouchingTheGameFolder** (×Windows/macOS/Linux) — without
  the preloader present, the plan isn't built, and the game folder is left untouched.
- **MissingDoorstopLibrary_FailsWithoutTouchingTheGameFolder** (×macOS/Linux) — without
  `doorstop_libs/libdoorstop_x64.{dylib,so}` present, the plan isn't built.
- **MissingGameExecutable_Fails** (×Windows/macOS/Linux) — without the game executable in
  the game folder, preparation fails cleanly with a reason.
- **MacLaunchShape_RunsX64UnderEnvWithAbsoluteDoorstopLibrary** — the macOS launch is
  `arch -x86_64 /usr/bin/env KEY=VALUE… <valheim.app/Contents/MacOS/…>`: x86_64 for the
  x64-only doorstop lib, and the DOORSTOP/DYLD variables passed as `env` arguments (not the
  inherited environment, which the kernel would strip).
- **NonMacLaunchShape_RunsExecutableDirectlyWithEnvironmentVariables** (Windows/Linux) — the
  game binary is the process, doorstop rides in `EnvironmentVariables`, no wrapper.
- **ResolveGameExecutable_MacBundle_ResolvesToTheBinaryNamedInInfoPlist** — a `.app`
  resolves to `Contents/MacOS/<CFBundleExecutable>`.
- **ResolveGameExecutable_MacBundleNoPlist_FallsBackToTheSoleBinary** — no `Info.plist`:
  the single file in `Contents/MacOS/` is used.
- **ResolveGameExecutable_MacBundleNoPlistAmbiguous_ReturnsNull** — no `Info.plist` and
  more than one file in `Contents/MacOS/`: nothing is guessed.
- **ResolveGameExecutable_NonMac_ResolvesThePlainFile** (Windows/Linux) — `valheim.exe` /
  `valheim.x86_64` resolves as itself.
- **ResolveGameExecutable_NothingThere_ReturnsNull** (×Windows/macOS/Linux) — an empty
  folder resolves to nothing.

## ClientLedgerTests.cs

The ledger mechanism itself (`verified.info`).

- **FreshLedger_HasNoConfirmedEntries** — an empty folder starts with no memory of
  anything confirmed.
- **RecordThenSaveThenLoad_RoundTripsTheConfirmedHash** — a recorded and saved hash reads
  back correctly after `Load`.
- **RecordConfirmed_OverwritesThePreviousHashForTheSamePath** — recording the same path
  again overwrites the old hash instead of duplicating it.
- **Save_OnlyKeepsEntriesRecordedInThatRun_StaleEntriesAreDropped** — the ledger doesn't
  grow forever: only what was actually confirmed in the current run is kept, stale entries
  fall away on their own.
- **MissingOrCorruptLedgerFile_LoadsEmptyInsteadOfThrowing** — a corrupt or unreadable
  `verified.info` doesn't crash the launcher — it's simply treated as "nothing to compare
  against".

## InstallStepModelTests.cs

`InstallStepModel` — the state behind the install progress overlay: the flat step list
`FileDownloader` drives, folded into the collapsible groups the window draws. Not OS-shaped.

- **Groups_AreBuiltFromTheGroupTags_InFirstSeenOrder** — the group list comes from each
  step's `Group` tag, in order, with the right steps under each.
- **EveryGroupStartsExpanded** — nothing is folded before the run begins.
- **StartingAStepInTheNextGroup_FoldsAwayThePreviousFinishedGroup** — the check phase
  collapses to one row the moment the download step goes active.
- **TheActiveGroupIsNeverCollapsed** — the group being worked on always stays open.
- **AGroupThatIsNotFullyDone_IsNotFoldedAway** — a group with a still-pending step stays
  visible even after the run moves on (shouldn't happen in practice, but it's not hidden).
- **TheDownloadGroupFoldsAway_OnceFinishingUpBegins** — the same fold rule applies to the
  download group, so a slow finalize phase isn't buried under it.
- **ToggleGroup_ReopensAFoldedGroup_AndItStaysOpenAfterwards** — the user's chevron click
  survives later group transitions (an explicit open sticks).
- **OverallPercent_NeverMovesBackwards** — the one bar only ever moves forward, including
  across the gap between one step finishing and the next starting.
- **OverallPercent_IsDoneStepsWhole_PlusTheActiveStepFraction** — 1 of 5 done + a half-done
  active step reads 30%.
- **CurrentOrdinalAndLabel_FollowTheActiveStep_ThenRestOnTheLast** — the "step N of M"
  header tracks the active step and settles on the last one when idle.

## DownloadGroupingTests.cs

`DownloadGrouping.GroupKeyFor` — the bucket a downloaded file shows under in the progress
list.

- **FilesUnderAPluginOrConfigFolder_GroupByThatFolder** — a file under
  `BepInEx/plugins/<mod>/` or `BepInEx/config/<mod>/` groups by that folder (config counts
  too — on a real build it holds hundreds of MB of music/textures).
- **LooseFiles_LandInTheCatchAllBucket** — a stray `.cfg` straight in `config/`, a loose
  plugin file, a game-root file: all one catch-all group.
- **LeadingSlashesAndBackslashesDontMatter** — path separators and a leading slash are
  normalized.

## DownloadProgressTrackerTests.cs

`DownloadProgressTracker` — folds a parallel download into the "N / M mods · X / Y MB" line
and the biggest-remaining-first short list.

- **TotalsComeFromTheConstructor** — group count and byte total are fixed up front.
- **AddBytes_AccumulatesPerGroup_AndClampsAtZeroOnRollback** — a retry rollback (negative
  delta) can't drive a group's counter below zero.
- **AGroupCountsAsDone_OnlyWhenEveryFileInItIsComplete** — "mods done" is by file count,
  not bytes (the byte total is only an estimate).
- **CompleteFile_NeverOvercounts_PastTheGroupSize** — a stray extra completion call is
  ignored.
- **ActiveGroups_AreStartedButUnfinished_BiggestRemainingFirst_Capped** — the short list is
  ordered by bytes remaining and honours the display cap.
- **ActiveGroups_ExcludesFinishedGroups_EvenIfBytesLagBehind** — a group whose files are
  all done drops off the list even if its byte counter never reached the estimate.
