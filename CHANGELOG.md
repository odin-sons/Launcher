# Changelog

Format inspired by [Keep a Changelog](https://keepachangelog.com/). Versions come from
`Launcher.Avalonia/Launcher.Avalonia.csproj` — the legacy WPF `Launcher` no longer gets
version bumps of its own.

## [2.0.0] — 2026-09-14

**`Launcher.Avalonia` is now the primary GUI**, cross-platform (Windows/macOS/Linux); the
WPF-only `Launcher` is kept for compatibility but no longer receives new development.

- Window chrome gets real Maximize/Restore buttons and states — Restore now correctly
  returns to the pre-maximize size and position instead of just un-snapping from the
  screen edges.
- Install settings (auto-start after validation, desktop/Start Menu shortcuts, Windows
  Defender exclusion) move into a collapsible group with checkboxes reflecting live
  state, replacing one-time popups.
- The launcher no longer needs administrator on every launch — it self-elevates (a
  one-shot UAC prompt) only the one time it actually needs to write into a Steam game
  folder for injector mode, and skips even that once nothing needs writing.
- Mods list redesign: collapsible per-mod descriptions, Thunderstore/Hexium links,
  clearer chevron/toggle layout.
- Ukrainian (`uk`) added as a 10th language; the UI language, once picked manually from
  the switcher, is now remembered across launches.
- Gray body/secondary text unified to a single, contrast-checked color across markdown
  rendering, mod descriptions, and status text.
- The "no players online" label now wraps/truncates correctly instead of overflowing
  its row.
- Cancelling an update responds immediately instead of finishing the in-flight batch of
  files first; a cancelled Steam-install check no longer lets an incomplete verification
  pass as a match.
- Fixed a cross-platform bug where extra-file cleanup deleted files in enumeration order
  instead of deepest-first on Linux/macOS.
- `Indexer`: an empty admin-only-mods list is now a note, not a build failure — only a
  non-empty list that matches nothing still fails the build.

## [1.3.6] — 2026-08-29

- Fixed the server-status indicator always showing offline (0 players) regardless of the
  server's actual state. PublicWebLink's `/serverinfo` response changed `mods` from a bare
  list of names to a list of objects (name/guid/version/description/websiteUrl/dependencies);
  deserializing that shape into `List<string>` threw, which took down the entire status
  update — the online indicator and player count are read from the same response as the mod
  list, not a separate call. `ServerInfo.mods` is now typed as `List<ServerModInfo>`,
  matching the real shape (the mod list itself still isn't shown anywhere in the UI).

## [1.3.5] — 2026-08-28

1.3.4 never shipped — its content is folded into this version instead of leaving an
empty "never published" entry, same as [1.2.12] below.


- Exclusion rules (`ignore_patterns.txt`, `admin_only_patterns.txt`, `optional_patterns.txt`)
  learned to match an exact path from the build root — previously a rule containing a
  slash silently fell back to matching by filename alone, and the path portion of the
  rule was effectively ignored.
- Config files of admin and optional mods are now classified the same way as the mods
  themselves: an optional mod's config only reaches a player who already has the mod
  itself — the same `optional.info` logic, now applied beyond just the dll.
- A new general "what was added/removed since the last run" report in `Indexer`, grouped
  by mod folder — three lines instead of a diff spanning thousands.
- A local ledger of confirmed-good files (`verified.info` in the client folder): if a
  file no longer matches the manifest even though it matched last time and the server
  hasn't changed it, that's suspicious (antivirus, permissions) and is now flagged
  separately instead of looking like an ordinary download.
- `exclude.txt` → `ignore_patterns.txt`, `exclude_admin.txt` → `admin_only_patterns.txt`,
  `greylist.txt`/`greylist_hash.info` → `optional_patterns.txt`/`optional.info`,
  `force_check.txt` → `force_check_files.txt`. Parsing of the legacy `.json` versions of
  these lists has been removed entirely — backward compatibility with them was not kept.
- Renamed `admin_only_mods.txt`/`optional_mods.txt` to `admin_only_patterns.txt`/
  `optional_patterns.txt` — both files mix mod-folder prefixes with exact-path config
  entries, not just mod names, same reasoning as the `ignore_patterns.txt` rename.
- `HashCreator` renamed to `Indexer` — more accurately describes what the tool does
  (builds an index of the build, doesn't publish or archive anything).
- One namespace across the whole solution — `Odinsons.ValheimLauncher` (previously a mix
  of `MeadlandLauncher` and a `RootNamespace` in the GUI project that had drifted out of
  sync). The executable name `OdinsonsLauncher.exe` was not changed.
- The hash cache (`hashes.cache`) now stores paths relative to the build root instead of
  absolute — portable between machines, no longer carries a username or a directory layout.
- Removed from the build: debug symbols (`.pdb`/`.mdb`), mod `README.md`/`PATCHNOTES.md`
  files (but not `LICENSE*` — some mods are MIT-licensed, which requires the license text
  to be included in every copy), the remaining files of the abandoned ValheimAdminTool
  mod. The PlanBuild blueprint library was moved to the admin-only distribution rather
  than removed.
- Wired up the injector (Doorstop) launch mode: when a matching Steam install is found
  and the client folder already has BepInEx deployed from a prior run, Valheim now runs
  directly from the Steam installation instead of duplicating ~1.5 GB of game files into
  the client folder. Falls back to the classic copy-based flow automatically whenever the
  injector's preconditions aren't met yet (fresh installs, no Steam copy, wrong OS).
- Tests: `Launcher.Tests` (client side — `FileDownloader`, `UpdateSession`,
  `ClientLedger`, `InjectorLauncher`) and `Indexer.Tests` (server side — rule parsing,
  diffing, grouping). GitHub Actions runs both suites on push/PR.
- License changed from GPL-3.0 to MIT.
- Full interface localization pass: every remaining hardcoded string in `MainWindow` and
  `ServerSelectionWindow` (dialogs, window titles, the player-count label, the copyright
  line) now goes through `Loc.T()` across all 9 languages, and the XAML comments in both
  windows were translated to English for consistency with the rest of the codebase.
- All commits are now GPG-signed; the project's git history was rebuilt from a clean,
  fully-English, MIT-licensed tree with no trace of the project's old "Meadland" branding.
- Fixed the launcher mirror list: the URLs pointed at `/launcher/` (lowercase), but the
  backend serves build files from `/Launcher/` — a case mismatch that made every mirror
  check fail and the launcher report all servers as unavailable. Also dropped
  `server-mirror.odinsons.club`, which has no DNS record and was never actually configured.
- The server-status check (player count / online indicator) no longer trusts SteamQuery's
  own retry timing for its UDP fallback, which could silently stall the Start button for
  up to ~2 minutes on an unresponsive query port. Bounded to a 5s timeout, same as the
  HTTP status check that precedes it.
- The GUI now looks for a Steam-installed copy of the game before updating, the same way
  the CLI already did — previously only the CLI could use injector mode (running Valheim
  in place from Steam instead of duplicating ~1.5 GB into the client folder); GUI players
  always fell back to the classic copy-based flow regardless of having Steam installed.
- Raised concurrent file downloads from 3 to 8, matched against the server's actual nginx
  connection limits (`conn_game`, `MaxConnectionsPerServer`) rather than an arbitrary
  round number.
- The mirror check on startup now backs off properly on HTTP 429/503 (exponential backoff
  with jitter, honoring `Retry-After`) instead of a flat one-second retry shared with every
  other kind of failure — a burst of simultaneous launches no longer risks every mirror
  attempt failing within a few seconds and reporting a false "all servers unavailable."
- Reordered the mirror list: the Cloudflare-fronted mirror (now serving with Encrypted
  Client Hello enabled) is tried first, then the direct domain, then the bare IP as a
  last resort — better odds under partial network blocking.
- Startup now logs the launcher's version and the running executable's SHA-256 as the
  very first line, so a player-submitted log says up front exactly which build produced it.
- Fixed a fail-open bug: if the update check threw partway through (a corrupt or
  unreadable manifest, for instance), the player saw an error message but the game
  launched anyway with whatever happened to already be on disk, unverified. The
  exception handler now sets `CanStartGame = false` before showing the error — a failed
  check fails closed, not open.
- Added a local hash cache (`filehashes.cache` for the client folder,
  `steamhashes.cache` for the Steam-install game-file check) keyed by a file's size and
  exact write time: a file that hasn't changed since the last run is no longer re-read
  and re-hashed from disk. Measured on a real 1931-file/1.1 GB `BepInEx` folder: ~1.3s of
  CPU/disk time to fully rehash on an ordinary run, ~0.03s once the cache is warm — about
  40x. Not a security boundary (see `FileHashCache`'s doc comment): the existing full
  check (`--full`, or a missing `BepInEx/LogOutput.log`) still bypasses the cache
  entirely and remains the real safety net.
- Optional-mod file grouping (`ModGrouping`) now matches only `BepInEx/plugins/<folder>/`
  — `BepInEx/config/` was dropped entirely after it produced duplicate and phantom mod
  entries (a bare loose config file's name was mistaken for a folder; the same mod's
  `plugins/` and `config/` folder names don't always match). No effect on the currently
  shipped GUI, which has no interface to write `optional_selected.txt` yet — this is
  groundwork for the upcoming mod-selection panel.

## [1.3.3] — 2026-08-16
- `servers.info`/`version.info` → `servers.json`/`version.txt` — the mirror's root file
  names now honestly describe the format of their content. Backward compatibility was
  not kept on purpose.
- Removed `_custommods` — the "folder for your own mods" gave no real protection: a mod
  placed there was deleted as unrecognized exactly like one placed directly in plugins,
  it only created an illusion of safety.

## [1.3.2]
- `force_check_files.json` → `force_check.txt`, the same line-based format as the other
  rule lists, instead of JSON.
- Fixed cleanup of `update.info`/`update_admin.info` in the client folder: previously
  only the manifest for the current update mode was deleted, the other one was left
  behind — an admin could carry a stale `update.info` for weeks.

## [1.3.1]
- `game.info` is now actually merged into the general check list instead of only acting
  as a "can be taken from Steam" marker. Previously this meant game files either never
  downloaded at all, or were deleted immediately as unrecognized.
- Moved the return of `valheim.exe` from `.updating` to before the "ready" report,
  instead of the update session's `Dispose` — previously the launcher could report "the
  game is ready to launch" before the file had actually been moved back.

## [1.3.0]
- Manifest format: binary → text (`MANIFEST 3 sha256`), with the algorithm named right
  in the header. Hash: MD5 → SHA-256 — faster on CPUs with SHA-NI, not just more robust.
- Interface localized into 9 languages; the launcher's console output stays English always.
- Client folder safety checks: refuses to work in a non-empty folder with no sign of a
  Valheim install, with no override; write-access check kept separate from the "is this
  folder safe" check.
- Console version of the launcher (`launcher-cli`) — Windows/Linux/macOS.
- Step-by-step network diagnostics (DNS → TCP → TLS → HTTP) when every mirror is
  unreachable, instead of one generic error.

## [1.2.12] — never published
This version existed in source but this particular build was never shipped to players.

## [1.2.11] and earlier
The last version actually running for players before this collaborative work began.
Further back — honestly: there is nothing to reconstruct from. This project's own git
history holds only two commits, both from December 2023 (`Initial commit`, `sign` — at
the time the launcher was a single monolithic .NET Framework 4.8 project, with no
Core/CLI/GUI/Indexer split, and the signing key was called `KG.snk`). Between that commit
and the start of this collaborative work there is a gap of more than two years without a
single recorded change. Almost everything that makes up the project's current structure
appeared during that gap: extracting the shared `Launcher.Core` library, the move to
.NET 9, the server-side update pipeline, the ladder system, the groundwork for injector
mode — but without commits there is no way to reconstruct the order or the timing.
