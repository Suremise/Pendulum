"""Draws Pendulum's icons: the SVG masters, the .ico and .png files, and the preview sheet.

    python assets/export.py [--family NAME=PATH.ico ...]

Build-only (Python 3.10+ and Pillow; see assets/README.md). Every icon is described once, below,
as a list of shapes on a 16-unit grid; the same list is written as SVG and drawn as pixels. Each
pixel size is drawn on its own (supersampled, then reduced), not shrunk from a big image, and
32 px and below use the small drawing, flat and bolder, so they read on a taskbar.

The icons are in the Tools family's style: one bold circle with a vertical gradient, a soft
gloss ring, a dark face and a simple glyph in the brand's light tones. Pendulum's glyph is its
own, kept from its first icon: a clock face with a pendulum swinging across it. The machinery
(shapes, SVG, pixels, contrast) is copied and adapted from the family's other export scripts.

`--family` adds other apps' icons to the preview sheet for comparison (their .ico files); the
sheet is for review only, and nothing of theirs is written anywhere else.
"""

from __future__ import annotations

import argparse
import math
import sys
from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ASSETS = Path(__file__).resolve().parent
GRID = 16.0

APP_ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
APP_PNG_SIZES = [64, 128, 256, 512, 1024]
TRAY_ICO_SIZES = [16, 20, 24, 32]
# Up to this size, the small drawing (flat, bolder).
SMALL_UP_TO = 32

# The palette (assets/README.md lists it as tokens). Fixed: the same in both themes. Rose, clear
# of Hive's honey amber, Dialect's violet, Replica's teal, Legible's blue and Overmark's copper.
ROSE_100 = "#FFE1EA"  # The large icon's ticks and rod.
ROSE_300 = "#FF8FB0"  # Gradient top; the large icon's bob.
ROSE_400 = "#F0567F"  # Gradient middle.
ROSE_500 = "#D23A66"  # The small icon's and the tray's flat disc.
ROSE_600 = "#A3234B"  # Gradient bottom.
FACE = "#2E1420"  # The face, top.
FACE_DEEP = "#1A0A12"  # The face, bottom, and its ring; the small icon's glyph.
WHITE = "#FFFFFF"


# --- Shapes ------------------------------------------------------------------------------------


@dataclass
class Gradient:
    """A vertical gradient over the shape's box: (offset 0 to 1, colour, opacity) stops."""

    stops: list[tuple[float, str, float]]


Paint = str | Gradient | None


@dataclass
class Circle:
    cx: float
    cy: float
    r: float
    fill: Paint = None
    stroke: Paint = None
    width: float = 0.0  # A stroke is centred on the circle, as in SVG.
    opacity: float = 1.0


@dataclass
class Arc:
    """A stroke along a circle, from `start` to `end` degrees, clockwise on screen (0 = 3 o'clock)."""

    cx: float
    cy: float
    r: float
    start: float
    end: float
    width: float
    stroke: Paint = None


@dataclass
class Polygon:
    points: list[tuple[float, float]]
    fill: Paint = None


Shape = Circle | Arc | Polygon


@dataclass
class Icon:
    shapes: list[Shape] = field(default_factory=list)

    def add(self, *shapes: Shape) -> "Icon":
        self.shapes.extend(shapes)
        return self


# --- The drawings ------------------------------------------------------------------------------


def bar(p0: tuple[float, float], p1: tuple[float, float], width: float, ink: str) -> list[Shape]:
    """A straight stroke from `p0` to `p1` with round ends."""
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    n = math.hypot(dx, dy) or 1.0
    ox, oy = -dy / n * width / 2, dx / n * width / 2
    body = Polygon(
        [(p0[0] + ox, p0[1] + oy), (p1[0] + ox, p1[1] + oy), (p1[0] - ox, p1[1] - oy), (p0[0] - ox, p0[1] - oy)],
        fill=ink,
    )
    return [body, Circle(p0[0], p0[1], width / 2, fill=ink), Circle(p1[0], p1[1], width / 2, fill=ink)]


# The pendulum hangs from above the face's centre and is caught swung to the right, as on
# Pendulum's first icon: by this much on the large icon, a little more on the small one, so the
# swing still shows at 16 px. The small icon's ticks sit out at the rim, clear of the bob.
SWING = 18.0
SWING_SMALL = 20.0


