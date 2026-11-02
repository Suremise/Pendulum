# Pendulum's icons and sounds

The alert sounds (`Pendulum.App/Resources/Sounds/chime.wav`, `bell.wav`, `alert.wav`) are made
from sine waves by `assets/sounds/generate.py` (Python 3.10+, standard library only, build-only),
so they're Pendulum's own, under its MIT licence. Run `python assets/sounds/generate.py` to redraw
them; the same bytes come out every time. Their names stay the same, because reminders save the
name of the sound they play.


Original art for Pendulum, under Pendulum's MIT licence. No third-party icon sets, fonts or
images are used in any of the icons.

**Every icon file here is generated** by `export.py`. To change an icon, edit the
shapes in `export.py` and run it again; don't edit the SVG, ICO or PNG files by hand.

```
python assets/export.py
```

It needs Python 3.10 or later and [Pillow](https://python-pillow.org/) (`pip install pillow`;
made with Pillow 12.3.0). Both are build-only tools and don't ship in Pendulum. With the same
Pillow version, it writes the same bytes every time. The machinery (the shapes, SVG writer,
renderer and contrast check) is copied and adapted from the other Tools apps' icon scripts; the
drawings, palette and preview are Pendulum's.

The preview sheet can show other apps' icons beside Pendulum's, for comparison only:

```
python assets/export.py --family "Dialect=<path>/dialect.ico" --family "Replica=<path>/replica.ico" --family "Legible=<path>/legible.ico" --family "Overmark=<path>/overmark.ico"
```

Their icons appear only in `preview.png`, which is for review and doesn't ship.

## The files

| File | What |
|---|---|
| `icon/pendulum.svg` | The app icon's master, used for 40 px and up |
| `icon/pendulum-small.svg` | The small variant, used at 16 to 32 px: a flat disc, the glyph bolder |
| `icon/pendulum.ico` | The exe's, the installer's and the windows' icon: 16, 20, 24, 32, 40, 48, 64 and 256 px |
| `icon/pendulum-64.png` … `pendulum-1024.png` | 64, 128, 256, 512 and 1024 px, for the README and the website |
| `tray/tray.svg`, `tray/tray.ico` | The notification-area icon: 16, 20, 24 and 32 px, for light and dark taskbars alike |
| `preview.png` | Every icon at its real sizes on light and dark, for review |

The app (`Pendulum.App.csproj`) and the installer (`installer/Pendulum.iss`) use the files here
directly. The `.ico` frames are 32-bit BMPs, not PNGs: WPF's icon decoder then picks the frame
nearest the size it shows (About's 56 px, the alert's 18 px); with PNG frames it took the 16 px
one and stretched it.

## The icon

**A clock face with a swinging pendulum**, kept from Pendulum's first icon: ticks at 12, 3 and 9
o'clock, and a pendulum hung just above the centre, caught swung to the right with its bob over
6 o'clock.

It's built the way the family's icons are (Hive, Dialect, Replica, Legible, Overmark), so they
sit together on the website: one bold circle with a vertical gradient, a soft gloss ring, a dark
face, and the glyph in the brand's light tones. The shapes are on a 16-unit grid (the circle
7.375, the gloss ring 6.6875, the face 5.625 with a 0.5 ring).

- **The large icon** (40 px and up): the ticks and rod in pale rose, the bob in bright rose.
- **The small icon** (16 to 32 px), as Dialect's `icon-small.svg` is: no gradient, gloss or
  face. A flat rose disc with the glyph in the deep face colour, bolder, the ticks out at the
  rim and the swing a little wider, so the bob stays clear of the 3 o'clock tick.
- **The tray icon**, as Dialect's `tray.svg` is: the small icon filling its square with heavier
  strokes. The disc reads on either taskbar, so one file serves light and dark. The tray shows
  no states (its tooltip names the next reminder), so there's one icon.
- Each pixel size is drawn on its own, supersampled and then reduced, rather than shrunk from
  the biggest image.

**Contrast** (WCAG 2, printed by `export.py`):

