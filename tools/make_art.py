#!/usr/bin/env python3
"""Exports all of Selene's artwork from the master drawings in tools/art/.

Needs rsvg-convert (librsvg) and Pillow. Re-run after changing the design: python3 tools/make_art.py
"""
import re
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT / "tools" / "art"
IMAGES = ROOT / "theme" / "Images"
LOGO_DIR = ROOT / "docs" / "logo"

MOONLIGHT = "#EAF3FF"
NAVY = "#0A142C"
NAVY_TOP = "#102A5C"
NAVY_BOTTOM = "#03081A"
PAPER = "#F4F7FB"

# horizontal centre of the mark's drawing inside its 100x100 box is 50; vertical centre of the ink is ~53.5
MARK_INK_CENTER_Y = 53.5


def inner(svg_file):
    text = (ART / svg_file).read_text(encoding="utf-8")
    view_box = re.search(r'viewBox="([^"]+)"', text).group(1)
    body = text[text.index(">", text.index("<svg")) + 1 : text.rindex("</svg>")]
    return view_box, body


MARK_BOX, MARK = inner("selene-mark.svg")
WORD_BOX, WORD = inner("selene-wordmark.svg")
WORD_W, WORD_H = (float(v) for v in WORD_BOX.split()[2:])


def mark(x, y, size, color, opacity=1.0):
    return (f'<svg x="{x}" y="{y}" width="{size}" height="{size}" viewBox="{MARK_BOX}" '
            f'style="color:{color}" opacity="{opacity}">{MARK}</svg>')


def wordmark(cx, y, width, color):
    height = width * WORD_H / WORD_W
    return (f'<svg x="{cx - width / 2}" y="{y}" width="{width}" height="{height}" viewBox="{WORD_BOX}" '
            f'style="color:{color}">{WORD}</svg>')


def doc(width, height, body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
            f'viewBox="0 0 {width} {height}"><defs>{defs}</defs>{body}</svg>')


def render(svg, out_png, width=None):
    out_png.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", suffix=".svg", delete=False, encoding="utf-8") as tmp:
        tmp.write(svg)
    cmd = ["rsvg-convert", tmp.name, "-o", str(out_png)]
    if width:
        cmd[1:1] = ["-w", str(width)]
    subprocess.run(cmd, check=True)
    Path(tmp.name).unlink()
    if out_png.is_relative_to(ROOT):
        print("wrote", out_png.relative_to(ROOT))


def mark_alone(size=256):
    s = size * 0.9
    return doc(size, size, mark((size - s) / 2, size / 2 - s * MARK_INK_CENTER_Y / 100, s, MOONLIGHT))


def mark_on_disc(size=256):
    defs = (f'<linearGradient id="bg" x1="0.25" y1="0" x2="0.75" y2="1">'
            f'<stop offset="0" stop-color="{NAVY_TOP}"/><stop offset="1" stop-color="{NAVY_BOTTOM}"/></linearGradient>')
    s = size * 0.72
    body = (f'<circle cx="{size / 2}" cy="{size / 2}" r="{size / 2}" fill="url(#bg)"/>'
            + mark((size - s) / 2, size / 2 - s * MARK_INK_CENTER_Y / 100, s, MOONLIGHT))
    return doc(size, size, body, defs)


def cover():
    defs = (f'<linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{NAVY_TOP}"/><stop offset="1" stop-color="{NAVY_BOTTOM}"/></linearGradient>')
    body = '<rect width="600" height="900" fill="url(#bg)"/>' + mark(150, 450 - 300 * MARK_INK_CENTER_Y / 100, 300, MOONLIGHT, 0.35)
    return doc(600, 900, body, defs)


def lockup(background, ink):
    w, h = 1200, 800
    s = 380
    body = (f'<rect width="{w}" height="{h}" fill="{background}"/>'
            + mark((w - s) / 2, 90, s, ink)
            + wordmark(w / 2, 560, 640, ink))
    return doc(w, h, body)


render(mark_alone(), IMAGES / "applogo_clean.png")
render(mark_on_disc(), IMAGES / "applogo.png")
render(mark_on_disc(), IMAGES / "applogo_full.png", width=512)
render(mark_on_disc(), ROOT / "SeleneCore" / "icon.png")
render(cover(), IMAGES / "custom_cover_background.png")

for name, background, ink in (("dark", NAVY, MOONLIGHT), ("light", PAPER, NAVY)):
    svg = lockup(background, ink)
    (LOGO_DIR / f"selene-logo-{name}.svg").parent.mkdir(parents=True, exist_ok=True)
    (LOGO_DIR / f"selene-logo-{name}.svg").write_text(svg, encoding="utf-8")
    print("wrote", (LOGO_DIR / f"selene-logo-{name}.svg").relative_to(ROOT))
    render(svg, LOGO_DIR / f"selene-logo-{name}.png")

with tempfile.TemporaryDirectory() as tmp:
    big = Path(tmp) / "icon.png"
    render(mark_on_disc(), big, width=256)
    Image.open(big).save(IMAGES / "applogo.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
    print("wrote", (IMAGES / "applogo.ico").relative_to(ROOT))
