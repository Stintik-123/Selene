#!/usr/bin/env python3
"""Draws the README's animated banner, docs/readme/intro.gif: the login's moonlit sky, the crescent rising in with its halo
breathing, the lettering under it, dust drifting up, then a soft fade so the loop starts over cleanly.

Needs Pillow, ffmpeg and a Chromium browser (Edge or Chrome) to draw the SVG layers; the master drawings are tools/art/.
Re-run after changing the logo: python3 tools/make_readme_motion.py
"""
import math
import os
import random
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT / "tools" / "art"
OUT = ROOT / "docs" / "readme" / "intro.gif"

W, H = 1280, 640
FPS, SECONDS = 20, 7.0
MOONLIGHT = "#EAF3FF"
NAVY_TOP, NAVY_BOTTOM = "#102A5C", "#03081A"
MARK_CX, MARK_CY, MARK_SIZE = W / 2, 276, 280
WORD_Y, WORD_W = 428, 345

BROWSERS = [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            "/usr/bin/chromium", "/usr/bin/google-chrome", "/usr/bin/microsoft-edge"]


def inner(svg_file):
    text = (ART / svg_file).read_text(encoding="utf-8")
    view_box = re.search(r'viewBox="([^"]+)"', text).group(1)
    body = text[text.index(">", text.index("<svg")) + 1: text.rindex("</svg>")]
    return view_box, body


MARK_BOX, MARK = inner("selene-mark.svg")
WORD_BOX, WORD = inner("selene-wordmark.svg")
SHINE = ('<linearGradient id="shine" x1="0" y1="0" x2="1" y2="1">'
         f'<stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="{MOONLIGHT}"/></linearGradient>')


def doc(body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}"><defs>{defs}</defs>{body}</svg>')


def sky_svg():
    defs = (f'<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{NAVY_TOP}"/><stop offset="1" stop-color="{NAVY_BOTTOM}"/></linearGradient>'
            '<radialGradient id="spot" cx="0.16" cy="-0.12" r="0.9"><stop offset="0" stop-color="#5C86C9" stop-opacity="0.35"/><stop offset="1" stop-color="#5C86C9" stop-opacity="0"/></radialGradient>'
            '<radialGradient id="haze" cx="0.5" cy="1.05" r="0.7"><stop offset="0" stop-color="#1E3F7A" stop-opacity="0.45"/><stop offset="1" stop-color="#1E3F7A" stop-opacity="0"/></radialGradient>')
    return doc(f'<rect width="{W}" height="{H}" fill="url(#sky)"/><rect width="{W}" height="{H}" fill="url(#spot)"/><rect width="{W}" height="{H}" fill="url(#haze)"/>', defs)


def stars_svg():
    rng = random.Random(7)
    stars = "".join(f'<circle cx="{rng.uniform(0, W):.1f}" cy="{rng.uniform(0, H):.1f}" r="{rng.choice((0.8, 1.1, 1.5))}" '
                    f'fill="{MOONLIGHT}" opacity="{rng.uniform(0.15, 0.6):.2f}"/>' for _ in range(110))
    return doc(stars)


def mark_svg():
    body = MARK.replace('id="crescent" fill="currentColor"', 'id="crescent" fill="url(#shine)"')
    return doc(f'<svg x="{MARK_CX - MARK_SIZE / 2}" y="{MARK_CY - MARK_SIZE / 2}" width="{MARK_SIZE}" height="{MARK_SIZE}" viewBox="{MARK_BOX}" '
               f'style="color:{MOONLIGHT}" overflow="visible"><defs>{SHINE}</defs>{body}</svg>')


def halo_svg():
    # the halo at full strength (its breathing is the frame's alpha): the same shape as the app's, around the full moon
    def strength(t):
        if t <= 0.42:
            return 1.0
        u = (t - 0.42) / 0.58
        return (1 - u * u * (3 - 2 * u)) ** 1.6
    stops = "".join(f'<stop offset="{t / 20}" stop-color="#9CCBFF" stop-opacity="{strength(t / 20):.4f}"/>' for t in range(21))
    return doc(f'<svg x="{MARK_CX - MARK_SIZE / 2}" y="{MARK_CY - MARK_SIZE / 2}" width="{MARK_SIZE}" height="{MARK_SIZE}" viewBox="{MARK_BOX}" overflow="visible">'
               f'<defs><radialGradient id="glow">{stops}</radialGradient></defs><circle cx="50" cy="42" r="26" fill="url(#glow)"/></svg>')


def word_svg():
    box_w, box_h = (float(v) for v in WORD_BOX.split()[2:])
    height = WORD_W * box_h / box_w
    return doc(f'<svg x="{W / 2 - WORD_W / 2}" y="{WORD_Y}" width="{WORD_W}" height="{height}" viewBox="{WORD_BOX}" style="color:{MOONLIGHT}">{WORD}</svg>')


