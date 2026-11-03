#!/usr/bin/env python3
"""Renders ModCover.jpg (800x450) for the Stasis Capture mod.

Drawn at 2x and downscaled for smooth edges. Original artwork only:
space backdrop, a cryo chamber tile with a sleeping silhouette, title
and a mock of the "Send to Stasis" button.

Needs Pillow and the Inter font (Bold, ExtraBold, SemiBold, Medium):
  py -m pip install pillow        (python3 -m pip install pillow elsewhere)
  Inter: https://rsms.me/inter/ - install it, or unzip it into Artwork/fonts/

Usage: py make_cover.py [out_path]   (default: ModCover.jpg in the mod root)
"""
import os
import random
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter, ImageFont
except ImportError:
    sys.exit("Pillow is missing: run  py -m pip install pillow  (or python3 -m pip install pillow)")

SCALE = 2
W, H = 800 * SCALE, 450 * SCALE
ARTWORK_DIR = Path(__file__).resolve().parent
DEFAULT_OUT = ARTWORK_DIR.parent / "ModCover.jpg"
FONT_EXTENSIONS = (".otf", ".ttf")
_font_paths = {}

BG_TOP = (8, 14, 34)
BG_BOTTOM = (18, 40, 78)
TILE_FILL = (20, 34, 70)
TILE_EDGE = (90, 200, 240)
GLASS = (60, 150, 210)
FROST = (190, 240, 255)
WHITE = (236, 246, 255)
MUTED = (160, 196, 225)
GREEN = (92, 220, 120)


def font_dirs():
    """Folders searched for Inter, most specific first: unzipped next to the script,
    INTER_FONT_DIR, then the fonts installed on Windows, Linux or macOS."""
    home = Path.home()
    candidates = [
        ARTWORK_DIR / "fonts",
        os.environ.get("INTER_FONT_DIR"),
        Path(os.environ.get("LOCALAPPDATA", home)) / "Microsoft" / "Windows" / "Fonts",
        Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts",
        home / ".local" / "share" / "fonts",
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        home / "Library" / "Fonts",
        "/Library/Fonts",
    ]
    return [Path(c) for c in candidates if c and Path(c).is_dir()]


def find_font(stem):
    """Path of e.g. "Inter-Bold" as .otf or .ttf, searched recursively."""
    if stem in _font_paths:
        return _font_paths[stem]
    wanted = {(stem + ext).lower() for ext in FONT_EXTENSIONS}
    for folder in font_dirs():
        for path in folder.rglob("*"):
            if path.name.lower() in wanted:
                _font_paths[stem] = path
                return path
    sys.exit(f"Font {stem} not found. Install Inter (https://rsms.me/inter/) "
             f"or unzip it into {ARTWORK_DIR / 'fonts'}")


def font(stem, size):
    return ImageFont.truetype(str(find_font(stem)), size * SCALE)


def s(v):
    return int(v * SCALE)


def background():
    img = Image.new("RGB", (W, H))
    px = img.load()
    for y in range(H):
        t = y / (H - 1)
        row = tuple(int(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM))
        for x in range(W):
            px[x, y] = row
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse([s(-120), s(40), s(460), s(520)], fill=(30, 110, 170))
    g.ellipse([s(520), s(-160), s(980), s(260)], fill=(40, 50, 140))
    glow = glow.filter(ImageFilter.GaussianBlur(s(90)))
    img = Image.blend(img, Image.composite(glow, img, glow.convert("L")), 0.5)
    rng = random.Random(7)
    d = ImageDraw.Draw(img)
    for _ in range(170):
        x, y = rng.randrange(W), rng.randrange(H)
        r = rng.choice([1, 1, 1, 2, 2, 3])
        c = rng.randint(140, 255)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(c, min(255, c + 10), 255))
    return img


