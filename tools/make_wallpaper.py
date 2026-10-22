#!/usr/bin/env python3
"""Draws Selene's own wallpaper (theme/Wallpapers/selene/0-moonlight.jpg): a moonlit night in Selene's navy, a glowing
crescent, stars and soft hills. Our own art, like the logo. Needs Pillow. Re-run: python3 tools/make_wallpaper.py
"""
import math
import random
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "theme" / "Wallpapers" / "selene" / "0-moonlight.jpg"
W, H = 2560, 1440
rnd = random.Random(1969)


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def vertical_gradient(stops):
    col = Image.new("RGB", (1, H))
    px = col.load()
    for y in range(H):
        t = y / (H - 1)
        for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
            if t0 <= t <= t1:
                k = (t - t0) / (t1 - t0)
                k = k * k * (3 - 2 * k)
                px[0, y] = tuple(round(a + (b - a) * k) for a, b in zip(hex_rgb(c0), hex_rgb(c1)))
                break
    return col.resize((W, H))


def glow(center, radius, color, strength, blur):
    layer = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = center
    c = tuple(round(v * strength) for v in hex_rgb(color))
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=c)
    return layer.filter(ImageFilter.GaussianBlur(blur))


def ridge(base, amplitude, seed, octaves):
    r = random.Random(seed)
    waves = [(r.uniform(0.6, 1.4) * (i + 1) * 1.7, r.uniform(0, math.tau), amplitude / (i + 1) ** 1.25) for i in range(octaves)]
    return [base - sum(a * math.sin(x / W * math.tau * f + p) for f, p, a in waves) for x in range(0, W + 8, 8)]


def hills(img, base, amplitude, seed, color, haze):
    pts = ridge(base, amplitude, seed, 6)
    layer = Image.new("L", (W, H), 0)
    ImageDraw.Draw(layer).polygon([(i * 8, y) for i, y in enumerate(pts)] + [(W, H), (0, H)], fill=255)
    layer = layer.filter(ImageFilter.GaussianBlur(haze))
    img.paste(Image.new("RGB", (W, H), hex_rgb(color)), (0, 0), layer)


img = vertical_gradient([(0.0, "#081633"), (0.5, "#060F26"), (1.0, "#02060F")])
moon = (int(W * 0.73), int(H * 0.27))

# sky glow around the moon and a faint band low on the horizon
img = ImageChops.add(img, glow(moon, 520, "#5C86C9", 0.20, 240))
img = ImageChops.add(img, glow(moon, 180, "#9CCBFF", 0.28, 90))
img = ImageChops.add(img, glow((W // 2, int(H * 0.95)), 900, "#1E3F7A", 0.30, 260))

# stars: many faint ones, a few bright with a soft halo; fewer near the moon's glare
stars = Image.new("RGB", (W, H), (0, 0, 0))
halos = Image.new("RGB", (W, H), (0, 0, 0))
ds, dh = ImageDraw.Draw(stars), ImageDraw.Draw(halos)
for _ in range(1100):
    x, y = rnd.uniform(0, W), rnd.uniform(0, H * 0.78) ** 1.0
    if math.hypot(x - moon[0], y - moon[1]) < rnd.uniform(160, 420):
        continue
    b = rnd.random() ** 3
    r = 0.6 + b * 1.6
    c = tuple(round(v * (0.35 + 0.65 * b)) for v in hex_rgb(rnd.choice(["#EAF3FF", "#EAF3FF", "#CFE0FF", "#FFF4E0"])))
    ds.ellipse((x - r, y - r, x + r, y + r), fill=c)
    if b > 0.8:
        hr = 3 + b * 5
        dh.ellipse((x - hr, y - hr, x + hr, y + hr), fill=tuple(v // 3 for v in c))
img = ImageChops.add(img, stars.filter(ImageFilter.GaussianBlur(0.6)))
img = ImageChops.add(img, halos.filter(ImageFilter.GaussianBlur(4)))

# the crescent: a bright disc with an offset disc taken out, lit edge glowing
R = 120
mask = Image.new("L", (W, H), 0)
dm = ImageDraw.Draw(mask)
dm.ellipse((moon[0] - R, moon[1] - R, moon[0] + R, moon[1] + R), fill=255)
cut = (moon[0] + 52, moon[1] - 30)
dm.ellipse((cut[0] - R * 0.92, cut[1] - R * 0.92, cut[0] + R * 0.92, cut[1] + R * 0.92), fill=0)
mask = mask.filter(ImageFilter.GaussianBlur(1.2))
halo = Image.new("RGB", (W, H), hex_rgb("#9CCBFF"))
img = ImageChops.add(img, ImageChops.multiply(halo, Image.merge("RGB", [mask.filter(ImageFilter.GaussianBlur(40))] * 3)).point(lambda v: v * 0.8))
img.paste(Image.new("RGB", (W, H), hex_rgb("#EEF5FF")), (0, 0), mask)

# hills, far to near, each a little darker, with mist between them
hills(img, H * 0.80, 70, 3, "#0C1C3E", 6)
img = ImageChops.add(img, glow((W // 2, int(H * 0.86)), 1100, "#1A3366", 0.18, 120))
hills(img, H * 0.86, 60, 11, "#081430", 3)
hills(img, H * 0.93, 45, 27, "#040A1C", 1.5)

OUT.parent.mkdir(parents=True, exist_ok=True)
img.save(OUT, quality=90, optimize=True, progressive=True)
print(f"wrote {OUT.relative_to(ROOT)} ({OUT.stat().st_size // 1024} KB)")
