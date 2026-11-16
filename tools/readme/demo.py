"""Demo GIF: Shift+Alt+4 -> drag an area -> card "In Zwischenablage kopiert". All UI from raw/ (the app's own renders)."""
import os
from pathlib import Path

from PIL import Image, ImageDraw

RAW = Path(os.environ["NAZAR_README_OUT"])
OUT = Path(__file__).resolve().parents[2] / "docs" / "images"
GW, GH = 960, 600
F = GW / 2560                      # raw device pixels -> GIF pixels
L = GW / 1280                      # WPF units -> GIF pixels


def shrink(im):
    return im.convert("RGBA").resize((round(im.width * F), round(im.height * F)), Image.LANCZOS)


screen = shrink(Image.open(RAW / "mock-screen.png"))
hotkey = shrink(Image.open(RAW / "hotkey.png"))
overlay = [(shrink(Image.open(RAW / name)), float(x) * F, float(y) * F)
           for name, x, y in (line.split(",") for line in (RAW / "overlay-frames.txt").read_text().split())]
cards = {v: shrink(Image.open(RAW / f"card-{v}.png")) for v in (100, 85, 70, 55, 40)}


def arrow(draw, x, y):
    s = 1.0
    pts = [(0, 0), (0, 17), (4, 13), (7, 20), (10, 19), (7, 12), (12, 12)]
    poly = [(x + px * s, y + py * s) for px, py in pts]
    draw.polygon(poly, fill=(255, 255, 255), outline=(0, 0, 0))


def cross(draw, x, y):
    for w, c in ((3, (255, 255, 255)), (1, (0, 0, 0))):
        draw.line((x - 8, y, x + 8, y), fill=c, width=w)
        draw.line((x, y - 8, x, y + 8), fill=c, width=w)


frames, durations = [], []


def add(im, ms):
    frames.append(im.convert("RGB"))
    durations.append(ms)


def desktop(pointer, key=None, card=None, card_dx=0.0):
    im = screen.copy()
    if card is not None:
        # bottom right of the work area (above the taskbar), as the app places it
        x = GW - card.width + 6 * L + card_dx
        y = GH - 40 * L - card.height + 6 * L
        im.alpha_composite(card, (round(x), round(y)))
    if key is not None:
        im.alpha_composite(key, ((GW - key.width) // 2, round(GH - 40 * L - key.height - 18 * L)))
    arrow(ImageDraw.Draw(im), *pointer)
    return im


# 1. the desktop, the pointer comes to rest; Shift+Alt+4 is pressed
start = (overlay[0][1], overlay[0][2])
for i in range(5):
    t = i / 4
    add(desktop((start[0] + 120 * (1 - t), start[1] + 70 * (1 - t))), 90)
add(desktop(start), 500)
add(desktop(start, key=hotkey), 700)

# 2. the overlay: frozen screen, dimming fades in, the pointer goes to the corner and drags
first = overlay[0][0]
add(Image.blend(screen, first, 0.5), 60)
for i, (im, x, y) in enumerate(overlay):
    im = im.copy()
    cross(ImageDraw.Draw(im), x, y)
    add(im, 500 if i == 0 else (60 if i < 9 else 55))
    if i == 8:
        durations[-1] = 260   # a short rest before pressing
durations[-1] = 450            # the finished area, before letting go

# 3. released: the card slides in and counts down
end = (overlay[-1][1], overlay[-1][2])
for dx in (120, 50, 12):
    add(desktop(end, card=cards[100], card_dx=dx * L), 50)
for v, ms in ((100, 700), (85, 600), (70, 600), (55, 600), (40, 500)):
    add(desktop(end, card=cards[v]), ms)
add(desktop(end), 600)

palette_source = Image.new("RGB", (GW, GH * 4))
for k, idx in enumerate((0, 8, len(frames) - 6, 14)):
    palette_source.paste(frames[idx], (0, GH * k))
palette = palette_source.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
q = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
path = OUT / "demo.gif"
q[0].save(path, save_all=True, append_images=q[1:], duration=durations, loop=0, optimize=True, disposal=1)
print(path, len(frames), "frames", sum(durations) / 1000, "s", path.stat().st_size // 1024, "KB")
# a contact sheet and a few frames to look at, next to the raw renders
sheet = Image.new("RGB", (GW // 2 * 4, GH // 2 * ((len(frames) + 3) // 4)), (255, 255, 255))
for i, f in enumerate(frames):
    sheet.paste(f.resize((GW // 2, GH // 2)), ((i % 4) * GW // 2, (i // 4) * GH // 2))
sheet.save(RAW / "demo-sheet.png")
for i in (7, 20, 33, len(frames) - 3):
    frames[i].save(RAW / f"demo-frame-{i}.png")
