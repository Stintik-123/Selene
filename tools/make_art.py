#!/usr/bin/env python3
"""Exports all of Selene's artwork from the master drawings in tools/art/.

Needs rsvg-convert (librsvg) and Pillow. Re-run after changing the design: python3 tools/make_art.py
"""
import math
import random
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

# moonlight shine for dark backgrounds: the crescent goes from pure white to moonlight
SHINE = ('<linearGradient id="shine" x1="0" y1="0" x2="1" y2="1">'
         f'<stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="{MOONLIGHT}"/></linearGradient>')


def glow(color, opacity):
    # full strength out to the full moon's ring (r ~11 of 26), then a smooth fade so the halo surrounds the ring
    def strength(t):
        if t <= 0.42:
            return 1.0
        u = (t - 0.42) / 0.58
        return (1 - u * u * (3 - 2 * u)) ** 1.6
    stops = "".join(f'<stop offset="{t / 20}" stop-color="{color}" stop-opacity="{opacity * strength(t / 20):.4f}"/>'
                    for t in range(21))
    return (f'<defs><radialGradient id="glow">{stops}</radialGradient>{SHINE}</defs>'
            '<circle cx="50" cy="42" r="26" fill="url(#glow)"/>')


def inner(svg_file):
    text = (ART / svg_file).read_text(encoding="utf-8")
    view_box = re.search(r'viewBox="([^"]+)"', text).group(1)
    body = text[text.index(">", text.index("<svg")) + 1 : text.rindex("</svg>")]
    return view_box, body


MARK_BOX, MARK = inner("selene-mark.svg")
WORDMARK = inner("selene-wordmark.svg")


def mark(cx, cy, size, color, opacity=1.0, shine=False, glow_color="#9CCBFF", glow_opacity=0.32):
    body = glow(glow_color, glow_opacity) + MARK
    if shine:
        body = body.replace('id="crescent" fill="currentColor"', 'id="crescent" fill="url(#shine)"')
    return (f'<svg x="{cx - size / 2}" y="{cy - size / 2}" width="{size}" height="{size}" viewBox="{MARK_BOX}" '
            f'style="color:{color}" opacity="{opacity}" overflow="visible">{body}</svg>')


def lettering(art, cx, y, width, color, opacity=1.0):
    box, body = art
    box_w, box_h = (float(v) for v in box.split()[2:])
    height = width * box_h / box_w
    return (f'<svg x="{cx - width / 2}" y="{y}" width="{width}" height="{height}" viewBox="{box}" '
            f'style="color:{color}" opacity="{opacity}">{body}</svg>')


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
    return doc(size, size, mark(size / 2, size / 2, size * 0.92, MOONLIGHT, shine=True))


def mark_on_disc(size=256):
    defs = (f'<linearGradient id="bg" x1="0.25" y1="0" x2="0.75" y2="1">'
            f'<stop offset="0" stop-color="{NAVY_TOP}"/><stop offset="1" stop-color="{NAVY_BOTTOM}"/></linearGradient>')
    body = (f'<circle cx="{size / 2}" cy="{size / 2}" r="{size / 2}" fill="url(#bg)"/>'
            + mark(size / 2, size / 2, size * 0.68, MOONLIGHT, shine=True))
    return doc(size, size, body, defs)


def cover():
    defs = (f'<linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{NAVY_TOP}"/><stop offset="1" stop-color="{NAVY_BOTTOM}"/></linearGradient>')
    body = '<rect width="600" height="900" fill="url(#bg)"/>' + mark(300, 450, 260, MOONLIGHT, 0.35, shine=True)
    return doc(600, 900, body, defs)


def lockup(background, ink, shine):
    w, h = 1200, 800
    body = (f'<rect width="{w}" height="{h}" fill="{background}"/>'
            + (mark(w / 2, 300, 380, ink, shine=True) if shine
               else mark(w / 2, 300, 380, ink, glow_color="#4A78C8", glow_opacity=0.22))
            + lettering(WORDMARK, w / 2, 545, 465, ink))
    return doc(w, h, body)


def banner():
    """1280x640 social preview: the login's moonlit sky with the logo."""
    w, h = 1280, 640
    defs = (f'<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{NAVY_TOP}"/><stop offset="1" stop-color="{NAVY_BOTTOM}"/></linearGradient>'
            '<radialGradient id="spot" cx="0.16" cy="-0.12" r="0.9">'
            '<stop offset="0" stop-color="#5C86C9" stop-opacity="0.35"/><stop offset="1" stop-color="#5C86C9" stop-opacity="0"/></radialGradient>'
            '<radialGradient id="haze" cx="0.5" cy="1.05" r="0.7">'
            '<stop offset="0" stop-color="#1E3F7A" stop-opacity="0.45"/><stop offset="1" stop-color="#1E3F7A" stop-opacity="0"/></radialGradient>')
    rng = random.Random(7)
    stars = "".join(f'<circle cx="{rng.uniform(0, w):.1f}" cy="{rng.uniform(0, h):.1f}" r="{rng.choice((0.8, 1.1, 1.5))}" '
                    f'fill="{MOONLIGHT}" opacity="{rng.uniform(0.15, 0.6):.2f}"/>' for _ in range(110))
    body = (f'<rect width="{w}" height="{h}" fill="url(#sky)"/><rect width="{w}" height="{h}" fill="url(#spot)"/>'
            f'<rect width="{w}" height="{h}" fill="url(#haze)"/>{stars}'
            + mark(w / 2, 276, 280, MOONLIGHT, shine=True)
            + lettering(WORDMARK, w / 2, 428, 345, MOONLIGHT))
    return doc(w, h, body, defs)


render(mark_alone(), IMAGES / "applogo_clean.png")
render(mark_on_disc(), IMAGES / "applogo.png")
render(mark_on_disc(), IMAGES / "applogo_full.png", width=512)
render(mark_on_disc(), ROOT / "SeleneCore" / "icon.png")
render(cover(), IMAGES / "custom_cover_background.png")

for name, background, ink, shine in (("dark", NAVY, MOONLIGHT, True), ("light", PAPER, NAVY, False)):
    svg = lockup(background, ink, shine)
    (LOGO_DIR / f"selene-logo-{name}.svg").parent.mkdir(parents=True, exist_ok=True)
    (LOGO_DIR / f"selene-logo-{name}.svg").write_text(svg, encoding="utf-8")
    print("wrote", (LOGO_DIR / f"selene-logo-{name}.svg").relative_to(ROOT))
    render(svg, LOGO_DIR / f"selene-logo-{name}.png")

render(banner(), LOGO_DIR / "selene-banner.png")

with tempfile.TemporaryDirectory() as tmp:
    big = Path(tmp) / "icon.png"
    render(mark_on_disc(), big, width=256)
    Image.open(big).save(IMAGES / "applogo.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
    print("wrote", (IMAGES / "applogo.ico").relative_to(ROOT))
