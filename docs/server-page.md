# Server page: steps, About, News and Terms

Companion to [themes.md](themes.md), [registry-format.md](registry-format.md) and
[registry-trust-policy.md](registry-trust-policy.md). The theme document says how a profile looks;
this one says what the window shows for a profile, where each piece comes from, and how the player
agrees to a server's terms before anything is installed.

## Steps

The window is a wizard. The steps are tabs and stay clickable, and the wizard button always names the
next one.

| Step | Content | Wizard button |
|------|---------|---------------|
| 1 Server | Online list, then the community's art, About and News in the chosen template | Next: Terms |
| 2 Terms | The launcher notice, the community's terms and the acceptance | Accept and continue |
| 3 Mods | Required and optional mods | Next: Install |
| 4 Install | Update check, installation, progress | Install and play, then Play |

The template frames all four steps (see [themes.md](themes.md#layout-templates)). The Server step is
arranged by zones: the Online list at the side the theme names, then the art, About and News. On the
other three steps the community's art becomes a frame: a narrow column at the left or right (`classic`,
`sidebar`), a slim strip on top (`banner`), or just the community's name and logo (`minimal`). The
step's own content belongs to the launcher and arranges itself in the room that is left: one column
beside a side column, two columns when it has the full width.

| Step | One column | Two columns |
|------|------------|-------------|
| 2 Terms | The launcher notice, then the terms, then the acceptance | The notice on the left, the terms on the right, the acceptance underneath |
| 3 Mods | Required mods, then optional ones | Required mods on the left, optional ones on the right |
| 4 Install | Status, settings, progress and the step list, top to bottom | Status, settings and progress on the left, the step list on the right |

In every arrangement the launcher notice comes before the community's terms in reading order and is at
least as prominent: a template cannot shrink it, collapse it or place it after the terms. Nothing in the
frame can carry text other than the community's name and tagline.

Terms is skipped, and shown as done, while the player's acceptance still matches the current terms.
The built-in profile has no launcher notice to show; it shows the Terms step only if it defines terms
of its own.

## Registry entry fields

Three optional fields point at files on the community's mirror. Like the theme, each path must have an
entry in `digests`, so a file that was swapped after review is not used.

| Field | Type | Meaning |
|-------|------|---------|
| `theme` | string | `theme.json`, see [themes.md](themes.md) |
| `about` | string | `about.json`, below |
| `terms` | object | Language code to path of a Markdown file, for example `{ "en": "terms/en.md", "de": "terms/de.md" }`; `en` is required |

A missing field means the zone or step is simply absent. Unknown fields are ignored by older
launchers, so none of these needs a schema bump.

## About

The About zone shows a list of facts and a description. It is for what a player wants to know before
joining: what kind of server this is, not how it looks.

### Facts the launcher measures

These come from data the launcher already has, are shown first and are marked ◆. A community cannot
set or override them.

| Fact | Source |
|------|--------|
| Game version | The game manifest |
| Number of mods | The modpack manifest |
| Download size | The modpack manifest |
| Online now and capacity | The server query that fills the Online list |

### about.json

```json
{
  "aboutSchema": 1,
  "tagline": { "en": "Hardcore survival, no wipes" },
  "facts": {
    "playstyle": "Hardcore survival",
    "pvp": "off",
    "difficulty": "hard",
    "region": "EU",
    "languages": "en, ru",
    "wipes": "every 3 months",
    "restarts": "04:00 UTC"
  },
  "description": {
    "en": "A long-running server for small groups.\n\n- Weekly events\n- Friendly admins",
    "de": "The same description in German."
  }
}
```

| Field | Rules |
|-------|-------|
| `aboutSchema` | integer; a launcher that does not support the value shows no About zone |
| `tagline` | language code to one line of plain text, at most 80 characters; shown on the art; `en` required |
| `facts` | object, one value per key; keys come from the vocabulary below and unknown keys are ignored; each value is plain text of at most 48 characters |
| `description` | language code to text, at most 2000 characters each; `en` required |

The vocabulary is `playstyle`, `pvp`, `difficulty`, `region`, `languages`, `wipes` and `restarts`. The
launcher owns and localises the labels, so a community cannot invent a label, and every server's list
looks alike. A new key is a launcher release (a translation and an entry in this table), never a
registry change.

The launcher shows the text for its interface language and falls back to `en`. The file is at most
16 KiB.

### The built-in profile

The built-in profile has no `about.json`. Its `info.md` is the description and the `servers.json` description
of the selected game server is the tagline. Its News is the `changelog.md` on its own mirror. The online
capacity is shown when the server's status answer carries `maxPlayers`; without it only the count is shown.

### Text format

Descriptions, news and terms use the same Markdown subset: paragraphs, bold, italic, bullet lists,
headings of level 2 and 3, and links. Links must be `https`; the launcher shows the host before it
opens one in the browser. Images and HTML are never rendered; raw HTML appears as text.

## News

News is the community's changelog, `changelog.md` on the mirror today, in the same Markdown subset,
and it is pinned in `digests` like the other display files. A community without one has no News zone. The file
is at most 64 KiB.

## Terms

A player agrees to two texts, in this order, on one screen.

1. **The launcher notice.** Fixed text owned and localised by the launcher's maintainers, drawn in the
   launcher's fixed colours with a "Launcher notice" label. It says that servers in the launcher are
   run by third parties, that their content is not reviewed line by line, what a registry status does
   and does not mean (see [registry-trust-policy.md](registry-trust-policy.md)), and that the player
   runs it at their own risk.
2. **The community's terms.** The text from the entry's `terms` field, drawn on a theme surface and
   captioned "Written by the community, not by the launcher". This is where a community states its own
   rules and rights: server rules, how its content and modpack may be used, privacy, and so on.

Terms are text only. They cannot add a checkbox, a button or a link target the launcher does not
show, and they cannot change what the launcher does. A maintainer reads them in the registry pull
request and rejects terms that imitate the launcher or the registry or claim that either endorses the
community.

The terms file is at most 20 KiB per language.

### Acceptance

The player ticks one box, "I have read and accept the notice and the terms", and the wizard button
becomes available. Only display files (the theme and its images, About, News, the terms) are fetched
before this point.

**Downloading waits for acceptance.** Acceptance is a precondition for any download from the
community, for legal reasons that belong to the maintainers: the terms are agreed before files change
hands. Until the player's acceptance matches the current terms and notice, the launcher does not
download a mod or a game file, does not install, update or repair, and does not run a verification
that could fetch files. This holds on a first install and again whenever the terms or the notice
change; the wizard button then leads to the Terms step. The launcher does not interpret the terms or
where the player is: it enforces that the current text was accepted.

The launcher stores the acceptance locally, in the profile's own data folder, as: the profile id, the
digest of the terms file that was shown, the version of the launcher notice, and the time. It is never
sent anywhere and goes away with the profile.

The player is asked again when the digest of the terms or the version of the notice changes. Declining
leaves every file untouched and downloads nothing; the player can pick another server.

### Open points

* The wording of the launcher notice. It is a legal text and should be written and reviewed by the
  maintainers before 3.0.0 ships, not by the implementation.
* Whether starting an already installed and complete profile should also wait for changed terms. The
  rule above gates downloads only, and starting such a profile downloads nothing.
* Terms in more than one language when the interface language is missing: today it falls back to `en`.
