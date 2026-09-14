# Odinsons.ValheimLauncher

A launcher for a modified Valheim build. It keeps a player's mod set in sync with the
server, verifies every file by SHA-256, and gets out of the way otherwise — no manual
patching, no "which mods am I even missing".

The primary GUI is `Launcher.Avalonia`, cross-platform (Windows, macOS, Linux). The
original WPF-only `Launcher` still exists but is no longer where development happens.

## Features

### Keeping mods up to date

- **Delta updates.** Every file is checked against the server's manifest by SHA-256; only
  what's actually missing or changed gets downloaded. Files no longer part of the build are
  removed automatically.
- **Optional mods.** Players can opt in to specific mods without them being wiped on the
  next update — an optional mod, once installed, survives updates the same way a required
  one does.
- **Admin distribution.** A separate, larger mod set (extra tools, admin utilities) is
  available to admins without being pushed to every player.
- **Suspicious-file detection.** If a file matched the server's manifest last time but
  doesn't now — with the server not having changed it — the launcher flags this as
  suspicious instead of silently redownloading it. Usually antivirus or a permissions
  problem on the player's machine, not a real update.
- **Full check.** A dedicated button re-verifies every file from scratch, ignoring the
  normal exclusion rules — for when something's wrong and a normal check isn't catching it.

### Steam integration

- **Injector mode.** If Valheim is already installed via Steam and its build matches what
  the server expects, the launcher runs the game directly from the Steam install with mods
  injected via Doorstop/BepInEx — no ~1.5 GB duplicate copy of the game. The build match is
  verified by hash, not just by the executable's path existing, so a stale or auto-updated
  Steam copy never silently launches with the wrong files. Falls back to the classic
  copy-based flow automatically whenever any of this isn't the case (no Steam copy found,
  build mismatch, fresh install, non-Windows OS).

### Servers and connectivity

- **Multiple servers.** Pulls the server list from the mirror; if more than one is
  configured, the player picks which to play on, and the choice is remembered.
- **Live status.** An online/offline indicator and current player count for the selected
  server, with a fallback path (SteamQuery) if the primary status endpoint isn't reachable.
- **Multi-mirror resilience.** Several independent mirrors (a Cloudflare-fronted domain
  with Encrypted Client Hello enabled, a direct domain, and a bare-IP fallback) are tried in
  order, so a problem reaching any single one of them doesn't mean the game is unreachable.
- **Self-updating.** The launcher checks its own version against the server and replaces
  itself automatically when a newer build is published.
- **Resilient networking.** Rate-limit and server-error responses (HTTP 429/503) are
  retried with backoff instead of being treated as "the server is down" — a burst of
  players launching at once doesn't produce false "all servers unavailable" errors.

### Interface

- **9 languages**, picked automatically from the system language, with English as the
  fallback: English, Deutsch, Español, Français, Polski, Português, Русский, Svenska, 中文.
- **In-launcher changelog and news panel**, pulled straight from the server.
- Quick links to the community: [Discord](https://discord.gg/eTteBxWcfu),
  [Telegram](https://t.me/+AHY_F2jClNJmNTcy), and the [website](https://odinsons.club/).

### Safety

- Refuses to operate in a folder that doesn't look like a Valheim install and isn't empty
  either, without an explicit override — it won't guess and start deleting things in the
  wrong place.
- Checks write access to the target folder up front, with advice specific to *why* it
  failed, rather than a generic error partway through downloading.

## Also available: a console version

`Launcher.Cli` is a cross-platform (Windows/Linux/macOS) command-line equivalent of the
same update pipeline — useful for dedicated-server setups or scripted installs. Run it with
`--help` for its options.

## Third-party

`Launcher/SteamQuery.dll` is a third-party binary used as a fallback source for live player
counts when the HTTP status endpoint is unavailable. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for its license and provenance.

## Credits

- **[KG](https://github.com/war3i4i/Valheim-Online_Launcher)** — original author and
  creator of the launcher as an idea and its first version.
- **MadBomg** — did most of the work on this launcher.
- **fogrew** — cross-platform (macOS/Linux) support and the Avalonia install-progress
  overlay.

## License

MIT — see [LICENSE](LICENSE).

---

See [CHANGELOG.md](CHANGELOG.md) for the history of changes, and
[CONTRIBUTING.md](CONTRIBUTING.md) for building the project, its code layout, and how to
run it locally.
