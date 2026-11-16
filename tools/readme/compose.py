"""Builds the README images of Nazar Capture from the app's own renders (made by ReadmeRenders.cs)."""
import os
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

RAW = Path(os.environ["NAZAR_README_OUT"])
OUT = Path(__file__).resolve().parents[2] / "docs" / "images"
FRAME = (236, 234, 230)          # the calm frame around every picture, a step below the "Kalkweiss" window
S = 2                            # renders are at 200 %


def rounded(im, radius):
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius, fill=255)
    out = im.convert("RGBA")
    out.putalpha(ImageChops.darker(out.getchannel("A"), mask))
    return out


def framed(window, pad=64, shadow=True, corner=16, outer=28):
    """A window (already with its own size) on the frame with a soft shadow, as Hams' screenshots."""
    w = rounded(window, corner) if corner else window.convert("RGBA")
    W, H = w.width + 2 * pad, w.height + 2 * pad
    canvas = Image.new("RGBA", (W, H), FRAME + (255,))
    if shadow:
        sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        a = Image.new("L", (W, H), 0)
        a.paste(w.getchannel("A").point(lambda v: int(v * 0.20)), (pad, pad + 10))
        a = a.filter(ImageFilter.GaussianBlur(20))
        sh.putalpha(a)
        canvas.alpha_composite(sh)
    canvas.alpha_composite(w, (pad, pad))
    return rounded(canvas, outer)


def save(im, name):
    path = OUT / name
    im.save(path, optimize=True)
    print(name, im.size, path.stat().st_size // 1024, "KB")


# main window and settings (Screenshot tab, cut at the window's minimum height of 420)
save(framed(Image.open(RAW / "main-window.png")), "main-window.png")
settings = Image.open(RAW / "settings-0.png")
save(framed(settings.crop((0, 0, settings.width, 420 * S))), "settings-screenshot.png")

# recording bar: it brings its own shadow and margin
bar = Image.open(RAW / "recording-bar.png")
bar = bar.crop(bar.getbbox())
save(framed(bar, pad=56, shadow=False, corner=0), "recording-bar.png")

# editor: the crop with its handles and the toolbar below, on the dimmed frozen screen
tx, ty, tw, th, cx, cy, cw, ch = (float(v) for v in (RAW / "editor-layout.txt").read_text().split(","))
left, top = min(tx, cx) - 24, cy - 48
right, bottom = max(tx + tw, cx + cw) + 24, ty + th + 28
editor = Image.open(RAW / "editor-full.png").crop(tuple(int(v * S) for v in (left, top, right, bottom)))
save(framed(editor, pad=48, corner=16), "editor-annotations.png")