| Pair | Ratio |
|---|---|
| The small icon's and tray's disc on a light taskbar (`#F3F3F3`) | 4.17:1 |
| The disc on a dark taskbar (`#202020`) | 3.52:1 |
| The glyph (`--brand-face-deep`) on the disc | 4.14:1 |
| The large icon's ticks and rod (`--brand-rose-100`) on the face | 13.91:1 |
| The large icon's bob (`--brand-rose-300`) on the face | 7.93:1 |

## Palette

The icon's colours, fixed and the same in both themes. **Rose**, clear of Hive's honey amber,
Dialect's violet, Replica's teal, Legible's blue and Overmark's copper.

| Token | Hex | Used for |
|---|---|---|
| `--brand-rose-100` | `#FFE1EA` | The large icon's ticks and rod |
| `--brand-rose-300` | `#FF8FB0` | Gradient top; the large icon's bob |
| `--brand-rose-400` | `#F0567F` | Gradient middle |
| `--brand-rose-500` | `#D23A66` | The small icon's and the tray's flat disc |
| `--brand-rose-600` | `#A3234B` | Gradient bottom |
| `--brand-face` | `#2E1420` | The face, top |
| `--brand-face-deep` | `#1A0A12` | The face, bottom, and its ring; the small icon's glyph |

## The app's accent

The app's UI takes its accent from the icon: rose, with a set of shades for each theme
(`Pendulum.App/Services/AppThemeManager.cs`, which applies them; WPF-UI draws its controls from
them). Contrast is WCAG 2, against the themes' backgrounds (light `#FAFAFA` to `#EEEEEE`, dark
`#202020` to `#323232`).

| Token | Light | Dark | Used for | Contrast (worst case) |
|---|---|---|---|---|
| `--accent-fill` | `#C7305E` | `#F56A8F` | Primary buttons, checked boxes; the selected tab's and Settings section's bar, today in the calendar | Its label (white on light, black on dark): 5.23 / 7.34. On the backgrounds: 4.51 / 4.48 |
| `--accent-hover` | `#A3234B` | `#FF8FB0` | A primary button under the pointer; WPF-UI's other accent fills | Its label: 7.24 / 9.81 |
| `--accent-text` | `#A3234B` | `#FF8FB0` | Accent-coloured text (the calendar's "+N more") | 6.24 / 5.99 |
| `--accent-link` | `#A3234B` | `#FF8FB0` | Links | 6.24 / 5.99 |
| `--accent-system` | `#D23A66` | `#F0567F` | The selection highlight | |
| `--accent-soft` | `#C7305E` at 16% | `#F56A8F` at 16% | The selected tab's and Settings section's background | |

**Rose is the brand's alone:** primary buttons, check boxes, the selection, today in the calendar,
focus and links. Everything else that colour tells apart takes the palette below, and the rest is
the theme's own neutrals. `AppThemeManager` is the only place a colour is written:
`ThemeColourTests` fails on a colour written in any other XAML or C# file of the app, and checks
the contrast of every brush it sets, at rest, under the pointer and pressed.

The alert popup keeps its own dark look in both themes: a plum face (`#2A1720`, the icon's face
colour), a `#8E3A58` frame, the "PENDULUM" label in `#E3A9BD` (8.6:1) and the subtitle in
`#EBD3DC` (12.0:1).

## The palette

Everything that colour tells apart, other than the brand, takes one of eight **muted colours
chosen round the colour wheel from rose**: the Reminders list's badges and its on/off toggles, the
calendar's chips, and each reminder's own colour. The panel has no rose frame, and the sort arrows
and the Scheduled badge are grey. Rose isn't one of the eight, so nothing in the palette is
confused with the app's own accent.

Each token has a **shade** per theme, a solid colour 3:1 or more against the panels' surface (light
`#FBFBFB`, dark `#2D2D2D`): a dot, a chip's bar, a swatch. Chips and badges are a **tint**, the
shade at 20% (light) or 30% (dark) over the surface, with the theme's own text on it (light
`#1C1C1C`, dark white), 4.5:1 or more. (`ThemeColourTests` checks both against every surface
they sit on, light `#FFFFFF` to `#EEEEEE` and dark `#202020` to `#3A3A3A`.) The shades were picked in OKLCH (the hue fixed for each,
chroma 0.07 to 0.11, lightness varied between them) so that every pair stays apart for colour-blind
eyes too. A reminder's name is always written beside its colour, so colour is never the only cue.

