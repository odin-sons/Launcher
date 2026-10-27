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
| One layout template from a fixed set, which frames all four steps | Positions, sizes or order of controls |
| The side of the Online list on the Server step: left or right | The fixed header, the step strip, the info block |
| A colour scheme for a light launcher and one for a dark launcher: accent, button colours, art | Whether the launcher is light or dark: that is the player's setting |
| How the wizard button looks: fill, border, radius | The wizard button's position, size and label |
| The art: a colour, a gradient or a picture | The trust icon, status notices and confirmation dialogs |
| A logo and a font from the allowlist | Any text of the launcher itself, or a font file of its own |

A template decides how the community's art frames the window and how the zones of the Server step
share the middle. The Online list is a list, not a control, so its side is a community choice; the
controls (the selectors, the step strip, the wizard button) never move. Requests to reposition
controls are deliberately not served: a launcher that looks different on every server is harder to use
and easier to imitate.

### What the launcher always draws itself

* **The header**: the raven mark, the launcher name and "by OdinSons" on two lines, the community and
  server selectors and the trust icon. The name text comes from a launcher resource, never from a
  profile. The header uses the same dark colours in both schemes, so the mark and the trust icon look
  alike everywhere.
* **The step strip**, the wizard button's position and label, and the player's own settings next to the
  language selector.
* **The contents of the Online list**, its refresh button, and the footer info block (version,
  copyright, site and community links).
* **The trust icon**: a shield with a tick (`approved`), a warning triangle (`needs-approval`) or a
  blocked circle (`suspended`). Each status has its own shape as well as its own colour, so it does not
  depend on colour vision, and it has an accessible name and a tooltip. It is not themable.
* **Notices and dialogs** that carry a security decision (the `needs-approval` confirmation, the
  `suspended` notice, the offer to remove files, the launcher notice on the Terms step) use the
  launcher's fixed colours for the active scheme.

## Light and dark

A theme does not choose between a light and a dark launcher. The player does, so nobody who needs one
or the other is overruled by a community's taste. A theme carries a colour scheme for each, and the
launcher shows the one that matches the player's setting.

* The launcher has a theme setting with three values: **System**, **Light** and **Dark**. It sits next
  to the language selector, applies to every profile including the built-in one, and is kept in
  `config.ini` as `ColorScheme`. The default is System.
* With System the launcher follows the operating system, and switches while it is open when the
  system does.
* The launcher's own palettes (surfaces, text, lines and its notices) come in a light and a dark
  version, are fixed, and are never taken from a theme.
* A theme describes its colours once per scheme, under `schemes.dark` and `schemes.light`. Only colours
  differ between them. The template, the Online list's side, the logo, the font and the shape of the
  button (radius and border width) are shared.
* A scheme the theme leaves out, or cannot be used, is built from the other one: the same values,
  then adjusted to the missing scheme's palette by the readability rules. With neither, the default
  scheme is used.

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
  "online": "left",
  "font": "montserrat",
  "logo": { "file": "theme/logo.png" },
  "button": { "radius": 6, "borderWidth": 2 },
  "schemes": {
    "dark": {
      "accent": "#C8A24A",
      "buttonFill": { "type": "gradient", "from": "#C8A24A", "to": "#8F6B1F", "direction": "horizontal" },
      "buttonBorder": { "type": "solid", "color": "#E6C477" },
      "art": { "type": "image", "file": "theme/art-dark.jpg", "position": "right" }
    },
    "light": {
      "accent": "#8F6B1F",
      "buttonFill": { "type": "solid", "color": "#8F6B1F" },
      "buttonBorder": { "type": "none" },
      "art": { "type": "image", "file": "theme/art-light.jpg", "position": "right" }
    }
  }
}
```

| Field | Rules |
|-------|-------|
| `themeSchema` | integer; a launcher that does not support the value uses the default theme |
| `layout` | one of the template ids below; an unknown id means `classic` |
| `online` | `left` or `right`, the side of the Online list on the Server step; anything else means `left` |
| `font` | an id from the allowlist below; anything else means the default |
| `logo` | `file` is a path listed in `digests`; shared by both schemes |
| `button.radius` | integer 0 to 12 (pixels); out-of-range values are clamped; default 6 |
| `button.borderWidth` | integer 0 to 4; 0 means no border; default 0 |
| `schemes.<scheme>.accent` | `#RRGGBB`; a missing or malformed value takes the default |
| `schemes.<scheme>.buttonFill` | a paint (below) or `{ "type": "none" }`; default is a solid fill in the accent |
| `schemes.<scheme>.buttonBorder` | a paint or `{ "type": "none" }`; default is none; unused while `borderWidth` is 0 |
| `schemes.<scheme>.art` | a paint, or `{ "type": "image", "file", "position" }`; `position` is `left`, `center` (the default) or `right` and sets which part of the picture stays visible when it is cropped |

`<scheme>` is `dark` or `light`.

A **paint** is one of:

* `{ "type": "solid", "color": "#RRGGBB" }`
* `{ "type": "gradient", "from": "#RRGGBB", "to": "#RRGGBB", "direction": "horizontal" }`, where
  `direction` is `horizontal` (the default), `vertical` or `diagonal`. A gradient has exactly two stops.

Fill and border are independent: a button can have a gradient fill with a solid border, a border
only, a gradient border around a solid fill, and so on. A button needs a visible edge, so a scheme with
`buttonFill: none` needs a border paint and a `borderWidth` of at least 1; otherwise that scheme uses
the default fill.

The file is at most 16 KiB. Unknown fields are ignored, so a newer theme still loads on an older
launcher; a change an older launcher must not misread bumps `themeSchema`. Alpha channels are not
accepted, so a theme cannot make a colour transparent.

### Layout templates

