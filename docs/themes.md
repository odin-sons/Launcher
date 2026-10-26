# Layout templates and themes

Companion to [registry-format.md](registry-format.md), [registry-trust-policy.md](registry-trust-policy.md)
and [server-page.md](server-page.md). This document specifies what a community may choose about the
look of its profile. It is data only: a theme never carries code, markup or styles, so it can change
colours and pictures but not behaviour.

The design rule is that every option a community can pick is valid. The launcher does not reject an
unreadable theme, it adjusts the colours until the result is readable (see
[Readability guarantees](#readability-guarantees)), so there is no combination that produces
unreadable text.

## What a community can and cannot choose

| Can choose | Cannot choose |
|------------|---------------|
| One layout template from a fixed set | Positions, sizes or order of controls |
| Dark or light mode | The fixed header, the step strip, the Online list, the info block |
| An accent colour | The trust icon, status notices and confirmation dialogs |
| How the wizard button looks: fill, border, radius | The wizard button's position, size and label |
| The art: a colour, a gradient or a picture | Any text of the launcher itself |
| A logo and a font from the allowlist | A font file of its own |

The window has the same control placement in every layout. A template only decides how the community's
own zones share the middle of the Server step. Requests to move selectors or buttons are deliberately
not served: a launcher that looks different on every server is harder to use and easier to imitate.

### What the launcher always draws itself

* **The header**: the raven mark, the launcher name and "by OdinSons" on two lines, the community and
  server selectors and the trust icon. The name text comes from a launcher resource, never from a
  profile.
* **The step strip** and the wizard button's position and label.
* **The Online list** and the footer info block (version, copyright, site and community links).
* **The trust icon**: a shield with a tick (`approved`), a warning triangle (`needs-approval`) or a
  blocked circle (`suspended`). Each status has its own shape as well as its own colour, so it does not
  depend on colour vision, and it has an accessible name and a tooltip. It is not themable.
* **Notices and dialogs** that carry a security decision (the `needs-approval` confirmation, the
  `suspended` notice, the offer to remove files, the launcher notice on the Terms step) use the
  launcher's fixed colours.

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
  "mode": "dark",
  "accent": "#C8A24A",
  "button": {
    "fill": { "type": "gradient", "from": "#C8A24A", "to": "#8F6B1F", "direction": "horizontal" },
    "border": { "type": "solid", "color": "#E6C477", "width": 2 },
    "radius": 6
  },
  "art": { "type": "image", "file": "theme/art.jpg", "position": "right" },
  "logo": { "file": "theme/logo.png" },
  "font": "montserrat"
}
```

| Field | Rules |
|-------|-------|
| `themeSchema` | integer; a launcher that does not support the value uses the default theme |
| `layout` | one of the template ids below; an unknown id means `classic` |
| `mode` | `dark` or `light`; anything else means `dark` |
| `accent` | `#RRGGBB`; a missing or malformed value takes the default |
| `button.fill` | a paint (below) or `{ "type": "none" }`; default is a solid fill in the accent |
| `button.border` | a paint plus `width`, or `{ "type": "none" }`; `width` is an integer 0 to 4, where 0 means no border; default is none |
| `button.radius` | integer 0 to 12 (pixels); out-of-range values are clamped |
| `art` | a paint, or `{ "type": "image", "file", "position" }`; `position` is `left`, `center` or `right` and sets which part of the picture stays visible when it is cropped |
| `logo` | `file` is a path listed in `digests` |
| `font` | an id from the allowlist below; anything else means the default |

A **paint** is one of:

* `{ "type": "solid", "color": "#RRGGBB" }`
* `{ "type": "gradient", "from": "#RRGGBB", "to": "#RRGGBB", "direction": "horizontal" }`, where
  `direction` is `horizontal`, `vertical` or `diagonal`. A gradient has exactly two stops.

Fill and border are independent: a button can have a gradient fill with a solid border, a border
only, a gradient border around a solid fill, and so on. A button needs a visible edge, so `fill: none`
requires a border with `width` of at least 1; otherwise the default button style is used.

The file is at most 16 KiB. Unknown fields are ignored, so a newer theme still loads on an older
launcher; a change an older launcher must not misread bumps `themeSchema`. Alpha channels are not
accepted, so a theme cannot make a colour transparent.

### Layout templates

The community's content has three zones in the Server step: the **art** (a picture, gradient or colour
with the community name, an optional logo and a one-line tagline over it), **About** (facts and a
description) and **News**. The Online list stays at the left in every template and is not themable.

| Id | Arrangement |
|----|-------------|
| `classic` | Art on the left, About above News on the right |
| `sidebar` | About above News on the left, art on the right |
| `banner` | A wide art strip on top, About and News side by side underneath |
| `minimal` | Art fills the area, About as a compact card over it, no News |