def swung(pivot: tuple[float, float], length: float, degrees: float) -> tuple[float, float]:
    """Where a pendulum of `length` hung from `pivot` is, swung `degrees` from straight down
    (positive: to the right)."""
    a = math.radians(degrees)
    return pivot[0] + length * math.sin(a), pivot[1] + length * math.cos(a)


def clock(
    ticks_at: float,
    tick_len: float,
    tick_w: float,
    pivot: tuple[float, float],
    length: float,
    rod_w: float,
    pivot_r: float,
    bob_r: float,
    ink: str,
    bob: str,
    swing: float = SWING,
) -> list[Shape]:
    """The glyph: ticks at 12, 3 and 9 o'clock (the bob swings over 6), the pivot, the rod and
    the bob."""
    shapes: list[Shape] = []
    for deg in (270.0, 0.0, 180.0):  # 12, 3 and 9 o'clock, clockwise from 3 on screen
        a = math.radians(deg)
        outer = (8 + ticks_at * math.cos(a), 8 + ticks_at * math.sin(a))
        inner = (8 + (ticks_at - tick_len) * math.cos(a), 8 + (ticks_at - tick_len) * math.sin(a))
        shapes += bar(outer, inner, tick_w, ink)
    end = swung(pivot, length, swing)
    shapes += bar(pivot, end, rod_w, ink)
    shapes += [Circle(pivot[0], pivot[1], pivot_r, fill=ink), Circle(end[0], end[1], bob_r, fill=bob)]
    return shapes


def app_icon_large() -> Icon:
    pivot, length = (8.0, 5.4), 4.3
    return Icon().add(
        Circle(8, 8, 7.375, fill=Gradient([(0, ROSE_300, 1), (0.55, ROSE_400, 1), (1, ROSE_600, 1)])),
        Circle(8, 8, 6.6875, stroke=Gradient([(0, WHITE, 0.35), (1, WHITE, 0)]), width=0.1875, opacity=0.6),
        Circle(8, 8, 5.625, fill=Gradient([(0, FACE, 1), (1, FACE_DEEP, 1)]), stroke=FACE_DEEP, width=0.5),
        *clock(4.55, 0.85, 0.55, pivot, length, 0.6, 0.6, 1.45, ROSE_100, ROSE_300),
    )


def small_glyph(heavy: bool = False) -> list[Shape]:
    """The small glyph in the deep face colour, bolder: the ticks out at the rim, a bigger bob."""
    extra = 0.25 if heavy else 0.0
    return clock(
        6.4, 1.3, 1.25 + extra, (8.0, 5.6), 4.4, 1.45 + extra, 1.0 + extra / 2, 2.1 + extra / 2, FACE_DEEP, FACE_DEEP, SWING_SMALL
    )


def app_icon_small(size: int) -> Icon:
    return Icon().add(Circle(8, 8, 7.5, fill=ROSE_500), *small_glyph())


def app_icon(size: int) -> Icon:
    return app_icon_small(size) if size <= SMALL_UP_TO else app_icon_large()


def tray_icon(size: int) -> Icon:
    """The small icon filling its square, with heavier strokes. The rose disc reads on light and
    dark taskbars alike, so one tray icon serves both. The tray shows no states: its tooltip
    names the next reminder."""
    return Icon().add(Circle(8, 8, 7.75, fill=ROSE_500), *small_glyph(heavy=True))


# --- SVG ---------------------------------------------------------------------------------------


def fmt(v: float) -> str:
    return f"{v:.3f}".rstrip("0").rstrip(".")


