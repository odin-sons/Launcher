# Launcher.Tests

Client-side tests: the update pipeline (`FileDownloader`, `UpdateSession`), the injector
(Doorstop) launch mode, and the local ledger of confirmed-good files.

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
- **InjectorMode_ActivatesOnTheVeryFirstRun_NeverDuplicatesGameFiles** — injector mode
  activates on the very first run (doesn't require the preloader to already be sitting in
  the client folder beforehand); game files are never duplicated.
- **InjectorMode_SteamBuildDoesNotMatchServer_FallsBackAndDownloadsCorrectFiles** — when the
  Steam copy's build doesn't match what the server expects, the launcher does not start the
  game against the wrong files — it falls back to the classic check and downloads what the
  server actually wants.

## InjectorLauncherTests.cs

Building and preparing the Doorstop launch plan.

- **HappyPath_ReturnsPlanAndPreparesTheGameFolder** — under normal conditions the plan is
  built correctly, `winhttp.dll` is copied, and `doorstop_config.ini` is written with
  `enabled = false`.
- **BlockingBepInExFolder_IsMovedToBackup_ThenPrepSucceeds** — a foreign `BepInEx` folder
  (left by another mod manager) is moved to a backup rather than deleted, and preparation
  still succeeds afterward.
- **MissingPreloader_FailsWithoutTouchingTheGameFolder** — without the preloader present,
  the plan isn't built, and — importantly — the game folder is left untouched.
- **MissingGameExecutable_Fails** — without `valheim.exe` in the game folder, preparation
  fails cleanly with a reason.

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