A zone with nothing to show (no news, no description) collapses and the others take its space. The
art always exists: with no `art` field it is the default picture. The other steps (Terms, Mods,
Install) look the same in every template.

The set is fixed in the launcher. Adding a template is a launcher release, never a registry change.

### Accent and mode

`mode` selects one of two fixed palettes for the surfaces, the text and the lines. A community never
supplies surface or text colours, which is what keeps ordinary text readable by construction. The
`accent` is used for the progress bar, the current step, toggles and ticks, links, and as the default
for the button fill.

### Font allowlist

`montserrat` (the default). Further ids are added with a launcher release; fonts are bundled, never
downloaded.

## Readability guarantees

The launcher applies these rules to whatever the theme says. None of them rejects a theme; each moves
colours (keeping hue, changing lightness in small steps) until the rule holds. Contrast ratios are the
WCAG 2 relative-luminance ratio.

| What | Rule | How it is met |
|------|------|---------------|
| Panel text | At least 7 to 1 on the surface | Text and surface come from the fixed mode palette |
| Accent as an indicator | At least 3 to 1 against the surface | The accent's lightness is moved until it holds |
| Button label | At least 4.5 to 1 over every colour of the fill | The label is black or white, whichever has the better worst case; if still short, the fill's lightness is moved |
| Button edge | The border, or the fill when there is no border, is at least 3 to 1 against the surface | The border's (or fill's) lightness is moved |
| Text on the art | At least 4.5 to 1 over the brightest part behind the text | A darkening layer, below |

A gradient is checked at several points along its length, not only at its stops. Hover, pressed and
disabled states are derived by the launcher from the resulting colours, and the keyboard focus ring is
always the launcher's own.

### The darkening layer on the art

Text is never drawn on the raw picture. The name, logo and tagline sit at the bottom left of the art
on a black layer whose strength the launcher computes:

1. After the picture's hash has matched and it has been decoded, the launcher downsamples the area
   behind the text and takes its brightest region (an average over a small cell, so one stray bright
   pixel does not decide).
2. It raises the layer's opacity from 0 in steps of 5% and uses the first step at which white text
   reaches 4.5 to 1 over that region, measured on the composited colour that is actually painted.
3. The layer is strongest behind the text and fades out above it, so the rest of the picture keeps its
   colours.

No picture is rejected for being bright: the darkest result any picture needs is below 60%. A solid
colour or a gradient takes the same path, using its brightest point. The art has no other text and no
controls on it.

### What the rules do not judge

They guarantee legibility, not taste: a theme can still be ugly. They also do not look at what a
picture shows. That is the registry review: a maintainer sees the theme and its images in the pull
request, like everything else the community submits.

## Limits

| Item | Limit |
|------|-------|
| `theme.json` | 16 KiB |
| Logo | PNG, at most 256 KiB and 512 x 512 pixels |
| Art picture | PNG or JPEG, at most 1 MiB and 1920 x 1080 pixels |

Images are decoded only after the hash matches, from a size-bounded buffer; the decoded dimensions are
checked before the bitmap is kept. Animated and vector formats are not accepted. A file that breaks a
limit is ignored and the default picture is used.

## Failure behaviour

A theme that cannot be fetched, whose hash differs, or that does not parse is replaced by the default
theme. A field that is malformed falls back to its default and the rest of the theme still applies.
Nothing about a theme can block the profile from being listed, installed or started.

## The theme editor

Communities should not have to write `theme.json` by hand. The registry repository carries a small
static web page, the **theme editor**, under `editor/`.

* It is a plain HTML and JavaScript page with no server side: it needs no account, stores nothing and
  works offline once loaded. Pictures are processed in the browser and never uploaded.
* It is published as a Cloudflare Pages project of its own, connected to the registry repository and
  built from `editor/`, separate from the odinsons.club site so the two cannot break each other.
* The pickers can only produce valid themes: it previews the window exactly as the launcher will draw
  it, including the adjustments listed above, and shows what it changed.
* It resizes and re-encodes a picture to fit the limits, computes the SHA-256 of each file with the
  browser's Web Crypto, and outputs `theme.json`, the image files and the `digests` lines to paste into
  the registry entry.

The readability rules exist twice, in C# in the launcher and in JavaScript in the editor. They are kept
equal by a shared file of test vectors, `theme-vectors.json`, in the registry repository: inputs and the
expected adjusted colours, label colour and darkening step. Both implementations run against it in CI.

The editor is a convenience, never a trust boundary: the launcher applies every rule itself and ignores
what an editor claims.

## Out of scope

Custom layouts beyond the fixed templates, moving or reordering controls, per-control colours,
translucent panels, community fonts, animation, sound and any scripting.
