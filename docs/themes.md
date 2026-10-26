# Layout templates and themes

Companion to [registry-format.md](registry-format.md) and [registry-trust-policy.md](registry-trust-policy.md).
This document specifies what a community may choose about the look of its profile. It is data only:
a theme never carries code, markup or styles, so it can change colours and pictures but not behaviour.

## What a community can and cannot choose

| Can choose | Cannot choose |
|------------|---------------|
| One layout template from a fixed set | Positions or sizes of individual controls |
| Colour tokens | The trust badge, status notices and the launch/confirm controls |
| A background image and a logo | Any text of the launcher itself |
| A font from the allowlist | A font file of its own |

The trust badge, the `needs-approval` and `suspended` notices, and the launch and confirmation
controls are drawn by the launcher from its own fixed colours. A theme cannot restyle or hide them.

## Where a theme lives

A theme is a file on the community's mirror, next to its other files, and it is pinned by the registry
like any other file:

* The registry entry gains an optional `theme` field: a path relative to a mirror base URL, under the
  same rules as the keys of `digests`.
* That path, and every image the theme refers to, must have an entry in `digests`. A file whose hash
  differs from the signed digest is not used, so changing a theme means a new registry entry.
* Unknown fields in the entry are ignored by older launchers, so adding `theme` needs no schema bump.

## theme.json

```json
{
  "themeSchema": 1,
  "layout": "classic",
  "colors": {
    "accent": "#C8A24A",
    "surface": "#1B1B1F",
    "text": "#F2F2F2",
    "textMuted": "#B5B5BA"
  },
  "background": { "file": "theme/background.jpg", "position": "right" },
  "logo": { "file": "theme/logo.png" },
  "font": "montserrat"
}
```

| Field | Rules |
|-------|-------|
| `themeSchema` | integer; a launcher that does not support the value uses the default theme |
| `layout` | one of the template ids below; an unknown id means `classic` |
| `colors` | each value is `#RRGGBB`; a missing or malformed token takes the default |
| `background`, `logo` | `file` is a path listed in `digests`; `position` is `left`, `center` or `right` (background only) |
| `font` | an id from the allowlist below; anything else means the default |

The file is at most 16 KiB. Unknown fields and unknown tokens are ignored, so a newer theme still
loads on an older launcher; a change an older launcher must not misread bumps `themeSchema`.
Alpha channels are not accepted, so a theme cannot make a colour transparent.

### Layout templates

| Id | Description |
|----|-------------|
| `classic` | The current window: background picture, logo, news panel and the controls |
| `compact` | Narrower window without the news panel, for communities that do not publish news |

The set is fixed in the launcher. Adding a template is a launcher release, never a registry change.

### Colour tokens

| Token | Used for |
|-------|----------|
| `accent` | Highlights, progress bar, selected items |
| `surface` | Panels laid over the background |
| `text` | Text on `surface` |
| `textMuted` | Secondary text on `surface` |

### Font allowlist

`montserrat` (the default). Further ids are added with a launcher release; fonts are bundled, never
downloaded.

## Limits

| Item | Limit |
|------|-------|
| `theme.json` | 16 KiB |
| Logo | PNG, at most 256 KiB and 512 x 512 pixels |
| Background | PNG or JPEG, at most 1 MiB and 1920 x 1080 pixels |

Images are decoded only after the hash matches, from a size-bounded buffer; the decoded dimensions are
checked before the bitmap is kept. Animated and vector formats are not accepted. A file that breaks a
limit is ignored and the default picture is used.

## Contrast guard

Before a theme is applied the launcher checks the WCAG contrast ratio of its colours:

* `text` on `surface`: at least 4.5
* `textMuted` on `surface`: at least 4.5
* `accent` on `surface`: at least 3

If any check fails, the whole colour set falls back to the default theme (images and layout are kept),
so a theme can never make the text unreadable. The launcher's own notices and controls use fixed
colours and are not part of this check.

## Failure behaviour

A theme that cannot be fetched, whose hash differs, or that does not parse is replaced by the default
theme. Nothing about a theme can block the profile from being listed, installed or started.

## Out of scope

Custom layouts beyond the fixed templates, community fonts, animation, sound and any scripting.