A template does two things. On the Server step it arranges the community's three zones: the **art** (a
picture, gradient or colour with the community name, an optional logo and a one-line tagline over it),
**About** (facts and a description) and **News**. On Terms, Mods and Install it decides how the art
frames the launcher's own content, and whether that content has one column or two.

| Id | Server step | Terms, Mods and Install |
|----|-------------|-------------------------|
| `classic` | Art on the left, About above News on the right | A narrow art column on the left, content in one column |
| `sidebar` | About above News on the left, art on the right | A narrow art column on the right, content in one column |
| `banner` | A wide art strip on top, About and News side by side underneath | A slim art strip on top, content in two columns |
| `minimal` | Art fills the area, About as a compact card over it, no News | No art: the logo and name on one line on top, content in two columns |

The content of each step and how it uses one or two columns is in [server-page.md](server-page.md#steps).
A zone with nothing to show (no news, no description) collapses and the others take its space. The art
always exists: with no `art` field it is the default picture. In `minimal` the name on the other steps
sits on the launcher's own surface, in its text colour.

The Online list is a full-height column at the side that `online` names, in every template. On the
other steps there is no list.

The set of templates is fixed in the launcher. Adding a template is a launcher release, never a
registry change.

### Accent and palette

Each scheme's surfaces, text and lines come from the launcher's fixed palette for that scheme. A
community never supplies surface or text colours, which is what keeps ordinary text readable by
construction. The `accent` is used for the progress bar, the current step, toggles and ticks, links, and
as the default for the button fill.

### Font allowlist

`montserrat` (the default). Further ids are added with a launcher release; fonts are bundled, never
downloaded.

## Readability guarantees

The launcher applies these rules to whatever the theme says, to each scheme against its own palette: a
theme that reads well in the dark scheme is not assumed to read well in the light one. None of the rules
rejects a theme; each moves colours (keeping hue, changing lightness in small steps) until the rule
holds. Contrast ratios are the WCAG 2 relative-luminance ratio.

| What | Rule | How it is met |
|------|------|---------------|
| Panel text | At least 7 to 1 on the surface | Text and surface come from the launcher's fixed palette for the scheme |
| Accent as an indicator | At least 3 to 1 against the surface | The accent's lightness is moved until it holds |
| Button label | At least 4.5 to 1 over every colour of the fill | The label is black or white, whichever has the better worst case; if still short, the fill's lightness is moved |
| Button edge | The border, or the fill when there is no border, is at least 3 to 1 against the surface | The border's (or fill's) lightness is moved |
| Text on the art | At least 4.5 to 1 over the brightest part behind the text | A darkening layer, below |

A gradient is checked at several points along its length, not only at its stops. Hover, pressed and
disabled states are derived by the launcher from the resulting colours, and the keyboard focus ring is
always the launcher's own.

### The darkening layer on the art

Text is never drawn on the raw picture. The name, logo and tagline sit at the bottom left of the art
on a black layer whose strength the launcher computes. It is the same in both schemes, because the text
on the art is always white:

1. After the picture's hash has matched and it has been decoded, the launcher fits it to each frame it
   appears in (the Server art, the side column and the strip: the same picture is cropped differently
   in each). For each frame it downsamples the area behind the text and takes its brightest region (an
   average over a small cell, so one stray bright pixel does not decide).
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
| Art picture | PNG or JPEG, at most 1 MiB and 1920 x 1080 pixels each |

Each scheme may name its own art picture, so a theme can carry two. A file named by both schemes is
fetched and counted once.

Images are decoded only after the hash matches, from a size-bounded buffer; the decoded dimensions are
checked before the bitmap is kept. Animated and vector formats are not accepted. A file that breaks a
limit is ignored and the default picture is used.

## Failure behaviour

A theme that cannot be fetched, whose hash differs, or that does not parse is replaced by the default
theme. A field that is malformed falls back to its default and the rest of the theme still applies. A
scheme that is missing or unusable is built from the other one (see [Light and dark](#light-and-dark)).
Nothing about a theme can block the profile from being listed, installed or started.

## The theme editor

Communities should not have to write `theme.json` by hand. The registry repository carries a small
static web page, the **theme editor**, under `editor/`.

* It is a plain HTML and JavaScript page with no server side: it needs no account, stores nothing and
  works offline once loaded. Pictures are processed in the browser and never uploaded.
* It is published as a Cloudflare Pages project of its own, connected to the registry repository and
  built from `editor/`, separate from the odinsons.club site so the two cannot break each other.
* It edits one colour scheme at a time, dark or light, with one button to copy the dark scheme into the
  light one and another for the reverse, so a second scheme starts from the first and only the
  differences need editing.
* The pickers can only produce valid themes: it previews the window in either scheme, on any of the four
  steps, exactly as the launcher will draw it, including the adjustments listed above, and shows what it
  changed.
* It resizes and re-encodes a picture to fit the limits, computes the SHA-256 of each file with the
  browser's Web Crypto, and outputs `theme.json`, the image files and the `digests` lines to paste into
  the registry entry.

The readability rules exist twice, in C# in the launcher and in JavaScript in the editor. They are kept
equal by a shared file of test vectors, `theme-vectors.json`, in the registry repository: inputs for each
palette and the expected adjusted colours, label colour and darkening step. Both implementations run
against it in CI.

The editor is a convenience, never a trust boundary: the launcher applies every rule itself and ignores
what an editor claims.

## Open points

* The operating system's high-contrast setting. When it is on, the launcher should probably ignore the
  theme's colours altogether; how that is detected on each platform is left to the implementation.

## Out of scope

Custom layouts beyond the fixed templates, moving or reordering controls, a community forcing a
scheme, per-control colours, translucent panels, community fonts, animation, sound and any scripting.
