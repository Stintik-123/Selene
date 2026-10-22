#!/usr/bin/env python3
"""Draws the README's showcase images from what Selene ships: docs/readme/themes.jpg and docs/readme/avatars.jpg.

Needs Pillow and a TrueType font (Liberation Sans or DejaVu Sans on Linux, Segoe UI on Windows).
Re-run after adding themes, wallpapers or avatars: python3 tools/make_readme_art.py
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "readme"
BG = (5, 8, 15)
TEXT = (234, 243, 255)
MUTED = (127, 146, 173)
FONTS = ["/usr/share/fonts/liberation/LiberationSans-{w}.ttf", "/usr/share/fonts/TTF/DejaVuSans{d}.ttf",
         "C:/Windows/Fonts/segoeui{s}.ttf"]


def font(size, bold=False):
    for pattern in FONTS:
        path = pattern.format(w="Bold" if bold else "Regular", d="-Bold" if bold else "", s="b" if bold else "")
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    raise FileNotFoundError("no TrueType font found; install Liberation Sans or DejaVu Sans")


def hex_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def rounded(img, radius):
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, img.size[0] - 1, img.size[1] - 1), radius, fill=255)
    out = Image.new("RGBA", img.size)
    out.paste(img, (0, 0), mask)
    return out


def themes_image():
    themes = [t for t in json.loads((ROOT / "theme" / "Palettes" / "palettes.json").read_text(encoding="utf-8")) if t["Wallpaper"]]
    cols, card_w, card_h, gap, label_h = 4, 440, 248, 26, 46
    rows = (len(themes) + cols - 1) // cols
    width = cols * card_w + (cols + 1) * gap
    height = rows * (card_h + label_h) + (rows + 1) * gap
    sheet = Image.new("RGBA", (width, height), BG)
    draw = ImageDraw.Draw(sheet)
    name_font, mode_font = font(22, True), font(16)
    for i, t in enumerate(themes):
        x = gap + (i % cols) * (card_w + gap)
        y = gap + (i // cols) * (card_h + label_h + gap)
        wall = ImageOps.fit(Image.open(ROOT / "theme" / t["Wallpaper"]).convert("RGB"), (card_w, card_h), Image.LANCZOS)
        # the theme's own colors over its wallpaper: a darkened left side with an accent bar, like Selene's home
        shade = Image.new("RGBA", (card_w, card_h), (0, 0, 0, 0))
        sd = ImageDraw.Draw(shade)
        bg = hex_rgb(t["Background"])
        for px in range(card_w):
            alpha = int(200 * max(0.0, 1 - px / (card_w * 0.7)))
            sd.line([(px, 0), (px, card_h)], fill=bg + (alpha,))
        card = Image.alpha_composite(wall.convert("RGBA"), shade)
        cd = ImageDraw.Draw(card)
        accent, fg = hex_rgb(t["Accent"]), hex_rgb(t["Foreground"])
        cd.rounded_rectangle((22, 26, 150, 98), 12, outline=fg, width=3)
        cd.rounded_rectangle((22, 26, 150, 98), 12, fill=bg + (170,))
        glow = Image.new("RGBA", (card_w, card_h), (0, 0, 0, 0))
        ImageDraw.Draw(glow).rounded_rectangle((18, 22, 154, 102), 14, outline=accent + (255,), width=6)
        card = Image.alpha_composite(card, glow.filter(ImageFilter.GaussianBlur(6)))
        cd = ImageDraw.Draw(card)
        cd.rounded_rectangle((22, 26, 150, 98), 12, outline=fg, width=3)
        cd.rounded_rectangle((22, 150, 110, 176), 13, fill=fg)
        cd.rounded_rectangle((118, 150, 206, 176), 13, fill=bg + (200,))
        for k in range(3):
            cd.rounded_rectangle((162 + k * 58, 40, 210 + k * 58, 88), 8, fill=bg + (180,))
        card = rounded(card, 14)
        sheet.alpha_composite(card, (x, y))
        draw.text((x + 2, y + card_h + 10), t["Name"], font=name_font, fill=TEXT)
        name_w = draw.textlength(t["Name"], font=name_font)
        draw.text((x + 12 + name_w, y + card_h + 15), t["Mode"], font=mode_font, fill=MUTED)
        for k, color in enumerate((t["Accent"], t["Foreground"], t["Background"])):
            cx = x + card_w - 18 - k * 26
            draw.ellipse((cx - 9, y + card_h + 14, cx + 9, y + card_h + 32), fill=hex_rgb(color), outline=(255, 255, 255, 60))
    OUT.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(OUT / "themes.jpg", quality=86, optimize=True)
    print(f"wrote docs/readme/themes.jpg ({len(themes)} themes)")


def avatars_image():
    files = sorted((ROOT / "theme" / "Avatars").glob("*.jpg"))
    step = max(1, len(files) // 36)
    picks = files[::step][:36]
    cols, size, gap = 12, 120, 18
    rows = (len(picks) + cols - 1) // cols
    width, height = cols * size + (cols + 1) * gap, rows * size + (rows + 1) * gap
    sheet = Image.new("RGBA", (width, height), BG)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, size - 1, size - 1), fill=255)
    for i, f in enumerate(picks):
        x = gap + (i % cols) * (size + gap)
        y = gap + (i // cols) * (size + gap)
        pic = ImageOps.fit(Image.open(f).convert("RGB"), (size, size), Image.LANCZOS)
        sheet.paste(pic, (x, y), mask)
        ImageDraw.Draw(sheet).ellipse((x, y, x + size - 1, y + size - 1), outline=(234, 243, 255, 70), width=2)
    sheet.convert("RGB").save(OUT / "avatars.jpg", quality=88, optimize=True)
    print(f"wrote docs/readme/avatars.jpg ({len(picks)} of {len(files)} avatars)")


themes_image()
avatars_image()