def browser():
    for b in BROWSERS:
        if Path(b).exists():
            return b
    for name in ("msedge", "chrome", "chromium", "google-chrome"):
        found = shutil.which(name)
        if found:
            return found
    raise FileNotFoundError("no Chromium browser found to draw the SVG layers (Edge or Chrome)")


def render(svg, folder, name):
    svg_path = folder / f"{name}.svg"
    png_path = folder / f"{name}.png"
    svg_path.write_text(svg, encoding="utf-8")
    subprocess.run([browser(), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
                    f"--window-size={W},{H}", f"--screenshot={png_path}", svg_path.resolve().as_uri()],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return Image.open(png_path).convert("RGBA")


def ease_out(x):
    x = min(1.0, max(0.0, x))
    return 1 - (1 - x) ** 3


def ease_in_out(x):
    x = min(1.0, max(0.0, x))
    return 0.5 - 0.5 * math.cos(math.pi * x)


def with_alpha(layer, alpha):
    if alpha >= 0.999:
        return layer
    a = layer.getchannel("A").point(lambda v: int(v * alpha))
    out = layer.copy()
    out.putalpha(a)
    return out


def shifted(layer, dx, dy):
    out = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    out.paste(layer, (int(round(dx)), int(round(dy))))
    return out


def main():
    work = Path(tempfile.mkdtemp(prefix="selene-intro-"))
    sky = render(sky_svg(), work, "sky")
    stars = render(stars_svg(), work, "stars")
    mark = render(mark_svg(), work, "mark")
    halo = render(halo_svg(), work, "halo")
    word = render(word_svg(), work, "word")

    # the dust: slow motes rising with a sway, as the app's animated background has (each with its own phase and size)
    rng = random.Random(11)
    motes = [dict(x=rng.uniform(0, W), y=rng.uniform(0, H), r=rng.choice((1.5, 2.0, 2.6, 3.4)), speed=rng.uniform(6, 16),
                  sway=rng.uniform(6, 18), phase=rng.uniform(0, math.tau), a=rng.uniform(0.25, 0.7)) for _ in range(70)]

    frames = int(FPS * SECONDS)
    frames_dir = work / "frames"
    frames_dir.mkdir()
    for i in range(frames):
        t = i / FPS
        # in: the crescent rises as it fades in, the lettering a beat later; out: everything lifts and fades for the loop
        rise_in = ease_out(t / 0.9)
        word_in = ease_out((t - 0.45) / 0.9)
        out = ease_in_out((t - 5.9) / 0.7)
        logo_a = rise_in * (1 - out)
        word_a = word_in * (1 - out)
        lift = -22 * out
        # the halo breathes twice, then settles, like the login's glow
        breath = 0.0
        if 0.7 <= t <= 3.9:
            breath = 0.5 - 0.5 * math.cos(math.tau * (t - 0.7) / 1.6)
        halo_a = (0.34 + 0.5 * breath) * logo_a

        frame = sky.copy()
        # stars drift a touch to the left over the loop (wrapping): alive, but barely
        drift = -(t / SECONDS) * 14
        star_layer = ImageChops.offset(stars, int(round(drift)), 0)
        frame.alpha_composite(star_layer)
        dust = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        dd = ImageDraw.Draw(dust)
        for m in motes:
            y = (m["y"] - m["speed"] * t) % (H + 20) - 10
            x = (m["x"] + math.sin(m["phase"] + t * 0.9) * m["sway"]) % W
            twinkle = 0.75 + 0.25 * math.sin(m["phase"] * 3 + t * 2.3)
            a = int(255 * m["a"] * twinkle)
            dd.ellipse((x - m["r"], y - m["r"], x + m["r"], y + m["r"]), fill=(234, 243, 255, a))
        frame.alpha_composite(dust.filter(ImageFilter.GaussianBlur(0.6)))

        dy = 24 * (1 - rise_in) + lift
        halo_scaled = halo
        if breath > 0:
            s = 1 + 0.07 * breath
            big = halo.resize((int(W * s), int(H * s)), Image.BILINEAR)
            halo_scaled = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            halo_scaled.paste(big, (int(MARK_CX - MARK_CX * s), int(MARK_CY - MARK_CY * s)))
        frame.alpha_composite(shifted(with_alpha(halo_scaled, halo_a), 0, dy))
        frame.alpha_composite(shifted(with_alpha(mark, logo_a), 0, dy))
        frame.alpha_composite(shifted(with_alpha(word, word_a), 0, 14 * (1 - word_in) + lift))
        frame.convert("RGB").save(frames_dir / f"f{i:03d}.png")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-framerate", str(FPS), "-i", str(frames_dir / "f%03d.png"),
                    "-vf", "split[a][b];[a]palettegen=max_colors=160:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle",
                    "-loop", "0", str(OUT)], check=True)
    shutil.rmtree(work, ignore_errors=True)
    print(f"wrote {OUT.relative_to(ROOT)} ({OUT.stat().st_size / 1048576:.1f} MB, {frames} frames)")


if __name__ == "__main__":
    main()