class Svg:
    def __init__(self) -> None:
        self.defs: list[str] = []

    def paint(self, p: Paint) -> str:
        if p is None:
            return "none"
        if isinstance(p, str):
            return p
        n = len(self.defs) + 1
        stops = "".join(
            f'<stop offset="{fmt(o)}" stop-color="{c}"' + (f' stop-opacity="{fmt(a)}"' if a != 1 else "") + "/>"
            for o, c, a in p.stops
        )
        self.defs.append(f'<linearGradient id="g{n}" x1="0" y1="0" x2="0" y2="1">{stops}</linearGradient>')
        return f"url(#g{n})"

    def attrs(self, fill: Paint, stroke: Paint = None, width: float = 0.0) -> str:
        out = f'fill="{self.paint(fill)}"'
        if stroke:
            out += f' stroke="{self.paint(stroke)}" stroke-width="{fmt(width)}"'
        return out

    def shape(self, shape: Shape) -> str:
        if isinstance(shape, Circle):
            opacity = f' opacity="{fmt(shape.opacity)}"' if shape.opacity != 1 else ""
            return (
                f'<circle cx="{fmt(shape.cx)}" cy="{fmt(shape.cy)}" r="{fmt(shape.r)}"'
                f" {self.attrs(shape.fill, shape.stroke, shape.width)}{opacity}/>"
            )
        if isinstance(shape, Arc):
            a0, a1 = math.radians(shape.start), math.radians(shape.end)
            x0, y0 = shape.cx + shape.r * math.cos(a0), shape.cy + shape.r * math.sin(a0)
            x1, y1 = shape.cx + shape.r * math.cos(a1), shape.cy + shape.r * math.sin(a1)
            large = 1 if (shape.end - shape.start) % 360 > 180 else 0
            d = f"M{fmt(x0)} {fmt(y0)}A{fmt(shape.r)} {fmt(shape.r)} 0 {large} 1 {fmt(x1)} {fmt(y1)}"
            return f'<path d="{d}" {self.attrs(None, shape.stroke, shape.width)}/>'
        if isinstance(shape, Polygon):
            points = " ".join(f"{fmt(x)},{fmt(y)}" for x, y in shape.points)
            return f'<polygon points="{points}" {self.attrs(shape.fill)}/>'
        raise TypeError(shape)


def to_svg(icon: Icon) -> str:
    svg = Svg()
    body = "".join(svg.shape(s) for s in icon.shapes)
    defs = f"<defs>{''.join(svg.defs)}</defs>" if svg.defs else ""
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 16 16">\n'
        "<!-- Pendulum icon (MIT). Generated by assets/export.py: edit that, not this. -->\n"
        f"{defs}{body}\n</svg>\n"
    )


# --- Pixels ------------------------------------------------------------------------------------


def rgb(colour: str) -> tuple[int, int, int]:
    c = colour.lstrip("#")
    return int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16)


def supersample(size: int) -> int:
    """How many times bigger a size is drawn before it's reduced: less for the big ones, which
    have pixels enough."""
    return 8 if size <= 64 else 4 if size <= 256 else 2


def fill_image(paint: str | Gradient, top: float, bottom: float, big: int) -> Image.Image:
    """`paint` over the whole canvas; a gradient runs from `top` to `bottom` (pixels)."""
    if isinstance(paint, str):
        return Image.new("RGBA", (big, big), (*rgb(paint), 255))
    column = Image.new("RGBA", (1, big))
    stops = paint.stops
    for y in range(big):
        t = min(max((y + 0.5 - top) / max(bottom - top, 1e-6), 0.0), 1.0)
        for (o0, c0, a0), (o1, c1, a1) in zip(stops, stops[1:]):
            if t <= o1 or (o1, c1, a1) == stops[-1]:
                f = 0.0 if o1 == o0 else min(max((t - o0) / (o1 - o0), 0.0), 1.0)
                r0, r1 = rgb(c0), rgb(c1)
                colour = tuple(round(r0[i] + (r1[i] - r0[i]) * f) for i in range(3))
                column.putpixel((0, y), (*colour, round(255 * (a0 + (a1 - a0) * f))))
                break
    return column.resize((big, big), Image.Resampling.NEAREST)