def snowflake(d, cx, cy, r, width, fill):
    """Six-armed flake with small side ticks."""
    import math
    for k in range(6):
        a = math.pi / 3 * k
        ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
        d.line([cx, cy, ex, ey], fill=fill, width=width)
        for f in (0.55, 0.8):
            bx, by = cx + r * f * math.cos(a), cy + r * f * math.sin(a)
            for side in (-1, 1):
                sa = a + side * math.pi / 4
                d.line([bx, by, bx + r * 0.22 * math.cos(sa), by + r * 0.22 * math.sin(sa)], fill=fill, width=width)


def draw_tile(img):
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = s(48), s(75), s(348), s(375)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([x0 + s(6), y0 + s(10), x1 + s(6), y1 + s(10)], s(34), fill=(0, 0, 0, 150))
    sh = sh.filter(ImageFilter.GaussianBlur(s(14)))
    img.paste(sh, (0, 0), sh)
    d.rounded_rectangle([x0, y0, x1, y1], s(34), fill=TILE_FILL, outline=TILE_EDGE, width=s(4))

    # the chamber: a capsule with a glass lid
    cx0, cy0, cx1, cy1 = x0 + s(70), y0 + s(60), x1 - s(70), y1 - s(36)
    d.rounded_rectangle([cx0, cy0, cx1, cy1], s(80), fill=(28, 52, 96), outline=(120, 210, 245), width=s(5))
    lid = [cx0 + s(18), cy0 + s(20), cx1 - s(18), cy1 - s(40)]
    glass = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glass).rounded_rectangle(lid, s(62), fill=GLASS + (150,))
    img.paste(glass, (0, 0), glass)
    d = ImageDraw.Draw(img)
    # sleeper silhouette: head and shoulders, lying still
    mx = (cx0 + cx1) / 2
    d.ellipse([mx - s(24), cy0 + s(44), mx + s(24), cy0 + s(92)], fill=(16, 30, 58))
    d.rounded_rectangle([mx - s(46), cy0 + s(100), mx + s(46), cy1 - s(56)], s(40), fill=(16, 30, 58))
    # frost highlight on the glass
    d.line([lid[0] + s(22), lid[1] + s(40), lid[0] + s(22), lid[3] - s(40)], fill=FROST, width=s(5))
    # status light
    d.ellipse([mx - s(8), cy1 - s(28), mx + s(8), cy1 - s(12)], fill=GREEN)

    # snowflake badge
    bx, by, r = x1 - s(10), y0 + s(10), s(40)
    d.ellipse([bx - r, by - r, bx + r, by + r], fill=(70, 170, 235), outline=(200, 240, 255), width=s(4))
    snowflake(d, bx, by, s(26), s(5), WHITE)

    d.text((x0 + s(36), y0 + s(22)), "CRYO", font=font("Inter-Bold", 22), fill=MUTED)


def draw_text(img):
    d = ImageDraw.Draw(img)
    x = s(392)
    d.text((x, s(92)), "STASIS", font=font("Inter-ExtraBold", 54), fill=WHITE)
    d.text((x, s(150)), "CAPTURE", font=font("Inter-ExtraBold", 54), fill=WHITE)

    # mock of the bottom bar button
    card = [x, s(238), s(760), s(318)]
    d.rounded_rectangle(card, s(14), fill=(14, 30, 60), outline=(90, 200, 240), width=s(3))
    snowflake(d, x + s(40), s(278), s(20), s(4), FROST)
    d.text((x + s(76), s(262)), "Send to Stasis", font=font("Inter-SemiBold", 26), fill=WHITE)

    d.text((x, s(342)), "raiders  ·  animals  ·  crawlers", font=font("Inter-Medium", 20), fill=MUTED)
    d.text((x, s(392)), "for Stardeus v0.15", font=font("Inter-Medium", 16), fill=(120, 150, 190))


def main(out_path):
    img = background()
    draw_tile(img)
    draw_text(img)
    img = img.resize((800, 450), Image.LANCZOS)
    img.save(out_path, "JPEG", quality=92, optimize=True)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