The figures are WCAG 2 contrast on the panels' surface, and CIEDE2000 differences:

| Token | Light | Dark | Light: on surface / text on tint | Dark: on surface / text on tint |
|---|---|---|---|---|
| `--palette-terracotta` | `#874128` | `#C77C61` | 7.20 / 12.04 | 4.25 / 8.85 |
| `--palette-ochre` | `#946A10` | `#CEA153` | 4.69 / 12.73 | 5.80 / 7.83 |
| `--palette-olive` | `#81894F` | `#C2CB8E` | 3.61 / 13.28 | 8.02 / 6.78 |
| `--palette-sage` | `#456F4E` | `#89B591` | 5.58 / 12.53 | 5.96 / 7.73 |
| `--palette-teal` | `#269595` | `#4CB3B2` | 3.49 / 13.20 | 5.51 / 8.12 |
| `--palette-blue` | `#4E77A1` | `#6E96BF` | 4.53 / 12.88 | 4.45 / 8.64 |
| `--palette-violet` | `#614D92` | `#BAA7F4` | 6.80 / 12.18 | 6.51 / 7.43 |
| `--palette-plum` | `#724869` | `#E3B2D7` | 7.13 / 12.13 | 7.62 / 6.98 |

The closest two colours, as each kind of eye sees them (simulated with Machado, Oliveira and
Fernandes 2009 at full strength; about 10 or more reads as clearly different side by side, and
`ThemeColourTests` fails under 9):

| Vision | Light: closest pair, ΔE2000 | Dark: closest pair, ΔE2000 |
|---|---|---|
| Typical | Violet and Plum, 13.0 | Olive and Sage, 13.5 |
| Deuteranopia | Blue and Violet, 9.2 | Terracotta and Ochre, 9.6 |
| Protanopia | Violet and Plum, 9.9 | Olive and Sage, 9.6 |
| Tritanopia | Sage and Blue, 9.2 | Teal and Blue, 9.6 |

**What takes which:** Upcoming (the badge, and the calendar's chips until each reminder has its
own colour) in teal; the on/off toggle, when on, in teal with the theme's text-on-accent tick;
the Filters button and its dot, while filters are on, in teal; Sound only in blue, Sound and speech
in ochre, Speech only in plum, each as a tint with the theme's text. Fixed, Scheduled, Disabled and
Spent stay neutral greys. Error messages are Windows' own critical red (`#C42B1C` light, `#FF99A4`
dark).

**In the theme** each token is a pair of brushes in `AppThemeManager`, `PendulumPalette<Name>Brush`
for the shade and `PendulumPalette<Name>TintBrush` for the tint, set per theme as the accent's are,
so a theme switch recolours everything at once. The views use them through named brushes for what
they mean (`PendulumChipBrush`, `PendulumToggleOnBrush`, `PendulumBadgeSoundOnlyBrush` and so on).

The light teal and blue were darkened a little from the first proposal (`#2A9898` and `#537AA2`),
so that teal keeps 3:1 on the lightest surface too.

### How a reminder's colour is chosen and saved (planned)

- **Saved by name, not by value:** a new optional property on the reminder in `triggers.json`,
  `"Color": "teal"` (one of the eight token names). The name, not a hex value, so the theme picks
  the shade, and a later change to a shade changes every reminder of that colour.
- **Chosen automatically** when a reminder is made (with Add reminder, Quick Reminder, or an
  import of reminders that have none): the colour fewest reminders have, ties going in the order
  teal, terracotta, blue, ochre, violet, sage, plum, olive (each next to colours far from it, so
  reminders made together are told apart).
- **Changeable in the reminder's editor**, a row of the eight swatches under the name; the chosen
  one is ringed and named.
- **Reminders saved by earlier versions load unchanged.** They have no `Color`, and nothing is
  written for them until they're edited: until then each shows a colour worked out from its `Id`
  (the same one every time, on every PC), so the calendar never reshuffles. Saving one from the
  editor writes the colour it shows. A name this version doesn't know (from a later version) is
  shown as the worked-out colour and kept as it is when saved, so going back and forth between
  versions loses nothing. Earlier versions ignore the property (one that saves the reminder drops
  it, and the colour is worked out from the `Id` again).