def parts(shape: Shape, k: float, big: int) -> list[tuple[Image.Image, Paint, float, float]]:
    """Each painted part of `shape` as (its coverage mask, its paint, and the top and bottom of
    the box a gradient spans), scaled by `k` pixels per grid unit."""

    def mask() -> tuple[Image.Image, ImageDraw.ImageDraw]:
        m = Image.new("L", (big, big), 0)
        return m, ImageDraw.Draw(m)

    out = []
    if isinstance(shape, Circle):
        box = [(shape.cx - shape.r) * k, (shape.cy - shape.r) * k, (shape.cx + shape.r) * k, (shape.cy + shape.r) * k]
        if shape.fill:
            m, d = mask()
            d.ellipse([box[0], box[1], box[2] - 1, box[3] - 1], fill=255)
            out.append((m, shape.fill, box[1], box[3]))
        if shape.stroke:
            m, d = mask()
            h = shape.width * k / 2
            d.ellipse([box[0] - h, box[1] - h, box[2] + h - 1, box[3] + h - 1], fill=255)
            d.ellipse([box[0] + h, box[1] + h, box[2] - h - 1, box[3] - h - 1], fill=0)
            out.append((m, shape.stroke, box[1], box[3]))
    elif isinstance(shape, Arc):
        outer = shape.r + shape.width / 2
        box = [(shape.cx - outer) * k, (shape.cy - outer) * k, (shape.cx + outer) * k - 1, (shape.cy + outer) * k - 1]
        m, d = mask()
        d.arc(box, shape.start, shape.end, fill=255, width=round(shape.width * k))
        out.append((m, shape.stroke, box[1], box[3]))
    elif isinstance(shape, Polygon):
        m, d = mask()
        d.polygon([(x * k, y * k) for x, y in shape.points], fill=255)
        ys = [y * k for _, y in shape.points]
        out.append((m, shape.fill, min(ys), max(ys)))
    else:
        raise TypeError(shape)
    return out


def render(icon: Icon, size: int) -> Image.Image:
    big = size * supersample(size)
    k = big / GRID
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    for shape in icon.shapes:
        opacity = shape.opacity if isinstance(shape, Circle) else 1.0
        for m, paint, top, bottom in parts(shape, k, big):
            layer = fill_image(paint, top, bottom, big)
            a = Image.composite(layer.getchannel("A"), Image.new("L", (big, big), 0), m)
            if opacity != 1.0:
                a = a.point(lambda v: round(v * opacity))
            layer.putalpha(a)
            canvas = Image.alpha_composite(canvas, layer)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def save_ico(path: Path, draw, sizes: list[int]) -> dict[int, Image.Image]:
    images = {s: draw(s) for s in sizes}
    largest = images[sizes[-1]]
    # Every frame as a 32-bit BMP, the classic layout: WPF's icon decoder then picks the frame
    # nearest the size it's shown at (with PNG frames it took the 16 px one and stretched it).
    largest.save(
        path, format="ICO", sizes=[(s, s) for s in sizes], append_images=[images[s] for s in sizes[:-1]], bitmap_format="bmp"
    )
    return images


# --- Contrast ----------------------------------------------------------------------------------


def contrast(a: str, b: str) -> float:
    """WCAG 2's contrast ratio between two opaque colours."""

    def lum(c: str) -> float:
        v = [x / 255 for x in rgb(c)]
        v = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in v]
        return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]

    la, lb = lum(a), lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


LIGHT_TASKBAR = "#F3F3F3"
DARK_TASKBAR = "#202020"


# --- The preview sheet -------------------------------------------------------------------------


def font(size: int) -> ImageFont.ImageFont:
    for name in ["segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"]:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


PREVIEW_SIZES = [16, 24, 32, 48, 256]


def family_icon(path: Path, size: int) -> Image.Image:
    """Another app's icon at `size`, from its .ico (the nearest size it has, scaled)."""
    with Image.open(path) as ico:
        sizes = sorted(ico.info.get("sizes", {(size, size)}))
        best = min(sizes, key=lambda s: (abs(s[0] - size), -s[0]))
        ico.size = best
        image = ico.convert("RGBA")
    return image if image.size == (size, size) else image.resize((size, size), Image.Resampling.LANCZOS)


