#!/usr/bin/env python3
"""Draws theme/Images/TrailerMask.png: the opacity mask over the trailers behind Collection and a Store game's page.

Clear at the left (where the logo and words sit), fully shown from about the middle, and fading out toward the bottom
(the covers' row). Only the alpha matters: the theme uses it as an OpacityMask. Run: python3 tools/make_trailer_mask.py
"""
from pathlib import Path

from PIL import Image

W, H = 1920, 1080
OUT = Path(__file__).resolve().parent.parent / "theme" / "Images" / "TrailerMask.png"


def smooth(edge0, edge1, x):
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def main():
    across = [smooth(0.0, 0.52, x / (W - 1)) for x in range(W)]
    down = [1 - smooth(0.54, 1.02, y / (H - 1)) for y in range(H)]
    alpha = Image.new("L", (W, H))
    alpha.putdata([round(255 * a * d) for d in down for a in across])
    mask = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    mask.putalpha(alpha)
    mask.save(OUT, optimize=True)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
