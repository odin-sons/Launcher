# Mods list & the changelog/mods tab strip

Status: **planned, not started.** The mods panel itself already works (Avalonia branch) —
this is about reworking how it's presented.

## Where it is now

- A separate **mods panel** (`ModsGrid`) lists the server's mods. Links come from the
  `website` field of each mod's `manifest.json` (`Launcher.Core/ModManifest.cs`).
- The **changelog** is a separate overlay (`ChangelogGrid`), opened by the gear button
  (`ChangelogButton`, `settings.png` / `settings_hover.png`). This is the panel the news
  skeleton was added to.

## What changed on the data side (done)

The mod `manifest.json` was extended: from its parameters the launcher can now build a link
to **Thunderstore** or **Hexium**, not only use a raw `website` URL. `ModManifest` /
whatever consumes it should prefer a constructed store link when the params are present and
fall back to `website` otherwise.

## The plan

1. **A tab strip above the changelog/skeleton area** (the panel at `NewsGrid` /
   `ChangelogGrid` position). Tabs:
   - **Changelog** — default, exactly today's changelog content (+ the loading skeleton).
   - **Обычные моды** — required mods (`update.info` / `update_admin.info`, grouped by
     `ModGrouping`).
   - **Опциональные моды** — optional mods (`optional.info`), each with the on/off toggle
     that `OptionalModSelection` already backs.

   Open design question: two separate mod tabs may be clunky — maybe one "Mods" tab with
   required/optional as sections or a filter. No strong idea yet; revisit with a designer.

2. **Move the mod list into those tabs** and split required vs optional between them.

3. **Remove the gear button** (`ChangelogButton`) and its icons
   (`settings.png`, `settings_hover.png`) once the changelog is a tab — nothing opens the
   old overlay anymore. Same for whatever currently opens `ModsGrid`.

## Notes

- Do this test-first where logic is involved (link construction from manifest params,
  required/optional split) — those belong in `Launcher.Core` + `Launcher.Tests`.
- The tab strip / panel swap is view-layer (`Launcher.Avalonia`), not unit-tested (no
  headless harness).