def preview(app: dict[int, Image.Image], tray: dict[int, Image.Image], family: list[tuple[str, Path]]) -> Image.Image:
    rows: list[tuple[str, dict[int, Image.Image]]] = [("Pendulum", app)]
    rows += [(name, {s: family_icon(path, s) for s in PREVIEW_SIZES}) for name, path in family]
    row_h = 300
    tray_y = 100 + len(rows) * 2 * row_h + 40
    sheet = Image.new("RGB", (1200, tray_y + 2 * 140 + 10), "#FAFAFA")
    d = ImageDraw.Draw(sheet)
    title, label, small = font(26), font(16), font(13)

    d.text((30, 20), "Pendulum's icons", fill="#111", font=title)
    note = "Actual pixels at 16, 24, 32, 48 and 256 px"
    if family:
        note += ", beside the family's (" + ", ".join(n for n, _ in family) + ")"
    d.text((30, 58), note, fill="#444", font=label)

    y = 100
    for name, images in rows:
        for bg, fg in [(LIGHT_TASKBAR, "#333"), (DARK_TASKBAR, "#DDD")]:
            d.rectangle([30, y, 1170, y + row_h - 10], fill=bg)
            d.text((40, y + 8), f"{name}, on {'light' if bg == LIGHT_TASKBAR else 'dark'}", fill=fg, font=small)
            x = 50
            for s in PREVIEW_SIZES:
                shown = images[s]
                sheet.paste(shown, (x, y + 30 + (256 - s) // 2 if s < 256 else y + 30), shown)
                d.text((x, y + row_h - 34), f"{s} px", fill=fg, font=small)
                x += max(s, 40) + 40
            y += row_h

    d.text(
        (30, tray_y - 30),
        "Tray: 16, 20, 24 and 32 px (100 to 200% scaling), then 16 px at 400%",
        fill="#444",
        font=label,
    )
    for row, (bg, fg, name) in enumerate([(LIGHT_TASKBAR, "#333", "light"), (DARK_TASKBAR, "#DDD", "dark")]):
        y = tray_y + row * 140
        d.rectangle([30, y, 1170, y + 128], fill=bg)
        d.text((40, y + 6), f"{name} taskbar", fill=fg, font=small)
        cx = 40
        for s in TRAY_ICO_SIZES:
            sheet.paste(tray[s], (cx, y + 44 + (32 - s) // 2), tray[s])
            cx += s + 10
        zoomed = tray[16].resize((64, 64), Image.Resampling.NEAREST)
        sheet.paste(zoomed, (cx + 4, y + 28), zoomed)
    return sheet


# --- Main --------------------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--family", action="append", default=[], metavar="NAME=PATH.ico", help="another app's icon, for the preview sheet only")
    args = parser.parse_args()
    family = []
    for item in args.family:
        name, _, path = item.partition("=")
        family.append((name, Path(path)))

    icon_dir = ASSETS / "icon"
    tray_dir = ASSETS / "tray"
    icon_dir.mkdir(exist_ok=True)
    tray_dir.mkdir(exist_ok=True)

    (icon_dir / "pendulum.svg").write_text(to_svg(app_icon_large()), encoding="utf-8", newline="\n")
    (icon_dir / "pendulum-small.svg").write_text(to_svg(app_icon_small(32)), encoding="utf-8", newline="\n")
    app = save_ico(icon_dir / "pendulum.ico", lambda s: render(app_icon(s), s), APP_ICO_SIZES)
    for s in APP_PNG_SIZES:
        image = app.get(s) or render(app_icon(s), s)
        app[s] = image
        image.save(icon_dir / f"pendulum-{s}.png", optimize=True)
    for s in PREVIEW_SIZES:
        app.setdefault(s, render(app_icon(s), s))

    (tray_dir / "tray.svg").write_text(to_svg(tray_icon(32)), encoding="utf-8", newline="\n")
    tray = save_ico(tray_dir / "tray.ico", lambda s: render(tray_icon(s), s), TRAY_ICO_SIZES)

    preview(app, tray, family).save(ASSETS / "preview.png", optimize=True)

    print(f"Wrote the icons to {icon_dir}, {tray_dir} and {ASSETS / 'preview.png'}")
    print("Contrast (WCAG 2):")
    print(f"  the small icon's and tray's disc {ROSE_500} on a light taskbar: {contrast(ROSE_500, LIGHT_TASKBAR):.2f}:1")
    print(f"  on a dark taskbar: {contrast(ROSE_500, DARK_TASKBAR):.2f}:1")
    print(f"  the glyph {FACE_DEEP} on the disc: {contrast(FACE_DEEP, ROSE_500):.2f}:1")
    print(f"  the large icon's ticks and rod {ROSE_100} on the face {FACE}: {contrast(ROSE_100, FACE):.2f}:1")
    print(f"  the large icon's bob {ROSE_300} on the face {FACE}: {contrast(ROSE_300, FACE):.2f}:1")
    return 0


if __name__ == "__main__":
    sys.exit(main())
