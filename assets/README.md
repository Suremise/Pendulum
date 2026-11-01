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
| `--accent-fill` | `#C7305E` | `#F56A8F` | Primary buttons, toggles, checked boxes; the selected tab's bar, the panel's frame, today in the calendar | Its label (white on light, black on dark): 5.23 / 7.34. On the backgrounds: 4.51 / 4.48 |
| `--accent-hover` | `#A3234B` | `#FF8FB0` | A primary button under the pointer; WPF-UI's other accent fills | Its label: 7.24 / 9.81 |
| `--accent-text` | `#A3234B` | `#FF8FB0` | Accent-coloured text and icons (the Settings section icons) | 6.24 / 5.99 |
| `--accent-link` | `#A3234B` | `#FF8FB0` | Links | 6.24 / 5.99 |
| `--accent-system` | `#D23A66` | `#F0567F` | The selection highlight | |
| `--accent-soft` | `#C7305E` at 16% | `#F56A8F` at 16% | The selected tab's background | |

The alert popup keeps its own dark look in both themes: a plum face (`#2A1720`, the icon's face
colour), a `#8E3A58` frame, the "PENDULUM" label in `#E3A9BD` (8.6:1) and the subtitle in
`#EBD3DC` (12.0:1).
